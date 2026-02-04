using System;

[Serializable]
public struct UIHudData
{
    public int AttackPower;
    public int Hp;
    public int MaxHp;

    public int PermanentGold; // 외부(영구) 골드
    public int TempGold;      // 런 전용 골드(선택)

    public bool HasValue;

    public static UIHudData Empty => new UIHudData { HasValue = false };
}
