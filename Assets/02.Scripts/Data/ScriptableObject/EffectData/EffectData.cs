using UnityEngine;

[CreateAssetMenu(fileName = "NewEffectData", menuName = "Effects/Effect Data")]
public class EffectData : ScriptableObject
{
    public string effectName; // 이펙트 이름
    public float damage; // 기본 데미지
    public float hitInterval; // 타격 간격
    public string effectTag; // 이펙트 태그 추가
    public bool isPlayerEffect; // 플레이어용 이펙트 여부

    // 다른 변수 ex) 캐릭터 데미지 + 이펙트 데미지

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

    // 이 데이터 스크립트 안에서 캐릭터 데이터와 이펙트 데이터를 연결하는 함수
}
