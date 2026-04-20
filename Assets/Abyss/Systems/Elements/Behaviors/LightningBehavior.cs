/// <summary>
/// Lightning — chain: 발동 시 즉시 magnitude_b 반경 내 적에게 원본 데미지 × magnitude_a 만큼 체인 데미지.
/// </summary>
public class LightningBehavior : ElementBehavior
{
    public override ElementType Element => ElementType.Lightning;

    public override void Apply(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry)
    {
        float radius = entry.magnitude_b;
        float damage = buildup.LastTriggerDamage * entry.magnitude_a;
        ElementEffectRunner.LightningChain(target, radius, damage);
    }

    public override void Clear(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry) { }
}
