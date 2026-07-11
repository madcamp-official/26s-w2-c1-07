using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

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
    ///      동일한 랜덤 타워를 로컬 생성.
    ///  (c) 체력: 라운드 시작 시 roundHp. 서버 전용(복제/표시 금지, 비공개 정보).
    ///      낙하 데미지는 소유자가 착지 시 보고(ReportFall)하고 서버가 차감·사망 판정.
    ///  (d) 점수: 라운드 종료 시점 높이 / mapHeight x heightScoreMax + 순위 보너스. 3라운드 누적.
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
        [Tooltip("우상단 디버그 패널 표시.")]
        [SerializeField] private bool showDebugGui = true;

        [Header("페이즈 시간(초)")]
        [SerializeField] private float _lobbyDuration     = 3f;
        [SerializeField] private float _prepDuration      = 60f;
        [SerializeField] private float _playDuration      = 180f;
        [SerializeField] private float _highlightDuration = 8f;
        [SerializeField] private float _resultDuration    = 14f;
        [Tooltip("첫 정상 도달자가 나오면 잔여 타이머를 이 값으로 단축.")]
        [SerializeField] private float _finishGrace = 15f;

        [Header("스폰")]
        [Tooltip("출발 지점 플레이어 간 X 간격.")]
        [SerializeField] private float spawnSpacingX = 1.2f;

        [Header("체력 / 낙하 데미지 (서버 전용, 비공개)")]
        [SerializeField] private int _roundHp = 10;
        [Tooltip("낙하 데미지가 들어오는 최소 낙하 높이.")]
        [SerializeField] private float _fallDamageMinHeight = 5f;
        [Tooltip("기준 데미지(최소 높이 도달 시).")]
        [SerializeField] private float _fallDamageBase = 2f;
        [Tooltip("최소 높이 초과 1m 당 추가 데미지.")]
        [SerializeField] private float _fallDamagePerMeter = 1.5f;

        [Header("구조물 지급/설치")]
        [Tooltip("라운드별 보이는 구조물 지급 개수(인덱스 0 = 라운드 1). 이월 없음.")]
        [SerializeField] private int[] _visibleAllowance = { 3, 2, 1 };
        [Tooltip("라운드별 보이지 않는 구조물 지급 개수(인덱스 0 = 라운드 1). 이월 없음.")]
        [SerializeField] private int[] _invisibleAllowance = { 1, 2, 3 };
        [Tooltip("구조물 간 최소 간격(겹침 방지, 설치 검증).")]
        [SerializeField] private float _minSpacing = 0.8f;
        [Tooltip("구조물 프리팹(NetworkObject + Obstacle). NetworkPrefabs 리스트 등록 필수.")]
        [SerializeField] private GameObject _wallPrefab;
        [SerializeField] private GameObject _cylinderPrefab;
        [SerializeField] private GameObject _ghostPrefab;

        [Header("점수")]
        [Tooltip("높이 점수 만점(종료 시점 높이 / mapHeight x 이 값).")]
        [SerializeField] private float _heightScoreMax = 700f;
        [Tooltip("종료 높이 순위 보너스(1위부터). 배열 밖 순위는 0.")]
        [SerializeField] private int[] _rankBonus = { 300, 200, 100 };

        // ============================ 복제 상태(서버 write) ============================
        private NetworkVariable<MatchPhase> _phase        = new NetworkVariable<MatchPhase>(MatchPhase.Lobby);
        private NetworkVariable<int>        _round        = new NetworkVariable<int>(0);
        private NetworkVariable<double>     _phaseEndTime = new NetworkVariable<double>(0d);
        private NetworkVariable<int>        _aliveCount   = new NetworkVariable<int>(0);
        private NetworkVariable<ulong>      _roundWinner  = new NetworkVariable<ulong>(ulong.MaxValue);
        private NetworkVariable<ulong>      _matchWinner  = new NetworkVariable<ulong>(ulong.MaxValue);
        /// <summary>맵 랜덤 생성 시드(매치당 1회 갱신). 전 피어가 이 시드로 동일 맵을 로컬 생성.</summary>
        public NetworkVariable<int> MapSeed = new NetworkVariable<int>(0);

        // 라운드별 순위 행(클라 점수판/하이라이트/결과 UI 데이터)
        private NetworkList<RoundResult> _results = new NetworkList<RoundResult>();

        // ============================ 서버 전용 상태 ============================
        private readonly Dictionary<ulong, PlayerRuntime> _players    = new Dictionary<ulong, PlayerRuntime>();
        private readonly Dictionary<ulong, int>           _totalScore = new Dictionary<ulong, int>();
        private readonly System.Random _rng = new System.Random();
        private bool _graceApplied;

        // 이번 PREP 에 사용한 설치 수(이월 없음 규칙: PREP 진입마다 리셋)
        private readonly Dictionary<ulong, int> _prepVisibleUsed   = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, int> _prepInvisibleUsed = new Dictionary<ulong, int>();

        // 매치 동안 스폰된 구조물 기록(겹침 검증·매치 종료 일괄 정리용)
        private struct StructRec { public NetworkObject No; public Vector3 Pos; }
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
        /// <summary>현재 라운드의 지급 개수(클라 UI 표시용).</summary>
        public int VisibleGrant   => GrantOf(_visibleAllowance, _round.Value);
        public int InvisibleGrant => GrantOf(_invisibleAllowance, _round.Value);
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

        private void HandleClientConnected(ulong clientId) => EnsurePlayer(clientId);

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

            if (_phase.Value == MatchPhase.Play) SamplePlayers(now);
            else                                 RescueFallenPlayers();

            if (now >= _phaseEndTime.Value) { AdvancePhase(); return; }

            // 전원 탈락 시 조기 종료(2인 이상 매치에서만; 솔로 테스트는 풀타이머).
            if (_phase.Value == MatchPhase.Play && _players.Count >= 2 && _aliveCount.Value == 0)
                AdvancePhase();
        }

        private float BaseDuration(MatchPhase p)
        {
            switch (p)
            {
                case MatchPhase.Lobby:     return _lobbyDuration;
                case MatchPhase.Prep:      return _prepDuration;
                case MatchPhase.Play:      return _playDuration;
                case MatchPhase.Highlight: return _highlightDuration;
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

            switch (p)
            {
                case MatchPhase.Lobby:     ResetMatch(); break;
                case MatchPhase.Prep:      BeginPrep();  break;
                case MatchPhase.Play:      BeginPlay();  break;
                case MatchPhase.Highlight: EndPlayEvaluate(); break;
                case MatchPhase.Result:    ComputeMatchWinner(); break;
            }
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
                    {
                        _round.Value++;
                        EnterPhase(MatchPhase.Prep); // 매 라운드 전 준비(핵심 변경점)
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
            _results.Clear();
            _round.Value = 0;
            _roundWinner.Value = ulong.MaxValue;
            _matchWinner.Value = ulong.MaxValue;

            DespawnAllStructures();          // 구조물은 매치 리셋에만 전체 제거(라운드 간 누적 유지)
            MapSeed.Value = _rng.Next(1, int.MaxValue); // 매 매치 새 랜덤 타워
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
            int i = 0, n = NetworkManager.ConnectedClientsList.Count;
            foreach (var c in NetworkManager.ConnectedClientsList)
            {
                EnsurePlayer(c.ClientId);
                _players[c.ClientId].ResetForRound(_roundHp, i);
                SetDead(c.ClientId, false);
                TeleportOwner(c.ClientId, SpawnSlot(i, n));
                i++;
            }
            _aliveCount.Value = _players.Count;
        }

        // 바닥 스폰 슬롯: 인원수 기반 센터링(맵 폭 안으로 클램프).
        // 주의: MapSurface(위->아래 Ground 레이캐스트)를 쓰면 타워 구조물 "꼭대기"에 맞아
        // 정상 스폰(즉시 만점) 버그가 나므로, 바닥(y=0) 고정 높이로 스폰한다.
        private Vector3 SpawnSlot(int index, int total)
        {
            float halfX = (ClimbMapGenerator.Instance != null ? ClimbMapGenerator.Instance.MapWidth : 6f) * 0.5f - 0.6f;
            float x = (index - (Mathf.Max(1, total) - 1) * 0.5f) * spawnSpacingX;
            x = Mathf.Clamp(x, -halfX, halfX);
            return new Vector3(x, 1.2f, 0f);
        }

        // 비 PLAY 페이즈 안전망: 바닥 아래로 샌 플레이어 구조.
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

        // PLAY 중 서버 샘플링: 정상 도달 감지(위치는 READ 만).
        private void SamplePlayers(double now)
        {
            float dur = EffectiveDuration(MatchPhase.Play);
            float elapsed = dur - (float)(_phaseEndTime.Value - now);
            float top = MapHeight;

            foreach (var c in NetworkManager.ConnectedClientsList)
            {
                if (!_players.TryGetValue(c.ClientId, out var pr) || !pr.Alive) continue;
                var po = c.PlayerObject;
                if (po == null) continue;

                float y = po.transform.position.y;
                if (y < -5f) TeleportOwner(c.ClientId, SpawnSlot(pr.SpawnIndex, _players.Count)); // 안전망

                if (!pr.ReachedTop && y >= top)
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

        // ============================ 낙하 데미지 / 탈락 (서버) ============================
        /// <summary>소유 클라의 착지 보고 처리. PlayerController.ReportFallServerRpc 가 서버에서 호출.</summary>
        public void ApplyFallDamage(ulong clientId, float fallHeight)
        {
            if (!IsServer || _phase.Value != MatchPhase.Play) return;
            if (!_players.TryGetValue(clientId, out var pr) || !pr.Alive) return;
            if (fallHeight < _fallDamageMinHeight) return;

            int dmg = Mathf.CeilToInt(_fallDamageBase + (fallHeight - _fallDamageMinHeight) * _fallDamagePerMeter);
            pr.Hp -= dmg; // 체력은 서버 전용(비공개) — 로그도 남기지 않는다.

            if (pr.Hp <= 0)
            {
                pr.Alive = false;
                var po = NetworkManager.ConnectedClients.TryGetValue(clientId, out var cl) ? cl.PlayerObject : null;
                pr.DeathHeight = po != null ? Mathf.Max(0f, po.transform.position.y) : 0f;
                SetDead(clientId, true);
                _aliveCount.Value = Mathf.Max(0, _aliveCount.Value - 1);
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
        /// PREP 중 클라 -> 서버 설치 요청. 검증: PREP / 종류별 잔여 개수 / 볼륨 경계 / 겹침.
        /// yawStep = 90도 단위 회전(0~3). Ghost 타입 = 보이지 않는 구조물.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void PlaceStructureServerRpc(Vector3 pos, byte yawStep, byte typeByte, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            var type = (ObstacleType)typeByte;
            bool invisible = type == ObstacleType.Ghost;

            if (_phase.Value != MatchPhase.Prep) return;

            // 종류별 잔여 개수(이월 없음).
            var used = invisible ? _prepInvisibleUsed : _prepVisibleUsed;
            int grant = invisible ? InvisibleGrant : VisibleGrant;
            used.TryGetValue(sender, out int usedCount);
            if (usedCount >= grant) return;

            // 볼륨 경계.
            var gen = ClimbMapGenerator.Instance;
            if (gen == null || !gen.InsideVolume(pos, 0.1f)) return;

            // 겹침 검사는 "플레이어 설치 구조물끼리"만 한다(간격 _minSpacing).
            // 랜덤 타워는 슬라이스당 2개씩 매우 밀집해 있어 물리 겹침 검사를 하면 사실상
            // 어디에도 설치할 수 없다. 타워 발판에 끼워/걸쳐 설치하는 것은 게임 규칙상 유효.
            for (int i = 0; i < _structures.Count; i++)
                if (Vector3.Distance(_structures[i].Pos, pos) < _minSpacing) return;

            GameObject prefab = PrefabFor(type);
            if (prefab == null) { Debug.LogWarning($"[Match] structure prefab for {type} not assigned."); return; }

            GameObject go = Instantiate(prefab);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yawStep * 90f, 0f));
            var no = go.GetComponent<NetworkObject>();
            no.Spawn(true);

            // 종류/설치자는 반드시 Spawn "이후"에 기록한다: 스폰 시 NetworkVariable 이 초기값으로
            // 리셋돼 스폰 전 쓰기가 유실되는 문제를 실측으로 확인(서버 쓰기는 델타 복제로 전 클라 반영).
            var ob = go.GetComponent<Obstacle>();
            if (ob != null) ob.ServerInit(type, sender);

            _structures.Add(new StructRec { No = no, Pos = pos });
            used[sender] = usedCount + 1;
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

        /// <summary>보이지 않는 구조물과의 충돌 보고 -> 전원 일시 공개(RevealUntil 갱신).</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RevealStructureServerRpc(NetworkObjectReference structRef, RpcParams rpcParams = default)
        {
            if (!structRef.TryGet(out NetworkObject no)) return;
            var ob = no.GetComponent<Obstacle>();
            if (ob == null || !ob.IsInvisibleKind) return;
            ob.RevealUntil.Value = NetworkManager.ServerTime.Time + ob.RevealDuration;
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
        // 라운드 점수 = 종료 시점 높이/mapHeight x heightScoreMax + 순위 보너스. 3라운드 누적.
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
                    y = po != null ? Mathf.Clamp(po.transform.position.y, 0f, top) : 0f;
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
                float heightScore = top <= 0f ? 0f : Mathf.Clamp01(rows[i].finalY / top) * _heightScoreMax;
                int bonus = (_rankBonus != null && i < _rankBonus.Length) ? _rankBonus[i] : 0;
                int totalScore = Mathf.RoundToInt(heightScore) + bonus;

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
            TeleportPlayerRpc(pos, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void TeleportPlayerRpc(Vector3 pos, RpcParams rpcParams = default)
        {
            var po = NetworkManager.LocalClient != null ? NetworkManager.LocalClient.PlayerObject : null;
            if (po == null) return;
            po.gameObject.SendMessage("TeleportTo", pos, SendMessageOptions.DontRequireReceiver);
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
