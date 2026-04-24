/// <summary>Fire — burn: duration 동안 tick_interval 마다 대상 MaxHp × magnitude_a 데미지.
/// CSV: magnitude_a=0.02, tick_interval=1.0, duration=3 → 초당 2% 화염 데미지 × 3회.</summary>
public class FireBehavior : ElementBehavior
{
    public override ElementType Element => ElementType.Fire;

    public override void Apply(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry) { }

    public override void Tick(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry)
    {
        float damage = target.MaxHp * entry.magnitude_a;
        target.TakeElementalDoT(damage, ElementType.Fire);
    }

    public override void Clear(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry) { }
}
