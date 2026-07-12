using Unity.Netcode;
using UnityEngine;

namespace RouletteParty.Map
{
    /// <summary>
    /// 자체 회전 발판(Only Up 스타일). 각도를 서버 시간의 순수 함수로 계산하므로 모든 피어가
    /// 항상 동일한 상태를 본다(NetworkObject 없이 결정론 동기화 — 맵 생성 규약과 동일,
    /// 늦은 합류도 자동 일치). 시작 위상은 ClimbMapGenerator 가 시드에서 뽑아 Initialize 로
    /// 주입한다(전 피어 동일).
    ///
    /// 알려진 한계: CharacterController 는 움직이는 지면에 실려가지 않는다. 회전 발판은
    /// "가만히 서 있기 어려운 발판"이라는 게임성으로 사용한다(운반 로직은 후속 작업).
    /// </summary>
    [DisallowMultipleComponent]
    public class RotatingPlatform : MonoBehaviour
    {
        [Tooltip("초당 회전 각도(도). 음수 = 반대 방향.")]
        [SerializeField] private float _degreesPerSecond = 40f;
        [Tooltip("회전 축(로컬).")]
        [SerializeField] private Vector3 _axis = Vector3.up;

        private Quaternion _baseRotation;
        private float _phaseDegrees;
        private bool _initialized;

        /// <summary>생성기 전용: 시드 기반 시작 위상(도). 배치(위치/회전 확정) 후 호출할 것.</summary>
        public void Initialize(float phaseDegrees)
        {
            _phaseDegrees = phaseDegrees;
            _baseRotation = transform.localRotation;
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
            // 큰 시간값의 float 정밀도 손실을 피하려고 각도 합산을 double 로 하고 360 으로 감는다.
            double angle = (_phaseDegrees + _degreesPerSecond * NetTime()) % 360.0;
            transform.localRotation = _baseRotation * Quaternion.AngleAxis((float)angle, _axis.normalized);
        }

        /// <summary>동기화 기준 시간: 접속 중엔 서버 클럭, 아니면 로컬(오프라인 테스트).</summary>
        internal static double NetTime()
        {
            var nm = NetworkManager.Singleton;
            return (nm != null && nm.IsListening) ? nm.ServerTime.Time : Time.timeAsDouble;
        }
    }
}
