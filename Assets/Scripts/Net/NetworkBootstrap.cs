using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using RouletteParty.UI; // ImguiScale (OnGUI 해상도 스케일링)

namespace RouletteParty.Net
{
    /// <summary>
    /// 씬의 아무 GameObject(권장: NetworkManager 오브젝트)에 부착한다.
    /// 좌상단 OnGUI 버튼으로 Host / Client / Server 시작 및 접속 상태를 표시하므로,
    /// 별도 UI 프리팹 없이 바로 테스트할 수 있다.
    ///
    /// 트랜스포트(UnityTransport) 전용 설정은 ApplyConnectionData() 한 곳에만 있으므로,
    /// 나중에 Steam/Facepunch 트랜스포트로 교체할 때 이 메서드만 바꾸면 된다.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkBootstrap : MonoBehaviour
    {
        [Header("접속 정보 (클라이언트가 접속할 대상)")]
        [SerializeField] private string _ipAddress = "127.0.0.1";
        [SerializeField] private ushort _port = 7777;

        private string _statusMessage = "대기 중";
        private bool _subscribed;
        private ushort _activePort; // 실제 호스팅에 사용된 포트(자동 탐색 결과 표시용)

        // NetworkManager 와 같은 오브젝트에 붙는 경우 OnEnable 시점에 Singleton 이 아직
        // 준비 안 됐을 수 있어(컴포넌트 초기화 순서), 구독을 지연/재시도로 안전화한다.
        private void OnEnable()
        {
            TrySubscribe();
        }

        private void Start()
        {
            // OnEnable 에서 Singleton 이 null 이었던 경우를 대비한 2차 구독 시도.
            TrySubscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void TrySubscribe()
        {
            if (_subscribed) return;
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            // NGO 2.x 권장 통합 이벤트
            nm.OnConnectionEvent += HandleConnectionEvent;
            nm.OnServerStarted   += HandleServerStarted;
            nm.OnClientStarted   += HandleClientStarted;
            nm.OnServerStopped   += HandleServerStopped;
            nm.OnClientStopped   += HandleClientStopped;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.OnConnectionEvent -= HandleConnectionEvent;
                nm.OnServerStarted   -= HandleServerStarted;
                nm.OnClientStarted   -= HandleClientStarted;
                nm.OnServerStopped   -= HandleServerStopped;
                nm.OnClientStopped   -= HandleClientStopped;
            }
            _subscribed = false;
        }

        private void OnGUI()
        {
            ImguiScale.Apply(); // 이하 좌표는 1080p 기준 가상 픽셀
            GUILayout.BeginArea(new Rect(10, 10, 260, 270), GUI.skin.box);

            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                GUILayout.Label("NetworkManager.Singleton 없음\n씬에 NetworkManager 오브젝트가 필요합니다.");
                GUILayout.EndArea();
                return;
            }

            // Singleton 이 늦게 준비된 경우를 대비해 이벤트 구독을 보장.
            TrySubscribe();

            if (nm.ShutdownInProgress)
            {
                // Shutdown 은 비동기 정리라, 완료 전에 다시 Start 하면 실패한다. 끝날 때까지 버튼 숨김.
                GUILayout.Label("이전 세션 종료 처리 중...");
            }
            else if (!nm.IsClient && !nm.IsServer)
            {
                GUILayout.Label("주소 / 포트");
                _ipAddress = GUILayout.TextField(_ipAddress);
                string portStr = GUILayout.TextField(_port.ToString());
                if (ushort.TryParse(portStr, out ushort parsed)) _port = parsed;

                GUILayout.Space(6);
                if (GUILayout.Button("Host (서버 + 내 플레이어)"))
                {
                    StartHosting(nm, asServer: false);
                }
                if (GUILayout.Button("Client"))
                {
                    // 클라는 위에 입력한 IP:포트로 접속(호스트 화면에 표시된 포트를 입력)
                    ApplyConnectionData(_ipAddress, listen: false, _port);
                    if (!nm.StartClient()) _statusMessage = "StartClient 실패";
                }
                if (GUILayout.Button("Server (헤드리스, 내 플레이어 없음)"))
                {
                    StartHosting(nm, asServer: true);
                }
            }
            else
            {
                string mode = nm.IsHost ? "Host" : (nm.IsServer ? "Server" : "Client");
                GUILayout.Label($"모드: {mode}");
                GUILayout.Label($"내 ClientId: {nm.LocalClientId}");
                if (nm.IsServer && _activePort != 0)
                    GUILayout.Label($"호스팅 포트: {_activePort}"); // 클라이언트가 입력할 포트

                // ConnectedClients 는 서버 전용 프로퍼티라 클라에서 접근하면 예외가 난다. 반드시 IsServer 가드.
                if (nm.IsServer)
                    GUILayout.Label($"접속 수: {nm.ConnectedClients.Count}");

                GUILayout.Space(6);
                if (GUILayout.Button("Shutdown"))
                {
                    nm.Shutdown();
                    _statusMessage = "종료됨";
                }
            }

            GUILayout.Space(6);
            GUILayout.Label($"상태: {_statusMessage}");
            GUILayout.EndArea();
        }

        /// <summary>
        /// UnityTransport 전용 설정을 이 메서드 하나에 격리한다.
        /// [Steam 전환 지점] 나중에 Facepunch/Steam 트랜스포트로 갈아끼울 때,
        /// 이 메서드 내부(SetConnectionData 등 트랜스포트별 설정)만 교체하면 나머지 코드는 그대로 둔다.
        /// </summary>
        /// <summary>
        /// 호스트/서버 시작 공통 경로. 요청 포트가 사용 중이면(에디터 소켓 누수·다른 인스턴스·
        /// MPPM 가상 플레이어 등) 다음 포트를 자동 탐색해 그 포트로 리스닝하고, GUI 에 표시한다.
        /// </summary>
        private void StartHosting(NetworkManager nm, bool asServer)
        {
            ushort freePort = FindFreeUdpPort(_port);
            _activePort = freePort;
            if (freePort != _port)
                _statusMessage = $"포트 {_port} 사용 중 → {freePort} 로 대체";

            // 호스트/서버는 모든 인터페이스에서 리스닝(LAN 접속 허용)
            ApplyConnectionData("0.0.0.0", listen: true, freePort);

            bool ok = asServer ? nm.StartServer() : nm.StartHost();
            if (!ok)
                _statusMessage = (asServer ? "StartServer" : "StartHost") +
                                 $" 실패 (포트 {freePort}, 콘솔 로그 확인)";
        }

        /// <summary>
        /// preferred 부터 UDP 바인딩을 실제로 시도해 사용 가능한 첫 포트를 찾는다.
        /// probe 소켓은 즉시 닫으므로 곧바로 이어지는 트랜스포트 바인딩이 그 포트를 쓸 수 있다.
        /// 전부 사용 중이면 preferred 를 그대로 반환한다(Start 쪽 실패 메시지가 안내).
        /// </summary>
        private static ushort FindFreeUdpPort(ushort preferred, int tryCount = 10)
        {
            for (int i = 0; i < tryCount; i++)
            {
                ushort candidate = (ushort)(preferred + i);
                try
                {
                    using (var probe = new UdpClient())
                    {
                        probe.ExclusiveAddressUse = true;
                        probe.Client.Bind(new IPEndPoint(IPAddress.Any, candidate));
                        return candidate; // 바인딩 성공 = 사용 가능
                    }
                }
                catch (SocketException)
                {
                    // 사용 중 -> 다음 후보
                }
            }
            return preferred;
        }

        private void ApplyConnectionData(string address, bool listen, ushort port)
        {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[Net] UnityTransport 컴포넌트를 찾을 수 없습니다. NetworkManager 에 트랜스포트가 붙어있는지 확인하세요.");
                return;
            }

            // 서버/호스트: listenAddress 를 0.0.0.0 으로 두어 모든 인터페이스에서 접속을 받는다(로컬+LAN).
            // 클라: 대상 IP(address)로만 접속. listenAddress 는 클라에서 사용되지 않으므로 null.
            // 시그니처: SetConnectionData(string ipv4Address, ushort port, string listenAddress = null)
            transport.SetConnectionData(address, port, listen ? "0.0.0.0" : null);
        }

        private void HandleConnectionEvent(NetworkManager nm, ConnectionEventData data)
        {
            _statusMessage = $"{data.EventType} (clientId={data.ClientId})";
            Debug.Log($"[Net] {data.EventType} clientId={data.ClientId}");
        }

        private void HandleServerStarted()          { _statusMessage = "서버 리스닝 시작"; Debug.Log("[Net] Server started, listening."); }
        private void HandleClientStarted()          { Debug.Log("[Net] Client started."); }
        private void HandleServerStopped(bool host) { _statusMessage = "서버 종료"; Debug.Log($"[Net] Server stopped (wasHost={host})."); }
        private void HandleClientStopped(bool host) { _statusMessage = "클라 종료"; Debug.Log($"[Net] Client stopped (wasHost={host})."); }
    }
}
