using UnityEngine;

namespace RouletteParty.Match
{
    /// <summary>
    /// 라운드 점수 설정(점수_시스템_개편안.md 반영). MatchManager 의 [SerializeField] 로 인스펙터에 노출된다.
    ///
    /// 확장 절차(새 점수 요소 추가 시):
    ///  1) 여기에 설정 필드 추가(만점/가중치/곡선/상한)
    ///  2) RoundPerformance 에 입력 필드 추가(MatchManager 가 라운드 중 수집해 채움)
    ///  3) MatchScoring.RoundScore 에 독립 항 하나 추가(+ RoundScoreBreakdown 필드)
    /// 점수 공식은 이 파일 밖에 존재하지 않는다(단일 지점). 실시간 계산/동기화 없음:
    /// 라운드 중에는 사실(성적 데이터)만 수집하고 PLAY -> HIGHLIGHT 전환에서 1회 계산한다.
    /// </summary>
    [System.Serializable]
    public class ScoringConfig
    {
        [Header("진행도 (최고 높이 + 종료 시점 높이)")]
        [Tooltip("진행도 점수 만점(정규화 높이 1.0 = 정상 도달).")]
        public float heightScoreMax = 700f;
        [Tooltip("정규화 높이(0~1) -> 점수 비율(0~1) 곡선. 직선 = 높이 비례. 최고/최종 높이에 공통 적용.")]
        public AnimationCurve heightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("최고 도달 높이 가중치."), Range(0f, 1f)]
        public float bestHeightWeight = 0.7f;
        [Tooltip("종료 시점 높이 가중치."), Range(0f, 1f)]
        public float finalHeightWeight = 0.3f;

        [Header("정상 도달 시간 (도달자만, 빠를수록 높음)")]
        [Tooltip("정상 도달 시간 점수 만점(0초 도달 가정).")]
        public float topTimeScoreMax = 300f;
        [Tooltip("남은 시간 비율(0~1) -> 점수 비율(0~1) 곡선. 분모는 최초 설정된 전체 라운드 시간(finishGrace 단축 무시).")]
        public AnimationCurve timeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("청크 선착순 (청크마다 독립 판정)")]
        [Tooltip("도달 순번별 보너스(인덱스 0 = 최초 도달). 배열 밖 순번은 0.")]
        public int[] chunkArrivalBonus = { 40, 20, 10 };

        [Header("순위 (참가자 수 비례 선형 배분)")]
        [Tooltip("순위 점수 만점(1위). 최하위 0, 중간은 선형. 1인 참가는 1위 취급(만점).")]
        public float rankScoreMax = 300f;

        [Header("안정성 (충분히 등반 + 무탈락)")]
        [Tooltip("안정성 보너스 점수.")]
        public int stabilityBonus = 150;
        [Tooltip("보너스 조건: 최고 높이 / 정상 높이 최소 비율(버티기 플레이 배제)."), Range(0f, 1f)]
        public float stabilityMinHeight01 = 0.6f;

        [Header("반복 탈락 감점 (첫 탈락 무료)")]
        [Tooltip("두 번째 탈락 감점.")]
        public int secondDeathPenalty = 10;
        [Tooltip("세 번째부터 탈락 1회당 감점.")]
        public int extraDeathPenalty = 20;
        [Tooltip("라운드당 최대 감점 한도(안전장치).")]
        public int deathPenaltyCap = 100;

        [Header("투명 구조물 영향 (설치자 보상)")]
        [Tooltip("상대가 내 투명 구조물 접촉 후 유효 시간 안에 탈락하면 설치자 +점(셀프 제외).")]
        public int baitScore = 40;
        [Tooltip("접촉 -> 탈락 인정 유효 시간(초). 낚시왕 통계(MatchStatsTracker)와 같은 창을 쓴다.")]
        public float baitWindowSeconds = 6f;
        [Tooltip("같은 (설치자, 피해자) 쌍의 라운드당 인정 횟수 한도(동일 피해자 반복 파밍 방지).")]
        public int baitRepeatLimit = 2;
        [Tooltip("라운드당 영향 점수 상한.")]
        public int baitScoreCap = 120;

        [Header("최종")]
        [Tooltip("라운드 최종 점수 하한(감점으로 이 밑으로 내려가지 않음).")]
        public int roundScoreMin = 0;
    }

    /// <summary>
    /// 한 플레이어의 라운드 성적(점수 계산 입력 = 라운드 중 수집된 사실).
    /// 서버(MatchManager.EndPlayEvaluate)가 라운드 종료 시 채운다. 네트워크 직렬화 아님 —
    /// 복제되는 것은 확정된 RoundResult(총점)뿐이다.
    /// </summary>
    public struct RoundPerformance
    {
        public float BestHeight01;    // 라운드 최고 발끝 높이 / 정상 높이. 정상 도달자 = 1 고정, 탈락·부활에도 유지
        public float FinalHeight01;   // 라운드 종료 시점 채점 높이 / 정상 높이
        public int   Rank;            // 0-based 최종 순위(0 = 1등)
        public int   PlayerCount;     // 라운드 채점 인원(순위 점수 선형 배분의 분모)
        public bool  ReachedTop;      // 정상 도달 여부(최초 도달만 기록, 이후 낙하해도 유지)
        public float TopTime;         // 최초 정상 도달 시각(PLAY 시작 기준 경과 초)
        public float RoundDuration;   // 최초 설정된 전체 라운드 시간(초, finishGrace 단축 무시)
        public int[] ChunkPlacements; // 도달한 청크별 선착 순번(1 = 최초). null/빈 배열 허용
        public int   Deaths;          // 라운드 총 탈락 횟수
        public int   BaitKills;       // 유효 투명 구조물 영향 횟수(셀프 제외·반복 제한 적용 후)
    }

    /// <summary>라운드 점수 내역(호스트 로그·밸런싱용). Total 만 RoundResult 로 복제된다.</summary>
    public struct RoundScoreBreakdown
    {
        public int Progress;     // 진행도(최고 x 0.7 + 최종 x 0.3)
        public int TopTimeScore; // 정상 도달 시간
        public int ChunkBonus;   // 청크 선착순 합
        public int RankScore;    // 참가자 수 비례 순위
        public int Stability;    // 안정성 보너스
        public int BaitScore;    // 투명 구조물 영향(상한 적용 후)
        public int DeathPenalty; // 반복 탈락 감점(양수로 기록, 총점에서 차감)
        public int Total;        // 최종(하한 적용 후)

        public override string ToString() =>
            $"total={Total} (progress={Progress} time={TopTimeScore} chunk={ChunkBonus} rank={RankScore} " +
            $"stability={Stability} bait={BaitScore} death=-{DeathPenalty})";
    }

    /// <summary>
    /// 라운드 점수 계산기. 순수 함수(상태 없음) — 수집 데이터와 설정만 주면 되므로 밸런스 실험과
    /// 테스트가 쉽고, 추후 전용 서버/실시간 계산으로 이 파일을 그대로 이전할 수 있다.
    /// </summary>
    public static class MatchScoring
    {
        public static RoundScoreBreakdown RoundScore(in RoundPerformance p, ScoringConfig cfg)
        {
            var b = new RoundScoreBreakdown();

            // 진행도: 최고 높이 x 가중치 + 최종 높이 x 가중치(곡선 공통 적용).
            float best01  = Mathf.Clamp01(p.BestHeight01);
            float final01 = Mathf.Clamp01(p.FinalHeight01);
            b.Progress = Mathf.RoundToInt(cfg.heightScoreMax *
                (cfg.bestHeightWeight  * cfg.heightCurve.Evaluate(best01) +
                 cfg.finalHeightWeight * cfg.heightCurve.Evaluate(final01)));

            // 정상 도달 시간: 도달자만. 남은 시간 비율 비례(분모 = 최초 전체 라운드 시간).
            if (p.ReachedTop && p.RoundDuration > 0f)
            {
                float remain01 = Mathf.Clamp01(1f - p.TopTime / p.RoundDuration);
                b.TopTimeScore = Mathf.RoundToInt(cfg.topTimeScoreMax * cfg.timeCurve.Evaluate(remain01));
            }

            // 청크 선착순: 청크마다 독립. 순번이 배열 밖이면 0.
            if (p.ChunkPlacements != null && cfg.chunkArrivalBonus != null)
                foreach (int place in p.ChunkPlacements)
                    if (place >= 1 && place <= cfg.chunkArrivalBonus.Length)
                        b.ChunkBonus += cfg.chunkArrivalBonus[place - 1];

            // 순위: 선형 배분(1위 만점, 최하위 0). 1인 참가는 1위 취급(0 나눗셈 방지).
            b.RankScore = p.PlayerCount <= 1
                ? Mathf.RoundToInt(cfg.rankScoreMax)
                : Mathf.RoundToInt(cfg.rankScoreMax * (p.PlayerCount - 1 - p.Rank) / (float)(p.PlayerCount - 1));

            // 안정성: 충분한 등반(최고 높이 비율) + 무탈락.
            if (best01 >= cfg.stabilityMinHeight01 && p.Deaths == 0)
                b.Stability = cfg.stabilityBonus;

            // 투명 구조물 영향(라운드 상한).
            b.BaitScore = Mathf.Min(p.BaitKills * cfg.baitScore, cfg.baitScoreCap);

            // 반복 탈락 감점: 0회/1회 = 0, 2회 = -10, 3회부터 회당 -20(라운드 상한).
            int penalty = 0;
            if (p.Deaths >= 2) penalty = cfg.secondDeathPenalty + Mathf.Max(0, p.Deaths - 2) * cfg.extraDeathPenalty;
            b.DeathPenalty = Mathf.Min(penalty, cfg.deathPenaltyCap);

            b.Total = Mathf.Max(cfg.roundScoreMin,
                b.Progress + b.TopTimeScore + b.ChunkBonus + b.RankScore + b.Stability + b.BaitScore - b.DeathPenalty);
            return b;
        }
    }
}
