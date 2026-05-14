// WeaponData.cs
using System;
using UnityEngine;

public enum PromoteMode
{
    None,       // 차지/강화 없음
    Stage,      // 단계별 강화
    ChargeFull  // 일정 시간 누르면 최대 강화
}

/// <summary>
/// 런타임 Weapon 데이터 (WeaponSO 기반 생성)
/// </summary>
[Serializable]
public class WeaponData
{
    public string weaponDisplayKey;
    public string displayName;
    public string weaponPrefabKey;
    /// <summary>WeaponSO 에셋 이름 = Addressable 주소. 이어하기 복원 시 WeaponSO 로드에 사용한다.</summary>
    public string weaponSOKey;
    public string iconKey;
    public Sprite icon;            // HUD 표시용 직접 참조
    public float baseAttack;
    public float baseDefense;
    public float attackSpeed;
    public float attackRange;
    public float areaOfEffect;
    public float holdThreshold;

    // ── 치명타 ───────────────────────────────────────────────────────
    public float critChance = 25f;   // % 단위 (0~100)
    public float critDamage = 1.25f; // 배율 (1.25 = +25%)

    public int tier = 1;
    // 아이템 추첨 시스템과 통합된 등급. tier와 병행 유지(점진적 마이그레이션).
    public ItemRarity rarity = ItemRarity.Common;

    public PromoteMode promoteMode;
    public int chargeStages;

    public int groundEndCount;
    public int airEndCount;

    public WeaponAnimationSetSO animationSet;
    public WeaponAbilitySetSO abilitySet;
    public WeaponType weaponType = WeaponType.None;
    public WeaponElement element = WeaponElement.None;

    // ── 원소 누적치 부여량 ───────────────────────────────────────────
    public float elementAmountBasic = 0f;
    public float elementAmountHeavy = 0f;
    public float elementAmountAir   = 0f;

    // ── 스킬 SO 참조 ─────────────────────────────────────────────────
    public SkillSO skillQ;
    public SkillSO skillE;

    // ── 하위 호환 편의 접근자 ─────────────────────────────────────────
    public string skillName        => skillQ?.skillName;
    public string skillDescription => skillQ?.description;
    public Sprite skillQIcon       => skillQ?.icon;
    public Sprite skillEIcon       => skillE?.icon;
    public float  skillQCooldown   => skillQ?.cooldown ?? 0f;
    public float  skillECooldown   => skillE?.cooldown ?? 0f;

    /// <summary>WeaponSO 기반 생성</summary>
    public WeaponData(WeaponSO so)
    {
        if (so == null) throw new ArgumentNullException(nameof(so));

        weaponDisplayKey = so.weaponDisplayKey;
        weaponPrefabKey  = so.weaponPrefabKey;
        weaponSOKey      = so.name;
        displayName      = so.displayName;
        iconKey          = so.iconKey;
        icon             = so.icon;
        baseAttack       = so.baseAttack;
        baseDefense      = so.baseDefense;
        attackSpeed      = so.attackSpeed;
        attackRange      = so.attackRange;
        areaOfEffect     = so.areaOfEffect;
        holdThreshold    = so.holdThreshold;
        critChance       = so.critChance;
        critDamage       = so.critDamage > 0f ? so.critDamage : 1.25f;
        tier             = so.tier;
        rarity           = so.Rarity;

        promoteMode  = so.promoteMode;
        chargeStages = so.chargeStages;

        weaponType = so.weaponType;
        element    = so.element;

        skillQ = so.skillQ;
        skillE = so.skillE;

        groundEndCount = so.groundEndCount;
        airEndCount    = so.airEndCount;

        animationSet = so.animationSet;
        abilitySet   = so.abilitySet;
    }

    /// <summary>SO 타입을 자동 판별해 적절한 WeaponData를 생성하는 팩토리</summary>
    public static WeaponData FromSO(WeaponSO so) => new WeaponData(so);

    /// <summary>
    /// 차트(EquipmentEntry) stats로 SO 디폴트값을 덮어쓴다.
    /// 차트가 마스터이므로 무기 획득 시 SO 로드 후 반드시 호출.
    /// SO 참조(animationSet, abilitySet, skillQ/E, icon, weaponType, displayName, prefabKey 등)는 보존.
    /// </summary>
    public void ApplyServerOverride(EquipmentEntry entry)
    {
        if (entry == null) return;

        baseAttack         = entry.base_attack;
        baseDefense        = entry.base_defense;
        critChance         = entry.crit_chance;
        critDamage         = entry.crit_damage > 0f ? entry.crit_damage : 1.25f;
        attackSpeed        = entry.attack_speed;
        attackRange        = entry.attack_range;
        areaOfEffect       = entry.area_of_effect;
        holdThreshold      = entry.hold_threshold;
        groundEndCount     = entry.ground_combo_count;
        airEndCount        = entry.air_combo_count;
        promoteMode        = ParsePromoteMode(entry.promote_mode);
        chargeStages       = entry.charge_stages;
        element            = ParseElement(entry);
        elementAmountBasic = entry.element_amount_basic;
        elementAmountHeavy = entry.element_amount_heavy;
        elementAmountAir   = entry.element_amount_air;
        tier               = entry.tier;
        rarity             = ParseRarity(entry.rarity, entry.tier);
        // weaponType, displayName, weaponPrefabKey, weaponDisplayKey, iconKey,
        // animationSet, abilitySet, skillQ/E 는 SO 값 유지
    }

    /// <summary>
    /// 서버 EquipmentEntry 기반 생성.
    /// SO 참조(animationSet, abilitySet, skillQ/E, icon)는 null — 별도 바인딩 필요.
    /// </summary>
    public static WeaponData FromServer(EquipmentEntry entry)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));

        var data = new WeaponData
        {
            weaponDisplayKey = entry.weapon_display_key,
            weaponPrefabKey  = entry.weapon_prefab_key,
            displayName      = entry.weapon_name,
            iconKey          = entry.icon_key,
            icon             = null, // Addressables로 별도 로드
            baseAttack       = entry.base_attack,
            baseDefense      = entry.base_defense,
            attackSpeed      = entry.attack_speed,
            attackRange      = entry.attack_range,
            areaOfEffect     = entry.area_of_effect,
            holdThreshold    = entry.hold_threshold,
            critChance       = entry.crit_chance,
            critDamage       = entry.crit_damage > 0f ? entry.crit_damage : 1.25f,
            tier             = entry.tier,
            rarity           = ParseRarity(entry.rarity, entry.tier),
            promoteMode      = ParsePromoteMode(entry.promote_mode),
            chargeStages     = entry.charge_stages,
            groundEndCount   = entry.ground_combo_count,
            airEndCount      = entry.air_combo_count,
            weaponType           = ParseWeaponType(entry.weapon_type),
            element              = ParseElement(entry),
            elementAmountBasic   = entry.element_amount_basic,
            elementAmountHeavy   = entry.element_amount_heavy,
            elementAmountAir     = entry.element_amount_air,
            // SO 참조는 null — WeaponSO에서 바인딩하거나 Addressables로 로드
            animationSet     = null,
            abilitySet       = null,
            skillQ           = null,
            skillE           = null,
        };
        UnityEngine.Debug.Log($"[WeaponData.FromServer] {entry.weapon_id} ({entry.weapon_name}) | Elem={data.element} (raw='{entry.element}') | Amt(B/H/A)={data.elementAmountBasic}/{data.elementAmountHeavy}/{data.elementAmountAir}");
        return data;
    }

    /// <summary>SO 없이 빈 WeaponData 생성 (서버 팩토리용)</summary>
    private WeaponData() { }

    private static PromoteMode ParsePromoteMode(string s) => s switch
    {
        "Stage"      => PromoteMode.Stage,
        "ChargeFull" => PromoteMode.ChargeFull,
        _            => PromoteMode.None,
    };

    /// <summary>
    /// 서버 rarity 문자열을 ItemRarity로 파싱.
    /// 비어있거나 파싱 실패 시 tier 기반 폴백.
    /// </summary>
    private static ItemRarity ParseRarity(string s, int tier)
    {
        if (string.IsNullOrEmpty(s)) return MapTierToRarity(tier);
        if (Enum.TryParse<ItemRarity>(s, ignoreCase: true, out var r)) return r;
        return MapTierToRarity(tier);
    }

    /// <summary>tier(1~4) → ItemRarity 매핑. 잘못된 값은 Common으로 폴백.</summary>
    private static ItemRarity MapTierToRarity(int tier) => tier switch
    {
        1 => ItemRarity.Common,
        2 => ItemRarity.Rare,
        3 => ItemRarity.Epic,
        4 => ItemRarity.Legendary,
        _ => ItemRarity.Common,
    };

    public static WeaponElement ParseElementPublic(EquipmentEntry entry) => ParseElement(entry);

    private static WeaponElement ParseElement(EquipmentEntry entry)
    {
        // EquipmentEntry에 element 필드가 있으면 사용, 없으면 None
        var s = entry != null ? (entry.element ?? "") : "";
        return s switch
        {
            "Water"     => WeaponElement.Water,
            "Fire"      => WeaponElement.Fire,
            "Grass"     => WeaponElement.Grass,
            "Earth"     => WeaponElement.Earth,
            "Lightning" => WeaponElement.Lightning,
            _           => WeaponElement.None,
        };
    }

    private static WeaponType ParseWeaponType(string s) => s switch
    {
        "Katana"     => WeaponType.Katana,
        "Greatsword" => WeaponType.Greatsword,
        "Crossbow"   => WeaponType.Crossbow,
        "Bow"        => WeaponType.Bow,
        "Staff"      => WeaponType.Staff,
        _            => WeaponType.None,
    };
}
