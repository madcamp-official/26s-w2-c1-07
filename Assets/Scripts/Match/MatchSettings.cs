using Unity.Netcode;
using UnityEngine;

namespace RouletteParty.Match
{
    /// <summary>
    /// 라운드 한 판의 진행 설정(준비 시간/등반 시간/구조물 개수).
    /// 구조물 개수는 "그 라운드에 받는 총 개수"이며, 그중 정확히 1개가 투명(함정)이다
    /// (투명 몫은 MatchManager._invisiblePerRound).
    /// </summary>
    public struct RoundSetup
    {
        public float Prep;  // 준비(구조물 설치) 시간(초)
        public float Play;  // 등반(플레이) 시간(초)
        public int   Count; // 구조물 지급 총 개수
    }

    /// <summary>
    /// 매치 전체 설정(라운드별). 호스트가 대기방에서 고르고 전 클라에 복제된다.
    ///
    /// 라운드 수(Climb.ROUNDS = 3)가 고정이라 배열이 아니라 고정 필드로 편다:
    /// NetworkVariable 은 관리되는 배열을 싣지 못하고, NetworkList 로 쪼개면
    /// "표준 모드" 버튼처럼 9개 값을 한 번에 바꾸는 갱신이 원자적이지 않다.
    /// 라운드 수를 늘리려면 필드와 Get/Set 을 함께 늘린다.
    /// </summary>
    public struct MatchSetup : INetworkSerializable, System.IEquatable<MatchSetup>
    {
        public RoundSetup R1, R2, R3;

        /// <summary>라운드(1~3) 설정 읽기. 범위 밖은 가장 가까운 라운드로 클램프.</summary>
        public RoundSetup Get(int round) => round <= 1 ? R1 : round == 2 ? R2 : R3;

        /// <summary>라운드(1~3) 설정 쓰기. 범위 밖은 가장 가까운 라운드로 클램프.</summary>
        public void Set(int round, RoundSetup v)
        {
            if (round <= 1) R1 = v;
            else if (round == 2) R2 = v;
            else R3 = v;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref R1.Prep); s.SerializeValue(ref R1.Play); s.SerializeValue(ref R1.Count);
            s.SerializeValue(ref R2.Prep); s.SerializeValue(ref R2.Play); s.SerializeValue(ref R2.Count);
            s.SerializeValue(ref R3.Prep); s.SerializeValue(ref R3.Play); s.SerializeValue(ref R3.Count);
        }

        public bool Equals(MatchSetup o) =>
            Same(R1, o.R1) && Same(R2, o.R2) && Same(R3, o.R3);

        private static bool Same(RoundSetup a, RoundSetup b) =>
            Mathf.Approximately(a.Prep, b.Prep) && Mathf.Approximately(a.Play, b.Play) && a.Count == b.Count;

        public override bool Equals(object obj) => obj is MatchSetup o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + R1.Prep.GetHashCode(); h = h * 31 + R1.Play.GetHashCode(); h = h * 31 + R1.Count;
                h = h * 31 + R2.Prep.GetHashCode(); h = h * 31 + R2.Play.GetHashCode(); h = h * 31 + R2.Count;
                h = h * 31 + R3.Prep.GetHashCode(); h = h * 31 + R3.Play.GetHashCode(); h = h * 31 + R3.Count;
                return h;
            }
        }
    }

    /// <summary>
    /// 호스트가 대기방에서 고른 매치 진행 설정의 서버 전용 소비 지점.
    ///
    /// 씬 분리로 LobbyManager(대기방 씬)와 MatchManager(게임 씬)가 공존하지 않으므로
    /// 정적 값으로 전달한다 — 페이즈 시간/지급 개수는 서버 권위라 서버 값만 의미 있다
    /// (클라의 이 값은 쓰이지 않는다. 표시는 LobbyManager 의 복제 상태를 읽는다).
    ///
    /// 0 이하 = 미설정 -> MatchManager 인스펙터 기본값 사용. 그 외 연출 페이즈
    /// (로비/하이라이트/대기/결과) 시간은 MatchManager 인스펙터에서 조절한다.
    /// </summary>
    public static class MatchSettings
    {
        private static MatchSetup s_setup; // 전부 0 = 미설정

        /// <summary>서버가 대기방 확정 설정을 주입한다(LobbyManager).</summary>
        public static void Apply(MatchSetup setup) => s_setup = setup;

        /// <summary>준비 시간(초). 0 이하 = 미설정.</summary>
        public static float PrepOf(int round) => s_setup.Get(round).Prep;

        /// <summary>등반 시간(초). 0 이하 = 미설정.</summary>
        public static float PlayOf(int round) => s_setup.Get(round).Play;

        /// <summary>구조물 지급 총 개수. 0 이하 = 미설정.</summary>
        public static int CountOf(int round) => s_setup.Get(round).Count;
    }
}
