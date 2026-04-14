using UnityEngine;

// ═══════════════════════════════════════════════════════════
// 방/보스 클리어 시 발동하는 효과
// ═══════════════════════════════════════════════════════════

public sealed class HPRegenOnClearEffect : ItemEffectBase
{
    public HPRegenOnClearEffect(ItemEffectSlot s) : base(s) { }

    public override void OnRoomClear(ItemEffectContext ctx)
    {
        ctx.Player?.Heal((int)_value);
        Debug.Log($"[HPRegenOnClear] 체력 {_value} 회복");
    }
}

public sealed class BossDropItemEffect : ItemEffectBase
{
    public BossDropItemEffect(ItemEffectSlot s) : base(s) { }

    public override void OnBossClear(ItemEffectContext ctx)
    {
        // TODO: 추가 아이템 드랍 로직
        Debug.Log($"[BossDropItem] 추가 아이템 {(int)_value}개 드랍");
    }
}

public sealed class BuffRefreshOnBossEffect : ItemEffectBase
{
    public BuffRefreshOnBossEffect(ItemEffectSlot s) : base(s) { }

    public override void OnBossClear(ItemEffectContext ctx)
    {
        var handler = ctx.Session?.BuffHandler;
        if (handler == null) return;

        handler.RefreshAndUpgradeAll();
        Debug.Log("[BuffRefreshOnBoss] 모든 버프 지속시간 리셋 + 1티어 승급!");
    }
}

public sealed class HPRegenOnRecipeEffect : ItemEffectBase
{
    public HPRegenOnRecipeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnRecipeComplete(ItemEffectContext ctx)
    {
        ctx.Player?.Heal((int)_value);
        Debug.Log($"[HPRegenOnRecipe] 체력 {_value} 회복");
    }
}

public sealed class AllDamageOnRecipeEffect : ItemEffectBase
{
    private const string StackKey = "AllDamageOnRecipe";

    public AllDamageOnRecipeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnRecipeComplete(ItemEffectContext ctx)
    {
        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;
        if (mgr == null) return;

        int stacks = mgr.GetPersistentStack(StackKey);
        if (_maxStack > 0 && stacks >= _maxStack) return;

        mgr.SetPersistentStack(StackKey, stacks + 1);
        Debug.Log($"[AllDamageOnRecipe] 공격력 +{_value * 100f:F0}% ({stacks + 1}/{_maxStack})");
    }

    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats)
    {
        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;
        int stacks = mgr?.GetPersistentStack(StackKey) ?? 0;
        stats.AllDamagePercent += _value * stacks;
    }
}

public sealed class RecipeSynergyNextAttackEffect : ItemEffectBase
{
    public RecipeSynergyNextAttackEffect(ItemEffectSlot s) : base(s) { }

    public override void OnRecipeComplete(ItemEffectContext ctx)
    {
        // TODO: 다음 공격에 원소 효과 추가 플래그
        Debug.Log("[RecipeSynergyNextAttack] 다음 공격에 원소 효과 추가");
    }
}
