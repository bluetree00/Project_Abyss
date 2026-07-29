/// <summary>
/// ICharacterPassive 기본 구현.
/// CanApply는 항상 true — 조건이 필요한 패시브에서 override한다.
/// </summary>
public abstract class CharacterPassiveBase : ICharacterPassive
{
    public abstract string         PassiveName { get; }
    public abstract PassiveTrigger Trigger     { get; }

    public virtual bool CanApply(PlayerController ctrl, in PassiveContext ctx) => true;
    public abstract void Apply(PlayerController ctrl, in PassiveContext ctx);

    /// <summary>
    /// FirePassive의 자동 발동 토스트를 끈다. CanApply는 통과하지만 Apply가 내부 조건으로 자주 no-op하는
    /// 패시브(예: 태양의 각인)는 true로 두고, 실제로 효과가 나갔을 때만 스스로 토스트한다(거짓 표시 방지).
    /// </summary>
    public virtual bool SuppressAutoToast => false;
}
