using System.Collections.Generic;

/// <summary>
/// ElementType → ElementBehavior 매핑.
/// 새 원소 추가 시 _map에 한 줄 추가.
/// </summary>
public static class ElementRegistry
{
    private static readonly Dictionary<ElementType, ElementBehavior> _map = new()
    {
        { ElementType.Fire,      new FireBehavior() },
        { ElementType.Water,     new WaterBehavior() },
        { ElementType.Grass,     new GrassBehavior() },
        { ElementType.Earth,     new EarthBehavior() },
        { ElementType.Lightning, new LightningBehavior() },
    };

    public static ElementBehavior Get(ElementType element)
    {
        return _map.TryGetValue(element, out var behavior) ? behavior : null;
    }
}
