using UnityEngine;

/// <summary>
/// 미등록 effectType 폴백. 값을 AllDamageFlat에 가산.
/// </summary>
public sealed class GenericStatEffect : ItemEffectBase
{
    public GenericStatEffect(ItemEffectSlot slot) : base(slot) { }

    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats)
    {
        stats.AllDamageFlat += _value;
    }
}
