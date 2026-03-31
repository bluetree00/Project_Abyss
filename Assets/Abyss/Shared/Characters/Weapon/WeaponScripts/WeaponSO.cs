using UnityEngine;

[CreateAssetMenu(menuName = "Game/WeaponSO")]
public class WeaponSO : ScriptableObject
{
    [Header("기본 정보")]
    public string weaponDisplayKey;
    public string weaponPrefabKey;  // Addressables 무기 프리팹 키
    public string displayName;
    public string iconKey;          // Addressables 비동기 로드용 키
    public Sprite icon;             // HUD 즉시 표시용 직접 참조
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
    public WeaponSlotType slotType = WeaponSlotType.Main; // Main: 일반 공격 + E/R스킬, Sub: Q스킬 전용

    [Header("스킬")]
    public SkillSO skillQ;
    public SkillSO skillE;

    [Header("차지/강화 공격 설정")]
    public float holdThreshold;
    public PromoteMode promoteMode = PromoteMode.None;    // None, Stage, ChargeFull
    public int chargeStages = 1;                          // 차지 공격 단계 수

}
