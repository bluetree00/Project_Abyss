using UnityEngine;

namespace RelicFairy.Monster
{
[CreateAssetMenu(fileName = "DragonAirDashPattern",
    menuName = "RelicFairy/Boss/Dragon/AirDashPattern")]
public class DragonAirDashPatternSO : BossPatternSO
{
    [Header("Pattern")]
    [SerializeField] private float _cooldown = 16f;

    [Header("Flight")]
    [SerializeField] private float _takeoffDuration = 0.55f;
    [SerializeField] private float _takeoffHeight = 3f;
    [SerializeField] private float _warningDuration = 0.75f;
    [SerializeField] private float _warningHoverHeight = 3f;
    [SerializeField] private float _rotationSpeed = 3.2f;

    [Header("Dash")]
    [SerializeField] private float _dashSpeed = 18f;
    [SerializeField] private float _dashDistance = 12f;
    [SerializeField] private float _dashCollisionRadius = 1.1f;
    [SerializeField] private float _dashHitRadius = 1.5f;
    [SerializeField] private int _dashDamage = 28;
    [SerializeField] private int _selfCrashDamage = 80;
    [SerializeField] private float _wallStopPadding = 0.3f;

    [Header("Warning Marker")]
    [SerializeField] private GameObject _warningMarkerPrefab;
    [SerializeField] private Vector3 _warningMarkerScale = new Vector3(2.2f, 1f, 8f);
    [SerializeField] private float _warningMarkerHeightOffset = 0.18f;
    [SerializeField] private Color _warningLineColor = new Color(1f, 0.35f, 0.05f, 0.8f);

    [Header("Animator State Names")]
    [SerializeField] private string _takeoffStateName = "Takeoff";
    [SerializeField] private string _warningHoverStateName = "AirChase";
    [SerializeField] private string _dashStateName = "AirDashForward";
    [SerializeField] private string _crashStateName = "AirDashCrash";
    [SerializeField] private string _fallStateName = "AirDashFall";
    [SerializeField] private string _recoverStateName = "AirDashRecover";
    [SerializeField] private string _landingStateName = "Landing";

    public float Cooldown => _cooldown;
    public float TakeoffDuration => _takeoffDuration;
    public float TakeoffHeight => _takeoffHeight;
    public float WarningDuration => _warningDuration;
    public float WarningHoverHeight => _warningHoverHeight;
    public float RotationSpeed => _rotationSpeed;
    public float DashSpeed => _dashSpeed;
    public float DashDistance => _dashDistance;
    public float DashCollisionRadius => _dashCollisionRadius;
    public float DashHitRadius => _dashHitRadius;
    public int DashDamage => _dashDamage;
    public int SelfCrashDamage => _selfCrashDamage;
    public float WallStopPadding => _wallStopPadding;
    public GameObject WarningMarkerPrefab => _warningMarkerPrefab;
    public Vector3 WarningMarkerScale => _warningMarkerScale;
    public float WarningMarkerHeightOffset => _warningMarkerHeightOffset;
    public Color WarningLineColor => _warningLineColor;
    public string TakeoffStateName => _takeoffStateName;
    public string WarningHoverStateName => _warningHoverStateName;
    public string DashStateName => _dashStateName;
    public string CrashStateName => _crashStateName;
    public string FallStateName => _fallStateName;
    public string RecoverStateName => _recoverStateName;
    public string LandingStateName => _landingStateName;

    private DragonAirDashState _runtimeState;

    public override void Initialize(BossPatternContext ctx)
        => _runtimeState = new DragonAirDashState(this);

    public override void OnRecycled()
        => _runtimeState?.Reset();

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        return ctx.Blackboard is DragonBossBlackboard bb
               && bb.BodyState == BodyState.Airborne
               && bb.DashSlashCooldown <= 0f;
    }

    public override SpecialStateBase GetRuntimeState() => _runtimeState;
}

internal sealed class DragonAirDashState : FullLockState<DragonAirDashPatternSO>
{
    private enum Phase
    {
        Takeoff,
        Warning,
        Dash,
        Crash,
        Fall,
        Recover,
        Landing,
        Done,
    }

    private Phase _phase;
    private float _phaseTimer;
    private Vector3 _takeoffStartPos;
    private Vector3 _hoverPos;
    private Vector3 _dashDirection;
    private float _traveledDistance;
    private bool _playerHit;
    private bool _selfDamageApplied;
    private int _takeoffHash;
    private int _crashHash;
    private int _fallHash;
    private int _recoverHash;
    private int _landingHash;
    private DragonBossWarningZone _warningZone;

    internal DragonAirDashState(DragonAirDashPatternSO data) : base(data) { }

    internal void Reset()
    {
        _phase = Phase.Done;
        _phaseTimer = 0f;
        _traveledDistance = 0f;
        _playerHit = false;
        _selfDamageApplied = false;
        DestroyWarningZone();
    }

    public override void Enter(MonsterContext ctx)
    {
        _phase = Phase.Warning;
        _phaseTimer = 0f;
        _traveledDistance = 0f;
        _playerHit = false;
        _selfDamageApplied = false;
        _takeoffStartPos = ctx.Transform.position;
        _hoverPos = _takeoffStartPos;
        _hoverPos.y = Mathf.Max(ctx.Transform.position.y, ctx.Runtime.SpawnPosition.y + Data.WarningHoverHeight);

        _takeoffHash = Animator.StringToHash(Data.TakeoffStateName);
        _crashHash = Animator.StringToHash(Data.CrashStateName);
        _fallHash = Animator.StringToHash(Data.FallStateName);
        _recoverHash = Animator.StringToHash(Data.RecoverStateName);
        _landingHash = Animator.StringToHash(Data.LandingStateName);

        // [DESIGN GUIDE] DashSlashCooldown 은 DragonBossBlackboard 소속이어야 합니다.
        // 베이스 타입(BossAttackBlackboard) 캐스팅 대신 DragonBossBlackboard 로 캐스팅하세요.
        // 예: if (ctx.Blackboard is DragonBossBlackboard dragonBB) dragonBB.DashSlashCooldown = ...
        if ((ctx.Monster as IBoss)?.Blackboard is BossAttackBlackboard bb)
            bb.DashSlashCooldown = Data.Cooldown;

        _dashDirection = GetHorizontalDirectionToPlayer(ctx);
        PlayAnim(ctx, Data.WarningHoverStateName, 0.1f);
        SpawnWarningMarker(ctx);
    }

    public override void Update(MonsterContext ctx)
    {
        _phaseTimer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Takeoff:
                UpdateTakeoff(ctx);
                break;
            case Phase.Warning:
                UpdateWarning(ctx);
                break;
            case Phase.Dash:
                UpdateDash(ctx);
                break;
            case Phase.Crash:
                UpdateCrash(ctx);
                break;
            case Phase.Fall:
                UpdateFall(ctx);
                break;
            case Phase.Recover:
                UpdateRecover(ctx);
                break;
            case Phase.Landing:
                UpdateLanding(ctx);
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        DestroyWarningZone();
    }

    private void UpdateTakeoff(MonsterContext ctx)
    {
        FacePlayer(ctx, Data.RotationSpeed);

        // Takeoff 클립이 제자리로 변경됨 → 코드에서 Y 상승 처리
        float targetY = ctx.Runtime.SpawnPosition.y + Data.WarningHoverHeight;
        Vector3 p = ctx.Transform.position;
        p.y = Mathf.MoveTowards(p.y, targetY, ctx.Stat.moveSpeed * 2f * Time.deltaTime);
        ctx.Transform.position = p;

        if (!IsAnimNearEnd(ctx, _takeoffHash))
            return;

        Vector3 pos = ctx.Transform.position;
        pos.y = targetY;
        ctx.Transform.position = pos;

        StartWarning(ctx);
    }

    private void UpdateWarning(MonsterContext ctx)
    {
        ctx.Transform.position = _hoverPos;
        FaceDirection(ctx, _dashDirection, Data.RotationSpeed);

        if (_phaseTimer < Data.WarningDuration)
            return;

        StartDash(ctx);
    }

    private void UpdateDash(MonsterContext ctx)
    {
        float step = Data.DashSpeed * Time.deltaTime;
        if (TryGetWallHit(ctx, step, out RaycastHit hit))
        {
            Vector3 stopPos = hit.point - _dashDirection * Data.WallStopPadding;
            stopPos.y = ctx.Transform.position.y;
            ctx.Transform.position = stopPos;
            StartCrash(ctx);
            return;
        }

        Vector3 pos = ctx.Transform.position + _dashDirection * step;
        pos.y = _hoverPos.y;
        ctx.Transform.position = pos;
        _traveledDistance += step;

        TryHitPlayer(ctx);

        if (_traveledDistance >= Data.DashDistance)
            ReturnToAirCombat(ctx);
    }

    private void UpdateCrash(MonsterContext ctx)
    {
        if (!IsAnimNearEnd(ctx, _crashHash))
            return;

        StartFall(ctx);
    }

    private void UpdateFall(MonsterContext ctx)
    {
        Vector3 pos = ctx.Transform.position;
        pos.y = Mathf.MoveTowards(pos.y, ctx.Runtime.SpawnPosition.y, Data.DashSpeed * 0.45f * Time.deltaTime);
        ctx.Transform.position = pos;

        if (!IsAnimNearEnd(ctx, _fallHash) || pos.y > ctx.Runtime.SpawnPosition.y + 0.05f)
            return;

        StartRecover(ctx);
    }

    private void UpdateRecover(MonsterContext ctx)
    {
        Vector3 pos = ctx.Transform.position;
        pos.y = ctx.Runtime.SpawnPosition.y;
        ctx.Transform.position = pos;
        FacePlayer(ctx, Data.RotationSpeed);

        if (!IsAnimNearEnd(ctx, _recoverHash))
            return;

        RestoreAgent(ctx);
        if ((ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard bb)
            bb.BodyState = BodyState.Grounded;
        ReturnToGroundCombat(ctx);
    }

    private void UpdateLanding(MonsterContext ctx)
    {
        Vector3 pos = ctx.Transform.position;
        pos.y = Mathf.MoveTowards(pos.y, ctx.Runtime.SpawnPosition.y, Data.DashSpeed * 0.45f * Time.deltaTime);
        ctx.Transform.position = pos;
        FacePlayer(ctx, Data.RotationSpeed);

        if (!IsAnimNearEnd(ctx, _landingHash) || pos.y > ctx.Runtime.SpawnPosition.y + 0.05f)
            return;

        RestoreAgent(ctx);
        ReturnToGroundCombat(ctx);
    }

    private void StartWarning(MonsterContext ctx)
    {
        _phase = Phase.Warning;
        _phaseTimer = 0f;
        _hoverPos = ctx.Transform.position; // UpdateTakeoff에서 이미 Y 스냅 완료
        _dashDirection = GetHorizontalDirectionToPlayer(ctx);
        PlayAnim(ctx, Data.WarningHoverStateName, 0.12f);
        SpawnWarningMarker(ctx);
    }

    private void StartDash(MonsterContext ctx)
    {
        _phase = Phase.Dash;
        _phaseTimer = 0f;
        _traveledDistance = 0f;
        _playerHit = false;
        float dashDuration = Data.DashDistance / Mathf.Max(1f, Data.DashSpeed);
        _warningZone?.TransitionToHitPhase(dashDuration + 0.2f);
        _warningZone = null;
        PlayAnim(ctx, Data.DashStateName, 0.05f);
        FaceDirection(ctx, _dashDirection, 100f);
    }

    private void StartCrash(MonsterContext ctx)
    {
        _phase = Phase.Crash;
        _phaseTimer = 0f;
        PlayAnim(ctx, Data.CrashStateName, 0.05f);

        if (_selfDamageApplied)
            return;

        _selfDamageApplied = true;
        ctx.Monster.TakeDamage(Data.SelfCrashDamage, ctx.Monster.gameObject, 0f);
    }

    private void StartFall(MonsterContext ctx)
    {
        _phase = Phase.Fall;
        _phaseTimer = 0f;
        PlayAnim(ctx, Data.FallStateName, 0.05f);
    }

    private void StartRecover(MonsterContext ctx)
    {
        _phase = Phase.Recover;
        _phaseTimer = 0f;
        Vector3 pos = ctx.Transform.position;
        pos.y = ctx.Runtime.SpawnPosition.y;
        ctx.Transform.position = pos;
        PlayAnim(ctx, Data.RecoverStateName, 0.05f);
    }

    private void StartLanding(MonsterContext ctx)
    {
        _phase = Phase.Landing;
        _phaseTimer = 0f;
        PlayAnim(ctx, Data.LandingStateName, 0.08f);
    }

    private void SpawnWarningMarker(MonsterContext ctx)
    {
        float groundY = ctx.Runtime.SpawnPosition.y;
        Vector3 origin = new Vector3(_hoverPos.x, groundY, _hoverPos.z);
        Vector3 center = origin + _dashDirection * (Data.DashDistance * 0.5f);
        _warningZone = DragonBossWarningZone.CreateRectangle(
            "DashRangeWarning",
            center,
            Quaternion.LookRotation(_dashDirection, Vector3.up),
            Data.DashHitRadius * 2f,
            Data.DashDistance,
            Data.WarningLineColor,
            Data.WarningDuration + 0.5f,
            Data.WarningMarkerHeightOffset);
    }

    private void DestroyWarningZone()
    {
        if (_warningZone == null) return;
        Object.Destroy(_warningZone.gameObject);
        _warningZone = null;
    }

    private void TryHitPlayer(MonsterContext ctx)
    {
        if (_playerHit)
            return;

        var hits = Physics.OverlapSphere(ctx.Transform.position, Data.DashHitRadius);
        foreach (var col in hits)
        {
            var player = col.GetComponent<PlayerController>()
                ?? col.GetComponentInParent<PlayerController>();
            if (player == null)
                continue;

            _playerHit = true;
            player.TakeDamage(Data.DashDamage);
            break;
        }
    }

    private bool TryGetWallHit(MonsterContext ctx, float stepDistance, out RaycastHit bestHit)
    {
        Vector3 origin = ctx.Transform.position + Vector3.up * 0.5f;
        int mask = ctx.Config != null ? ~ctx.Config.playerLayer.value : Physics.DefaultRaycastLayers;
        var hits = Physics.SphereCastAll(origin, Data.DashCollisionRadius, _dashDirection, stepDistance + Data.WallStopPadding, mask, QueryTriggerInteraction.Ignore);

        float bestDistance = float.MaxValue;
        bestHit = default;

        foreach (var hit in hits)
        {
            if (hit.collider == null)
                continue;
            if (hit.collider.transform == ctx.Transform || hit.collider.transform.IsChildOf(ctx.Transform))
                continue;
            if (hit.collider.GetComponent<PlayerController>() != null || hit.collider.GetComponentInParent<PlayerController>() != null)
                continue;

            if (hit.distance >= bestDistance)
                continue;

            bestDistance = hit.distance;
            bestHit = hit;
        }

        return bestDistance < float.MaxValue;
    }

    private static Vector3 GetHorizontalDirectionToPlayer(MonsterContext ctx)
    {
        Vector3 dir = ctx.Runtime.PlayerTarget != null
            ? ctx.Runtime.PlayerTarget.position - ctx.Transform.position
            : ctx.Transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f)
            dir = ctx.Transform.forward;
        dir.y = 0f;
        return dir.normalized;
    }

    private static void PlayAnim(MonsterContext ctx, string stateName, float fadeDuration)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName))
            return;

        int hash = Animator.StringToHash(stateName);
        if (!ctx.Animator.HasState(0, hash))
            return;

        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(stateName, fadeDuration, 0, 0f);
    }

    private static bool IsAnimNearEnd(MonsterContext ctx, int hash)
    {
        if (ctx.Animator == null)
            return true;
        if (!ctx.Animator.HasState(0, hash))
            return true;
        if (ctx.Animator.IsInTransition(0))
            return false;

        var info = ctx.Animator.GetCurrentAnimatorStateInfo(0);
        if (info.shortNameHash != hash)
            return true;

        return info.normalizedTime >= 0.9f;
    }

    private static void FacePlayer(MonsterContext ctx, float rotationSpeed)
    {
        if (ctx.Runtime.PlayerTarget == null)
            return;

        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f)
            return;

        FaceDirection(ctx, dir.normalized, rotationSpeed);
    }

    private static void FaceDirection(MonsterContext ctx, Vector3 direction, float rotationSpeed)
    {
        if (direction.sqrMagnitude < 0.001f)
            return;

        ctx.Transform.rotation = Quaternion.Slerp(
            ctx.Transform.rotation,
            Quaternion.LookRotation(direction),
            rotationSpeed * Time.deltaTime);
    }

    private static void RestoreAgent(MonsterContext ctx)
    {
        if (ctx.Agent == null || ctx.Agent.enabled)
            return;

        ctx.Agent.enabled = true;
        ctx.Agent.Warp(ctx.Transform.position);
    }

    private static void ReturnToAirCombat(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        ctx.Monster.ChangeState<AttackReadyState>();
    }

    private static void ReturnToGroundCombat(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        if (ctx.Monster is not DragonBossMonster dragon)
        {
            ctx.Monster.ChangeState<ChaseState>();
            return;
        }

        if (ctx.Runtime.DistToPlayer > dragon.WalkToRunThreshold)
            ctx.Monster.ChangeState<DragonRunChaseState>();
        else
            ctx.Monster.ChangeState<AttackReadyState>();
    }
}
}
