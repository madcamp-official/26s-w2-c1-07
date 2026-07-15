using UnityEngine;
using RouletteParty.Core;  // SettingsManager (볼륨)
using RouletteParty.Match; // MatchManager (페이즈 폴링)

namespace RouletteParty.Audio
{
    /// <summary>게임 전역 효과음 식별자. AudioManager 의 클립 슬롯과 1:1.</summary>
    public enum Sfx : byte
    {
        Jump,        // 점프
        Land,        // 착지(작은 낙하 이상)
        Place,       // 구조물 설치 확정(스폰)
        Reveal,      // 투명 구조물 발동(전원 공개 순간)
        Death,       // 탈락
        PlayStart,   // 라운드(PLAY) 시작
        RoundEnd,    // 라운드 종료(HIGHLIGHT 진입)
        MatchResult, // 최종 결과(RESULT 진입)
        UIClick,     // UI 버튼 클릭(로비/설정 등)
    }

    /// <summary>
    /// 전역 사운드 매니저. 씬에 1개 배치(클립 슬롯 인스펙터 할당).
    ///
    ///  - SFX: AudioManager.Play(Sfx.X) 정적 호출 한 줄로 어디서든 재생.
    ///    인스턴스/클립이 없으면 조용히 무시 -> 클립을 아직 안 꽂아도 게임이 깨지지 않는다.
    ///  - 페이즈 스팅어/BGM: MatchManager 페이즈를 폴링해 스스로 처리(게임 코드에 훅 불필요).
    ///    BGM 은 PLAY 중 bgmPlay, 그 외 페이즈는 bgmLobby 를 페이드 전환.
    ///  - 볼륨: 마스터는 SettingsManager 가 AudioListener.volume 으로 처리.
    ///    SFX/BGM 개별 볼륨은 이 매니저가 SettingsManager 값을 읽어 적용한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("SFX 클립 (비워두면 해당 사운드는 무음)")]
        [SerializeField] private AudioClip _jump;
        [SerializeField] private AudioClip _land;
        [Tooltip("구조물 설치음 후보. 2개 이상이면 설치할 때마다 무작위로 하나를 재생(반복 기계감 방지).")]
        [SerializeField] private AudioClip[] _placeVariants;
        [SerializeField] private AudioClip _reveal;
        [SerializeField] private AudioClip _death;
        [SerializeField] private AudioClip _playStart;
        [SerializeField] private AudioClip _roundEnd;
        [SerializeField] private AudioClip _matchResult;
        [SerializeField] private AudioClip _uiClick;

        [Header("BGM (비워두면 무음)")]
        [Tooltip("PLAY 외 페이즈(로비/준비/하이라이트/결과) 배경음. 루프 재생.")]
        [SerializeField] private AudioClip _bgmLobby;
        [Tooltip("PLAY(등반) 배경음. 루프 재생.")]
        [SerializeField] private AudioClip _bgmPlay;
        [Tooltip("BGM 전환 페이드 속도(초당 볼륨 변화량).")]
        [SerializeField] private float _bgmFadeSpeed = 1.5f;

        private AudioSource _sfxSource; // 2D one-shot 전용
        private AudioSource _bgmSource; // 루프 전용
        private AudioClip _bgmTarget;   // 페이드 목표 클립
        private MatchPhase _lastPhase = (MatchPhase)byte.MaxValue; // 스팅어 중복 방지

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;
            _sfxSource.spatialBlend = 0f; // 2D

            _bgmSource = gameObject.AddComponent<AudioSource>();
            _bgmSource.playOnAwake = false;
            _bgmSource.spatialBlend = 0f;
            _bgmSource.loop = true;
            _bgmSource.volume = 0f;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>효과음 재생. 어디서든 정적 호출(인스턴스/클립 없으면 무시).</summary>
        public static void Play(Sfx id)
        {
            var am = Instance;
            if (am == null) return;
            AudioClip clip = am.ClipFor(id);
            if (clip == null) return;
            // 자주 나는 소리(점프·설치)는 매번 살짝 다른 피치로 재생해 기계적 반복을 줄인다.
            bool vary = id == Sfx.Jump || id == Sfx.Place;
            am._sfxSource.pitch = vary ? UnityEngine.Random.Range(0.95f, 1.06f) : 1f;
            am._sfxSource.PlayOneShot(clip, SfxVolume());
        }

        private AudioClip ClipFor(Sfx id)
        {
            switch (id)
            {
                case Sfx.Jump:        return _jump;
                case Sfx.Land:        return _land;
                case Sfx.Place:       return PickRandom(_placeVariants);
                case Sfx.Reveal:      return _reveal;
                case Sfx.Death:       return _death;
                case Sfx.PlayStart:   return _playStart;
                case Sfx.RoundEnd:    return _roundEnd;
                case Sfx.MatchResult: return _matchResult;
                case Sfx.UIClick:     return _uiClick;
                default:              return null;
            }
        }

        /// <summary>후보 중 하나를 무작위 선택(비었으면 null = 무음). 같은 소리의 반복 기계감을 줄인다.</summary>
        private static AudioClip PickRandom(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0) return null;
            if (clips.Length == 1) return clips[0];
            return clips[UnityEngine.Random.Range(0, clips.Length)];
        }

        private static float SfxVolume() =>
            SettingsManager.Instance != null ? SettingsManager.Instance.SfxVolume : 1f;

        private static float BgmVolume() =>
            SettingsManager.Instance != null ? SettingsManager.Instance.BgmVolume : 0.7f;

        // ============================ 페이즈 스팅어 + BGM (자체 폴링) ============================
        private void Update()
        {
            var mm = MatchManager.Instance;
            MatchPhase phase = (mm != null && mm.IsSpawned) ? mm.CurrentPhase : MatchPhase.Lobby;

            if (phase != _lastPhase)
            {
                // 초기 진입(첫 프레임)에는 스팅어를 울리지 않는다.
                bool initial = _lastPhase == (MatchPhase)byte.MaxValue;
                _lastPhase = phase;
                if (!initial)
                {
                    switch (phase)
                    {
                        case MatchPhase.Play:      Play(Sfx.PlayStart);   break;
                        case MatchPhase.Highlight: Play(Sfx.RoundEnd);    break;
                        case MatchPhase.Result:    Play(Sfx.MatchResult); break;
                    }
                }
            }

            UpdateBgm(phase);
        }

        private void UpdateBgm(MatchPhase phase)
        {
            AudioClip want = phase == MatchPhase.Play ? _bgmPlay : _bgmLobby;

            if (_bgmTarget != want)
            {
                _bgmTarget = want;
            }

            float maxVol = BgmVolume();

            // 목표 클립과 다르면 페이드아웃 -> 교체, 같으면 목표 볼륨까지 페이드인.
            if (_bgmSource.clip != _bgmTarget)
            {
                _bgmSource.volume = Mathf.MoveTowards(_bgmSource.volume, 0f, _bgmFadeSpeed * Time.deltaTime);
                if (_bgmSource.volume <= 0.001f)
                {
                    _bgmSource.clip = _bgmTarget;
                    if (_bgmTarget != null) _bgmSource.Play();
                    else _bgmSource.Stop();
                }
            }
            else if (_bgmSource.clip != null)
            {
                _bgmSource.volume = Mathf.MoveTowards(_bgmSource.volume, maxVol, _bgmFadeSpeed * Time.deltaTime);
            }
        }
    }
}
