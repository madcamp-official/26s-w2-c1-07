namespace RouletteParty.Net
{
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using RouletteParty.Match;

/// <summary>
/// 좌클릭(꾹) 잡기 2종. Player 프리팹 루트(PlayerController 옆)에 부착한다.
///
///  (1) 렛지 잡기(Only Up 식, 소유자 로컬): 등반 중 공중에서 좌클릭을 누르고 있으면 전방
///      발판 모서리를 감지해 매달린다. 매달린 동안 Space = 올라서기(맨틀), 좌클릭 해제/S = 낙하.
///      이동은 소유자 권위(ClientNetworkTransform)이므로 전 과정 로컬 처리 -> 위치가 그대로 복제
///      되고 원격은 접지 프로브가 공중으로 판정해 점프 포즈(Airborne)로 보인다.
///      모서리 콜라이더의 로컬 좌표로 매달림 지점을 기억하므로 회전/이동 발판도 따라간다.
///      나에게 숨겨진 투명 구조물은 잡을 수 없다(조준 레이와 동일한 가시성 규칙).
///
///  (2) 플레이어 잡기(폴가이즈 식, 서버 검증): 지상에서 좌클릭을 누르고 있으면 전방 근접
///      상대를 붙잡는다. 붙잡힌 쪽은 이동 감속 + 점프 봉인(서버가 Grabbed 를 써서 전파,
///      적용은 피해자 소유자가 자기 이동에서 한다). 잡는 쪽도 감속된다(리스크). 최대 지속·
///      쿨다운·거리 이탈·탈락·페이즈 종료 해제는 서버 틱이 강제한다. 매달린 상대를 잡으면
///      떨어뜨린다(렛지 잡기 쪽에서 Grabbed 를 감지해 스스로 놓는다).
///
/// PlayerController 와의 계약: 등반 프레임마다 ClimbTick() 을 호출하고, true(매달림/맨틀 중)면
/// 통상 이동을 생략한다. ApplyMotion 은 MoveSpeedMultiplier/JumpBlocked 를 반영한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController))]
public class PlayerGrab : NetworkBehaviour
{
    [Header("렛지 잡기")]
    [Tooltip("전방 벽면 감지 거리(m). 캡슐 반지름(0.5)보다 커야 한다.")]
    [SerializeField] private float _ledgeReach = 0.8f;
    [Tooltip("이 수직 속도 이하(= 하강 시작)일 때만 잡는다. 상승 중 자석처럼 붙는 억지 잡기 방지.")]
    [SerializeField] private float _grabMaxRiseSpeed = 0.5f;
    [Tooltip("공중에 이 시간(초) 이상 있어야 잡는다. 점프 직후 옆 벽에 즉시 붙는 것 방지.")]
    [SerializeField] private float _minAirTime = 0.12f;
    [Tooltip("벽면을 이 각도(도) 이내로 마주봐야 잡는다.")]
    [SerializeField] private float _maxFacingAngle = 55f;
    [Tooltip("매달림 진입 블렌드 시간(초). 0 이면 즉시 스냅.")]
    [SerializeField] private float _hangBlendTime = 0.12f;
    [Tooltip("잡을 수 있는 모서리 높이 범위: 발끝 기준 최소(이보다 낮으면 그냥 올라간다).")]
    [SerializeField] private float _ledgeMinAbove = 0.4f;
    [Tooltip("잡을 수 있는 모서리 높이 범위: 발끝 기준 최대(점프 dy 1 + 팔 리치).")]
    [SerializeField] private float _ledgeMaxAbove = 2.2f;
    [Tooltip("매달렸을 때 발끝이 모서리에서 내려가는 깊이(m).")]
    [SerializeField] private float _hangDepth = 1.35f;
    [Tooltip("올라서기(맨틀) 소요 시간(초).")]
    [SerializeField] private float _mantleDuration = 0.25f;
    [Tooltip("모서리에서 안쪽으로 올라서는 거리(m).")]
    [SerializeField] private float _mantleInset = 0.45f;
    [Tooltip("놓은 뒤 재잡기 금지 시간(초). S 로 떨어질 때 즉시 재부착 방지.")]
    [SerializeField] private float _regrabDelay = 0.3f;

    [Header("플레이어 잡기")]
    [Tooltip("잡기 시도 거리(m).")]
    [SerializeField] private float _grabRange = 1.7f;
    [Tooltip("전방 판정 각도(도). 시선 기준 이 각도 안의 상대만 잡는다.")]
    [SerializeField] private float _grabAngle = 65f;
    [Tooltip("붙잡힌 쪽 이동속도 배율.")]
    [SerializeField] private float _victimSpeedMult = 0.35f;
    [Tooltip("잡는 쪽 이동속도 배율(리스크).")]
    [SerializeField] private float _grabberSpeedMult = 0.6f;
    [Tooltip("최대 지속(초). 서버 강제.")]
    [SerializeField] private float _grabMaxDuration = 2f;
    [Tooltip("놓은 뒤 쿨다운(초). 서버 강제.")]
    [SerializeField] private float _grabCooldown = 3f;
    [Tooltip("서버 거리 검증 여유(m). 복제 지연 보정.")]
    [SerializeField] private float _serverRangeSlack = 1.2f;

    /// <summary>붙잡힌 상태(서버 write, 전 클라 read). 피해자 소유자가 감속/점프 봉인에 사용.</summary>
    public NetworkVariable<bool> Grabbed = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    enum LedgeState { None, Hanging, Mantling }

    // ---- 소유자 로컬 상태 ----
    LedgeState _state;
    Transform _ledgeRef;        // 매달린 콜라이더(움직이는 발판 추적용)
    Vector3 _ledgeLocalPoint;   // 모서리 지점(콜라이더 로컬)
    Vector3 _ledgeLocalNormal;  // 벽면 법선(콜라이더 로컬, 수평)
    float _mantleT;
    Vector3 _mantleFrom;
    float _noRegrabUntil;
    float _airTime;             // 연속 공중 시간(즉시 붙기 방지 게이트)
    Vector3 _hangEnterPos;      // 매달림 진입 블렌드 시작 자세
    Quaternion _hangEnterRot;
    float _hangBlend;
    bool _grabbingPlayer;       // 내가 상대를 잡는 중(로컬 표시용, 진실은 서버)
    NetworkObjectReference _grabTargetRef;

    // ---- 서버 상태(잡는 쪽 오브젝트에 기록) ----
    PlayerGrab _srvVictim;
    double _srvGrabEnd;
    double _srvCooldownUntil;

    CharacterController _cc;
    PlayerController _pc;
    static readonly RaycastHit[] _rayBuf = new RaycastHit[8];
    static readonly Collider[] _overlapBuf = new Collider[8];

    /// <summary>이동속도 배율(소유자 시점): 붙잡힘 x 잡는 중 페널티.</summary>
    public float MoveSpeedMultiplier =>
        (Grabbed.Value ? _victimSpeedMult : 1f) * (_grabbingPlayer ? _grabberSpeedMult : 1f);

    /// <summary>붙잡힌 동안 점프 봉인.</summary>
    public bool JumpBlocked => Grabbed.Value;

    public override void OnNetworkSpawn()
    {
        _cc = GetComponent<CharacterController>();
        _pc = GetComponent<PlayerController>();
        _state = LedgeState.None;
        _grabbingPlayer = false;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer) ServerRelease(); // 잡은 채 나가면 피해자 해방
    }

    void Update()
    {
        if (IsServer && IsSpawned) ServerTick();
    }

    // ============================ 소유자: 등반 프레임 훅 ============================
    /// <summary>등반 모드에서 매 프레임 호출(PlayerController). 반환 true = 매달림/맨틀 중이라
    /// 통상 이동을 생략해야 한다. 커서가 풀려 있으면 호출하지 않는다.</summary>
    public bool ClimbTick()
    {
        if (!IsOwner || !IsSpawned || _pc == null || _cc == null) return false;
        if (Dead()) { AbortLedge(false); ReleasePlayerGrab(); return false; }

        var mouse = Mouse.current;
        bool hold = mouse != null && mouse.leftButton.isPressed;

        // 공중 시간 추적(점프 직후 옆 벽에 즉시 붙는 억지 잡기 방지 게이트).
        if (_cc.enabled && _cc.isGrounded) _airTime = 0f;
        else _airTime += Time.deltaTime;

        switch (_state)
        {
            case LedgeState.Hanging:  return TickHanging(hold);
            case LedgeState.Mantling: return TickMantling();
        }

        // ---- None: 잡기 시도 ----
        if (!hold) { ReleasePlayerGrab(); return false; }

        if (!_cc.isGrounded)
        {
            ReleasePlayerGrab(); // 공중에선 렛지 우선
            // 떨어질 때만(상승 중 금지) + 최소 공중 시간 경과 후에만 잡는다.
            if (Time.time >= _noRegrabUntil
                && _airTime >= _minAirTime
                && _pc.VerticalVelocity <= _grabMaxRiseSpeed)
                TryStartHang();
            return _state != LedgeState.None;
        }

        TickPlayerGrab();
        return false;
    }

    bool Dead() => _pc.Dead.Value;

    /// <summary>페이즈 전환 등 외부 사정으로 잡기 전면 취소(PlayerController 가 호출, 소유자).</summary>
    public void CancelAll()
    {
        if (!IsOwner) return;
        AbortLedge(true);
        ReleasePlayerGrab();
    }

    // ============================ 렛지: 감지 ============================
    void TryStartHang()
    {
        float footY = _pc.FootY;
        Vector3 fwd = transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) return;
        fwd.Normalize();

        // 1) 윗면 프로브: 몸 앞(리치만큼)의 지점에서, 잡기 상한 위 -> 아래로 쏘아 밟을 면을 찾는다.
        //    벽 레이를 먼저 쏘면 모서리 높이에 따라 레이가 위/아래로 빗나가므로 윗면부터 찾는 게 강건하다.
        Vector3 probe = transform.position + fwd * (_cc.radius + _ledgeReach - 0.5f);
        probe.y = footY + _ledgeMaxAbove + 0.5f;
        float probeLen = _ledgeMaxAbove + 0.5f - _ledgeMinAbove;
        if (!RaycastSolid(probe, Vector3.down, probeLen, out RaycastHit top)) return;
        if (top.normal.y < 0.6f) return; // 밟을 수 없는 면

        float ledgeY = top.point.y;
        if (ledgeY < footY + _ledgeMinAbove || ledgeY > footY + _ledgeMaxAbove) return;

        // 2) 벽면 보정: 모서리 살짝 아래 높이에서 전방 레이로 실제 면의 위치/법선을 얻는다
        //    (매달림 위치를 면에 밀착). 실패하면 시선 반대 방향으로 폴백.
        Vector3 faceOrigin = new Vector3(transform.position.x, ledgeY - 0.15f, transform.position.z);
        Vector3 facePoint; Vector3 faceNormal;
        if (RaycastSolid(faceOrigin, fwd, _cc.radius + _ledgeReach, out RaycastHit wall)
            && Mathf.Abs(wall.normal.y) < 0.5f)
        {
            faceNormal = new Vector3(wall.normal.x, 0f, wall.normal.z).normalized;
            // 벽을 어느 정도 마주봐야 잡힌다(옆·뒤 방향 억지 잡기 방지).
            if (Vector3.Angle(fwd, -faceNormal) > _maxFacingAngle) return;
            facePoint = wall.point;
        }
        else
        {
            facePoint = top.point;
            faceNormal = -fwd;
        }

        // 매달림 시작: 모서리를 콜라이더 로컬로 기억(움직이는 발판 추적).
        _ledgeRef = top.collider.transform;
        Vector3 edgePoint = new Vector3(facePoint.x, ledgeY, facePoint.z);
        _ledgeLocalPoint = _ledgeRef.InverseTransformPoint(edgePoint);
        _ledgeLocalNormal = _ledgeRef.InverseTransformDirection(faceNormal);

        _cc.enabled = false; // 매달림 동안 CC 시뮬 정지(밀림 방지)
        _pc.OnGrabHangStart();
        _state = LedgeState.Hanging;
        _hangEnterPos = transform.position; // 순간이동 대신 블렌드 진입(억지 잡기 느낌 방지)
        _hangEnterRot = transform.rotation;
        _hangBlend = 0f;
        ApplyHangPose();
    }

    // 자기 자신/다른 플레이어/숨겨진 투명 구조물을 제외한 첫 히트.
    bool RaycastSolid(Vector3 origin, Vector3 dir, float dist, out RaycastHit best)
    {
        best = default;
        int n = Physics.RaycastNonAlloc(origin, dir, _rayBuf, dist, ~0, QueryTriggerInteraction.Ignore);
        float bestD = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            var col = _rayBuf[i].collider;
            if (col == null) continue;
            var t = col.transform;
            if (t == transform || t.IsChildOf(transform)) continue;
            if (col.GetComponentInParent<PlayerController>() != null) continue; // 사람은 렛지가 아니다
            var st = col.GetComponentInParent<Structure>();
            if (st != null && st.IsHiddenFromLocal) continue; // 안 보이면 못 잡는다
            if (_rayBuf[i].distance < bestD) { bestD = _rayBuf[i].distance; best = _rayBuf[i]; found = true; }
        }
        return found;
    }

    // ============================ 렛지: 매달림/맨틀 ============================
    Vector3 EdgeWorld()   => _ledgeRef.TransformPoint(_ledgeLocalPoint);
    Vector3 NormalWorld() { var v = _ledgeRef.TransformDirection(_ledgeLocalNormal); v.y = 0f; return v.sqrMagnitude > 0.001f ? v.normalized : -transform.forward; }

    void ApplyHangPose()
    {
        Vector3 edge = EdgeWorld();
        Vector3 n = NormalWorld();
        // 발끝 = 모서리 - hangDepth, 몸은 벽면 밖으로 캡슐 반지름만큼.
        Vector3 pos = edge + n * (_cc.radius + 0.05f);
        pos.y = edge.y - _hangDepth + 1f; // FootY 오프셋(캡슐 중심 y - 1 = 발끝)
        Quaternion rot = Quaternion.LookRotation(-n, Vector3.up); // 벽을 마주본다

        // 진입 블렌드: 잡은 순간 자세에서 매달림 자세로 부드럽게. 이후엔 밀착 유지(이동 발판 추적).
        _hangBlend = _hangBlendTime <= 0f ? 1f
            : Mathf.Min(1f, _hangBlend + Time.deltaTime / _hangBlendTime);
        float t = Mathf.SmoothStep(0f, 1f, _hangBlend);
        transform.position = Vector3.Lerp(_hangEnterPos, pos, t);
        transform.rotation = Quaternion.Slerp(_hangEnterRot, rot, t);
    }

    bool TickHanging(bool hold)
    {
        var kb = Keyboard.current;
        bool drop   = !hold || (kb != null && kb.sKey.isPressed) || Grabbed.Value; // 붙잡히면 강제 낙하
        bool mantle = kb != null && kb.spaceKey.wasPressedThisFrame;

        if (_ledgeRef == null) { AbortLedge(true); return false; } // 발판이 파괴됨(라운드 재생성 등)

        if (mantle)
        {
            _state = LedgeState.Mantling;
            _mantleT = 0f;
            _mantleFrom = transform.position;
            return true;
        }
        if (drop)
        {
            AbortLedge(true);
            _noRegrabUntil = Time.time + _regrabDelay;
            return false;
        }

        ApplyHangPose(); // 진입 블렌드 + 움직이는 발판 추적
        return true;
    }

    bool TickMantling()
    {
        if (_ledgeRef == null) { AbortLedge(true); return false; }

        _mantleT += Time.deltaTime / Mathf.Max(0.05f, _mantleDuration);
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_mantleT));

        Vector3 edge = EdgeWorld();
        Vector3 inward = -NormalWorld();
        Vector3 to = edge + inward * _mantleInset;
        to.y = edge.y + 0.05f + 1f; // 발끝을 모서리 위로(+FootY 오프셋)

        // 위로 먼저, 안쪽은 나중(모서리에 걸리지 않는 L자 궤적).
        Vector3 p = _mantleFrom;
        p.y = Mathf.Lerp(_mantleFrom.y, to.y, Mathf.Min(1f, t * 1.6f));
        p.x = Mathf.Lerp(_mantleFrom.x, to.x, t);
        p.z = Mathf.Lerp(_mantleFrom.z, to.z, t);
        transform.position = p;

        if (_mantleT >= 1f)
        {
            _state = LedgeState.None;
            _cc.enabled = !Dead();
            Physics.SyncTransforms();
            _pc.OnGrabMantleEnd();
        }
        return _state != LedgeState.None;
    }

    /// <summary>매달림/맨틀 중단. restoreCc: 탈락 처리(ApplyDeadVisual)가 CC 를 관리 중이면 false.</summary>
    void AbortLedge(bool restoreCc)
    {
        if (_state == LedgeState.None) return;
        _state = LedgeState.None;
        _ledgeRef = null;
        if (restoreCc && _cc != null && !Dead())
        {
            _cc.enabled = true;
            Physics.SyncTransforms();
        }
        _pc.OnGrabHangStart(); // 수직 속도 0 에서 낙하 재시작
    }

    // ============================ 플레이어 잡기: 소유자 ============================
    void TickPlayerGrab()
    {
        if (_grabbingPlayer)
        {
            // 유지 조건 검사(해제는 서버도 강제하지만 로컬에서 먼저 끊어 반응성 확보).
            if (!TargetStillInRange()) ReleasePlayerGrab();
            return;
        }

        // 획득 시도: 전방 근접 생존자.
        Vector3 center = transform.position + transform.forward * (_grabRange * 0.6f);
        int n = Physics.OverlapSphereNonAlloc(center, _grabRange * 0.7f, _overlapBuf, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var col = _overlapBuf[i];
            if (col == null || col.transform == transform || col.transform.IsChildOf(transform)) continue;
            var other = col.GetComponentInParent<PlayerGrab>();
            if (other == null || other == this || !other.IsSpawned) continue;
            if (other._pc != null && other._pc.Dead.Value) continue;

            Vector3 to = other.transform.position - transform.position; to.y = 0f;
            if (to.magnitude > _grabRange) continue;
            if (Vector3.Angle(transform.forward, to) > _grabAngle) continue;

            _grabbingPlayer = true;
            _grabTargetRef = other.NetworkObject;
            GrabServerRpc(_grabTargetRef, true);
            return;
        }
    }

    bool TargetStillInRange()
    {
        if (!_grabTargetRef.TryGet(out NetworkObject no) || no == null) return false;
        Vector3 to = no.transform.position - transform.position;
        return to.magnitude <= _grabRange + 0.8f;
    }

    void ReleasePlayerGrab()
    {
        if (!_grabbingPlayer) return;
        _grabbingPlayer = false;
        GrabServerRpc(_grabTargetRef, false);
    }

    // ============================ 플레이어 잡기: 서버 ============================
    [Rpc(SendTo.Server)]
    void GrabServerRpc(NetworkObjectReference targetRef, bool on, RpcParams rpcParams = default)
    {
        if (!on) { ServerRelease(); return; }

        double now = NetworkManager.ServerTime.Time;
        if (now < _srvCooldownUntil || _srvVictim != null) return;
        if (_pc != null && _pc.Dead.Value) return;

        var mm = MatchManager.Instance;
        if (mm != null && mm.IsSpawned && mm.CurrentPhase != MatchPhase.Play) return;

        if (!targetRef.TryGet(out NetworkObject no) || no == null) return;
        var victim = no.GetComponent<PlayerGrab>();
        if (victim == null || victim == this || victim.Grabbed.Value) return;
        if (victim._pc != null && victim._pc.Dead.Value) return;
        if (Vector3.Distance(transform.position, victim.transform.position) > _grabRange + _serverRangeSlack) return;

        _srvVictim = victim;
        _srvGrabEnd = now + _grabMaxDuration;
        victim.Grabbed.Value = true;
    }

    void ServerTick()
    {
        if (_srvVictim == null) return;
        double now = NetworkManager.ServerTime.Time;

        bool release =
            now >= _srvGrabEnd ||
            !_srvVictim.IsSpawned ||
            (_srvVictim._pc != null && _srvVictim._pc.Dead.Value) ||
            (_pc != null && _pc.Dead.Value) ||
            Vector3.Distance(transform.position, _srvVictim.transform.position) > _grabRange + _serverRangeSlack;

        var mm = MatchManager.Instance;
        if (mm != null && mm.IsSpawned && mm.CurrentPhase != MatchPhase.Play) release = true;

        if (release) ServerRelease();
    }

    void ServerRelease()
    {
        if (_srvVictim != null)
        {
            if (_srvVictim.IsSpawned) _srvVictim.Grabbed.Value = false;
            _srvVictim = null;
            _srvCooldownUntil = NetworkManager.ServerTime.Time + _grabCooldown;
        }
    }
}
}
