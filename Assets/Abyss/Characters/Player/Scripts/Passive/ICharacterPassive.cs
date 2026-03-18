/// <summary>
/// 캐릭터 고유 패시브 인터페이스.
/// PlayerController.RegisterPassive()로 등록하고,
/// FirePassive()가 Trigger 일치 + CanApply 통과 시 Apply()를 호출한다.
/// </summary>
public interface ICharacterPassive
{
    string        PassiveName { get; }
    PassiveTrigger Trigger    { get; }

    /// <summary>발동 조건 — false 반환 시 Apply 스킵.</summary>
    bool CanApply(PlayerController ctrl, in PassiveContext ctx);

    /// <summary>실제 패시브 효과 적용.</summary>
    void Apply(PlayerController ctrl, in PassiveContext ctx);
}
