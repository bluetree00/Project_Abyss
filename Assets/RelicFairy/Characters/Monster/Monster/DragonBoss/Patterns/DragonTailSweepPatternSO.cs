using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 「꼬리 휩쓸기」 — 지상 전용 등 뒤 응징(10-22 E3 D5). 지상 화룡의 등 뒤가 늘 안전하던 자리를 막는다.
///   발동: 지상에서 플레이어가 등 뒤 <c>_rearAngle</c>° · <c>_rearReach</c> m 안에 <c>_rearStaySeconds</c>초 넘게 머물렀다
///         (머문 시간 = <see cref="DragonBossBlackboard.RearStaySeconds"/> — <see cref="DragonBossMonster"/>가 매 프레임 <see cref="IsPlayerBehind"/>로 센다).
///   흐름: 시작 순간 뒤쪽 방향 고정 → 뒤쪽 반원 예고(<c>_telegraphSeconds</c>초 차오름) → 판정(같은 반원 · 공격력 × <c>_damageMult</c> · 넉백)
///         → 끝 자세 → 복귀. 조건부라 남발하지 않는다.
/// BossConfig: 지상 조건 엔트리 하나에 이것만 넣어 지상 풀들보다 앞에 둔다(조건이 안 맞으면 러너가 다음 엔트리로 넘어간다).
/// </summary>
[CreateAssetMenu(fileName = "DragonTailSweepPattern",
    menuName = "RelicFairy/Boss/Dragon/TailSweepPattern")]
public class DragonTailSweepPatternSO : BossPatternSO
{
    [Header("발동 — 등 뒤 머묾")]
    [Tooltip("등 뒤 부채꼴 전체 각 (도)")]
    [SerializeField] private float _rearAngle       = 120f;
    [Tooltip("등 뒤 거리 (m, 화룡 중심에서)")]
    [SerializeField] private float _rearReach       = 4f;
    [Tooltip("이만큼 머물면 발동 (초)")]
    [SerializeField] private float _rearStaySeconds = 1.5f;
    [SerializeField] private float _cooldown        = 8f;

    [Header("판정 — 뒤쪽 반원")]
    [Tooltip("예고 = 방향 고정 뒤 판정까지 (초) — 0.45 이상")]
    [SerializeField] private float _telegraphSeconds = 0.6f;
    [Tooltip("뒤쪽 반원 반경 (m, 화룡 중심에서)")]
    [SerializeField] private float _radius           = 5f;
    [Tooltip("피해 = 공격력 × 이 값")]
    [SerializeField] private float _damageMult       = 1.2f;
    [SerializeField] private float _knockbackMult    = 1f;
    [Tooltip("판정 뒤 복귀까지 (초)")]
    [SerializeField] private float _endPoseSeconds   = 0.6f;

    [Header("애니메이션")]
    [Tooltip("꼬리 치기 상태(Unka Attack.FBX 「UAttack Tail L」) — Animator에 없으면 아래 대체 상태")]
    [SerializeField] private string _tailStateName     = "UAttack Tail L";
    [SerializeField] private string _fallbackStateName = "ClawAttackL";

    public float  Cooldown          => _cooldown;
    public float  TelegraphSeconds  => Mathf.Max(PatternGuideHelper.MinLead, _telegraphSeconds);
    public float  Radius            => _radius;
    public float  DamageMult        => _damageMult;
    public float  KnockbackMult     => _knockbackMult;
    public float  EndPoseSeconds    => _endPoseSeconds;
    public string TailStateName     => _tailStateName;
    public string FallbackStateName => _fallbackStateName;

    private DragonTailSweepState _runtimeState;

    public override void Initialize(BossPatternContext ctx) => _runtimeState = new DragonTailSweepState(this);

    public override void OnRecycled() => _runtimeState?.Reset();

    public override bool CanExecute(BossPatternContext ctx)
        => ctx.Ctx.Runtime.PlayerTarget != null
           && ctx.Blackboard is DragonBossBlackboard bb
           && bb.BodyState == BodyState.Grounded
           && bb.TailSweepCooldown <= 0f
           && bb.RearStaySeconds >= _rearStaySeconds;

    public override SpecialStateBase GetRuntimeState() => _runtimeState ??= new DragonTailSweepState(this);

    /// <summary>플레이어가 지금 등 뒤 부채꼴(전체 <c>_rearAngle</c>° · <c>_rearReach</c> m) 안인가 — 수평.</summary>
    public bool IsPlayerBehind(MonsterContext ctx)
    {
        var target = ctx?.Runtime?.PlayerTarget;
        if (target == null) return false;
        Vector3 d = target.position - ctx.Transform.position;
        d.y = 0f;
        if (d.sqrMagnitude > _rearReach * _rearReach) return false;
        Vector3 back = -ctx.Transform.forward;
        back.y = 0f;
        return Vector3.Angle(back, d) <= _rearAngle * 0.5f;
    }
}

// ────────────────────────────────────────────────────────────────────────────
// Runtime state
// ────────────────────────────────────────────────────────────────────────────

/// <summary>Telegraph(뒤쪽 방향 고정 · 반원 차오름) → 판정 → EndPose → 복귀. 중단 불가 · 이동 잠금.</summary>
internal sealed class DragonTailSweepState : FullLockState<DragonTailSweepPatternSO>
{
    private const float HalfCircle    = 180f;   // 뒤쪽 반원
    private const float GuideHitFlash = 0.15f;  // 판정 순간 판정 색으로 잠깐 남았다 사라진다(할퀴기와 같다)
    private static Color GuideColor => PatternGuideHelper.AttackFill;   // 할퀴기 · 공중 물기 예고와 같은 색 — 공용 공격 색(10-23 가이드 색 통일)

    private enum Phase { Telegraph, EndPose, Done }

    private Phase      _phase;
    private float      _timer;
    private Vector3    _center;
    private Vector3    _back;
    private GameObject _guide;

    internal DragonTailSweepState(DragonTailSweepPatternSO data) : base(data) { }

    internal void Reset()
    {
        _phase = Phase.Done;
        PatternGuideHelper.SafeDestroy(ref _guide);
    }

    public override void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.enabled && ctx.Agent.isOnNavMesh) ctx.Agent.isStopped = true;
        _phase  = Phase.Telegraph;
        _timer  = 0f;
        _center = DragonAbyssFx.OnFloor(ctx, ctx.Transform.position);
        _back   = DragonAbyssFx.FlatDir(Vector3.zero, -ctx.Transform.forward, Vector3.back);   // 시작 순간 고정 — 예고 = 판정

        var bb = DragonAbyssFx.Blackboard(ctx);
        if (bb != null)
        {
            bb.TailSweepCooldown = Data.Cooldown;
            bb.RearStaySeconds   = 0f;
        }

        PatternGuideHelper.SafeDestroy(ref _guide);
        _guide = PatternGuideHelper.Prepare(
            PatternGuideHelper.Sector(_center, Data.Radius, HalfCircle, DragonAbyssFx.Yaw(_back), GuideColor), GuideColor);
        PlayTailAnim(ctx);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        switch (_phase)
        {
            case Phase.Telegraph:
                PatternGuideHelper.SetProgress(_guide, _timer / Data.TelegraphSeconds);
                if (_timer >= Data.TelegraphSeconds) Hit(ctx);
                break;
            case Phase.EndPose:
                if (_guide != null && _timer >= GuideHitFlash) PatternGuideHelper.SafeDestroy(ref _guide);
                if (_timer >= Data.EndPoseSeconds) Finish(ctx);
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        PatternGuideHelper.SafeDestroy(ref _guide);
        _phase = Phase.Done;
    }

    private void Hit(MonsterContext ctx)
    {
        PatternGuideHelper.Arm(_guide);
        var player = DragonAbyssFx.Player(ctx);
        if (player != null && InHalfCircle(player.transform.position))
            DragonAbyssFx.HitPlayer(ctx, Data.DamageMult, HitWeight.Heavy,
                                    knockbackMult: Data.KnockbackMult, knockFrom: _center);   // 꼬리 — 강 · 넉백
        _phase = Phase.EndPose;
        _timer = 0f;
    }

    /// <summary>판정 = 예고 반원(뒤쪽 · 반경 Radius) — 수평.</summary>
    private bool InHalfCircle(Vector3 p)
    {
        Vector3 d = p - _center;
        d.y = 0f;
        return d.sqrMagnitude <= Data.Radius * Data.Radius && Vector3.Dot(d, _back) >= 0f;
    }

    // 꼬리 치기 클립 — Animator에 상태가 없으면 대체 상태(지상 공격)
    private void PlayTailAnim(MonsterContext ctx)
    {
        bool hasTail = ctx.Animator != null && ctx.Animator.HasState(0, Animator.StringToHash(Data.TailStateName));
        DragonAbyssFx.PlayAnim(ctx, hasTail ? Data.TailStateName : Data.FallbackStateName, 0.1f);
    }

    private void Finish(MonsterContext ctx)
    {
        _phase = Phase.Done;
        DragonAbyssFx.ReturnToCombat(ctx);
    }
}
}
