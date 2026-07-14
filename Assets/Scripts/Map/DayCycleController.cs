using UnityEngine;
using RouletteParty.Match; // MatchManager (라운드 폴링)

namespace RouletteParty.Map
{
    /// <summary>
    /// 라운드 진행에 따라 하늘/햇빛을 하루의 흐름처럼 바꾸는 연출 컨트롤러(게임 씬 전용).
    ///  - 라운드 1 = 정오(중천), 라운드 2 = 오후(해 지기 4시간 전), 라운드 3 = 골든아워(해 지기 1시간 전).
    ///  - 스카이 머티리얼은 원본 에셋을 복제한 런타임 인스턴스에 쓴다(플레이가 에셋을 오염시키지 않게).
    ///  - 라운드가 바뀌면 transitionSeconds 동안 하늘 3색/태양/라이트/포그/앰비언트를 부드럽게 보간.
    ///    태양 원반 위치는 SunSync 가 라이트 회전에서 자동 동기화한다(여기선 라이트만 돌리면 됨).
    ///  - MatchManager 폴링(라운드 정수 비교)이라 늦게 합류한 클라도 현재 라운드 하늘로 수렴한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class DayCycleController : MonoBehaviour
    {
        /// <summary>하루의 한 시점(라운드별 프리셋). 전부 인스펙터에서 조절 가능.</summary>
        [System.Serializable]
        public class SkyMoment
        {
            public string name = "정오";
            public Color topColor;
            public Color midColor;
            public Color bottomColor;
            public Color sunColor;
            [Range(0f, 2f)] public float sunGlowStrength = 0.5f;
            public Color lightColor;
            public float lightIntensity = 1.1f;
            [Tooltip("해의 고도(도). 90 = 머리 위, 0 = 지평선.")]
            public float sunPitch = 60f;
            [Tooltip("해의 방위(도). 90 = -X 하늘(타이틀/등반 기준 뒤쪽).")]
            public float sunYaw = 90f;
            public Color fogColor;
            [Range(0f, 2f)] public float ambientIntensity = 1.15f;
        }

        [Tooltip("스카이 머티리얼 원본(M_Sky). 런타임 인스턴스로 복제해 사용한다.")]
        [SerializeField] private Material _skySource;
        [Tooltip("씬 디렉셔널 라이트(해). SunSync 가 함께 붙어 있어야 태양 원반이 따라온다.")]
        [SerializeField] private Light _sun;
        [Tooltip("라운드 전환 시 하늘 보간 시간(초).")]
        [SerializeField] private float _transitionSeconds = 4f;

        [Tooltip("라운드별 하늘 프리셋(인덱스 0 = 라운드 1). 라운드가 배열보다 크면 마지막 프리셋 유지.")]
        [SerializeField] private SkyMoment[] _rounds =
        {
            new SkyMoment // R1: 정오 - 맑고 밝은 파란 하늘, 머리 위의 해
            {
                name = "정오",
                topColor = new Color(0.30f, 0.58f, 0.95f),
                midColor = new Color(0.72f, 0.88f, 0.97f),
                bottomColor = new Color(0.90f, 0.88f, 0.80f),
                sunColor = new Color(1f, 0.98f, 0.90f),
                sunGlowStrength = 0.35f,
                lightColor = new Color(1f, 0.96f, 0.88f),
                lightIntensity = 1.1f,
                sunPitch = 65f, sunYaw = 90f,
                fogColor = new Color(0.82f, 0.90f, 0.98f),
                ambientIntensity = 1.15f,
            },
            new SkyMoment // R2: 오후(해 지기 4시간 전) - 살짝 웜, 기울기 시작한 해
            {
                name = "오후",
                topColor = new Color(0.34f, 0.55f, 0.88f),
                midColor = new Color(0.90f, 0.87f, 0.72f),
                bottomColor = new Color(0.90f, 0.78f, 0.62f),
                sunColor = new Color(1f, 0.94f, 0.75f),
                sunGlowStrength = 0.45f,
                lightColor = new Color(1f, 0.90f, 0.74f),
                lightIntensity = 1.12f,
                sunPitch = 38f, sunYaw = 90f,
                fogColor = new Color(0.92f, 0.88f, 0.74f),
                ambientIntensity = 1.18f,
            },
            new SkyMoment // R3: 골든아워(해 지기 1시간 전) - 노을 세트
            {
                name = "골든아워",
                topColor = new Color(0.34f, 0.47f, 0.76f),
                midColor = new Color(0.99f, 0.80f, 0.58f),
                bottomColor = new Color(0.86f, 0.62f, 0.52f),
                sunColor = new Color(1f, 0.90f, 0.65f),
                sunGlowStrength = 0.6f,
                lightColor = new Color(1f, 0.79f, 0.58f),
                lightIntensity = 1.15f,
                sunPitch = 16f, sunYaw = 90f,
                fogColor = new Color(0.97f, 0.80f, 0.63f),
                ambientIntensity = 1.25f,
            },
        };

        private static readonly int TopId  = Shader.PropertyToID("_TopColor");
        private static readonly int MidId  = Shader.PropertyToID("_MidColor");
        private static readonly int BotId  = Shader.PropertyToID("_BottomColor");
        private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        private static readonly int SunGlowId  = Shader.PropertyToID("_SunGlowStrength");

        private Material _sky;      // 런타임 인스턴스
        private int _appliedIndex = -1;
        private float _t = 1f;      // 보간 진행(1 = 완료)
        private SkyMoment _from;    // 전환 시작 시점 스냅샷
        private Quaternion _fromRot, _toRot;

        private void Awake()
        {
            if (_skySource == null) _skySource = RenderSettings.skybox;
            if (_skySource == null || _sun == null)
            {
                Debug.LogWarning("[DayCycle] 스카이 머티리얼/라이트 미배선 - 비활성");
                enabled = false;
                return;
            }
            _sky = new Material(_skySource);
            RenderSettings.skybox = _sky;
            ApplyIndex(0, true); // 시작은 라운드 1(정오)
        }

        private void OnDestroy()
        {
            if (_sky != null) Destroy(_sky);
        }

        private void Update()
        {
            var mm = MatchManager.Instance;
            int round = (mm != null && mm.IsSpawned) ? Mathf.Max(1, mm.Round) : 1;
            int idx = Mathf.Clamp(round - 1, 0, _rounds.Length - 1);
            if (idx != _appliedIndex) ApplyIndex(idx, false);

            if (_t < 1f)
            {
                _t = Mathf.MoveTowards(_t, 1f, Time.deltaTime / Mathf.Max(0.01f, _transitionSeconds));
                Blend(_from, _rounds[_appliedIndex], Mathf.SmoothStep(0f, 1f, _t));
            }
        }

        private void ApplyIndex(int idx, bool instant)
        {
            _from = CaptureCurrent();
            _fromRot = _sun.transform.rotation;
            _toRot = Quaternion.Euler(_rounds[idx].sunPitch, _rounds[idx].sunYaw, 0f);
            _appliedIndex = idx;
            _t = instant ? 1f : 0f;
            if (instant) Blend(_from, _rounds[idx], 1f);
        }

        // 현재 실제 값 스냅샷(전환 시작점). 어떤 상태에서 전환돼도 튐 없이 이어진다.
        private SkyMoment CaptureCurrent()
        {
            return new SkyMoment
            {
                topColor = _sky.GetColor(TopId),
                midColor = _sky.GetColor(MidId),
                bottomColor = _sky.GetColor(BotId),
                sunColor = _sky.GetColor(SunColorId),
                sunGlowStrength = _sky.GetFloat(SunGlowId),
                lightColor = _sun.color,
                lightIntensity = _sun.intensity,
                fogColor = RenderSettings.fogColor,
                ambientIntensity = RenderSettings.ambientIntensity,
            };
        }

        private void Blend(SkyMoment a, SkyMoment b, float t)
        {
            _sky.SetColor(TopId, Color.Lerp(a.topColor, b.topColor, t));
            _sky.SetColor(MidId, Color.Lerp(a.midColor, b.midColor, t));
            _sky.SetColor(BotId, Color.Lerp(a.bottomColor, b.bottomColor, t));
            _sky.SetColor(SunColorId, Color.Lerp(a.sunColor, b.sunColor, t));
            _sky.SetFloat(SunGlowId, Mathf.Lerp(a.sunGlowStrength, b.sunGlowStrength, t));
            _sun.color = Color.Lerp(a.lightColor, b.lightColor, t);
            _sun.intensity = Mathf.Lerp(a.lightIntensity, b.lightIntensity, t);
            _sun.transform.rotation = Quaternion.Slerp(_fromRot, _toRot, t); // SunSync 가 하늘에 반영
            RenderSettings.fogColor = Color.Lerp(a.fogColor, b.fogColor, t);
            RenderSettings.ambientIntensity = Mathf.Lerp(a.ambientIntensity, b.ambientIntensity, t);
        }
    }
}
