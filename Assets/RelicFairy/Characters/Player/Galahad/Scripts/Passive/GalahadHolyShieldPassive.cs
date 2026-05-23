/// <summary>
/// 갈라하드 패시브 2 — 성스러운 방패
/// 방어 입력 성공 시 받은 피해의 30%를 전방 적에게 반사.
///
/// [STUB] 블록 시스템 구현 전까지 비활성.
/// 블록 시스템 추가 시 PassiveTrigger.OnBlockSuccess 트리거에 연결.
/// </summary>
public class GalahadHolyShieldPassive : CharacterPassiveBase
{
    public override string         PassiveName => "성스러운 방패";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnTakeDamage;

    public override bool CanApply(PlayerController ctrl, in PassiveContext ctx) => false; // STUB

    public override void Apply(PlayerController ctrl, in PassiveContext ctx) { }
}
