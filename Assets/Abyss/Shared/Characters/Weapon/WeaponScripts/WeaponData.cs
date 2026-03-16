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

    public WeaponData(WeaponSO so)
    {
        if (so == null) throw new ArgumentNullException(nameof(so));

        weaponDisplayKey = so.weaponDisplayKey;
        weaponPrefabKey  = so.weaponPrefabKey;
        displayName = so.displayName;
        iconKey = so.iconKey;
        icon = so.icon;
        baseAttack = so.baseAttack;
        baseDefense = so.baseDefense;
        holdThreshold = so.holdThreshold;

        promoteMode = so.promoteMode;
        chargeStages = so.chargeStages;

        weaponType = so.weaponType;

        groundEndCount = so.groundEndCount;
        airEndCount = so.airEndCount;

        animationSet = so.animationSet;
        abilitySet = so.abilitySet;
    }
}
