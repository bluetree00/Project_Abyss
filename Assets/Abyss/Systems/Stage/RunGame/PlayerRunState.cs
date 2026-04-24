//============================================================
// PlayerRunState.cs
// - Run 동안만 유효한 플레이어 상태(HP/Gold 등)
// - 이벤트 기반으로 HUD 갱신
// - Deactivate()로 종료 후 이벤트/변경 차단
//============================================================
using System;

public sealed class PlayerRunState
{
    public int Hp { get; private set; }
    public int MaxHp { get; private set; }
    public int TempGold { get; private set; }

    public bool IsActive { get; private set; } = true;

    public event Action<int, int> OnHpChanged; // (hp, maxHp)
    public event Action<int> OnGoldChanged;    // tempGold

    public PlayerRunState(int maxHp = 100)
    {
        MaxHp = Math.Max(1, maxHp);
        Hp = MaxHp;
    }

    public void Deactivate()
    {
        if (!IsActive) return;

        IsActive = false;
        OnHpChanged = null;
        OnGoldChanged = null;
    }

    public void SetHp(int hp)
    {
        if (!IsActive) return;

        hp = Math.Clamp(hp, 0, MaxHp);
        if (Hp == hp) return;

        Hp = hp;
        OnHpChanged?.Invoke(Hp, MaxHp);
    }

    public void SetMaxHp(int maxHp, bool healToFull = false)
    {
        if (!IsActive) return;

        maxHp = Math.Max(1, maxHp);
        if (MaxHp == maxHp && !healToFull) return;

        MaxHp = maxHp;

        if (healToFull) Hp = MaxHp;
        else if (Hp > MaxHp) Hp = MaxHp;

        OnHpChanged?.Invoke(Hp, MaxHp);
    }

    public void AddTempGold(int amount)
    {
        if (!IsActive) return;
        if (amount <= 0) return;

        if (TempGold > int.MaxValue - amount)
            TempGold = int.MaxValue;
        else
            TempGold += amount;

        OnGoldChanged?.Invoke(TempGold);
    }

    /// <summary>골드를 차감한다. 잔액 부족이면 false 반환(차감 없음).</summary>
    public bool TrySpendGold(int amount)
    {
        if (!IsActive) return false;
        if (amount <= 0) return false;
        if (TempGold < amount) return false;

        TempGold -= amount;
        OnGoldChanged?.Invoke(TempGold);
        return true;
    }

    public void Damage(int amount)
    {
        if (amount <= 0) return;
        SetHp(Hp - amount);
    }

    public void Heal(int amount)
    {
        if (amount <= 0) return;
        SetHp(Hp + amount);
    }
}
