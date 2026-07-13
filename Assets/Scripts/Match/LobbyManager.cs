using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RouletteParty.Match
{
    /// <summary>
    /// 대기방 참가자 한 줄(전 클라 복제, NetworkList 항목). 서버만 쓴다 — 클라는 RPC 로 변경을 요청.
    /// 닉네임은 네트워크 전송에 적합한 고정 길이 문자열(FixedString64Bytes = 한글 12자 여유).
    /// </summary>
    public struct LobbyPlayerState : INetworkSerializable, System.IEquatable<LobbyPlayerState>
    {
        public ulong ClientId;
        public FixedString64Bytes Name;
        public bool Ready;
        public bool IsHost;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref ClientId);
            s.SerializeValue(ref Name);
            s.SerializeValue(ref Ready);
            s.SerializeValue(ref IsHost);
        }

        public bool Equals(LobbyPlayerState o) =>
            ClientId == o.ClientId && Name.Equals(o.Name) && Ready == o.Ready && IsHost == o.IsHost;

        public override bool Equals(object obj) => obj is LobbyPlayerState o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + ClientId.GetHashCode();
                h = h * 31 + Name.GetHashCode();
                h = h * 31 + (Ready ? 1 : 0);
                h = h * 31 + (IsHost ? 1 : 0);
                return h;
            }
        }
    }

    /// <summary>
    /// 대기방(로비) 서버 권위 상태 관리자.
    /// 씬 배치: MatchManager 가 붙은 씬 NetworkObject 에 컴포넌트로 추가한다
    /// (한 NetworkObject 에 NetworkBehaviour 여러 개는 표준 구성. 스폰 수명을 매치와 공유).
    ///
    ///  - 참가자 목록(ClientId/닉네임/준비/호스트 여부)을 NetworkList 로 복제. 서버만 쓴다.
    ///  - 클라는 자기 닉네임/준비 상태만 RPC 로 요청(다른 플레이어 상태 변경 불가).
    ///  - 게임 시작: 호스트 RPC -> 서버가 (LOBBY/호스트/최소 인원/전원 준비) 재검증
    ///    -> MatchManager.StartMatchFromLobby() 로 기존 FSM(PREP) 진입.
    ///  - LOBBY 재진입(결과 후 복귀) 감지 시 준비 상태만 초기화(목록/닉네임 유지).
    ///
    /// 이 컴포넌트가 씬에 없으면 MatchManager 는 기존처럼 lobbyDuration 후 자동 시작한다
    /// (WaitsForStart == false — 대기방 없는 구성도 안전하게 동작).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class LobbyManager : NetworkBehaviour
    {
        [Header("대기방 규칙")]
        [Tooltip("켜면 LOBBY 는 호스트의 '게임 시작'으로만 PREP 로 진행된다. 끄면 기존처럼 lobbyDuration 후 자동 시작(솔로 테스트용).")]
        [SerializeField] private bool _requireManualStart = true;
        [Tooltip("게임 시작 최소 인원.")]
        [SerializeField] private int _minPlayers = 2;
        [Tooltip("방 정원. 초과 접속은 접속 승인(ConnectionService) 단계에서 거절된다.")]
        [SerializeField] private int _maxPlayers = 4;

        private NetworkList<LobbyPlayerState> _players = new NetworkList<LobbyPlayerState>();

        // 서버: LOBBY 재진입 감지용(결과 후 복귀 시 준비 초기화)
        private MatchPhase _lastSeenPhase = (MatchPhase)byte.MaxValue;

        public static LobbyManager Instance { get; private set; }

        /// <summary>MatchManager FSM 게이트: LOBBY 를 수동 시작으로 붙잡아야 하는가.</summary>
        public static bool WaitsForStart =>
            Instance != null && Instance.IsSpawned && Instance._requireManualStart;

        /// <summary>대기방 참가자 목록(전 클라 read). UI(LobbyUI)가 그대로 그린다.</summary>
        public NetworkList<LobbyPlayerState> Players => _players;
        public int MinPlayers => _minPlayers;
        public int MaxPlayers => _maxPlayers;

        /// <summary>대기방 닉네임(없으면 null). PlayerPalette.NameFor 가 폴백과 함께 사용.</summary>
        public string NameOf(ulong clientId)
        {
            if (!IsSpawned) return null;
            for (int i = 0; i < _players.Count; i++)
                if (_players[i].ClientId == clientId)
                    return _players[i].Name.ToString();
            return null;
        }

        // ============================ Lifecycle ============================
        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (!IsServer) return;

            NetworkManager.OnClientConnectedCallback  += HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            foreach (var c in NetworkManager.ConnectedClientsList)
                AddPlayer(c.ClientId);
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            if (!IsServer) return;
            NetworkManager.OnClientConnectedCallback  -= HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        // 서버: 페이즈 변화를 관찰해 LOBBY 재진입 시 준비 상태를 초기화한다.
        // (MatchManager 가 LobbyManager 를 호출하지 않는다 — 의존 방향은 Lobby -> Match 한쪽만.)
        private void Update()
        {
            if (!IsServer || !IsSpawned) return;
            var mm = MatchManager.Instance;
            if (mm == null || !mm.IsSpawned) return;

            MatchPhase p = mm.CurrentPhase;
            if (p == _lastSeenPhase) return;
            _lastSeenPhase = p;
            if (p == MatchPhase.Lobby) ResetReady();
        }

        // ============================ 서버 훅(연결) ============================
        private void HandleClientConnected(ulong clientId) => AddPlayer(clientId);
        private void HandleClientDisconnected(ulong clientId) => RemovePlayer(clientId);

        private void AddPlayer(ulong clientId)
        {
            for (int i = 0; i < _players.Count; i++)
                if (_players[i].ClientId == clientId) return; // 중복 방지

            _players.Add(new LobbyPlayerState
            {
                ClientId = clientId,
                Name = new FixedString64Bytes($"P{clientId + 1}"), // 닉네임 RPC 도착 전 기본 이름
                Ready = false,
                IsHost = NetworkManager.IsHost && clientId == NetworkManager.LocalClientId,
            });
        }

        private void RemovePlayer(ulong clientId)
        {
            for (int i = 0; i < _players.Count; i++)
                if (_players[i].ClientId == clientId) { _players.RemoveAt(i); return; }
        }

        /// <summary>직전 게임의 준비 상태만 초기화(목록/닉네임 유지).</summary>
        private void ResetReady()
        {
            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                if (!p.Ready) continue;
                p.Ready = false;
                _players[i] = p;
            }
        }

        // ============================ 클라 -> 서버 RPC ============================

        private const int NAME_MAX = 12; // 표시 길이 제한(FixedString64Bytes 용량 안쪽)

        /// <summary>접속 직후 클라가 자기 닉네임을 보고. 서버가 정제(공백/길이/중복)해 목록에 반영.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SetPlayerNameServerRpc(FixedString64Bytes rawName, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;

            string name = rawName.ToString().Trim();
            if (name.Length == 0) name = $"P{sender + 1}";
            if (name.Length > NAME_MAX) name = name.Substring(0, NAME_MAX);

            // 같은 닉네임이 이미 있으면 "#클라번호"를 붙여 최소한 구분되게 한다.
            for (int i = 0; i < _players.Count; i++)
                if (_players[i].ClientId != sender && _players[i].Name.ToString() == name)
                {
                    name = $"{name}#{sender + 1}";
                    break;
                }

            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].ClientId != sender) continue;
                var p = _players[i];
                p.Name = new FixedString64Bytes(name);
                _players[i] = p;
                return;
            }
        }

        /// <summary>본인 준비/준비 취소. sender 기준으로만 갱신(타인 상태 변경 불가).</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SetReadyServerRpc(bool ready, RpcParams rpcParams = default)
        {
            var mm = MatchManager.Instance;
            if (mm == null || !mm.IsSpawned || mm.CurrentPhase != MatchPhase.Lobby) return;

            ulong sender = rpcParams.Receive.SenderClientId;
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].ClientId != sender) continue;
                var p = _players[i];
                if (p.Ready == ready) return;
                p.Ready = ready;
                _players[i] = p;
                return;
            }
        }

        /// <summary>
        /// 게임 시작 요청. UI 가 버튼을 비활성화하지만, 변조된 요청 대비로 서버가
        /// 조건(LOBBY / 요청자가 호스트 / 최소 인원 / 전원 준비)을 전부 재검증한다.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void StartGameServerRpc(RpcParams rpcParams = default)
        {
            var mm = MatchManager.Instance;
            if (mm == null || !mm.IsSpawned || mm.CurrentPhase != MatchPhase.Lobby) return;

            ulong sender = rpcParams.Receive.SenderClientId;
            bool senderIsHost = false;
            int count = _players.Count;
            bool allReady = count > 0;
            for (int i = 0; i < count; i++)
            {
                var p = _players[i];
                if (p.ClientId == sender && p.IsHost) senderIsHost = true;
                if (!p.Ready) allReady = false;
            }

            if (!senderIsHost)
            {
                Debug.LogWarning($"[Lobby] 비호스트(clientId={sender})의 게임 시작 요청 거절");
                return;
            }
            if (count < _minPlayers || !allReady) return;

            Debug.Log($"[Lobby] 게임 시작 (인원 {count})");
            mm.StartMatchFromLobby();
        }
    }
}
