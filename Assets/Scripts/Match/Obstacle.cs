using Unity.Netcode;
using UnityEngine;

namespace RouletteParty.Match
{
    /// <summary>
    /// 구조물 종류. Wall/Cylinder = 보이는 구조물, Ghost = 보이지 않는 구조물.
    /// byte 백킹 -> RPC/NetworkVariable 직렬화 그대로 사용.
    /// </summary>
    public enum ObstacleType : byte { Wall = 0, Cylinder = 1, Ghost = 2 }

    /// <summary>
    /// 구조물(플레이어가 설치하는 Object). 클라이밍 전환 명세 6절의 가시성 규칙 구현.
    ///
    ///  - 콜라이더는 모든 클라에서 항상 켜져 있다(물리 공정성, 기존 규약 유지). 렌더러만 분기.
    ///  - 보이는 구조물(Wall/Cylinder): 생성 후 어떤 페이즈에서도 전원에게 보임(분기 없음).
    ///  - 보이지 않는 구조물(Ghost):
    ///      PREP + 설치자 본인      -> 보임(반투명 머티리얼이 있으면 반투명)
    ///      그 외 페이즈/타인       -> 안 보임(설치자 본인 포함)
    ///      플레이어 충돌(상호작용) -> 서버가 RevealUntil 기록, 그 시각까지 전원에게 보임
    ///  - Type/OwnerId 는 서버가 Spawn() 이전에 세팅(ServerInit)해 초기 동기화에 싣는다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class Obstacle : NetworkBehaviour
    {
        [Header("GHOST(보이지 않는 구조물) 렌더 제어 (Wall/Cylinder 프리팹은 비워도 됨)")]
        [Tooltip("가시성 분기 대상 렌더러들. Ghost 프리팹에서만 필요.")]
        [SerializeField] private Renderer[] _renderers;

        [Tooltip("선택: PREP 중 설치자에게 보여줄 반투명 머티리얼. 비우면 원본 머티리얼로 보인다.")]
        [SerializeField] private Material _ghostOwnerMaterial;

        [Tooltip("플레이어 상호작용(충돌) 시 전원에게 공개되는 시간(초). 연속 충돌은 연장된다.")]
        [SerializeField] private float _revealDuration = 2f;
        public float RevealDuration => _revealDuration;

        // 기본 권한 { Read: Everyone, Write: Server }.
        public NetworkVariable<byte>  Type    = new NetworkVariable<byte>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<ulong> OwnerId = new NetworkVariable<ulong>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        // 이 서버 시각까지 전원에게 공개(보이지 않는 구조물 전용). 서버만 write.
        public NetworkVariable<double> RevealUntil = new NetworkVariable<double>(
            0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public ObstacleType ObType => (ObstacleType)Type.Value;
        public bool IsInvisibleKind => ObType == ObstacleType.Ghost;

        // 원본 머티리얼 캐시(반투명 <-> 원본 전환용)
        Material[] _originalMats;
        int _lastState = -1; // 0=숨김, 1=반투명(설치자 PREP), 2=원본(공개/보이는 구조물)

        /// <summary>서버 전용. Instantiate 직후, Spawn() 이전에 호출해 종류/설치자를 확정한다.</summary>
        public void ServerInit(ObstacleType type, ulong owner)
        {
            Type.Value    = (byte)type;
            OwnerId.Value = owner;
        }

        public override void OnNetworkSpawn()
        {
            if (_renderers != null && _renderers.Length > 0)
            {
                _originalMats = new Material[_renderers.Length];
                for (int i = 0; i < _renderers.Length; i++)
                    if (_renderers[i] != null) _originalMats[i] = _renderers[i].sharedMaterial;
            }
            _lastState = -1; // 첫 프레임에 강제 적용
        }

        // 가시성은 페이즈·RevealUntil(시간)에 의존하므로 이벤트가 아니라 매 프레임 평가한다.
        // (상태 전환 시에만 렌더러를 만지므로 비용은 미미)
        void Update()
        {
            if (!IsSpawned) return;
            if (!IsInvisibleKind) return; // 보이는 구조물은 프리팹 기본 렌더 그대로

            int state = ComputeState();
            if (state == _lastState) return;
            _lastState = state;
            ApplyState(state);
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

        void ApplyState(int state)
        {
            if (_renderers == null) return;
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;
                switch (state)
                {
                    case 0:
                        r.enabled = false;
                        break;
                    case 1:
                        r.enabled = true;
                        if (_ghostOwnerMaterial != null) r.sharedMaterial = _ghostOwnerMaterial;
                        else if (_originalMats != null && _originalMats[i] != null) r.sharedMaterial = _originalMats[i];
                        break;
                    case 2:
                        r.enabled = true;
                        if (_originalMats != null && _originalMats[i] != null) r.sharedMaterial = _originalMats[i];
                        break;
                }
            }
        }
    }
}
