/// <summary>Lightning — shock: duration 동안 이동/공격 모두 정지 (마비).
/// CSV: duration=1, magnitude 사용 안 함. 체인 로직 제거.</summary>
public class LightningBehavior : ElementBehavior
{
    public override ElementType Element => ElementType.Lightning;

    public override void Apply(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry)
    {
        target.SetMovementMultiplier(0f);
        target.SetAttackSpeedMultiplier(0f);
    }

    public override void Clear(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry)
    {
        target.SetMovementMultiplier(1f);
        target.SetAttackSpeedMultiplier(1f);
    }
}
