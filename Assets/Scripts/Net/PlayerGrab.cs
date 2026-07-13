namespace RouletteParty.Net
{
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using RouletteParty.Match;

/// <summary>
/// 좌클릭(꾹) 플레이어 잡기(폴가이즈 식, 서버 검증). Player 프리팹 루트에 부착한다.
///
/// 등반(PLAY) 중 지상에서 좌클릭을 누르고 있으면 전방 근접 상대를 붙잡는다. 붙잡힌 쪽은
/// 이동 감속 + 점프 봉인(서버가 Grabbed 를 써서 전파, 적용은 피해자 소유자가 자기 이동에서),
/// 잡는 쪽도 감속된다(리스크). 최대 지속·쿨다운·거리 이탈·탈락·페이즈 종료 해제는
/// 서버 틱이 강제한다(변조 방지).
///
/// PlayerController 와의 계약: 등반 프레임마다 ClimbTick() 호출(커서 잠금 상태에서만),
/// ApplyMotion 은 MoveSpeedMultiplier/JumpBlocked 를 반영, 페이즈 이탈 시 CancelAll().
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController))]
public class PlayerGrab : NetworkBehaviour
{
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

    // ---- 소유자 로컬 상태 ----
    bool _grabbingPlayer;       // 내가 상대를 잡는 중(로컬 표시용, 진실은 서버)
    NetworkObjectReference _grabTargetRef;

    // ---- 서버 상태(잡는 쪽 오브젝트에 기록) ----
    PlayerGrab _srvVictim;
    double _srvGrabEnd;
    double _srvCooldownUntil;

    CharacterController _cc;
    PlayerController _pc;
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

    /// <summary>페이즈 전환 등 외부 사정으로 잡기 취소(PlayerController 가 호출, 소유자).</summary>
    public void CancelAll()
    {
        if (!IsOwner) return;
        ReleasePlayerGrab();
    }

    // ============================ 소유자: 등반 프레임 훅 ============================
    /// <summary>등반 모드에서 매 프레임 호출(PlayerController). 커서가 풀려 있으면 호출하지 않는다.</summary>
    public void ClimbTick()
    {
        if (!IsOwner || !IsSpawned || _pc == null || _cc == null) return;

        var mouse = Mouse.current;
        bool hold = mouse != null && mouse.leftButton.isPressed;

        if (!hold || _pc.Dead.Value || !_cc.enabled || !_cc.isGrounded)
        {
            ReleasePlayerGrab();
            return;
        }

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

    // ============================ 서버 ============================
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
