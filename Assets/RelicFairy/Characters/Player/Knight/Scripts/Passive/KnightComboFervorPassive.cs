/// <summary>
/// [Knight 패시브] 전투 열기 — 콤보를 완료하면 E스킬 쿨다운을 초기화한다.
///
/// 조건: comboStep >= requiredComboStep (기본 2타, 즉 3타 이상 콤보)
/// 효과: SkillType.E 쿨다운 즉시 초기화
/// </summary>
public class KnightComboFervorPassive : CharacterPassiveBase
{
    private readonly int _requiredComboStep;

    public KnightComboFervorPassive(int requiredComboStep = 2) => _requiredComboStep = requiredComboStep;

    public override string         PassiveName => "전투 열기";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnComboFinish;

    public override bool CanApply(PlayerController ctrl, in PassiveContext ctx)
        => ctx.comboStep >= _requiredComboStep;

    public override void Apply(PlayerController ctrl, in PassiveContext ctx)
        => ctrl.CooldownTracker.ResetCooldown(SkillType.E);
}
