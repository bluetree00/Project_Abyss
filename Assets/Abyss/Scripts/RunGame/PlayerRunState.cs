using System;

public sealed class PlayerRunState
{
    public int Hp { get; private set; }
    public int MaxHp { get; private set; }
    public int TempGold { get; private set; }

    /// <summary>
    /// 런이 유효한지(종료되면 false). 종료 후에는 상태 변경/이벤트 발행을 막는다.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    public event Action<int, int> OnHpChanged; // (hp, maxHp)
    public event Action<int> OnGoldChanged;    // tempGold

    public PlayerRunState(int maxHp = 100)
    {
        MaxHp = Math.Max(1, maxHp);
        Hp = MaxHp;
    }

    /// <summary>
    /// 런 종료 시 호출 권장: 이후 상태 변경을 무시하고 이벤트 참조도 정리.
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive) return;

        IsActive = false;

        // 이벤트 참조 해제(누수/중복 방지)
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

        // max만 동일해도 healToFull이면 반영해야 함
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

        // overflow 방어
        if (TempGold > int.MaxValue - amount)
            TempGold = int.MaxValue;
        else
            TempGold += amount;

        OnGoldChanged?.Invoke(TempGold);
    }

    // 편의 API(전투 시스템에서 쓰기 좋음)
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
