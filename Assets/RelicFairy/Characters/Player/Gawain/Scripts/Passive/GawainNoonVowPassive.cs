/// <summary>
/// 가웨인 패시브 1 — 정오의 서약
/// 태양 타이머가 강화 구간일 때 공격력 보너스 적용.
///
/// SolarTimer가 PlayerRuntimeStats에 직접 배율을 적용하므로,
/// 이 패시브는 HUD/디버그용 설명 역할만 한다.
/// 실제 배율은 SolarTimer.EnterPhase()에서 SetCharacterAttackMultiplier로 처리됨.
/// </summary>
public class GawainNoonVowPassive : CharacterPassiveBase
{
    public override string         PassiveName => "정오의 서약";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnAttackHit;

    public override bool CanApply(PlayerController ctrl, in PassiveContext ctx) => false;

    public override void Apply(PlayerController ctrl, in PassiveContext ctx) { }
}
