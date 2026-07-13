using UnityEngine;

namespace RouletteParty.UI
{
    /// <summary>
    /// 플레이어 색/이름의 단일 접근점. HUD 스코어보드·머리 위 이름표·대기방이 공유해 항상 일치한다.
    ///  - 색: clientId 로 결정론 계산(모든 피어 동일 -> 동기화 불필요).
    ///  - 이름: 대기방 닉네임(LobbyManager.Players, 전 피어 복제) 우선, 없으면 P{n} 폴백.
    /// </summary>
    public static class PlayerPalette
    {
        // 8색 파티 팔레트(서로 잘 구분되는 채도 높은 색).
        static readonly Color[] Colors =
        {
            new Color(0.93f, 0.26f, 0.31f), // red
            new Color(0.25f, 0.55f, 0.96f), // blue
            new Color(0.35f, 0.80f, 0.38f), // green
            new Color(0.98f, 0.75f, 0.18f), // yellow
            new Color(0.72f, 0.40f, 0.93f), // purple
            new Color(0.98f, 0.55f, 0.20f), // orange
            new Color(0.30f, 0.82f, 0.80f), // cyan
            new Color(0.95f, 0.45f, 0.72f), // pink
        };

        public static Color ColorFor(ulong clientId) => Colors[(int)(clientId % (ulong)Colors.Length)];

        /// <summary>대기방에서 정한 닉네임(전 피어 복제)이 있으면 사용, 없으면 P{n} 폴백.</summary>
        public static string NameFor(ulong clientId)
        {
            var lm = RouletteParty.Match.LobbyManager.Instance;
            if (lm != null && lm.IsSpawned)
            {
                string n = lm.NameOf(clientId);
                if (!string.IsNullOrEmpty(n)) return n;
            }
            return $"P{clientId + 1}";
        }
    }
}
