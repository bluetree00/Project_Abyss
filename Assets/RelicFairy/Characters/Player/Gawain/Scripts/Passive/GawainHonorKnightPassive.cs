/// <summary>
/// 가웨인 패시브 2 — 명예의 기사
/// HP 50% 초과 시 방어력 +25%, 피해감소 10%.
///
/// 실제 방어 배율 계산은 Gawain.RefreshDefenseBonus()에서 SolarTimer와 합산.
/// 이 클래스는 식별자 역할 (PassiveName/Trigger 노출).
/// </summary>
public class GawainHonorKnightPassive : CharacterPassiveBase
{
    public override string         PassiveName => "명예의 기사";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnTakeDamage;

    public override bool CanApply(PlayerController ctrl, in PassiveContext ctx) => false;

    public override void Apply(PlayerController ctrl, in PassiveContext ctx) { }
}
