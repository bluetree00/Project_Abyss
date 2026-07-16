using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
[CreateAssetMenu(fileName = "DragonAirBreathPattern",
    menuName = "RelicFairy/Boss/Dragon/AirBreathPattern")]
public class DragonAirBreathPatternSO : BossPatternSO
{
    [Header("Pattern")]
    [SerializeField] private int _biteCount = 3;
    [SerializeField] private float _cooldown = 14f;

    [Header("Flight")]
    [SerializeField] private float _takeoffDuration = 0.45f;
    [SerializeField] private float _takeoffHeight = 2.8f;
    [SerializeField] private float _hoverHeight = 7f;
    [SerializeField] private float _approachSpeed = 22f;
    [SerializeField] private float _approachDuration = 0.75f;
    [SerializeField] private float _maxApproachDuration = 2.1f;
    [SerializeField] private float _recoveryDuration = 0.35f;
    [SerializeField] private float _airTurnAngleThreshold = 40f;
    [SerializeField] private float _airChaseRotationSpeed = 7f;

    [Header("Pre-Dash (거리 초과 시 접근 돌진)")]
    [SerializeField] private float _preDashDistanceThreshold = 14f;
    [SerializeField] private float _preDashSpeed = 26f;
    [SerializeField] private float _preDashMaxDuration = 0.9f;
    [SerializeField] private float _airTurnMoveMultiplier = 0.55f;
    [SerializeField] private float _cameraViewportY = 0.78f;
    [SerializeField] private float _cameraSideOffset = 1.5f;
    [SerializeField] private float _minAnchorDistance = 3.5f;
    [SerializeField] private float _maxAnchorDistance = 6.5f;
    [SerializeField] private float _fallbackForwardOffset = 4.5f;

    [Header("Breath Fire")]
    [SerializeField] private float _biteDuration = 1.05f;
    [SerializeField] private float _warningTime = 0.22f;
    [SerializeField] private float _hitTime = 0.52f;
    [SerializeField] private float _biteAttackHeight = 6f;
    [SerializeField] private float _biteTriggerDistance = 3.1f;
    [SerializeField] private float _attackRadius = 2.8f;
    [SerializeField] private float _attackCenterForwardOffset = 1.4f;
    [SerializeField] private int _attackDamage = 24;

    [Header("Fireball")]
    [SerializeField] private GameObject _fireballPrefab;
    [SerializeField] private float _fireballScale = 1.5f;
    [SerializeField] private GameObject _explosionPrefab;
    [SerializeField] private float _explosionScale = 1f;

    [Header("Warning Marker")]
    [SerializeField] private GameObject _warningMarkerPrefab;
    [SerializeField] private float _warningMarkerScale = 2.4f;
    [SerializeField] private float _warningMarkerLifetime = 0.75f;
    [SerializeField] private float _warningMarkerHeightOffset = 0.12f;

    [Header("EndPose (반격 창)")]
    [SerializeField] private float _endPoseDuration = 0.4f;

    [Header("사운드")]
    [Tooltip("발사할 때마다(3회 각각) 재생할 사운드 클립")]
    [SerializeField] private AudioClip _fireRainSfx;

    [Header("Animator State Names")]
    [SerializeField] private string _takeoffStateName = "Takeoff";
    [SerializeField] private string _preDashStateName = "AirDashForward";
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
    public GameObject FireballPrefab => _fireballPrefab;
    public float FireballScale => _fireballScale;
    public GameObject ExplosionPrefab => _explosionPrefab;
    public float ExplosionScale => _explosionScale;
    public GameObject WarningMarkerPrefab => _warningMarkerPrefab;
    public float WarningMarkerScale => _warningMarkerScale;
    public float WarningMarkerLifetime => _warningMarkerLifetime;
    public float WarningMarkerHeightOffset => _warningMarkerHeightOffset;
    public float PreDashDistanceThreshold => _preDashDistanceThreshold;
    public float PreDashSpeed => _preDashSpeed;
    public float PreDashMaxDuration => _preDashMaxDuration;
    public string TakeoffStateName => _takeoffStateName;
    public string PreDashStateName => _preDashStateName;
    public string AirChaseStateName => _airChaseStateName;
    public string AirChaseLeftStateName => _airChaseLeftStateName;
    public string AirChaseRightStateName => _airChaseRightStateName;
    public string BiteStateName => _biteStateName;
    public string LandingStateName  => _landingStateName;
    public float  EndPoseDuration   => _endPoseDuration;
    public AudioClip FireRainSfx    => _fireRainSfx;

    private DragonAirBreathState _runtimeState;

    public override void Initialize(BossPatternContext ctx)
        => _runtimeState = new DragonAirBreathState(this);

    public override void OnRecycled()
        => _runtimeState?.Reset();

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        if (ctx.Blackboard is not DragonBossBlackboard bb) return false;
        return bb.BodyState == BodyState.Airborne
               && bb.AirBiteCooldown <= 0f;
    }

    public override SpecialStateBase GetRuntimeState() => _runtimeState;
}

internal sealed class DragonAirBreathState : FullLockState<DragonAirBreathPatternSO>
{
    private enum Phase
    {
        PreDash,
        Takeoff,
        Approach,
        Bite,
        Recovery,
        EndPose,
        Landing,
        Done,
    }

    private const float AnchorReachedDistance = 0.35f;

    private Phase _phase;
    private float _phaseTimer;
    private int _completedBites;
    private bool _warningShown;
    private bool _damageApplied;
    private DragonBossWarningZone _activeWarningZone;
    private Vector3 _hoverAnchorPos;
    private Vector3 _biteTargetGroundPos;
    private Vector3 _biteAttackPos;
    private float _lockedAttackY;
    private bool _hasStartedBiteSequence;
    private int _takeoffHash;
    private int _landingHash;
    private string _currentAirChaseAnim;
    private GameObject _fireballGo;
    private Vector3 _fireballStartPos;

    internal DragonAirBreathState(DragonAirBreathPatternSO data) : base(data) { }

    internal void Reset()
    {
        _phase = Phase.Done;
        _phaseTimer = 0f;
        _completedBites = 0;
        _hasStartedBiteSequence = false;
        _currentAirChaseAnim = null;
        _activeWarningZone = null;
        ReleaseFireball();
    }

    public override void Enter(MonsterContext ctx)
    {
        GameCameraController.Instance?.DeactivateDragonTopDownView(0.8f);
        _phaseTimer = 0f;
        _completedBites = 0;
        _warningShown = false;
        _damageApplied = false;
        _hoverAnchorPos = BuildAirAnchor(ctx, 0);
        _lockedAttackY = Mathf.Max(
            ctx.Transform.position.y,
            ctx.Runtime.SpawnPosition.y + Data.BiteAttackHeight);
        _hasStartedBiteSequence = false;

        if ((ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard bb)
            bb.AirBiteCooldown = Data.Cooldown;

        if (GetHorizontalDistanceToPlayer(ctx) > Data.PreDashDistanceThreshold)
            StartPreDash(ctx);
        else
        {
            _phase = Phase.Approach;
            UpdateAirChaseAnimation(ctx, _hoverAnchorPos, force: true);
        }
    }

    public override void Update(MonsterContext ctx)
    {
        _phaseTimer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.PreDash:
                UpdatePreDash(ctx);
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
            case Phase.EndPose:
                UpdateEndPose(ctx);
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        ReleaseFireball();
    }

    private void StartPreDash(MonsterContext ctx)
    {
        _phase = Phase.PreDash;
        _phaseTimer = 0f;
        PlayAnim(ctx, Data.PreDashStateName, 0.08f);
    }

    private void UpdatePreDash(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null)
        {
            StartApproach(ctx);
            return;
        }

        Vector3 playerPos = ctx.Runtime.PlayerTarget.position;
        Vector3 target = new Vector3(playerPos.x, ctx.Transform.position.y, playerPos.z);
        ctx.Transform.position = Vector3.MoveTowards(
            ctx.Transform.position,
            target,
            Data.PreDashSpeed * Time.deltaTime);
        FaceTarget(ctx, playerPos, Data.AirChaseRotationSpeed);

        bool closeEnough = GetHorizontalDistanceToPlayer(ctx) <= Data.MaxAnchorDistance;
        if (closeEnough || _phaseTimer >= Data.PreDashMaxDuration)
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

        // 파이어볼을 드래곤 위치에서 경고 장판 center로 이동
        if (_fireballGo != null && !_damageApplied)
        {
            float t = Mathf.Clamp01(_phaseTimer / Mathf.Max(0.01f, Data.HitTime));
            Vector3 fireballTarget = _biteTargetGroundPos + Vector3.up * Data.WarningMarkerHeightOffset;
            _fireballGo.transform.position = Vector3.Lerp(_fireballStartPos, fireballTarget, t);
        }

        if (!_warningShown && _phaseTimer >= Data.WarningTime)
        {
            _warningShown = true;
            SpawnWarningMarker(ctx);
        }

        if (!_damageApplied && _phaseTimer >= Data.HitTime)
        {
            _damageApplied = true;
            _activeWarningZone?.TransitionToHitPhase(0.35f);
            _activeWarningZone = null;
            ApplyFireballImpact(ctx);
        }

        if (_phaseTimer < duration)
            return;

        _completedBites++;
        if (_completedBites >= Mathf.Max(1, Data.BiteCount))
        {
            StartEndPose(ctx);
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
        ReleaseFireball();

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

        // 드래곤 입 위치에서 파이어볼 스폰
        _fireballStartPos = _biteAttackPos;
        if (Data.FireballPrefab != null)
        {
            _fireballGo = BossEffectPool.Spawn(Data.FireballPrefab, _fireballStartPos, Quaternion.identity);
            if (_fireballGo != null)
                _fireballGo.transform.localScale = Vector3.one * Data.FireballScale;
        }

        Managers.Sound?.PlayEffectAt(Data.FireRainSfx, ctx.Transform.position);
    }

    private void StartEndPose(MonsterContext ctx)
    {
        _phase = Phase.EndPose;
        _phaseTimer = 0f;
        _currentAirChaseAnim = null;
    }

    private void UpdateEndPose(MonsterContext ctx)
    {
        if (_phaseTimer >= Data.EndPoseDuration)
            ReturnToAirCombat(ctx);
    }

    private void StartRecovery(MonsterContext ctx)
    {
        _phase = Phase.Recovery;
        _phaseTimer = 0f;
        _currentAirChaseAnim = null;
        UpdateAirChaseAnimation(ctx, BuildAttackAnchor(ctx, _completedBites), force: true);
    }

    private void SpawnWarningMarker(MonsterContext ctx)
    {
        Vector3 pos = _biteTargetGroundPos;
        pos.y = ctx.Runtime.SpawnPosition.y;
        _activeWarningZone = DragonBossWarningZone.CreateCircle(
            "DragonAirBreathWarning",
            pos,
            Data.AttackRadius,
            new Color(1f, 0.28f, 0.18f, 0.85f),
            Data.WarningMarkerLifetime,
            Data.WarningMarkerHeightOffset);
    }

    private void ApplyFireballImpact(MonsterContext ctx)
    {
        if (Data.ExplosionPrefab != null)
        {
            var expGo = BossEffectPool.SpawnOneShot(
                Data.ExplosionPrefab,
                _biteTargetGroundPos,
                Quaternion.identity,
                fallbackLifetime: 3f);
            if (expGo != null)
                expGo.transform.localScale = Vector3.one * Data.ExplosionScale;
        }

        ReleaseFireball();

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

    private void ReleaseFireball()
    {
        if (_fireballGo == null) return;
        foreach (var ps in _fireballGo.GetComponentsInChildren<ParticleSystem>(true))
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        BossEffectPool.Release(_fireballGo);
        _fireballGo = null;
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
        => DragonPatternFloorUtils.SnapToFloorAndRestoreAgent(ctx);

    private static void ReturnToAirCombat(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        ctx.Monster.ChangeState<AttackReadyState>();
    }
}
}
