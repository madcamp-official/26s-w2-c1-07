using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;
using RouletteParty.Audio; // AudioManager (설치/발동 사운드)

namespace RouletteParty.Match
{
    /// <summary>
    /// 구조물 형태(가구/자연물). 크기 등급(소/중/대)은 형태에 내장된 성질이며
    /// `MatchManager._structureDefs` 가 형태 -> 등급/프리팹을 정한다 — 스케일 배율로 키우지 않는다
    /// (대형 구조물은 "2배로 늘린 소형"이 아니라 실제로 큰 에셋. 옷장을 2배로 늘리면 비례가 깨진다).
    ///
    /// 투명 여부는 형태가 아니다: 별도 플래그(<see cref="Structure.Hidden"/>)라 어떤 형태든
    /// 투명일 수 있다(투명 나무·투명 옷장). 예전의 "투명 벽" 전용 타입은 폐지됐다.
    ///
    /// byte 백킹 -> RPC/NetworkVariable 직렬화 그대로 사용.
    /// 255 = <see cref="Structure.TypeUnknown"/>(서버 값이 아직 도착하지 않음).
    /// </summary>
    public enum StructureType : byte
    {
        Stump   = 0, // 소 - 그루터기
        Cushion = 1, // 소 - 쿠션
        Boulder = 2, // 소 - 돌덩이
        Table   = 3, // 중 - 테이블
        Drawer  = 4, // 중 - 서랍장
        Rock    = 5, // 중 - 바위
        Closet  = 6, // 대 - 옷장
        Tree    = 7, // 대 - 나무
    }

    /// <summary>구조물 크기 등급. 배치 큐가 낮은 확률로 중/대를 굴린다(형태 자체가 커진다).</summary>
    public enum StructureSize : byte { Small = 0, Medium = 1, Large = 2 }

    /// <summary>
    /// 구조물(플레이어가 설치하는 Object). 클라이밍 전환 명세 6절의 가시성 규칙 구현.
    ///
    ///  - 콜라이더는 모든 클라에서 항상 켜져 있다(물리 공정성, 기존 규약 유지). 렌더러만 분기.
    ///  - 보이는 구조물(Hidden=false): 종류가 도착한 뒤로는 어떤 페이즈에서도 전원에게 보임.
    ///  - 투명 구조물(Hidden=true):
    ///      PREP + 설치자 본인      -> 반투명으로 보임(전용 머티리얼이 없으면 원본을 알파 낮춰 표시)
    ///      그 외 페이즈/타인       -> 안 보임(설치자 본인 포함)
    ///      플레이어 충돌(상호작용) -> 서버가 RevealUntil 기록, 그 시각까지 전원에게 보임.
    ///      공개/숨김 전환은 알파 페이드(_fadeInDuration/_fadeOutDuration)로 부드럽게 진행.
    ///  - Type/OwnerId/Hidden 은 서버가 Spawn() "직후"에 세팅한다(ServerInit) — 스폰 전 쓰기는
    ///    초기값으로 리셋돼 유실된다(실측). 그래서 스폰 직후 몇 프레임은 종류를 모르는 구간이
    ///    생기는데, 이때 렌더러를 켜 두면 투명 구조물이 잠깐 보여 함정 위치가 샌다.
    ///    -> Type 기본값을 TypeUnknown(255)으로 두고, 값이 도착하기 전에는 전부 숨긴다
    ///       (보이는 구조물이 1~2프레임 늦게 나타나는 건 체감되지 않는다).
    ///  - 겹침 규칙: 설치물끼리 "접촉(면 맞대기)은 허용, 관통만 금지"(OverlapScan).
    ///    구조물 위에 구조물을 쌓아 발판처럼 쓰는 플레이를 지원한다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class Structure : NetworkBehaviour
    {
        /// <summary>서버 값이 아직 도착하지 않은 상태를 나타내는 Type 센티널.
        /// Hidden(bool) 은 기본값 false 가 "보임"과 구분되지 않으므로, 도착 여부는 Type 으로 판정한다.</summary>
        public const byte TypeUnknown = 255;

        // 스폰 중인 전체 구조물 레지스트리. 매 프레임 FindObjectsByType 스캔을 피하기 위한
        // 표준 패턴(스폰/디스폰 시점에만 갱신). 잔여 개수 계산(PrepClientUI) 등이 순회한다.
        private static readonly List<Structure> s_active = new List<Structure>();
        public static IReadOnlyList<Structure> Active => s_active;

        [Header("렌더 제어 (어떤 형태든 투명이 될 수 있다)")]
        // 숨김은 renderer.enabled 가 아니라 forceRenderingOff 로 한다. 나무·그루터기 같은 에셋에는
        // LODGroup 이 붙어 있고 LODGroup 이 거리마다 자기 LOD 의 enabled 를 직접 켠다 —
        // enabled 로 숨기면 LODGroup 이 다음 갱신에 도로 켜서 투명 구조물이 저절로 드러난다.
        // forceRenderingOff 는 enabled 를 건드리지 않고 렌더만 막으므로 LODGroup 과 싸우지 않는다.
        [Tooltip("가시성 분기 대상 렌더러. 비워 두면 스폰 시 자식 전체에서 자동 수집한다(권장 - 형태마다 배선할 필요 없음).")]
        [SerializeField] private Renderer[] _renderers;

        [Tooltip("선택: PREP 중 설치자에게 보여줄 반투명 머티리얼. 비우면 원본 머티리얼을 아래 알파로 낮춰 표시한다.")]
        [FormerlySerializedAs("_ghostOwnerMaterial")]
        [SerializeField] private Material _ownerPreviewMaterial;

        [Tooltip("전용 머티리얼이 없을 때 설치자 PREP 표시에 쓸 알파(0~1). '내가 둔 투명 구조물'임을 알 수 있게 반투명으로.")]
        [Range(0f, 1f)]
        [SerializeField] private float _ownerPreviewAlpha = 0.45f;

        [Tooltip("플레이어 상호작용(충돌) 시 전원에게 공개되는 시간(초). 연속 충돌은 연장된다.")]
        [SerializeField] private float _revealDuration = 2f;
        public float RevealDuration => _revealDuration;

        [Tooltip("공개 시 알파 페이드 인 시간(초). 0 = 즉시.")]
        [SerializeField] private float _fadeInDuration = 0.15f;
        [Tooltip("공개 종료 시 알파 페이드 아웃 시간(초). 0 = 즉시.")]
        [SerializeField] private float _fadeOutDuration = 0.6f;

        // 기본 권한 { Read: Everyone, Write: Server }.
        public NetworkVariable<byte>  Type    = new NetworkVariable<byte>(
            TypeUnknown, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<ulong> OwnerId = new NetworkVariable<ulong>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        /// <summary>투명 구조물(함정) 여부. 형태와 무관한 별도 속성 — 서버만 write.</summary>
        public NetworkVariable<bool> Hidden = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        // 이 서버 시각까지 전원에게 공개(투명 구조물 전용). 서버만 write.
        public NetworkVariable<double> RevealUntil = new NetworkVariable<double>(
            0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public StructureType Kind => (StructureType)Type.Value;
        /// <summary>서버가 보낸 종류/투명 여부가 도착했는가(스폰 직후 몇 프레임은 false).</summary>
        public bool StateKnown => Type.Value != TypeUnknown;
        /// <summary>투명 구조물(함정)인가. 형태가 아니라 플래그로 판정한다.</summary>
        public bool IsInvisibleKind => Hidden.Value;

        /// <summary>이 구조물이 로컬 클라이언트에게 숨겨져 있는가(렌더러 가시성과 같은 규칙 = ComputeState).
        /// 조준 레이(PlayerController.UpdateAimRay)가 이 값으로 함정 콜라이더를 통과시킨다 —
        /// 블루프린트가 함정 표면에 얹히거나 윗면 스냅이 작동해 위치가 드러나는 것을 막는다.</summary>
        public bool IsHiddenFromLocal => IsSpawned && ComputeState() == 0;

        // 원본 머티리얼 캐시(반투명 <-> 원본 전환용). 렌더러당 슬롯 배열 = 멀티 머티리얼 대응:
        // sharedMaterial(단수)로 다루면 슬롯이 1개로 뭉개져 나무(줄기+잎)·옷장처럼 재질이
        // 여러 개인 에셋의 서브메시가 깨진다.
        Material[][] _originalMats;
        // 콜라이더 캐시(WorldBounds 용. 콜라이더는 규약상 모든 클라에서 항상 켜져 있다)
        Collider[] _colliders;
        int _lastState = -1; // 0=숨김, 1=반투명(설치자 PREP), 2=원본(공개/보이는 구조물)

        // ---- 공개 페이드(투명 구조물 전용) ----
        // 원본은 불투명 머티리얼이므로 페이드 중에만 투명 블렌딩으로 전환한 복제본을 쓰고,
        // 알파 1 도달 시 원본(불투명)으로 복귀한다(투명 큐 상주 방지 — 정렬/오버드로우 표준 관행).
        float _alpha;                 // 현재 표시 알파(0 = 완전 숨김, 1 = 완전 공개)
        Material[][] _fadeMats;       // 투명 전환된 원본 복제본(지연 생성, [렌더러][슬롯])
        Color[][] _origColors;
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
        /// 설치 후보 AABB 와 스폰된 플레이어 설치 구조물의 겹침 스캔(서버/클라 공용 판정).
        /// 규칙: 접촉(면 맞대기)은 허용, 관통만 금지 — 후보 AABB 를 tolerance 만큼 면당 축소 후
        /// 교차 검사하므로 구조물 위에 구조물을 얹는(쌓는) 설치가 통과한다.
        /// 결과는 viewer(요청자) 기준으로 분리:
        ///  - visibleHit: viewer 에게 보이는 설치물과 관통(보이는 구조물 또는 viewer 본인의 투명 구조물)
        ///  - hiddenHit : viewer 에게 숨겨진 타인의 투명 구조물과 관통(함정 위치 비노출용 별도 처리)
        /// 클라 프리뷰는 visibleHit 만 빨간색 근거로 쓰고, 서버는 둘 다 거부하되 hiddenHit 단독이면
        /// 요청자에게 "함정" 통지를 보낸다. 랜덤 레인 발판은 레지스트리 밖 -> 검사 제외(겹침 허용).
        /// </summary>
        public static void OverlapScan(Bounds candidate, float tolerance, ulong viewer,
                                       out bool visibleHit, out bool hiddenHit)
        {
            visibleHit = false; hiddenHit = false;
            candidate.Expand(-tolerance * 2f); // Expand 는 전체 크기 기준 -> 면당 tolerance 축소
            for (int i = 0; i < s_active.Count; i++)
            {
                var s = s_active[i];
                if (s == null || !s.IsSpawned) continue;
                if (!candidate.Intersects(s.WorldBounds)) continue;

                bool hiddenToViewer = s.IsInvisibleKind && s.OwnerId.Value != viewer;
                if (hiddenToViewer) hiddenHit = true; else visibleHit = true;
                if (visibleHit && hiddenHit) return; // 둘 다 확정되면 조기 종료
            }
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

        /// <summary>서버 전용. Spawn() "직후"에 호출해 종류/설치자/투명 여부를 확정한다
        /// (스폰 전 쓰기는 초기값으로 리셋돼 유실된다).</summary>
        public void ServerInit(StructureType type, ulong owner, bool invisible)
        {
            Type.Value    = (byte)type;
            OwnerId.Value = owner;
            Hidden.Value  = invisible;
        }

        public override void OnNetworkSpawn()
        {
            s_active.Add(this);

            // 설치 확정음: PREP 중 스폰 = 방금 설치된 구조물(늦은 합류의 초기 동기화 재생은 페이즈 가드로 걸러짐).
            var mm = MatchManager.Instance;
            if (mm != null && mm.IsSpawned && mm.CurrentPhase == MatchPhase.Prep)
                AudioManager.Play(Sfx.Place);

            // 어떤 형태든 투명이 될 수 있으므로 렌더러는 기본 자동 수집(프리팹마다 배선 불필요).
            if (_renderers == null || _renderers.Length == 0)
                _renderers = GetComponentsInChildren<Renderer>(true);

            _originalMats = new Material[_renderers.Length][];
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                _originalMats[i] = _renderers[i].sharedMaterials;
                // 종류/투명 여부가 도착하기 전에는 무조건 숨긴다(함정 누출 방지 — 클래스 주석 참조).
                _renderers[i].forceRenderingOff = true;
            }
            _lastState = -1; _appliedState = -1; _appliedAlpha = -1f; _alpha = 0f;
        }

        public override void OnNetworkDespawn()
        {
            s_active.Remove(this);
        }

        // 가시성은 페이즈·RevealUntil(시간)·복제 도착 여부에 의존하므로 매 프레임 평가한다.
        // (렌더러는 상태/알파가 실제로 변한 프레임에만 만지므로 비용은 미미)
        void Update()
        {
            if (!IsSpawned) return;

            int state = ComputeState();
            if (state != _lastState)
            {
                bool initial = _lastState == -1; // 스폰 직후 첫 평가(늦은 합류 포함)
                _lastState = state;
                if (initial) _alpha = state == 2 ? 1f : 0f;              // 늦은 합류는 페이드 없이 현 상태로
                else if (state == 2 && IsInvisibleKind)
                    AudioManager.Play(Sfx.Reveal);                        // 충돌 공개 순간(전 클라)
            }

            // 보이는 구조물은 페이드 대상이 아니다: 스폰 직후 숨김은 함정 누출 방지 장치일 뿐
            // 연출이 아니므로, 종류가 도착하는 즉시 원본으로 스냅한다.
            if (StateKnown && !IsInvisibleKind) _alpha = 1f;
            else
            {
                float target = state == 2 ? 1f : 0f;
                float dur = target > _alpha ? _fadeInDuration : _fadeOutDuration;
                _alpha = dur <= 0f ? target : Mathf.MoveTowards(_alpha, target, Time.deltaTime / dur);
            }

            ApplyVisual(state, _alpha);
        }

        // 0 = 숨김, 1 = 반투명(설치자 PREP 프리뷰), 2 = 원본 표시.
        int ComputeState()
        {
            // 서버 값이 아직 안 왔다 -> 숨김(투명 구조물 누출 방지).
            if (!StateKnown) return 0;

            // 보이는 구조물 = 언제나 원본(분기 없음).
            if (!IsInvisibleKind) return 2;

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

                // 설치자 PREP 프리뷰: 페이드와 무관한 별도 표시.
                // 전용 머티리얼이 없으면 페이드용 투명 복제본을 재사용해 원본 색 그대로 알파만 낮춘다
                // -> 설치자가 "저건 내가 둔 투명 구조물"임을 구분할 수 있다.
                if (state == 1)
                {
                    r.forceRenderingOff = false;
                    if (_ownerPreviewMaterial != null) FillSlots(r, i, _ownerPreviewMaterial);
                    else if (!TryApplyFade(r, i, _ownerPreviewAlpha) && _originalMats[i] != null)
                        r.sharedMaterials = _originalMats[i]; // 폴백 실패(커스텀 셰이더 등) -> 원본
                    continue;
                }

                if (alpha <= 0f) { r.forceRenderingOff = true; continue; }

                r.forceRenderingOff = false;
                if (alpha >= 1f)
                {
                    // 완전 공개 = 원본(불투명) 복귀.
                    if (_originalMats[i] != null) r.sharedMaterials = _originalMats[i];
                }
                else
                {
                    TryApplyFade(r, i, alpha); // 페이드 중 = 투명 전환 복제본에 알파 반영
                }
            }
        }

        // 렌더러의 모든 서브메시 슬롯을 같은 머티리얼로 채운다(슬롯 수를 보존해야 서브메시가 안 깨진다).
        void FillSlots(Renderer r, int i, Material m)
        {
            int n = _originalMats[i] != null ? _originalMats[i].Length : 1;
            var mats = new Material[n];
            for (int s = 0; s < n; s++) mats[s] = m;
            r.sharedMaterials = mats;
        }

        // 투명 복제본에 알파를 반영해 적용. 복제본을 만들 수 없으면 false(호출부가 원본으로 폴백).
        bool TryApplyFade(Renderer r, int i, float alpha)
        {
            EnsureFadeMats();
            var slots = _fadeMats != null ? _fadeMats[i] : null;
            if (slots == null) return false;
            var src = _origColors[i];
            for (int s = 0; s < slots.Length; s++)
            {
                if (slots[s] == null) continue;
                Color c = src[s];
                c.a *= alpha;
                slots[s].color = c;
            }
            r.sharedMaterials = slots;
            return true;
        }

        // 페이드용 머티리얼 지연 생성: 원본 복제 -> 투명 블렌딩 전환(렌더러별 슬롯 배열 유지).
        void EnsureFadeMats()
        {
            if (_fadeMats != null || _originalMats == null) return;
            _fadeMats = new Material[_originalMats.Length][];
            _origColors = new Color[_originalMats.Length][];
            for (int i = 0; i < _originalMats.Length; i++)
            {
                var src = _originalMats[i];
                if (src == null) continue;
                _fadeMats[i] = new Material[src.Length];
                _origColors[i] = new Color[src.Length];
                for (int s = 0; s < src.Length; s++)
                {
                    if (src[s] == null) continue;
                    var m = new Material(src[s]);
                    MakeTransparent(m);
                    _fadeMats[i][s] = m;
                    _origColors[i][s] = src[s].color;
                }
            }
        }

        // URP Lit 계열 머티리얼을 런타임에 투명(알파 블렌드)으로 전환하는 표준 절차.
        // 프로퍼티가 없는 커스텀 셰이더는 해당 항목만 건너뛴다(그 경우 페이드가 안 먹고
        // 즉시 on/off 처럼 보일 수 있음 -> 투명 구조물 프리팹은 Lit 계열 권장).
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
            {
                var slots = _fadeMats[i];
                if (slots == null) continue;
                for (int s = 0; s < slots.Length; s++)
                    if (slots[s] != null) Destroy(slots[s]);
            }
        }
    }
}
