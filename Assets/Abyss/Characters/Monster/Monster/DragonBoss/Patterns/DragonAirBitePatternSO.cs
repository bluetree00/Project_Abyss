using UnityEngine;

namespace Abyss.Monster
{
[CreateAssetMenu(fileName = "DragonAirBitePattern",
    menuName = "Abyss/Boss/Dragon/AirBitePattern")]
public class DragonAirBitePatternSO : BossPatternSO
{
    [Header("Pattern")]
    [SerializeField] private int _biteCount = 3;
    [SerializeField] private float _cooldown = 14f;

    [Header("Flight")]
    [SerializeField] private float _takeoffDuration = 0.45f;
    [SerializeField] private float _takeoffHeight = 2.8f;
    [SerializeField] private float _hoverHeight = 4f;
    [SerializeField] private float _approachSpeed = 8f;
    [SerializeField] private float _approachDuration = 0.75f;
    [SerializeField] private float _maxApproachDuration = 2.1f;
    [SerializeField] private float _recoveryDuration = 0.35f;
    [SerializeField] private float _airTurnAngleThreshold = 40f;
    [SerializeField] private float _airChaseRotationSpeed = 4.5f;
    [SerializeField] private float _airTurnMoveMultiplier = 0.55f;
    [SerializeField] private float _cameraViewportY = 0.78f;
    [SerializeField] private float _cameraSideOffset = 1.5f;
    [SerializeField] private float _minAnchorDistance = 3.5f;
    [SerializeField] private float _maxAnchorDistance = 6.5f;
    [SerializeField] private float _fallbackForwardOffset = 4.5f;

    [Header("Bite")]
    [SerializeField] private float _biteDuration = 1.05f;
    [SerializeField] private float _warningTime = 0.22f;
    [SerializeField] private float _hitTime = 0.52f;
    [SerializeField] private float _biteAttackHeight = 1.45f;
    [SerializeField] private float _biteTriggerDistance = 3.1f;
    [SerializeField] private float _attackRadius = 2.8f;
    [SerializeField] private float _attackCenterForwardOffset = 1.4f;
    [SerializeField] private int _attackDamage = 24;

    [Header("Warning Marker")]
    [SerializeField] private GameObject _warningMarkerPrefab;
    [SerializeField] private float _warningMarkerScale = 2.4f;
    [SerializeField] private float _warningMarkerLifetime = 0.75f;
    [SerializeField] private float _warningMarkerHeightOffset = 0.12f;

    [Header("Animator State Names")]
    [SerializeField] private string _takeoffStateName = "Takeoff";
    [SerializeField] private string _airChaseStateName = "AirChase";
    [SerializeField] private string _airChaseLeftStateName = "AirChaseLeft";
    [SerializeField] private string _airChaseRightStateName = "AirChaseRight";
    [SerializeField] private string _biteStateName = "AirBite";
    [SerializeField] private string _landingStateName = "Landing";

    public int BiteCount => _biteCount;
    public float Cooldown => _cooldown;
    public float TakeoffDuration => _takeoffDuration;
    public float TakeoffHeight => _takeoffHeight;
    public float HoverHeight => _hoverHeight;
    public float ApproachSpeed => _approachSpeed;
    public float ApproachDuration => _approachDuration;
    public float MaxApproachDuration => _maxApproachDuration;
    public float RecoveryDuration => _recoveryDuration;
    public float AirTurnAngleThreshold => _airTurnAngleThreshold;
    public float AirChaseRotationSpeed => _airChaseRotationSpeed;
    public float AirTurnMoveMultiplier => _airTurnMoveMultiplier;
    public float CameraViewportY => _cameraViewportY;
    public float CameraSideOffset => _cameraSideOffset;
    public float MinAnchorDistance => _minAnchorDistance;
    public float MaxAnchorDistance => _maxAnchorDistance;
    public float FallbackForwardOffset => _fallbackForwardOffset;
    public float BiteDuration => _biteDuration;
    public float WarningTime => _warningTime;
    public float HitTime => _hitTime;
    public float BiteAttackHeight => _biteAttackHeight;
    public float BiteTriggerDistance => _biteTriggerDistance;
    public float AttackRadius => _attackRadius;
    public float AttackCenterForwardOffset => _attackCenterForwardOffset;
    public int AttackDamage => _attackDamage;
    public GameObject WarningMarkerPrefab => _warningMarkerPrefab;
    public float WarningMarkerScale => _warningMarkerScale;
    public float WarningMarkerLifetime => _warningMarkerLifetime;
    public float WarningMarkerHeightOffset => _warningMarkerHeightOffset;
    public string TakeoffStateName => _takeoffStateName;
    public string AirChaseStateName => _airChaseStateName;
    public string AirChaseLeftStateName => _airChaseLeftStateName;
    public string AirChaseRightStateName => _airChaseRightStateName;
    public string BiteStateName => _biteStateName;
    public string LandingStateName => _landingStateName;

    private DragonAirBiteState _runtimeState;

    public override void Initialize(BossPatternContext ctx)
        => _runtimeState = new DragonAirBiteState(this);

    public override void OnRecycled()
        => _runtimeState?.Reset();

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        if (ctx.Blackboard is not DragonBossBlackboard bb) return false;
        return bb.AirBiteCooldown <= 0f;
    }

    public override SpecialStateBase GetRuntimeState() => _runtimeState;
}

internal sealed class DragonAirBiteState : FullLockState<DragonAirBitePatternSO>
{
    private enum Phase
    {
        Takeoff,
        Approach,
        Bite,
        Recovery,
        Landing,
        Done,
    }

    private const float AnchorReachedDistance = 0.35f;

    private Phase _phase;
    private float _phaseTimer;
    private int _completedBites;
    private bool _warningShown;
    private bool _damageApplied;
    private Vector3 _takeoffStartPos;
    private Vector3 _hoverAnchorPos;
    private Vector3 _biteTargetGroundPos;
    private Vector3 _biteAttackPos;
    private float _lockedAttackY;
    private bool _hasStartedBiteSequence;
    private int _takeoffHash;
    private int _landingHash;
    private string _currentAirChaseAnim;

    internal DragonAirBiteState(DragonAirBitePatternSO data) : base(data) { }

    internal void Reset()
    {
        _phase = Phase.Done;
        _phaseTimer = 0f;
        _completedBites = 0;
        _hasStartedBiteSequence = false;
        _currentAirChaseAnim = null;
    }

    public override void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null)
            ctx.Agent.enabled = false;

        _phase = Phase.Takeoff;
        _phaseTimer = 0f;
        _completedBites = 0;
        _warningShown = false;
        _damageApplied = false;
        _takeoffStartPos = ctx.Transform.position;
        _hoverAnchorPos = BuildAirAnchor(ctx, 0);
        _lockedAttackY = ctx.Runtime.SpawnPosition.y + Data.BiteAttackHeight;
        _hasStartedBiteSequence = false;
        _takeoffHash = Animator.StringToHash(Data.TakeoffStateName);
        _landingHash = Animator.StringToHash(Data.LandingStateName);

        if (ctx.Monster is DragonBossMonster dragon)
            dragon.DragonBlackboard.IsAirborne = true;

        if ((ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard bb)
            bb.AirBiteCooldown = Data.Cooldown;

        PlayAnim(ctx, Data.TakeoffStateName, 0.1f);
    }

    public override void Update(MonsterContext ctx)
    {
        _phaseTimer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Takeoff:
                UpdateTakeoff(ctx);
                break;
            case Phase.Approach:
                UpdateApproach(ctx);
                break;
            case Phase.Bite:
                UpdateBite(ctx);
                break;
            case Phase.Recovery:
                UpdateRecovery(ctx);
                break;
            case Phase.Landing:
                UpdateLanding(ctx);
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        RestoreAgent(ctx);
        if (ctx.Monster is DragonBossMonster dragon)
            dragon.DragonBlackboard.IsAirborne = false;
    }

    private void UpdateTakeoff(MonsterContext ctx)
    {
        FacePlayer(ctx, Data.AirChaseRotationSpeed);

        // Takeoff 클립이 제자리로 변경됨 → 코드에서 Y 상승 처리
        Vector3 p = ctx.Transform.position;
        p.y = Mathf.MoveTowards(p.y, _lockedAttackY, ctx.Stat.moveSpeed * 2f * Time.deltaTime);
        ctx.Transform.position = p;

        if (!IsAnimNearEnd(ctx, _takeoffHash))
            return;

        Vector3 pos = ctx.Transform.position;
        pos.y = _lockedAttackY;
        ctx.Transform.position = pos;

        _hoverAnchorPos = BuildAirAnchor(ctx, _completedBites);
        StartApproach(ctx);
    }

    private void UpdateApproach(MonsterContext ctx)
    {
        _hoverAnchorPos = _hasStartedBiteSequence
            ? BuildAttackAnchor(ctx, _completedBites)
            : BuildAirAnchor(ctx, _completedBites);
        MoveAirChase(ctx, _hoverAnchorPos);

        bool reachedAnchor = Vector3.Distance(ctx.Transform.position, _hoverAnchorPos) <= AnchorReachedDistance;
        bool closeEnoughToBite = GetHorizontalDistanceToPlayer(ctx) <= Data.BiteTriggerDistance;

        if (_phaseTimer < Data.ApproachDuration)
            return;

        if (!closeEnoughToBite && !reachedAnchor && _phaseTimer < Data.MaxApproachDuration)
            return;

        StartBite(ctx);
    }

    private void UpdateBite(MonsterContext ctx)
    {
        float duration = Mathf.Max(0.1f, Data.BiteDuration);
        ctx.Transform.position = _biteAttackPos;
        FacePlayer(ctx, Data.AirChaseRotationSpeed);

        if (!_warningShown && _phaseTimer >= Data.WarningTime)
        {
            _warningShown = true;
            SpawnWarningMarker(ctx);
        }

        if (!_damageApplied && _phaseTimer >= Data.HitTime)
        {
            _damageApplied = true;
            ApplyHit();
        }

        if (_phaseTimer < duration)
            return;

        _completedBites++;
        if (_completedBites >= Mathf.Max(1, Data.BiteCount))
        {
            StartLanding(ctx);
            return;
        }

        StartRecovery(ctx);
    }

    private void UpdateRecovery(MonsterContext ctx)
    {
        _hoverAnchorPos = BuildAttackAnchor(ctx, _completedBites);
        MoveAirChase(ctx, _hoverAnchorPos);

        if (_phaseTimer < Data.RecoveryDuration)
            return;

        StartApproach(ctx);
    }

    private void UpdateLanding(MonsterContext ctx)
    {
        Vector3 pos = ctx.Transform.position;
        pos.y = Mathf.MoveTowards(pos.y, ctx.Runtime.SpawnPosition.y, Data.ApproachSpeed * Time.deltaTime);
        ctx.Transform.position = pos;
        FacePlayer(ctx, Data.AirChaseRotationSpeed);

        if (!IsAnimNearEnd(ctx, _landingHash) || pos.y > ctx.Runtime.SpawnPosition.y + 0.1f)
            return;

        RestoreAgent(ctx);
        ReturnToCombat(ctx);
    }

    private void StartApproach(MonsterContext ctx)
    {
        _phase = Phase.Approach;
        _phaseTimer = 0f;
        _currentAirChaseAnim = null;
        Vector3 initialTarget = _hasStartedBiteSequence
            ? BuildAttackAnchor(ctx, _completedBites)
            : BuildAirAnchor(ctx, _completedBites);
        UpdateAirChaseAnimation(ctx, initialTarget, force: true);
    }

    private void StartBite(MonsterContext ctx)
    {
        _phase = Phase.Bite;
        _phaseTimer = 0f;
        _warningShown = false;
        _damageApplied = false;

        if (!_hasStartedBiteSequence)
        {
            _lockedAttackY = ctx.Runtime.SpawnPosition.y + Data.BiteAttackHeight;
            _hasStartedBiteSequence = true;
        }

        _biteTargetGroundPos = GetGroundTarget(ctx);
        _biteAttackPos = ctx.Transform.position;
        _biteAttackPos.y = _lockedAttackY;
        ctx.Transform.position = _biteAttackPos;
        _currentAirChaseAnim = null;

        PlayAnim(ctx, Data.BiteStateName, 0.08f);
    }

    private void StartRecovery(MonsterContext ctx)
    {
        _phase = Phase.Recovery;
        _phaseTimer = 0f;
        _currentAirChaseAnim = null;
        UpdateAirChaseAnimation(ctx, BuildAttackAnchor(ctx, _completedBites), force: true);
    }

    private void StartLanding(MonsterContext ctx)
    {
        _phase = Phase.Landing;
        _phaseTimer = 0f;
        PlayAnim(ctx, Data.LandingStateName, 0.1f);
    }

    private void SpawnWarningMarker(MonsterContext ctx)
    {
        if (Data.WarningMarkerPrefab == null)
            return;

        Vector3 pos = _biteTargetGroundPos;
        pos.y = ctx.Runtime.SpawnPosition.y + Data.WarningMarkerHeightOffset;

        var marker = BossEffectPool.SpawnOneShot(
            Data.WarningMarkerPrefab,
            pos,
            Quaternion.identity,
            fallbackLifetime: Data.WarningMarkerLifetime);

        if (marker != null)
            marker.transform.localScale = Vector3.one * Data.WarningMarkerScale;
    }

    private void ApplyHit()
    {
        if (Data.effectPrefab != null)
            BossEffectPool.SpawnOneShot(
                Data.effectPrefab,
                _biteTargetGroundPos + Vector3.up * 0.2f,
                Quaternion.identity,
                fallbackLifetime: 1.2f);

        var hits = Physics.OverlapSphere(_biteTargetGroundPos, Data.AttackRadius);
        foreach (var col in hits)
        {
            var player = col.GetComponent<PlayerController>()
                ?? col.GetComponentInParent<PlayerController>();
            if (player == null)
                continue;

            player.TakeDamage(Data.AttackDamage);
            Data.playerStatusEffect?.Apply(player);
            break;
        }
    }

    private Vector3 BuildAirAnchor(MonsterContext ctx, int biteIndex)
    {
        Transform player = ctx.Runtime.PlayerTarget;
        Vector3 playerPos = player != null ? player.position : ctx.Transform.position;
        Camera cam = Camera.main;

        Vector3 anchor = playerPos + Vector3.up * Data.HoverHeight;

        if (cam != null)
        {
            var plane = new Plane(Vector3.up, new Vector3(0f, _lockedAttackY, 0f));
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, Data.CameraViewportY, 0f));
            if (plane.Raycast(ray, out float enter))
                anchor = ray.GetPoint(enter);
            else
                anchor = playerPos + GetCameraForwardFlat(cam.transform) * Data.FallbackForwardOffset + Vector3.up * Data.HoverHeight;

            float sideSign = biteIndex % 2 == 0 ? -1f : 1f;
            anchor += cam.transform.right * (Data.CameraSideOffset * sideSign);
        }
        else
        {
            anchor = playerPos + ctx.Transform.forward * Data.FallbackForwardOffset + Vector3.up * Data.HoverHeight;
        }

        Vector3 horizontal = anchor - playerPos;
        horizontal.y = 0f;
        float distance = horizontal.magnitude;
        if (distance > 0.001f)
        {
            float clamped = Mathf.Clamp(distance, Data.MinAnchorDistance, Data.MaxAnchorDistance);
            horizontal = horizontal.normalized * clamped;
        }
        else
        {
            horizontal = ctx.Transform.forward * Data.MinAnchorDistance;
        }

        anchor = playerPos + horizontal;
        anchor.y = _lockedAttackY;
        return anchor;
    }

    private Vector3 BuildAttackAnchor(MonsterContext ctx, int biteIndex)
    {
        Vector3 anchor = BuildAirAnchor(ctx, biteIndex);
        anchor.y = _lockedAttackY;
        return anchor;
    }

    private static Vector3 GetCameraForwardFlat(Transform cameraTransform)
    {
        Vector3 forward = cameraTransform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            return Vector3.forward;
        return forward.normalized;
    }

    private Vector3 GetGroundTarget(MonsterContext ctx)
    {
        Vector3 origin = ctx.Transform.position;
        origin.y = ctx.Runtime.SpawnPosition.y;

        Vector3 forward = ctx.Transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f && ctx.Runtime.PlayerTarget != null)
        {
            forward = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
            forward.y = 0f;
        }

        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;

        forward.Normalize();

        Vector3 target = origin + forward * Data.AttackCenterForwardOffset;
        target.y = ctx.Runtime.SpawnPosition.y;
        return target;
    }

    private static float GetHorizontalDistanceToPlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null)
            return float.MaxValue;

        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        toPlayer.y = 0f;
        return toPlayer.magnitude;
    }

    private void MoveAirChase(MonsterContext ctx, Vector3 targetPos)
    {
        float moveSpeed = Data.ApproachSpeed;
        string animState = UpdateAirChaseAnimation(ctx, targetPos, force: false);

        if (animState == Data.AirChaseLeftStateName || animState == Data.AirChaseRightStateName)
            moveSpeed *= Data.AirTurnMoveMultiplier;

        ctx.Transform.position = Vector3.MoveTowards(
            ctx.Transform.position,
            targetPos,
            moveSpeed * Time.deltaTime);

        FaceTarget(ctx, targetPos, Data.AirChaseRotationSpeed);
    }

    private string UpdateAirChaseAnimation(MonsterContext ctx, Vector3 targetPos, bool force)
    {
        string next = GetAirChaseAnimationName(ctx, targetPos);
        if (!force && _currentAirChaseAnim == next)
            return next;

        _currentAirChaseAnim = next;
        PlayAnim(ctx, next, 0.12f);
        return next;
    }

    private string GetAirChaseAnimationName(MonsterContext ctx, Vector3 targetPos)
    {
        Vector3 dir = targetPos - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f)
            return Data.AirChaseStateName;

        float angle = Vector3.SignedAngle(ctx.Transform.forward, dir, Vector3.up);
        if (angle <= -Data.AirTurnAngleThreshold)
            return Data.AirChaseRightStateName;
        if (angle >= Data.AirTurnAngleThreshold)
            return Data.AirChaseLeftStateName;
        return Data.AirChaseStateName;
    }

    private static void FacePlayer(MonsterContext ctx, float rotationSpeed)
    {
        if (ctx.Runtime.PlayerTarget == null)
            return;

        FaceTarget(ctx, ctx.Runtime.PlayerTarget.position, rotationSpeed);
    }

    private static void FaceTarget(MonsterContext ctx, Vector3 targetPos, float rotationSpeed)
    {
        Vector3 dir = targetPos - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f)
            return;

        ctx.Transform.rotation = Quaternion.Slerp(
            ctx.Transform.rotation,
            Quaternion.LookRotation(dir),
            rotationSpeed * Time.deltaTime);
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

    private static void RestoreAgent(MonsterContext ctx)
    {
        if (ctx.Agent == null || ctx.Agent.enabled)
            return;

        ctx.Agent.enabled = true;
        ctx.Agent.Warp(ctx.Transform.position);
    }

    private static void ReturnToCombat(MonsterContext ctx)
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
            ctx.Monster.ChangeState<ChaseState>();
    }
}
}
