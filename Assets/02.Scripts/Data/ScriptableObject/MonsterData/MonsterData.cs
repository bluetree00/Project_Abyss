using UnityEngine;

[CreateAssetMenu(fileName = "NewEffectData", menuName = "Monsters/Monster Data")]
public class MonsterData : ScriptableObject
{
    public float maxHealth;
    public float Hp;
    public float _scacRange = 10;
    public float _attackRange = 1;
    public float moveSpeed = 5f; // 기본 이동 속도
    public float runSpeed = 8f;  // 기본 달리기 속도

}
