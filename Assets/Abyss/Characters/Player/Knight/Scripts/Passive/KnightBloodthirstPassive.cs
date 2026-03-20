/// <summary>
/// [Knight 패시브] 흡혈 — 적 처치 시 HP를 회복한다.
///
/// 조건: 대상이 IKillable을 구현하고 IsDead == true (OnKill 트리거)
/// 효과: healAmount만큼 즉시 회복
///
/// 몬스터가 IKillable을 구현하지 않으면 발동되지 않는다.
/// </summary>
public class KnightBloodthirstPassive : CharacterPassiveBase
{
    private readonly int _healAmount;

    public KnightBloodthirstPassive(int healAmount = 15) => _healAmount = healAmount;

    public override string         PassiveName => "흡혈";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnKill;

    public override void Apply(PlayerController ctrl, in PassiveContext ctx)
        => ctrl.Heal(_healAmount);
}
