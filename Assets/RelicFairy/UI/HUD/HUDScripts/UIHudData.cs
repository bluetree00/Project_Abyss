using UnityEngine;

public struct WeaponSlotInfo
{
    public bool HasWeapon;
    public Sprite Icon;
    public string Name;
    public float Attack;
    public float Defense;
    public WeaponType Type;
    /// <summary>강화 단계(+N). 0이면 표시하지 않는다 — HUD 무기 칸이 「지금 든 장비」를 말하는 데 쓴다.</summary>
    public int EnhanceLevel;
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
