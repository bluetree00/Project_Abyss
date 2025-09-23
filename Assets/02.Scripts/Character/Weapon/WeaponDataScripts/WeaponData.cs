using UnityEngine;
using UnityEngine.Serialization; // ★ FormerlySerializedAs

public abstract class WeaponData : ScriptableObject
{
    [Header("기본 무기 정보")]
    public string weaponName;
    public int attackPower;
    public string description;

    [Header("프리팹 키")]
    public Define.WeaponPrefabKey weaponKey;

    [Header("무기 타입")]
    public Define.WeaponType weaponType;

    // ------- 능력 모듈 -------
    [Header("라이트/헤비 어빌리티(SO)")]
    public LightAttackAbilitySO lightAttackSO;
    public HeavyAttackAbilitySO heavyAttackSO;

    // 인터페이스로 노출
    public ILightAttackAbility<PlayerController> LightAttack => lightAttackSO;
    public IHeavyAttackAbility<PlayerController> HeavyAttack => heavyAttackSO;

    // ------- 애니메이션 세트 -------
    [Header("라이트 애니메이션 세트(지상/공중)")]
    [FormerlySerializedAs("lightAttackAnimationSetSO")] // 기존 필드명 호환
    public LightAttackAnimationSetSO lightSet;

    [Header("헤비 애니메이션 세트(지상/공중)")]
    public HeavyAttackAnimationSetSO heavyAttackSet;

    [Header("무기 등급")]
    public Define.WeaponRarity rarity;

    [Header("장비 교체 애니메이션 이름(선택)")]
    public string weapon_ChangeWeapon_AnimationName;

    [Header("스킬")]
    public SkillData QSkillData;
    public SkillData ESkillData;

    public virtual void UseQSkill(GameObject user) => QSkillData?.Activate(user);
    public virtual void UseESkill(GameObject user) => ESkillData?.Activate(user);
}
