using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem; // 새 Input System

namespace RouletteParty.Match
{
    /// <summary>
    /// PREP 구조물 설치 클라 UI. 씬의 단일 GameObject 에 부착(호스트/클라 공용).
    ///
    /// PREP 동안 플레이어는 자유 비행 카메라(PlayerController)로 맵을 날아다니고, 이 컴포넌트가:
    ///  - 종류 선택: 1(벽)/2(원기둥) = 보이는 구조물, 3 = 보이지 않는 구조물. R = 90도 회전.
    ///  - 설치: 화면 중앙 조준점 레이캐스트 히트점에 좌클릭 -> PlaceStructureServerRpc.
    ///  - 프리뷰: 조준점 위치에 반투명 고스트(볼륨 안 + 잔여 개수 있으면 초록, 아니면 빨강).
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

        private ObstacleType _selected = ObstacleType.Wall;
        private int _yawStep;

        // PREP 시작 시 내 구조물 수 스냅샷(잔여 계산용)
        private bool _inPrep;
        private int _baseVisible, _baseInvisible;

        // 프리뷰
        private GameObject _previewBox, _previewCyl;
        private Material _previewMat;

        private readonly Rect _panelRect = new Rect(10, 290, 300, 210);
        private GUIStyle _rich;

        private bool Active =>
            MatchManager.Instance != null &&
            MatchManager.Instance.IsSpawned &&
            MatchManager.Instance.CurrentPhase == MatchPhase.Prep;

        private void Update()
        {
            bool active = Active;

            // PREP 진입/이탈 감지: 스냅샷 갱신 + 프리뷰 정리.
            if (active && !_inPrep)
            {
                _inPrep = true;
                CountMine(out _baseVisible, out _baseInvisible);
            }
            else if (!active && _inPrep)
            {
                _inPrep = false;
                HidePreview();
            }
            if (!active) return;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) _selected = ObstacleType.Wall;
                else if (kb.digit2Key.wasPressedThisFrame) _selected = ObstacleType.Cylinder;
                else if (kb.digit3Key.wasPressedThisFrame) _selected = ObstacleType.Ghost;
                if (kb.rKey.wasPressedThisFrame) _yawStep = (_yawStep + 1) & 3;
            }

            // 조준점 = 로컬 PlayerController 의 AimPoint(자기 몸 콜라이더 제외 처리 완료).
            var pc = LocalPlayer();
            if (pc == null) { HidePreview(); return; }
            Vector3 point = pc.AimPoint;

            bool invisible = _selected == ObstacleType.Ghost;
            int remaining = invisible ? RemainingInvisible() : RemainingVisible();
            var gen = ClimbMapGenerator.Instance;
            bool ok = remaining > 0 && gen != null && gen.InsideVolume(point, 0.1f);

            ShowPreview(point, ok);

            var mouse = Mouse.current;
            if (ok && mouse != null && mouse.leftButton.wasPressedThisFrame &&
                Cursor.lockState == CursorLockMode.Locked)
            {
                MatchManager.Instance.PlaceStructureServerRpc(point, (byte)_yawStep, (byte)_selected);
            }
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
        private void CountMine(out int visible, out int invisible)
        {
            visible = 0; invisible = 0;
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            ulong me = nm.LocalClientId;

            var all = Object.FindObjectsByType<Obstacle>();
            foreach (var ob in all)
            {
                if (!ob.IsSpawned || ob.OwnerId.Value != me) continue;
                if (ob.IsInvisibleKind) invisible++;
                else visible++;
            }
        }

        // ============================ 프리뷰 ============================
        private void EnsurePreview()
        {
            if (_previewMat == null)
            {
                // Sprites/Default: 알파 지원 + 항상 포함 셰이더 -> 런타임 반투명 프리뷰에 안전.
                _previewMat = new Material(Shader.Find("Sprites/Default"));
            }
            if (_previewBox == null)
            {
                _previewBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _previewBox.name = "PlacePreview_Box";
                Destroy(_previewBox.GetComponent<Collider>());
                _previewBox.GetComponent<MeshRenderer>().sharedMaterial = _previewMat;
                _previewBox.SetActive(false);
            }
            if (_previewCyl == null)
            {
                _previewCyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                _previewCyl.name = "PlacePreview_Cyl";
                Destroy(_previewCyl.GetComponent<Collider>());
                _previewCyl.GetComponent<MeshRenderer>().sharedMaterial = _previewMat;
                _previewCyl.SetActive(false);
            }
        }

        private void ShowPreview(Vector3 basePos, bool ok)
        {
            EnsurePreview();
            _previewMat.color = ok ? _okColor : _badColor;

            bool cyl = _selected == ObstacleType.Cylinder;
            _previewBox.SetActive(!cyl);
            _previewCyl.SetActive(cyl);

            // 프리팹 실루엣 근사: 벽/투명벽 = (3,2,0.5) 박스(피벗 바닥), 원기둥 = 지름 1.6 높이 2.
            var rot = Quaternion.Euler(0f, _yawStep * 90f, 0f);
            if (cyl)
            {
                _previewCyl.transform.localScale = new Vector3(1.6f, 1f, 1.6f);
                _previewCyl.transform.SetPositionAndRotation(basePos + Vector3.up * 1f, rot);
            }
            else
            {
                _previewBox.transform.localScale = new Vector3(3f, 2f, 0.5f);
                _previewBox.transform.SetPositionAndRotation(basePos + Vector3.up * 1f, rot);
            }
        }

        private void HidePreview()
        {
            if (_previewBox != null) _previewBox.SetActive(false);
            if (_previewCyl != null) _previewCyl.SetActive(false);
        }

        // ============================ OnGUI (정보/안내) ============================
        private void OnGUI()
        {
            if (!Active) return;
            if (_rich == null) _rich = new GUIStyle(GUI.skin.label) { richText = true };

            GUILayout.BeginArea(_panelRect, GUI.skin.box);
            GUILayout.Label("<b>준비</b> — 구조물 설치 (자유 비행)", _rich);
            GUILayout.Label("비행: WASD + 마우스, Space 상승 / Ctrl 하강", _rich);

            GUILayout.Space(4);
            GUILayout.Label($"종류  <b>[1]</b> 벽  <b>[2]</b> 원기둥  <b>[3]</b> 투명(안 보임)", _rich);
            GUILayout.Label($"선택: <b>{TypeKorean(_selected)}</b>   회전 <b>[R]</b> ({_yawStep * 90}°)", _rich);

            GUILayout.Space(4);
            GUILayout.Label($"남은 개수  보이는 <b>{RemainingVisible()}</b>/{MatchManager.Instance.VisibleGrant}" +
                            $"   안 보이는 <b>{RemainingInvisible()}</b>/{MatchManager.Instance.InvisibleGrant}", _rich);
            GUILayout.Label("조준점에 <b>좌클릭</b>으로 설치 (설치물은 3라운드 누적)", _rich);
            GUILayout.EndArea();
        }

        private static string TypeKorean(ObstacleType t)
        {
            switch (t)
            {
                case ObstacleType.Wall:     return "벽 (보이는)";
                case ObstacleType.Cylinder: return "원기둥 (보이는)";
                case ObstacleType.Ghost:    return "투명 (안 보이는)";
                default:                    return "-";
            }
        }
    }
}
