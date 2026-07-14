using UnityEngine;

namespace RouletteParty.UI
{
    /// <summary>
    /// 코드 생성 UI 공용 키트: 라운드 코너 스프라이트/텍스처(런타임 1회 생성)와 공용 팔레트.
    /// GameHUD(uGUI)와 LobbyUI(IMGUI)가 같은 톤을 쓰도록 단일 지점으로 관리한다.
    /// </summary>
    public static class UiKit
    {
        // ---- 팔레트 ----
        public static readonly Color PanelBg     = new Color(0.07f, 0.09f, 0.15f, 0.80f); // 다크 네이비 반투명
        public static readonly Color PanelBgSoft = new Color(0.07f, 0.09f, 0.15f, 0.55f); // 옅은 패널(플레이 중 정보)
        public static readonly Color TextMain    = new Color(0.96f, 0.97f, 1f, 1f);
        public static readonly Color TextDim     = new Color(0.72f, 0.78f, 0.88f, 1f);
        public static readonly Color Accent      = new Color(1f, 0.83f, 0.35f, 1f);      // 옐로(타이머/강조)
        public static readonly Color Danger      = new Color(1f, 0.38f, 0.38f, 1f);      // 레드(임박/탈락)

        private const int TEX = 32, RADIUS = 10;

        private static Sprite s_round;
        private static Texture2D s_imguiPanel;

        /// <summary>uGUI 용 라운드 코너 9-slice 스프라이트(흰색 - Image.color 로 틴트, Image.Type.Sliced 로 사용).</summary>
        public static Sprite RoundSprite
        {
            get
            {
                if (s_round == null)
                {
                    var tex = BuildTex(Color.white);
                    s_round = Sprite.Create(tex, new Rect(0, 0, TEX, TEX), new Vector2(0.5f, 0.5f), 100f, 0,
                                            SpriteMeshType.FullRect,
                                            new Vector4(RADIUS + 2, RADIUS + 2, RADIUS + 2, RADIUS + 2));
                }
                return s_round;
            }
        }

        /// <summary>IMGUI(GUIStyle.normal.background)용 패널 텍스처(다크 네이비를 구워 넣음, border 12 로 9-slice).</summary>
        public static Texture2D ImguiPanelTex
        {
            get
            {
                if (s_imguiPanel == null) s_imguiPanel = BuildTex(new Color(0.07f, 0.09f, 0.15f, 0.92f));
                return s_imguiPanel;
            }
        }

        public const int ImguiBorder = 12;

        private static Texture2D BuildTex(Color fill)
        {
            var tex = new Texture2D(TEX, TEX, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
            };
            for (int y = 0; y < TEX; y++)
                for (int x = 0; x < TEX; x++)
                {
                    float a = CornerAlpha(x, y);
                    tex.SetPixel(x, y, new Color(fill.r, fill.g, fill.b, fill.a * a));
                }
            tex.Apply();
            return tex;
        }

        // 모서리 원 바깥은 투명, 경계 1px 안티에일리어싱. 내부(모서리 밖 영역)는 항상 1.
        private static float CornerAlpha(int x, int y)
        {
            float cx = Mathf.Clamp(x + 0.5f, RADIUS, TEX - RADIUS);
            float cy = Mathf.Clamp(y + 0.5f, RADIUS, TEX - RADIUS);
            float d = new Vector2(x + 0.5f - cx, y + 0.5f - cy).magnitude;
            return Mathf.Clamp01(RADIUS - d + 0.5f);
        }
    }
}
