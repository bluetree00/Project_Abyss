using UnityEngine;

[CreateAssetMenu(fileName = "NewWeaponData", menuName = "Weapon/Weapon Data")]
public class WeaponData : ScriptableObject
{
    [Header("무기 기본 수치")]
    public string weaponName;
    public float weaponDamage;
}
