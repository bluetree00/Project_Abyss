using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// ForestGuardian 2페이즈 돌입 패턴.
///
/// 조건  : FG_PhaseChangePending (HP ≤ 50% && !IsPhase2), forceExecute = true
/// 흐름  : AttackReady 애니메이션 재생 → 머터리얼·속도 교체(비동기) → duration 경과 → ChaseState
/// 제약  : FullLockState — 경직·이동 불가, 데미지는 수령
/// </summary>
[CreateAssetMenu(menuName = "Abyss/Boss/ForestGuardian/FG_Phase2EntryPattern", fileName = "FG_Phase2EntryPattern")]
public class FGPhase2EntryPatternSO : BossPatternSO
{
    [Header("Phase2Entry — Timing")]
    [Tooltip("AttackReady 애니메이션 유지 시간 (초)")]
    public float duration = 2.0f;

    private FGPhase2EntryState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new FGPhase2EntryState(this);
    public override void OnRecycled()                       => _state = new FGPhase2EntryState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        var fg = ctx.Boss as ForestGuardianMonster;
        return fg != null && fg.HpRatio <= 0.5f && !fg.FGBlackboard.IsPhase2;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// FGPhase2EntryState — FullLock (이동·경직 불가, 데미지 수령)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGPhase2EntryState : FullLockState<FGPhase2EntryPatternSO>
{
    private const string AnimAttackReady = "AttackReady";

    private float _timer;

    public FGPhase2EntryState(FGPhase2EntryPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _timer = 0f;

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.ResetPath();
        }

        // 2페이즈 전환 트리거 — 머터리얼·속도 교체는 비동기로 진행됨
        var fg = ctx.Monster as ForestGuardianMonster;
        fg?.TriggerPhase2();

        PlayAnim(ctx, AnimAttackReady);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        if (_timer >= Data.duration)
            ctx.Monster.ChangeState<ChaseState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"[FGPhase2Entry] Animator state not found: '{stateName}'", ctx.Monster);
            return;
        }
        ctx.Animator.CrossFade(stateName, 0.1f, 0, 0f);
    }
}
}
