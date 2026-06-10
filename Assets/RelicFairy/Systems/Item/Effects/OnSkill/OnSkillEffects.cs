using UnityEngine;

// ═══════════════════════════════════════════════════════════
// 스킬 사용 시 발동하는 효과
// ═══════════════════════════════════════════════════════════

public sealed class FireExplosionOnSkillEffect : ItemEffectBase
{
    private const string DefaultVfxKey = "VFX_FireExplosion";
    private const float Radius = 4f;

    public FireExplosionOnSkillEffect(ItemEffectSlot s) : base(s) { }

    public override void OnSkillUse(ItemEffectContext ctx, SkillType skill)
    {
        if (ctx.Player == null || ctx.Stats == null) return;

        Vector3 center = ctx.Player.transform.position;

        // 공격력 기반 데미지
        var weaponData = ctx.Player.WeaponManager?.CurrentWeaponData;
        var kind = weaponData != null ? weaponData.weaponType.GetAttackStatKind() : AttackStatKind.Melee;
        float damage = DamageFormula.Calculate(_value, ctx.Stats.GetEffectiveAttack(kind));

        var hits = Physics.OverlapSphere(center, Radius);
        foreach (var col in hits)
        {
            if (col.gameObject == ctx.Player.gameObject) continue;
            if (col.TryGetComponent<IDamageable>(out var damageable))
                damageable.TakeDamage(damage, ctx.Player.gameObject, knockbackMultiplier: 0.5f);
        }

        ItemEffectVfxHelper.SpawnOneShotAt(ResolveVfxKey(DefaultVfxKey), center);
        ItemEffectVfxHelper.ShowNotice($"<color=#FF6622>화염 폭발</color> {damage:F0} 데미지");
        Debug.Log($"[FireExplosionOnSkill] 화염 폭발 {damage:F0} (반경 {Radius}m)");
    }
}

public sealed class LightningOnSkillEffect : ItemEffectBase
{
    private const string DefaultVfxKey = "VFX_LightningStrike";
    private const float Radius = 3f;

    public LightningOnSkillEffect(ItemEffectSlot s) : base(s) { }

    public override void OnSkillUse(ItemEffectContext ctx, SkillType skill)
    {
        if (ctx.Player == null || ctx.Stats == null) return;

        Vector3 center = ctx.Player.transform.position;

        var weaponData = ctx.Player.WeaponManager?.CurrentWeaponData;
        var kind = weaponData != null ? weaponData.weaponType.GetAttackStatKind() : AttackStatKind.Melee;
        float damage = DamageFormula.Calculate(_value, ctx.Stats.GetEffectiveAttack(kind));

        var hits = Physics.OverlapSphere(center, Radius);
        foreach (var col in hits)
        {
            if (col.gameObject == ctx.Player.gameObject) continue;
            if (col.TryGetComponent<IDamageable>(out var damageable))
                damageable.TakeDamage(damage, ctx.Player.gameObject, knockbackMultiplier: 0.3f);
        }

        ItemEffectVfxHelper.SpawnOneShotAt(ResolveVfxKey(DefaultVfxKey), center);
        ItemEffectVfxHelper.ShowNotice($"<color=#44CCFF>번개 강타</color> {damage:F0} 데미지");
        Debug.Log($"[LightningOnSkill] 번개 강타 {damage:F0} (반경 {Radius}m)");
    }
}
