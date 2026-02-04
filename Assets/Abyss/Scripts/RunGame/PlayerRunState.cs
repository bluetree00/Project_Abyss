using System;

public sealed class PlayerRunState
{
    public int Hp { get; private set; }
    public int MaxHp { get; private set; }

    public int TempGold { get; private set; }

    public event Action<int, int> OnHpChanged;     // (hp, maxHp)
    public event Action<int> OnGoldChanged;        // tempGold

    public PlayerRunState(int maxHp = 100)
    {
        MaxHp = maxHp;
        Hp = maxHp;
    }

    public void SetHp(int hp)
    {
        hp = Math.Clamp(hp, 0, MaxHp);
        if (Hp == hp) return;
        Hp = hp;
        OnHpChanged?.Invoke(Hp, MaxHp);
    }

    public void SetMaxHp(int maxHp, bool healToFull = false)
    {
        maxHp = Math.Max(1, maxHp);
        if (MaxHp == maxHp) return;

        MaxHp = maxHp;
        if (Hp > MaxHp) Hp = MaxHp;
        if (healToFull) Hp = MaxHp;

        OnHpChanged?.Invoke(Hp, MaxHp);
    }

    public void AddTempGold(int amount)
    {
        if (amount <= 0) return;
        TempGold += amount;
        OnGoldChanged?.Invoke(TempGold);
    }
}
