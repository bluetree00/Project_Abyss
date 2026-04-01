using UnityEngine;

/// <summary>
/// 기본 캐릭터 특성1 (스텁):
/// 1회 5챕터 보스 클리어 시 카드 액티브 아이템을 게임에 장착하고 시작한다.
/// → 액티브 아이템 시스템 구현 전까지 동작하지 않음.
/// </summary>
public class DefaultCardItemPassive : CharacterPassiveBase
{
    public override string         PassiveName => "카드 아이템";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnAttackHit; // 트리거 미사용 (스텁)

    public override bool CanApply(PlayerController ctrl, in PassiveContext ctx) => false;

    public override void Apply(PlayerController ctrl, in PassiveContext ctx)
    {
        // 액티브 아이템 시스템 구현 시 활성화
    }
}
