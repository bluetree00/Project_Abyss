using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

// ═══════════════════════════════════════════════════════════
// 구르기 관련 효과
// ═══════════════════════════════════════════════════════════

public sealed class FirstAttackAfterRollEffect : ItemEffectBase
{
    // 구르기 후 첫 공격 강화는 일정 윈도 내에서만 유효 — 무제한 보관 시 다음 전투까지 누수.
    private const float DefaultWindow = 3f;
    private float _windowUntil = -999f;

    public FirstAttackAfterRollEffect(ItemEffectSlot s) : base(s) { }

    public override void OnRollEnd(ItemEffectContext ctx)
    {
        _windowUntil = Time.time + (_duration > 0f ? _duration : DefaultWindow);
    }

    public override void OnPreDealDamage(ItemEffectContext ctx, ref DamagePacket pkt)
    {
        if (Time.time >= _windowUntil) return;
        pkt.FinalDamage *= (1f + _value);
        _windowUntil = -999f;   // 1회 소비
        Debug.Log($"[FirstAttackAfterRoll] 구르기 후 첫 공격 +{_value * 100f:F0}%");
    }
}

public sealed class RollLandingDamageEffect : ItemEffectBase
{
    private const float DefaultRadius = 3f;
    private const string DefaultVfxKey = "VFX_JumpLandingDamage";
    private static readonly List<MonsterBase> s_buf = new();

    public RollLandingDamageEffect(ItemEffectSlot s) : base(s) { }

    public override void OnRollLand(ItemEffectContext ctx, Vector3 position)
    {
        if (ctx.Player == null || ctx.Stats == null) return;

        float radius = _value2 > 0f ? _value2 : DefaultRadius;

        // 플레이어 공격력 기반 데미지
        var weaponData = ctx.Player.WeaponManager?.CurrentWeaponData;
        var kind = weaponData != null ? weaponData.weaponType.GetAttackStatKind() : AttackStatKind.Melee;
        float damage = DamageFormula.Calculate(_value, ctx.Stats.GetEffectiveAttack(kind));

        int n = CombatQuery.GetNearbyEnemies(position, radius, ctx.Player.gameObject, 32, s_buf);
        for (int i = 0; i < n; i++)
            s_buf[i].TakeDamage(damage, ctx.Player.gameObject, knockbackMultiplier: 0f);

        ItemEffectVfxHelper.SpawnOneShotAt(ResolveVfxKey(DefaultVfxKey), position);
        ItemEffectVfxHelper.ShowNotice($"<color=#FFAA44>착지 충격</color> {damage:F0} 데미지");
        Debug.Log($"[JumpLandingDamage] 착지 범위 피해 {damage:F0} (반경 {radius}m)");
    }
}
