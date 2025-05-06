using UnityEngine;
using Game.CharacterStates;
using System.Collections.Generic;

public abstract class WeaponData : ScriptableObject
{
    // ======= 기본 정보 =======
    [Header("기본 무기 정보")]
    public string weaponName;
    public int attackPower;
    public string description;

    [Header("생성할 무기 오브젝트 이름")]
     public Define.WeaponPrefabKey weaponKey;


    [Header("무기 타입")] // 타입을 이름 기준으로 풀러 연결 초기화화 기본 이펙트를 정해줌. 추후 수정가능 
    public Define.WeaponType weaponType;

    public DefaultLightAttackAbility lightAttackSO;
    public DefaultHeavyAttackAbility heavyAttackSO;

    public ILightAttackAbility<CharacterController> LightAttack => lightAttackSO;
    public IHeavyAttackAbility<CharacterController> HeavyAttack => heavyAttackSO;

    [Header("무기 등급")]
    public Define.WeaponRarity rarity;
    public int upgradeLevel;

    // ======= 특수 효과 =======
    [Header("특수 효과")]
    public bool hasSpecialEffect;
    public string specialEffectDescription;
    public float bonusDamage;

    // ======= 공격 속도 =======
    [Header("공격 속도")]
    public float attackSpeed = 1.0f;

        [Header("Combo")]
    public float comboResetTime = 1.5f; // 콤보 입력 대기 시간


    // ======= 무기 오브젝트 및 애니메이션 =======

    [Header("애니메이터 컨트롤러")]
    public RuntimeAnimatorController animatorController; // 여기에 애니메이터 컨트롤러 추가

    [Header("무기 애니메이션")]
    public List<AnimationClip> attackAnimations; // 공격 애니메이션 목록
    public int maxAttackCount = 3; // 최대 공격 수
    public string weapon_Idle_AnimationName;
    public string weapon_ChangeWeapon_AnimationName;
    public string[] normalAttackAnimations;
    public float[] comboEndTimes;

    // ======= 스킬 (인터페이스 기반 SO 연결) =======
    [Header("Q 스킬")]
    public SkillData QSkillData;

    [Header("E 스킬")]
    public SkillData ESkillData;

    // ======= 스킬 실행 메서드 =======
    public virtual void UseQSkill(GameObject user)
    {
        QSkillData?.Activate(user);
    }

    public virtual void UseESkill(GameObject user)
    {
        ESkillData?.Activate(user);
    }

    // ======= 기타 기능 메서드 =======
    public int CalculateEffectiveAttackPower()
    {
        int bonusPower = upgradeLevel * 10;
        return attackPower + bonusPower;
    }

    public virtual void ApplyWeaponEffects(ref float damage)
    {
        if (hasSpecialEffect)
        {
            damage += bonusDamage;
            Debug.Log($"{weaponName}의 특수 효과 적용됨: 추가 데미지 {bonusDamage}");
        }
    }

    public virtual string GetNormalAttackAnimation(int step)
    {
        if (normalAttackAnimations == null || normalAttackAnimations.Length == 0)
            return "DefaultAttack";

        if (step <= 0 || step > normalAttackAnimations.Length)
            return normalAttackAnimations[0];

        return normalAttackAnimations[step - 1];
    }
}
