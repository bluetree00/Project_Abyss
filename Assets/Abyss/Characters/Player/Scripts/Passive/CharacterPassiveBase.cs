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
}
