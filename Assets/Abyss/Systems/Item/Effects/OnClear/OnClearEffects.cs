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
        if (ctx.Player == null) return;

        // 현재 무기 원소를 다음 공격에 부여 (무속성이면 랜덤)
        var element = ctx.WeaponElement;
        if (element == WeaponElement.None)
        {
            var elements = new[] { WeaponElement.Fire, WeaponElement.Water, WeaponElement.Grass,
                                   WeaponElement.Earth, WeaponElement.Lightning };
            element = elements[Random.Range(0, elements.Length)];
        }

        ctx.Player.NextAttackElement = element;
        ItemEffectVfxHelper.ShowNotice($"<color=#EEDD44>다음 공격에 원소 부여!</color>");
        Debug.Log($"[RecipeSynergyNextAttack] 다음 공격 원소: {element}");
    }

    public override void OnPreDealDamage(ItemEffectContext ctx, ref DamagePacket pkt)
    {
        if (ctx.Player == null) return;
        if (ctx.Player.NextAttackElement == WeaponElement.None) return;

        pkt.Element = ctx.Player.NextAttackElement;
        ctx.Player.NextAttackElement = WeaponElement.None; // 1회 소비
    }
}

public sealed class SkillCooldownFlatEffect : ItemEffectBase
{
    public SkillCooldownFlatEffect(ItemEffectSlot s) : base(s) { }

    public override void OnRecipeComplete(ItemEffectContext ctx)
    {
        var tracker = ctx.Player?.CooldownTracker;
        if (tracker == null) return;

        float seconds = Mathf.Abs(_value); // value -2 → 2초 감소
        tracker.ReduceAllCooldowns(seconds);
        Debug.Log($"[SkillCooldownFlat] 모든 스킬 쿨타임 -{seconds}초");
    }
}

public sealed class DefensePermStackEffect : ItemEffectBase
{
    private const string StackKey = "DefensePermStack";

    public DefensePermStackEffect(ItemEffectSlot s) : base(s) { }

    public override void OnRecipeComplete(ItemEffectContext ctx)
    {
        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;
        if (mgr == null) return;

        int stacks = mgr.GetPersistentStack(StackKey);
        mgr.SetPersistentStack(StackKey, stacks + 1);
        Debug.Log($"[DefensePermStack] 방어력 +{_value} 영구 중첩 ({stacks + 1}스택)");
    }

    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats)
    {
        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;
        int stacks = mgr?.GetPersistentStack(StackKey) ?? 0;
        stats.Defense += (int)(_value * stacks);
    }
}

public sealed class HPRegenOnBossEnterEffect : ItemEffectBase
{
    public HPRegenOnBossEnterEffect(ItemEffectSlot s) : base(s) { }

    public override void OnBossEnter(ItemEffectContext ctx)
    {
        ctx.Player?.Heal((int)_value);
        Debug.Log($"[HPRegenOnBossEnter] 보스방 진입 — 체력 {_value} 회복");
    }
}

public sealed class MaxHPDecreasePerRoomEffect : ItemEffectBase
{
    public MaxHPDecreasePerRoomEffect(ItemEffectSlot s) : base(s) { }

    public override void OnRoomEnter(ItemEffectContext ctx)
    {
        if (ctx.Player == null) return;

        // value -0.05 = 현재 최대체력의 5% 감소
        var stats = ctx.Player.RuntimeStats;
        if (stats == null || stats.MaxHp <= 1) return;
        int decrease = Mathf.Max(1, Mathf.RoundToInt(stats.MaxHp * Mathf.Abs(_value)));
        stats.DecreaseMaxHp(decrease);
        Debug.Log($"[MaxHPDecreasePerRoom] 최대 체력 -{decrease} (남은 {stats.MaxHp})");
    }
}
