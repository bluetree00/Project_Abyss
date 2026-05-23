using System;
using System.Collections.Generic;
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
    public float MoveSpeedMultiplier { get; private set; } = 1f;
    public int BonusProjectile { get; private set; }

    // ── 아이템 확장 스탯 (AccumulatedStats 기반) ─────────────────────────────────
    // 전투
    public float RollCooldownBonus { get; private set; }
    public float RollDistanceBonus { get; private set; }
    public float RangedRangeBonus { get; private set; }
    public float HealingReceivedBonus { get; private set; }
    public float DebuffResistance { get; private set; }
    public float AllDamagePercent { get; private set; }
    public float DamageReduction { get; private set; }
    public float ItemLifesteal { get; private set; }
    // 시스템
    public float AllElementBonus { get; private set; }
    public float SpecialRoomChance { get; private set; }
    public float HighGradeItemChance { get; private set; }
    public int ConsumableSlotBonus { get; private set; }
    public float DebuffDurationBonus { get; private set; }

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
        _roomMoveSpeed = 0f;
        _roomAttackSpeed = 0f;
        _roomProjectile = 0;

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
        _roomMoveSpeed = 0f; _roomAttackSpeed = 0f; _roomProjectile = 0;
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

    /// <summary>
    /// 최대 체력 영구 감소. 현재 HP가 새 MaxHP를 초과하면 같이 내려간다.
    /// MaxHp는 최소 1로 클램프.
    /// </summary>
    public void DecreaseMaxHp(int amount)
    {
        if (amount <= 0) return;
        MaxHp = Mathf.Max(1, MaxHp - amount);
        if (Hp > MaxHp) Hp = MaxHp;
        OnChanged?.Invoke();
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
    private int _itemMaxHp;
    private float _itemMoveSpeed;
    private float _itemAttackSpeed;
    private float _itemAllDamagePercent;
    private float _itemAllStatsPercent;
    private float _itemRollCooldown;
    private float _itemRollDistance;
    private float _itemRangedRange;
    private float _itemHealingReceived;
    private float _itemDebuffResistance;
    private float _itemDamageReduction;
    private float _itemLifesteal;
    private float _itemAllElementBonus;
    private float _itemSpecialRoomChance;
    private float _itemHighGradeItemChance;
    private int   _itemConsumableSlotBonus;
    private float _itemDebuffDuration;

    // -- Room Buff --
    private int _roomMelee;
    private int _roomRanged;
    private int _roomDefense;
    private float _roomMoveSpeed;      // 퍼센트 가산 (0.1 = +10%)
    private float _roomAttackSpeed;    // 퍼센트 가산
    private int _roomProjectile;       // 투사체 가산

    // -- Covenant (서약 시스템) --
    private int   _covenantMelee;
    private int   _covenantRanged;
    private int   _covenantDefense;
    private float _covenantMoveSpeed;   // 퍼센트 가산
    private float _covenantAttackSpeed; // 퍼센트 가산

    // -- Grid Synergy (Always) --
    private int _synergyMelee;
    private int _synergyRanged;
    private int _synergyDefense;
    private int _synergyLuck;
    private int _synergyMaxHp;
    private float _synergySkillCdr;
    private float _synergyActiveItemCdr;
    private float _synergyAttackSpeed;

    // -- Relic Awakening (영구 성장 레이어) --
    private int   _awakeningMelee;
    private int   _awakeningRanged;
    private int   _awakeningDefense;
    private int   _awakeningMaxHp;
    private float _awakeningMoveSpeed;
    private float _awakeningSkillCdr;
    private int   _awakeningLuck;
    private float _awakeningLifesteal;

    // -- Grid Synergy (조건부) --
    private float _synergyLifesteal;
    private readonly System.Collections.Generic.List<ConditionalSynergy> _conditionalSynergies = new();

    // -- 공격 속도 보너스 (패시브 등에서 직접 설정) --
    private float _bonusAttackSpeed;

    // -- Character Mechanic (고유 메커닉 배율 — HolyGauge 만충, SolarTimer 강화 등) --
    private float _characterMeleeMult  = 1f;
    private float _characterRangedMult = 1f;
    private float _characterDefenseMult = 1f;
    private float _characterDamageReduction = 0f;

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

    /// <summary>
    /// RunItemInventory.OnInventoryChanged 이벤트에 연결.
    /// ItemEffectManager.GetAccumulatedStats() 경유.
    /// </summary>
    /// <summary>
    /// RunItemInventory.OnInventoryChanged 이벤트에 연결.
    /// ItemEffectManager가 있으면 새 구조, 없으면 레거시 폴백.
    /// </summary>
    public void RefreshItemBonuses(RunItemInventory inventory)
    {
        _itemMelee = _itemRanged = _itemDefense = _itemLuck = 0;
        _itemSkillCdr = _itemActiveItemCdr = 0f;
        _itemMaxHp = 0; _itemMoveSpeed = 0f; _itemAttackSpeed = 0f;
        _itemAllDamagePercent = 0f; _itemAllStatsPercent = 0f; _itemRollCooldown = 0f; _itemRollDistance = 0f;
        _itemRangedRange = 0f; _itemHealingReceived = 0f; _itemDebuffResistance = 0f;
        _itemDamageReduction = 0f; _itemLifesteal = 0f; _itemAllElementBonus = 0f;
        _itemSpecialRoomChance = 0f; _itemHighGradeItemChance = 0f;
        _itemConsumableSlotBonus = 0; _itemDebuffDuration = 0f;

        // ItemEffectManager 경유
        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;
        if (mgr == null || mgr.ActiveEffects.Count == 0)
        {
            Recalculate();
            return;
        }

        var acc = mgr.GetAccumulatedStats();

        _itemMelee   = acc.MeleeDamage + (int)acc.AllDamageFlat;
        _itemRanged  = acc.RangedDamage + (int)acc.AllDamageFlat;
        _itemDefense = acc.Defense;
        _itemLuck    = acc.Luck;
        _itemSkillCdr = acc.SkillCooldownReduction;
        _itemActiveItemCdr = acc.ActiveItemCooldownReduction;
        _itemMaxHp = acc.MaxHP;
        _itemMoveSpeed = acc.MoveSpeed;
        _itemAttackSpeed = acc.AttackSpeed;
        _itemAllDamagePercent = acc.AllDamagePercent + acc.AllStatsPercent;
        _itemAllStatsPercent = acc.AllStatsPercent;
        _itemRollCooldown = acc.RollCooldown;
        _itemRollDistance = acc.RollDistance;
        _itemRangedRange = acc.RangedRange;
        _itemHealingReceived = acc.HealingReceived;
        _itemDebuffResistance = acc.DebuffResistance;
        _itemDamageReduction = acc.DamageReduction;
        _itemLifesteal = acc.Lifesteal;
        _itemAllElementBonus = acc.AllElementBonus;
        _itemSpecialRoomChance = acc.SpecialRoomChance;
        _itemHighGradeItemChance = acc.HighGradeItemChance;
        _itemConsumableSlotBonus = acc.ConsumableSlotBonus;
        _itemDebuffDuration = acc.DebuffDuration;

        Recalculate();
    }

    // ── 방 버프 ──────────────────────────────────────────────────────────────────

    /// <summary>RoomBuffHandler.OnBuffsChanged 이벤트에 연결.</summary>
    public void RefreshRoomBuffs(RoomBuffHandler handler)
    {
        if (handler == null)
        {
            _roomMelee = _roomRanged = _roomDefense = 0;
            _roomMoveSpeed = _roomAttackSpeed = 0f;
            _roomProjectile = 0;
        }
        else
        {
            // Flat 가산
            float flatMelee  = handler.GetFlatTotal(StatType.MeleeAttack) + handler.GetFlatTotal(StatType.AttackPower);
            float flatRanged = handler.GetFlatTotal(StatType.RangedAttack) + handler.GetFlatTotal(StatType.AttackPower);
            float flatDef    = handler.GetFlatTotal(StatType.Defense);

            // Percent 가산 (기본 스탯 기준)
            float pctMelee  = handler.GetPercentTotal(StatType.MeleeAttack) + handler.GetPercentTotal(StatType.AttackPower);
            float pctRanged = handler.GetPercentTotal(StatType.RangedAttack) + handler.GetPercentTotal(StatType.AttackPower);
            float pctDef    = handler.GetPercentTotal(StatType.Defense);

            _roomMelee   = (int)(flatMelee  + _baseMelee  * pctMelee);
            _roomRanged  = (int)(flatRanged + _baseRanged * pctRanged);
            _roomDefense = (int)(flatDef    + _baseDefense * pctDef);

            // MoveSpeed, AttackSpeed — 퍼센트만 (이동/공속은 퍼센트 기반)
            _roomMoveSpeed   = handler.GetPercentTotal(StatType.MoveSpeed);
            _roomAttackSpeed = handler.GetPercentTotal(StatType.AttackSpeed);

            // Projectile — Flat 가산만
            _roomProjectile = (int)handler.GetFlatTotal(StatType.Projectile);
        }

        Recalculate();
    }

    // ── 유물의 각성 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 런 시작 시 영구 각성 보너스를 적용한다.
    /// BackendGameData.Data와 Managers.RelicAwakening이 준비된 후 호출.
    /// </summary>
    public void RefreshAwakening()
    {
        _awakeningMelee     = 0;
        _awakeningRanged    = 0;
        _awakeningDefense   = 0;
        _awakeningMaxHp     = 0;
        _awakeningMoveSpeed = 0f;
        _awakeningSkillCdr  = 0f;
        _awakeningLuck      = 0;
        _awakeningLifesteal = 0f;

        var userData = BackendGameData.Instance?.Data;
        var mgr      = Managers.RelicAwakening;
        if (userData == null || mgr == null || !mgr.IsInitialized)
        {
            Recalculate();
            return;
        }

        foreach (var cat in AwakeningCategory.All)
        {
            int level = userData.GetAwakeningLevel(cat);
            if (level <= 0) continue;

            var entries = mgr.GetEntriesUpToLevel(cat, level);
            foreach (var e in entries)
                ApplyAwakeningEntry(e);
        }

        if (_awakeningMaxHp != 0)
        {
            MaxHp = Mathf.Max(1, MaxHp + _awakeningMaxHp);
            Hp    = Mathf.Min(Hp, MaxHp);
        }

        Recalculate();
    }

    private void ApplyAwakeningEntry(RelicAwakeningEntry e)
    {
        switch (e.stat_type)
        {
            case "AttackPower":
                _awakeningMelee  += (int)e.value;
                _awakeningRanged += (int)e.value;
                break;
            case "MeleeAttack":              _awakeningMelee     += (int)e.value; break;
            case "RangedAttack":             _awakeningRanged    += (int)e.value; break;
            case "Defense":                  _awakeningDefense   += (int)e.value; break;
            case "MaxHp":                    _awakeningMaxHp     += (int)e.value; break;
            case "MoveSpeed":                _awakeningMoveSpeed += e.value;      break;
            case "SkillCooldownReduction":   _awakeningSkillCdr  += e.value;      break;
            case "Luck":                     _awakeningLuck      += (int)e.value; break;
            case "Lifesteal":                _awakeningLifesteal += e.value;      break;
        }
    }

    /// <summary>서약 핸들러에서 스탯 기여를 읽어 레이어를 갱신한다.</summary>
    public void RefreshCovenants(CovenantHandler handler)
    {
        if (handler == null)
        {
            _covenantMelee = _covenantRanged = _covenantDefense = 0;
            _covenantMoveSpeed = _covenantAttackSpeed = 0f;
        }
        else
        {
            _covenantMelee   = 0;
            _covenantRanged  = 0;
            _covenantDefense = 0;
            _covenantMoveSpeed   = 0f;
            _covenantAttackSpeed = 0f;

            foreach (var mod in handler.GetAllModifiers())
            {
                switch (mod.Type)
                {
                    case StatType.AttackPower:
                        _covenantMelee  += (int)mod.Value;
                        _covenantRanged += (int)mod.Value;
                        break;
                    case StatType.MeleeAttack:   _covenantMelee   += (int)mod.Value; break;
                    case StatType.RangedAttack:  _covenantRanged  += (int)mod.Value; break;
                    case StatType.Defense:        _covenantDefense += (int)mod.Value; break;
                    case StatType.MoveSpeed:      _covenantMoveSpeed   += mod.Value;  break;
                    case StatType.AttackSpeed:    _covenantAttackSpeed += mod.Value;  break;
                }
            }
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

        // % 보너스 배율
        float dmgMul  = (1f + _itemAllDamagePercent) * _characterMeleeMult;
        float dmgMulR = (1f + _itemAllDamagePercent) * _characterRangedMult;
        float defMul  = (1f + _itemAllStatsPercent) * _characterDefenseMult;
        float luckMul = 1f + _itemAllStatsPercent;

        int baseMeleeSum  = _baseMelee  + _passiveMelee  + _weaponMelee  + _itemMelee  + _roomMelee  + _covenantMelee  + _synergyMelee  + _awakeningMelee  + condMelee;
        int baseRangedSum = _baseRanged + _passiveRanged + _weaponRanged + _itemRanged + _roomRanged + _covenantRanged + _synergyRanged + _awakeningRanged + condRanged;
        int baseDefSum    = _baseDefense + _passiveDefense + _weaponDefense + _itemDefense + _roomDefense + _covenantDefense + _synergyDefense + _awakeningDefense;
        int baseLuckSum   = _baseLuck + _passiveLuck + _itemLuck + _synergyLuck + _awakeningLuck;

        MeleeAttack  = Mathf.Max(0, Mathf.RoundToInt(baseMeleeSum * dmgMul));
        RangedAttack = Mathf.Max(0, Mathf.RoundToInt(baseRangedSum * dmgMulR));
        Defense      = Mathf.Max(0, Mathf.RoundToInt(baseDefSum * defMul));
        Luck         = Mathf.Max(0, Mathf.RoundToInt(baseLuckSum * luckMul));

        // MaxHp 아이템 보너스
        if (_itemMaxHp != 0)
        {
            int newMax = Mathf.Max(1, MaxHp + _itemMaxHp);
            if (newMax != MaxHp)
            {
                MaxHp = newMax;
                Hp = Mathf.Min(Hp, MaxHp);
            }
        }

        AttackSpeedMultiplier = Mathf.Max(0.1f, 1f + _bonusAttackSpeed + _synergyAttackSpeed + _roomAttackSpeed + _covenantAttackSpeed + _itemAttackSpeed + condAttackSpeed);
        MoveSpeedMultiplier  = Mathf.Max(0.1f, 1f + _roomMoveSpeed + _covenantMoveSpeed + _itemMoveSpeed + _awakeningMoveSpeed);
        BonusProjectile      = Mathf.Max(0, _roomProjectile);
        SkillCooldownReduction = Mathf.Clamp01(_passiveSkillCdr + _itemSkillCdr + _synergySkillCdr + _awakeningSkillCdr);
        ActiveItemCooldownReduction = Mathf.Clamp01(_passiveActiveItemCdr + _itemActiveItemCdr + _synergyActiveItemCdr);

        // 확장 스탯 공개 프로퍼티 갱신
        RollCooldownBonus   = _itemRollCooldown;
        RollDistanceBonus   = _itemRollDistance;
        RangedRangeBonus    = _itemRangedRange;
        HealingReceivedBonus = _itemHealingReceived;
        DebuffResistance    = _itemDebuffResistance;
        AllDamagePercent    = _itemAllDamagePercent;
        DamageReduction     = Mathf.Clamp01(_itemDamageReduction + _characterDamageReduction);
        ItemLifesteal       = _itemLifesteal + LifestealRate + _awakeningLifesteal;

        // 시스템 스탯
        AllElementBonus     = _itemAllElementBonus;
        SpecialRoomChance   = _itemSpecialRoomChance;
        HighGradeItemChance = _itemHighGradeItemChance;
        ConsumableSlotBonus = _itemConsumableSlotBonus;
        DebuffDurationBonus = _itemDebuffDuration;

        OnChanged?.Invoke();
    }

    // ── 캐릭터 메커닉 배율 ─────────────────────────────────────────────────────

    /// <summary>
    /// 캐릭터 고유 메커닉(HolyGauge 만충, SolarTimer 강화 등)의 공격 배율을 설정한다.
    /// meleeMult=1f, rangedMult=1f 이 기본값(배율 없음).
    /// </summary>
    public void SetCharacterAttackMultiplier(float meleeMult, float rangedMult)
    {
        _characterMeleeMult  = Mathf.Max(0f, meleeMult);
        _characterRangedMult = Mathf.Max(0f, rangedMult);
        Recalculate();
    }

    /// <summary>방어 배율 및 피해 감소를 캐릭터 메커닉에서 설정한다.</summary>
    public void SetCharacterDefenseBonus(float defenseMult, float damageReduction)
    {
        _characterDefenseMult   = Mathf.Max(0f, defenseMult);
        _characterDamageReduction = Mathf.Clamp01(damageReduction);
        Recalculate();
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

    /// <summary>
    /// 시너지 레코드 목록으로부터 전체 재계산.
    /// ClearSynergyEffects() 후 순회 적용하므로 항상 정확한 상태.
    /// 씬 전환 후 BindPlayer 시점에서 호출.
    /// </summary>
    public void RestoreSynergies(System.Collections.Generic.IReadOnlyList<SynergyRecord> records)
    {
        ClearSynergyEffects();

        if (records == null) return;

        foreach (var r in records)
        {
            if (string.IsNullOrEmpty(r.effectType)) continue;

            switch (r.trigger)
            {
                case "Always":
                    ApplySynergyEffect(r.effectType, r.value);
                    break;
                case "OnHit":
                    RegisterConditionalSynergy(new ConditionalSynergy
                    {
                        gridId     = r.gridId,
                        effectType = r.effectType,
                        trigger    = "OnHit",
                        value      = r.value,
                        maxStack   = r.maxStack > 0 ? r.maxStack : 1,
                        duration   = r.duration,
                    });
                    break;
                case "OnLowHp":
                    RegisterConditionalSynergy(new ConditionalSynergy
                    {
                        gridId     = r.gridId,
                        effectType = r.effectType,
                        trigger    = "OnLowHp",
                        value      = r.value,
                        threshold  = r.value2 > 0f ? r.value2 : 0.3f,
                    });
                    break;
            }
        }
    }
}
