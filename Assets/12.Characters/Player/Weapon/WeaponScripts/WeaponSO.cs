using UnityEngine;

[CreateAssetMenu(menuName = "Game/WeaponSO")]
public class WeaponSO : ScriptableObject
{
    [Header("기본 정보")]
    public string weaponDisplayKey;
    public string weaponKey;
    public string displayName;
    public string prefabKey;
    public string iconKey;
    public float baseAttack;
    public float baseDefense;

    [Header("콤보 정보")]
    public int groundEndCount;
    public int airEndCount;

    [Header("애니메이션")]
    public WeaponAnimationSetSO animationSet;

    [Header("이펙트 & 콜라이더")]
    public WeaponEffectPackageSO effectPackage;
    public WeaponColliderPackageSO colliderPackage;

    [Header("Ability Set")]
    public WeaponAbilitySetSO abilitySet;

     [Header("타입 & 정책")]
    public WeaponType weaponType = WeaponType.Sword;      // Sword / Bow / 나중에 추가
}
