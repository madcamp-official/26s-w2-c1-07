using UnityEngine;

namespace RouletteParty.Match
{
    /// <summary>
    /// 호스트가 대기방에서 고른 매치 진행 설정(서버 전용 소비).
    /// 씬 분리로 LobbyManager(대기방 씬)와 MatchManager(게임 씬)가 공존하지 않으므로
    /// 정적 값으로 전달한다 - 페이즈 시간은 서버 권위(_phaseEndTime 복제)라 서버 값만 의미 있다.
    /// 0 이하 = 미설정(MatchManager 인스펙터 기본값 사용). 그 외 연출 페이즈(로비/하이라이트/
    /// 대기/결과) 시간은 MatchManager 인스펙터에서 페이즈별로 조절한다.
    /// </summary>
    public static class MatchSettings
    {
        /// <summary>준비(구조물 설치) 페이즈 시간(초). 0 이하 = 인스펙터 기본값.</summary>
        public static float PrepSeconds;

        /// <summary>등반(플레이) 페이즈 시간(초). 0 이하 = 인스펙터 기본값.</summary>
        public static float PlaySeconds;
    }
}
