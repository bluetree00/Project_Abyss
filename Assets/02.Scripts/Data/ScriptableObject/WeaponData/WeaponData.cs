using UnityEngine;

// 무기 타입 열거형
    public enum WeaponType
    {
        Sword,
        Bow,
        Staff,
        Dagger,
        Axe
    }

    // 무기 등급 열거형
    public enum WeaponRarity
    {
        Common,
        Rare,
        Epic,
        Legendary
    }

// 얘를 상속받아서 각 무기들의 SO 데이터 생성 예정
// 얘는 모든 무기 SO 들의 부모 가상 클래스

public abstract class WeaponData : ScriptableObject
{
    // 기본 무기 정보
    [Header("기본 무기 정보")]
    public string weaponName;   // 무기 이름
    public int attackPower;     // 무기 공격력
    public string description;  // 무기 설명

    // 특수 효과 관련
    [Header("특수 효과")]
    public bool hasSpecialEffect;          // 특수 효과 여부
    public string specialEffectDescription; // 특수 효과 설명
    public float bonusDamage;               // 추가 데미지

    // 무기 등급 관련
    [Header("무기 등급")]
    public WeaponRarity rarity;            // 무기 등급
    public int upgradeLevel;               // 무기 업그레이드 레벨

    // 무기 타입 분류
    [Header("무기 타입")]
    public WeaponType weaponType;          // 무기 타입 (예: 검, 활, 지팡이 등)

    // 무기 공격 속도
    [Header("공격 속도")]
    public float attackSpeed = 1.0f;       // 기본 공격 속도 (1초당 공격 횟수)

    // 공격력 증가 계산
    public int CalculateEffectiveAttackPower()
    {
        int bonusPower = upgradeLevel * 10; // 업그레이드 레벨당 추가 공격력 (예시)
        return attackPower + bonusPower;
    }

    // 무기 효과를 적용하는 메서드
    public virtual void ApplyWeaponEffects(ref float damage)
    {
        if (hasSpecialEffect)
        {
            // 무기의 추가 데미지를 적용
            damage += bonusDamage;
            Debug.Log($"{weaponName}의 특수 효과가 적용되었습니다: 추가 데미지 {bonusDamage}");
        }
    }

    
}
