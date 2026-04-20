/// <summary>Fire — burn DoT (최대 HP × magnitude_a 만큼 tick_interval 마다 데미지).</summary>
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
