using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacterData", menuName = "Characters/Character Data")]
public class CharacterData : ScriptableObject
{
    public float baseMoveSpeed;
    public float baseRunSpeed;
    public int maxHealth;
    public int attackPower;
}
