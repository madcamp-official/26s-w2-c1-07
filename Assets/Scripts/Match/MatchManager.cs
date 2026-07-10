using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components; // NetworkTransform (built-in test-teleport fallback)
using UnityEngine;

namespace RouletteParty.Match
{
    /// <summary>
    /// Day4 -> Day5 host-authoritative match FSM. Attach to a scene-placed GameObject with a
    /// NetworkObject (server authority). A scene NetworkObject auto-spawns on server start,
    /// so it does NOT need to be in any NetworkPrefabs list.
    ///
    ///   LOBBY -> PREP -> [ROULETTE -> PLAY -> HIGHLIGHT] x3 -> RESULT -> (loop back to LOBBY)
    ///
    /// DAY5 추가 요약:
    ///   (a) 투표: PREP 중 각 클라가 주제(Race/Height/Survive)에 투표(VoteServerRpc). 집계는
    ///       RaceVotes/HeightVotes/SurviveVotes NetworkVariable 로 전 클라에 동기화.
    ///   (b) 룰렛: PickTopic 을 무작위 -> 투표 가중(표수+1, 직전 주제 제외)으로 교체.
    ///   (c) 장애물: PREP 중 클라가 지면 히트점을 보내면(PlaceObstacleServerRpc) 호스트가 검증 후
    ///       NetworkObject 프리팹을 스폰(서버 전용). 소유자는 Obstacle.OwnerId 에 기록. 매치 시작
    ///       (LOBBY/PREP 진입)마다 이전 장애물을 전부 despawn.
    ///   (d) 상세 점수: 모드별 0..1000 정규화 클리어 점수 + 장애물 kill 귀속 점수(min 300)를
    ///       70:30 으로 합산, 3라운드 누적. 최종 순위 = 누적 총점 내림차순.
    ///
    /// TIMER MODEL (Day4 그대로):
    ///   페이즈 타이머는 서버에서만 돈다. 서버가 페이즈 종료 절대시각(서버 클럭)을
    ///   NetworkVariable&lt;double&gt; 에 페이즈당 1회 쓰고, 각 클라는
    ///       remaining = _phaseEndTime.Value - NetworkManager.ServerTime.Time
    ///   로 남은 시간을 계산한다(프레임별 카운트다운 복제 불필요, join-in-progress 자동 대응).
    ///
    /// OWNER-AUTHORITATIVE MOVEMENT CONTRACT (ClientNetworkTransform, OnIsServerAuthoritative=>false):
    ///   호스트는 모든 플레이어 transform 을 READ 만 한다(소유자가 위치를 서버로 복제). 이동/부활은
    ///   해당 플레이어의 소유 클라에게 RPC 를 보내 소유자가 스스로 텔레포트한다. 장애물도 마찬가지로
    ///   호스트가 스폰/권위를 갖고, 플레이어 이동 권위는 소유자에게 있다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class MatchManager : NetworkBehaviour
    {
        // ============================ 튜닝 ============================
        [Header("Test tuning")]
        [Tooltip("When on, every phase duration is multiplied by fastMultiplier for quick testing.")]
        [SerializeField] private bool fastMode = false;
        [SerializeField, Range(0.05f, 1f)] private float fastMultiplier = 0.25f;

        [Tooltip("Horizontal spacing between players on the start line.")]
        [SerializeField] private float spawnSpacingX = 2f;

        [Tooltip("Draw the top-right debug panel (phase/round/topic/time/alive/votes/score).")]
        [SerializeField] private bool showDebugGui = true;

        [Tooltip("Day4 STANDALONE-TEST ONLY. Owner teleports itself directly from this RPC " +
                 "(no PlayerController needed). PlayerController.TeleportTo 가 이미 있으므로 평소엔 OFF. " +
                 "ON 이면 텔레포트가 두 번 적용되니 주의.")]
        [SerializeField] private bool builtInTeleportFallback = false;

        [Header("Day5 — Obstacles")]
        [Tooltip("장애물 프리팹 3종(각 루트에 NetworkObject + Obstacle). NetworkManager 의 NetworkPrefabs 리스트에도 등록 필수.")]
        [SerializeField] private GameObject _wallPrefab;
        [SerializeField] private GameObject _cylinderPrefab;
        [SerializeField] private GameObject _ghostPrefab;

        [Tooltip("장애물 간 최소 간격(XZ 평면).")]
        [SerializeField] private float _minSpacing = 2.5f;
        [Tooltip("플레이어 1인당 최대 장애물 개수.")]
        [SerializeField] private int _maxPerPlayer = 3;
        [Tooltip("낙사 시 kill 귀속 반경(XZ 평면).")]
        [SerializeField] private float _killRadius = 4f;

        // ============================ 점수 상수 ============================
        private const float HEIGHT_MAX      = 12f;   // height 모드 정규화 상한
        private const int   OBSTACLE_CAP    = 300;   // 장애물 점수 상한(= kill 3개)
        private const int   KILL_SCORE      = 100;   // kill 1개당 점수
        private const float CLEAR_WEIGHT    = 0.7f;  // 클리어 점수 가중(장애물은 나머지 0.3 몫)

        // ============================ 복제 상태(서버 write / everyone read) ============================
        private NetworkVariable<MatchPhase> _phase        = new NetworkVariable<MatchPhase>(MatchPhase.Lobby);
        private NetworkVariable<int>        _round        = new NetworkVariable<int>(0);
        private NetworkVariable<TopicMode>  _topic        = new NetworkVariable<TopicMode>(TopicMode.None);
        private NetworkVariable<double>     _phaseEndTime = new NetworkVariable<double>(0d);
        private NetworkVariable<int>        _aliveCount   = new NetworkVariable<int>(0);
        private NetworkVariable<ulong>      _roundWinner  = new NetworkVariable<ulong>(ulong.MaxValue);
        private NetworkVariable<ulong>      _matchWinner  = new NetworkVariable<ulong>(ulong.MaxValue);

        // Day5 투표 집계(클라 UI 가 실시간으로 읽음). NetworkVariable 배열은 자동 등록되지 않으므로
        // 반드시 개별 필드로 선언한다.
        public NetworkVariable<int> RaceVotes    = new NetworkVariable<int>(0);
        public NetworkVariable<int> HeightVotes  = new NetworkVariable<int>(0);
        public NetworkVariable<int> SurviveVotes = new NetworkVariable<int>(0);

        // 전 라운드 랭킹(각 행의 Score = 라운드 총점). 클라 결과/리더보드 UI 가 이걸 읽는다.
        // 반드시 spawn 이전에 존재해야 하므로 필드 이니셜라이저로 생성.
        private NetworkList<RoundResult> _results = new NetworkList<RoundResult>();

        // ============================ 서버 전용 상태(비 네트워크) ============================
        private readonly Dictionary<ulong, PlayerRuntime> _players     = new Dictionary<ulong, PlayerRuntime>();
        private readonly Dictionary<ulong, int>           _totalScore  = new Dictionary<ulong, int>(); // 매치 누적 총점
        private readonly Dictionary<ulong, int>           _roundKills  = new Dictionary<ulong, int>();  // 이번 라운드 kill 수
        private readonly System.Random _rng = new System.Random();
        private IGameMode _mode;
        private TopicMode _lastTopic = TopicMode.None;

        // 투표 원장(clientId -> 선택 주제). PREP 종료(페이즈 전환)로 자동 잠금.
        private readonly Dictionary<ulong, TopicMode> _votes = new Dictionary<ulong, TopicMode>();

        // 이번 매치에 스폰된 장애물 기록. 위치는 kill 귀속/겹침 검증에 재사용.
        private struct ObstacleRec { public NetworkObject No; public ulong OwnerId; public Vector3 Pos; }
        private readonly List<ObstacleRec>    _obstacles      = new List<ObstacleRec>();
        private readonly Dictionary<ulong,int> _obstacleCount = new Dictionary<ulong, int>();

        // ============================ 클라 접근점(정적 싱글턴) ============================
        // 클라 UI(PrepClientUI)가 상태 읽기 + RPC 호출에 사용. 서버/클라 모두에서 세팅됨.
        public static MatchManager Instance { get; private set; }
        public MatchPhase CurrentPhase => _phase.Value;
        public int MaxPerPlayer => _maxPerPlayer;

        // ============================ Lifecycle ============================
        public override void OnNetworkSpawn()
        {
            Instance = this; // 서버/클라 모두에서 참조 확보

            if (!IsServer) return;

            NetworkManager.OnClientConnectedCallback  += HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;

            // 이미 접속된 클라(호스트 자신 포함)를 시드. 호스트 본인은 connect 콜백을 놓칠 수 있어
            // ConnectedClientsList 로 시드하는 것이 안전한 패턴.
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

        private void HandleClientConnected(ulong clientId) => EnsurePlayer(clientId);

        private void HandleClientDisconnected(ulong clientId)
        {
            _players.Remove(clientId);
            _totalScore.Remove(clientId);
            _roundKills.Remove(clientId);
            _obstacleCount.Remove(clientId);
            _votes.Remove(clientId);
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

            if (_phase.Value == MatchPhase.Play)
                SamplePlayers(now);
            else
                RescueFallenPlayers();

            if (now >= _phaseEndTime.Value) { AdvancePhase(); return; }

            // PLAY 는 결과가 확정되면 남은 타이머를 건너뛰고 조기 종료(멀티플레이 전용).
            if (_phase.Value == MatchPhase.Play && CheckEarlyEnd())
                AdvancePhase();
        }

        private float BaseDuration(MatchPhase p)
        {
            switch (p)
            {
                case MatchPhase.Lobby:     return 3f;
                case MatchPhase.Prep:      return 30f;
                case MatchPhase.Roulette:  return 5f;
                case MatchPhase.Play:      return 45f;
                case MatchPhase.Highlight: return 8f;
                case MatchPhase.Result:    return 14f;
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

            switch (p)
            {
                case MatchPhase.Lobby:
                    ResetMatch();
                    break;

                case MatchPhase.Prep:
                    // Day5: 새 매치 준비. 이전 장애물/투표를 초기화하고, 이제부터 배치·투표를 받는다.
                    DespawnAllObstacles();
                    ResetVotes();
                    break;

                case MatchPhase.Roulette:
                    PickTopic();
                    break;

                case MatchPhase.Play:
                    BeginPlay();
                    break;

                case MatchPhase.Highlight:
                    EndPlayEvaluate();
                    break;

                case MatchPhase.Result:
                    ComputeMatchWinner();
                    break;
            }
        }

        private void AdvancePhase()
        {
            switch (_phase.Value)
            {
                case MatchPhase.Lobby:
                    EnterPhase(MatchPhase.Prep);
                    break;
                case MatchPhase.Prep:
                    _round.Value = 1;
                    EnterPhase(MatchPhase.Roulette);
                    break;
                case MatchPhase.Roulette:
                    EnterPhase(MatchPhase.Play);
                    break;
                case MatchPhase.Play:
                    EnterPhase(MatchPhase.Highlight);
                    break;
                case MatchPhase.Highlight:
                    if (_round.Value < Course.ROUNDS)
                    {
                        _round.Value++;
                        EnterPhase(MatchPhase.Roulette);
                    }
                    else
                    {
                        EnterPhase(MatchPhase.Result);
                    }
                    break;
                case MatchPhase.Result:
                    EnterPhase(MatchPhase.Lobby);
                    break;
            }
        }

        private void ResetMatch()
        {
            _totalScore.Clear();
            _roundKills.Clear();
            _results.Clear();
            _lastTopic = TopicMode.None;
            _round.Value = 0;
            _topic.Value = TopicMode.None;
            _roundWinner.Value = ulong.MaxValue;
            _matchWinner.Value = ulong.MaxValue;

            DespawnAllObstacles(); // LOBBY 진입 시에도 방어적으로 전체 제거
            ResetVotes();
        }

        // ============================ Day5: 투표 + 가중 룰렛 ============================
        #region Voting

        /// <summary>
        /// PREP 중 클라 -> 서버 투표. 서버 소유 오브젝트이므로 비소유 클라도 호출 가능해야 한다.
        /// NGO 2.x: 구식 RequireOwnership=false 대신 InvokePermission=Everyone(기본값과 동일, deprecation 경고 없음).
        /// 송신자 clientId 는 인자값이 아니라 rpcParams.Receive.SenderClientId 에서만 신뢰한다(조작 방지).
        /// 주제는 byte 로 받아 소스제너레이터 호환성을 최대화(RoundResult.Topic 와 동일 패턴).
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void VoteServerRpc(byte topicByte, RpcParams rpcParams = default)
        {
            if (_phase.Value != MatchPhase.Prep) return; // PREP 밖에서는 투표 잠금
            var topic = (TopicMode)topicByte;
            if (topic == TopicMode.None) return;

            ulong sender = rpcParams.Receive.SenderClientId;
            _votes[sender] = topic; // 1인 1표(재투표 시 덮어씀)
            RecountVotes();
        }

        private void RecountVotes()
        {
            int race = 0, height = 0, survive = 0;
            foreach (var t in _votes.Values)
            {
                switch (t)
                {
                    case TopicMode.Race:    race++;    break;
                    case TopicMode.Height:  height++;  break;
                    case TopicMode.Survive: survive++; break;
                }
            }
            RaceVotes.Value    = race;    // 서버 write -> 전 클라 복제
            HeightVotes.Value  = height;
            SurviveVotes.Value = survive;
        }

        private void ResetVotes()
        {
            _votes.Clear();
            RaceVotes.Value = 0; HeightVotes.Value = 0; SurviveVotes.Value = 0;
        }

        // 룰렛 주제 선택: w_i = 표수_i + 1, 직전 주제는 제외(w=0). 누적합 가중 랜덤.
        private void PickTopic()
        {
            int wRace    = RaceVotes.Value    + 1;
            int wHeight  = HeightVotes.Value  + 1;
            int wSurvive = SurviveVotes.Value + 1;

            if      (_lastTopic == TopicMode.Race)    wRace    = 0;
            else if (_lastTopic == TopicMode.Height)  wHeight  = 0;
            else if (_lastTopic == TopicMode.Survive) wSurvive = 0;

            int total = wRace + wHeight + wSurvive;
            TopicMode t;
            if (total <= 0)
            {
                t = TopicMode.Race; // 이론상 도달 불가(항상 +1)하지만 안전 폴백
            }
            else
            {
                int r = _rng.Next(total); // 0..total-1
                if      (r < wRace)           t = TopicMode.Race;
                else if (r < wRace + wHeight) t = TopicMode.Height;
                else                          t = TopicMode.Survive;
            }

            _topic.Value = t;
            _lastTopic   = t;
            _mode        = CreateMode(t);
        }

        #endregion

        private IGameMode CreateMode(TopicMode t)
        {
            switch (t)
            {
                case TopicMode.Race:    return new RaceMode();
                case TopicMode.Height:  return new HeightMode();
                case TopicMode.Survive: return new SurviveMode();
                default:                return new RaceMode();
            }
        }

        // ============================ Day5: 장애물 배치(서버 권위 동적 스폰) ============================
        #region Obstacles

        /// <summary>
        /// PREP 중 클라 -> 서버 장애물 배치 요청. 클라는 지면 레이캐스트 히트점만 보내고,
        /// 검증/스폰은 전부 서버가 한다. 서버 소유 오브젝트이므로 비소유 클라도 호출 가능해야 한다.
        /// NGO 2.x: 구식 RequireOwnership=false 대신 InvokePermission=Everyone(deprecation 경고 없음).
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void PlaceObstacleServerRpc(Vector3 hit, byte typeByte, RpcParams rpcParams = default)
        {
            ulong sender  = rpcParams.Receive.SenderClientId;
            var   type    = (ObstacleType)typeByte;

            if (!ValidatePlacement(sender, hit, out Vector3 pos))
                return; // 거부(조용히). 필요 시 sender 대상 "denied" RPC 를 추가할 수 있음.

            GameObject prefab = PrefabFor(type);
            if (prefab == null)
            {
                Debug.LogWarning($"[Match] Obstacle prefab for {type} not assigned.");
                return;
            }

            // 정적 장애물: NetworkTransform 불필요. 위치/회전은 Spawn() '이전'에 확정해야 초기 동기화에 실림.
            GameObject go = Instantiate(prefab);
            go.transform.SetPositionAndRotation(pos, Quaternion.identity);

            var ob = go.GetComponent<Obstacle>();
            if (ob != null) ob.ServerInit(type, sender); // ★ Spawn 전에 종류/소유자 기록

            var no = go.GetComponent<NetworkObject>();
            no.Spawn(true); // destroyWithScene=true, 네트워크 소유권은 서버

            _obstacles.Add(new ObstacleRec { No = no, OwnerId = sender, Pos = pos });
            _obstacleCount[sender] = GetObstacleCount(sender) + 1;
        }

        private GameObject PrefabFor(ObstacleType type)
        {
            switch (type)
            {
                case ObstacleType.Wall:     return _wallPrefab;
                case ObstacleType.Cylinder: return _cylinderPrefab;
                case ObstacleType.Ghost:    return _ghostPrefab;
                default:                    return _wallPrefab;
            }
        }

        // 서버 검증: PREP 여부 / 1인 최대 개수 / 코스 구간 / 지형 X 범위 / 강(수면) 금지 / 겹침 금지.
        private bool ValidatePlacement(ulong sender, Vector3 hit, out Vector3 pos)
        {
            pos = default;

            if (_phase.Value != MatchPhase.Prep) return false;          // 1) PREP 에서만
            if (GetObstacleCount(sender) >= _maxPerPlayer) return false; // 2) 1인 최대 개수

            float x = hit.x, z = hit.z;

            // 3) 코스 구간: 스폰선(START_Z) 뒤 / 골(GOAL_Z) 뒤 금지
            if (z > Course.START_Z || z < Course.GOAL_Z) return false;

            // 4) 지형 X 범위(중앙 정렬 -40..40): 가장자리 여유 2
            float halfX = TerrainGenerator.SIZE_X * 0.5f;
            if (Mathf.Abs(x) > halfX - 2f) return false;

            // 5) Y 는 지면에 스냅. 강/수면 근처(강 중앙 포함)는 지형이 물 아래로 파여 있으므로 자연히 금지된다.
            float gy = TerrainGenerator.SampleHeight(x, z);
            if (gy < TerrainGenerator.WATER_Y + 0.8f) return false;

            Vector3 candidate = new Vector3(x, gy, z);

            // 6) 기존 장애물과 겹침 금지(XZ 평면 거리)
            var cand2 = new Vector2(candidate.x, candidate.z);
            foreach (var rec in _obstacles)
            {
                var other2 = new Vector2(rec.Pos.x, rec.Pos.z);
                if (Vector2.Distance(cand2, other2) < _minSpacing) return false;
            }

            pos = candidate;
            return true;
        }

        private int GetObstacleCount(ulong id) => _obstacleCount.TryGetValue(id, out int c) ? c : 0;

        // 매치 시작(LOBBY/PREP 진입) 시 이전 장애물 전부 제거. 서버 전용. Despawn 은 서버만 호출.
        private void DespawnAllObstacles()
        {
            if (!IsServer) return;
            foreach (var rec in _obstacles)
                if (rec.No != null && rec.No.IsSpawned)
                    rec.No.Despawn(true); // true = GameObject 도 Destroy
            _obstacles.Clear();
            _obstacleCount.Clear();
        }

        // 낙사 kill 귀속: 사망 지점 XZ 반경 내 "타인 소유" 최근접 장애물의 소유자에게 kill+1.
        // (사망은 강 바닥에서 나므로 3D 거리 대신 XZ 평면 거리로 판정한다.)
        private bool TryAttributeKill(Vector3 deathPos, ulong victimId, out ulong killerId)
        {
            killerId = 0;
            float best = _killRadius;
            bool found = false;
            var dp = new Vector2(deathPos.x, deathPos.z);

            foreach (var rec in _obstacles)
            {
                if (rec.OwnerId == victimId) continue; // 자기 함정은 제외
                float d = Vector2.Distance(dp, new Vector2(rec.Pos.x, rec.Pos.z));
                if (d <= best) { best = d; killerId = rec.OwnerId; found = true; }
            }
            return found;
        }

        private int GetRoundKills(ulong id) => _roundKills.TryGetValue(id, out int k) ? k : 0;

        #endregion

        // 출발선으로 전원 이동 + 라운드 판정 데이터 초기화 + 라운드 kill 리셋.
        private void BeginPlay()
        {
            _roundKills.Clear(); // 라운드마다 장애물 kill 누적을 새로 시작

            int i = 0;
            foreach (var c in NetworkManager.ConnectedClientsList)
            {
                EnsurePlayer(c.ClientId);
                _players[c.ClientId].ResetForRound(Course.SPAWN_Y, i);
                TeleportOwner(c.ClientId, SpawnSlot(i)); // owner-authoritative: 소유자에게 RPC
                i++;
            }
            _aliveCount.Value = _players.Count;
        }

        private Vector3 SpawnSlot(int index)
        {
            // x=0 중심으로 좌우로 벌린 출발선. y 는 지형 높이 + 여유로 지면 위에 스폰.
            float x = (index - 3) * spawnSpacingX;
            float y = TerrainGenerator.SampleHeight(x, Course.START_Z) + 1.5f;
            return new Vector3(x, y, Course.START_Z);
        }

        // 비 PLAY 페이즈에서 강물(y < DEATH_Y)에 빠진 플레이어(초기 스폰 포함)를 출발선으로 구조.
        private void RescueFallenPlayers()
        {
            int i = 0;
            foreach (var c in NetworkManager.ConnectedClientsList)
            {
                var po = c.PlayerObject;
                if (po != null && po.transform.position.y < Course.DEATH_Y)
                    TeleportOwner(c.ClientId, SpawnSlot(i));
                i++;
            }
        }

        // 서버 프레임 샘플링: 복제된 위치(소유자 -> 서버)를 READ 해서 판정 데이터 갱신 + 사망/부활 구동.
        // 위치는 여기서 READ 만(절대 WRITE 하지 않음). 낙사 kill 귀속도 여기서 처리.
        private void SamplePlayers(double now)
        {
            if (_mode == null) return;

            bool allowRespawn = _mode.AllowRespawn;
            float dur = EffectiveDuration(MatchPhase.Play);
            float elapsed = dur - (float)(_phaseEndTime.Value - now); // PLAY 경과 초
            int alive = 0;

            foreach (var c in NetworkManager.ConnectedClientsList)
            {
                if (!_players.TryGetValue(c.ClientId, out var pr)) continue;
                var po = c.PlayerObject;
                if (po == null) continue; // 플레이어 오브젝트 미스폰

                if (!pr.Alive)
                {
                    // race/height: 지연 후 소유자에게 텔레포트 RPC 로 부활.
                    if (allowRespawn && pr.PendingRespawn && now >= pr.RespawnAtServerTime)
                    {
                        pr.Alive = true;
                        pr.PendingRespawn = false;
                        TeleportOwner(c.ClientId, SpawnSlot(pr.SpawnIndex));
                    }
                    if (pr.Alive) alive++;
                    continue; // survive: 탈락자는 그대로 유지(관전)
                }

                Vector3 pos = po.transform.position; // READ ok(소유자가 위치를 서버로 복제)

                if (pos.y > pr.PeakY) pr.PeakY = pos.y;

                float prog = Mathf.Clamp01((Course.START_Z - pos.z) / (Course.START_Z - Course.GOAL_Z));
                if (prog > pr.Progress) pr.Progress = prog;

                if (!pr.Finished && pos.z <= Course.GOAL_Z)
                {
                    pr.Finished = true;
                    pr.FinishTime = elapsed;
                }

                // 사망 판정(PLAY 중에만). 서버가 복제된 Y 를 읽어 판정. 반응(부활)은 소유자에게 위임.
                if (pos.y < Course.DEATH_Y)
                {
                    pr.Alive = false;
                    pr.DeathTime = elapsed;

                    // Day5: 낙사 지점 근처 "타인 소유" 장애물 소유자에게 kill 귀속.
                    if (TryAttributeKill(pos, c.ClientId, out ulong killer))
                        _roundKills[killer] = GetRoundKills(killer) + 1;

                    if (allowRespawn)
                    {
                        pr.PendingRespawn = true;
                        pr.RespawnAtServerTime = now + Course.RESPAWN_DELAY;
                    }
                    // survive: 부활 예약 없음 -> 라운드 남은 시간 동안 탈락.
                    continue; // 이 프레임은 사망 -> alive 미집계
                }

                alive++;
            }

            _aliveCount.Value = alive;
        }

        // 선택적 라운드 조기 종료. 솔로 테스트에서는 항상 풀타이머가 돌도록 2인 이상에서만.
        private bool CheckEarlyEnd()
        {
            if (_mode == null || _players.Count < 2) return false;
            if (_topic.Value == TopicMode.Race)    return _players.Values.All(p => p.Finished);
            if (_topic.Value == TopicMode.Survive) return _aliveCount.Value <= 1;
            return false; // height 는 항상 풀타이머
        }

        // ============================ Day5: 상세 점수 산출 ============================
        // 라운드 종료 시: 모드별 0..1000 클리어 점수 + 장애물 kill 점수(min 300)를 70:30 으로 합산,
        // 매치 누적에 더하고, 라운드 총점으로 순위를 매겨 복제 결과 행을 추가한다.
        private void EndPlayEvaluate()
        {
            if (_mode == null) return;

            var ctx = new MatchContext { Players = _players, PlayDuration = EffectiveDuration(MatchPhase.Play) };
            List<ModeRank> modeRanking = _mode.Evaluate(ctx); // 모드 지표 정렬(best-first) — race 완주순위 추출에 재사용

            float dur = ctx.PlayDuration;
            int   M   = Mathf.Max(1, _players.Count);

            // race 완주순위 맵: Evaluate 결과는 완주자가 완주시각 순으로 앞에 온다.
            var finishRank = new Dictionary<ulong, int>();
            if (_topic.Value == TopicMode.Race)
            {
                int r = 1;
                foreach (var mr in modeRanking)
                    if (_players.TryGetValue(mr.ClientId, out var pr) && pr.Finished)
                        finishRank[mr.ClientId] = r++;
            }

            // 라운드 총점 계산 + 매치 누적.
            var roundTotal = new Dictionary<ulong, int>();
            foreach (var pr in _players.Values)
            {
                float clear    = ComputeClearScore(pr, dur, M, finishRank); // 0..1000
                int   obstacle = Mathf.Min(OBSTACLE_CAP, GetRoundKills(pr.ClientId) * KILL_SCORE);
                int   total    = Mathf.RoundToInt(clear * CLEAR_WEIGHT) + obstacle;

                roundTotal[pr.ClientId] = total;
                _totalScore[pr.ClientId] = (_totalScore.TryGetValue(pr.ClientId, out int acc) ? acc : 0) + total;
            }

            // 라운드 총점 내림차순으로 순위 매겨 복제 결과 행 추가.
            var ordered = roundTotal.OrderByDescending(kv => kv.Value).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                _results.Add(new RoundResult
                {
                    Round    = _round.Value,
                    ClientId = ordered[i].Key,
                    Rank     = i + 1,
                    Score    = ordered[i].Value, // 라운드 총점(0..1000)
                    Topic    = (byte)_topic.Value
                });
            }

            _roundWinner.Value = ordered.Count > 0 ? ordered[0].Key : ulong.MaxValue;

            Debug.Log($"[Match] R{_round.Value} {_topic.Value} end: players={_players.Count} " +
                      $"roundWin={_roundWinner.Value} topTotal={(ordered.Count > 0 ? ordered[0].Value : 0)}");
        }

        // 모드별 클리어 점수(0..1000 정규화).
        private float ComputeClearScore(PlayerRuntime pr, float dur, int M, Dictionary<ulong, int> finishRank)
        {
            switch (_topic.Value)
            {
                case TopicMode.Race:
                    // 완주자: 500 + 500*(M-rank)/max(M-1,1). 미완주: 500*progress(0..1).
                    if (pr.Finished && finishRank.TryGetValue(pr.ClientId, out int rank))
                        return 500f + 500f * (M - rank) / Mathf.Max(M - 1, 1);
                    return 500f * Mathf.Clamp01(pr.Progress);

                case TopicMode.Height:
                    // 1000 * clamp(peakY / HEIGHT_MAX, 0, 1).
                    return 1000f * Mathf.Clamp01(pr.PeakY / HEIGHT_MAX);

                case TopicMode.Survive:
                    // 1000 * aliveTime / roundDuration. 생존자는 roundDuration, 탈락자는 사망시각.
                    float aliveTime = pr.Alive ? dur : pr.DeathTime;
                    return dur <= 0f ? 0f : 1000f * Mathf.Clamp01(aliveTime / dur);

                default:
                    return 0f;
            }
        }

        // 최종 순위 1위 = 누적 총점 최고.
        private void ComputeMatchWinner()
        {
            ulong best = ulong.MaxValue;
            int bestScore = int.MinValue;
            foreach (var kv in _totalScore)
                if (kv.Value > bestScore) { bestScore = kv.Value; best = kv.Key; }
            _matchWinner.Value = best;
        }

        // ============ Owner-authoritative teleport bridge (server -> owning client) ============
        // 서버 측 헬퍼: 한 소유 클라에게 자기 플레이어를 텔레포트하라고 요청.
        private void TeleportOwner(ulong clientId, Vector3 pos)
        {
            TeleportPlayerRpc(pos, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        // 대상 클라에서만 실행(호스트가 자기 자신을 대상으로 하면 로컬에서도 실행).
        // 클라가 자기 플레이어(=ClientNetworkTransform 권위)를 텔레포트한다.
        [Rpc(SendTo.SpecifiedInParams)]
        private void TeleportPlayerRpc(Vector3 pos, RpcParams rpcParams = default)
        {
            var po = NetworkManager.LocalClient != null ? NetworkManager.LocalClient.PlayerObject : null;
            if (po == null) return;

            // 소유자 권위 경로: PlayerController.TeleportTo(Vector3) 를 이름으로 호출(컴파일 의존성 0).
            po.gameObject.SendMessage("TeleportTo", pos, SendMessageOptions.DontRequireReceiver);

            // Day4 단독 테스트용 폴백(평소엔 OFF). TeleportTo 가 이미 있으므로 ON 이면 이중 텔레포트.
            if (builtInTeleportFallback)
                BuiltInTeleport(po, pos);
        }

        // Day4 테스트 폴백 전용 자체 텔레포트. 소유자(=권위)에서 실행되므로 자기 transform WRITE 는 유효/복제됨.
        private static void BuiltInTeleport(NetworkObject po, Vector3 pos)
        {
            var cc = po.GetComponent<CharacterController>();
            bool wasEnabled = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;

            po.transform.position = pos;

            var nt = po.GetComponent<NetworkTransform>();
            if (nt != null) nt.Teleport(pos, po.transform.rotation, po.transform.localScale);

            if (cc != null) cc.enabled = wasEnabled;
        }

        // ============================ Debug GUI (씬 UI 불필요) ============================
        private void OnGUI()
        {
            if (!showDebugGui) return;
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return;

            double now = IsSpawned ? nm.ServerTime.Time : 0d;
            float rem = Mathf.Max(0f, (float)(_phaseEndTime.Value - now));
            string role = IsServer ? (IsHost ? "Host" : "Server") : "Client";

            // NetworkBootstrap 패널(x:10..270) 오른쪽에 배치.
            GUILayout.BeginArea(new Rect(280, 10, 380, 420), GUI.skin.box);
            GUILayout.Label($"[MatchManager] role={role}");
            GUILayout.Label($"Phase   : {_phase.Value}");
            GUILayout.Label($"Round   : {_round.Value}/{Course.ROUNDS}");
            GUILayout.Label($"Topic   : {_topic.Value}");
            GUILayout.Label($"Time    : {rem:0.0}s");
            GUILayout.Label($"Alive   : {_aliveCount.Value}");
            GUILayout.Label($"Votes   : R{RaceVotes.Value} H{HeightVotes.Value} S{SurviveVotes.Value}");
            if (IsServer)
            {
                GUILayout.Label($"Players : {_players.Count} (server)");
                GUILayout.Label($"Obstacle: {_obstacles.Count} placed");
            }
            // 내 누적 점수: 복제된 결과 행(라운드 총점)에서 내 것만 합산 -> 별도 복제 불필요.
            ulong me = nm.LocalClientId;
            int myScore = 0;
            for (int i = 0; i < _results.Count; i++)
                if (_results[i].ClientId == me) myScore += (int)_results[i].Score;
            GUILayout.Label($"MyScore : {myScore} (id {me})");
            GUILayout.Label($"RoundWin: {WinnerLabel(_roundWinner.Value)}");
            GUILayout.Label($"MatchWin: {WinnerLabel(_matchWinner.Value)}");
            GUILayout.Label($"Results : {_results.Count} rows");
            if (fastMode) GUILayout.Label($"FAST x{fastMultiplier}");
            GUILayout.EndArea();
        }

        private static string WinnerLabel(ulong id) => id == ulong.MaxValue ? "-" : id.ToString();
    }
}
