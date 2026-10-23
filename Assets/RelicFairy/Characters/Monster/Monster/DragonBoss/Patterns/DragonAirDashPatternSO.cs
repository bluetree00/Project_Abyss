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
    [Tooltip("마지막 돌진 뒤 내려앉기 — 하강 · 착지 상태(착지 패턴과 같은 클립 · 10-22 E3 D2)")]
    [SerializeField] private string _landDescendStateName   = "Landing_Descend";
    [SerializeField] private string _landTouchdownStateName = "Landing_Touchdown";

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
    public string LandDescendStateName => _landDescendStateName;
    public string LandTouchdownStateName => _landTouchdownStateName;

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
    private const float FallCrossFade  = 0.2f;   // 낙하 · 회복 클립 전환(0.05초는 자세가 튀었다)
    // 휘는 돌진(10-22 E3 D4) — 셋째 돌진은 경고 BendAt초 뒤 한 번, 플레이어가 간 쪽으로 ±BendMaxAngle°까지 꺾어 예고를 다시 그린다.
    // 꺾은 뒤 MinLead(0.45초) 넘게 굳힌 채 돌진한다(예고 = 판정) — 셋째 경고만 0.6 → 0.7초.
    private const int   BendDashIndex    = 2;
    private const float BendAt           = 0.25f;
    private const float BendMaxAngle     = 15f;
    private const float BendMinAngle     = 2f;    // 이보다 덜 꺾이면 다시 그리지 않는다
    private const float LandDescentSpeed = 8f;    // 마지막 돌진 뒤 내려앉는 속도(착지 패턴과 같다 · 10-22 E3 D2)

    private enum Phase
    {
        Takeoff,
        Warning,
        Dash,
        Crash,
        Fall,
        Recover,
        Land,
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
    private int _touchdownHash;
    private bool _bent;            // 이번 경고에서 이미 꺾었다(휘는 돌진)
    private bool _landTouchdown;   // 내려앉기 — 바닥에 닿았다
    private DragonBossWarningZone _warningZone;

    /// <summary>이번 경고 시간 — 휘는 돌진(셋째)은 꺾은 뒤 MinLead를 지키게 늘린다(10-22 E3 D4).</summary>
    private float WarningSeconds => _dashCount == BendDashIndex
        ? Mathf.Max(Data.WarningDuration, BendAt + PatternGuideHelper.MinLead)
        : Data.WarningDuration;

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
        _takeoffStartPos = ctx.Transform.position;
        _hoverPos = _takeoffStartPos;
        _hoverPos.y = Mathf.Max(ctx.Transform.position.y, ctx.Runtime.SpawnPosition.y + Data.WarningHoverHeight);

        _takeoffHash = Animator.StringToHash(Data.TakeoffStateName);
        _crashHash = Animator.StringToHash(Data.CrashStateName);
        _fallHash = Animator.StringToHash(Data.FallStateName);
        _recoverHash = Animator.StringToHash(Data.RecoverStateName);
        _touchdownHash = Animator.StringToHash(Data.LandTouchdownStateName);

        // [DESIGN GUIDE] DashSlashCooldown 은 DragonBossBlackboard 소속이어야 합니다.
        // 베이스 타입(BossAttackBlackboard) 캐스팅 대신 DragonBossBlackboard 로 캐스팅하세요.
        // 예: if (ctx.Blackboard is DragonBossBlackboard dragonBB) dragonBB.DashSlashCooldown = ...
        if ((ctx.Monster as IBoss)?.Blackboard is BossAttackBlackboard bb)
            bb.DashSlashCooldown = Data.Cooldown;

        _bent = false;
        _dashDirection = GetHorizontalDirectionToPlayer(ctx, WarningSeconds + DashLeadExtra);
        PlayAnim(ctx, Data.WarningHoverStateName, 0.1f);
        SpawnWarningMarker(ctx, WarningSeconds);
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
            case Phase.Land:
                UpdateLand(ctx);
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

        if (!_bent && _dashCount == BendDashIndex && _phaseTimer >= BendAt)
            Bend(ctx);

        if (_phaseTimer < WarningSeconds)
            return;

        StartDash(ctx);
    }

    private void UpdateDash(MonsterContext ctx)
    {
        float step = Data.DashSpeed * Time.deltaTime;
        if (TryGetWallHit(ctx, step, out float wallDistance))
        {
            // 벽 앞 여유까지만 나아가 멈춘다 — 옛 hit.point 기준은 구체 캐스트 접점이 옆으로 비껴 한 프레임에 6 m 튀었다(10-06 연출 점검)
            float   advance = Mathf.Clamp(wallDistance - Data.WallStopPadding, 0f, step);
            Vector3 stopPos = ctx.Transform.position + _dashDirection * advance;
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

    private void FinishDash(MonsterContext ctx)
    {
        _dashCount++;
        if (_dashCount < DashCount(ctx))
        {
            _phase = Phase.Warning;
            _phaseTimer = 0f;
            _traveledDistance = 0f;
            _playerHit = false;
            _bent = false;
            _hoverPos = ctx.Transform.position;
            _dashDirection = GetHorizontalDirectionToPlayer(ctx, WarningSeconds + DashLeadExtra);
            PlayAnim(ctx, Data.WarningHoverStateName, 0.12f);
            SpawnWarningMarker(ctx, WarningSeconds);
        }
        else
        {
            StartLand(ctx);   // 마지막 돌진 끝 — 그 자리에 내려앉아 지상전으로(연계: 할퀴기 · 10-22 E3 D2)
        }
    }

    private void StartWarning(MonsterContext ctx)
    {
        _phase = Phase.Warning;
        _phaseTimer = 0f;
        _hoverPos = ctx.Transform.position; // UpdateTakeoff에서 이미 Y 스냅 완료
        _bent = false;
        _dashDirection = GetHorizontalDirectionToPlayer(ctx, WarningSeconds + DashLeadExtra);
        PlayAnim(ctx, Data.WarningHoverStateName, 0.12f);
        SpawnWarningMarker(ctx, WarningSeconds);
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

    /// <summary>
    /// 휘는 돌진 — 지금 겨눔에서 플레이어가 간 쪽(돌진이 닿을 즈음 자리)으로 ±BendMaxAngle°까지 한 번 꺾고 예고를 다시 그린다.
    /// 남은 경고(≥ MinLead) 동안 굳힌다 — 첫 예고를 옆걸음으로 비킨 자리를 꺾어 노린다(10-22 E3 D4).
    /// </summary>
    private void Bend(MonsterContext ctx)
    {
        _bent = true;
        float   remain = Mathf.Max(0f, WarningSeconds - _phaseTimer);
        Vector3 want   = GetHorizontalDirectionToPlayer(ctx, remain + DashLeadExtra);
        float   angle  = Mathf.Clamp(Vector3.SignedAngle(_dashDirection, want, Vector3.up), -BendMaxAngle, BendMaxAngle);
        if (Mathf.Abs(angle) < BendMinAngle) return;

        _dashDirection = Quaternion.Euler(0f, angle, 0f) * _dashDirection;
        DestroyWarningZone();
        SpawnWarningMarker(ctx, remain);
    }

    private void StartLand(MonsterContext ctx)
    {
        _phase = Phase.Land;
        _phaseTimer = 0f;
        _landTouchdown = false;
        PlayAnim(ctx, Data.LandDescendStateName, 0.15f);
    }

    /// <summary>제자리로 내려앉는다(착지 패턴과 같은 클립) → 지상 · 에이전트 복구 → 지상전(러너가 연계 할퀴기를 잇는다 · 10-22 E3 D2).</summary>
    private void UpdateLand(MonsterContext ctx)
    {
        if (!_landTouchdown)
        {
            float   groundY = DragonPatternFloorUtils.GetFloorY(ctx.Transform.position, ctx.Runtime.SpawnPosition.y);
            Vector3 pos     = ctx.Transform.position;
            pos.y = Mathf.MoveTowards(pos.y, groundY, LandDescentSpeed * Time.deltaTime);
            ctx.Transform.position = pos;
            if (pos.y > groundY + 0.1f) return;

            _landTouchdown = true;
            PlayAnim(ctx, Data.LandTouchdownStateName, 0.1f);
            return;
        }

        if (!IsAnimNearEnd(ctx, _touchdownHash))
            return;

        _phase = Phase.Done;
        RestoreAgent(ctx);
        if ((ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard bb)
            bb.BodyState = BodyState.Grounded;
        ReturnToGroundCombat(ctx);
    }

    private void SpawnWarningMarker(MonsterContext ctx, float fillSeconds)
    {
        float groundY = ctx.Runtime.SpawnPosition.y;
        Vector3 origin = new Vector3(_hoverPos.x, groundY, _hoverPos.z);
        // 경고장판이 항상 바닥 끝까지 닿도록 fallback을 룸 최대 크기로 보정 — 드래곤이 경고장판 경로 끝까지 돌진한다
        float maxDistance = Mathf.Max(Data.DashDistance, DragonPatternFloorUtils.GetRoomMaxExtent());
        _effectiveDashDistance = Mathf.Max(2f, DragonPatternFloorUtils.DistanceToFloorEdge(origin, _dashDirection, maxDistance) - WallBodyMargin);
        // 예고는 돌진 거리 + 몸 판정 반경 — 멈춘 자리에서도 판정 캡슐이 반경만큼 앞으로 닿는다(10-03)
        // 출발 순간에도 판정 캡슐이 출발점 뒤로 반경만큼 닿는다 — 예고도 출발점 뒤 반경부터(10-13 검증)
        float guideLength = _effectiveDashDistance + Data.DashHitRadius * 2f;
        Vector3 center = origin + _dashDirection * (guideLength * 0.5f - Data.DashHitRadius);
        // (10-23 가이드 색 통일) 1 · 2페이지 모두 공용 공격 색 — 에셋 색은 알파만 쓴다
        Color lineColor = DragonAbyssFx.WithAlpha(PatternGuideHelper.AttackFill, Data.WarningLineColor.a);
        _warningZone = DragonBossWarningZone.CreateRectangle(
            "DashRangeWarning",
            center,
            Quaternion.LookRotation(_dashDirection, Vector3.up),
            Data.DashHitRadius * 2f,
            guideLength,
            lineColor,
            fillSeconds + 0.5f,
            Data.WarningMarkerHeightOffset);
        _warningZone.BeginFill(fillSeconds);   // 다 차는 순간 돌진
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

    /// <summary>
    /// 벽 = 아레나 경계(바닥 상자 − <see cref="DragonPatternFloorUtils.WallInset"/> = 벽 안면) — 돌진 길이 · 몸 가두기와 같은 기준.
    /// 화룡 아레나(Ch2)의 경계벽(Bound_N/S/E/W)은 Default 레이어 · Wall 레이어는 경계 밖 탑뿐이라 레이어로는 못 가른다.
    /// 예전 구체 캐스트는 마스크가 ~playerLayer(에셋 0 → 모든 레이어)라 장식 · 바닥 아치 · 새끼 용에 걸려 「충돌 → 자해 80」이 났다(10-08 점검).
    /// <paramref name="wallDistance"/> = 몸 판정 구(DashCollisionRadius)가 벽 안면에 닿기까지 남은 거리.
    /// </summary>
    private bool TryGetWallHit(MonsterContext ctx, float stepDistance, out float wallDistance)
    {
        wallDistance = DragonPatternFloorUtils.DistanceToFloorEdge(ctx.Transform.position, _dashDirection, float.MaxValue)
                     - DragonPatternFloorUtils.WallInset - Data.DashCollisionRadius;
        return wallDistance < stepDistance + Data.WallStopPadding;
    }

    private const float DashLeadExtra = 0.25f;   // 돌진이 플레이어 근처에 닿기까지(경고 뒤)

    // 2페이지(심연)는 한 번 더(10-14 화룡 위협)
    private int DashCount(MonsterContext ctx)
        => Data.DashRepeatCount + ((ctx.Monster as DragonBossMonster)?.CurrentElement == DragonBossBlackboard.DragonElement.Abyss ? 1 : 0);

    // 플레이어가 가는 쪽 — 경고가 끝나고 돌진이 닿을 즈음의 자리를 겨눈다(10-14 화룡 위협 · 예전엔 지금 자리라 옆으로 걷기만 해도 비켜났다).
    // 예고 줄은 그 방향에 그려지고 경고 내내 굳어 있다 — 멈추거나 꺾으면 피한다.
    private Vector3 GetHorizontalDirectionToPlayer(MonsterContext ctx, float lead)
    {
        Vector3 dir = ctx.Runtime.PlayerTarget != null
            ? DragonThreat.PredictPlayer(ctx, lead) - ctx.Transform.position
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

        const float TurnCap = 360f;   // 초당 — Slerp(dt×k)만이면 공중 돌진 중 76°/프레임 튐(10-19 실측)
        ctx.Transform.rotation = Quaternion.RotateTowards(ctx.Transform.rotation,
            Quaternion.Slerp(ctx.Transform.rotation, Quaternion.LookRotation(direction), rotationSpeed * Time.deltaTime),
            TurnCap * Time.deltaTime);
    }

    private static void RestoreAgent(MonsterContext ctx)
        => DragonPatternFloorUtils.SnapToFloorAndRestoreAgent(ctx);

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
