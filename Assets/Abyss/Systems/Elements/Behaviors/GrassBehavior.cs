/// <summary>Grass — poison: 받는 데미지 (1 + magnitude_b)배 + tick_interval 마다 magnitude_a 고정 DoT.</summary>
public class GrassBehavior : ElementBehavior
{
    public override ElementType Element => ElementType.Grass;

    public override void Apply(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry)
    {
        target.SetIncomingDamageMultiplier(1f + entry.magnitude_b);
    }

    public override void Tick(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry)
    {
        target.TakeElementalDoT(entry.magnitude_a, ElementType.Grass);
    }

    public override void Clear(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry)
    {
        target.SetIncomingDamageMultiplier(1f);
    }
}
