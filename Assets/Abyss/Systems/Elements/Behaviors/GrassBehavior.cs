/// <summary>Grass — poison (스택형):
/// 발동 1회 = 스택 +1 (무한 중첩). tick_interval 마다 PoisonStacks × magnitude_a 고정 데미지.
/// duration은 매 발동 시 갱신. Clear 시 스택 리셋.
/// CSV: magnitude_a=5, tick_interval=1.0, duration=3 → 스택당 초당 5 고정 데미지.</summary>
public class GrassBehavior : ElementBehavior
{
    public override ElementType Element => ElementType.Grass;

    public override void Apply(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry)
    {
        buildup.AddPoisonStack();
    }

    public override void Tick(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry)
    {
        int stacks = buildup.PoisonStacks;
        if (stacks > 0)
            target.TakeElementalDoT(stacks * entry.magnitude_a, ElementType.Grass);
    }

    public override void Clear(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry)
    {
        buildup.ResetPoisonStacks();
    }
}
