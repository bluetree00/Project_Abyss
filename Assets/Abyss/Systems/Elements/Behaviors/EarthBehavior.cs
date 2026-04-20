/// <summary>Earth — petrify: duration 동안 완전 정지 + 무적.</summary>
public class EarthBehavior : ElementBehavior
{
    public override ElementType Element => ElementType.Earth;

    public override void Apply(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry)
    {
        target.SetPetrified(true);
    }

    public override void Clear(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry)
    {
        target.SetPetrified(false);
    }
}
