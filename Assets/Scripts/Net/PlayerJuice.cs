using UnityEngine;

namespace RouletteParty.Net
{
    /// <summary>
    /// 캐릭터 "juice"(폴가이즈식 통통함): 점프 시 세로로 늘어나고 착지 시 납작해졌다
    /// 스프링으로 튕겨 복원하는 스쿼시&스트레치 + 착지 먼지 파티클.
    ///
    /// 전 피어 공통 동작(원격 플레이어도 juice 가 보인다):
    ///  - 수직 속도는 transform.y 프레임 차분으로 추정(소유자/원격 공통 신호).
    ///  - 접지는 소유자=CharacterController.isGrounded(정확), 원격=발밑 레이캐스트.
    ///    PlayerAnimDriver 와 같은 방식이라 원격도 착지/도약을 감지한다.
    ///  - 착지/도약을 스프링 임펄스로 넣고, 스프링이 0(중립)으로 튕겨 돌아오며 통통함을 만든다.
    ///
    /// 스케일은 자식 "Model" 트랜스폼에만 적용한다(콜라이더/CC 는 불변, 물리·판정 영향 없음).
    /// Model 피벗이 발(CC 바닥)에 있어 눌림/늘어남이 발을 땅에 붙인 채 일어난다.
    /// Animator 는 Model 하위 본을 구동하므로 Model.localScale 과 독립적으로 합성된다.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerJuice : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("스쿼시&스트레치를 적용할 모델 트랜스폼. 비우면 자식 'Model' 자동 탐색.")]
        [SerializeField] private Transform _model;
        [Tooltip("착지 먼지 파티클 프리팹(발 위치에 1회 재생 후 자동 소멸). 비우면 먼지 생략.")]
        [SerializeField] private GameObject _dustPrefab;

        [Header("스쿼시 스프링")]
        [Tooltip("스프링 강성(클수록 빠르게 복원).")]
        [SerializeField] private float _stiffness = 220f;
        [Tooltip("스프링 감쇠(작을수록 더 통통 튄다).")]
        [SerializeField] private float _damping = 14f;
        [Tooltip("스쿼시 값 -> 스케일 변환 배율(클수록 과장).")]
        [SerializeField] private float _scaleAmount = 0.5f;
        [Tooltip("스쿼시 최대치(과도한 찌그러짐 방지).")]
        [SerializeField] private float _maxSquash = 0.6f;

        [Header("임펄스")]
        [Tooltip("도약 시 세로로 늘어나는 임펄스.")]
        [SerializeField] private float _jumpStretch = 7f;
        [Tooltip("착지 임펄스 = 착지 순간 낙하속도 x 이 값(범위 클램프).")]
        [SerializeField] private float _landPerSpeed = 0.55f;
        [SerializeField] private float _landMin = 2.5f;
        [SerializeField] private float _landMax = 10f;
        [Tooltip("이 낙하속도(m/s) 이상 착지에만 먼지 생성.")]
        [SerializeField] private float _dustMinSpeed = 3f;

        [Header("접지 프로브(원격)")]
        [SerializeField] private float _groundRayLength = 0.4f;
        [SerializeField] private float _maxPlausibleVSpeed = 40f; // 이 이상 = 텔레포트

        static readonly RaycastHit[] _hits = new RaycastHit[6];

        CharacterController _cc;
        PlayerController _pc;
        Vector3 _baseScale;
        float _lastY, _vy, _vyPrev;
        bool _grounded = true, _airborne;
        float _airTime, _landCooldown;
        float _squash, _squashVel;

        void Awake()
        {
            if (_model == null) _model = transform.Find("Model");
            _cc = GetComponent<CharacterController>();
            _pc = GetComponent<PlayerController>();
            if (_model != null) _baseScale = _model.localScale;
            _lastY = transform.position.y;
        }

        void OnEnable() { _lastY = transform.position.y; _squash = _squashVel = 0f; }

        void Update()
        {
            if (_model == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // ---- 수직 속도(프레임 차분, 텔레포트 무시) ----
            _vyPrev = _vy;
            float vy = (transform.position.y - _lastY) / dt;
            _lastY = transform.position.y;
            if (Mathf.Abs(vy) > _maxPlausibleVSpeed) vy = 0f; // 스폰/부활 순간이동
            _vy = vy;

            // ---- 접지(소유자=CC, 원격=레이) ----
            bool owner = _pc != null && _pc.IsSpawned && _pc.IsOwner;
            bool grounded = (owner && _cc != null && _cc.enabled) ? _cc.isGrounded : ProbeGround();

            if (_landCooldown > 0f) _landCooldown -= dt;

            // ---- 전이 감지 ----
            if (!grounded) _airTime += dt;
            if (grounded && !_grounded)
            {
                // 착지: 충분히 공중에 있었고 하강 중이었으면 스쿼시 + 먼지.
                float impact = Mathf.Max(-_vyPrev, -_vy); // 착지 직전 낙하속도(양수)
                if (_airTime > 0.08f && impact > 0.5f && _landCooldown <= 0f)
                {
                    float imp = Mathf.Clamp(impact * _landPerSpeed, _landMin, _landMax);
                    _squashVel += imp;           // + = 납작
                    _landCooldown = 0.12f;
                    if (_dustPrefab != null && impact >= _dustMinSpeed) SpawnDust(impact);
                }
                _airTime = 0f;
            }
            else if (!grounded && _grounded && _vy > 0.5f)
            {
                _squashVel -= _jumpStretch;      // 도약: - = 세로로 늘어남
            }
            _grounded = grounded;

            // ---- 스프링 적분(중립 0 으로 복원, 언더댐프 -> 통통) ----
            _squashVel += (-_stiffness * _squash - _damping * _squashVel) * dt;
            _squash = Mathf.Clamp(_squash + _squashVel * dt, -_maxSquash, _maxSquash);

            // ---- 스케일 적용(부피 보존 근사) ----
            float s = _squash * _scaleAmount;
            _model.localScale = new Vector3(
                _baseScale.x * (1f + s * 0.5f),
                _baseScale.y * (1f - s),
                _baseScale.z * (1f + s * 0.5f));
        }

        void SpawnDust(float impact)
        {
            float footY = _pc != null ? _pc.FootY : transform.position.y - 1f;
            var pos = new Vector3(transform.position.x, footY + 0.02f, transform.position.z);
            var dust = Instantiate(_dustPrefab, pos, Quaternion.identity);
            float scale = Mathf.Clamp(impact / 8f, 0.7f, 1.6f);
            dust.transform.localScale *= scale;
            Destroy(dust, 2f); // 파티클 재생 후 안전 소멸
        }

        bool ProbeGround()
        {
            float footY = _pc != null ? _pc.FootY : transform.position.y - 1f;
            Vector3 origin = new Vector3(transform.position.x, footY + 0.1f, transform.position.z);
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, _hits, _groundRayLength + 0.1f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = _hits[i].collider;
                if (col == null) continue;
                var t = col.transform;
                if (t == transform || t.IsChildOf(transform)) continue;
                return true;
            }
            return false;
        }
    }
}
