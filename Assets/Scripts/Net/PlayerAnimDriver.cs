namespace RouletteParty.Net
{
using UnityEngine;

/// <summary>
/// 스틱맨 애니메이션 드라이버. Player 프리팹 루트에 부착하고, 자식 모델의 Animator 를 구동한다.
///
/// 모든 피어(소유자/원격)에서 동일하게 동작해야 원격 플레이어도 팔다리가 움직인다:
///  - 속도: 입력이 아니라 "transform 의 프레임 차분"으로 추정한다. 원격 인스턴스는
///    CharacterController 시뮬레이션이 없고 ClientNetworkTransform 보간 위치만 있으므로
///    이것이 소유자/원격 공통으로 성립하는 유일한 신호다. 보간 지터는 지수 스무딩으로 흡수.
///  - 접지: 소유자는 CharacterController.isGrounded(정확), 원격은 발밑 레이캐스트
///    (자기 콜라이더 제외, 레이어 무관 -> 맵 발판·설치 구조물 모두 위에서 성립).
///  - 텔레포트(라운드 배치/부활)는 이동으로 오인하지 않게 한 프레임 큰 변위는 무시한다.
///
/// Animator 파라미터 계약(Assets/Animations/PlayerStickman.controller):
///  Speed(float, m/s) / Grounded(bool). 루트모션은 끈다(이동 권위는 CC/복제 transform).
/// </summary>
[DisallowMultipleComponent]
public class PlayerAnimDriver : MonoBehaviour
{
    [Tooltip("자식 모델의 Animator. 비우면 자식에서 자동 탐색.")]
    [SerializeField] private Animator _animator;
    [Tooltip("속도 스무딩 계수(클수록 반응이 빠름). 네트워크 보간 지터 흡수용.")]
    [SerializeField] private float _speedSmoothing = 10f;
    [Tooltip("원격 접지 판정: 발끝에서 아래로 쏘는 레이 길이.")]
    [SerializeField] private float _groundRayLength = 0.4f;
    [Tooltip("한 프레임 변위가 이 값(m) 이상이면 텔레포트로 간주하고 속도 계산에서 제외.")]
    [SerializeField] private float _teleportThreshold = 3f;

    static readonly int SpeedId    = Animator.StringToHash("Speed");
    static readonly int GroundedId = Animator.StringToHash("Grounded");
    static readonly RaycastHit[] _hits = new RaycastHit[6];

    CharacterController _cc;
    PlayerController _pc;
    Vector3 _lastPos;
    float _speed;
    bool _grounded = true;

    void Awake()
    {
        if (_animator == null) _animator = GetComponentInChildren<Animator>(true);
        if (_animator != null) _animator.applyRootMotion = false; // 이동은 CC/복제가 담당
        _cc = GetComponent<CharacterController>();
        _pc = GetComponent<PlayerController>();
        _lastPos = transform.position;
    }

    void OnEnable() => _lastPos = transform.position;

    void Update()
    {
        if (_animator == null || !_animator.isActiveAndEnabled) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // ---- 수평 속도(프레임 차분) ----
        Vector3 delta = transform.position - _lastPos;
        _lastPos = transform.position;
        float raw;
        if (delta.magnitude >= _teleportThreshold)
            raw = 0f; // 텔레포트/스폰 배치: 이동 아님
        else
        {
            delta.y = 0f;
            raw = delta.magnitude / dt;
        }
        _speed = Mathf.Lerp(_speed, raw, 1f - Mathf.Exp(-_speedSmoothing * dt));

        // ---- 접지 ----
        bool owner = _pc != null && _pc.IsSpawned && _pc.IsOwner;
        if (owner && _cc != null && _cc.enabled)
            _grounded = _cc.isGrounded;
        else
            _grounded = ProbeGround();

        _animator.SetFloat(SpeedId, _speed);
        _animator.SetBool(GroundedId, _grounded);
    }

    // 원격용 접지 프로브: 발끝 살짝 위에서 아래로. 자기 자신(자식 포함) 콜라이더는 제외.
    bool ProbeGround()
    {
        float footY = _pc != null ? _pc.FootY : transform.position.y - 1f;
        Vector3 origin = new Vector3(transform.position.x, footY + 0.1f, transform.position.z);
        int n = Physics.RaycastNonAlloc(origin, Vector3.down, _hits,
            _groundRayLength + 0.1f, ~0, QueryTriggerInteraction.Ignore);
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
