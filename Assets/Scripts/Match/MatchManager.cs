using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;
using RouletteParty.Map; // ClimbMapGenerator
using RouletteParty.Net; // PlayerController (소유자 텔레포트 위임)
using RouletteParty.UI;  // ImguiScale (디버그 OnGUI 해상도 스케일링)

namespace RouletteParty.Match
{
    /// <summary>
    /// 클라이밍 전환(docs/클라이밍_전환_명세서.md) 호스트 권위 매치 FSM.
    /// 씬 배치 NetworkObject(서버 권위)에 부착. 서버 시작 시 자동 스폰.
    ///
    ///   LOBBY -> [PREP -> PLAY -> HIGHLIGHT] x3 -> RESULT -> (LOBBY 로 루프)
    ///
    /// 핵심 규칙:
    ///  (a) 매 라운드 전 PREP: 플레이어가 구조물 설치(라운드별 지급: 보임 3/2/1, 안보임 1/2/3).
    ///      설치물은 라운드 사이에 지우지 않고 3라운드 누적, 매치 리셋(LOBBY)에만 전체 despawn.
    ///  (b) 맵: 매치마다 시드(NetworkVariable MapSeed) 복제 -> 전 피어가 ClimbMapGenerator 로
    ///      동일한 레인 맵(시작 섬 + 대각선 발판 길)을 로컬 생성.
    ///  (c) 탈락: 체력 시스템 없음. 낙하 거리 2규칙(서버 판정, 둘 다 시리얼라이즈드):
    ///      ① 공중 낙하 거리 >= _lethalAirFall -> 즉시 탈락(서버가 위치 샘플링으로 직접 추적,
    ///         시작 섬 밖 허공 추락도 이 규칙이 잡는다).
    ///      ② 낙하 후 착지, 낙하 거리 >= _lethalLandFall -> 탈락(소유자 착지 보고 + 서버 판정).
    ///  (d) 점수: [임시 — 점수 개편 예정] 라운드 종료 시점 높이 / mapHeight x heightScoreMax
    ///      + 순위 보너스. 3라운드 누적. 공식은 MatchScoring.RoundScore 한 곳에만 존재.
    ///
    /// OWNER-AUTHORITATIVE MOVEMENT CONTRACT (기존 유지):
    ///  호스트는 플레이어 transform 을 READ 만 한다. 이동/배치는 소유 클라에게 RPC 로 위임.
    /// TIMER MODEL (기존 유지): 페이즈 종료 절대시각(서버 클럭)을 NetworkVariable 로 1회 복제.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class MatchManager : NetworkBehaviour
    {
        // ============================ 튜닝 (전부 인스펙터) ============================
        [Header("Test tuning")]
        [Tooltip("켜면 모든 페이즈 시간이 fastMultiplier 배로 줄어든다(빠른 테스트).")]
        [SerializeField] private bool fastMode = false;
        [SerializeField, Range(0.05f, 1f)] private float fastMultiplier = 0.25f;
        [Tooltip("좌상단 디버그 패널 표시(개발 전용 - 배포 화면에는 끔).")]
        [SerializeField] private bool showDebugGui = false;

        [Header("페이즈 시간(초)")]
        [SerializeField] private float _lobbyDuration     = 3f;
        [SerializeField] private float _prepDuration      = 60f;
        [SerializeField] private float _playDuration      = 180f;
        [SerializeField] private float _highlightDuration = 8f;
        [Tooltip("하이라이트 종료 후 다음 라운드 PREP 시작 전 대기 시간(라운드 사이 완충 구간, 마지막 라운드 뒤에는 적용 안 됨).")]
        [SerializeField] private float _intermissionDuration = 5f;
        [SerializeField] private float _resultDuration    = 14f;
        [Tooltip("첫 정상 도달자가 나오면 잔여 타이머를 이 값으로 단축.")]
        [SerializeField] private float _finishGrace = 15f;

        [Header("씬 분리")]
        [Tooltip("결과 화면 종료 후 돌아갈 대기방 씬 이름(빌드 설정 등록 필수). 비우면 기존처럼 같은 씬의 LOBBY 페이즈로 복귀(레거시 단일 씬).")]
        [SerializeField] private string _lobbySceneName = "LobbyScene";

        [Header("스폰")]
        [Tooltip("출발 지점 플레이어 간 X 간격.")]
        [SerializeField] private float spawnSpacingX = 1.2f;
        [Tooltip("낙하 탈락 후 시작 섬 자동 부활까지의 지연(초). 관전이 잠깐 보였다가 복귀한다.")]
        [SerializeField] private float _respawnDelay = 1.5f;

        [Header("낙하 탈락 (서버 판정, 체력 시스템 없음)")]
        [Tooltip("탈락 규칙 ①: 공중 낙하 거리(최고점 - 현재 높이)가 이 값 이상이면 착지 여부와 무관하게 즉시 탈락. 시작 섬 밖 허공 추락도 이 규칙이 잡는다.")]
        [SerializeField] private float _lethalAirFall = 15f;
        [Tooltip("탈락 규칙 ②: 이 거리 이상 낙하한 뒤 착지하면 탈락. PlayerController.fallReportMin(착지 보고 최소 낙하)보다 커야 한다.")]
        [SerializeField] private float _lethalLandFall = 7f;

        [Header("구조물 지급/설치")]
        [Tooltip("라운드별 보이는 구조물 지급 개수(인덱스 0 = 라운드 1). 이월 없음.")]
        [SerializeField] private int[] _visibleAllowance = { 3, 2, 1 };
        [Tooltip("라운드별 보이지 않는 구조물 지급 개수(인덱스 0 = 라운드 1). 이월 없음.")]
        [SerializeField] private int[] _invisibleAllowance = { 1, 2, 3 };
        [Tooltip("설치물 겹침 검사의 관통 허용 오차(m). 접촉(면 맞대기)은 허용하고 이 깊이를 넘는 관통만 거부 -> 구조물 위에 쌓기 가능. 값이 클수록 살짝 겹치는 설치까지 허용된다.")]
        [SerializeField] private float _overlapTolerance = 0.05f;
        [Tooltip("구조물 프리팹(NetworkObject + Structure). NetworkPrefabs 리스트 등록 필수.")]
        [SerializeField] private GameObject _wallPrefab;
        [SerializeField] private GameObject _cylinderPrefab;
        [FormerlySerializedAs("_ghostPrefab")]
        [SerializeField] private GameObject _invisiblePrefab;
        [SerializeField] private GameObject _treePrefab;
        [SerializeField] private GameObject _rockPrefab;

        [Header("점수")]
        [Tooltip("라운드 점수 설정(공식은 MatchScoring.RoundScore 한 곳에만 존재).")]
        [SerializeField] private ScoringConfig _scoring = new ScoringConfig();

        // ============================ 복제 상태(서버 write) ============================
        private NetworkVariable<MatchPhase> _phase        = new NetworkVariable<MatchPhase>(MatchPhase.Lobby);
        private NetworkVariable<int>        _round        = new NetworkVariable<int>(0);
        private NetworkVariable<double>     _phaseEndTime = new NetworkVariable<double>(0d);
        private NetworkVariable<int>        _aliveCount   = new NetworkVariable<int>(0);
        private NetworkVariable<ulong>      _roundWinner  = new NetworkVariable<ulong>(ulong.MaxValue);
        private NetworkVariable<ulong>      _matchWinner  = new NetworkVariable<ulong>(ulong.MaxValue);
        /// <summary>맵 랜덤 생성 시드(매치당 1회 갱신). 전 피어가 이 시드로 동일 맵을 로컬 생성.</summary>
        public NetworkVariable<int> MapSeed = new NetworkVariable<int>(0);
        // 라운드 통계(하이라이트 카드용). 서버가 라운드 종료 시 1회 채운다.
        private NetworkVariable<RoundStats> _roundStats = new NetworkVariable<RoundStats>(RoundStats.Empty);

        // 라운드별 순위 행(클라 점수판/하이라이트/결과 UI 데이터)
        private NetworkList<RoundResult> _results = new NetworkList<RoundResult>();

        // ============================ 서버 전용 상태 ============================
        private readonly Dictionary<ulong, PlayerRuntime> _players    = new Dictionary<ulong, PlayerRuntime>();
        private readonly Dictionary<ulong, int>           _totalScore = new Dictionary<ulong, int>();
        private readonly System.Random _rng = new System.Random();
        private readonly MatchStatsTracker _stats = new MatchStatsTracker(); // 서버 전용 라운드 이벤트 로거
        private bool _graceApplied;

        // ---- 점수 수집(개편안) 서버 전용 상태: 라운드 중 사실만 기록, 계산은 EndPlayEvaluate 1회 ----
        private int[]  _chunkArrivals = System.Array.Empty<int>(); // 청크 k 에 도달한 인원 수(다음 도달자의 선착 순번)
        private float  _playRoundDuration; // 최초 설정된 이번 라운드 전체 시간(시간 점수 분모, finishGrace 단축 무시)
        private double _playStartTime;     // PLAY 시작 서버 시각(정상 도달 경과 시각의 기준)

        // 이번 PREP 에 사용한 설치 수(이월 없음 규칙: PREP 진입마다 리셋)
        private readonly Dictionary<ulong, int> _prepVisibleUsed   = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, int> _prepInvisibleUsed = new Dictionary<ulong, int>();

        // 매치 동안 스폰된 구조물 기록(겹침 검증·매치 종료 일괄 정리용)
        // 매치 리셋 시 전체 despawn 용 기록(겹침 검사는 Structure.OverlapScan 이 레지스트리로 수행).
        private struct StructRec { public NetworkObject No; }
        private readonly List<StructRec> _structures = new List<StructRec>();

        // ============================ 클라 접근점 ============================
        public static MatchManager Instance { get; private set; }
        public MatchPhase CurrentPhase => _phase.Value;
        public int    Round              => _round.Value;
        public int    AliveCount         => _aliveCount.Value;
        public ulong  RoundWinnerId      => _roundWinner.Value;
        public ulong  MatchWinnerId      => _matchWinner.Value;
        public double PhaseEndServerTime => _phaseEndTime.Value;
        public float PhaseRemaining =>
            (IsSpawned && NetworkManager != null)
                ? Mathf.Max(0f, (float)(_phaseEndTime.Value - NetworkManager.ServerTime.Time))
                : 0f;
        public NetworkList<RoundResult> Results => _results;
        /// <summary>가장 최근 라운드 통계(하이라이트 카드용). Round == 0 이면 아직 없음.</summary>
        public RoundStats RoundStats => _roundStats.Value;
        /// <summary>현재 라운드의 지급 개수(클라 UI 표시용).</summary>
        public int VisibleGrant   => GrantOf(_visibleAllowance, _round.Value);
        public int InvisibleGrant => GrantOf(_invisibleAllowance, _round.Value);
        /// <summary>설치물 겹침 검사의 관통 허용 오차(접촉 허용). 클라 블루프린트 예비검증(PrepClientUI)이 서버와 같은 값을 쓴다.</summary>
        public float OverlapTolerance => _overlapTolerance;

        /// <summary>내 설치 요청이 "숨겨진 함정과의 겹침"으로 거부됨(해당 클라에서만 발생).
        /// PrepClientUI 가 구독해 경고 문구를 표시한다.</summary>
        public static event System.Action PlacementDeniedByTrap;
        private static int GrantOf(int[] arr, int round)
        {
            if (arr == null || arr.Length == 0) return 0;
            int i = Mathf.Clamp(round - 1, 0, arr.Length - 1);
            return arr[i];
        }

        float MapHeight => ClimbMapGenerator.Instance != null ? ClimbMapGenerator.Instance.MapHeight : 50f;

        // ============================ Lifecycle ============================
        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (!IsServer) return;

            NetworkManager.OnClientConnectedCallback  += HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            foreach (var c in NetworkManager.ConnectedClientsList)
                EnsurePlayer(c.ClientId);

            EnterPhase(MatchPhase.Lobby);
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            if (!IsServer) return;
            NetworkManager.OnClientConnectedCallback  -= HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        private void HandleClientConnected(ulong clientId)
        {
            EnsurePlayer(clientId);
            // 대기방(LOBBY) 입장 시 시작 섬 위에 줄 세우기. 이동이 소유자 권위(ClientNetworkTransform)라
            // 접속 승인 응답의 Position 은 소유자 상태로 덮이므로, 검증된 TeleportOwner(RPC) 경로를 쓴다.
            if (_phase.Value == MatchPhase.Lobby)
            {
                int n = NetworkManager.ConnectedClientsList.Count;
                TeleportOwner(clientId, SpawnSlot(n - 1, Mathf.Max(1, n)));
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            _players.Remove(clientId);
            _totalScore.Remove(clientId);
            _prepVisibleUsed.Remove(clientId);
            _prepInvisibleUsed.Remove(clientId);
        }

        private void EnsurePlayer(ulong clientId)
        {
            if (!_players.ContainsKey(clientId))
                _players[clientId] = new PlayerRuntime { ClientId = clientId };
        }

        // ============================ Server tick ============================
        private void Update()
        {
            if (!IsServer || !IsSpawned) return;

            double now = NetworkManager.ServerTime.Time;

            if (_phase.Value == MatchPhase.Play) { SamplePlayers(now); ProcessRespawns(now); }
            else                                 RescueFallenPlayers();

            if (now >= _phaseEndTime.Value)
            {
                // 대기방(LobbyManager)이 씬에 있으면 LOBBY 는 타이머로 넘어가지 않는다:
                // 호스트의 게임 시작(LobbyManager.StartGameServerRpc -> StartMatchFromLobby)만이
                // PREP 로 보낸다. LobbyManager 가 없으면 기존처럼 자동 진행(우아한 성능 저하).
                if (_phase.Value != MatchPhase.Lobby || !LobbyManager.WaitsForStart) { AdvancePhase(); return; }
            }

            // (전원 탈락 조기 종료 규칙은 자동 부활 도입으로 폐지 — 탈락은 일시 상태다.)
        }

        private float BaseDuration(MatchPhase p)
        {
            switch (p)
            {
                case MatchPhase.Lobby:     return _lobbyDuration;
                case MatchPhase.Prep:      return _prepDuration;
                case MatchPhase.Play:      return _playDuration;
                case MatchPhase.Highlight: return _highlightDuration;
                case MatchPhase.Intermission: return _intermissionDuration;
                case MatchPhase.Result:    return _resultDuration;
                default:                   return 3f;
            }
        }

        private float EffectiveDuration(MatchPhase p) =>
            BaseDuration(p) * (fastMode ? fastMultiplier : 1f);

        // ============================ FSM ============================
        private void EnterPhase(MatchPhase p)
        {
            _phase.Value = p;
            _phaseEndTime.Value = NetworkManager.ServerTime.Time + EffectiveDuration(p);
            _pendingRespawn.Clear(); // 페이즈 경계에서는 BeginPrep/BeginPlay 가 전원 복귀를 처리한다

            switch (p)
            {
                case MatchPhase.Lobby:     ResetMatch(); break;
                case MatchPhase.Prep:      BeginPrep();  break;
                case MatchPhase.Play:      BeginPlay();  break;
                case MatchPhase.Highlight: EndPlayEvaluate(); break;
                case MatchPhase.Intermission: break; // 대기만, 별도 로직 없음
                case MatchPhase.Result:    ComputeMatchWinner(); break;
            }
        }

        /// <summary>
        /// 대기방(LobbyManager)의 게임 시작 진입점. 시작 조건(호스트/인원/전원 준비) 검증은
        /// LobbyManager 가 마친 상태고, 여기서는 FSM 관점의 유효성(서버/LOBBY)만 확인한다.
        /// </summary>
        public void StartMatchFromLobby()
        {
            if (!IsServer || !IsSpawned || _phase.Value != MatchPhase.Lobby) return;
            AdvancePhase(); // LOBBY -> round=1, PREP (기존 FSM 경로 그대로)
        }

        private void AdvancePhase()
        {
            switch (_phase.Value)
            {
                case MatchPhase.Lobby:
                    _round.Value = 1;
                    EnterPhase(MatchPhase.Prep);
                    break;
                case MatchPhase.Prep:
                    EnterPhase(MatchPhase.Play);
                    break;
                case MatchPhase.Play:
                    EnterPhase(MatchPhase.Highlight);
                    break;
                case MatchPhase.Highlight:
                    if (_round.Value < Climb.ROUNDS)
                        EnterPhase(MatchPhase.Intermission); // 다음 라운드 PREP 전 완충 대기
                    else
                        EnterPhase(MatchPhase.Result);
                    break;
                case MatchPhase.Intermission:
                    _round.Value++;
                    EnterPhase(MatchPhase.Prep); // 매 라운드 전 준비(핵심 변경점)
                    break;
                case MatchPhase.Result:
                    if (!TryReturnToLobbyScene())
                        EnterPhase(MatchPhase.Lobby); // 레거시 단일 씬 폴백
                    break;
            }
        }

        // 씬 분리: 결과 후 대기방 씬으로 복귀(NGO 씬 동기화 - 전 클라 함께 전환).
        // 구조물은 동적 스폰이라 씬 전환에도 살아남으므로 먼저 정리한다(대기방 반입 방지).
        // 씬 미설정/로드 실패 시 false -> 호출부가 기존 LOBBY 페이즈로 폴백.
        private bool TryReturnToLobbyScene()
        {
            if (string.IsNullOrEmpty(_lobbySceneName) || NetworkManager.SceneManager == null) return false;
            DespawnAllStructures();
            var status = NetworkManager.SceneManager.LoadScene(_lobbySceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
            if (status != Unity.Netcode.SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[Match] 대기방 씬 로드 실패: {status} (빌드 설정에 '{_lobbySceneName}' 등록 확인)");
                return false;
            }
            return true;
        }

        private void ResetMatch()
        {
            _totalScore.Clear();
            _results.Clear();
            _round.Value = 0;
            _roundWinner.Value = ulong.MaxValue; // 매치 종료 시 라운드 우승 표시는 초기화
            // MatchWinner 는 여기서 초기화하지 않는다: 직전 매치 챔피언을 다음 매치 RESULT 재계산 전까지 유지.

            DespawnAllStructures();          // 구조물은 매치 리셋에만 전체 제거(라운드 간 누적 유지)
            _roundStats.Value = RoundStats.Empty;       // 직전 매치 통계 초기화
            MapSeed.Value = _rng.Next(1, int.MaxValue); // 매 매치 새 랜덤 레인 맵
        }

        private void BeginPrep()
        {
            // 이월 없음: PREP 마다 사용량 리셋(지급량은 라운드별 배열).
            _prepVisibleUsed.Clear();
            _prepInvisibleUsed.Clear();

            int i = 0, n = NetworkManager.ConnectedClientsList.Count;
            foreach (var c in NetworkManager.ConnectedClientsList)
            {
                EnsurePlayer(c.ClientId);
                SetDead(c.ClientId, false);                    // 이전 라운드 탈락자 복귀
                TeleportOwner(c.ClientId, SpawnSlot(i, n));    // 본체는 바닥에 배치(설치는 비행 카메라)
                i++;
            }
        }

        private void BeginPlay()
        {
            _graceApplied = false;
            _stats.BaitWindow = _scoring.baitWindowSeconds;      // 점수 설정 -> 수집기 주입(설정은 ScoringConfig 한 곳)
            _stats.ScoreRepeatLimit = _scoring.baitRepeatLimit;
            _stats.BeginRound();
            _playRoundDuration = EffectiveDuration(MatchPhase.Play);
            _playStartTime = NetworkManager.ServerTime.Time;
            var gen = ClimbMapGenerator.Instance;
            _chunkArrivals = new int[(gen != null ? gen.ScoringChunkCount : 0) + 1];
            int i = 0, n = NetworkManager.ConnectedClientsList.Count;
            foreach (var c in NetworkManager.ConnectedClientsList)
            {
                EnsurePlayer(c.ClientId);
                _players[c.ClientId].ResetForRound(i);
                SetDead(c.ClientId, false);
                TeleportOwner(c.ClientId, SpawnSlot(i, n));
                i++;
            }
            _aliveCount.Value = _players.Count;
        }

        // 스폰 슬롯: 시작 섬(ClimbMapGenerator.SpawnAreaCenter, 윗면 y=0) 위에 z 축으로 줄지어
        // 배치(인원수 기반 센터링, 섬 세로 폭 안으로 클램프). 섬 세로는 레인 수에 비례해 넓어진다.
        // 주의: 위->아래 Ground 레이캐스트로 스폰 높이를 잡으면 발판 "꼭대기"에 맞아
        // 정상 스폰(즉시 만점) 버그가 나므로, 섬 윗면(y=0) 고정 높이로 스폰한다.
        private Vector3 SpawnSlot(int index, int total)
        {
            var gen = ClimbMapGenerator.Instance;
            if (gen == null) return new Vector3(0f, 1.2f, 0f);

            Vector3 center = gen.SpawnAreaCenter;
            float halfZ = gen.SpawnAreaDepth * 0.5f - 0.6f;
            float z = (index - (Mathf.Max(1, total) - 1) * 0.5f) * spawnSpacingX;
            z = Mathf.Clamp(z, -halfZ, halfZ);
            return new Vector3(center.x, 1.2f, center.z + z);
        }

        // 발끝(캡슐 바닥) 높이. 높이 표시/채점의 공통 기준(PlayerController.FootY 위임).
        private static float FootYOf(NetworkObject po)
        {
            var pc = po != null ? po.GetComponent<RouletteParty.Net.PlayerController>() : null;
            return pc != null ? pc.FootY : (po != null ? po.transform.position.y : 0f);
        }

        // 비 PLAY 페이즈 안전망: 시작 섬 밖 허공으로 떨어진 플레이어를 섬 위로 구조.
        // (PLAY 중 추락은 구조하지 않는다 — 낙하 탈락 규칙 ①의 영역.)
        private void RescueFallenPlayers()
        {
            int i = 0, n = NetworkManager.ConnectedClientsList.Count;
            foreach (var c in NetworkManager.ConnectedClientsList)
            {
                var po = c.PlayerObject;
                if (po != null && po.transform.position.y < -5f)
                    TeleportOwner(c.ClientId, SpawnSlot(i, n));
                i++;
            }
        }

        // PLAY 중 서버 샘플링: 낙하 탈락 규칙 ① 추적 + 정상 도달 감지(위치는 READ 만).
        private void SamplePlayers(double now)
        {
            // 시작 시각 기준 실제 경과(초). 종료 시각 역산이 아니라서 finishGrace 단축에 왜곡되지 않는다.
            float elapsed = (float)(now - _playStartTime);
            float top = MapHeight;
            var gen = ClimbMapGenerator.Instance;

            foreach (var c in NetworkManager.ConnectedClientsList)
            {
                if (!_players.TryGetValue(c.ClientId, out var pr) || !pr.Alive) continue;
                var po = c.PlayerObject;
                if (po == null) continue;

                float y = FootYOf(po); // 발끝 기준(HUD 표시·채점과 동일 기준)
                if (y > pr.BestY) pr.BestY = y; // 진행도·안정성 입력. 낙하 추적용 ApexY 와 별개(부활에도 유지)

                // ---- 낙하 탈락 규칙 ① (서버 직접 추적): 공중 낙하 거리 >= _lethalAirFall ----
                // 접지 상태는 소유 클라만 알 수 있으므로 수직 속도로 "자유낙하"만 걸러낸다:
                // 하강 속도가 -3m/s 를 넘지 않는 상태가 0.3초 유지되면 지지된 것으로 보고
                // 기준 최고점(ApexY)을 현재 높이로 리셋한다. 중력 -20 에서 자유낙하는 0.15초
                // 안에 -3m/s 를 넘고, 점프 정점의 순간 정지(±1m/s 구간 약 0.1초)도 유지 시간
                // 조건에 걸리지 않는다 -> 실제 낙하 중에는 리셋되지 않는다.
                float dt = Time.deltaTime;
                float vy = dt > 0f ? (y - pr.LastY) / dt : 0f;
                pr.LastY = y;
                if (vy > -3f)
                {
                    pr.FallStillTime += dt;
                    if (pr.FallStillTime >= 0.3f) pr.ApexY = y;
                }
                else pr.FallStillTime = 0f;
                if (y > pr.ApexY) pr.ApexY = y;

                float falling = pr.ApexY - y;
                if (falling >= _lethalAirFall || y < -60f) // -60 = 무한 추락 안전망(임계 과대 설정 대비)
                {
                    EliminateByFall(pr, falling);
                    continue;
                }

                // 청크 선착순 기록: 발판 영역 -> 청크 순번. 상위 청크 진입 시 건너뛴 하위 청크도
                // 도달로 인정한다(구조물 지름길이 게임 컨셉이므로 스킵을 벌하지 않음).
                if (gen != null && _chunkArrivals.Length > 1 &&
                    gen.TryGetChunkAt(new Vector3(po.transform.position.x, y, po.transform.position.z), out int chunk) &&
                    chunk > pr.MaxChunk)
                {
                    for (int k = pr.MaxChunk + 1; k <= chunk && k < _chunkArrivals.Length; k++)
                        pr.ChunkPlacements.Add(++_chunkArrivals[k]);
                    pr.MaxChunk = chunk;
                }

                // 도달 판정: 도착 청크(큰 발판) 위에 올라서면 완주. 도착 청크가 없는 구성
                // (레거시 균일 체인)에서는 기존 높이 기준(y >= top)으로 폴백.
                bool arrived = gen != null && gen.HasFinishPlates
                    ? gen.IsAtFinish(new Vector3(po.transform.position.x, y, po.transform.position.z))
                    : y >= top;
                if (!pr.ReachedTop && arrived)
                {
                    pr.ReachedTop = true;
                    pr.TopTime = elapsed;
                    if (!_graceApplied)
                    {
                        // 첫 정상 도달: 잔여 타이머를 finishGrace 로 단축(이미 더 짧으면 그대로).
                        double graceEnd = now + _finishGrace * (fastMode ? fastMultiplier : 1f);
                        if (graceEnd < _phaseEndTime.Value) _phaseEndTime.Value = graceEnd;
                        _graceApplied = true;
                    }
                }
            }
        }

        // ============================ 낙하 탈락 (서버) ============================
        /// <summary>소유 클라의 착지 보고 처리. PlayerController.ReportFallServerRpc 가 서버에서 호출.
        /// 탈락 규칙 ②: 낙하 거리 >= _lethalLandFall 인 착지 = 탈락. 미만이면 통계만 기록.</summary>
        public void ReportLanding(ulong clientId, float fallHeight)
        {
            if (!IsServer || _phase.Value != MatchPhase.Play) return;
            if (!_players.TryGetValue(clientId, out var pr) || !pr.Alive) return;

            if (fallHeight >= _lethalLandFall) { EliminateByFall(pr, fallHeight); return; }

            var po = NetworkManager.ConnectedClients.TryGetValue(clientId, out var cl) ? cl.PlayerObject : null;
            _stats.RecordFall(clientId, fallHeight, false, NetworkManager.ServerTime.Time,
                po != null ? po.transform.position : Vector3.zero);
            // 착지 확정 -> 서버 낙하 추적(규칙 ①)의 기준점도 리셋(같은 낙하의 이중 계산 방지).
            pr.ApexY = FootYOf(po);
            pr.FallStillTime = 0f;
        }

        /// <summary>낙하 탈락 확정(규칙 ①/② 공용). 위치를 통계에 남긴다 —
        /// 추후 하이라이트가 ClimbMapGenerator.TryGetRegionAt 으로 발판 영역에 귀속시킬 데이터.
        /// 탈락은 일시 상태: _respawnDelay 뒤 시작 섬에서 자동 부활한다(ProcessRespawns).</summary>
        private void EliminateByFall(PlayerRuntime pr, float fallHeight)
        {
            var po = NetworkManager.ConnectedClients.TryGetValue(pr.ClientId, out var cl) ? cl.PlayerObject : null;
            _stats.RecordFall(pr.ClientId, fallHeight, true, NetworkManager.ServerTime.Time,
                po != null ? po.transform.position : Vector3.zero);

            pr.Alive = false;
            pr.Deaths++; // 반복 탈락 감점·안정성 보너스 입력
            pr.DeathHeight = Mathf.Max(0f, FootYOf(po)); // 발끝 기준
            SetDead(pr.ClientId, true);
            _aliveCount.Value = Mathf.Max(0, _aliveCount.Value - 1);
            _pendingRespawn.Add((pr.ClientId, NetworkManager.ServerTime.Time + _respawnDelay));
        }

        // ---- 자동 부활(서버) ----
        // 탈락자를 지연 후 시작 섬에 복귀시킨다. 서버 낙하 추적 기준점(ApexY/LastY)을
        // 스폰 높이로 리셋해 부활 텔레포트가 낙하로 오인되지 않게 한다.
        private readonly List<(ulong id, double at)> _pendingRespawn = new List<(ulong, double)>();

        private void ProcessRespawns(double now)
        {
            for (int i = _pendingRespawn.Count - 1; i >= 0; i--)
            {
                if (now < _pendingRespawn[i].at) continue;
                ulong id = _pendingRespawn[i].id;
                _pendingRespawn.RemoveAt(i);

                if (!_players.TryGetValue(id, out var pr)) continue;
                if (!NetworkManager.ConnectedClients.ContainsKey(id)) continue; // 접속 종료자

                Vector3 slot = SpawnSlot(pr.SpawnIndex, Mathf.Max(1, _players.Count));
                pr.Alive = true;
                pr.ApexY = slot.y; pr.LastY = slot.y; pr.FallStillTime = 0f;
                SetDead(id, false);
                TeleportOwner(id, slot);
                _aliveCount.Value = Mathf.Min(_players.Count, _aliveCount.Value + 1);
            }
        }

        // Dead 플래그(플레이어 오브젝트의 NetworkVariable, 서버 write)로 탈락 상태를 전 클라에 전파.
        // (입력 잠금·렌더/콜라이더 off·관전 전환은 PlayerController 가 이 값으로 처리)
        private void SetDead(ulong clientId, bool dead)
        {
            if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var cl) || cl.PlayerObject == null) return;
            var pc = cl.PlayerObject.GetComponent<RouletteParty.Net.PlayerController>();
            if (pc != null && pc.Dead.Value != dead) pc.Dead.Value = dead;
        }

        // ============================ 구조물 설치 (서버 권위) ============================
        /// <summary>
        /// PREP 중 클라 -> 서버 설치 요청. 검증: PREP / 종류별 잔여 개수 / 설치 허용 범위(시작 섬 제외) / 겹침.
        /// yawStep = 90도 단위 회전(0~3). Invisible 타입 = 보이지 않는 구조물.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void PlaceStructureServerRpc(Vector3 pos, byte yawStep, byte pitchStep, byte rollStep, byte typeByte, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            var type = (StructureType)typeByte;
            bool invisible = type == StructureType.Invisible;

            if (_phase.Value != MatchPhase.Prep) return;

            // 종류별 잔여 개수(이월 없음).
            var used = invisible ? _prepInvisibleUsed : _prepVisibleUsed;
            int grant = invisible ? InvisibleGrant : VisibleGrant;
            used.TryGetValue(sender, out int usedCount);
            if (usedCount >= grant) return;

            // 설치 허용 범위(맵 footprint + 여유) && 시작 섬 영역 밖(스폰 봉쇄 방지).
            var gen = ClimbMapGenerator.Instance;
            if (gen == null || !gen.IsPlacementAllowed(pos, 0.1f)) return;

            GameObject prefab = PrefabFor(type);
            if (prefab == null) { Debug.LogWarning($"[Match] structure prefab for {type} not assigned."); return; }

            // 프리팹 "바닥"이 조준점(pos)에 닿도록 피벗을 들어 올린 최종 위치.
            // 회전(3축 90도 스텝)을 먼저 적용해야 회전된 렌더 바운즈로 바닥 오프셋이 나온다.
            // 클라 블루프린트(PrepClientUI)와 동일한 계산 -> 프리뷰 위치 = 실물 위치.
            GameObject go = Instantiate(prefab);
            go.transform.rotation = Quaternion.Euler(pitchStep * 90f, yawStep * 90f, rollStep * 90f);
            go.transform.position = pos + Vector3.up * Structure.BottomOffset(go);

            // 겹침 검사는 "플레이어 설치 구조물끼리"만. 접촉은 허용, 관통(_overlapTolerance 초과)만 거부
            // -> 구조물 위에 쌓기 가능. 최종 트랜스폼이 반영된 렌더 AABB 를 쓰므로 클라 예비검증과 결과가 같다.
            // 랜덤 발판(레인)은 검사 대상이 아니다 — 발판에 끼워/걸쳐 설치하는 것은 게임 규칙상 유효.
            // 요청자에게 숨겨진 함정(타인의 투명 구조물)과만 겹치면: 거부하되 "함정 있음"을 그 클라에만 통지.
            // (클라 프리뷰는 함정을 모르고 초록으로 표시했을 것 — 가져다 대는 것만으로는 함정이 안 드러난다.)
            Structure.OverlapScan(Structure.RenderBounds(go), _overlapTolerance, sender,
                                  out bool visibleHit, out bool hiddenHit);
            if (visibleHit || hiddenHit)
            {
                Destroy(go);
                if (hiddenHit && !visibleHit)
                    DenyPlacementByTrapRpc(RpcTarget.Single(sender, RpcTargetUse.Temp));
                return;
            }

            var no = go.GetComponent<NetworkObject>();
            no.Spawn(true);

            // 종류/설치자는 반드시 Spawn "이후"에 기록한다: 스폰 시 NetworkVariable 이 초기값으로
            // 리셋돼 스폰 전 쓰기가 유실되는 문제를 실측으로 확인(서버 쓰기는 델타 복제로 전 클라 반영).
            var ob = go.GetComponent<Structure>();
            if (ob != null) ob.ServerInit(type, sender);

            _structures.Add(new StructRec { No = no });
            used[sender] = usedCount + 1;
        }

        /// <summary>
        /// 종류별 구조물 프리팹. 서버 스폰과 클라 블루프린트(PrepClientUI 배치 프리뷰)가
        /// 같은 프리팹을 쓰므로, 새 구조물 추가 = 프리팹 필드 + 이 switch 에 한 줄이면 끝.
        /// </summary>
        public GameObject PrefabFor(StructureType type)
        {
            switch (type)
            {
                case StructureType.Wall:      return _wallPrefab;
                case StructureType.Cylinder:  return _cylinderPrefab;
                case StructureType.Invisible: return _invisiblePrefab;
                case StructureType.Tree:      return _treePrefab;
                case StructureType.Rock:      return _rockPrefab;
                default:                      return _wallPrefab;
            }
        }

        /// <summary>서버 -> 요청자 단독: 설치가 숨겨진 함정과의 겹침으로 거부됨.
        /// 함정 위치를 프리뷰(빨간색)로 노출하지 않기 위해 "설치 시도" 시점에만 알려주는 통지.</summary>
        [Rpc(SendTo.SpecifiedInParams)]
        private void DenyPlacementByTrapRpc(RpcParams rpcParams = default)
        {
            PlacementDeniedByTrap?.Invoke();
        }

        /// <summary>보이지 않는 구조물과의 충돌 보고 -> 전원 일시 공개(RevealUntil 갱신).</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RevealStructureServerRpc(NetworkObjectReference structRef, RpcParams rpcParams = default)
        {
            if (!structRef.TryGet(out NetworkObject no)) return;
            var ob = no.GetComponent<Structure>();
            if (ob == null || !ob.IsInvisibleKind) return;
            ob.RevealUntil.Value = NetworkManager.ServerTime.Time + ob.RevealDuration;

            // 통계: 접촉자(보고자) 기록 -> 이후 낙하 피해가 이 설치자의 "낚시"로 귀속될 수 있다.
            if (_phase.Value == MatchPhase.Play)
                _stats.RecordReveal(rpcParams.Receive.SenderClientId, ob.OwnerId.Value, NetworkManager.ServerTime.Time);
        }

        // 매치 리셋에만 전체 제거(라운드 간 누적이 핵심 룰).
        private void DespawnAllStructures()
        {
            if (!IsServer) return;
            foreach (var rec in _structures)
                if (rec.No != null && rec.No.IsSpawned)
                    rec.No.Despawn(true);
            _structures.Clear();
        }

        // ============================ 점수 (하이라이트 진입 시) ============================
        // 라운드 점수(개편안) = 진행도(최고 0.7 + 최종 0.3) + 정상 도달 시간 + 청크 선착순
        //                    + 참가자 수 비례 순위 + 안정성 + 투명 구조물 영향 - 반복 탈락 감점.
        // 공식은 MatchScoring.RoundScore 한 곳에만 존재. 여기서는 수집된 사실을 입력으로 조립만 한다.
        private void EndPlayEvaluate()
        {
            float top = MapHeight;

            // 최종 높이 수집: 생존자는 현재 y, 탈락자는 사망 지점 높이. 정상 도달자는 만점 고정.
            var rows = new List<(ulong id, float finalY, bool topped, float topTime)>();
            foreach (var pr in _players.Values)
            {
                float y;
                if (pr.ReachedTop) y = top;
                else if (!pr.Alive) y = pr.DeathHeight;
                else
                {
                    var po = NetworkManager.ConnectedClients.TryGetValue(pr.ClientId, out var cl) ? cl.PlayerObject : null;
                    y = Mathf.Clamp(FootYOf(po), 0f, top); // 발끝 기준
                }
                rows.Add((pr.ClientId, y, pr.ReachedTop, pr.TopTime));
            }

            // 순위: 높이 내림차순, 동률(정상 도달 등)은 먼저 도달한 쪽.
            rows.Sort((a, b) =>
            {
                int c = b.finalY.CompareTo(a.finalY);
                if (c != 0) return c;
                if (a.topped && b.topped) return a.topTime.CompareTo(b.topTime);
                return a.id.CompareTo(b.id);
            });

            for (int i = 0; i < rows.Count; i++)
            {
                var pr = _players[rows[i].id];
                var perf = new RoundPerformance
                {
                    // 정상 도달자는 최고 높이를 정상으로 고정(개편안 4.1). 최고 높이는 탈락·부활에도 유지.
                    BestHeight01  = top <= 0f ? 0f : (pr.ReachedTop ? 1f : Mathf.Clamp01(pr.BestY / top)),
                    FinalHeight01 = top <= 0f ? 0f : rows[i].finalY / top,
                    Rank = i,
                    PlayerCount = rows.Count,
                    ReachedTop = rows[i].topped,
                    TopTime = rows[i].topTime,
                    RoundDuration = _playRoundDuration,
                    ChunkPlacements = pr.ChunkPlacements.ToArray(),
                    Deaths = pr.Deaths,
                    BaitKills = _stats.ScoreBaitsOf(rows[i].id),
                };
                var breakdown = MatchScoring.RoundScore(perf, _scoring);
                int totalScore = breakdown.Total;
                Debug.Log($"[Score] R{_round.Value} #{i + 1} client={rows[i].id} {breakdown}"); // 내역은 호스트 로그로만(UI 는 총점)

                _totalScore[rows[i].id] = (_totalScore.TryGetValue(rows[i].id, out int acc) ? acc : 0) + totalScore;
                _results.Add(new RoundResult
                {
                    Round = _round.Value,
                    ClientId = rows[i].id,
                    Rank = i + 1,
                    Score = totalScore,
                    Topic = 0
                });
            }

            _roundWinner.Value = rows.Count > 0 ? rows[0].id : ulong.MaxValue;
            _roundStats.Value = _stats.Snapshot(_round.Value); // 라운드 통계 확정 -> 하이라이트 카드
            Debug.Log($"[Match] R{_round.Value} end: players={rows.Count} winner={_roundWinner.Value}");
        }

        private void ComputeMatchWinner()
        {
            ulong best = ulong.MaxValue;
            int bestScore = int.MinValue;
            foreach (var kv in _totalScore)
                if (kv.Value > bestScore) { bestScore = kv.Value; best = kv.Key; }
            _matchWinner.Value = best;
        }

        // ============ Owner-authoritative teleport bridge (기존 유지) ============
        private void TeleportOwner(ulong clientId, Vector3 pos)
        {
            // 순간이동은 낙하가 아니다: 서버 낙하 추적(규칙 ①) 기준점을 목적지로 리셋.
            if (_players.TryGetValue(clientId, out var pr))
            {
                pr.ApexY = pos.y;
                pr.LastY = pos.y;
                pr.FallStillTime = 0f;
            }
            TeleportPlayerRpc(pos, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void TeleportPlayerRpc(Vector3 pos, RpcParams rpcParams = default)
        {
            var po = NetworkManager.LocalClient != null ? NetworkManager.LocalClient.PlayerObject : null;
            if (po == null) return;
            var pc = po.GetComponent<PlayerController>();
            if (pc != null) pc.TeleportTo(pos);
        }

        // ============================ Debug GUI ============================
        private void OnGUI()
        {
            if (!showDebugGui) return;
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return;

            double now = IsSpawned ? nm.ServerTime.Time : 0d;
            float rem = Mathf.Max(0f, (float)(_phaseEndTime.Value - now));
            string role = IsServer ? (IsHost ? "Host" : "Server") : "Client";

            ImguiScale.Apply(); // 이하 좌표는 1080p 기준 가상 픽셀
            GUILayout.BeginArea(new Rect(280, 10, 380, 360), GUI.skin.box);
            GUILayout.Label($"[MatchManager] role={role} (Climb)");
            GUILayout.Label($"Phase   : {_phase.Value}");
            GUILayout.Label($"Round   : {_round.Value}/{Climb.ROUNDS}");
            GUILayout.Label($"Time    : {rem:0.0}s");
            GUILayout.Label($"Alive   : {_aliveCount.Value}");
            GUILayout.Label($"Seed    : {MapSeed.Value}");
            if (IsServer)
            {
                GUILayout.Label($"Players : {_players.Count} (server)");
                GUILayout.Label($"Structs : {_structures.Count} placed");
            }
            ulong me = nm.LocalClientId;
            int myScore = 0;
            for (int i = 0; i < _results.Count; i++)
                if (_results[i].ClientId == me) myScore += (int)_results[i].Score;
            GUILayout.Label($"MyScore : {myScore} (id {me})");
            GUILayout.Label($"RoundWin: {WinnerLabel(_roundWinner.Value)}");
            GUILayout.Label($"MatchWin: {WinnerLabel(_matchWinner.Value)}");
            if (fastMode) GUILayout.Label($"FAST x{fastMultiplier}");
            GUILayout.EndArea();
        }

        private static string WinnerLabel(ulong id) => id == ulong.MaxValue ? "-" : id.ToString();
    }
}
