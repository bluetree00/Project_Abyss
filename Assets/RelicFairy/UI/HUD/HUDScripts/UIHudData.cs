using UnityEngine;

public struct WeaponSlotInfo
{
    public bool HasWeapon;
    public Sprite Icon;
    public string Name;
    public float Attack;
    public float Defense;
    public WeaponType Type;
}

public struct UIHudData
{
    public int Hp;
    public int MaxHp;
    public int TempGold;
    public int AttackPower;
    public WeaponSlotInfo Slot0;
    public WeaponSlotInfo Slot1;
}
