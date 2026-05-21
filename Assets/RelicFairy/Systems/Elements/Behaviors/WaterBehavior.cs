/// <summary>Water — wet: 이동속도 (1 - magnitude_a) 배 + 공격속도 (1 - magnitude_b) 배.
/// CSV: magnitude_a=0.5, magnitude_b=0.5, duration=2 → 이속/공속 각 50% 감소 2초.</summary>
public class WaterBehavior : ElementBehavior
{
    public override ElementType Element => ElementType.Water;

    public override void Apply(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry)
    {
        target.SetMovementMultiplier(1f - entry.magnitude_a);
        target.SetAttackSpeedMultiplier(1f - entry.magnitude_b);
    }

    public override void Clear(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry)
    {
        target.SetMovementMultiplier(1f);
        target.SetAttackSpeedMultiplier(1f);
    }
}
