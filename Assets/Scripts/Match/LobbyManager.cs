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

        [Header("매치 설정 (호스트가 대기방에서 조절, 전 클라 표시)")]
        [Tooltip("준비(구조물 설치) 시간 프리셋(초).")]
        [SerializeField] private float[] _prepPresets = { 30f, 60f, 90f, 120f };
        [Tooltip("등반(플레이) 시간 프리셋(초).")]
        [SerializeField] private float[] _playPresets = { 120f, 180f, 240f, 300f };

        [Header("씬 분리 (대기방 씬 전용)")]
        [Tooltip("게임 시작 시 NGO 씬 동기화로 로드할 게임 씬 이름(빌드 설정 등록 필수). 같은 씬에 MatchManager 가 있으면(레거시 단일 씬) 씬 전환 없이 기존 FSM 으로 시작한다.")]
        [SerializeField] private string _gameSceneName = "MainScene";
        [Tooltip("대기방 스테이지 기준점(플레이어 줄 세우기). 게임 씬에는 MatchManager 가 배치를 담당하므로 대기방 씬에서만 배선한다. 비우면 배치 생략.")]
        [SerializeField] private Transform _stagePoint;
        [Tooltip("스테이지 줄 세우기 간격(m).")]
        [SerializeField] private float _stageSpacing = 1.2f;

        private NetworkList<LobbyPlayerState> _players = new NetworkList<LobbyPlayerState>();

        // 페이즈 시간 설정(서버 write, 전 클라 read - 대기방 UI 표시). 호스트 선택은 PlayerPrefs 로 영속.
        private NetworkVariable<float> _prepSeconds = new NetworkVariable<float>(60f);
        private NetworkVariable<float> _playSeconds = new NetworkVariable<float>(180f);
        private const string KEY_PREP = "match.prepSeconds";
        private const string KEY_PLAY = "match.playSeconds";

        /// <summary>현재 설정된 준비 페이즈 시간(초, 전 클라 표시용).</summary>
        public float PrepSeconds => _prepSeconds.Value;
        /// <summary>현재 설정된 등반 페이즈 시간(초, 전 클라 표시용).</summary>
        public float PlaySeconds => _playSeconds.Value;
        public float[] PrepPresets => _prepPresets;
        public float[] PlayPresets => _playPresets;

        // 게임 씬에는 LobbyManager 가 없으므로(씬 분리) 마지막 대기방 닉네임을 정적으로 남긴다.
        // 모든 피어가 자기 복제본(NetworkList)에서 스냅샷을 뜨므로 전 피어 동일 - PlayerPalette 폴백.
        private static readonly System.Collections.Generic.Dictionary<ulong, string> s_lastNames
            = new System.Collections.Generic.Dictionary<ulong, string>();

        /// <summary>마지막으로 알려진 대기방 닉네임(없으면 null). 게임 씬에서 이름표/점수판이 사용.</summary>
        public static string SnapshotNameOf(ulong clientId) =>
            s_lastNames.TryGetValue(clientId, out string n) ? n : null;

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
            _players.OnListChanged += HandleListChanged; // 닉네임 스냅샷 동기화(전 피어)
            SyncNameSnapshot();
            if (!IsServer) return;

            // 호스트가 마지막으로 고른 페이즈 시간 복원 -> 서버 소비 값(MatchSettings)에 반영.
            _prepSeconds.Value = PlayerPrefs.GetFloat(KEY_PREP, _prepSeconds.Value);
            _playSeconds.Value = PlayerPrefs.GetFloat(KEY_PLAY, _playSeconds.Value);
            ApplyMatchSettings();

            NetworkManager.OnClientConnectedCallback  += HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            int i = 0, n = NetworkManager.ConnectedClientsList.Count;
            foreach (var c in NetworkManager.ConnectedClientsList)
            {
                AddPlayer(c.ClientId);
                PlaceOnStage(c.ClientId, i++, n); // 게임에서 복귀 시 스테이지 재배치(씬 분리)
            }
        }

        public override void OnNetworkDespawn()
        {
            SyncNameSnapshot(); // 게임 씬 전환 직전 마지막 닉네임 보존
            _players.OnListChanged -= HandleListChanged;
            if (Instance == this) Instance = null;
            if (!IsServer) return;
            NetworkManager.OnClientConnectedCallback  -= HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        private void HandleListChanged(NetworkListEvent<LobbyPlayerState> _) => SyncNameSnapshot();

        private void SyncNameSnapshot()
        {
            for (int i = 0; i < _players.Count; i++)
                s_lastNames[_players[i].ClientId] = _players[i].Name.ToString();
        }

        // ---- 스테이지 배치(씬 분리): 대기방 씬에는 MatchManager 가 없으므로 로비가 담당 ----
        private void PlaceOnStage(ulong clientId, int index, int total)
        {
            if (_stagePoint == null) return;
            Vector3 pos = _stagePoint.position
                + _stagePoint.forward * ((index - (Mathf.Max(1, total) - 1) * 0.5f) * _stageSpacing)
                + Vector3.up * 1.2f;
            TeleportPlayerRpc(pos, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void TeleportPlayerRpc(Vector3 pos, RpcParams rpcParams = default)
        {
            var po = NetworkManager.LocalClient != null ? NetworkManager.LocalClient.PlayerObject : null;
            var pc = po != null ? po.GetComponent<RouletteParty.Net.PlayerController>() : null;
            if (pc != null) pc.TeleportTo(pos);
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
        private void HandleClientConnected(ulong clientId)
        {
            AddPlayer(clientId);
            PlaceOnStage(clientId, _players.Count - 1, Mathf.Max(1, _players.Count));
        }
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

        // 서버 소비 값 반영: MatchManager(게임 씬)가 PREP/PLAY 시간으로 사용한다.
        private void ApplyMatchSettings()
        {
            MatchSettings.PrepSeconds = _prepSeconds.Value;
            MatchSettings.PlaySeconds = _playSeconds.Value;
        }

        /// <summary>
        /// 호스트의 페이즈 시간 변경(대기방에서만). 서버가 호스트 여부/범위를 재검증하고
        /// 선택을 PlayerPrefs 로 영속화한다(호스트 = 서버라 서버 저장 = 호스트 저장).
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SetPhaseTimingServerRpc(float prepSeconds, float playSeconds, RpcParams rpcParams = default)
        {
            var mm = MatchManager.Instance;
            if (mm != null && mm.IsSpawned && mm.CurrentPhase != MatchPhase.Lobby) return; // 게임 중 변경 금지

            ulong sender = rpcParams.Receive.SenderClientId;
            bool senderIsHost = false;
            for (int i = 0; i < _players.Count; i++)
                if (_players[i].ClientId == sender && _players[i].IsHost) { senderIsHost = true; break; }
            if (!senderIsHost) return;

            _prepSeconds.Value = Mathf.Clamp(prepSeconds, 10f, 600f);
            _playSeconds.Value = Mathf.Clamp(playSeconds, 30f, 900f);
            PlayerPrefs.SetFloat(KEY_PREP, _prepSeconds.Value);
            PlayerPrefs.SetFloat(KEY_PLAY, _playSeconds.Value);
            ApplyMatchSettings();
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
            // 씬 분리: 대기방 씬에는 MatchManager 가 없다(없음 = 대기방 상태로 취급).
            var mm = MatchManager.Instance;
            if (mm != null && mm.IsSpawned && mm.CurrentPhase != MatchPhase.Lobby) return;

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
            // 씬 분리: 대기방 씬에는 MatchManager 가 없다(없음 = 대기방 상태로 취급).
            var mm = MatchManager.Instance;
            if (mm != null && mm.IsSpawned && mm.CurrentPhase != MatchPhase.Lobby) return;

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

            // 레거시 단일 씬(같은 씬에 MatchManager): 씬 전환 없이 기존 FSM 진입.
            if (mm != null && mm.IsSpawned) { mm.StartMatchFromLobby(); return; }

            // 씬 분리: 게임 씬 로드(NGO 씬 동기화 - 전 클라 함께 전환, 대기방 씬 오브젝트는 despawn).
            var status = NetworkManager.SceneManager.LoadScene(_gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
                Debug.LogError($"[Lobby] 게임 씬 로드 실패: {status} (빌드 설정에 '{_gameSceneName}' 등록 확인)");
        }
    }
}
