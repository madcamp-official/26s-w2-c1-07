using Unity.Netcode;

namespace RouletteParty.Match
{
    // =====================================================================================
    // 클라이밍 전환(docs/클라이밍_전환_명세서.md) 후의 매치 도메인 타입.
    // 룰렛/주제/게임 모드(IGameMode·Race/Height/Survive)/Course 는 전부 폐기: 단일 클라이밍 모드.
    //  - 복제 표시 상태(페이즈/라운드/타이머/승자/맵 시드)는 MatchManager 의 NetworkVariable.
    //  - 라운드 판정 데이터(체력/사망 높이/정상 도달)는 서버 전용 PlayerRuntime(비복제).
    //    체력은 플레이어 비공개 정보이므로 절대 복제/표시하지 않는다.
    //  - 라운드 순위는 기존 NetworkList<RoundResult> 를 그대로 재사용(직렬화 안정).
    // =====================================================================================

    /// <summary>매치 FSM: LOBBY -> [PREP -> PLAY -> HIGHLIGHT] x3 -> RESULT -> loop. (룰렛 없음)</summary>
    public enum MatchPhase : byte { Lobby, Prep, Play, Highlight, Result }

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
        public int   Hp;             // 은닉 체력(서버 전용, 비공개)
        public bool  Alive = true;
        public float DeathHeight;    // 탈락 지점 높이 -> 점수 계산에 사용
        public bool  ReachedTop;     // 정상(y >= mapHeight) 도달 여부
        public float TopTime;        // 정상 도달 시각(PLAY 경과 초, 동률 tie-break)

        public void ResetForRound(int hp, int spawnIndex)
        {
            Hp = hp;
            SpawnIndex = spawnIndex;
            Alive = true;
            DeathHeight = 0f;
            ReachedTop = false;
            TopTime = 0f;
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
