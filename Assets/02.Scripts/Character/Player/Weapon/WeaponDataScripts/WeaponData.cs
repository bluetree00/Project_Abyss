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


    [Header("무기 구성 풀러 패키지 이펙트, 프리펩 등")] // 타입을 이름 기준으로 풀러 연결 초기화화 기본 이펙트를 정해줌. 추후 패키지 풀러 연결로 사용용
    public Define.WeaponType weaponType;

    // ======= 노말 공격 공격 방식, 애니메이션 =======
    [Header("사용할 무기 노말 공격 기능")]
    public LightAttackAbilitySO lightAttackSO;

    [Header("노말 공격 애니메이션, 콤보보 세팅 SO")]
    public LightAttackAnimationSetSO lightAttackAnimationSetSO; 

    // ======= 모으기, 강공격 공격 방식, 애니메이션 =======
    [Header("사용할 무기 모으기, 강 공격 기능")]
    public HeavyAttackAbilitySO heavyAttackSO;

    [Header("무기 강공격 애니메이션 세트")]
    public HeavyAttackAnimationSetSO heavyAttackSet;

    // SO로 모듈을 받아서 인터페이스로 연결
    public ILightAttackAbility<CharacterController> LightAttack => lightAttackSO;
    public IHeavyAttackAbility<CharacterController> HeavyAttack => heavyAttackSO;

    [Header("무기 등급")] //추후 사용 가능성 있음
    public Define.WeaponRarity rarity;

    // ======= 콤보 입력 대기 =======
    [Header("Combo")]
   // public float comboResetTime = 1.5f; // 콤보 입력 대기 최대 콤보 까지 도달 유지하는 시간

    [Header("장비 교체 애니메이션")]
    public string weapon_ChangeWeapon_AnimationName;

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

}
