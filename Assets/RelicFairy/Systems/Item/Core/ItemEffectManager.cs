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

    // 다음 1타에만 가산되는 피해 보너스 누적(광폭형 "다음 공격 ×N" + 단발 조건부 "첫 공격 +N%" 공용).
    // OnPreDealDamage에서 (1+합)을 곱하고 소비. 0.4=+40%, 1.5=+150%(×2.5).
    private float _nextAttackBonus;

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

        foreach (var item in _inventory.PlacedItems)
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
        _nextAttackBonus = 0f;
        _ctx.Stats?.ApplyItemDynamicStats(default);   // 동적 레이어 0으로 복원
        ItemCombatMods.Clear();                       // 공격 변형 스냅샷 복원
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

    // ── 버프창 표시 수집 ────────────────────────────────────

    /// <summary>
    /// 현재 조건이 충족된 활성 지속 버프(조건부 Cond* 등)를 버프창 표시 항목으로 수집.
    /// 읽기 전용 — 효과 상태/조건을 질의만 한다(동작/밸런스 무변경). HUD 어댑터(ItemBuffViewSource)가 폴링 호출.
    /// </summary>
    public void CollectActiveBuffViews(List<BuffViewItem> into)
    {
        if (into == null) return;
        for (int i = 0; i < _activeEffects.Count; i++)
            if (_activeEffects[i] is IItemBuffViewProvider p && p.TryGetBuffView(_ctx, out var item))
                into.Add(item);
    }

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

    public void OnPreDealDamage(ref DamagePacket pkt, bool meleeAttack)
    {
        foreach (var eff in _activeEffects)
            if (eff.IsActive(_ctx))
                eff.OnPreDealDamage(_ctx, ref pkt);

        // 다음-공격-강화 버퍼 소비(1타 한정) — 일반(근접)공격에서만(원거리/스킬 누수 방지).
        if (meleeAttack && _nextAttackBonus > 0f)
        {
            pkt.FinalDamage *= 1f + _nextAttackBonus;
            _nextAttackBonus = 0f;
        }
    }

    /// <summary>다음 1타에 가산될 피해 보너스 예약(광폭형/단발 조건부 공용). 누적 후 첫 공격에서 소비.</summary>
    public void QueueNextAttackBonus(float bonus)
    {
        if (bonus > 0f) _nextAttackBonus += bonus;
    }

    public void OnPostDealDamage(DamageReport report)
    {
        foreach (var eff in _activeEffects)
            if (eff.IsActive(_ctx))
                eff.OnPostDealDamage(_ctx, report);

        // 룬 속성 효과 OnHit/OnCrit — 근접(ColliderInstance)·원거리(BasicArrow) 공통 경로.
        // (Fix#2) 원거리는 HitFeedbackService.RaiseHit를 안 타므로 이 경로가 근/원 단일 통지점이다.
        _ctx.Player?.RuneEffects?.NotifyHit(report);
    }

    public void OnKill(GameObject target)
    {
        foreach (var eff in _activeEffects)
            if (eff.IsActive(_ctx))
                eff.OnKill(_ctx, target);

        // 유물 파츠 처치 훅 — 죽은 적 GameObject를 넘겨 화상/출혈 전염·처치 보상 등에 쓴다.
        // (룬 디스패처의 QuestEvents 경로는 codeName뿐이라 여기서 발화. 미생성 시 no-op.)
        _ctx.Player?.RuneEffectsOrNull?.PartsOrNull?.NotifyKill(target);
    }

    /// <summary>활성 보스드랍 아이템들의 추가 드랍 횟수 합(설계 ④). RoomClearGate가 보스방에서 호출.</summary>
    public int GetBonusBossDropCount()
    {
        int total = 0;
        foreach (var eff in _activeEffects)
            if (eff is ItemEffectBase b) total += b.BonusBossDrops;
        return total;
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
        _ctx.RefreshHpRatio();   // HP 조건 라이브 평가

        foreach (var eff in _activeEffects)
            eff.OnTick(_ctx, deltaTime);

        // 조건부/타임드 동적 스탯 합산 → 플레이어 동적 레이어 push (무변동 시 내부에서 재계산 생략)
        var dyn = new ItemDynamicStats();
        foreach (var eff in _activeEffects)
            eff.ContributeDynamicStats(_ctx, ref dyn);
        _ctx.Stats?.ApplyItemDynamicStats(in dyn);

        // 공격 판정 변형(형태/사거리/다단/투사체) 합산 → 무기 판정 코드가 읽는 전역 스냅샷에 push
        var mods = new ItemCombatModifiers();
        foreach (var eff in _activeEffects)
            if (eff is ItemEffectBase b) b.ContributeCombatMods(_ctx, ref mods);
        ItemCombatMods.Current = mods;
    }
}
