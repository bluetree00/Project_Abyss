using System;
using UnityEngine;

[Serializable]
public sealed class PlayerRuntimeStats
{
    // ── 공개 스탯 ────────────────────────────────────────────────────────────────
    public int MaxHp { get; private set; }
    public int Hp { get; private set; }
    public int MeleeAttack { get; private set; }
    public int RangedAttack { get; private set; }
    public int Defense { get; private set; }
    public int Luck { get; private set; }
    public float AttackSpeedMultiplier { get; private set; } = 1f;
    public float SkillCooldownReduction { get; private set; }
    public float ActiveItemCooldownReduction { get; private set; }
    public float HeavyChargeThreshold { get; private set; }

    /// <summary>레거시 호환 — Max(Melee, Ranged). 범용 공격력이 필요한 곳에서 사용.</summary>
    public int AttackPower => Mathf.Max(MeleeAttack, RangedAttack);

    public event Action OnChanged;

    // ── 초기화 ───────────────────────────────────────────────────────────────────

    public void InitializeFrom(CharacterData data)
    {
        if (data == null)
        {
            Debug.LogError("[PlayerRuntimeStats] CharacterData is null.");
            return;
        }

        MaxHp = Mathf.Max(1, data.maxHealth);
        Hp = MaxHp;

        _baseMelee  = Mathf.Max(0, data.baseMeleeAttack);
        _baseRanged = Mathf.Max(0, data.baseRangedAttack);
        _baseDefense = Mathf.Max(0, data.baseDefense);
        _baseLuck    = Mathf.Max(0, data.baseLuck);

        _weaponMelee = 0;
        _weaponRanged = 0;
        _weaponDefense = 0;

        _passiveMelee = 0;
        _passiveRanged = 0;
        _passiveDefense = 0;
        _passiveLuck = 0;
        _passiveSkillCdr = 0f;
        _passiveActiveItemCdr = 0f;

        _itemMelee = 0;
        _itemRanged = 0;
        _itemDefense = 0;
        _itemLuck = 0;
        _itemSkillCdr = 0f;
        _itemActiveItemCdr = 0f;

        _roomMelee = 0;
        _roomRanged = 0;
        _roomDefense = 0;

        _bonusAttackSpeed = 0f;

        HeavyChargeThreshold = Mathf.Max(0f, data.heavyAttackChargeThreshold);

        // 패시브 초기 적용
        ApplyPassive(data.passive);   // Recalculate + OnChanged 포함
    }

    /// <summary>서버 PlayerStatEntry 기반 초기화.</summary>
    public void InitializeFromServer(PlayerStatEntry entry, System.Collections.Generic.List<PassiveEntry> passives = null)
    {
        if (entry == null)
        {
            Debug.LogError("[PlayerRuntimeStats] PlayerStatEntry is null.");
            return;
        }

        MaxHp = Mathf.Max(1, entry.max_health);
        Hp = MaxHp;

        _baseMelee   = Mathf.Max(0, entry.base_melee_attack);
        _baseRanged  = Mathf.Max(0, entry.base_ranged_attack);
        _baseDefense = Mathf.Max(0, entry.base_defense);
        _baseLuck    = Mathf.Max(0, entry.base_luck);

        _weaponMelee = 0; _weaponRanged = 0; _weaponDefense = 0;
        _passiveMelee = 0; _passiveRanged = 0; _passiveDefense = 0;
        _passiveLuck = 0; _passiveSkillCdr = 0f; _passiveActiveItemCdr = 0f;
        _itemMelee = 0; _itemRanged = 0; _itemDefense = 0;
        _itemLuck = 0; _itemSkillCdr = 0f; _itemActiveItemCdr = 0f;
        _roomMelee = 0; _roomRanged = 0; _roomDefense = 0;
        _bonusAttackSpeed = 0f;

        HeavyChargeThreshold = Mathf.Max(0f, entry.heavy_charge_threshold);

        // 서버 패시브 적용 (Always 트리거만 — 조건부는 코드 패시브에서 처리)
        if (passives != null)
        {
            foreach (var p in passives)
            {
                if (string.IsNullOrEmpty(p.effect_type)) continue;
                if (p.trigger != "Always") continue;

                switch (p.effect_type)
                {
                    case "MeleeAttack":  _passiveMelee  += (int)p.value; break;
                    case "RangedAttack": _passiveRanged += (int)p.value; break;
                    case "Defense":      _passiveDefense += (int)p.value; break;
                    case "MaxHp":
                        MaxHp += (int)p.value;
                        Hp = Mathf.Min(Hp, MaxHp);
                        break;
                    case "Luck":         _passiveLuck += (int)p.value; break;
                    case "AttackSpeed":  _bonusAttackSpeed += p.value; break;
                    case "SkillCooldownReduction":       _passiveSkillCdr += p.value; break;
                    case "ActiveItemCooldownReduction":   _passiveActiveItemCdr += p.value; break;
                    case "AttackPower":
                        _passiveMelee  += (int)p.value;
                        _passiveRanged += (int)p.value;
                        break;
                }
            }
        }

        Recalculate();
    }

    // ── HP ────────────────────────────────────────────────────────────────────────

    public void SetHp(int hp)
    {
        hp = Mathf.Clamp(hp, 0, MaxHp);
        if (Hp == hp) return;
        Hp = hp;
        OnChanged?.Invoke();
    }

    public void Damage(int amount)
    {
        if (amount <= 0) return;
        SetHp(Hp - amount);
    }

    public void Heal(int amount)
    {
        if (amount <= 0) return;
        SetHp(Hp + amount);
    }

    public void SetHeavyChargeThreshold(float value)
    {
        value = Mathf.Max(0f, value);
        if (Mathf.Approximately(HeavyChargeThreshold, value)) return;
        HeavyChargeThreshold = value;
        OnChanged?.Invoke();
    }

    // ── 편의 메서드 ──────────────────────────────────────────────────────────────

    /// <summary>무기 타입에 따라 MeleeAttack 또는 RangedAttack 반환.</summary>
    public int GetEffectiveAttack(AttackStatKind kind) =>
        kind == AttackStatKind.Ranged ? RangedAttack : MeleeAttack;

    // ── 스탯 레이어 (내부) ───────────────────────────────────────────────────────
    // 최종 스탯 = Base (CharacterData)
    //           + PassiveBonus  (캐릭터 패시브, 런 시작 시 1회)
    //           + WeaponBonus   (장착 무기)
    //           + ItemBonus     (아이템 누적, 런 내 영구)
    //           + RoomBuff      (일시적, 방 단위)

    // -- Base --
    private int _baseMelee;
    private int _baseRanged;
    private int _baseDefense;
    private int _baseLuck;

    // -- Passive (PassiveSO) --
    private int _passiveMelee;
    private int _passiveRanged;
    private int _passiveDefense;
    private int _passiveLuck;
    private float _passiveSkillCdr;
    private float _passiveActiveItemCdr;

    // -- Weapon --
    private int _weaponMelee;
    private int _weaponRanged;
    private int _weaponDefense;

    // -- Item --
    private int _itemMelee;
    private int _itemRanged;
    private int _itemDefense;
    private int _itemLuck;
    private float _itemSkillCdr;
    private float _itemActiveItemCdr;

    // -- Room Buff --
    private int _roomMelee;
    private int _roomRanged;
    private int _roomDefense;

    // -- Grid Synergy (Always) --
    private int _synergyMelee;
    private int _synergyRanged;
    private int _synergyDefense;
    private int _synergyLuck;
    private int _synergyMaxHp;
    private float _synergySkillCdr;
    private float _synergyActiveItemCdr;
    private float _synergyAttackSpeed;

    // -- Grid Synergy (조건부) --
    private float _synergyLifesteal;
    private readonly System.Collections.Generic.List<ConditionalSynergy> _conditionalSynergies = new();

    // -- 공격 속도 보너스 (패시브 등에서 직접 설정) --
    private float _bonusAttackSpeed;

    // ── 무기 ─────────────────────────────────────────────────────────────────────

    /// <summary>무기 장착/해제 시 호출.</summary>
    public void SetWeaponStats(int melee, int ranged, int defense)
    {
        _weaponMelee   = Mathf.Max(0, melee);
        _weaponRanged  = Mathf.Max(0, ranged);
        _weaponDefense = Mathf.Max(0, defense);
        Recalculate();
    }

    // ── 패시브 ───────────────────────────────────────────────────────────────────

    /// <summary>런 시작 시 PassiveSO 적용. null이면 0으로 초기화.</summary>
    public void ApplyPassive(PassiveSO passive)
    {
        _passiveMelee = 0;
        _passiveRanged = 0;
        _passiveDefense = 0;
        _passiveLuck = 0;
        _passiveSkillCdr = 0f;
        _passiveActiveItemCdr = 0f;

        if (passive != null)
        {
            foreach (var mod in passive.baseModifiers)
            {
                switch (mod.Type)
                {
                    case StatType.AttackPower:
                        // 범용 AttackPower → Melee + Ranged 동시 적용
                        _passiveMelee  += (int)mod.Value;
                        _passiveRanged += (int)mod.Value;
                        break;
                    case StatType.MeleeAttack:
                        _passiveMelee += (int)mod.Value;
                        break;
                    case StatType.RangedAttack:
                        _passiveRanged += (int)mod.Value;
                        break;
                    case StatType.Defense:
                        _passiveDefense += (int)mod.Value;
                        break;
                    case StatType.MaxHp:
                        MaxHp = Mathf.Max(1, MaxHp + (int)mod.Value);
                        Hp = Mathf.Min(Hp, MaxHp);
                        break;
                    case StatType.Luck:
                        _passiveLuck += (int)mod.Value;
                        break;
                    case StatType.SkillCooldownReduction:
                        _passiveSkillCdr += mod.Value;
                        break;
                    case StatType.ActiveItemCooldownReduction:
                        _passiveActiveItemCdr += mod.Value;
                        break;
                }
            }
        }

        Recalculate();
    }

    // ── 아이템 누적 ──────────────────────────────────────────────────────────────

    /// <summary>RunItemInventory.OnInventoryChanged 이벤트에 연결.</summary>
    public void RefreshItemBonuses(RunItemInventory inventory)
    {
        if (inventory == null)
        {
            _itemMelee = _itemRanged = _itemDefense = _itemLuck = 0;
            _itemSkillCdr = _itemActiveItemCdr = 0f;
        }
        else
        {
            _itemMelee = _itemRanged = _itemDefense = _itemLuck = 0;
            _itemSkillCdr = _itemActiveItemCdr = 0f;

            foreach (var item in inventory.Items)
            {
                if (item.effects == null) continue;
                foreach (var eff in item.effects)
                {
                    if (string.IsNullOrEmpty(eff.effectType) || eff.trigger != "Always") continue;
                    switch (eff.effectType)
                    {
                        case "MeleeAttack":  _itemMelee  += (int)eff.value; break;
                        case "RangedAttack": _itemRanged += (int)eff.value; break;
                        case "AttackPower":
                            _itemMelee  += (int)eff.value;
                            _itemRanged += (int)eff.value;
                            break;
                        case "Defense":      _itemDefense += (int)eff.value; break;
                        case "Luck":         _itemLuck    += (int)eff.value; break;
                        case "SkillCooldownReduction":      _itemSkillCdr += eff.value; break;
                        case "ActiveItemCooldownReduction": _itemActiveItemCdr += eff.value; break;
                    }
                }
            }
        }

        Recalculate();
    }

    // ── 방 버프 ──────────────────────────────────────────────────────────────────

    /// <summary>RoomBuffHandler.OnBuffsChanged 이벤트에 연결.</summary>
    public void RefreshRoomBuffs(RoomBuffHandler handler)
    {
        if (handler == null)
        {
            _roomMelee = _roomRanged = _roomDefense = 0;
        }
        else
        {
            _roomMelee   = (int)(handler.GetTotal(StatType.MeleeAttack) + handler.GetTotal(StatType.AttackPower));
            _roomRanged  = (int)(handler.GetTotal(StatType.RangedAttack) + handler.GetTotal(StatType.AttackPower));
            _roomDefense = (int)handler.GetTotal(StatType.Defense);
        }

        Recalculate();
    }

    // ── 공격 속도 (패시브/특성에서 직접 조작) ────────────────────────────────────

    /// <summary>패시브 등에서 공격 속도 보너스를 직접 설정 (0.0 = 0%, 0.25 = +25%)</summary>
    public void SetBonusAttackSpeed(float bonus)
    {
        _bonusAttackSpeed = bonus;
        Recalculate();
    }

    // ── 내부 재계산 ──────────────────────────────────────────────────────────────

    private void Recalculate()
    {
        // 조건부 시너지 누적
        int condMelee = 0, condRanged = 0;
        float condAttackSpeed = 0f;

        foreach (var s in _conditionalSynergies)
        {
            float multiplier = 0f;

            if (s.trigger == "OnHit" && s.currentStacks > 0)
                multiplier = s.currentStacks;
            else if (s.trigger == "OnLowHp" && s.isActive)
                multiplier = 1f;

            if (multiplier <= 0f) continue;

            switch (s.effectType)
            {
                case "AttackPower":
                    condMelee  += (int)(s.value * multiplier);
                    condRanged += (int)(s.value * multiplier);
                    break;
                case "MeleeAttack":  condMelee  += (int)(s.value * multiplier); break;
                case "RangedAttack": condRanged += (int)(s.value * multiplier); break;
                case "AttackSpeed":  condAttackSpeed += s.value * multiplier; break;
            }
        }

        MeleeAttack  = Mathf.Max(0, _baseMelee  + _passiveMelee  + _weaponMelee  + _itemMelee  + _roomMelee  + _synergyMelee + condMelee);
        RangedAttack = Mathf.Max(0, _baseRanged + _passiveRanged + _weaponRanged + _itemRanged + _roomRanged + _synergyRanged + condRanged);
        Defense      = Mathf.Max(0, _baseDefense + _passiveDefense + _weaponDefense + _itemDefense + _roomDefense + _synergyDefense);
        Luck         = Mathf.Max(0, _baseLuck + _passiveLuck + _itemLuck + _synergyLuck);

        AttackSpeedMultiplier = Mathf.Max(0.1f, 1f + _bonusAttackSpeed + _synergyAttackSpeed + condAttackSpeed);
        SkillCooldownReduction = Mathf.Clamp01(_passiveSkillCdr + _itemSkillCdr + _synergySkillCdr);
        ActiveItemCooldownReduction = Mathf.Clamp01(_passiveActiveItemCdr + _itemActiveItemCdr + _synergyActiveItemCdr);

        OnChanged?.Invoke();
    }

    // ── 시너지 ──────────────────────────────────────────────────────────────────

    /// <summary>Always 트리거 시너지 — 즉시 영구 스탯 적용.</summary>
    public void ApplySynergyEffect(string effectType, float value)
    {
        switch (effectType)
        {
            case "MeleeAttack":   _synergyMelee  += (int)value; break;
            case "RangedAttack":  _synergyRanged += (int)value; break;
            case "AttackPower":
                _synergyMelee  += (int)value;
                _synergyRanged += (int)value;
                break;
            case "Defense":       _synergyDefense += (int)value; break;
            case "MaxHp":
                _synergyMaxHp += (int)value;
                MaxHp = Mathf.Max(1, MaxHp + (int)value);
                Hp = Mathf.Min(Hp, MaxHp);
                break;
            case "Luck":          _synergyLuck += (int)value; break;
            case "AttackSpeed":   _synergyAttackSpeed += value; break;
            case "Lifesteal":     _synergyLifesteal += value; break;
            case "MoveSpeed":     break; // TODO: 이동속도 레이어 추가 시
            case "SkillCooldownReduction":       _synergySkillCdr += value; break;
            case "ActiveItemCooldownReduction":  _synergyActiveItemCdr += value; break;
        }

        Recalculate();
    }

    /// <summary>조건부 시너지 등록 (OnHit, OnLowHp 등).</summary>
    public void RegisterConditionalSynergy(ConditionalSynergy synergy)
    {
        if (synergy == null) return;
        _conditionalSynergies.Add(synergy);
    }

    /// <summary>흡혈 비율 (OnHit Lifesteal 포함).</summary>
    public float LifestealRate => _synergyLifesteal;

    /// <summary>조건부 시너지 목록 (읽기 전용).</summary>
    public System.Collections.Generic.IReadOnlyList<ConditionalSynergy> ConditionalSynergies => _conditionalSynergies;

    /// <summary>OnHit 트리거 발동 — 공격 적중 시 호출.</summary>
    public void TriggerOnHit()
    {
        foreach (var s in _conditionalSynergies)
        {
            if (s.trigger != "OnHit") continue;
            s.currentStacks = Mathf.Min(s.currentStacks + 1, s.maxStack > 0 ? s.maxStack : 1);
            s.remainingDuration = s.duration;
        }

        Recalculate();
    }

    /// <summary>OnLowHp 체크 — HP 비율 기반 조건부 효과 활성화.</summary>
    public void CheckOnLowHp()
    {
        float hpRatio = MaxHp > 0 ? (float)Hp / MaxHp : 1f;

        foreach (var s in _conditionalSynergies)
        {
            if (s.trigger != "OnLowHp") continue;
            s.isActive = hpRatio <= s.threshold;
        }

        Recalculate();
    }

    /// <summary>조건부 시너지 시간 경과 — Update에서 호출.</summary>
    public void TickConditionalSynergies(float deltaTime)
    {
        bool changed = false;
        foreach (var s in _conditionalSynergies)
        {
            if (s.trigger != "OnHit" || s.currentStacks <= 0) continue;
            if (s.duration <= 0f) continue;

            s.remainingDuration -= deltaTime;
            if (s.remainingDuration <= 0f)
            {
                s.currentStacks = 0;
                s.remainingDuration = 0f;
                changed = true;
            }
        }

        if (changed) Recalculate();
    }

    /// <summary>모든 시너지 효과 초기화.</summary>
    public void ClearSynergyEffects()
    {
        // MaxHp 복원 (초기화 전에 처리)
        if (_synergyMaxHp != 0)
        {
            MaxHp = Mathf.Max(1, MaxHp - _synergyMaxHp);
            Hp = Mathf.Min(Hp, MaxHp);
        }

        _synergyMelee = _synergyRanged = _synergyDefense = _synergyLuck = _synergyMaxHp = 0;
        _synergySkillCdr = _synergyActiveItemCdr = _synergyAttackSpeed = 0f;
        _synergyLifesteal = 0f;
        _conditionalSynergies.Clear();

        Recalculate();
    }
}
