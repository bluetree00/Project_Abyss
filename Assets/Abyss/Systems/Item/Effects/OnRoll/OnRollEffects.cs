using UnityEngine;

// ═══════════════════════════════════════════════════════════
// 구르기 관련 효과
// ═══════════════════════════════════════════════════════════

public sealed class FirstAttackAfterRollEffect : ItemEffectBase
{
    private bool _buffActive;

    public FirstAttackAfterRollEffect(ItemEffectSlot s) : base(s) { }

    public override void OnRollEnd(ItemEffectContext ctx)
    {
        _buffActive = true;
    }

    public override void OnPreDealDamage(ItemEffectContext ctx, ref DamagePacket pkt)
    {
        if (!_buffActive) return;
        pkt.FinalDamage *= (1f + _value);
        _buffActive = false;
        Debug.Log($"[FirstAttackAfterRoll] 구르기 후 첫 공격 +{_value * 100f:F0}%");
    }
}

public sealed class RollLandingDamageEffect : ItemEffectBase
{
    private const float DefaultRadius = 3f;
    private const string VfxKey = "VFX_JumpLandingDamage";

    public RollLandingDamageEffect(ItemEffectSlot s) : base(s) { }

    public override void OnJumpLand(ItemEffectContext ctx, Vector3 position)
    {
        if (ctx.Player == null || ctx.Stats == null) return;

        float radius = _value2 > 0f ? _value2 : DefaultRadius;

        // 플레이어 공격력 기반 데미지
        var weaponData = ctx.Player.WeaponManager?.CurrentWeaponData;
        var kind = weaponData != null ? weaponData.weaponType.GetAttackStatKind() : AttackStatKind.Melee;
        float damage = DamageFormula.Calculate(_value, ctx.Stats.GetEffectiveAttack(kind));

        var hits = Physics.OverlapSphere(position, radius);
        foreach (var col in hits)
        {
            if (col.gameObject == ctx.Player.gameObject) continue;

            if (col.TryGetComponent<IDamageable>(out var damageable))
            {
                damageable.TakeDamage(damage, ctx.Player.gameObject, knockbackMultiplier: 0f);
            }
        }

        ItemEffectVfxHelper.SpawnOneShotAt(VfxKey, position);
        ItemEffectVfxHelper.ShowNotice($"<color=#FFAA44>착지 충격</color> {damage:F0} 데미지");
        Debug.Log($"[JumpLandingDamage] 착지 범위 피해 {damage:F0} (반경 {radius}m)");
    }
}
