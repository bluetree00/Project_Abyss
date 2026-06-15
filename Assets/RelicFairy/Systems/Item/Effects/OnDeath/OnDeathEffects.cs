using UnityEngine;

// ═══════════════════════════════════════════════════════════
// 사망 직전 발동 효과
// ═══════════════════════════════════════════════════════════

public sealed class ReviveHealEffect : ItemEffectBase
{
    // 사용 횟수는 persistentStack에 보존 — 인스턴스 필드면 그리드 재배치(Rebuild) 때 0으로
    // 리셋돼 부활 무한 재사용이 가능했다. persistentStack은 Rebuild를 넘어 살아남고 런 종료 시 정리된다.
    private readonly string _usedKey;

    public ReviveHealEffect(ItemEffectSlot s) : base(s)
        => _usedKey = $"ReviveHeal_used_{s?.slot}";

    public override bool OnNearDeath(ItemEffectContext ctx, ref float healPercent, ref float invincibleDuration)
    {
        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;
        if (mgr == null) return false;

        int limit = _maxStack > 0 ? _maxStack : 1;
        int used = mgr.GetPersistentStack(_usedKey);
        if (used >= limit) return false;

        mgr.SetPersistentStack(_usedKey, used + 1);
        healPercent = _value;
        Debug.Log($"[ReviveHeal] 부활! HP {_value * 100f:F0}% 회복 ({used + 1}/{limit})");
        return true;
    }
}

public sealed class DeathNegateEffect : ItemEffectBase
{
    private readonly string _usedKey;

    public DeathNegateEffect(ItemEffectSlot s) : base(s)
        => _usedKey = $"DeathNegate_used_{s?.slot}";

    public override bool OnNearDeath(ItemEffectContext ctx, ref float healPercent, ref float invincibleDuration)
    {
        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;
        if (mgr == null) return false;

        int limit = _maxStack > 0 ? _maxStack : 1;
        int used = mgr.GetPersistentStack(_usedKey);
        if (used >= limit) return false;

        mgr.SetPersistentStack(_usedKey, used + 1);
        healPercent = 0.01f; // HP 1%로 생존
        invincibleDuration = _duration;
        Debug.Log($"[DeathNegate] 사망 무효! {_duration}초 무적 ({used + 1}/{limit})");
        return true;
    }
}
