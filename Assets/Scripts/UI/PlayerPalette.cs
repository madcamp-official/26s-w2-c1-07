using UnityEngine;

namespace RouletteParty.UI
{
    /// <summary>
    /// 플레이어 색/이름을 clientId 로부터 결정론적으로 계산한다.
    /// 모든 피어가 같은 clientId -> 같은 색/이름이므로 네트워크 동기화가 필요 없다.
    /// HUD 스코어보드와 머리 위 이름표가 이 헬퍼를 공유해 항상 일치한다.
    /// (커스텀 닉네임/색 선택은 아트 주간의 확장 과제 — 그때 PlayerIdentity 로 대체.)
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

        public static string NameFor(ulong clientId) => $"P{clientId + 1}";
    }
}
