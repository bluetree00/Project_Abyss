/// <summary>
/// Water — wet: 이속 (1 - magnitude_a) 배 + 받는 원소 누적치 magnitude_b 배.
/// </summary>
public class WaterBehavior : ElementBehavior
{
    public override ElementType Element => ElementType.Water;

    public override void Apply(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry)
    {
        target.SetMovementMultiplier(1f - entry.magnitude_a);
        buildup.SetAccumGainMultiplier(entry.magnitude_b);
    }

    public override void Clear(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry)
    {
        target.SetMovementMultiplier(1f);
        buildup.SetAccumGainMultiplier(1f);
    }
}
