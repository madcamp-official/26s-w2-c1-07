using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;
using RouletteParty.Audio; // AudioManager (설치/발동 사운드)

namespace RouletteParty.Match
{
    /// <summary>
    /// 구조물 종류. Wall/Cylinder = 보이는 구조물, Invisible = 보이지 않는 구조물.
    /// byte 백킹 -> RPC/NetworkVariable 직렬화 그대로 사용(값 변경 금지: 와이어 포맷).
    /// </summary>
    public enum StructureType : byte { Wall = 0, Cylinder = 1, Invisible = 2 }

    /// <summary>
    /// 구조물(플레이어가 설치하는 Object). 클라이밍 전환 명세 6절의 가시성 규칙 구현.
    ///
    ///  - 콜라이더는 모든 클라에서 항상 켜져 있다(물리 공정성, 기존 규약 유지). 렌더러만 분기.
    ///  - 보이는 구조물(Wall/Cylinder): 생성 후 어떤 페이즈에서도 전원에게 보임(분기 없음).
    ///  - 보이지 않는 구조물(Invisible):
    ///      PREP + 설치자 본인      -> 보임(반투명 머티리얼이 있으면 반투명)
    ///      그 외 페이즈/타인       -> 안 보임(설치자 본인 포함)
    ///      플레이어 충돌(상호작용) -> 서버가 RevealUntil 기록, 그 시각까지 전원에게 보임.
    ///      공개/숨김 전환은 알파 페이드(_fadeInDuration/_fadeOutDuration)로 부드럽게 진행.
    ///  - Type/OwnerId 는 서버가 Spawn() 이전에 세팅(ServerInit)해 초기 동기화에 싣는다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class Structure : NetworkBehaviour
    {
        // 스폰 중인 전체 구조물 레지스트리. 매 프레임 FindObjectsByType 스캔을 피하기 위한
        // 표준 패턴(스폰/디스폰 시점에만 갱신). 잔여 개수 계산(PrepClientUI) 등이 순회한다.
        private static readonly List<Structure> s_active = new List<Structure>();
        public static IReadOnlyList<Structure> Active => s_active;

        [Header("INVISIBLE(보이지 않는 구조물) 렌더 제어 (Wall/Cylinder 프리팹은 비워도 됨)")]
        [Tooltip("가시성 분기 대상 렌더러들. 보이지 않는 구조물 프리팹에서만 필요.")]
        [SerializeField] private Renderer[] _renderers;

        [Tooltip("선택: PREP 중 설치자에게 보여줄 반투명 머티리얼. 비우면 원본 머티리얼로 보인다.")]
        [FormerlySerializedAs("_ghostOwnerMaterial")]
        [SerializeField] private Material _ownerPreviewMaterial;

        [Tooltip("플레이어 상호작용(충돌) 시 전원에게 공개되는 시간(초). 연속 충돌은 연장된다.")]
        [SerializeField] private float _revealDuration = 2f;
        public float RevealDuration => _revealDuration;

        [Tooltip("공개 시 알파 페이드 인 시간(초). 0 = 즉시.")]
        [SerializeField] private float _fadeInDuration = 0.15f;
        [Tooltip("공개 종료 시 알파 페이드 아웃 시간(초). 0 = 즉시.")]
        [SerializeField] private float _fadeOutDuration = 0.6f;

        // 기본 권한 { Read: Everyone, Write: Server }.
        public NetworkVariable<byte>  Type    = new NetworkVariable<byte>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<ulong> OwnerId = new NetworkVariable<ulong>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        // 이 서버 시각까지 전원에게 공개(보이지 않는 구조물 전용). 서버만 write.
        public NetworkVariable<double> RevealUntil = new NetworkVariable<double>(
            0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public StructureType Kind => (StructureType)Type.Value;
        public bool IsInvisibleKind => Kind == StructureType.Invisible;

        // 원본 머티리얼 캐시(반투명 <-> 원본 전환용)
        Material[] _originalMats;
        // 콜라이더 캐시(WorldBounds 용. 콜라이더는 규약상 모든 클라에서 항상 켜져 있다)
        Collider[] _colliders;
        int _lastState = -1; // 0=숨김, 1=반투명(설치자 PREP), 2=원본(공개/보이는 구조물)

        // ---- 공개 페이드(보이지 않는 구조물 전용) ----
        // 원본은 불투명 머티리얼이므로 페이드 중에만 투명 블렌딩으로 전환한 복제본을 쓰고,
        // 알파 1 도달 시 원본(불투명)으로 복귀한다(투명 큐 상주 방지 — 정렬/오버드로우 표준 관행).
        float _alpha;                 // 현재 표시 알파(0 = 완전 숨김, 1 = 완전 공개)
        Material[] _fadeMats;         // 투명 전환된 원본 복제본(지연 생성)
        Color[] _origColors;
        int _appliedState = -1;       // 마지막으로 렌더러에 반영한 상태/알파(중복 세팅 방지)
        float _appliedAlpha = -1f;

        /// <summary>이 구조물의 월드 AABB. 렌더러는 가시성 분기로 꺼질 수 있으므로
        /// 항상 켜져 있는 콜라이더 기준 -> 서버/모든 클라에서 동일한 값이 나온다.</summary>
        public Bounds WorldBounds
        {
            get
            {
                if (_colliders == null || _colliders.Length == 0)
                    _colliders = GetComponentsInChildren<Collider>(true);
                if (_colliders.Length == 0) return new Bounds(transform.position, Vector3.zero);

                Bounds b = _colliders[0].bounds;
                for (int i = 1; i < _colliders.Length; i++)
                    b.Encapsulate(_colliders[i].bounds);
                return b;
            }
        }

        /// <summary>인스턴스(현재 트랜스폼 적용 상태)의 렌더 기준 월드 AABB.
        /// 설치 후보의 겹침 검사용: 블루프린트(콜라이더 없음)와 서버의 스폰 전 인스턴스가
        /// 같은 프리팹 렌더러로 같은 값을 계산한다.</summary>
        public static Bounds RenderBounds(GameObject instance)
        {
            var rs = instance.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(instance.transform.position, Vector3.zero);

            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++)
                b.Encapsulate(rs[i].bounds);
            return b;
        }

        /// <summary>
        /// 설치 후보 AABB 가 스폰된 플레이어 설치 구조물 중 하나와 겹치는지(서버/클라 공용 판정).
        /// gap = 표면 간 요구 틈(각 면 방향으로 후보 AABB 를 gap 만큼 확장 후 교차 검사).
        /// 랜덤 타워 발판은 레지스트리에 없으므로 자동으로 검사 대상에서 제외(겹침 허용, 명세 유지).
        /// </summary>
        public static bool OverlapsAny(Bounds candidate, float gap)
        {
            candidate.Expand(gap * 2f); // Expand 는 전체 크기 기준 -> 면당 gap 확장
            for (int i = 0; i < s_active.Count; i++)
            {
                var s = s_active[i];
                if (s == null || !s.IsSpawned) continue;
                if (candidate.Intersects(s.WorldBounds)) return true;
            }
            return false;
        }

        /// <summary>
        /// 루트 피벗에서 시각적 바닥(렌더 바운즈 최저점)까지의 y 오프셋.
        /// 설치 위치 = 조준점 + up * 오프셋 -> 프리팹 바닥이 조준점에 닿는다(땅에 안 박힘).
        /// 서버 스폰(MatchManager)과 클라 블루프린트(PrepClientUI)가 같은 계산을 써야
        /// 프리뷰와 실물이 일치한다. yaw 회전은 수직 범위를 바꾸지 않으므로 회전 전 계산해도 안전.
        /// </summary>
        public static float BottomOffset(GameObject instance)
        {
            float minY = float.PositiveInfinity;
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
                minY = Mathf.Min(minY, r.bounds.min.y);
            if (float.IsPositiveInfinity(minY)) return 0f; // 렌더러 없음 -> 피벗 기준 유지
            return instance.transform.position.y - minY;
        }

        /// <summary>서버 전용. Instantiate 직후, Spawn() 이전에 호출해 종류/설치자를 확정한다.</summary>
        public void ServerInit(StructureType type, ulong owner)
        {
            Type.Value    = (byte)type;
            OwnerId.Value = owner;
        }

        public override void OnNetworkSpawn()
        {
            s_active.Add(this);

            // 설치 확정음: PREP 중 스폰 = 방금 설치된 구조물(늦은 합류의 초기 동기화 재생은 페이즈 가드로 걸러짐).
            var mm = MatchManager.Instance;
            if (mm != null && mm.IsSpawned && mm.CurrentPhase == MatchPhase.Prep)
                AudioManager.Play(Sfx.Place);

            if (_renderers != null && _renderers.Length > 0)
            {
                _originalMats = new Material[_renderers.Length];
                for (int i = 0; i < _renderers.Length; i++)
                    if (_renderers[i] != null) _originalMats[i] = _renderers[i].sharedMaterial;
            }
            _lastState = -1; // 첫 프레임에 강제 적용
        }

        public override void OnNetworkDespawn()
        {
            s_active.Remove(this);
        }

        // 가시성은 페이즈·RevealUntil(시간)에 의존하므로 이벤트가 아니라 매 프레임 평가한다.
        // (렌더러는 상태/알파가 실제로 변한 프레임에만 만지므로 비용은 미미)
        void Update()
        {
            if (!IsSpawned) return;
            if (!IsInvisibleKind) return; // 보이는 구조물은 프리팹 기본 렌더 그대로

            int state = ComputeState();
            if (state != _lastState)
            {
                bool initial = _lastState == -1; // 스폰 직후 첫 적용(늦은 합류 포함)
                _lastState = state;
                if (initial) _alpha = state == 2 ? 1f : 0f;             // 늦은 합류는 페이드 없이 현 상태로
                else if (state == 2) AudioManager.Play(Sfx.Reveal);      // 충돌 공개 순간(전 클라)
            }

            // 목표 알파로 부드럽게 이동: 공개(2) = 1, 숨김(0) = 0. 프리뷰(1)는 알파와 무관.
            float target = _lastState == 2 ? 1f : 0f;
            float dur = target > _alpha ? _fadeInDuration : _fadeOutDuration;
            _alpha = dur <= 0f ? target : Mathf.MoveTowards(_alpha, target, Time.deltaTime / dur);

            ApplyVisual(_lastState, _alpha);
        }

        int ComputeState()
        {
            // 충돌 공개 중이면 전원에게 원본으로 보임(최우선).
            if (NetworkManager != null && NetworkManager.ServerTime.Time < RevealUntil.Value)
                return 2;

            // PREP + 설치자 본인 -> 반투명 표시.
            var mm = MatchManager.Instance;
            bool prep = mm != null && mm.IsSpawned && mm.CurrentPhase == MatchPhase.Prep;
            bool mine = NetworkManager.Singleton != null &&
                        OwnerId.Value == NetworkManager.Singleton.LocalClientId;
            if (prep && mine) return 1;

            return 0; // 그 외 전부 숨김(설치자 본인 포함)
        }

        void ApplyVisual(int state, float alpha)
        {
            if (_renderers == null) return;
            if (state == _appliedState && Mathf.Approximately(alpha, _appliedAlpha)) return;
            _appliedState = state;
            _appliedAlpha = alpha;

            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;

                // 설치자 PREP 프리뷰: 페이드와 무관한 별도 표시(기존 동작 유지).
                if (state == 1)
                {
                    r.enabled = true;
                    if (_ownerPreviewMaterial != null) r.sharedMaterial = _ownerPreviewMaterial;
                    else if (_originalMats != null && _originalMats[i] != null) r.sharedMaterial = _originalMats[i];
                    continue;
                }

                if (alpha <= 0f) { r.enabled = false; continue; }

                r.enabled = true;
                if (alpha >= 1f)
                {
                    // 완전 공개 = 원본(불투명) 복귀.
                    if (_originalMats != null && _originalMats[i] != null) r.sharedMaterial = _originalMats[i];
                }
                else
                {
                    // 페이드 중 = 투명 전환 복제본에 알파 반영.
                    EnsureFadeMats();
                    var m = _fadeMats != null ? _fadeMats[i] : null;
                    if (m == null) continue;
                    Color c = _origColors[i];
                    c.a *= alpha;
                    m.color = c;
                    r.sharedMaterial = m;
                }
            }
        }

        // 페이드용 머티리얼 지연 생성: 원본 복제 -> 투명 블렌딩 전환.
        void EnsureFadeMats()
        {
            if (_fadeMats != null || _originalMats == null) return;
            _fadeMats = new Material[_originalMats.Length];
            _origColors = new Color[_originalMats.Length];
            for (int i = 0; i < _originalMats.Length; i++)
            {
                if (_originalMats[i] == null) continue;
                var m = new Material(_originalMats[i]);
                MakeTransparent(m);
                _fadeMats[i] = m;
                _origColors[i] = _originalMats[i].color;
            }
        }

        // URP Lit 계열 머티리얼을 런타임에 투명(알파 블렌드)으로 전환하는 표준 절차.
        // 프로퍼티가 없는 커스텀 셰이더는 해당 항목만 건너뛴다(그 경우 페이드가 안 먹고
        // 즉시 on/off 처럼 보일 수 있음 -> 보이지 않는 구조물 프리팹은 Lit 계열 권장).
        static void MakeTransparent(Material m)
        {
            m.SetOverrideTag("RenderType", "Transparent");
            if (m.HasProperty("_Surface"))  m.SetFloat("_Surface", 1f); // 1 = Transparent
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite"))   m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        void OnDestroy()
        {
            if (_fadeMats == null) return;
            for (int i = 0; i < _fadeMats.Length; i++)
                if (_fadeMats[i] != null) Destroy(_fadeMats[i]);
        }
    }
}
