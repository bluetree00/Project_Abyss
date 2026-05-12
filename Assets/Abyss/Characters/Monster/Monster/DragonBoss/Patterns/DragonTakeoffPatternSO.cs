using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 이륙 전이 패턴 — 지상 → 공중.
///
/// 실행 순서:
///   1. Takeoff_Launch 애니 (준비 동작)
///   2. Launch 애니 종료 시 Takeoff_Rise 애니 루프로 CrossFade
///   3. 이륙 시작 위치 기준 상승 누적량 ≥ _airborneThreshold 도달 시:
///      - bb.IsAirborne = true (P7 에서 BodyState.Airborne 로 전환 예정)
///      - Airborne_Hover 애니로 CrossFade
///      - AttackReadyState 로 복귀 → BossPatternRunner 가 공중 패턴 선택
///
/// FullLock 제약 — 피격 시 GetHit 전환 차단, 데미지는 적용.
/// L4 Executing 구간이므로 상승 도중 다른 패턴 평가는 발생하지 않는다.
///
/// BossConfigSO 연결: Body_Grounded 조건 엔트리의 patterns 에 포함.
/// </summary>
[CreateAssetMenu(fileName = "DragonTakeoffPattern",
    menuName = "Abyss/Boss/Dragon/TakeoffPattern")]
public class DragonTakeoffPatternSO : BossPatternSO
{
    [Header("상승 파라미터")]
    [Tooltip("이륙 시작 위치 기준 상승 누적량 임계값 (m). 이 값 이상 상승하면 Airborne 플립.")]
    [SerializeField] private float _airborneThreshold = 4f;
    [Tooltip("Y 상승 속도 (m/s).")]
    [SerializeField] private float _liftSpeed = 6f;

    [Header("애니메이션 상태 이름")]
    [SerializeField] private string _launchStateName = "Takeoff_Launch";
    [SerializeField] private string _riseStateName   = "Takeoff_Rise";
    [SerializeField] private string _hoverStateName  = "Airborne_Hover";

    public float  AirborneThreshold => _airborneThreshold;
    public float  LiftSpeed         => _liftSpeed;
    public string LaunchStateName   => _launchStateName;
    public string RiseStateName     => _riseStateName;
    public string HoverStateName    => _hoverStateName;

    private DragonTakeoffState _runtimeState;

    public override void Initialize(BossPatternContext ctx)
        => _runtimeState = new DragonTakeoffState(this);

    public override void OnRecycled() => _runtimeState?.Reset();

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Blackboard is not DragonBossBlackboard bb) return false;
        return bb.BodyState == BodyState.Grounded;
    }

    public override SpecialStateBase GetRuntimeState() => _runtimeState;
}

// ────────────────────────────────────────────────────────────────────────────
// Runtime state
// ────────────────────────────────────────────────────────────────────────────

internal sealed class DragonTakeoffState : FullLockState<DragonTakeoffPatternSO>
{
    private float _liftAccum;
    private bool  _inRise;
    private int   _launchHash;

    internal DragonTakeoffState(DragonTakeoffPatternSO data) : base(data) { }

    internal void Reset()
    {
        _liftAccum = 0f;
        _inRise    = false;
    }

    public override void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null) ctx.Agent.enabled = false;

        _liftAccum  = 0f;
        _inRise     = false;
        _launchHash = Animator.StringToHash(Data.LaunchStateName);

        PlayAnim(ctx, Data.LaunchStateName, 0.1f);
    }

    public override void Update(MonsterContext ctx)
    {
        float dy = Data.LiftSpeed * Time.deltaTime;
        Vector3 pos = ctx.Transform.position + Vector3.up * dy;
        ctx.Transform.position = pos;
        _liftAccum += dy;

        // Launch 애니 종료 → Rise 루프로 CrossFade (한 번만)
        if (!_inRise && IsAnimNearEnd(ctx, _launchHash))
        {
            PlayAnim(ctx, Data.RiseStateName, 0.12f);
            _inRise = true;
        }

        // 임계치 도달 → BodyState 플립 + Hover 애니 + 종료
        if (_liftAccum >= Data.AirborneThreshold)
        {
            if ((ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard bb)
                bb.BodyState = BodyState.Airborne;

            PlayAnim(ctx, Data.HoverStateName, 0.2f);
            ctx.Monster.ChangeState<AttackReadyState>();
        }
    }

    public override void Exit(MonsterContext ctx) { }

    private static void PlayAnim(MonsterContext ctx, string stateName, float fadeDuration)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        int hash = Animator.StringToHash(stateName);
        if (!ctx.Animator.HasState(0, hash)) return;
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(stateName, fadeDuration, 0, 0f);
    }

    private static bool IsAnimNearEnd(MonsterContext ctx, int hash)
    {
        if (ctx.Animator == null) return true;
        if (!ctx.Animator.HasState(0, hash)) return true;
        if (ctx.Animator.IsInTransition(0)) return false;
        var info = ctx.Animator.GetCurrentAnimatorStateInfo(0);
        if (info.shortNameHash != hash) return true;
        return info.normalizedTime >= 0.9f;
    }
}
}
