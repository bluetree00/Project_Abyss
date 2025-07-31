using UnityEngine;

[CreateAssetMenu(fileName = "NewEffectData", menuName = "Effects/Effect Data")]
public class EffectData : ScriptableObject
{
    public CharacterData ownerCharacterData;  // 이펙트의 주인 캐릭터 데이터
    public WeaponData ownerWeaponData;  // 이펙트의 주인 무기 데이터
    public string effectName; // 이펙트 이름
    public float damage; // 기본 데미지 = 계수
    public float totalDamage; // 계산이 끝난 데미지 처리에서 사용할 변수
    public float hitInterval; // 타격 간격
    public string effectTag; // 이펙트 태그 추가
    public bool isPlayerEffect; // 플레이어용 이펙트 여부

    // 외부에서 데미지를 증가시키는 메서드
    public void IncreaseDamage(float amount)
    {
        damage += amount; // 원하는 만큼 데미지를 증가시킴
    }

    // 외부에서 데미지를 설정하는 메서드
    public void SetDamage(float newDamage)
    {
        damage = newDamage; // 새로운 데미지로 설정
    }

    public void SetOwnerCharacter(CharacterData characterData)
    {
        ownerCharacterData = characterData;
    }

    public void SetOwnerWeapon(WeaponData weaponData)
    {
        ownerWeaponData = weaponData;
    }

    // 캐릭터데이터 매니저에서 받아온 [캐릭터 공격력 + 무기 공격력 = {종합 공격력}]을 이펙트 데이터로 동기화
    public float SetEffectDamage()
    {
        if (Managers.CharacterData == null)
        {
            Debug.LogError("CharacterData is not initialized in Managers!");
            return 0; // 기본값 반환
        }

        // 캐릭터 공격력과 무기 공격력을 합산하여 총 데미지 계산
        totalDamage = damage + ownerCharacterData.GetTotalAttackPower();
        
        return totalDamage;
    }

}
