using UnityEngine;

[CreateAssetMenu(fileName = "NewEffectData", menuName = "Monsters/Monster Data")]
public class MonsterData : ScriptableObject
{
     // 캐릭터 기본 정보
    [Header("캐릭터 기본 정보")]
    public string monsterName;
    public float maxHealth;
    public float Hp;
    public float _scacRange = 10;
    public float _attackRange = 1;
    public float moveSpeed = 5f; // 기본 이동 속도
    public float runSpeed = 8f;  // 기본 달리기 속도

     [Header("공격 정보")]
    public float attackCooldown = 1.0f; // 공격 후 재공격까지의 대기 시간

}
