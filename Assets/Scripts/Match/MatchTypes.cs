using Unity.Netcode;

namespace RouletteParty.Match
{
    // =====================================================================================
    // 클라이밍 전환(docs/클라이밍_전환_명세서.md) 후의 매치 도메인 타입.
    // 룰렛/주제/게임 모드(IGameMode·Race/Height/Survive)/Course 는 전부 폐기: 단일 클라이밍 모드.
    //  - 복제 표시 상태(페이즈/라운드/타이머/승자/맵 시드)는 MatchManager 의 NetworkVariable.
    //  - 라운드 판정 데이터(낙하 추적/사망 높이/정상 도달)는 서버 전용 PlayerRuntime(비복제).
    //    체력 시스템은 폐지 — 탈락은 낙하 거리 2규칙(MatchManager 시리얼라이즈드)로만 판정한다.
    //  - 라운드 순위는 기존 NetworkList<RoundResult> 를 그대로 재사용(직렬화 안정).
    // =====================================================================================

    /// <summary>
    /// 매치 FSM: LOBBY -> [PREP -> PLAY -> HIGHLIGHT -> INTERMISSION] x3(마지막회차는 INTERMISSION 대신 RESULT) -> loop.
    /// INTERMISSION = 하이라이트 연출이 끝난 뒤 다음 라운드 PREP 로 곧장 넘어가면 어색하므로 두는 라운드 간 대기 구간.
    /// </summary>
    public enum MatchPhase : byte { Lobby, Prep, Play, Highlight, Intermission, Result }

    /// <summary>엔진 전역 상수(라운드 수만 상수, 나머지 튜닝값은 전부 SerializeField).</summary>
    public static class Climb
    {
        public const int ROUNDS = 3;
    }

    /// <summary>
    /// 서버 전용 per-player 라운드 판정 데이터. NOT networked.
    /// (최종 순위만 RoundResult 행으로 복제된다.)
    /// </summary>
    public class PlayerRuntime
    {
        public ulong ClientId;
        public int   SpawnIndex;
        public bool  Alive = true;
        public float DeathHeight;    // 탈락 지점 높이 -> 점수 계산에 사용
        public bool  ReachedTop;     // 정상(도착 청크) 도달 여부
        public float TopTime;        // 최초 정상 도달 시각(PLAY 시작 기준 경과 초, 시간 점수·동률 tie-break)

        // ---- 점수 수집(개편안): 라운드 중 사실만 기록, 계산은 MatchScoring 이 종료 시 1회 ----
        public float BestY;          // 라운드 최고 발끝 높이(진행도·안정성 입력). 탈락·부활에도 유지 — 낙하 추적용 ApexY 와 별개
        public int   Deaths;         // 라운드 탈락 횟수(반복 탈락 감점·안정성 보너스 입력)
        public int   MaxChunk;       // 도달한 최고 청크 순번(0 = 시작 청크만). 상위 진입 시 하위 자동 인정의 기준
        public readonly System.Collections.Generic.List<int> ChunkPlacements
            = new System.Collections.Generic.List<int>(); // 도달 청크별 선착 순번(1 = 최초)

        // ---- 서버 낙하 추적(탈락 규칙 ①: 공중 낙하 거리, MatchManager.SamplePlayers) ----
        public float ApexY;          // 낙하 기준 최고점(발끝 기준). 텔레포트/착지 시 리셋
        public float LastY;          // 직전 샘플 높이(수직 속도 추정용)
        public float FallStillTime;  // "하강 중 아님" 지속 시간(지지 상태 판정용)

        public void ResetForRound(int spawnIndex)
        {
            SpawnIndex = spawnIndex;
            Alive = true;
            DeathHeight = 0f;
            ReachedTop = false;
            TopTime = 0f;
            ApexY = 0f;
            LastY = 0f;
            FallStillTime = 0f;
            BestY = 0f;
            Deaths = 0;
            MaxChunk = 0;
            ChunkPlacements.Clear();
        }
    }

    /// <summary>
    /// 복제되는 라운드 결과 행(기존 구조 유지: NetworkList 직렬화 안정성).
    /// Topic 필드는 컨셉 전환 후 미사용(0 고정)으로 남겨 와이어 포맷을 보존한다.
    /// </summary>
    public struct RoundResult : INetworkSerializable, System.IEquatable<RoundResult>
    {
        public int   Round;      // 1..ROUNDS
        public ulong ClientId;
        public int   Rank;       // 1 = round winner
        public float Score;      // 라운드 총점(높이 점수 + 순위 보너스)
        public byte  Topic;      // 미사용(0)

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Round);
            s.SerializeValue(ref ClientId);
            s.SerializeValue(ref Rank);
            s.SerializeValue(ref Score);
            s.SerializeValue(ref Topic);
        }

        public bool Equals(RoundResult o) =>
            Round == o.Round && ClientId == o.ClientId &&
            Rank == o.Rank && Score.Equals(o.Score) && Topic == o.Topic;

        public override bool Equals(object obj) => obj is RoundResult o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + Round;
                h = h * 31 + ClientId.GetHashCode();
                h = h * 31 + Rank;
                h = h * 31 + Score.GetHashCode();
                h = h * 31 + Topic;
                return h;
            }
        }
    }
}
