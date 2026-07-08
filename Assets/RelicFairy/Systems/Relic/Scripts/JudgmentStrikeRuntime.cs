using UnityEngine;

/// <summary>
/// 랜슬롯 고유 스킬 — 심판의 일격. 광란(Frenzy) 상태 전용·광란당 1회(LancelotMadnessRelic 게이팅).
/// 전방 직선 관통(±45°, 반경6) ATK×(base+stack×per) + 심판 낙인. 리워크로 수동 발동.
/// 실제 판정은 LancelotMadnessRelic.PerformJudgmentStrike가 담당(스택/낙인 슬롯 소유).
/// </summary>
public sealed class JudgmentStrikeRuntime : ISkillRuntime
{
    private const string AnimName     = "QSkill_01";
    private const float  AnimDuration = 0.7f;
    private const float  HitTime      = 0.25f;

    private readonly LancelotMadnessRelic _relic;
    private float _elapsed;
    private bool  _hitDone;

    public JudgmentStrikeRuntime(LancelotMadnessRelic relic) { _relic = relic; }

    public void OnEnter(SkillExecutionContext ctx)
    {
        _elapsed = 0f; _hitDone = false;
        ctx.RotateToMouse();
        ctx.SetMoveScale(0f);
        ctx.Animator?.CrossFade(AnimName, 0.1f);
    }

    public void OnUpdate(SkillExecutionContext ctx)
    {
        _elapsed += Time.deltaTime;
        if (!_hitDone && _elapsed >= HitTime) { _hitDone = true; _relic?.PerformJudgmentStrike(ctx.PlayerTransform); }
        if (_elapsed >= AnimDuration) ctx.RequestEnd?.Invoke();
    }

    public void OnExit(SkillExecutionContext ctx) => ctx.SetMoveScale(1f);
}
