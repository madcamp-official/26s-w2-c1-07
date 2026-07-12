namespace RouletteParty.Net
{
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem; // 새 Input System (Keyboard.current / Mouse.current)
using RouletteParty.Match;      // MatchManager, MatchPhase, Structure
using RouletteParty.Map;        // ClimbMapGenerator (PREP 비행 범위 클램프)
using RouletteParty.Audio;      // AudioManager (점프/착지/탈락 사운드)
using RouletteParty.Core;       // SettingsManager (감도/Y반전/패널 열림 상태)

/// <summary>
/// 소유자 권위(ClientNetworkTransform) 3인칭 플레이어. 클라이밍 전환 명세 4절 구현.
///
/// 모드(소유자 로컬, 페이즈에 따라 자동 전환):
///  (A) 등반(PLAY 등): 배그식 3인칭 마우스룩. 점프 dy 최대 = jumpHeight(1).
///      낙하 추적: 공중 최고점 - 착지점 >= fallReportMin 이면 서버에 보고(ReportFallServerRpc).
///      체력·사망 판정은 서버(MatchManager)가 한다(체력은 비공개 정보라 클라는 모른다).
///  (B) 준비(PREP): 본체는 바닥에 잠금, 자유 비행 고스트 카메라로 이동. WASD 는 x/z 수평
///      전용(시선 상하 무관), Space 상승 / Shift 하강. 마우스룩으로 시선만 회전.
///      지면 범위(플레이어 이동 가능 범위와 동일) 전체를 날며 구조물을 설치한다(설치 자체는 PrepClientUI,
///      설치 가능 위치는 구조물 생성 범위로 더 좁게 제한).
///  (C) 탈락(Dead): 입력 잠금, 본체 렌더·콜라이더 off, 카메라는 생존자 추적 관전(좌클릭 순환).
///
/// Dead 는 서버가 쓰는 NetworkVariable 로 전 클라에 전파된다(렌더 분기·관전 필터의 근거).
/// 보이지 않는 구조물과 충돌하면 RevealStructureServerRpc 로 전원 일시 공개를 요청한다.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    [Header("이동")]
    public float moveSpeed = 6f;
    [Tooltip("점프로 도달하는 dy 최댓값(명세: 1).")]
    public float jumpHeight = 1f;
    public float gravity = -20f;
    [Tooltip("(후방추적 전용) 이동 방향으로 몸을 돌리는 속도.")]
    public float turnSpeed = 12f;

    [Header("낙하 보고")]
    [Tooltip("이 높이 이상 낙하 착지 시 서버에 보고한다(데미지 계산·차감은 서버).")]
    public float fallReportMin = 3f;

    [Header("카메라(공통)")]
    public float camHeight = 2.4f;
    public float camSphereRadius = 0.3f;
    public float camMinDistance = 0.8f;
    public float camCollisionSkin = 0.2f;
    [Tooltip("카메라 오클루전용 LayerMask. 비워두면(Nothing) 스폰 시 'Ground' 레이어로 자동 설정.")]
    public LayerMask camCollisionMask;

    [Header("후방추적 카메라(보존)")]
    public float camDistance = 6f;
    public float camPitch = 18f;

    [Header("마우스룩 조준")]
    [Tooltip("켜면 마우스룩 조준 시점. 끄면 옛 후방추적 시점(보존용).")]
    public bool useAimView = true;
    public float mouseSensitivity = 0.12f;
    public bool invertY = false;
    public float aimMinPitch = -35f;
    public float aimMaxPitch = 70f;
    public float aimDistance = 4.5f;
    public float aimShoulder = 0.7f;
    public float aimHeight = 1.6f;
    [Tooltip("조준 레이(설치/확장용) 최대 거리.")]
    public float aimRayDistance = 500f;

    [Header("준비 페이즈 비행 카메라")]
    [Tooltip("PREP 자유 비행 수평(x/z) 이동 속도. WASD 는 시선 상하와 무관하게 수평으로만 움직인다.")]
    public float flySpeed = 8f;
    [Tooltip("PREP 자유 비행 수직 이동 속도(Space = 상승, Shift = 하강).")]
    public float flyVerticalSpeed = 6f;

    // ---- 상태 접근점 ----
    public bool IsAiming { get; private set; }
    public Ray AimRay { get; private set; }
    public Vector3 AimPoint { get; private set; }
    /// <summary>탈락 여부(서버 write, 전 클라 read). 렌더·콜라이더·입력·관전의 단일 근거.</summary>
    public NetworkVariable<bool> Dead = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 캐시
    CharacterController _cc;
    NetworkTransform _net;
    Camera _cam;
    Renderer[] _bodyRenderers;
    float _verticalVelocity;

    // 마우스룩 상태
    float _yaw;
    float _pitch;
    bool _cursorFreeOverride;

    // 낙하 추적(소유자)
    float _airApexY;
    bool _wasAirborne;

    // 준비 비행(소유자)
    bool _flying;
    Vector3 _flyPos;

    // 관전(소유자·탈락 시)
    int _spectateIndex;

    // 보이지 않는 구조물 공개 요청 스로틀
    double _nextRevealTime;

    static readonly RaycastHit[] _rayBuf = new RaycastHit[8];

    public override void OnNetworkSpawn()
    {
        _cc  = GetComponent<CharacterController>();
        _net = GetComponent<NetworkTransform>();
        _bodyRenderers = GetComponentsInChildren<Renderer>(true);

        Dead.OnValueChanged += OnDeadChanged;
        ApplyDeadVisual(Dead.Value);

        if (IsOwner)
        {
            _cam   = Camera.main;
            _yaw   = transform.eulerAngles.y;
            _pitch = camPitch;
            _airApexY = transform.position.y;

            if (camCollisionMask.value == 0)
                camCollisionMask = LayerMask.GetMask("Ground");
        }
    }

    public override void OnNetworkDespawn()
    {
        Dead.OnValueChanged -= OnDeadChanged;
        if (IsOwner) SetCursor(false);
    }

    // ============================ 탈락 상태 (전 클라) ============================
    void OnDeadChanged(bool _, bool now)
    {
        ApplyDeadVisual(now);
        if (now) AudioManager.Play(Sfx.Death); // 누군가의 탈락은 전원에게 들린다(전 클라에서 실행됨)
    }

    void ApplyDeadVisual(bool dead)
    {
        // 본체 렌더 off + 콜라이더(CC) off: 좁은 발판에서 시체가 길을 막지 않게.
        if (_bodyRenderers != null)
            foreach (var r in _bodyRenderers)
                if (r != null) r.enabled = !dead;
        if (_cc == null) _cc = GetComponent<CharacterController>();
        if (_cc != null) _cc.enabled = !dead;
        if (dead) _verticalVelocity = 0f;
    }

    MatchPhase Phase()
    {
        var mm = MatchManager.Instance;
        return (mm != null && mm.IsSpawned) ? mm.CurrentPhase : MatchPhase.Play; // 매치 없으면 자유 등반 테스트
    }

    void Update()
    {
        if (!IsSpawned || !IsOwner) return;

        MatchPhase phase = Phase();
        bool aim = useAimView;
        HandleCursor(aim);
        IsAiming = aim;

        // (C) 탈락: 입력 잠금 + 관전 대상 순환(좌클릭).
        if (Dead.Value)
        {
            _flying = false;
            HandleMouseLook();
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !_cursorFreeOverride)
                _spectateIndex++;
            return;
        }

        // (B) 준비: 본체 잠금 + 비행 카메라 이동.
        if (phase == MatchPhase.Prep)
        {
            if (!_flying)
            {
                _flying = true;
                _flyPos = _cam != null ? _cam.transform.position
                                       : transform.position + Vector3.up * 3f;
            }
            HandleMouseLook();
            HandleFlyMove();
            if (_cc != null && _cc.enabled) ApplyMotion(Vector3.zero, false); // 본체는 제자리(중력만)
            return;
        }

        // (A) 등반.
        _flying = false;
        if (aim)
        {
            HandleMouseLook();
            HandleMovementAim();
        }
        else
        {
            HandleMovementFollow();
        }
        TrackFall();
        // 낙사/부활/배치는 MatchManager(호스트 권위)가 RPC 로 지시한다.
    }

    void LateUpdate()
    {
        if (!IsSpawned || !IsOwner) return;
        if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }

        if (Dead.Value)                    UpdateSpectatorCamera();
        else if (_flying)                  UpdateFlyCamera();
        else if (useAimView)               UpdateAimCamera();
        else                               UpdateFollowCamera();

        UpdateAimRay();
    }

    // ============================ 입력: 마우스룩 ============================
    void HandleMouseLook()
    {
        if (_cursorFreeOverride) return;
        if (SettingsManager.IsOpen) return; // 설정 패널 조작 중 시점 회전 방지
        var mouse = Mouse.current;
        if (mouse == null) return;

        // 감도/Y반전은 설정(SettingsManager) 우선, 없으면 인스펙터 값 폴백.
        var opt = SettingsManager.Instance;
        float sens = opt != null ? opt.MouseSensitivity : mouseSensitivity;
        bool  inv  = opt != null ? opt.InvertY : invertY;

        Vector2 d = mouse.delta.ReadValue();
        _yaw   += d.x * sens;
        _pitch += (inv ? d.y : -d.y) * sens;
        _pitch  = Mathf.Clamp(_pitch, aimMinPitch, aimMaxPitch);
    }

    // ============================ 입력: 이동 ============================
    void HandleMovementAim()
    {
        ReadMove(out float h, out float v, out bool jump);

        Quaternion flat = Quaternion.Euler(0f, _yaw, 0f);
        Vector3 dir = flat * Vector3.forward * v + flat * Vector3.right * h;
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        ApplyMotion(dir, jump);
    }

    void HandleMovementFollow()
    {
        ReadMove(out float h, out float v, out bool jump);

        Vector3 dir = new Vector3(h, 0f, v);
        if (dir.sqrMagnitude > 1f) dir.Normalize();
        if (dir.sqrMagnitude > 0.0001f)
        {
            Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, turnSpeed * Time.deltaTime);
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

    void ApplyMotion(Vector3 dir, bool jump)
    {
        if (_cc == null || !_cc.enabled) return;
        if (_cc.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;
        if (jump && _cc.isGrounded)
        {
            _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            AudioManager.Play(Sfx.Jump);
        }
        _verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = dir * moveSpeed;
        velocity.y = _verticalVelocity;
        _cc.Move(velocity * Time.deltaTime);
    }

    // ============================ 낙하 추적(소유자) -> 서버 보고 ============================
    void TrackFall()
    {
        if (_cc == null || !_cc.enabled) return;
        float y = transform.position.y;
        bool grounded = _cc.isGrounded;

        if (!grounded)
        {
            if (y > _airApexY) _airApexY = y;
            _wasAirborne = true;
        }
        else
        {
            if (_wasAirborne)
            {
                float fall = _airApexY - y;
                if (fall >= 0.5f) AudioManager.Play(Sfx.Land); // 잔걸음 접지 스팸 방지 하한
                if (fall >= fallReportMin)
                    ReportFallServerRpc(fall);
                _wasAirborne = false;
            }
            _airApexY = y;
        }
    }

    // 소유자 -> 서버. 데미지 계산·체력 차감·사망 판정은 서버(MatchManager, 체력 비공개).
    [Rpc(SendTo.Server)]
    void ReportFallServerRpc(float fallHeight, RpcParams rpcParams = default)
    {
        var mm = MatchManager.Instance;
        if (mm != null) mm.ApplyFallDamage(OwnerClientId, fallHeight);
    }

    // ============================ 보이지 않는 구조물 공개(충돌 상호작용) ============================
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!IsOwner || !IsSpawned) return;
        if (Time.timeAsDouble < _nextRevealTime) return;

        var ob = hit.collider.GetComponentInParent<Structure>();
        if (ob == null || !ob.IsInvisibleKind || !ob.IsSpawned) return;

        _nextRevealTime = Time.timeAsDouble + 0.5; // 스팸 방지
        var mm = MatchManager.Instance;
        if (mm != null) mm.RevealStructureServerRpc(ob.NetworkObject);
    }

    // ============================ 커서 ============================
    void HandleCursor(bool aim)
    {
        if (SettingsManager.IsOpen) { SetCursor(false); return; } // 설정 패널 중엔 커서 해제
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
            _cursorFreeOverride = !_cursorFreeOverride;

        var mouse = Mouse.current;
        if (aim && _cursorFreeOverride && mouse != null && mouse.leftButton.wasPressedThisFrame)
            _cursorFreeOverride = false;

        SetCursor(aim && !_cursorFreeOverride);
    }

    void SetCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !locked;
    }

    // ============================ 준비 비행 ============================
    void HandleFlyMove()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        float h = 0f, v = 0f, up = 0f;
        if (kb.aKey.isPressed) h -= 1f;
        if (kb.dKey.isPressed) h += 1f;
        if (kb.wKey.isPressed) v += 1f;
        if (kb.sKey.isPressed) v -= 1f;
        if (kb.spaceKey.isPressed) up += 1f;
        if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) up -= 1f;

        // 수평(x/z): 시선 pitch 는 무시하고 yaw 만 사용 -> 위를 보고 W 를 눌러도 수평 전진.
        Quaternion yawRot = Quaternion.Euler(0f, _yaw, 0f);
        Vector3 planar = yawRot * Vector3.forward * v + yawRot * Vector3.right * h;
        if (planar.sqrMagnitude > 1f) planar.Normalize();

        _flyPos += planar * flySpeed * Time.deltaTime;
        _flyPos += Vector3.up * (up * flyVerticalSpeed * Time.deltaTime); // 수직은 Space/Shift 전용

        // PREP 이동 범위로 클램프(경계 밖 시점 방지). 지면 크기(FloorWidth/FloorDepth) 기준 —
        // 구조물 생성 범위(MapWidth/MapDepth)보다 넓어, 비행 자체는 지면 전체 위를 자유로이 오간다.
        var gen = ClimbMapGenerator.Instance;
        if (gen != null)
        {
            _flyPos.x = Mathf.Clamp(_flyPos.x, -gen.FloorWidth * 0.5f + 0.3f, gen.FloorWidth * 0.5f - 0.3f);
            _flyPos.z = Mathf.Clamp(_flyPos.z, -gen.FloorDepth * 0.5f + 0.3f, gen.FloorDepth * 0.5f - 0.3f);
            _flyPos.y = Mathf.Clamp(_flyPos.y, 0.5f, gen.MapHeight + 2f);
        }
    }

    void UpdateFlyCamera()
    {
        _cam.transform.position = _flyPos;
        _cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    // ============================ 관전(탈락) ============================
    void UpdateSpectatorCamera()
    {
        Transform target = PickSpectateTarget();
        if (target == null) target = transform; // 볼 사람이 없으면 자기 자리

        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 pivot = target.position + Vector3.up * aimHeight;
        Vector3 dir = rot * Vector3.back;

        float dist = aimDistance;
        if (camCollisionMask.value != 0 &&
            Physics.SphereCast(pivot, camSphereRadius, dir, out RaycastHit hit,
                aimDistance, camCollisionMask, QueryTriggerInteraction.Ignore))
        {
            dist = Mathf.Max(camMinDistance, hit.distance - camCollisionSkin);
        }

        _cam.transform.position = pivot + dir * dist;
        _cam.transform.rotation = rot;
    }

    Transform PickSpectateTarget()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.SpawnManager == null) return null;

        // 생존 중인 다른 플레이어 목록(관전 후보).
        var candidates = new System.Collections.Generic.List<Transform>();
        foreach (NetworkObject no in nm.SpawnManager.SpawnedObjectsList)
        {
            if (no == null || !no.IsPlayerObject) continue;
            if (no.OwnerClientId == OwnerClientId) continue;
            var pc = no.GetComponent<PlayerController>();
            if (pc != null && pc.Dead.Value) continue;
            candidates.Add(no.transform);
        }
        if (candidates.Count == 0) return null;
        return candidates[Mathf.Abs(_spectateIndex) % candidates.Count];
    }

    // ============================ 카메라: 조준(오버숄더) ============================
    void UpdateAimCamera()
    {
        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 pivot = transform.position + Vector3.up * aimHeight + (rot * Vector3.right) * aimShoulder;
        Vector3 dir = rot * Vector3.back;

        float dist = aimDistance;
        if (camCollisionMask.value != 0 &&
            Physics.SphereCast(pivot, camSphereRadius, dir, out RaycastHit hit,
                aimDistance, camCollisionMask, QueryTriggerInteraction.Ignore))
        {
            dist = Mathf.Max(camMinDistance, hit.distance - camCollisionSkin);
        }

        _cam.transform.position = pivot + dir * dist;
        _cam.transform.rotation = rot;
    }

    // ============================ 카메라: 후방추적(보존) ============================
    void UpdateFollowCamera()
    {
        Vector3 pivot = transform.position + Vector3.up * camHeight;
        Quaternion rot = Quaternion.Euler(camPitch, transform.eulerAngles.y, 0f);
        Vector3 dir = (rot * Vector3.back).normalized;

        float dist = camDistance;
        if (camCollisionMask.value != 0 &&
            Physics.SphereCast(pivot, camSphereRadius, dir, out RaycastHit hit,
                camDistance, camCollisionMask, QueryTriggerInteraction.Ignore))
        {
            dist = Mathf.Max(camMinDistance, hit.distance - camCollisionSkin);
        }

        Vector3 camPos = pivot + dir * dist;
        _cam.transform.position = camPos;
        _cam.transform.rotation = Quaternion.LookRotation(pivot - camPos, Vector3.up);
    }

    // 카메라 중앙 조준 레이 + 히트 지점(자기 자신 제외). 구조물 설치(PrepClientUI)가 사용.
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
            if (ct == transform || ct.IsChildOf(transform)) continue;
            if (_rayBuf[i].distance < best) { best = _rayBuf[i].distance; found = true; AimPoint = _rayBuf[i].point; }
        }
        if (!found) AimPoint = r.origin + r.direction * aimRayDistance;
    }

    // ============================ 소유자 전용 순간이동(MatchManager RPC 가 호출) ============================
    public void TeleportTo(Vector3 spawn)
    {
        if (!IsOwner) return;
        if (_cc == null)  _cc  = GetComponent<CharacterController>();
        if (_net == null) _net = GetComponent<NetworkTransform>();

        bool wasEnabled = _cc != null && _cc.enabled;
        if (_net != null)
        {
            if (_cc != null) _cc.enabled = false;
            _net.Teleport(spawn, transform.rotation, transform.localScale);
            if (_cc != null) _cc.enabled = wasEnabled || !Dead.Value;
            Physics.SyncTransforms();
        }
        else
        {
            if (_cc != null) _cc.enabled = false;
            transform.position = spawn;
            if (_cc != null) _cc.enabled = wasEnabled || !Dead.Value;
            Physics.SyncTransforms();
        }

        _verticalVelocity = 0f;
        _airApexY = spawn.y;   // 텔레포트를 낙하로 오인하지 않게 리셋
        _wasAirborne = false;
    }
}
}
