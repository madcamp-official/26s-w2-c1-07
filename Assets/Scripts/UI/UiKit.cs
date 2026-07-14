using System.Collections.Generic;
using UnityEngine;

namespace RouletteParty.UI
{
    /// <summary>
    /// 코드 생성 UI 공용 키트. 얼티밋 치킨 호스 풍 디자인 시스템:
    /// 크림 패널 + 진한 잉크 테두리 + 채도 높은 포인트 컬러 + 볼드 타이포.
    /// 라운드 코너/테두리 텍스처는 런타임 1회 생성해 캐시하며,
    /// GameHUD(uGUI)와 LobbyUI/PrepClientUI(IMGUI)가 같은 톤을 쓰도록 단일 지점으로 관리한다.
    /// </summary>
    public static class UiKit
    {
        // ================= UCH 팔레트 =================
        public static readonly Color Cream     = new Color(0.957f, 0.925f, 0.863f, 1f); // 패널 바탕(종이 느낌)
        public static readonly Color CreamDark = new Color(0.894f, 0.850f, 0.760f, 1f); // 행 교차/입력 필드
        public static readonly Color Ink       = new Color(0.231f, 0.192f, 0.161f, 1f); // 테두리/본문 텍스트
        public static readonly Color InkSoft   = new Color(0.42f, 0.37f, 0.32f, 1f);    // 보조 텍스트
        public static readonly Color Red       = new Color(0.894f, 0.341f, 0.306f, 1f); // 시작/경고/포인트
        public static readonly Color Yellow    = new Color(0.965f, 0.769f, 0.271f, 1f); // 타이틀 배너/강조
        public static readonly Color Teal      = new Color(0.247f, 0.722f, 0.686f, 1f); // 참가/보조 액션
        public static readonly Color Blue      = new Color(0.306f, 0.561f, 0.851f, 1f); // 정보
        public static readonly Color Green     = new Color(0.435f, 0.750f, 0.294f, 1f); // 준비/성공
        public static readonly Color Grey      = new Color(0.604f, 0.561f, 0.502f, 1f); // 중립 버튼

        // ================= 레거시 다크 톤(2단계 HUD 개편 전까지 GameHUD 호환) =================
        public static readonly Color PanelBg     = new Color(0.07f, 0.09f, 0.15f, 0.80f);
        public static readonly Color PanelBgSoft = new Color(0.07f, 0.09f, 0.15f, 0.55f);
        public static readonly Color TextMain    = new Color(0.96f, 0.97f, 1f, 1f);
        public static readonly Color TextDim     = new Color(0.72f, 0.78f, 0.88f, 1f);
        public static readonly Color Accent      = new Color(1f, 0.83f, 0.35f, 1f);
        public static readonly Color Danger      = new Color(1f, 0.38f, 0.38f, 1f);

        // ================= 텍스처/스프라이트 생성 =================
        private const int TEX = 48;        // 텍스처 한 변(9-slice 원본)
        private const int RADIUS = 14;     // 코너 반지름(px)
        private const int BORDER = 4;      // 잉크 테두리 두께(px)
        /// <summary>IMGUI GUIStyle.border / uGUI 9-slice 경계값.</summary>
        public const int ImguiBorder = RADIUS + 2;

        private static Sprite s_round;                 // 테두리 없는 흰색(틴트용, 레거시)
        private static Texture2D s_imguiPanel;         // 레거시 다크 패널
        private static readonly Dictionary<long, Texture2D> s_texCache = new Dictionary<long, Texture2D>();
        private static readonly Dictionary<long, Sprite> s_spriteCache = new Dictionary<long, Sprite>();

        /// <summary>테두리 없는 라운드 코너 9-slice(흰색 - Image.color 로 틴트). 레거시 HUD 용.</summary>
        public static Sprite RoundSprite
        {
            get
            {
                if (s_round == null) s_round = MakeSprite(BuildTex(Color.white, Color.clear, 0));
                return s_round;
            }
        }

        /// <summary>레거시 다크 IMGUI 패널 텍스처(2단계 개편 전 호환).</summary>
        public static Texture2D ImguiPanelTex
        {
            get
            {
                if (s_imguiPanel == null) s_imguiPanel = BuildTex(new Color(0.07f, 0.09f, 0.15f, 0.92f), Color.clear, 0);
                return s_imguiPanel;
            }
        }

        /// <summary>잉크 테두리가 구워진 라운드 패널 텍스처(IMGUI 용). 색 조합별 캐시.</summary>
        public static Texture2D BorderedTex(Color fill, Color border)
        {
            long key = ColorKey(fill) ^ (ColorKey(border) * 31);
            if (!s_texCache.TryGetValue(key, out var tex) || tex == null)
            {
                tex = BuildTex(fill, border, BORDER);
                s_texCache[key] = tex;
            }
            return tex;
        }

        /// <summary>잉크 테두리가 구워진 라운드 패널 스프라이트(uGUI 용, Image.Type.Sliced). 색 조합별 캐시.</summary>
        public static Sprite BorderedSprite(Color fill, Color border)
        {
            long key = ColorKey(fill) ^ (ColorKey(border) * 31);
            if (!s_spriteCache.TryGetValue(key, out var sp) || sp == null)
            {
                sp = MakeSprite(BorderedTex(fill, border));
                s_spriteCache[key] = sp;
            }
            return sp;
        }

        /// <summary>버튼 3상태(기본/호버/눌림) 텍스처. 호버는 밝게, 눌림은 어둡게.</summary>
        public static void ButtonTex(Color fill, out Texture2D normal, out Texture2D hover, out Texture2D active)
        {
            normal = BorderedTex(fill, Ink);
            hover  = BorderedTex(Brighten(fill, 1.10f), Ink);
            active = BorderedTex(Brighten(fill, 0.85f), Ink);
        }

        private static Color Brighten(Color c, float k) =>
            new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a);

        private static long ColorKey(Color c) =>
            ((long)(c.r * 255) << 24) | ((long)(c.g * 255) << 16) | ((long)(c.b * 255) << 8) | (long)(c.a * 255);

        private static Sprite MakeSprite(Texture2D tex) =>
            Sprite.Create(tex, new Rect(0, 0, TEX, TEX), new Vector2(0.5f, 0.5f), 100f, 0,
                          SpriteMeshType.FullRect, new Vector4(ImguiBorder, ImguiBorder, ImguiBorder, ImguiBorder));

        // 라운드 사각 SDF 기반: 실루엣 밖 투명, 가장자리 borderW 픽셀은 테두리 색, 안쪽은 채움 색.
        // 경계마다 1px 안티에일리어싱.
        private static Texture2D BuildTex(Color fill, Color border, int borderW)
        {
            var tex = new Texture2D(TEX, TEX, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
            };
            for (int y = 0; y < TEX; y++)
                for (int x = 0; x < TEX; x++)
                {
                    // d = 라운드 사각 골격까지의 거리(경계가 d == RADIUS).
                    float cx = Mathf.Clamp(x + 0.5f, RADIUS, TEX - RADIUS);
                    float cy = Mathf.Clamp(y + 0.5f, RADIUS, TEX - RADIUS);
                    float d = new Vector2(x + 0.5f - cx, y + 0.5f - cy).magnitude;

                    float shape = Mathf.Clamp01(RADIUS - d + 0.5f);                    // 전체 실루엣
                    float tBorder = borderW > 0 ? Mathf.Clamp01(d - (RADIUS - borderW) + 0.5f) : 0f; // 1 = 테두리 영역
                    Color c = Color.Lerp(fill, border, tBorder);
                    c.a *= shape;
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            return tex;
        }
    }
}
