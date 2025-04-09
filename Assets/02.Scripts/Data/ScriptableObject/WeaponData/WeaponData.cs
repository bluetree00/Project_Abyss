using UnityEngine;
using Game.CharacterStates;

// 무기 타입
public enum WeaponType
{
    Sword,
    Bow,
    Staff,
    Dagger,
    Axe
}

// 무기 등급
public enum WeaponRarity
{
    Common,
    Rare,
    Epic,
    Legendary
}

/// <summary>
/// 모든 무기 데이터의 베이스 클래스.
/// 공격 FSM을 제공하기 위해 IComboAttackProvider 인터페이스를 구현함.
/// </summary>
public abstract class WeaponData : ScriptableObject, IComboAttackProvider
{
    // ======= 기본 정보 =======
    [Header("기본 무기 정보")]
    public string weaponName;
    public int attackPower;
    public string description;

    [Header("무기 타입")]
    public WeaponType weaponType;

    [Header("무기 등급")]
    public WeaponRarity rarity;
    public int upgradeLevel;

    // ======= 특수 효과 =======
    [Header("특수 효과")]
    public bool hasSpecialEffect;
    public string specialEffectDescription;
    public float bonusDamage;

    // ======= 공격 속도 =======
    [Header("공격 속도")]
    public float attackSpeed = 1.0f;

    // ======= 무기 오브젝트 및 애니메이션 =======
    [Header("무기 오브젝트")]
    public string weaponObjName;

    [Header("무기 애니메이션")]
    public string weapon_Idle_AnimationName;
    public string weapon_ChangeWeapon_AnimationName;
    public string[] normalAttackAnimations;
    public int maxComboCount;
    public float[] comboEndTimes;

    // ======= 스킬 =======
    [Header("Q 스킬")]
    public string Q_SkillName;
    public string Q_SkillDescription;
    public string[] Q_SkillEffectName;

    [Header("E 스킬")]
    public string E_SkillName;
    public string E_SkillDescription;
    public string[] E_SkillEffectName;

    // ======= 기능 메서드 =======
    public abstract void QSkill(); // Q 스킬 구현은 무기별 오버라이드
    public abstract void ESkill(); // E 스킬 구현은 무기별 오버라이드

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
            return "DefaultAttack"; // fallback

        if (step <= 0 || step > normalAttackAnimations.Length)
            return normalAttackAnimations[0];

        return normalAttackAnimations[step - 1];
    }



    /// <summary>
    /// 이 무기에 해당하는 공격 상태 머신 반환
    /// 캐릭터 타입에 따라 유효성을 판단할 수 있음
    /// </summary>
    public abstract StateMachine<CharacterController> CreateAttackStateMachine(CharacterController owner);
}
