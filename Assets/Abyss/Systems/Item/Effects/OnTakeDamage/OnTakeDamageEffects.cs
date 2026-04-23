using UnityEngine;

// ═══════════════════════════════════════════════════════════
// 피격 시 발동하는 효과
// ═══════════════════════════════════════════════════════════

public sealed class DamageNegateEffect : ItemEffectBase
{
    public DamageNegateEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPreTakeDamage(ItemEffectContext ctx, ref DamagePacket pkt)
    {
        if (Random.value < _value)
        {
            pkt.Negated = true;
            pkt.FinalDamage = 0f;
            Debug.Log("[DamageNegate] 피해 무효화!");
        }
    }
}

public sealed class DamageReflectEffect : ItemEffectBase
{
    private const string VfxKey = "VFX_DamageReflect";

    public DamageReflectEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.Attacker == null) return;
        float reflect = report.DamageDealt * _value;
        if (reflect <= 0f) return;

        if (report.Attacker.TryGetComponent<IDamageable>(out var damageable))
        {
            var instigator = ctx.Player != null ? ctx.Player.gameObject : null;
            damageable.TakeDamage(reflect, instigator, knockbackMultiplier: 0f);

            ItemEffectVfxHelper.SpawnOneShotAt(VfxKey, report.Attacker.transform.position);
            ItemEffectVfxHelper.ShowNotice($"<color=#88CCFF>피해 반사</color> {reflect:F0} → {report.Attacker.name}");
            Debug.Log($"[DamageReflect] {reflect:F0} 반사 → {report.Attacker.name}");
        }
    }
}

public sealed class DefenseOnHitEffect : ItemEffectBase
{
    private float _cooldownEnd;

    public DefenseOnHitEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        float cooldown = _duration > 0f ? _duration * 2f : 10f; // duration=5 → 쿨다운 10초
        if (Time.time < _cooldownEnd) return;

        var session = ctx.Session;
        if (session?.BuffHandler != null)
        {
            session.BuffHandler.AddBuff(
                new StatModifier(StatType.Defense, _value),
                1, false, false
            );
        }

        _cooldownEnd = Time.time + cooldown;
        Debug.Log($"[DefenseOnHit] 방어력 +{_value} ({_duration}초), 쿨다운 {cooldown}초");
    }
}

/// <summary>
/// 피격 시 자해 추가 체력 손실 (prometheus_flame slot2).
/// value 절대값 = 현재 최대체력 비율(예: 0.05 = 5%).
/// TODO: 자해 데미지 API 연결 (PlayerController.Heal은 음수를 막음).
/// 현재는 로그만 — 아이템 데이터(효과↔이름 불일치) 재확인 후 CSV 수정 또는 구현 완성.
/// </summary>
public sealed class ExtraDamageOnHitEffect : ItemEffectBase
{
    public ExtraDamageOnHitEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (ctx.Player == null) return;
        int maxHp = ctx.Player.RuntimeStats != null ? ctx.Player.RuntimeStats.MaxHp : 0;
        if (maxHp <= 0) return;

        int extraLoss = Mathf.Max(1, Mathf.RoundToInt(maxHp * Mathf.Abs(_value)));
        Debug.Log($"[ExtraDamageOnHit] 추가 체력 손실 {extraLoss} (미구현)");
    }
}
