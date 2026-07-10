using Unity.Netcode;
using UnityEngine;

namespace RouletteParty.Match
{
    /// <summary>장애물 종류. byte 백킹 enum -> NetworkVariable / RPC 인자에 그대로 직렬화 가능(unmanaged).</summary>
    public enum ObstacleType : byte { Wall = 0, Cylinder = 1, Ghost = 2 }

    /// <summary>
    /// 동적으로 스폰되는 장애물 프리팹(루트에 NetworkObject)에 부착하는 컴포넌트.
    ///
    /// 설계 (리서치 검증, NGO 2.13 / Unity 6):
    ///  - 오브젝트의 네트워크 소유권은 "서버(호스트)"다. 누가 놓았는지는 <see cref="OwnerId"/>
    ///    NetworkVariable 로 따로 기록한다. (SpawnWithOwnership 로 클라에 소유권을 주면
    ///    ClientNetworkTransform/권위 이슈가 생기므로 프로젝트 규약상 금지.)
    ///  - <see cref="Type"/>/<see cref="OwnerId"/> 는 서버가 반드시 Spawn() '이전'에 세팅한다
    ///    (ServerInit). 그래야 초기 스폰 페이로드에 값이 실려 전 클라가 스폰 즉시 올바른
    ///    종류/소유자를 본다. (Spawn 이후 세팅하면 issue #3876 레이스로 한 프레임 잘못 렌더링.)
    ///  - GHOST(투명벽) 공정성: 콜라이더는 절대 끄지 않는다. 모두 같은 물리로 막힌다.
    ///    렌더러만 클라별로 분기해서 타인에겐 안 보이고, 소유자에겐(반투명 머티리얼이 있으면)
    ///    흐릿하게 보인다. 물리는 전원 동일.
    ///
    /// 콜라이더는 각 프리팹에 미리 붙여 두고(WALL/GHOST=BoxCollider, CYLINDER=CapsuleCollider,
    /// isTrigger=false, Rigidbody 없음) 항상 켜진 상태로 둔다 -> 이 스크립트는 콜라이더를
    /// 만지지 않는다(설정 실수 여지 최소화). 렌더러만 제어한다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class Obstacle : NetworkBehaviour
    {
        [Header("GHOST 렌더 제어용 (WALL/CYLINDER 프리팹은 비워도 됨)")]
        [Tooltip("GHOST 프리팹의 메시 렌더러들. 타인에게는 숨기고 소유자에게는 (반투명 머티리얼이 있으면) 보여준다.")]
        [SerializeField] private Renderer[] _renderers;

        [Tooltip("선택: GHOST 소유자가 볼 반투명 머티리얼(URP Surface=Transparent). 비우면 소유자에게도 숨긴다.")]
        [SerializeField] private Material _ghostOwnerMaterial;

        // 기본 권한 { Read: Everyone, Write: Server } — 서버가 쓰고 전 클라가 읽는 이 용도에 정확히 맞음.
        // (Owner/Owner 조합은 서버 값 미갱신 버그(#2094)가 있으니 쓰지 않는다.)
        public NetworkVariable<byte>  Type    = new NetworkVariable<byte>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<ulong> OwnerId = new NetworkVariable<ulong>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>편의 프로퍼티: 현재 종류.</summary>
        public ObstacleType ObType => (ObstacleType)Type.Value;

        /// <summary>
        /// 서버 전용. Instantiate 직후, Spawn() '이전'에 호출해 종류/소유자를 확정한다.
        /// 이 시점엔 "written to, but doesn't know its NetworkBehaviour yet" 경고가 뜰 수 있으나
        /// 무해하며 값은 초기 스폰 동기화에 그대로 포함된다(Unity 개발자 확인).
        /// </summary>
        public void ServerInit(ObstacleType type, ulong owner)
        {
            Type.Value    = (byte)type;
            OwnerId.Value = owner;
        }

        public override void OnNetworkSpawn()
        {
            // Spawn 전에 세팅했으므로 여기서 값은 이미 도착해 있다.
            // 초기값 레이스에 대비해 OnValueChanged 도 방어적으로 구독.
            Type.OnValueChanged    += OnTypeChanged;
            OwnerId.OnValueChanged += OnOwnerChanged;
            ApplyVisual();
        }

        public override void OnNetworkDespawn()
        {
            Type.OnValueChanged    -= OnTypeChanged;
            OwnerId.OnValueChanged -= OnOwnerChanged;
        }

        private void OnTypeChanged(byte _, byte __)   => ApplyVisual();
        private void OnOwnerChanged(ulong _, ulong __) => ApplyVisual();

        /// <summary>
        /// 순수 클라측 시각 분기. 콜라이더는 건드리지 않는다(물리는 전원 동일 = 공정).
        /// WALL/CYLINDER: 렌더러 그대로(프리팹 기본). GHOST: 소유자 외에는 렌더러 off.
        /// </summary>
        private void ApplyVisual()
        {
            if (ObType != ObstacleType.Ghost)
                return; // WALL/CYLINDER 는 프리팹 렌더링 그대로 사용

            // "소유자"는 NetworkObject 소유권(=호스트)이 아니라 커스텀 OwnerId 로 판별해야 한다.
            bool mine = NetworkManager.Singleton != null &&
                        OwnerId.Value == NetworkManager.Singleton.LocalClientId;

            foreach (var r in _renderers)
            {
                if (r == null) continue;
                if (mine && _ghostOwnerMaterial != null)
                {
                    r.enabled = true;
                    r.sharedMaterial = _ghostOwnerMaterial; // 소유자: 반투명으로 흐릿하게
                }
                else
                {
                    r.enabled = false; // 타인(또는 머티리얼 미지정 소유자): 완전 비가시(콜라이더만 존재)
                }
            }
        }
    }
}
