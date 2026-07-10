namespace RouletteParty.Net
{
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem; // 새 Input System (Keyboard.current)

/// <summary>
/// 소유자 권위(ClientNetworkTransform) 3인칭 플레이어.
/// Day2: IsOwner 만 WASD 이동 + 점프 + 코드 카메라(Camera.main) 추적.
/// Day3 추가:
///   (a) 강물 낙사(y &lt; deathY) -> 안전 스폰으로 부활. NetworkTransform.Teleport 로
///       보간 streaking 없이 스냅. CharacterController 는 껐다 켠다.
///   (b) 카메라가 언덕을 뚫지 않게: SphereCast 오클루전(선택) + 지형 높이 클램프.
/// 모든 이동/부활/카메라 처리는 소유자(=권위) 로컬에서만 한다.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    [Header("이동")]
    public float moveSpeed = 6f;
    public float jumpHeight = 1.5f;
    public float gravity = -20f;
    [Tooltip("이동 방향으로 몸을 돌리는 속도(클수록 빠르게 회전).")]
    public float turnSpeed = 12f;

    [Header("부활 / 낙사")]
    [Tooltip("이 y 아래로 떨어지면 사망 처리. TerrainGenerator.DEATH_Y(-3.7) 와 일치시킬 것.")]
    public float deathY = -3.7f;
    [Tooltip("부활 지점(지형 위 안전한 곳). y 는 지형 높이 기준으로 자동 보정된다.")]
    public Vector3 spawnPoint = new Vector3(25f, 3f, 30f);
    [Tooltip("부활 시 지형 표면에서 얼마나 위에서 시작할지(낙하 여유).")]
    public float spawnHeightOffset = 1.5f;

    [Header("카메라")]
    public float camDistance = 6f;
    public float camHeight = 2.4f;          // 피벗(머리) 높이
    public float camPitch = 18f;            // 아래로 내려다보는 각도(deg)
    public float camSphereRadius = 0.3f;
    public float camMinDistance = 0.8f;     // 벽에 붙어도 이보다 가깝겐 안 옴
    public float camCollisionSkin = 0.2f;   // 표면에서 살짝 띄우기
    public float camGroundClearance = 0.5f; // 지형 위 최소 높이
    [Tooltip("선택: 카메라 오클루전용 LayerMask. 'Ground' 레이어만 지정 권장(플레이어 제외). " +
             "비워두면(Nothing) SphereCast 오클루전은 생략하고 지형 높이 클램프만 쓴다.")]
    public LayerMask camCollisionMask;

    // 캐시
    CharacterController _cc;
    NetworkTransform _net;   // ClientNetworkTransform 도 이 타입으로 잡힘
    Camera _cam;
    float _verticalVelocity;

    public override void OnNetworkSpawn()
    {
        _cc  = GetComponent<CharacterController>();
        _net = GetComponent<NetworkTransform>(); // ClientNetworkTransform(서브클래스)

        if (IsOwner)
            _cam = Camera.main; // Camera.main 은 비싸므로 한 번만 캐시
    }

    void Update()
    {
        // 스폰된 소유자(=권위)만 이동 처리. IsSpawned 로 스폰 전 프레임 차단.
        if (!IsSpawned || !IsOwner) return;
        HandleMovement();
        // 낙사 감지/부활/출발선 배치는 MatchManager(호스트 권위)가 담당한다.
        // (MatchManager 가 소유자에게 RPC 로 TeleportTo() 를 호출해 위치를 옮긴다.)
    }

    void LateUpdate()
    {
        if (!IsSpawned || !IsOwner) return;
        UpdateThirdPersonCamera(); // 이동 계산 이후에
    }

    void HandleMovement()
    {
        float h = 0f, v = 0f;
        bool jump = false;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.aKey.isPressed) h -= 1f;
            if (kb.dKey.isPressed) h += 1f;
            if (kb.wKey.isPressed) v += 1f;
            if (kb.sKey.isPressed) v -= 1f;
            jump = kb.spaceKey.wasPressedThisFrame;
        }

        Vector3 inputDir = new Vector3(h, 0f, v);
        if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();

        // 이동 방향으로 몸을 돌린다(카메라는 뒤를 따라옴).
        if (inputDir.sqrMagnitude > 0.0001f)
        {
            Quaternion target = Quaternion.LookRotation(inputDir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, target,
                                                  turnSpeed * Time.deltaTime);
        }

        // 중력 & 점프
        if (_cc.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f; // 지면에 살짝 붙여 isGrounded 안정화
        if (jump && _cc.isGrounded)
            _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        _verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = inputDir * moveSpeed;
        velocity.y = _verticalVelocity;
        _cc.Move(velocity * Time.deltaTime);
    }

    /// <summary>
    /// 소유자 전용 순간이동. MatchManager 가 RPC(SendMessage "TeleportTo")로 호출한다.
    /// CharacterController 를 잠깐 끄고 NetworkTransform.Teleport 로 보간 없이 스냅한다.
    /// </summary>
    public void TeleportTo(Vector3 spawn)
    {
        if (!IsOwner) return;                              // 소유자(권위)만 유효
        if (_cc == null)  _cc  = GetComponent<CharacterController>();
        if (_net == null) _net = GetComponent<NetworkTransform>();

        if (_net != null)
        {
            // CharacterController 는 내부 캐시 위치가 있어, 켜진 채 이동하면 다음 스텝에 원위치로
            // 되돌린다 -> 잠깐 끈다. NetworkTransform 은 끄지 말 것(issue #3183: 한 프레임 튐).
            _cc.enabled = false;
            // Teleport: 원격 클라 보간을 리셋(streaking 방지)하며 authority transform 을 즉시 세팅.
            _net.Teleport(spawn, transform.rotation, transform.localScale);
            _cc.enabled = true;
            Physics.SyncTransforms(); // 재활성 CC 가 새 위치를 인식(isGrounded/다음 Move)
        }
        else
        {
            // NetworkTransform 이 없는 예외적 경우의 안전망.
            _cc.enabled = false;
            transform.position = spawn;
            _cc.enabled = true;
            Physics.SyncTransforms();
        }

        _verticalVelocity = 0f; // 낙하 누적 속도 리셋(부활 직후 재낙사 방지)
    }

    // 지형 위 안전한 y 로 보정한 스폰 좌표.
    Vector3 GetSafeSpawn()
    {
        Vector3 p = spawnPoint;
        float ground = TerrainGenerator.SampleHeight(p.x, p.z);
        p.y = Mathf.Max(p.y, ground + spawnHeightOffset);
        return p;
    }

    void UpdateThirdPersonCamera()
    {
        if (_cam == null)
        {
            _cam = Camera.main;
            if (_cam == null) return;
        }

        Vector3 pivot = transform.position + Vector3.up * camHeight;
        Quaternion rot = Quaternion.Euler(camPitch, transform.eulerAngles.y, 0f);
        Vector3 dir = (rot * Vector3.back).normalized; // 플레이어 뒤쪽(+ 살짝 위)

        // (A) 오클루전: Ground 레이어를 지정했을 때만. 피벗->카메라 방향 sphere-sweep.
        float dist = camDistance;
        if (camCollisionMask.value != 0 &&
            Physics.SphereCast(pivot, camSphereRadius, dir, out RaycastHit hit,
                camDistance, camCollisionMask, QueryTriggerInteraction.Ignore))
        {
            dist = Mathf.Max(camMinDistance, hit.distance - camCollisionSkin);
        }

        Vector3 camPos = pivot + dir * dist;

        // (B) 지형 높이 클램프: 레이어 설정 없이도 동작(SphereCast 가 놓친 계곡·급경사 대비).
        //     지형 메시가 존재하는 범위(X:[-40,40], Z:[-80,80], 중앙 정렬) 안에서만 적용.
        float halfX = TerrainGenerator.SIZE_X / 2f;
        float halfZ = TerrainGenerator.SIZE_Z / 2f;
        if (camPos.x >= -halfX && camPos.x <= halfX &&
            camPos.z >= -halfZ && camPos.z <= halfZ)
        {
            float minY = TerrainGenerator.SampleHeight(camPos.x, camPos.z) + camGroundClearance;
            if (camPos.y < minY) camPos.y = minY;
        }

        _cam.transform.position = camPos;
        _cam.transform.rotation = Quaternion.LookRotation(pivot - camPos, Vector3.up);
    }
}
}
