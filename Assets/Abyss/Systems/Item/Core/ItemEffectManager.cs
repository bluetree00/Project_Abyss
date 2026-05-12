using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 활성 아이템 효과 관리 + 이벤트 디스패치.
///
/// ■ GameRunSession이 보유, BindPlayer 시 초기화.
/// ■ 인벤토리 변경/무기 변경 시 Rebuild() 호출.
/// ■ 게임 이벤트 발생 시 해당 메서드 호출 → 모든 효과 순회.
/// </summary>
public sealed class ItemEffectManager
{
    private readonly List<IItemEffect> _activeEffects = new();
    private readonly ItemEffectContext _ctx = new();
    private readonly HashSet<string> _onceTriggered = new(); // OnPickup 등 1회 발동 추적
    private readonly Dictionary<string, int> _persistentStacks = new(); // Rebuild 간 스택 보존
    private RunItemInventory _inventory;

    public IReadOnlyList<IItemEffect> ActiveEffects => _activeEffects;

    // ── 초기화 / 갱신 ───────────────────────────────────────

    /// <summary>플레이어 바인딩 시 호출.</summary>
    public void Initialize(PlayerController player, GameRunSession session, RunItemInventory inventory)
    {
        _inventory = inventory;
        _ctx.Update(player, session);
        Rebuild();
    }

    /// <summary>
    /// 인벤토리 변경/무기 변경/HP 변경 시 호출.
    /// 모든 효과 인스턴스를 재생성.
    /// </summary>
    public void Rebuild()
    {
        // 기존 효과 정리
        foreach (var eff in _activeEffects)
            eff.OnDeactivate();
        _activeEffects.Clear();

        if (_inventory == null) return;

        foreach (var item in _inventory.Items)
        {
            if (item.effects == null) continue;

            foreach (var slot in item.effects)
            {
                var effect = ItemEffectRegistry.Create(slot);
                if (effect == null) continue;

                _activeEffects.Add(effect);
                effect.OnActivate(_ctx);
            }
        }
    }

    /// <summary>컨텍스트 갱신 (무기 변경, HP 변경 시).</summary>
    public void RefreshContext(PlayerController player, GameRunSession session)
    {
        _ctx.Update(player, session);
    }

    /// <summary>HP 변경 시 경량 갱신.</summary>
    public void RefreshHpRatio()
    {
        _ctx.RefreshHpRatio();
    }

    /// <summary>런 종료 시 정리.</summary>
    public void Cleanup()
    {
        foreach (var eff in _activeEffects)
            eff.OnDeactivate();
        _activeEffects.Clear();
        _onceTriggered.Clear();
        _persistentStacks.Clear();
        _inventory = null;
    }

    /// <summary>1회 발동 효과 추적. 이미 발동했으면 false.</summary>
    public bool TryTriggerOnce(string key)
    {
        return _onceTriggered.Add(key);
    }

    /// <summary>Rebuild 간 보존되는 스택 값 읽기.</summary>
    public int GetPersistentStack(string key) =>
        _persistentStacks.TryGetValue(key, out int v) ? v : 0;

    /// <summary>Rebuild 간 보존되는 스택 값 쓰기.</summary>
    public void SetPersistentStack(string key, int value) =>
        _persistentStacks[key] = value;

    // ── 스탯 합산 ───────────────────────────────────────────

    /// <summary>
    /// 모든 활성 효과의 스탯 수정을 합산.
    /// PlayerRuntimeStats.RefreshItemBonuses()에서 호출.
    /// </summary>
    public AccumulatedStats GetAccumulatedStats()
    {
        var stats = new AccumulatedStats();
        foreach (var eff in _activeEffects)
        {
            if (eff.IsActive(_ctx))
                eff.ModifyStats(_ctx, ref stats);
        }
        return stats;
    }

    // ── 전투: 공격 ──────────────────────────────────────────

    public void OnPreDealDamage(ref DamagePacket pkt)
    {
        foreach (var eff in _activeEffects)
            if (eff.IsActive(_ctx))
                eff.OnPreDealDamage(_ctx, ref pkt);
    }

    public void OnPostDealDamage(DamageReport report)
    {
        foreach (var eff in _activeEffects)
            if (eff.IsActive(_ctx))
                eff.OnPostDealDamage(_ctx, report);
    }

    public void OnKill(GameObject target)
    {
        foreach (var eff in _activeEffects)
            if (eff.IsActive(_ctx))
                eff.OnKill(_ctx, target);
    }

    // ── 전투: 피격 ──────────────────────────────────────────

    public void OnPreTakeDamage(ref DamagePacket pkt)
    {
        foreach (var eff in _activeEffects)
            if (eff.IsActive(_ctx))
                eff.OnPreTakeDamage(_ctx, ref pkt);
    }

    public void OnPostTakeDamage(DamageReport report)
    {
        foreach (var eff in _activeEffects)
            if (eff.IsActive(_ctx))
                eff.OnPostTakeDamage(_ctx, report);
    }

    /// <summary>사망 직전. true 반환 시 생존.</summary>
    public bool OnNearDeath(out float healPercent, out float invincibleDuration)
    {
        healPercent = 0f;
        invincibleDuration = 0f;

        foreach (var eff in _activeEffects)
        {
            if (!eff.IsActive(_ctx)) continue;
            if (eff.OnNearDeath(_ctx, ref healPercent, ref invincibleDuration))
                return true;
        }
        return false;
    }

    // ── 이동/회피 ───────────────────────────────────────────

    public void OnRollEnd()
    {
        foreach (var eff in _activeEffects)
            if (eff.IsActive(_ctx))
                eff.OnRollEnd(_ctx);
    }

    public void OnRollLand(Vector3 position)
    {
        foreach (var eff in _activeEffects)
            if (eff.IsActive(_ctx))
                eff.OnRollLand(_ctx, position);
    }

    public void OnJumpLand(Vector3 position)
    {
        foreach (var eff in _activeEffects)
            if (eff.IsActive(_ctx))
                eff.OnJumpLand(_ctx, position);
    }

    // ── 진행 ────────────────────────────────────────────────

    public void OnRoomEnter()
    {
        foreach (var eff in _activeEffects)
            if (eff.IsActive(_ctx))
                eff.OnRoomEnter(_ctx);
    }

    public void OnRoomClear()
    {
        foreach (var eff in _activeEffects)
            if (eff.IsActive(_ctx))
                eff.OnRoomClear(_ctx);
    }

    public void OnBossEnter()
    {
        foreach (var eff in _activeEffects)
            if (eff.IsActive(_ctx))
                eff.OnBossEnter(_ctx);
    }

    public void OnBossClear()
    {
        foreach (var eff in _activeEffects)
            if (eff.IsActive(_ctx))
                eff.OnBossClear(_ctx);
    }

    public void OnRecipeComplete()
    {
        foreach (var eff in _activeEffects)
            if (eff.IsActive(_ctx))
                eff.OnRecipeComplete(_ctx);
    }

    public void OnItemPickup(RuntimeItemData pickedItem)
    {
        foreach (var eff in _activeEffects)
            if (eff.IsActive(_ctx))
                eff.OnItemPickup(_ctx, pickedItem);
    }

    // ── 스킬 ────────────────────────────────────────────────

    public void OnSkillUse(SkillType skill)
    {
        foreach (var eff in _activeEffects)
            if (eff.IsActive(_ctx))
                eff.OnSkillUse(_ctx, skill);
    }

    // ── 치유 ────────────────────────────────────────────────

    public void ModifyHeal(ref int amount)
    {
        foreach (var eff in _activeEffects)
            if (eff.IsActive(_ctx))
                eff.ModifyHeal(_ctx, ref amount);
    }

    // ── 틱 ──────────────────────────────────────────────────

    public void OnTick(float deltaTime)
    {
        foreach (var eff in _activeEffects)
            eff.OnTick(_ctx, deltaTime);
    }
}
