/// <summary>
/// 랜슬롯 패시브2 — 배신의 대가(빈틈 해제). 심판 일격 후 빈틈 중 적을 처치하면 빈틈을 즉시 해제한다.
/// 빈틈 진입/지속/이동·받피 페널티는 MadnessStack/LancelotMadnessRelic이 담당.
/// </summary>
public sealed class LancelotBetrayalPassive : CharacterPassiveBase
{
    public override string         PassiveName => "배신의 대가";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnKill;

    public override bool CanApply(PlayerController ctrl, in PassiveContext ctx)
        => ctrl.RelicBehavior is LancelotMadnessRelic lm && lm.Madness != null && lm.Madness.IsFaltering;

    public override void Apply(PlayerController ctrl, in PassiveContext ctx)
    {
        if (ctrl.RelicBehavior is LancelotMadnessRelic lm)
            lm.Madness?.ClearFalter();
    }
}
