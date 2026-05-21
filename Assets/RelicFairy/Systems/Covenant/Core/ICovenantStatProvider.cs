using System.Collections.Generic;

/// <summary>
/// PlayerRuntimeStats 레이어 6(서약)에 스탯 기여를 제공하는 인터페이스.
/// Stage에 따라 다른 값을 반환해야 한다.
/// </summary>
public interface ICovenantStatProvider
{
    IEnumerable<StatModifier> GetStatModifiers();
}
