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
    public string iconKey;
    public Sprite icon;            // HUD 표시용 직접 참조
    public float baseAttack;
    public float baseDefense;
    public float attackSpeed;
    public float attackRange;
    public float areaOfEffect;
    public float holdThreshold;

    public int tier = 1;

    public PromoteMode promoteMode;
    public int chargeStages;

    public int groundEndCount;
    public int airEndCount;

    public WeaponAnimationSetSO animationSet;
    public WeaponAbilitySetSO abilitySet;
    public WeaponType weaponType = WeaponType.None;

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
        displayName      = so.displayName;
        iconKey          = so.iconKey;
        icon             = so.icon;
        baseAttack       = so.baseAttack;
        baseDefense      = so.baseDefense;
        attackSpeed      = so.attackSpeed;
        attackRange      = so.attackRange;
        areaOfEffect     = so.areaOfEffect;
        holdThreshold    = so.holdThreshold;
        tier             = so.tier;

        promoteMode  = so.promoteMode;
        chargeStages = so.chargeStages;

        weaponType = so.weaponType;

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
            tier             = entry.tier,
            promoteMode      = ParsePromoteMode(entry.promote_mode),
            chargeStages     = entry.charge_stages,
            groundEndCount   = entry.ground_combo_count,
            airEndCount      = entry.air_combo_count,
            weaponType       = ParseWeaponType(entry.weapon_type),
            // SO 참조는 null — WeaponSO에서 바인딩하거나 Addressables로 로드
            animationSet     = null,
            abilitySet       = null,
            skillQ           = null,
            skillE           = null,
        };
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
