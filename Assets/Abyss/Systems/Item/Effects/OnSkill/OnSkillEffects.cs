using UnityEngine;

// ═══════════════════════════════════════════════════════════
// 스킬 사용 시 발동하는 효과
// ═══════════════════════════════════════════════════════════

public sealed class FireExplosionOnSkillEffect : ItemEffectBase
{
    public FireExplosionOnSkillEffect(ItemEffectSlot s) : base(s) { }

    public override void OnSkillUse(ItemEffectContext ctx, SkillType skill)
    {
        if (ctx.WeaponElement != WeaponElement.Fire) return;
        // TODO: 화염 폭발 이펙트 + 범위 피해 스폰
        Debug.Log("[FireExplosionOnSkill] 화염 폭발 발동!");
    }
}

public sealed class LightningOnSkillEffect : ItemEffectBase
{
    public LightningOnSkillEffect(ItemEffectSlot s) : base(s) { }

    public override void OnSkillUse(ItemEffectContext ctx, SkillType skill)
    {
        if (ctx.WeaponElement != WeaponElement.Lightning) return;
        // TODO: 번개 추가 피해 이펙트 스폰
        Debug.Log("[LightningOnSkill] 번개 추가 피해 발동!");
    }
}
