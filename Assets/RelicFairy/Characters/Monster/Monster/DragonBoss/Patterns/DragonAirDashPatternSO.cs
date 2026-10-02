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
    [SerializeField] private float _dashHitRadius = 2.5f;
    [SerializeField] private int _dashDamage = 28;
    [SerializeField] private float _damageMultiplier = 1.5f;
    [SerializeField] private int _selfCrashDamage = 80;
    [SerializeField] private float _wallStopPadding = 0.3f;
    [Tooltip("돌진 시작 시 재생할 사운드")]
    [SerializeField] private AudioClip _dashSfx;

    [Header("Repeat")]
    [SerializeField] private int _dashRepeatCount = 3;

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
    public float DamageMultiplier => _damageMultiplier;
    public int SelfCrashDamage => _selfCrashDamage;
    public float WallStopPadding => _wallStopPadding;
    public AudioClip DashSfx => _dashSfx;
    public int DashRepeatCount => _dashRepeatCount;
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
    // 벽 안면(바닥 상자 −1 m) + 머리 · 몸 앞 길이 — 돌진이 여기서 끝나 벽에 박히지 않는다(10-03 개선 1-2: 매번 벽 충돌 → 사망 클립 낙하)
    private const float WallBodyMargin = 4.5f;
    private const float YankFraction   = 0.5f;   // 봉인기: 첫 돌진의 이만큼에서 사슬이 끌어내린다(10-03 S2)
    private const float YankStagger    = 1.2f;
    private const float FallCrossFade  = 0.2f;   // 낙하 · 회복 클립 전환(0.05초는 자세가 튀었다)

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
    private float _effectiveDashDistance;
    private bool _playerHit;
    private bool _selfDamageApplied;
    private int _dashCount;
    private int _takeoffHash;
    private int _crashHash;
    private int _fallHash;
    private int _recoverHash;
    private int _landingHash;
    private DragonBossWarningZone _warningZone;
    private bool  _yankPlanned;
    private bool  _yanked;
    private float _yankStagger;
    private float _yankWait;

    internal DragonAirDashState(DragonAirDashPatternSO data) : base(data) { }

    internal void Reset()
    {
        _phase = Phase.Done;
        _phaseTimer = 0f;
        _traveledDistance = 0f;
        _effectiveDashDistance = 0f;
        _playerHit = false;
        _selfDamageApplied = false;
        _dashCount = 0;
        DestroyWarningZone();
    }

    public override void Enter(MonsterContext ctx)
    {
        GameCameraController.Instance?.DeactivateDragonTopDownView(0.8f);
        _phase = Phase.Warning;
        _phaseTimer = 0f;
        _traveledDistance = 0f;
        _playerHit = false;
        _selfDamageApplied = false;
        _dashCount = 0;
        _yankPlanned = false;
        _yanked = false;
        _yankStagger = 0f;
        _yankWait = 0f;
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

        if (_traveledDistance >= _effectiveDashDistance)
            FinishDash(ctx);
    }

    private void UpdateCrash(MonsterContext ctx)
    {
        if (!IsAnimNearEnd(ctx, _crashHash))
            return;

        StartFall(ctx);
    }

    private void UpdateFall(MonsterContext ctx)
    {
        // 착지 지점의 Y는 Spawn Y가 아닌 실제 바닥 높이를 사용 —
        // 그렇지 않으면 NavMeshAgent.Warp이 바닥과 어긋난 위치에서 실패해 착지 후 이동이 안 됨
        float groundY = DragonPatternFloorUtils.GetFloorY(ctx.Transform.position, ctx.Runtime.SpawnPosition.y);
        Vector3 pos = ctx.Transform.position;
        pos.y = Mathf.MoveTowards(pos.y, groundY, Data.DashSpeed * 0.45f * Time.deltaTime);
        ctx.Transform.position = pos;

        if (!IsAnimNearEnd(ctx, _fallHash) || pos.y > groundY + 0.05f)
            return;

        StartRecover(ctx);
    }

    private void UpdateRecover(MonsterContext ctx)
    {
        Vector3 pos = ctx.Transform.position;
        pos.y = DragonPatternFloorUtils.GetFloorY(pos, ctx.Runtime.SpawnPosition.y);
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
        float groundY = DragonPatternFloorUtils.GetFloorY(ctx.Transform.position, ctx.Runtime.SpawnPosition.y);
        Vector3 pos = ctx.Transform.position;
        pos.y = Mathf.MoveTowards(pos.y, groundY, Data.DashSpeed * 0.45f * Time.deltaTime);
        ctx.Transform.position = pos;
        FacePlayer(ctx, Data.RotationSpeed);

        if (!IsAnimNearEnd(ctx, _landingHash) || pos.y > groundY + 0.05f)
            return;

        // 사슬에 끌려 내려왔으면 그 자리에서 휘청
        if (_yankWait < _yankStagger)
        {
            _yankWait += Time.deltaTime;
            return;
        }

        RestoreAgent(ctx);
        if ((ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard bb)
            bb.BodyState = BodyState.Grounded;
        ReturnToGroundCombat(ctx);
    }

    private void FinishDash(MonsterContext ctx)
    {
        // 봉인기 — 옛 봉인 사슬이 날개를 끌어내린다: 착지 클립으로 내려와 휘청(10-03 S2)
        if (_yankPlanned && !_yanked)
        {
            _yanked      = true;
            _yankStagger = BossBinding.Of(ctx.Monster)?.Yank(YankStagger) ?? 0f;
            StartLanding(ctx);
            return;
        }

        _dashCount++;
        if (_dashCount < Data.DashRepeatCount)
        {
            _phase = Phase.Warning;
            _phaseTimer = 0f;
            _traveledDistance = 0f;
            _playerHit = false;
            _hoverPos = ctx.Transform.position;
            _dashDirection = GetHorizontalDirectionToPlayer(ctx);
            PlayAnim(ctx, Data.WarningHoverStateName, 0.12f);
            SpawnWarningMarker(ctx);
        }
        else
        {
            ReturnToAirCombat(ctx);
        }
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
        float dashDuration = _effectiveDashDistance / Mathf.Max(1f, Data.DashSpeed);
        _warningZone?.TransitionToHitPhase(dashDuration + 0.2f);
        _warningZone = null;
        PlayAnim(ctx, Data.DashStateName, 0.05f);
        FaceDirection(ctx, _dashDirection, 100f);
        Managers.Sound?.PlayEffectAt(Data.DashSfx, ctx.Transform.position);
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
        PlayAnim(ctx, Data.FallStateName, FallCrossFade);
    }

    private void StartRecover(MonsterContext ctx)
    {
        _phase = Phase.Recover;
        _phaseTimer = 0f;
        Vector3 pos = ctx.Transform.position;
        pos.y = DragonPatternFloorUtils.GetFloorY(pos, ctx.Runtime.SpawnPosition.y);
        ctx.Transform.position = pos;
        PlayAnim(ctx, Data.RecoverStateName, FallCrossFade);
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
        // 경고장판이 항상 바닥 끝까지 닿도록 fallback을 룸 최대 크기로 보정 — 드래곤이 경고장판 경로 끝까지 돌진한다
        float maxDistance = Mathf.Max(Data.DashDistance, DragonPatternFloorUtils.GetRoomMaxExtent());
        _effectiveDashDistance = Mathf.Max(2f, DragonPatternFloorUtils.DistanceToFloorEdge(origin, _dashDirection, maxDistance) - WallBodyMargin);
        // 봉인기 첫 돌진 — 사슬이 닿는 데까지만(예고도 이 길이)
        if (_dashCount == 0 && !StoryProgress.IsLiberated && BossBinding.Of(ctx.Monster)?.IsBound == true)
        {
            _yankPlanned = true;
            _effectiveDashDistance *= YankFraction;
        }
        // 예고는 돌진 거리 + 몸 판정 반경 — 멈춘 자리에서도 판정 캡슐이 반경만큼 앞으로 닿는다(10-03)
        float guideLength = _effectiveDashDistance + Data.DashHitRadius;
        Vector3 center = origin + _dashDirection * (guideLength * 0.5f);
        // 2페이지(심연)엔 검은 불 색으로 — 알파는 에셋 값 유지
        Color lineColor = Data.WarningLineColor;
        if (ctx.Monster is DragonBossMonster dragon && dragon.IsAbyssPage)
        {
            Color abyss = DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Abyss);
            lineColor = new Color(abyss.r, abyss.g, abyss.b, lineColor.a);
        }
        _warningZone = DragonBossWarningZone.CreateRectangle(
            "DashRangeWarning",
            center,
            Quaternion.LookRotation(_dashDirection, Vector3.up),
            Data.DashHitRadius * 2f,
            guideLength,
            lineColor,
            Data.WarningDuration + 0.5f,
            Data.WarningMarkerHeightOffset);
        _warningZone.BeginFill(Data.WarningDuration);   // 다 차는 순간 돌진
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

        // 드래곤이 공중(hoverHeight)에서 돌진하므로 OverlapSphere는 지상 플레이어에 닿지 않음.
        // OverlapCapsule로 바닥~드래곤 위치 전체 구간을 커버한다.
        Vector3 dashPos   = ctx.Transform.position;
        Vector3 groundPos = new Vector3(dashPos.x, ctx.Runtime.SpawnPosition.y, dashPos.z);
        var hits = Physics.OverlapCapsule(groundPos, dashPos, Data.DashHitRadius);
        foreach (var col in hits)
        {
            var player = col.GetComponent<PlayerController>()
                ?? col.GetComponentInParent<PlayerController>();
            if (player == null)
                continue;

            _playerHit = true;
            player.TakeDamage(Mathf.RoundToInt(ctx.Config.stat.attackPower * Data.DamageMultiplier), ctx.Monster.gameObject,
                              false, HitWeight.Heavy);   // 돌진 충돌 — 강
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
