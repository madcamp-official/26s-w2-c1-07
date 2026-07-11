namespace RouletteParty.Net
{
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem; // 새 Input System (Keyboard.current / Mouse.current)

/// <summary>
/// 소유자 권위(ClientNetworkTransform) 3인칭 플레이어. 카메라·입력·조준은 전부 소유자 로컬.
///
/// 이중 카메라 모드:
///   (A) 조준모드(기본, 모든 페이즈): 배그식 3인칭 마우스룩. 마우스로 카메라(=조준)를 궤도 회전,
///       화면 중앙 조준점, 이동은 카메라 기준 스트레이프, 몸은 카메라 yaw 를 향함, 커서 잠금.
///   (B) 후방추적(보존): 기존 동작. 카메라가 이동방향을 뒤에서 추적, 몸은 이동방향으로 회전.
///
/// 활성 판정: useAimView(기본 true) 면 모든 페이즈에서 (A). false 로 두면 (B) 후방추적으로 전환.
/// 준비(PREP) 조작은 잠긴 커서에 맞춰 숫자키+조준점 방식(PrepClientUI)이다.
/// 상세는 docs/조준_시점_명세서.md 참고.
///
/// 이동/부활/낙사 판정은 여전히 소유자(권위) 로컬에서만. 낙사→부활 배치는 MatchManager 가 RPC 로 호출.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    [Header("이동")]
    public float moveSpeed = 6f;
    public float jumpHeight = 1.5f;
    public float gravity = -20f;
    [Tooltip("(후방추적 전용) 이동 방향으로 몸을 돌리는 속도.")]
    public float turnSpeed = 12f;

    [Header("부활 / 낙사")]
    [Tooltip("이 y 아래로 떨어지면 사망 처리. TerrainGenerator.DEATH_Y(-3.7) 와 일치시킬 것.")]
    public float deathY = -3.7f;
    [Tooltip("부활 지점(지형 위 안전한 곳). y 는 지형 높이 기준으로 자동 보정된다.")]
    public Vector3 spawnPoint = new Vector3(25f, 3f, 30f);
    [Tooltip("부활 시 지형 표면에서 얼마나 위에서 시작할지(낙하 여유).")]
    public float spawnHeightOffset = 1.5f;

    [Header("카메라(공통)")]
    public float camHeight = 2.4f;          // 피벗(머리) 높이
    public float camSphereRadius = 0.3f;
    public float camMinDistance = 0.8f;     // 벽에 붙어도 이보다 가깝겐 안 옴
    public float camCollisionSkin = 0.2f;   // 표면에서 살짝 띄우기
    public float camGroundClearance = 0.5f; // 지형 위 최소 높이
    [Tooltip("선택: 카메라 오클루전용 LayerMask. 'Ground' 레이어만 지정 권장(플레이어 제외). " +
             "비우면 SphereCast 오클루전은 생략하고 지형 높이 클램프만 쓴다.")]
    public LayerMask camCollisionMask;

    [Header("후방추적 카메라(비조준: 준비/로비/결과 등)")]
    public float camDistance = 6f;
    public float camPitch = 18f;            // 아래로 내려다보는 각도(deg)

    [Header("마우스룩 조준")]
    [Tooltip("켜면 모든 페이즈에서 마우스룩 조준 시점. 끄면 옛 후방추적 시점(보존용).")]
    public bool useAimView = true;
    [Tooltip("마우스 감도(픽셀 delta 에 곱함).")]
    public float mouseSensitivity = 0.12f;
    public bool invertY = false;
    public float aimMinPitch = -35f;        // 위로 볼 수 있는 한계(음수=위)
    public float aimMaxPitch = 70f;         // 아래로 볼 수 있는 한계
    public float aimDistance = 4.5f;        // 조준 카메라 거리
    public float aimShoulder = 0.7f;        // 오른쪽 어깨 오프셋(캐릭터를 화면 중앙에서 비켜 배치)
    public float aimHeight = 1.6f;          // 조준 피벗 높이(머리 근처)
    [Tooltip("첫 조준 진입(스폰) 시 코스(-Z, 골 방향)를 바라보도록 초기 yaw 를 180 으로 맞춘다.")]
    public bool faceCourseOnAimStart = true;
    [Tooltip("조준 레이(발사 확장용) 최대 거리.")]
    public float aimRayDistance = 500f;

    // ---- 발사 확장 훅(소유자 로컬에서 매 프레임 갱신, 이번 범위에선 소비처 없음) ----
    public bool IsAiming { get; private set; }
    public Ray AimRay { get; private set; }
    public Vector3 AimPoint { get; private set; }

    // 캐시
    CharacterController _cc;
    NetworkTransform _net;   // ClientNetworkTransform 도 이 타입으로 잡힘
    Camera _cam;
    float _verticalVelocity;

    // 마우스룩 상태
    float _yaw;
    float _pitch;
    bool _wasAiming;
    bool _cursorFreeOverride;    // Esc 로 수동 커서 해제

    // 조준 레이 self-hit 방지용 버퍼(정렬 보장 안 되므로 최소거리 직접 선택)
    static readonly RaycastHit[] _rayBuf = new RaycastHit[8];

    public override void OnNetworkSpawn()
    {
        _cc  = GetComponent<CharacterController>();
        _net = GetComponent<NetworkTransform>(); // ClientNetworkTransform(서브클래스)

        if (IsOwner)
        {
            _cam   = Camera.main; // Camera.main 은 비싸므로 한 번만 캐시
            _yaw   = transform.eulerAngles.y;
            _pitch = camPitch;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner) SetCursor(false); // 커서 상태 복구(잠금 해제)
    }

    // 모든 페이즈에서 조준 시점을 쓴다. 비조준(후방추적)은 보존만 하고 useAimView 로 전환 가능.
    bool AimActive() => useAimView;

    void Update()
    {
        // 스폰된 소유자(=권위)만 처리. IsSpawned 로 스폰 전 프레임 차단.
        if (!IsSpawned || !IsOwner) return;

        bool aim = AimActive();
        HandleCursor(aim);

        if (aim)
        {
            if (!_wasAiming) EnterAim(); // 후방추적 -> 조준 전환 시 초기화
            HandleMouseLook();
            HandleMovementAim();
        }
        else
        {
            HandleMovementFollow(); // 기존 동작
        }

        _wasAiming = aim;
        IsAiming   = aim;
        // 낙사 감지/부활/출발선 배치는 MatchManager(호스트 권위)가 담당(RPC -> TeleportTo).
    }

    void LateUpdate()
    {
        if (!IsSpawned || !IsOwner) return;
        if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }

        if (AimActive()) UpdateAimCamera();
        else             UpdateFollowCamera();

        UpdateAimRay(); // 조준점/타깃 갱신(발사 확장용)
    }

    // ============================ 전환 ============================
    void EnterAim()
    {
        // 코스는 START_Z(+Z)에서 GOAL_Z(-Z)로 진행 -> 골 방향(-Z)은 yaw 180.
        _yaw   = faceCourseOnAimStart ? 180f : transform.eulerAngles.y;
        _pitch = Mathf.Clamp(10f, aimMinPitch, aimMaxPitch);
    }

    // ============================ 입력: 마우스룩 ============================
    void HandleMouseLook()
    {
        if (_cursorFreeOverride) return; // 커서 해제 중엔 시야 회전 정지(에디터 클릭 중)
        var mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 d = mouse.delta.ReadValue(); // 프레임 픽셀 delta (dt 곱하지 않음)
        _yaw   += d.x * mouseSensitivity;
        _pitch += (invertY ? d.y : -d.y) * mouseSensitivity;
        _pitch  = Mathf.Clamp(_pitch, aimMinPitch, aimMaxPitch);
    }

    // ============================ 입력: 이동 ============================
    void HandleMovementAim()
    {
        ReadMove(out float h, out float v, out bool jump);

        // 카메라 yaw 기준 전/후/좌/우 스트레이프.
        Quaternion flat = Quaternion.Euler(0f, _yaw, 0f);
        Vector3 dir = flat * Vector3.forward * v + flat * Vector3.right * h;
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        // 몸은 카메라 yaw 를 향한다(조준 스트레이프). 즉시 정렬.
        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

        ApplyMotion(dir, jump);
    }

    void HandleMovementFollow()
    {
        ReadMove(out float h, out float v, out bool jump);

        Vector3 dir = new Vector3(h, 0f, v);
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        // 이동 방향으로 몸을 돌린다(카메라는 뒤를 따라옴).
        if (dir.sqrMagnitude > 0.0001f)
        {
            Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, target,
                                                  turnSpeed * Time.deltaTime);
        }

        ApplyMotion(dir, jump);
    }

    void ReadMove(out float h, out float v, out bool jump)
    {
        h = 0f; v = 0f; jump = false;
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.aKey.isPressed) h -= 1f;
        if (kb.dKey.isPressed) h += 1f;
        if (kb.wKey.isPressed) v += 1f;
        if (kb.sKey.isPressed) v -= 1f;
        jump = kb.spaceKey.wasPressedThisFrame;
    }

    // 공통: 중력·점프·이동 적용.
    void ApplyMotion(Vector3 dir, bool jump)
    {
        if (_cc.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f; // 지면에 살짝 붙여 isGrounded 안정화
        if (jump && _cc.isGrounded)
            _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        _verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = dir * moveSpeed;
        velocity.y = _verticalVelocity;
        _cc.Move(velocity * Time.deltaTime);
    }

    // ============================ 커서 ============================
    void HandleCursor(bool aim)
    {
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
            _cursorFreeOverride = !_cursorFreeOverride; // Esc 토글

        var mouse = Mouse.current;
        if (aim && _cursorFreeOverride && mouse != null && mouse.leftButton.wasPressedThisFrame)
            _cursorFreeOverride = false; // 조준 중 게임 클릭 -> 재잠금

        SetCursor(aim && !_cursorFreeOverride);
    }

    void SetCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !locked;
    }

    // ============================ 카메라: 조준(오버숄더 궤도) ============================
    void UpdateAimCamera()
    {
        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 pivot = transform.position + Vector3.up * aimHeight + (rot * Vector3.right) * aimShoulder;
        Vector3 dir = rot * Vector3.back; // 피벗 뒤쪽

        float dist = aimDistance;
        if (camCollisionMask.value != 0 &&
            Physics.SphereCast(pivot, camSphereRadius, dir, out RaycastHit hit,
                aimDistance, camCollisionMask, QueryTriggerInteraction.Ignore))
        {
            dist = Mathf.Max(camMinDistance, hit.distance - camCollisionSkin);
        }

        Vector3 camPos = pivot + dir * dist;
        ClampToTerrain(ref camPos);

        _cam.transform.position = camPos;
        _cam.transform.rotation = rot; // 카메라 전방 = 조준 방향(중앙 조준점이 이 방향)
    }

    // ============================ 카메라: 후방추적(기존) ============================
    void UpdateFollowCamera()
    {
        Vector3 pivot = transform.position + Vector3.up * camHeight;
        Quaternion rot = Quaternion.Euler(camPitch, transform.eulerAngles.y, 0f);
        Vector3 dir = (rot * Vector3.back).normalized; // 플레이어 뒤쪽(+ 살짝 위)

        float dist = camDistance;
        if (camCollisionMask.value != 0 &&
            Physics.SphereCast(pivot, camSphereRadius, dir, out RaycastHit hit,
                camDistance, camCollisionMask, QueryTriggerInteraction.Ignore))
        {
            dist = Mathf.Max(camMinDistance, hit.distance - camCollisionSkin);
        }

        Vector3 camPos = pivot + dir * dist;
        ClampToTerrain(ref camPos);

        _cam.transform.position = camPos;
        _cam.transform.rotation = Quaternion.LookRotation(pivot - camPos, Vector3.up);
    }

    // 지형 메시 범위(중앙 정렬 X:[-40,40], Z:[-80,80]) 안에서 카메라가 지면을 뚫지 않게 y 클램프.
    void ClampToTerrain(ref Vector3 camPos)
    {
        float halfX = TerrainGenerator.SIZE_X / 2f;
        float halfZ = TerrainGenerator.SIZE_Z / 2f;
        if (camPos.x >= -halfX && camPos.x <= halfX &&
            camPos.z >= -halfZ && camPos.z <= halfZ)
        {
            float minY = TerrainGenerator.SampleHeight(camPos.x, camPos.z) + camGroundClearance;
            if (camPos.y < minY) camPos.y = minY;
        }
    }

    // 카메라 중앙에서 나가는 조준 레이 + 히트 지점(자기 콜라이더는 무시). 발사 확장용.
    void UpdateAimRay()
    {
        Ray r = new Ray(_cam.transform.position, _cam.transform.forward);
        AimRay = r;

        int n = Physics.RaycastNonAlloc(r, _rayBuf, aimRayDistance, ~0, QueryTriggerInteraction.Ignore);
        float best = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            var col = _rayBuf[i].collider;
            if (col == null) continue;
            var ct = col.transform;
            if (ct == transform || ct.IsChildOf(transform)) continue; // 자기 자신 제외
            if (_rayBuf[i].distance < best) { best = _rayBuf[i].distance; found = true; AimPoint = _rayBuf[i].point; }
        }
        if (!found) AimPoint = r.origin + r.direction * aimRayDistance;
    }

    // ============================ 소유자 전용 순간이동(MatchManager 가 RPC 로 호출) ============================
    /// <summary>
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
            _net.Teleport(spawn, transform.rotation, transform.localScale);
            _cc.enabled = true;
            Physics.SyncTransforms(); // 재활성 CC 가 새 위치를 인식(isGrounded/다음 Move)
        }
        else
        {
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
}
}
