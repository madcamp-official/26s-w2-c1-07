using UnityEngine;

namespace RouletteParty.Map
{
    /// <summary>
    /// 왕복 이동 발판(Only Up 스타일 움직이는 길). 위치를 서버 시간의 순수 함수로 계산하므로
    /// 모든 피어가 항상 동일한 상태를 본다(NetworkObject 없이 결정론 동기화, 늦은 합류 자동 일치).
    /// 시작 위상(0~1)은 ClimbMapGenerator 가 시드에서 뽑아 Initialize 로 주입한다(전 피어 동일).
    ///
    /// 알려진 한계: CharacterController 는 움직이는 지면에 실려가지 않는다(발판이 발밑에서
    /// 빠져나감). 운반 로직이 붙기 전까지는 "타이밍 맞춰 건너는 장애물"로만 사용할 것.
    /// </summary>
    [DisallowMultipleComponent]
    public class MovingPlatform : MonoBehaviour
    {
        [Tooltip("왕복 이동 벡터(로컬). 기준 위치 <-> 기준 위치 + 이 값 사이를 오간다.")]
        [SerializeField] private Vector3 _travel = new Vector3(0f, 0f, 3f);
        [Tooltip("왕복 1회 주기(초).")]
        [SerializeField, Min(0.1f)] private float _period = 4f;

        private Vector3 _basePosition;
        private float _phase01;
        private bool _initialized;

        /// <summary>생성기 전용: 시드 기반 시작 위상(0~1). 배치(위치 확정) 후 호출할 것.</summary>
        public void Initialize(float phase01)
        {
            _phase01 = phase01;
            _basePosition = transform.localPosition;
            _initialized = true;
        }

        private void Start()
        {
            // 씬 수동 배치 폴백(생성기 없이 단독 테스트 가능).
            if (!_initialized) Initialize(0f);
        }

        private void Update()
        {
            if (!_initialized) return;
            // 코사인 왕복(양 끝에서 부드럽게 감속). 위상 계산은 double 로 하고 1 로 감아
            // 큰 시간값의 float 정밀도 손실을 피한다.
            double cycle = (RotatingPlatform.NetTime() / _period + _phase01) % 1.0;
            float k = 0.5f * (1f - Mathf.Cos((float)cycle * Mathf.PI * 2f));
            transform.localPosition = _basePosition + _travel * k;
        }
    }
}
