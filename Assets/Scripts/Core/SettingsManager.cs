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
        private Rect _panelRect;
        private GUIStyle _title;

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

        // ============================ 설정 패널 (F1) ============================
        private void OnGUI()
        {
            if (!_open) return;
            if (_title == null)
                _title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 16 };

            ImguiScale.Apply(); // 이하 좌표는 1080p 기준 가상 픽셀
            const float W = 360f, H = 330f;
            _panelRect = new Rect((ImguiScale.VirtualWidth - W) * 0.5f, (ImguiScale.VirtualHeight - H) * 0.5f, W, H);

            GUILayout.BeginArea(_panelRect, GUI.skin.window);
            GUILayout.Label("설정  (F1 닫기)", _title);
            GUILayout.Space(8);

            MouseSensitivity = Slider("마우스 감도", MouseSensitivity, 0.02f, 0.5f, "0.00");

            bool invert = GUILayout.Toggle(InvertY, " 마우스 Y축 반전");
            if (invert != InvertY) { InvertY = invert; Store(); }

            GUILayout.Space(8);

            MasterVolume = Slider("전체 볼륨", MasterVolume, 0f, 1f, "P0");
            ApplyAudio();

            SfxVolume = Slider("효과음 볼륨", SfxVolume, 0f, 1f, "P0");
            BgmVolume = Slider("배경음 볼륨", BgmVolume, 0f, 1f, "P0");

            GUILayout.Space(8);

            bool fs = GUILayout.Toggle(Fullscreen, " 전체 화면(테두리 없는 창)");
            if (fs != Fullscreen)
            {
                Fullscreen = fs;
                ApplyWindowMode();
                Store();
            }

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("기본값 복원")) ResetToDefaults();
            if (GUILayout.Button("닫기"))
            {
                _open = false;
                Store();
                PlayerPrefs.Save();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // 라벨 + 슬라이더 + 현재 값 한 줄. 값이 바뀌면 즉시 Store(디스크 Save 는 닫을 때).
        private float Slider(string label, float value, float min, float max, string fmt)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(110));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
            GUILayout.Label(fmt == "P0" ? v.ToString("P0") : v.ToString(fmt), GUILayout.Width(46));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(v, value)) Store();
            return v;
        }
    }
}
