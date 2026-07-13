using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication; // 익명 로그인(Relay 필수)
using Unity.Services.Core;           // UnityServices 초기화
using Unity.Services.Relay;          // RelayService(할당·참가 코드)
using Unity.Services.Relay.Models;   // Allocation / JoinAllocation
using UnityEngine;
using RouletteParty.Match; // MatchManager (접속 승인: 페이즈/정원 검증)

namespace RouletteParty.Net
{
    /// <summary>
    /// 연결 계층 단일 창구. UI(LobbyUI / NetworkBootstrap 디버그 패널)는 "방 만들기 / 방 참가 /
    /// 나가기" 의도만 전달하고, NGO·UnityTransport 조작은 전부 이 서비스에 격리한다.
    ///
    ///  - 방 만들기 = StartHost: 요청 UDP 포트가 사용 중이면 다음 포트 자동 탐색(ActivePort 에 확정),
    ///    listen 은 0.0.0.0(모든 인터페이스 = LAN 접속 허용), 표시용 LAN 사설 IPv4 탐색.
    ///    ★표시용 IP(PrimaryLanIp)와 listen 주소(0.0.0.0)는 다른 개념이다 — 혼동 금지.★
    ///  - 방 참가 = StartClient: 입력한 IP:포트로 직접 접속(자체 방 코드 인코딩 없음).
    ///  - 접속 승인(ConnectionApproval, 서버): LOBBY 외 페이즈 신규 참가 거절("게임이 이미 진행 중"),
    ///    정원 초과 거절("방이 가득 참"). 거절 사유는 클라의 NetworkManager.DisconnectReason 으로 전달.
    ///
    /// [Unity Relay 모드, 기본 ON(_useRelay)] 서로 다른 네트워크(집/카페/캠퍼스 망 격리)에서도
    /// 접속되도록 유니티 릴레이 서버를 경유한다. 양쪽 모두 아웃바운드 연결만 쓰므로 방화벽·NAT
    /// 설정이 필요 없다. 흐름: UGS 초기화 + 익명 로그인 -> 호스트 CreateAllocation + 참가 코드 발급
    /// -> 참가자는 코드로 JoinAllocation -> UnityTransport 에 릴레이 데이터 주입 -> StartHost/Client.
    /// 비동기(수 초)이므로 UI 는 RelayBusy 로 진행 상태를, ConsumeLastError() 로 실패 사유를 읽는다.
    /// _useRelay 를 끄면 기존 LAN 직접 IP 접속으로 폴백(오프라인 시연 대비).
    /// [Steam 전환 지점] Facepunch 도입 시에도 같은 자리(CreateRoom/JoinRoom 내부)만 교체한다.
    /// 씬 배치: NetworkManager 오브젝트에 컴포넌트로 추가한다(에디터 Add Component).
    /// </summary>
    [DisallowMultipleComponent]
    public class ConnectionService : MonoBehaviour
    {
        [Header("Unity Relay (기본 접속 방식)")]
        [Tooltip("켜면 유니티 릴레이(참가 코드)로 접속한다: 서로 다른 네트워크 간에도 연결됨. " +
                 "끄면 기존 LAN 직접 IP:포트 접속으로 폴백(오프라인 시연용).")]
        [SerializeField] private bool _useRelay = true;
        [Tooltip("릴레이 방의 최대 '참가자' 수(호스트 제외). LobbyManager.MaxPlayers - 1 과 맞출 것.")]
        [SerializeField, Range(1, 15)] private int _relayMaxConnections = 7;

        [Header("LAN 접속 기본값 (_useRelay 꺼짐일 때)")]
        [Tooltip("방 만들기 기본 UDP 포트. 사용 중이면 다음 포트를 자동 탐색한다(ActivePort 에 확정).")]
        [SerializeField] private ushort _defaultPort = 7777;

        public ushort DefaultPort => _defaultPort;
        /// <summary>릴레이 모드 여부(UI 분기: 참가 코드 입력 vs IP:포트 입력).</summary>
        public bool UseRelay => _useRelay;
        /// <summary>호스트가 발급받은 릴레이 참가 코드(참가자에게 전달할 값). 릴레이 모드 전용.</summary>
        public string JoinCode { get; private set; } = "";
        /// <summary>릴레이 비동기 절차(로그인/할당/참가) 진행 중 여부. UI 는 이 동안 버튼을 잠근다.</summary>
        public bool RelayBusy { get; private set; }

        // 비동기 실패 사유(1회 소비). async void 경로의 오류를 UI 로 전달하는 통로.
        private string _lastAsyncError;
        /// <summary>마지막 비동기 오류를 읽고 비운다(없으면 null). UI 가 매 프레임 폴링.</summary>
        public string ConsumeLastError()
        {
            string e = _lastAsyncError;
            _lastAsyncError = null;
            return e;
        }

        public static ConnectionService Instance { get; private set; }

        /// <summary>호스팅에 실제 사용된 UDP 포트(자동 대체 결과 반영). 참가자가 입력할 포트.</summary>
        public ushort ActivePort { get; private set; }
        /// <summary>기본 포트가 사용 중이라 다른 포트로 대체됐는지(사용자에게 반드시 안내).</summary>
        public bool PortFallbackUsed { get; private set; }
        /// <summary>참가자에게 알려줄 대표 LAN 사설 IPv4. 탐색 실패 시 빈 문자열(루프백은 절대 안 씀).</summary>
        public string PrimaryLanIp { get; private set; } = "";
        /// <summary>가용 IPv4 후보 전체(VPN/가상 어댑터 등으로 여러 개일 수 있음 -> UI 가 함께 표시).</summary>
        public IReadOnlyList<string> LanIpCandidates => _lanIps;
        /// <summary>클라이언트가 마지막으로 접속을 시도한 대상("ip:port"). 대기방 표시용.</summary>
        public string JoinTarget { get; private set; } = "";

        /// <summary>호스트 화면에 표시할 접속 주소("ip:port").</summary>
        public string HostFullAddress =>
            string.IsNullOrEmpty(PrimaryLanIp) ? $"(IP 확인 필요):{ActivePort}" : $"{PrimaryLanIp}:{ActivePort}";

        private readonly List<string> _lanIps = new List<string>();
        private bool _logHooked;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Net] ConnectionService 가 씬에 두 개 이상 있습니다. 나중 것을 제거합니다.", this);
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            UnhookLogging();
        }

        // ============================ 방 만들기 / 참가 / 나가기 ============================

        /// <summary>
        /// 방 만들기 = 호스트 시작.
        /// 릴레이 모드: 비동기 절차를 시작하고 즉시 true 를 반환한다(진행 상태는 RelayBusy,
        /// 참가 코드는 JoinCode, 실패는 ConsumeLastError 로 전달). LAN 모드: 즉시 StartHost.
        /// </summary>
        public bool CreateRoom(ushort preferredPort, out string error)
        {
            if (RelayBusy) { error = "릴레이 접속 처리 중입니다. 잠시만 기다려 주세요."; return false; }
            if (!PrepareStart(out var nm, out error)) return false;

            // 신규 참가 정책(진행 중/정원 초과 거절)은 서버 접속 승인에서 검증한다.
            // StartHost "이전"에 설정해야 승인 경로가 활성화된다.
            nm.NetworkConfig.ConnectionApproval = true;
            nm.ConnectionApprovalCallback = HandleConnectionApproval;

            if (_useRelay)
            {
                CreateRelayRoomAsync(nm); // async void: 완료/실패는 상태 프로퍼티로 전달
                return true;
            }

            ushort port = FindFreeUdpPort(preferredPort);
            PortFallbackUsed = port != preferredPort;
            ActivePort = port;
            RefreshLanIps();

            ApplyConnectionData("0.0.0.0", listen: true, port);

            if (!nm.StartHost())
            {
                error = $"호스트 시작에 실패했습니다 (UDP 포트 {port}). 콘솔 로그를 확인하세요.";
                return false;
            }
            return true;
        }

        /// <summary>방 참가(릴레이) = 참가 코드로 클라이언트 시작. 비동기(RelayBusy/ConsumeLastError).</summary>
        public bool JoinRoomByCode(string joinCode, out string error)
        {
            if (RelayBusy) { error = "릴레이 접속 처리 중입니다. 잠시만 기다려 주세요."; return false; }
            if (!PrepareStart(out var nm, out error)) return false;

            // ★서버(CreateRoom)와 반드시 같은 값★ (NetworkConfig 해시 비교 -> 다르면 서버가 끊음)
            nm.NetworkConfig.ConnectionApproval = true;

            JoinTarget = $"코드 {joinCode}";
            JoinRelayRoomAsync(nm, joinCode);
            return true;
        }

        // ============================ Unity Relay (비동기 경로) ============================

        // UGS 초기화 + 익명 로그인(1회). 에디터 다중 인스턴스(MPPM 가상 플레이어)가 같은 익명
        // 프로필을 두고 충돌하지 않도록 프로세스별 프로필로 분리한다(익명 계정이라 부담 없음).
        private static async Task EnsureServicesAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                var options = new InitializationOptions();
                try
                {
                    int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                    options.SetProfile("p" + (pid % 100000));
                }
                catch { /* 프로필 미지원 환경이면 기본 프로필 사용 */ }
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        // 호스트: 할당 생성 -> 참가 코드 발급 -> 트랜스포트에 릴레이 데이터 주입 -> StartHost.
        private async void CreateRelayRoomAsync(NetworkManager nm)
        {
            RelayBusy = true;
            JoinCode = "";
            try
            {
                await EnsureServicesAsync();

                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(_relayMaxConnections);
                JoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                var transport = nm.GetComponent<UnityTransport>();
                transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));

                if (!nm.StartHost())
                    _lastAsyncError = "호스트 시작에 실패했습니다. 콘솔 로그를 확인하세요.";
                else
                    Debug.Log($"[Net] Relay 호스트 시작, 참가 코드 {JoinCode}");
            }
            catch (System.Exception e)
            {
                _lastAsyncError = "릴레이 방 생성에 실패했습니다: " + Summarize(e);
                Debug.LogException(e);
            }
            finally { RelayBusy = false; }
        }

        // 참가자: 코드로 할당 참가 -> 트랜스포트에 릴레이 데이터 주입 -> StartClient.
        private async void JoinRelayRoomAsync(NetworkManager nm, string joinCode)
        {
            RelayBusy = true;
            try
            {
                await EnsureServicesAsync();

                JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

                var transport = nm.GetComponent<UnityTransport>();
                transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));

                if (!nm.StartClient())
                    _lastAsyncError = "클라이언트 시작에 실패했습니다. 콘솔 로그를 확인하세요.";
            }
            catch (System.Exception e)
            {
                _lastAsyncError = "코드로 참가하지 못했습니다: 코드가 맞는지, 방이 아직 열려 있는지 확인하세요.";
                Debug.LogException(e);
            }
            finally { RelayBusy = false; }
        }

        // 서비스 예외를 사용자 메시지로 축약(전체 스택은 콘솔 로그로).
        private static string Summarize(System.Exception e)
        {
            string m = e.Message ?? "";
            return m.Length > 120 ? m.Substring(0, 120) + "..." : m;
        }

        /// <summary>방 참가 = 클라이언트 시작. ip 는 호출 전에 IPv4 검증을 마친 값이어야 한다.</summary>
        public bool JoinRoom(string ip, ushort port, out string error)
        {
            if (!PrepareStart(out var nm, out error)) return false;

            // ★서버(CreateRoom)와 반드시 같은 값이어야 한다★
            // NGO 는 접속 시 NetworkConfig 해시(ConnectionApproval 플래그 포함)를 비교해
            // 다르면 서버가 연결을 끊는다("disconnected by server"). 승인 판정 자체는
            // 서버 콜백에서만 하므로 클라는 플래그만 맞추면 된다.
            nm.NetworkConfig.ConnectionApproval = true;

            JoinTarget = $"{ip}:{port}";
            ApplyConnectionData(ip, listen: false, port);

            if (!nm.StartClient())
            {
                error = "클라이언트 시작에 실패했습니다. 콘솔 로그를 확인하세요.";
                return false;
            }
            return true;
        }

        /// <summary>방 나가기/방 닫기. 유일한 연결 종료 경로(대기방 복귀 시엔 절대 호출하지 않는다).</summary>
        public void Leave()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && (nm.IsClient || nm.IsServer)) nm.Shutdown();
        }

        // 시작 공통 가드: 없음/종료 처리 중/이미 접속 중이면 시작하지 않는다(중복 클릭 방지 포함).
        private bool PrepareStart(out NetworkManager nm, out string error)
        {
            error = null;
            nm = NetworkManager.Singleton;
            if (nm == null) { error = "NetworkManager 가 씬에 없습니다."; return false; }
            if (nm.ShutdownInProgress) { error = "이전 세션 종료 처리 중입니다. 잠시 후 다시 시도하세요."; return false; }
            if (nm.IsClient || nm.IsServer) { error = "이미 접속 중입니다."; return false; }
            HookLogging(nm);
            return true;
        }

        // ============================ 접속 승인 (서버) ============================
        // 게임 시작 후(late join) 정책: LOBBY 에서만 참가 허용. PREP 이후 신규 접속은
        // 현재 상태 동기화(누적 구조물 소유 통계·라운드 데이터)가 불완전하므로 안전하게 거절한다.
        private void HandleConnectionApproval(NetworkManager.ConnectionApprovalRequest request,
                                              NetworkManager.ConnectionApprovalResponse response)
        {
            // 기존(승인 비활성 시절)과 동일하게 기본 플레이어 프리팹을 자동 스폰.
            response.CreatePlayerObject = true;
            response.PlayerPrefabHash = null;
            response.Position = null;
            response.Rotation = null;
            response.Pending = false;
            response.Approved = true;

            // 호스트 자신의 접속(서버 시작 직후, 씬 NetworkObject 스폰 전)은 항상 승인.
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            var mm = MatchManager.Instance;
            if (mm != null && mm.IsSpawned && mm.CurrentPhase != MatchPhase.Lobby)
            {
                response.Approved = false;
                response.Reason = "게임이 이미 진행 중입니다.";
                return;
            }

            var lobby = LobbyManager.Instance;
            if (lobby != null && lobby.IsSpawned && nm.ConnectedClients.Count >= lobby.MaxPlayers)
            {
                response.Approved = false;
                response.Reason = "방이 가득 찼습니다.";
            }
        }

        // ============================ 트랜스포트 ============================

        /// <summary>
        /// preferred 부터 UDP 바인딩을 실제로 시도해 사용 가능한 첫 포트를 찾는다.
        /// probe 소켓은 즉시 닫으므로 곧바로 이어지는 트랜스포트 바인딩이 그 포트를 쓸 수 있다.
        /// 전부 사용 중이면 preferred 를 그대로 반환한다(StartHost 실패 메시지가 안내).
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

        /// <summary>
        /// UnityTransport 전용 설정을 이 메서드 하나에 격리한다.
        /// [Relay/Steam 전환 지점] 트랜스포트를 갈아끼울 때 이 내부만 교체하면 나머지는 그대로.
        /// </summary>
        private static void ApplyConnectionData(string address, bool listen, ushort port)
        {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[Net] UnityTransport 컴포넌트를 찾을 수 없습니다. NetworkManager 에 트랜스포트가 붙어있는지 확인하세요.");
                return;
            }
            // 서버/호스트: listenAddress 0.0.0.0 -> 모든 인터페이스에서 접속 수신(로컬+LAN).
            // 클라: 대상 IP(address)로만 접속. listenAddress 는 클라에서 사용되지 않으므로 null.
            transport.SetConnectionData(address, port, listen ? "0.0.0.0" : null);
        }

        // ============================ LAN IP 탐색 ============================

        /// <summary>
        /// 표시용 LAN IPv4 후보 수집. 루프백(127.*)·APIPA(169.254.*) 제외, 사설 대역
        /// (192.168 > 10 > 172.16~31) 우선 정렬. 실패해도 예외 없이 빈 목록(UI 가 안내).
        /// </summary>
        private void RefreshLanIps()
        {
            _lanIps.Clear();
            PrimaryLanIp = "";
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var b = ua.Address.GetAddressBytes();
                        if (b[0] == 127) continue;                // 루프백: 다른 PC 접속용으로 안내 금지
                        if (b[0] == 169 && b[1] == 254) continue; // APIPA(주소 미할당)
                        string s = ua.Address.ToString();
                        if (!_lanIps.Contains(s)) _lanIps.Add(s);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Net] LAN IP 탐색 실패: {e.Message}");
            }
            _lanIps.Sort((a, b) => PrivateRank(a).CompareTo(PrivateRank(b)));
            if (_lanIps.Count > 0) PrimaryLanIp = _lanIps[0];
        }

        // 사설 대역 우선순위(가정용 공유기에서 흔한 순). VPN/가상 어댑터 IP 를 뒤로 보낸다.
        private static int PrivateRank(string ip)
        {
            if (ip.StartsWith("192.168.")) return 0;
            if (ip.StartsWith("10.")) return 1;
            if (ip.StartsWith("172."))
            {
                int dot = ip.IndexOf('.', 4);
                if (dot > 4 && int.TryParse(ip.Substring(4, dot - 4), out int second) &&
                    second >= 16 && second <= 31)
                    return 2;
            }
            return 3;
        }

        // ============================ 진단 로그 ============================
        private void HookLogging(NetworkManager nm)
        {
            if (_logHooked) return;
            nm.OnConnectionEvent += HandleConnectionEvent;
            nm.OnServerStarted   += HandleServerStarted;
            nm.OnServerStopped   += HandleServerStopped;
            nm.OnClientStopped   += HandleClientStopped;
            _logHooked = true;
        }

        private void UnhookLogging()
        {
            if (!_logHooked) return;
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.OnConnectionEvent -= HandleConnectionEvent;
                nm.OnServerStarted   -= HandleServerStarted;
                nm.OnServerStopped   -= HandleServerStopped;
                nm.OnClientStopped   -= HandleClientStopped;
            }
            _logHooked = false;
        }

        private void HandleConnectionEvent(NetworkManager nm, ConnectionEventData data) =>
            Debug.Log($"[Net] {data.EventType} clientId={data.ClientId}");
        private void HandleServerStarted() =>
            Debug.Log($"[Net] Server started, listening on 0.0.0.0:{ActivePort} (표시 주소 {HostFullAddress})");
        private void HandleServerStopped(bool wasHost) => Debug.Log($"[Net] Server stopped (wasHost={wasHost}).");
        private void HandleClientStopped(bool wasHost)
        {
            var nm = NetworkManager.Singleton;
            string reason = nm != null ? nm.DisconnectReason : "";
            Debug.Log($"[Net] Client stopped (wasHost={wasHost}) reason='{reason}'");
        }
    }
}
