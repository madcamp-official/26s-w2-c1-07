using UnityEngine;

namespace RouletteParty.Match
{
    /// <summary>
    /// 라운드 점수 설정. MatchManager 의 [SerializeField] 로 인스펙터에 인라인 노출된다.
    ///
    /// 확장 절차(새 점수 요소 추가 시):
    ///  1) 여기에 설정 필드 추가(가중치/곡선 등)
    ///  2) RoundPerformance 에 입력 필드 추가(MatchManager.EndPlayEvaluate 가 채움)
    ///  3) MatchScoring.RoundScore 에 항 하나 추가
    /// 점수 공식은 이 파일 밖에 존재하지 않는다(단일 지점).
    /// </summary>
    [System.Serializable]
    public class ScoringConfig
    {
        [Tooltip("높이 점수 만점(정규화 높이 1.0 = 정상 도달).")]
        public float heightScoreMax = 700f;

        [Tooltip("정규화 높이(0~1) -> 점수 비율(0~1) 곡선. 직선 = 높이 비례(기존과 동일). " +
                 "곡선을 위로 볼록/오목하게 바꾸면 하단/상단 구간의 가치를 코드 수정 없이 조정할 수 있다.")]
        public AnimationCurve heightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("최종 순위 보너스(인덱스 0 = 1등). 배열 길이 밖 순위는 0.")]
        public int[] rankBonus = { 300, 200, 100 };
    }

    /// <summary>
    /// 한 플레이어의 라운드 성적(점수 계산 입력). 서버(MatchManager)가 라운드 종료 시 채운다.
    /// 지금 안 쓰는 필드(ReachedTop/TopTime)도 미리 실어 두어, 시간 보너스 같은 요소를
    /// 추가할 때 수집 코드를 다시 만들 필요가 없다.
    /// </summary>
    public struct RoundPerformance
    {
        public float NormalizedHeight; // 종료 시점 발끝 높이 / mapHeight (0~1)
        public int   Rank;             // 0-based 최종 순위(0 = 1등)
        public bool  ReachedTop;       // 정상 도달 여부
        public float TopTime;          // 정상 도달 시각(라운드 경과 초, 도달자만 유효)
    }

    /// <summary>
    /// 라운드 점수 계산기. 순수 함수(상태 없음) — 서버만 호출하지만 어디서 불러도 안전하고,
    /// 입력/설정만 주면 되므로 밸런스 실험·테스트가 쉽다.
    /// </summary>
    public static class MatchScoring
    {
        public static int RoundScore(in RoundPerformance p, ScoringConfig cfg)
        {
            float h01 = Mathf.Clamp01(p.NormalizedHeight);
            float heightScore = cfg.heightCurve.Evaluate(h01) * cfg.heightScoreMax;

            int bonus = (cfg.rankBonus != null && p.Rank >= 0 && p.Rank < cfg.rankBonus.Length)
                ? cfg.rankBonus[p.Rank] : 0;

            return Mathf.RoundToInt(heightScore) + bonus;
        }
    }
}
