// WeaponData.cs
using System;
using UnityEngine;

public enum WeaponSlotType { Main }

public enum PromoteMode
{
    None,       // 차지/강화 없음
    Stage,      // 단계별 강화
    ChargeFull  // 일정 시간 누르면 최대 강화
}

/// <summary>
/// 런타임 Weapon 데이터 (WeaponSO 기반 생성)
/// 실행 경로: abilitySet → WeaponAbilitySetSO → WeaponAbilitySO → AbilityStep
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
    public float holdThreshold;

    public PromoteMode promoteMode;
    public int chargeStages;

    public int groundEndCount;
    public int airEndCount;

    public WeaponAnimationSetSO animationSet;
    public WeaponAbilitySetSO abilitySet;
    public WeaponType weaponType = WeaponType.None;
    public WeaponSlotType slotType = WeaponSlotType.Main;
    public string skillName;
    public string skillDescription;

    // ── 스킬 아이콘 ───────────────────────────────────────────────────────────
    public Sprite skillQIcon;
    public Sprite skillEIcon;

    // ── 스킬 쿨다운 ──────────────────────────────────────────────────────────
    public float skillQCooldown;
    public float skillECooldown;

    /// <summary>WeaponSO 기반 생성 (공통 필드만 복사)</summary>
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
        holdThreshold    = so.holdThreshold;

        promoteMode  = so.promoteMode;
        chargeStages = so.chargeStages;

        weaponType       = so.weaponType;
        slotType         = so.slotType;
        skillName        = so.skillName;
        skillDescription = so.skillDescription;

        skillQIcon = so.skillQIcon;
        skillEIcon = so.skillEIcon;

        groundEndCount = so.groundEndCount;
        airEndCount    = so.airEndCount;

        animationSet = so.animationSet;
        abilitySet   = so.abilitySet;
    }

    /// <summary>MainWeaponSO 기반 생성 — Q/E 쿨다운 포함</summary>
    public WeaponData(MainWeaponSO so) : this((WeaponSO)so)
    {
        skillQCooldown = so.skillQCooldown;
        skillECooldown = so.skillECooldown;
    }

    /// <summary>SO 타입을 자동 판별해 적절한 WeaponData를 생성하는 팩토리</summary>
    public static WeaponData FromSO(WeaponSO so) => so switch
    {
        MainWeaponSO main => new WeaponData(main),
        _                 => new WeaponData(so),
    };
}
