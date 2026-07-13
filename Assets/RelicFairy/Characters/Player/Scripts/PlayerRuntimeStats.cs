using System;
using System.Collections.Generic;
using UnityEngine;
using RelicFairy;

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
    /// <summary>스킬 전용 피해 % 보너스 합(정적 SkillDamage 아이템 + 동적 스킬피해).
    /// allDamage는 공격스탯(dmgMul)에 이미 반영돼 스킬 base에 들어가므로 제외 — 안 그러면 ColliderInstance에서 이중곱.</summary>
    public float SkillDamageBonus => _itemSkillDamage + _itemDyn.skillDamage;
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

    // 포이즈(아머치) 최대치 — 누적 임팩트를 이만큼 버틴다. 넘으면 날아감 발동.
    // 베이스(캐릭터) + 보너스(유물 패시브 / 룬 시너지 등 effect_type "MaxPoise").
    private const float DefaultBasePoise = 100f;
    private float _basePoise  = DefaultBasePoise;
    private float _poiseBonus;
    public float MaxPoise => Mathf.Max(1f, _basePoise + _poiseBonus);

    // 스태미너 최대치 — 대시 자원. 베이스(캐릭터) + 보너스(effect_type "MaxStamina").
    private const float DefaultBaseStamina = 100f;
    private float _baseStamina  = DefaultBaseStamina;
    private float _staminaBonus;
    public float MaxStamina => Mathf.Max(1f, _baseStamina + _staminaBonus);

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
        _maxHpItemContribution = 0;   // 베이스 재설정 — 아이템 MaxHp 기여 스냅샷 초기화

        _basePoise  = data.basePoise > 0f ? data.basePoise : DefaultBasePoise;
        _poiseBonus = 0f;

        _baseStamina  = data.maxStamina > 0f ? data.maxStamina : DefaultBaseStamina;
        _staminaBonus = 0f;

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
        _maxHpItemContribution = 0;   // 베이스 재설정 — 아이템 MaxHp 기여 스냅샷 초기화

        // CHARACTER_DATA CSV에 포이즈/스태미너 컬럼이 아직 없어 기본치 사용(추가 시 entry에서 읽도록 교체).
        _basePoise    = DefaultBasePoise;
        _poiseBonus   = 0f;
        _baseStamina  = DefaultBaseStamina;
        _staminaBonus = 0f;

        _baseMelee   = Mathf.Max(0, entry.base_melee_attack);
        _baseRanged  = Mathf.Max(0, entry.base_ranged_attack);
        _baseDefense = Mathf.Max(0, entry.base_defense);
        _baseLuck    = Mathf.Max(0, entry.base_luck);

        // 유물=캐릭터 베이스 크릿(CHARACTER_DATA 행). 무기 크릿 위에 가산.
        _relicCritChance = entry.crit_chance;
        _relicCritDamage = entry.crit_damage;

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
                    case "MaxPoise":     _poiseBonus   += p.value; break;
                    case "MaxStamina":   _staminaBonus += p.value; break;
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

    // ── 실드(보호막) ──────────────────────────────────────────────────────────────
    // 상태는 SynergyMechanics.ShieldCurrentValue 재사용(룬 ShieldAccumulate/Burst와 공유). HP 차감 전 흡수.

    /// <summary>현재 실드값(표시용 정수).</summary>
    public int Shield => Mathf.RoundToInt(_synergyMechanics.ShieldCurrentValue);
    /// <summary>실드 보유 여부(아이템 보호막 조건·HasShield).</summary>
    public bool HasShield => _synergyMechanics.ShieldCurrentValue > 0f;
    /// <summary>실드 상한(최대 HP × CapRatio).</summary>
    public int ShieldCap => Mathf.Max(0, Mathf.RoundToInt(MaxHp * Mathf.Clamp01(_synergyMechanics.ShieldCapRatio)));

    /// <summary>실드 부여(상한 클램프). 음수 무시.</summary>
    public void AddShield(float amount)
    {
        if (amount <= 0f) return;
        float cap = ShieldCap;
        float v = Mathf.Clamp(_synergyMechanics.ShieldCurrentValue + amount, 0f, cap);
        if (Mathf.Approximately(v, _synergyMechanics.ShieldCurrentValue)) return;
        _synergyMechanics.ShieldCurrentValue = v;
        OnChanged?.Invoke();
    }

    /// <summary>실드로 피해를 먼저 흡수하고 HP로 넘길 잔여 피해를 반환.</summary>
    public int AbsorbWithShield(int damage)
    {
        float s = _synergyMechanics.ShieldCurrentValue;
        if (s <= 0f || damage <= 0) return damage;

        if (s >= damage)
        {
            _synergyMechanics.ShieldCurrentValue = s - damage;
            OnChanged?.Invoke();
            return 0;
        }
        _synergyMechanics.ShieldCurrentValue = 0f;
        OnChanged?.Invoke();
        return damage - Mathf.CeilToInt(s);
    }

    /// <summary>ShieldAccumulate(룬 방어 시너지) 활성 시 피격 피해의 일부를 실드로 축적.</summary>
    public void AccumulateShieldFromDamage(int incomingDamage)
    {
        if (!_synergyMechanics.ShieldAccumulateEnabled || incomingDamage <= 0) return;
        AddShield(incomingDamage * Mathf.Max(0f, _synergyMechanics.ShieldAccumulateRate));
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
    // 아이템 MaxHp 기여(flat + 동적 %)의 현재 적용분. 멱등 재계산용 — 매 Recalculate에서 제거 후 재적용.
    private int   _maxHpItemContribution;
    private float _itemAllDamagePercent;
    private float _itemSkillDamage;        // 스킬 피해 % (acc.SkillDamagePercent). 소비처: SkillDamageBonus 프로퍼티
    private float _itemCritChance;         // 정적 치명타 확률 %포인트
    private float _itemCritDamage;         // 정적 치명타 피해 배율 가산
    private float _itemDefensePercent;     // 정적 방어력 %
    private float _itemMaxHpPercent;       // 정적 최대 HP %
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

    // -- Relic (유물 클래스 스탯, 런 시작 시 1회) --
    private int   _relicMelee;
    private int   _relicRanged;
    private int   _relicDefense;
    private int   _relicLuck;
    private int   _relicMaxHp;
    private float _relicMoveSpeed;    // 퍼센트 가산
    private float _relicAttackSpeed;  // 퍼센트 가산
    private float _relicSkillCdr;
    private float _relicCritChance;   // 유물 클래스 크릿 확률 보너스(%포인트)
    private float _relicCritDamage;   // 유물 크릿 피해 배율 보너스(가산, 0.2=+20%)
    private float _buffCritChance;    // 일시 크릿 버프(예: 가웨인 정오 구간). 리소스가 토글.
    private float _buffCritDamage;

    /// <summary>치명타 확률 보너스 합(%포인트). 무기 크릿 위에 가산. CombatCalculator.RollCrit이 읽음.</summary>
    public float CritChanceBonus => _relicCritChance + _buffCritChance + _synergyDynCritChance + _itemDyn.critChance + _itemCritChance;
    /// <summary>치명타 피해 배율 보너스 합(가산). 무기 크릿 배율 위에 가산.</summary>
    public float CritDamageBonus => _relicCritDamage + _buffCritDamage + _synergyDynCritDamage + _itemDyn.critDamage + _itemCritDamage;

    /// <summary>유물 일시 크릿 버프 설정(가웨인 정오 등). chance=%포인트, damage=배율 가산. (0,0)=해제.</summary>
    public void SetRelicCritBuff(float chanceBonus, float damageBonus)
    {
        _buffCritChance = chanceBonus;
        _buffCritDamage = damageBonus;
    }

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

    // -- Synergy Mechanics (행동 역학 플래그) --
    private readonly SynergyMechanicsState _synergyMechanics = new();

    /// <summary>행동 역학 시너지 플래그. 전투·스킬·이동 시스템이 읽는다.</summary>
    public SynergyMechanicsState SynergyMechanics => _synergyMechanics;

    // -- 공격 속도 보너스 (패시브 등에서 직접 설정) --
    private float _bonusAttackSpeed;

    // -- Synergy Dynamic (룬 속성 효과의 런타임 동적 버프, 6속성 공용) --
    // 기존 단일 슬롯(SetRelicCritBuff/SetCharacterAttackMultiplier 등 유물 점유)과 충돌하지 않도록
    // 전부 가산 전용 별도 레이어로 둔다. 전기=공속, 어둠=공격%/받피, 빛=치확/치피.
    private float _synergyDynAttackSpeed;
    private float _synergyDynAttackPct;
    private float _synergyDynCritChance;
    private float _synergyDynCritDamage;
    private float _synergyDynDamageReduction;

    // -- Item Dynamic (조건부/타임드 아이템 효과의 런타임 동적 버프, 가산 합산) --
    // ItemEffectManager.OnTick이 매 프레임 ApplyItemDynamicStats로 갱신. 정적 아이템 스탯과 분리.
    private ItemDynamicStats _itemDyn;

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
        _itemAllDamagePercent = 0f; _itemSkillDamage = 0f; _itemAllStatsPercent = 0f; _itemRollCooldown = 0f; _itemRollDistance = 0f;
        _itemCritChance = 0f; _itemCritDamage = 0f; _itemDefensePercent = 0f; _itemMaxHpPercent = 0f;
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
        _itemSkillDamage = acc.SkillDamagePercent;   // 그동안 버려지던 스킬피해 % 연결
        _itemCritChance = acc.CritChancePercent;
        _itemCritDamage = acc.CritDamagePercent;
        _itemDefensePercent = acc.DefensePercent;
        _itemMaxHpPercent = acc.MaxHPPercent;
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

    // ── 유물 클래스 스탯 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 유물 클래스 스탯을 공통 베이스 위에 가산한다. 재호출 시 이전 기여를 reset 후 재적용(멱등).
    /// </summary>
    public void ApplyRelicStats(System.Collections.Generic.IReadOnlyList<StatModifier> mods)
    {
        // MaxHp는 합산 레이어가 아니므로 이전 기여분을 먼저 되돌린다.
        if (_relicMaxHp != 0)
        {
            MaxHp = Mathf.Max(1, MaxHp - _relicMaxHp);
            Hp = Mathf.Min(Hp, MaxHp);
        }

        _relicMelee = _relicRanged = _relicDefense = _relicLuck = _relicMaxHp = 0;
        _relicMoveSpeed = _relicAttackSpeed = _relicSkillCdr = 0f;
        _relicCritChance = _relicCritDamage = 0f;

        if (mods != null)
        {
            foreach (var mod in mods)
            {
                switch (mod.Type)
                {
                    case StatType.AttackPower:
                        _relicMelee  += (int)mod.Value;
                        _relicRanged += (int)mod.Value;
                        break;
                    case StatType.MeleeAttack:  _relicMelee   += (int)mod.Value; break;
                    case StatType.RangedAttack: _relicRanged  += (int)mod.Value; break;
                    case StatType.Defense:      _relicDefense += (int)mod.Value; break;
                    case StatType.MaxHp:        _relicMaxHp   += (int)mod.Value; break;
                    case StatType.Luck:         _relicLuck    += (int)mod.Value; break;
                    case StatType.MoveSpeed:    _relicMoveSpeed   += mod.Value;  break;
                    case StatType.AttackSpeed:  _relicAttackSpeed += mod.Value;  break;
                    case StatType.SkillCooldownReduction: _relicSkillCdr += mod.Value; break;
                    case StatType.CritChance:   _relicCritChance += mod.Value; break;
                    case StatType.CritDamage:   _relicCritDamage += mod.Value; break;
                }
            }
        }

        if (_relicMaxHp != 0)
        {
            MaxHp = Mathf.Max(1, MaxHp + _relicMaxHp);
            Hp = Mathf.Min(Hp, MaxHp);
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

    /// <summary>유물 일시 이동속도 보너스(퍼센트 가산, -0.2 = -20%). 랜슬롯 빈틈 등. 0 = 해제.</summary>
    public void SetRelicMoveSpeedBonus(float pct)
    {
        _relicMoveSpeed = pct;
        Recalculate();
    }

    // ── 시너지 동적 레이어 (룬 속성 효과 전용) ────────────────────────────────────

    /// <summary>룬 시너지 동적 공격 속도 보너스(가산, 0.18 = +18%). 전기 정전기/감전 등. 매 프레임 갱신될 수 있어 무변동 시 재계산 생략.</summary>
    public void SetSynergyDynamicAttackSpeed(float bonus)
    {
        if (Mathf.Approximately(_synergyDynAttackSpeed, bonus)) return;
        _synergyDynAttackSpeed = bonus;
        Recalculate();
    }

    /// <summary>룬 시너지 동적 공격력 % 보너스(가산, 0.15 = +15%). (향후)어둠 게이지 등.</summary>
    public void SetSynergyDynamicAttackPercent(float pct)
    {
        if (Mathf.Approximately(_synergyDynAttackPct, pct)) return;
        _synergyDynAttackPct = pct;
        Recalculate();
    }

    /// <summary>룬 시너지 동적 치명타 확률 보너스(%포인트, 가산). (향후)빛 광채 등.</summary>
    public void SetSynergyDynamicCritChance(float bonus)
    {
        if (Mathf.Approximately(_synergyDynCritChance, bonus)) return;
        _synergyDynCritChance = bonus;
        OnChanged?.Invoke();   // CritChanceBonus는 라이브 프로퍼티 — 재계산 불필요, UI 갱신만.
    }

    /// <summary>룬 시너지 동적 치명타 피해 보너스(배율 가산). (향후)빛 빛장판 등.</summary>
    public void SetSynergyDynamicCritDamage(float bonus)
    {
        if (Mathf.Approximately(_synergyDynCritDamage, bonus)) return;
        _synergyDynCritDamage = bonus;
        OnChanged?.Invoke();
    }

    /// <summary>룬 시너지 동적 피해 감소(가산, 0.15 = -15% 받는 피해). (향후)어둠 암흑 등.</summary>
    public void SetSynergyDynamicDamageReduction(float reduction)
    {
        if (Mathf.Approximately(_synergyDynDamageReduction, reduction)) return;
        _synergyDynDamageReduction = reduction;
        Recalculate();
    }

    /// <summary>
    /// 아이템 조건부/타임드 동적 스탯 갱신(ItemEffectManager.OnTick이 매 프레임 호출).
    /// 무변동 시 재계산 생략. 크릿은 라이브 프로퍼티라 변동 시 UI 갱신만으로 충분하나, 단순화를 위해 통합 재계산.
    /// </summary>
    public void ApplyItemDynamicStats(in ItemDynamicStats dyn)
    {
        if (dyn.Approximately(_itemDyn)) return;
        _itemDyn = dyn;
        Recalculate();
        OnChanged?.Invoke();
    }

    // ── 1회성 전투 플래그 (아이템 확정크릿 / 방어무시) ─────────────────────────────
    // 다음 플레이어 공격 1타에만 소비되는 one-shot. 스탯 레이어가 아니므로 Recalculate와 무관.
    // 확정크릿은 CombatCalculator.RollCrit, 방어무시는 ColliderInstance.ApplyDamage가 소비한다.
    private bool  _forceNextCrit;
    private float _forceNextCritMult;
    private bool  _penetrateNextHit;

    /// <summary>다음 1타를 강제 크리티컬로 예약(균열의 일격/숨 고르기). critMultiplier=총 배율(0 이하면 무기 크릿 배율).</summary>
    public void ArmForceCrit(float critMultiplier)
    {
        _forceNextCrit = true;
        _forceNextCritMult = critMultiplier;
    }

    /// <summary>강제 크릿 1회 소비. 무장돼 있으면 true + 배율 반환.</summary>
    public bool ConsumeForceCrit(out float critMultiplier)
    {
        critMultiplier = _forceNextCritMult;
        if (!_forceNextCrit) return false;
        _forceNextCrit = false;
        _forceNextCritMult = 0f;
        return true;
    }

    /// <summary>다음 일반공격 1타를 방어무시로 예약(광기의 파동). ColliderInstance가 소비.</summary>
    public void ArmPenetrateNextHit() => _penetrateNextHit = true;

    /// <summary>방어무시 1회 소비. 무장돼 있으면 true.</summary>
    public bool ConsumePenetrateNextHit()
    {
        if (!_penetrateNextHit) return false;
        _penetrateNextHit = false;
        return true;
    }

    /// <summary>방어무시 1타 무장 여부(가이드라인 배지 정리용).</summary>
    public bool PenetrateArmed => _penetrateNextHit;

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
        float dmgMul  = (1f + _itemAllDamagePercent + _synergyDynAttackPct + _itemDyn.attackPercent + _itemDyn.allDamage) * _characterMeleeMult;
        float dmgMulR = (1f + _itemAllDamagePercent + _synergyDynAttackPct + _itemDyn.attackPercent + _itemDyn.allDamage) * _characterRangedMult;
        float defMul  = (1f + _itemAllStatsPercent + _itemDefensePercent + _itemDyn.defensePercent) * _characterDefenseMult;
        float luckMul = 1f + _itemAllStatsPercent;

        int baseMeleeSum  = _baseMelee  + _passiveMelee  + _weaponMelee  + _itemMelee  + _roomMelee  + _covenantMelee  + _synergyMelee  + _awakeningMelee  + _relicMelee   + condMelee;
        int baseRangedSum = _baseRanged + _passiveRanged + _weaponRanged + _itemRanged + _roomRanged + _covenantRanged + _synergyRanged + _awakeningRanged + _relicRanged  + condRanged;
        int baseDefSum    = _baseDefense + _passiveDefense + _weaponDefense + _itemDefense + _roomDefense + _covenantDefense + _synergyDefense + _awakeningDefense + _relicDefense;
        int baseLuckSum   = _baseLuck + _passiveLuck + _itemLuck + _synergyLuck + _awakeningLuck + _relicLuck;

        MeleeAttack  = Mathf.Max(0, Mathf.RoundToInt(baseMeleeSum * dmgMul));
        RangedAttack = Mathf.Max(0, Mathf.RoundToInt(baseRangedSum * dmgMulR));
        Defense      = Mathf.Max(0, Mathf.RoundToInt(baseDefSum * defMul));
        Luck         = Mathf.Max(0, Mathf.RoundToInt(baseLuckSum * luckMul));

        // MaxHp 아이템 보너스 (flat + 동적 %) — 멱등 재계산.
        // 매 호출 자기 기여(_maxHpItemContribution)를 제거해 타 레이어 합(othersMax)을 얻고 재적용한다.
        // (이전 구현은 매 Recalculate마다 _itemMaxHp를 무조건 가산 → 반복 호출 시 MaxHp 폭증 버그였음)
        {
            int othersMax = MaxHp - _maxHpItemContribution;                       // base/passive/awakening/relic/synergy 합
            int flatTotal = othersMax + _itemMaxHp;                               // % 는 flat 총합 기준
            float maxHpPct = _itemMaxHpPercent + _itemDyn.maxHpPercent;           // 정적 + 동적
            int newContribution = _itemMaxHp + Mathf.RoundToInt(flatTotal * maxHpPct);
            int newMax = Mathf.Max(1, othersMax + newContribution);
            if (newMax != MaxHp)
            {
                MaxHp = newMax;
                if (Hp > MaxHp) Hp = MaxHp;
            }
            _maxHpItemContribution = newContribution;
        }

        AttackSpeedMultiplier = Mathf.Max(0.1f, 1f + _bonusAttackSpeed + _synergyAttackSpeed + _synergyDynAttackSpeed + _itemDyn.attackSpeed + _roomAttackSpeed + _covenantAttackSpeed + _itemAttackSpeed + _relicAttackSpeed + condAttackSpeed);
        MoveSpeedMultiplier  = Mathf.Max(0.1f, 1f + _itemDyn.moveSpeed + _roomMoveSpeed + _covenantMoveSpeed + _itemMoveSpeed + _awakeningMoveSpeed + _relicMoveSpeed);
        BonusProjectile      = Mathf.Max(0, _roomProjectile);
        SkillCooldownReduction = Mathf.Clamp01(_passiveSkillCdr + _itemSkillCdr + _synergySkillCdr + _awakeningSkillCdr + _relicSkillCdr);
        ActiveItemCooldownReduction = Mathf.Clamp01(_passiveActiveItemCdr + _itemActiveItemCdr + _synergyActiveItemCdr);

        // 확장 스탯 공개 프로퍼티 갱신
        RollCooldownBonus   = _itemRollCooldown;
        RollDistanceBonus   = _itemRollDistance;
        RangedRangeBonus    = _itemRangedRange;
        HealingReceivedBonus = _itemHealingReceived;
        DebuffResistance    = _itemDebuffResistance;
        AllDamagePercent    = _itemAllDamagePercent;
        DamageReduction     = Mathf.Clamp01(_itemDamageReduction + _characterDamageReduction + _synergyDynDamageReduction);
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
            case "MaxPoise":      _poiseBonus   += value; break;
            case "MaxStamina":    _staminaBonus += value; break;
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

    /// <summary>시너지 행동 역학 플래그를 entry 기반으로 활성화한다.</summary>
    public void ApplySynergyMechanicEffect(RuneSynergyEntry entry)
    {
        if (entry == null) return;
        var m = _synergyMechanics;

        switch (entry.effect_type)
        {
            case "ChargingStrike":
                m.ChargingStrikeEnabled = true;
                m.ChargingStrikePeriod = entry.value > 0 ? entry.value : 3f;
                m.ChargingStrikeDamageMultiplier = entry.value2 > 0 ? entry.value2 : 2f;
                m.ChargingStrikeCounter = 0;
                break;
            case "ShockwaveBurst":
                m.ShockwaveBurstEnabled = true;
                m.ShockwaveBurstStunDuration = entry.value > 0 ? entry.value : 0.5f;
                m.ShockwaveBurstDamageMultiplier = entry.value2 > 0 ? entry.value2 : 1.5f;
                m.ShockwaveBurstRadius = entry.value3 > 0 ? entry.value3 : 60f;
                break;
            case "MagicEcho":
                m.MagicEchoEnabled = true;
                m.MagicEchoChargeCount = entry.value > 0 ? entry.value : 3f;
                m.MagicEchoDamageBonus = entry.value2 > 0 ? entry.value2 : 0.3f;
                break;
            case "SkillEchoChain":
                m.SkillEchoChainEnabled = true;
                m.SkillEchoChainDamageMultiplier = entry.value2 > 0 ? entry.value2 : 0.5f;
                m.SkillEchoChainRange = entry.value3 > 0 ? entry.value3 : 8f;
                break;
            case "ShieldAccumulate":
                m.ShieldAccumulateEnabled = true;
                m.ShieldAccumulateRate = entry.value > 0 ? entry.value : 0.2f;
                m.ShieldCapRatio = 0.3f;
                break;
            case "ShieldBurst":
                m.ShieldBurstEnabled = true;
                m.ShieldBurstInvincibleDuration = entry.value > 0 ? entry.value : 0.5f;
                m.ShieldBurstDamageMultiplier = entry.value2 > 0 ? entry.value2 : 1.5f;
                break;
            case "DodgeOnMove":
                m.DodgeOnMoveEnabled = true;
                m.DodgeOnMoveBonus = entry.value > 0 ? entry.value : 0.1f;
                break;
            case "MoveAttackPenetrate":
                m.MoveAttackPenetrateEnabled = true;
                break;
            case "LowHpDamageReduce":
                m.LowHpDamageReduceEnabled = true;
                m.LowHpThreshold = entry.value > 0 ? entry.value : 0.5f;
                m.LowHpDamageReduceMax = entry.value2 > 0 ? entry.value2 : 0.4f;
                break;
            case "DeathSave":
                m.DeathSaveEnabled = true;
                m.DeathSaveInvincibleDuration = entry.value > 0 ? entry.value : 1.5f;
                m.DeathSaveHealRatio = entry.value2 > 0 ? entry.value2 : 0.3f;
                break;
            case "GambleDice":
                m.GambleDiceEnabled = true;
                m.GambleDiceDoubleChance = entry.value > 0 ? entry.value : 0.15f;
                m.GambleDiceMissChance = entry.value3 > 0 ? entry.value3 : 0.15f;
                break;
            case "CritChain":
                m.CritChainEnabled = true;
                m.CritChainChance = entry.value > 0 ? entry.value : 0.5f;
                break;
        }
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
        _synergyDynAttackSpeed = _synergyDynAttackPct = 0f;
        _synergyDynCritChance = _synergyDynCritDamage = _synergyDynDamageReduction = 0f;
        _conditionalSynergies.Clear();
        _synergyMechanics.Reset();

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
