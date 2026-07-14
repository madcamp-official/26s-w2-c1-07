using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem; // 새 Input System
using RouletteParty.Map; // ClimbMapGenerator (설치 볼륨 검증)
using RouletteParty.UI;  // ImguiScale (OnGUI 해상도 스케일링)

namespace RouletteParty.Match
{
    /// <summary>
    /// PREP 구조물 설치 클라 UI. 씬의 단일 GameObject 에 부착(호스트/클라 공용).
    ///
    /// PREP 동안 플레이어는 자유 비행 카메라(PlayerController)로 맵을 날아다니고, 이 컴포넌트가:
    ///  - 종류 선택: 1(벽)/2(원기둥) = 보이는 구조물, 3 = 보이지 않는 구조물. R = 90도 회전.
    ///  - 설치 모드(Q 토글):
    ///      표면 모드(기본) = 화면 중앙 조준점 레이캐스트 히트점. 설치된 구조물을 조준하면
    ///                        그 구조물 AABB 의 윗면으로 자동 스냅(구조물 위에 쌓기).
    ///      공중 모드       = 카메라 정면 거리 d 지점(지면 불필요). 휠 위/아래로 d 조절.
    ///                        전환 순간 d 를 현재 조준점 거리로 초기화 -> 프리뷰가 튀지 않음.
    ///  - 설치: 좌클릭 -> PlaceStructureServerRpc.
    ///  - 블루프린트(배치 프리뷰): 실제 구조물 프리팹(MatchManager.PrefabFor)을 인스턴스화해
    ///    물리/네트워크/로직을 제거하고 렌더러만 남긴 뒤 반투명 재질로 교체 -> 실물과 1:1 일치.
    ///    새 구조물이 추가돼도 프리팹만 연결하면 블루프린트는 자동으로 따라온다.
    ///    설치 가능(허용 범위 안, 시작 섬 영역 제외) = 초록, 불가 = 빨강. 잔여 0 이면 숨김.
    ///    겹침 판정은 "나에게 보이는 설치물"만 검사 — 타인의 투명 구조물(함정)은 프리뷰를
    ///    빨갛게 만들지 않는다(가져다 대는 것만으로 함정이 드러나면 안 됨). 실제로 그 자리에
    ///    설치를 시도하면 서버가 거부하고 "함정" 경고 문구를 이 클라에만 보낸다(잔여 소모 없음).
    ///  - 잔여 개수: 라운드 지급량 - 이번 PREP 에 설치한 수(이월 없음). 스폰된 내 구조물 수의
    ///    PREP 시작 스냅샷 대비 증가분으로 계산(서버 확정 결과와 일치).
    /// </summary>
    [DisallowMultipleComponent]
    public class PrepClientUI : MonoBehaviour
    {
        [Tooltip("설치 레이캐스트 최대 거리.")]
        [SerializeField] private float _rayDistance = 200f;
        [Tooltip("프리뷰 색(설치 가능).")]
        [SerializeField] private Color _okColor = new Color(0.3f, 1f, 0.4f, 0.45f);
        [Tooltip("프리뷰 색(설치 불가).")]
        [SerializeField] private Color _badColor = new Color(1f, 0.3f, 0.3f, 0.45f);

        [Header("공중 설치 모드 (Q 토글, 휠로 거리 조절)")]
        [Tooltip("공중 설치 거리 최소(m).")]
        [SerializeField] private float _airDistanceMin = 2f;
        [Tooltip("공중 설치 거리 최대(m).")]
        [SerializeField] private float _airDistanceMax = 30f;
        [Tooltip("휠 1노치당 거리 변화(m). 위 = 멀어짐, 아래 = 다가옴.")]
        [SerializeField] private float _airDistanceStep = 1f;

        [Tooltip("함정 겹침 거부 시 경고 문구 표시 시간(초).")]
        [SerializeField] private float _trapToastDuration = 2.5f;

        // 보이는 구조물 형태 풀: PREP 시작 때 이번 라운드 지급량 전체를 미리 굴린다(배치 큐).
        private static readonly StructureType[] VisiblePool =
            { StructureType.Wall, StructureType.Cylinder, StructureType.Tree, StructureType.Rock };

        // 배치 큐: 이번 PREP 에 깔 수 있는 구조물 전부(보이는 N개 + 투명 M개)를 시각화하고
        // Alt 로 다음에 놓을 것을 토글한다. 설치가 서버에서 확정될 때마다 큐에서 소모.
        private readonly List<StructureType> _queue = new List<StructureType>();
        private int _queueIndex;
        private int _lastVisMine = -1, _lastInvisMine = -1; // 설치 확정 감지(증가 시 큐 소모)

        // 구조물 실물 썸네일(런타임 렌더 캐시). 종류당 1회 생성, RT 는 파괴 시 해제.
        private readonly Dictionary<StructureType, RenderTexture> _thumbs =
            new Dictionary<StructureType, RenderTexture>();

        private int _yawStep;   // R: Y축 90도
        private int _pitchStep; // T: X축 90도
        private int _rollStep;  // G: Z축 90도
        private bool _airMode;            // false = 표면(조준점) 모드, true = 공중(거리 d) 모드
        private float _airDistance = 6f;  // 공중 모드 설치 거리(전환 순간 조준점 거리로 재설정)
        private float _trapToastUntil;    // 이 시각(unscaledTime)까지 함정 경고 표시

        // PREP 시작 시 내 구조물 수 스냅샷(잔여 계산용)
        private bool _inPrep;
        private int _baseVisible, _baseInvisible;

        // 블루프린트(배치 프리뷰): 종류별로 실제 프리팹에서 1회 생성해 캐시.
        // 바닥 오프셋은 캐시하지 않는다 — 3축 회전마다 렌더 바운즈가 달라지므로 매 프레임 계산.
        private readonly Dictionary<StructureType, GameObject> _blueprints =
            new Dictionary<StructureType, GameObject>();
        private GameObject _activeBlueprint;
        private Material _previewMat;

        private readonly Rect _panelRect = new Rect(10, 290, 400, 330);
        private GUIStyle _rich;
        private GUIStyle _toastStyle;

        private bool Active =>
            MatchManager.Instance != null &&
            MatchManager.Instance.IsSpawned &&
            MatchManager.Instance.CurrentPhase == MatchPhase.Prep;

        // 함정 겹침 거부 통지(서버 -> 나에게만 오는 RPC) 구독. 문구 표시는 OnGUI.
        private void OnEnable()  { MatchManager.PlacementDeniedByTrap += OnTrapDenied; }
        private void OnDisable() { MatchManager.PlacementDeniedByTrap -= OnTrapDenied; }
        private void OnTrapDenied() { _trapToastUntil = Time.unscaledTime + _trapToastDuration; }

        private void Update()
        {
            bool active = Active;

            // PREP 진입/이탈 감지: 스냅샷 갱신 + 프리뷰 정리.
            if (active && !_inPrep)
            {
                _inPrep = true;
                _airMode = false; // 매 PREP 은 기본(표면) 모드로 시작
                _yawStep = _pitchStep = _rollStep = 0;
                CountMine(out _baseVisible, out _baseInvisible);
                _lastVisMine = _baseVisible;
                _lastInvisMine = _baseInvisible;
                BuildQueue();                         // 이번 라운드 지급량 전체 미리 굴림
                StartCoroutine(BuildThumbsRoutine()); // 실물 썸네일 렌더(1프레임)
            }
            else if (!active && _inPrep)
            {
                _inPrep = false;
                HideBlueprint();
            }
            if (!active) return;

            var pc = LocalPlayer();
            if (pc == null) { HideBlueprint(); return; }

            // 설치 확정 감지(서버 확정 = 내 구조물 수 증가) -> 배치 큐에서 소모.
            CountMine(out int visNow, out int invisNow);
            if (_lastVisMine >= 0 && visNow > _lastVisMine) ConsumeFromQueue(false);
            if (_lastInvisMine >= 0 && invisNow > _lastInvisMine) ConsumeFromQueue(true);
            _lastVisMine = visNow;
            _lastInvisMine = invisNow;

            var kb = Keyboard.current;
            if (kb != null)
            {
                // [Alt] 배치 순서 토글, [1] 다음 보이는 것 / [2] 다음 투명으로 점프.
                if ((kb.leftAltKey.wasPressedThisFrame || kb.rightAltKey.wasPressedThisFrame) && _queue.Count > 0)
                    _queueIndex = (_queueIndex + 1) % _queue.Count;
                if (kb.digit1Key.wasPressedThisFrame) SelectFirstOfKind(false);
                else if (kb.digit2Key.wasPressedThisFrame || kb.digit3Key.wasPressedThisFrame)
                    SelectFirstOfKind(true);
                // 회전: R = Y축, T = X축, G = Z축 (각 90도 스텝).
                if (kb.rKey.wasPressedThisFrame) _yawStep = (_yawStep + 1) & 3;
                if (kb.tKey.wasPressedThisFrame) _pitchStep = (_pitchStep + 1) & 3;
                if (kb.gKey.wasPressedThisFrame) _rollStep = (_rollStep + 1) & 3;

                // 설치 모드 토글. 공중 모드 진입 시 거리를 "현재 조준점까지"로 초기화해
                // 블루프린트가 그 자리에서 이어지게 한다(전환 순간 튀지 않음).
                if (kb.qKey.wasPressedThisFrame)
                {
                    _airMode = !_airMode;
                    if (_airMode)
                        _airDistance = Mathf.Clamp(
                            Vector3.Distance(pc.AimRay.origin, pc.AimPoint),
                            _airDistanceMin, _airDistanceMax);
                }
            }

            // 설치점 계산.
            Vector3 point;
            if (_airMode)
            {
                // 공중 모드: 카메라 정면 거리 d 지점. 휠 위 = 멀어짐, 아래 = 다가옴.
                var wheel = Mouse.current;
                if (wheel != null)
                {
                    float sy = wheel.scroll.ReadValue().y;
                    if (sy > 0.01f) _airDistance += _airDistanceStep;
                    else if (sy < -0.01f) _airDistance -= _airDistanceStep;
                    _airDistance = Mathf.Clamp(_airDistance, _airDistanceMin, _airDistanceMax);
                }
                point = pc.AimRay.origin + pc.AimRay.direction * _airDistance;
            }
            else
            {
                // 표면 모드: 조준점 = 로컬 PlayerController 의 AimPoint(자기 몸 콜라이더 제외 처리 완료).
                point = pc.AimPoint;

                // 설치된 구조물을 조준하면 그 윗면으로 스냅 -> 옆면을 가져다 대도 위에 올라간다
                // (구조물을 발판처럼 쌓는 플레이). 서버는 받은 좌표를 그대로 검증하므로 추가 배선 불필요.
                var col = pc.AimHitCollider;
                var st = col != null ? col.GetComponentInParent<Structure>() : null;
                if (st != null && st.IsSpawned)
                    point.y = st.WorldBounds.max.y;
            }

            // 큐가 비면(전부 설치) 블루프린트 표시/설치 클릭 차단.
            if (_queue.Count == 0) { HideBlueprint(); return; }
            _queueIndex = Mathf.Clamp(_queueIndex, 0, _queue.Count - 1);
            StructureType selected = _queue[_queueIndex];
            bool invisible = selected == StructureType.Invisible;
            int remaining = invisible ? RemainingInvisible() : RemainingVisible();
            if (remaining <= 0) { HideBlueprint(); return; } // 서버 잔여와 이중 안전망

            var bp = GetBlueprint(selected);

            // 블루프린트를 먼저 최종 트랜스폼에 놓은 뒤(회전 반영된 렌더 AABB 가 필요),
            // 설치 허용 범위·시작 섬 금지 + "나에게 보이는" 설치물과의 겹침으로 판정.
            // 초록이어도 숨겨진 함정과 겹치면 서버가 거부한다(OverlapsVisiblePlaced 주석 참조).
            // 회전(3축)을 먼저 적용하고, 회전된 바운즈로 바닥 오프셋을 계산해 조준점에 얹는다
            // (서버 스폰과 동일 계산 -> 프리뷰 = 실물).
            var rot = Quaternion.Euler(_pitchStep * 90f, _yawStep * 90f, _rollStep * 90f);
            ShowBlueprint(bp, point, rot);
            var gen = ClimbMapGenerator.Instance;
            bool ok = bp != null && gen != null && gen.IsPlacementAllowed(point, 0.1f) && !OverlapsVisiblePlaced(bp);
            TintBlueprint(ok);

            var mouse = Mouse.current;
            if (ok && mouse != null && mouse.leftButton.wasPressedThisFrame &&
                Cursor.lockState == CursorLockMode.Locked)
            {
                MatchManager.Instance.PlaceStructureServerRpc(point, (byte)_yawStep, (byte)_pitchStep,
                    (byte)_rollStep, (byte)selected);
            }
        }

        // ============================ 배치 큐 ============================
        // PREP 시작 시 이번 라운드 지급량 전체를 미리 굴린다: 보이는 것은 형태 랜덤, 투명은 그대로.
        private void BuildQueue()
        {
            _queue.Clear();
            _queueIndex = 0;
            var mm = MatchManager.Instance;
            if (mm == null) return;
            for (int i = 0; i < mm.VisibleGrant; i++)
                _queue.Add(VisiblePool[Random.Range(0, VisiblePool.Length)]);
            for (int i = 0; i < mm.InvisibleGrant; i++)
                _queue.Add(StructureType.Invisible);
        }

        // 설치 확정 시 큐 소모: 선택 항목이 그 종류(보이는/투명)면 선택 항목을, 아니면 같은 종류의 첫 항목을.
        private void ConsumeFromQueue(bool invisible)
        {
            int idx = -1;
            if (_queueIndex < _queue.Count &&
                (_queue[_queueIndex] == StructureType.Invisible) == invisible)
                idx = _queueIndex;
            else
                for (int i = 0; i < _queue.Count; i++)
                    if ((_queue[i] == StructureType.Invisible) == invisible) { idx = i; break; }
            if (idx >= 0) _queue.RemoveAt(idx);
            if (_queueIndex >= _queue.Count) _queueIndex = Mathf.Max(0, _queue.Count - 1);
        }

        private void SelectFirstOfKind(bool invisible)
        {
            for (int i = 0; i < _queue.Count; i++)
                if ((_queue[i] == StructureType.Invisible) == invisible) { _queueIndex = i; return; }
        }

        private RouletteParty.Net.PlayerController LocalPlayer()
        {
            var nm = NetworkManager.Singleton;
            var po = (nm != null && nm.LocalClient != null) ? nm.LocalClient.PlayerObject : null;
            return po != null ? po.GetComponent<RouletteParty.Net.PlayerController>() : null;
        }

        // ============================ 잔여 개수 ============================
        private int RemainingVisible()
        {
            CountMine(out int vis, out _);
            return Mathf.Max(0, MatchManager.Instance.VisibleGrant - (vis - _baseVisible));
        }

        private int RemainingInvisible()
        {
            CountMine(out _, out int invis);
            return Mathf.Max(0, MatchManager.Instance.InvisibleGrant - (invis - _baseInvisible));
        }

        // 스폰된 내 구조물 수(서버 확정 결과 반영: 거부된 요청은 안 셈).
        // Structure.Active 레지스트리 순회(스폰/디스폰 시 갱신) — 매 프레임 씬 전체 스캔 없음.
        private void CountMine(out int visible, out int invisible)
        {
            visible = 0; invisible = 0;
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            ulong me = nm.LocalClientId;

            var all = Structure.Active;
            for (int i = 0; i < all.Count; i++)
            {
                var ob = all[i];
                if (ob == null || !ob.IsSpawned || ob.OwnerId.Value != me) continue;
                if (ob.IsInvisibleKind) invisible++;
                else visible++;
            }
        }

        // ============================ 블루프린트(배치 프리뷰) ============================
        // 실제 구조물 프리팹을 인스턴스화해 렌더러만 남긴 로컬 전용 복제본.
        // 종류별 1회 생성 후 캐시 -> 새 구조물은 프리팹(+ StructureType)만 추가하면 자동 지원.
        private GameObject GetBlueprint(StructureType type)
        {
            if (_blueprints.TryGetValue(type, out var cached) && cached != null) return cached;

            var mm = MatchManager.Instance;
            var prefab = mm != null ? mm.PrefabFor(type) : null;
            if (prefab == null) return null;

            if (_previewMat == null)
            {
                // Sprites/Default: 알파 지원 + 항상 포함 셰이더 -> 런타임 반투명 프리뷰에 안전.
                _previewMat = new Material(Shader.Find("Sprites/Default"));
            }

            var bp = Instantiate(prefab);
            bp.name = "Blueprint_" + type;
            StripForBlueprint(bp);
            ApplyBlueprintMaterial(bp);
            bp.SetActive(false);
            _blueprints[type] = bp;
            return bp;
        }

        // 실물처럼 동작하면 안 되는 것 전부 제거: 물리(충돌/강체), 네트워크, 게임 로직.
        // 렌더러(+메시)만 남긴다. NetworkBehaviour 를 NetworkObject 보다 먼저 제거해야 안전.
        private static void StripForBlueprint(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);
            foreach (var nb in go.GetComponentsInChildren<NetworkBehaviour>(true)) Destroy(nb);
            foreach (var no in go.GetComponentsInChildren<NetworkObject>(true)) Destroy(no);
        }

        // 모든 렌더러를 공유 프리뷰 재질로 교체(색은 ShowBlueprint 가 초록/빨강으로 갱신).
        // Invisible 프리팹도 렌더러를 강제로 켠다(실물은 Structure 가 숨기지만 프리뷰는 보여야 함).
        private void ApplyBlueprintMaterial(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = true;
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = _previewMat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        // 서버 겹침 검증(Structure.OverlapScan)의 부분 거울: "나에게 보이는" 설치물만 검사.
        // 타인의 투명 구조물(함정)은 여기서 의도적으로 무시한다 — 프리뷰가 빨개지면 가져다
        // 대는 것만으로 함정 위치가 드러나기 때문. 그 겹침은 설치 시도 시 서버가 거부 + 통지.
        // (따라서 이 프리뷰는 "초록이면 반드시 설치"가 아니라 "초록 = 내가 아는 한 설치 가능".)
        private bool OverlapsVisiblePlaced(GameObject bp)
        {
            var mm = MatchManager.Instance;
            var nm = NetworkManager.Singleton;
            float tol = mm != null ? mm.OverlapTolerance : 0f;
            ulong me = nm != null ? nm.LocalClientId : 0UL;
            Structure.OverlapScan(Structure.RenderBounds(bp), tol, me, out bool visibleHit, out _);
            return visibleHit;
        }

        private void ShowBlueprint(GameObject bp, Vector3 point, Quaternion rot)
        {
            if (bp == null) { HideBlueprint(); return; }

            if (_activeBlueprint != null && _activeBlueprint != bp)
                _activeBlueprint.SetActive(false); // 종류 전환 시 이전 것 숨김
            _activeBlueprint = bp;

            bp.SetActive(true);
            // 서버 스폰(PlaceStructureServerRpc)과 동일한 계산: 회전 먼저 -> 회전된 바운즈로
            // 바닥 오프셋 -> 조준점에 얹기. 실물과 1:1 일치.
            bp.transform.rotation = rot;
            bp.transform.position = point + Vector3.up * Structure.BottomOffset(bp.gameObject);
        }

        private void TintBlueprint(bool ok)
        {
            if (_previewMat != null) _previewMat.color = ok ? _okColor : _badColor;
        }

        private void HideBlueprint()
        {
            if (_activeBlueprint != null) _activeBlueprint.SetActive(false);
            _activeBlueprint = null;
        }

        private void OnDestroy()
        {
            foreach (var kv in _blueprints)
                if (kv.Value != null) Destroy(kv.Value);
            _blueprints.Clear();
            if (_previewMat != null) Destroy(_previewMat);
            foreach (var kv in _thumbs)
                if (kv.Value != null) { kv.Value.Release(); Destroy(kv.Value); }
            _thumbs.Clear();
        }

        // 배치 큐 시각화: 실물 썸네일 한 줄, 선택 항목은 노란 테두리 + 불투명 강조.
        private void DrawQueue()
        {
            const float size = 52f;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < _queue.Count; i++)
            {
                var t = _queue[i];
                Rect r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
                bool selHere = i == Mathf.Clamp(_queueIndex, 0, _queue.Count - 1);

                if (selHere)
                {
                    GUI.color = new Color(1f, 0.9f, 0.2f);
                    GUI.DrawTexture(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), Texture2D.whiteTexture);
                }
                GUI.color = selHere ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                var tex = ThumbFor(t);
                if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
                else GUI.Box(r, "?");
                if (t == StructureType.Invisible)
                {
                    GUI.color = selHere ? new Color(0.7f, 0.9f, 1f) : new Color(0.7f, 0.9f, 1f, 0.7f);
                    GUI.Label(new Rect(r.x, r.yMax - 20f, r.width, 20f), "투명", _rich);
                }
                GUI.color = Color.white;
                GUILayout.Space(6);
            }
            GUILayout.EndHorizontal();
        }

        // ============================ OnGUI (정보/안내) ============================
        private void OnGUI()
        {
            if (!Active) return;
            if (_rich == null) _rich = new GUIStyle(GUI.skin.label) { richText = true };

            ImguiScale.Apply(); // 이하 좌표는 1080p 기준 가상 픽셀
            GUILayout.BeginArea(_panelRect, GUI.skin.box);
            GUILayout.Label("<b>준비</b> — 구조물 설치 (자유 비행)", _rich);
            GUILayout.Label("비행: WASD 수평 + 마우스 시선, Space 상승 / Shift 하강", _rich);

            GUILayout.Space(4);
            GUILayout.Label("배치 큐  <b>[Alt]</b> 순서 토글   <b>[1]</b> 보이는 <b>[2]</b> 투명 점프", _rich);
            DrawQueue();
            StructureType? sel = _queue.Count > 0
                ? _queue[Mathf.Clamp(_queueIndex, 0, _queue.Count - 1)] : (StructureType?)null;
            GUILayout.Label(sel.HasValue
                ? $"선택: <b>{TypeKorean(sel.Value)}</b>"
                : "<b>이번 라운드 구조물을 전부 설치했습니다</b>", _rich);
            GUILayout.Label($"회전  <b>[R]</b>Y {_yawStep * 90}°  <b>[T]</b>X {_pitchStep * 90}°  <b>[G]</b>Z {_rollStep * 90}°", _rich);
            GUILayout.Label(_airMode
                ? $"모드 <b>[Q]</b>: <b>공중</b> (거리 {_airDistance:0.#}m, 휠로 조절)"
                : "모드 <b>[Q]</b>: <b>표면</b> (구조물 조준 시 그 위로 스냅)", _rich);

            GUILayout.Space(4);
            GUILayout.Label($"남은 개수  보이는 <b>{RemainingVisible()}</b>/{MatchManager.Instance.VisibleGrant}" +
                            $"   안 보이는 <b>{RemainingInvisible()}</b>/{MatchManager.Instance.InvisibleGrant}", _rich);
            GUILayout.Label("<b>좌클릭</b>으로 설치 (설치물은 3라운드 누적)", _rich);
            GUILayout.EndArea();

            // 함정 겹침 거부 경고(서버 통지). 화면 중앙 위쪽에 잠깐 표시.
            if (Time.unscaledTime < _trapToastUntil)
            {
                if (_toastStyle == null)
                    _toastStyle = new GUIStyle(GUI.skin.box)
                    {
                        fontSize = 28, fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = new Color(1f, 0.45f, 0.35f) }
                    };
                GUI.Box(new Rect(960 - 240, 340, 480, 52), "이 근처에 함정이 있습니다!", _toastStyle);
            }
        }

        // ============================ 썸네일(실물 미리보기) ============================
        // 실제 구조물 프리팹을 화면 밖(-800)에 잠깐 세워 전용 카메라 -> RenderTexture 로 1프레임
        // 렌더한다(URP 는 활성 카메라만 그리므로 코루틴으로 한 프레임 대기 후 정리, RT 는 유지).
        private System.Collections.IEnumerator BuildThumbsRoutine()
        {
            var mm = MatchManager.Instance;
            if (mm == null) yield break;

            var pending = new List<(StructureType type, GameObject model, Camera cam)>();
            var kinds = new HashSet<StructureType>(_queue);
            int slot = 0;
            foreach (var t in kinds)
            {
                if (_thumbs.ContainsKey(t) && _thumbs[t] != null) continue;
                var prefab = mm.PrefabFor(t);
                if (prefab == null) continue;

                var model = Instantiate(prefab, new Vector3(slot * 40f, -800f, 0f), Quaternion.identity);
                StripForBlueprint(model);
                foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.enabled = true; // 투명도 표시

                Bounds b = Structure.RenderBounds(model);
                var camGo = new GameObject("ThumbCam_" + t);
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = Mathf.Max(b.extents.x, b.extents.y, b.extents.z) * 1.35f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 200f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.13f, 0.13f, 0.18f, 1f);
                cam.depth = -50f; // 메인 카메라를 방해하지 않게
                Vector3 dir = new Vector3(1f, 0.7f, -1f).normalized;
                cam.transform.position = b.center + dir * (b.extents.magnitude + 3f);
                cam.transform.rotation = Quaternion.LookRotation(b.center - cam.transform.position);

                var rt = new RenderTexture(128, 128, 16);
                cam.targetTexture = rt;
                _thumbs[t] = rt;
                pending.Add((t, model, cam));
                slot++;
            }

            if (pending.Count > 0)
            {
                yield return null; // 파이프라인이 카메라들을 한 번 그린다
                yield return new WaitForEndOfFrame();
                foreach (var p in pending)
                {
                    if (p.cam != null) { p.cam.targetTexture = null; Destroy(p.cam.gameObject); }
                    if (p.model != null) Destroy(p.model);
                }
            }
        }

        private Texture ThumbFor(StructureType t) =>
            _thumbs.TryGetValue(t, out var rt) && rt != null ? rt : null;

        private static string TypeKorean(StructureType t)
        {
            switch (t)
            {
                case StructureType.Wall:      return "벽 (보이는)";
                case StructureType.Cylinder:  return "원기둥 (보이는)";
                case StructureType.Invisible: return "투명 (안 보이는)";
                case StructureType.Tree:      return "나무 (보이는)";
                case StructureType.Rock:      return "바위 (보이는)";
                default:                      return "-";
            }
        }
    }
}
