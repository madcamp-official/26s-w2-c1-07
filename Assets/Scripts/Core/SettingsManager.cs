using UnityEngine;
using UnityEngine.InputSystem; // 새 Input System
using RouletteParty.UI;        // ImguiScale (OnGUI 해상도 스케일링)

namespace RouletteParty.Core
{
    /// <summary>
    /// 게임 설정(마우스 감도·Y 반전·볼륨·창 모드)의 저장/복원/적용 + F1 설정 패널.
    ///
    ///  - PlayerPrefs 로 영속화(로컬 사용자 설정). 값 변경 즉시 적용, 패널 닫을 때 Save.
    ///  - 마스터 볼륨은 AudioListener.volume 에 직접 적용. SFX/BGM 개별 볼륨은
    ///    AudioManager 가 이 매니저의 값을 읽어 적용한다.
    ///  - 감도/Y 반전은 PlayerController 가 매 프레임 이 매니저의 값을 우선 사용한다
    ///    (인스턴스가 없으면 인스펙터 값 폴백 -> 씬 구성과 무관하게 안전).
    ///  - 씬 배치: 전용 GameObject 에 컴포넌트로 추가한다(에디터 Add Component).
    ///    인스턴스가 없어도 참조자(PlayerController/AudioManager)는 기본값으로 폴백해 안전하다.
    ///  - 패널이 열려 있는 동안 PlayerController 는 시점 회전을 멈추고 커서를 풀어준다(IsOpen 참조).
    /// </summary>
    [DisallowMultipleComponent]
    public class SettingsManager : MonoBehaviour
    {
        public static SettingsManager Instance { get; private set; }

        /// <summary>설정 패널 표시 중 여부(입력/커서 처리 분기용, PlayerController 가 참조).</summary>
        public static bool IsOpen => Instance != null && Instance._open;

        // ---- 기본값(PlayerController 인스펙터 기본과 일치) ----
        private const float DEF_SENSITIVITY = 0.12f;
        private const float DEF_MASTER = 1f;
        private const float DEF_SFX = 1f;
        private const float DEF_BGM = 0.7f;

        // ---- PlayerPrefs 키 ----
        private const string KEY_SENS = "opt.sensitivity";
        private const string KEY_INVERT = "opt.invertY";
        private const string KEY_MASTER = "opt.masterVolume";
        private const string KEY_SFX = "opt.sfxVolume";
        private const string KEY_BGM = "opt.bgmVolume";
        private const string KEY_FULLSCREEN = "opt.fullscreen";

        public float MouseSensitivity { get; private set; } = DEF_SENSITIVITY;
        public bool  InvertY          { get; private set; }
        public float MasterVolume     { get; private set; } = DEF_MASTER;
        public float SfxVolume        { get; private set; } = DEF_SFX;
        public float BgmVolume        { get; private set; } = DEF_BGM;
        public bool  Fullscreen       { get; private set; } = true;

        private bool _open;
        private int _tab; // 0 = 설정, 1 = 설명(조작/규칙 도움말)
        private Rect _panelRect;
        private GUIStyle _title, _panel, _label, _head, _small, _tabOn, _tabOff;
        private Vector2 _helpScroll;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Load();
            ApplyAudio();
            // 창 모드는 시작 시 강제하지 않는다(빌드 설정/사용자 조작 존중). 토글 시에만 적용.
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame)
            {
                _open = !_open;
                if (!_open) PlayerPrefs.Save(); // 닫을 때 디스크 반영
            }
        }

        // ============================ 영속화 / 적용 ============================
        private void Load()
        {
            MouseSensitivity = PlayerPrefs.GetFloat(KEY_SENS, DEF_SENSITIVITY);
            InvertY          = PlayerPrefs.GetInt(KEY_INVERT, 0) == 1;
            MasterVolume     = PlayerPrefs.GetFloat(KEY_MASTER, DEF_MASTER);
            SfxVolume        = PlayerPrefs.GetFloat(KEY_SFX, DEF_SFX);
            BgmVolume        = PlayerPrefs.GetFloat(KEY_BGM, DEF_BGM);
            Fullscreen       = PlayerPrefs.GetInt(KEY_FULLSCREEN, Screen.fullScreen ? 1 : 0) == 1;
        }

        private void Store()
        {
            PlayerPrefs.SetFloat(KEY_SENS, MouseSensitivity);
            PlayerPrefs.SetInt(KEY_INVERT, InvertY ? 1 : 0);
            PlayerPrefs.SetFloat(KEY_MASTER, MasterVolume);
            PlayerPrefs.SetFloat(KEY_SFX, SfxVolume);
            PlayerPrefs.SetFloat(KEY_BGM, BgmVolume);
            PlayerPrefs.SetInt(KEY_FULLSCREEN, Fullscreen ? 1 : 0);
        }

        private void ApplyAudio()
        {
            AudioListener.volume = Mathf.Clamp01(MasterVolume);
        }

        private void ApplyWindowMode()
        {
            Screen.fullScreenMode = Fullscreen
                ? FullScreenMode.FullScreenWindow
                : FullScreenMode.Windowed;
        }

        private void ResetToDefaults()
        {
            MouseSensitivity = DEF_SENSITIVITY;
            InvertY = false;
            MasterVolume = DEF_MASTER;
            SfxVolume = DEF_SFX;
            BgmVolume = DEF_BGM;
            ApplyAudio();
            Store();
        }

        // ============================ 설정/설명 패널 (F1, 탭 구조) ============================
        private void EnsureStyles()
        {
            if (_title != null) return;
            _title = new GUIStyle(GUI.skin.label)
            { fontStyle = FontStyle.Bold, fontSize = 30, alignment = TextAnchor.MiddleCenter };
            _title.normal.textColor = UiKit.Ink;

            _panel = new GUIStyle(GUI.skin.box)
            {
                border = new RectOffset(UiKit.ImguiBorder, UiKit.ImguiBorder, UiKit.ImguiBorder, UiKit.ImguiBorder),
                padding = new RectOffset(26, 26, 18, 18),
            };
            _panel.normal.background = UiKit.BorderedTex(UiKit.Cream, UiKit.Ink);

            _label = new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true };
            _label.normal.textColor = UiKit.Ink;
            _head = new GUIStyle(GUI.skin.label) { fontSize = 23, fontStyle = FontStyle.Bold };
            _head.normal.textColor = UiKit.Ink;
            _small = new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true, wordWrap = true };
            _small.normal.textColor = UiKit.InkSoft;

            _tabOn = MakeTab(UiKit.Teal);
            _tabOff = MakeTab(UiKit.Grey);
        }

        private static GUIStyle MakeTab(Color fill)
        {
            var st = new GUIStyle(GUI.skin.button)
            {
                fontSize = 22, fontStyle = FontStyle.Bold,
                border = new RectOffset(UiKit.ImguiBorder, UiKit.ImguiBorder, UiKit.ImguiBorder, UiKit.ImguiBorder),
            };
            Texture2D n, h, a;
            UiKit.ButtonTex(fill, out n, out h, out a);
            st.normal.background = n; st.hover.background = h; st.active.background = a; st.focused.background = n;
            st.normal.textColor = Color.white; st.hover.textColor = Color.white;
            st.active.textColor = Color.white; st.focused.textColor = Color.white;
            return st;
        }

        private void OnGUI()
        {
            if (!_open) return;
            EnsureStyles();

            ImguiScale.Apply(); // 이하 좌표는 1080p 기준 가상 픽셀
            if (UiKit.Font != null) GUI.skin.font = UiKit.Font; // 번들 폰트
            const float W = 560f, H = 620f;
            _panelRect = new Rect((ImguiScale.VirtualWidth - W) * 0.5f, (ImguiScale.VirtualHeight - H) * 0.5f, W, H);

            GUILayout.BeginArea(_panelRect, _panel);
            GUILayout.Label(_tab == 0 ? "설정" : "설명", _title);
            GUILayout.Space(6);

            // ---- 탭 ----
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("설정", _tab == 0 ? _tabOn : _tabOff, GUILayout.Height(44))) _tab = 0;
            GUILayout.Space(8);
            if (GUILayout.Button("설명", _tab == 1 ? _tabOn : _tabOff, GUILayout.Height(44))) _tab = 1;
            GUILayout.EndHorizontal();
            GUILayout.Space(12);

            if (_tab == 0) DrawSettingsTab();
            else DrawHelpTab();

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (_tab == 0 && GUILayout.Button("기본값 복원", _tabOff, GUILayout.Height(44))) ResetToDefaults();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("닫기 (F1)", _tabOff, GUILayout.Height(44), GUILayout.Width(150)))
            {
                _open = false;
                Store();
                PlayerPrefs.Save();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawSettingsTab()
        {
            MouseSensitivity = Slider("마우스 감도", MouseSensitivity, 0.02f, 0.5f, "0.00");

            bool invert = GUILayout.Toggle(InvertY, " 마우스 Y축 반전", _label);
            if (invert != InvertY) { InvertY = invert; Store(); }

            GUILayout.Space(8);

            MasterVolume = Slider("전체 볼륨", MasterVolume, 0f, 1f, "P0");
            ApplyAudio();

            SfxVolume = Slider("효과음 볼륨", SfxVolume, 0f, 1f, "P0");
            BgmVolume = Slider("배경음 볼륨", BgmVolume, 0f, 1f, "P0");

            GUILayout.Space(8);

            bool fs = GUILayout.Toggle(Fullscreen, " 전체 화면(테두리 없는 창)", _label);
            if (fs != Fullscreen)
            {
                Fullscreen = fs;
                ApplyWindowMode();
                Store();
            }
        }

        // 조작/규칙 도움말. 구조물 설치의 상세 안내는 PREP 화면에서 전부 여기로 옮겨 왔다
        // (PREP 은 카트라이더식 슬롯 바만 표시 - PrepClientUI).
        private void DrawHelpTab()
        {
            _helpScroll = GUILayout.BeginScrollView(_helpScroll);

            GUILayout.Label("기본 조작", _head);
            GUILayout.Label("<b>WASD</b> 이동 · 마우스 시선 · <b>Space</b> 점프(누른 시간만큼 높이 조절) · <b>Esc</b> 커서 잠금 해제", _small);
            GUILayout.Space(10);

            GUILayout.Label("준비 페이즈 - 구조물 설치", _head);
            GUILayout.Label(
                "자유 비행: <b>WASD</b> 수평 이동, <b>Space</b> 상승 / <b>Shift</b> 하강\n" +
                "<b>좌클릭</b> 설치 (프리뷰 초록 = 설치 가능, 빨강 = 불가)\n" +
                "<b>[Alt]</b> 다음 구조물로 전환 · <b>[1]</b> 보이는 것 / <b>[2]</b> 투명 선택\n" +
                "<b>[R]</b> Y축 · <b>[T]</b> X축 · <b>[G]</b> Z축 90도 회전\n" +
                "<b>[Q]</b> 설치 모드 전환: 표면(조준점) / 공중(휠로 거리 조절)\n" +
                "설치된 구조물을 조준하면 그 위에 쌓입니다. 설치물은 3라운드 내내 누적됩니다.", _small);
            GUILayout.Space(10);

            GUILayout.Label("투명 구조물(함정)", _head);
            GUILayout.Label(
                "상대에게 보이지 않고, 부딪히면 잠깐 모습이 드러납니다.\n" +
                "상대가 내 함정에 걸려 떨어지면 설치자가 점수를 얻습니다(셀프 제외).", _small);
            GUILayout.Space(10);

            GUILayout.Label("점수", _head);
            GUILayout.Label(
                "진행도(최고 높이 + 종료 높이) · 정상 도달 시간 · 청크 선착순 보너스\n" +
                "순위 점수(참가자 수 비례) · 안정성 보너스(무탈락) · 반복 탈락 감점\n" +
                "라운드 종료 시점의 위치가 중요합니다. 마지막까지 버티세요!", _small);

            GUILayout.EndScrollView();
        }

        // 라벨 + 슬라이더 + 현재 값 한 줄. 값이 바뀌면 즉시 Store(디스크 Save 는 닫을 때).
        private float Slider(string label, float value, float min, float max, string fmt)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _label, GUILayout.Width(150));
            GUILayout.BeginVertical();
            GUILayout.Space(12); // 슬라이더를 라벨 세로 중앙에 맞춤
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
            GUILayout.EndVertical();
            GUILayout.Label(fmt == "P0" ? v.ToString("P0") : v.ToString(fmt), _label, GUILayout.Width(60));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(v, value)) Store();
            return v;
        }
    }
}
