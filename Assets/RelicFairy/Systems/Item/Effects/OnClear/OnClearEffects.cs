using UnityEngine;

// ═══════════════════════════════════════════════════════════
// 방/보스 클리어 시 발동하는 효과
// ═══════════════════════════════════════════════════════════

/// <summary>
/// 방 클리어 시 보호막. <b>회복이 아니다</b> — 이 프로젝트는 흡혈·회복 계열을 두지 않는다.
///
/// <para>value는 최대HP 비율이다(차트 0.05 = 5%). 예전엔 <c>(int)_value</c>로 잘라 써서
/// 0.05가 <b>0</b>이 됐고, 「성역의 룬」은 설명만 "5% 회복"이고 실제론 아무것도 안 했다.</para>
///
/// <para>effect_type 키는 <c>HPRegenOnClear</c> 그대로 둔다 — 차트가 CDN 구동이라
/// 키를 바꾸면 재업로드 전까지 아이템이 통째로 죽는다. 이름과 동작이 어긋나는 건 그 대가다.</para>
/// </summary>
public sealed class HPRegenOnClearEffect : ItemEffectBase
{
    public HPRegenOnClearEffect(ItemEffectSlot s) : base(s) { }

    public override void OnRoomClear(ItemEffectContext ctx)
    {
        if (ctx.Player == null) return;
        float amount = ctx.Player.RuntimeStats.MaxHp * _value;
        if (amount <= 0f) return;
        ctx.Player.RuntimeStats.AddShield(amount);
        ItemGuide.Toast(ctx.Player.transform.position, $"보호막 +{(int)amount}");
    }
}

/// <summary>
/// 보스 처치 시 추가 아이템 드랍. value개만큼 RoomClearGate가 기존 _luckTable을 추가 롤해 append(설계 ④).
/// 드랍 로직은 RoomClearGate에 있고, 여기선 보너스 횟수만 노출한다.
/// </summary>
public sealed class BossDropItemEffect : ItemEffectBase
{
    public BossDropItemEffect(ItemEffectSlot s) : base(s) { }

    public override int BonusBossDrops => Mathf.Max(0, Mathf.RoundToInt(_value));
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
        if (ctx.Player == null) return;
        // 회복 → 보호막. 여긴 value가 고정값이라 비율로 읽지 않는다(사용 아이템 0종).
        if (_value <= 0f) return;
        ctx.Player.RuntimeStats.AddShield(_value);
        ItemGuide.Toast(ctx.Player.transform.position, $"보호막 +{(int)_value}");
    }
}

public sealed class AllDamageOnRecipeEffect : ItemEffectBase
{
    // 슬롯별 키 — 과거 공유 상수키는 같은 효과를 가진 모든 아이템이 한 카운터를 읽어
    // 1회 완성에 중복 가산되는 버그가 있었다(슬롯 포함으로 인스턴스별 분리).
    private readonly string _stackKey;

    public AllDamageOnRecipeEffect(ItemEffectSlot s) : base(s)
        => _stackKey = $"AllDamageOnRecipe_{s?.slot}";

    public override void OnRecipeComplete(ItemEffectContext ctx)
    {
        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;
        if (mgr == null) return;

        int stacks = mgr.GetPersistentStack(_stackKey);
        if (_maxStack > 0 && stacks >= _maxStack) return;

        mgr.SetPersistentStack(_stackKey, stacks + 1);
        Debug.Log($"[AllDamageOnRecipe] 공격력 +{_value * 100f:F0}% ({stacks + 1}/{_maxStack})");
    }

    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats)
    {
        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;
        int stacks = mgr?.GetPersistentStack(_stackKey) ?? 0;
        stats.AllDamagePercent += _value * stacks;
    }
}

/// <summary>레시피 완성 시 다음 일반(근접)공격 1타를 강화. 기존 다음-공격-강화 버퍼 재사용(0.4=+40%).</summary>
public sealed class RecipeSynergyNextAttackEffect : ItemEffectBase
{
    public RecipeSynergyNextAttackEffect(ItemEffectSlot s) : base(s) { }

    public override void OnRecipeComplete(ItemEffectContext ctx)
    {
        GameRunBootstrapper.Instance?.Run?.EffectManager?.QueueNextAttackBonus(_value);
        Debug.Log($"[RecipeSynergyNextAttack] 다음 공격 +{_value * 100f:F0}%");
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
    // 슬롯별 키 + 상한 — 과거 공유 상수키(중복 가산) + 무상한(무한 누적) 버그 수정.
    private const int DefaultCap = 20;
    private readonly string _stackKey;

    public DefensePermStackEffect(ItemEffectSlot s) : base(s)
        => _stackKey = $"DefensePermStack_{s?.slot}";

    public override void OnRecipeComplete(ItemEffectContext ctx)
    {
        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;
        if (mgr == null) return;

        int cap = _maxStack > 0 ? _maxStack : DefaultCap;
        int stacks = mgr.GetPersistentStack(_stackKey);
        if (stacks >= cap) return;

        mgr.SetPersistentStack(_stackKey, stacks + 1);
        if (ctx.Player != null) ItemGuide.Toast(ctx.Player.transform.position, $"방어 +{_value} 중첩");
        Debug.Log($"[DefensePermStack] 방어력 +{_value} 영구 중첩 ({stacks + 1}/{cap})");
    }

    public override void ModifyStats(ItemEffectContext ctx, ref AccumulatedStats stats)
    {
        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;
        int stacks = mgr?.GetPersistentStack(_stackKey) ?? 0;
        stats.Defense += (int)(_value * stacks);
    }
}

public sealed class HPRegenOnBossEnterEffect : ItemEffectBase
{
    public HPRegenOnBossEnterEffect(ItemEffectSlot s) : base(s) { }

    public override void OnBossEnter(ItemEffectContext ctx)
    {
        if (ctx.Player == null) return;
        // 회복 → 보호막. 여긴 value가 고정값이라 비율로 읽지 않는다(사용 아이템 0종).
        if (_value <= 0f) return;
        ctx.Player.RuntimeStats.AddShield(_value);
        ItemGuide.Toast(ctx.Player.transform.position, $"보스방 진입 — 보호막 +{(int)_value}");
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
        if (stats == null) return;

        // 하한 — value2(절대 최소 MaxHP, 미설정 시 1) 밑으로는 더 깎지 않는다(무한 감소 방지).
        int floor = _value2 > 0f ? Mathf.Max(1, (int)_value2) : 1;
        if (stats.MaxHp <= floor) return;

        int decrease = Mathf.Max(1, Mathf.RoundToInt(stats.MaxHp * Mathf.Abs(_value)));
        if (stats.MaxHp - decrease < floor) decrease = stats.MaxHp - floor;
        if (decrease <= 0) return;

        stats.DecreaseMaxHp(decrease);
        ItemGuide.Toast(ctx.Player.transform.position, $"최대 HP -{decrease}");
        Debug.Log($"[MaxHPDecreasePerRoom] 최대 체력 -{decrease} (남은 {stats.MaxHp}, 하한 {floor})");
    }
}
