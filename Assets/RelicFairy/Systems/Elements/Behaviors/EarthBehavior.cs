/// <summary>Earth — petrify: duration 동안 이동 불가 + 방어력 (1 - magnitude_a) 배.
/// CSV: magnitude_a=0.1, duration=2 → 이동 0 + 방어력 90% (10% 감소) 2초.</summary>
public class EarthBehavior : ElementBehavior
{
    public override ElementType Element => ElementType.Earth;

    public override void Apply(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry)
    {
        target.SetMovementMultiplier(0f);
        target.SetDefenseMultiplier(1f - entry.magnitude_a);
    }

    public override void Clear(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry)
    {
        target.SetMovementMultiplier(1f);
        target.SetDefenseMultiplier(1f);
    }
}
