//============================================================
// PlayerRunState.cs
// - Run 동안만 유효한 플레이어 상태(HP/Gold 등)
// - 이벤트 기반으로 HUD 갱신
// - Deactivate()로 종료 후 이벤트/변경 차단
//============================================================
using System;

public sealed class PlayerRunState
{
    public const int DefaultPotionCapacity = 3;

    public int Hp { get; private set; }
    public int MaxHp { get; private set; }
    public int TempGold { get; private set; }

    // 포션(퀵슬롯 소모품) — 런 지속. 사용 시 GameRunSession이 즉발 % 회복을 처리하고 여기서 개수를 깎는다.
    public int PotionCount { get; private set; }
    public int PotionCapacity { get; private set; } = DefaultPotionCapacity;

    public bool IsActive { get; private set; } = true;

    /// <summary>NoHeal(고행) 이벤트 챌린지 중 회복 봉인 — 포션·Heal 무효화. 방 수명 동안만 true.</summary>
    public bool HealLocked { get; set; }

    public event Action<int, int> OnHpChanged;       // (hp, maxHp)
    public event Action<int> OnGoldChanged;          // tempGold
    public event Action<int, int> OnPotionChanged;   // (count, capacity)

    public PlayerRunState(int maxHp = 100, int startGold = 0)
    {
        MaxHp = Math.Max(1, maxHp);
        Hp = MaxHp;
        TempGold = Math.Max(0, startGold);

        // 포션은 <b>가득 채워 시작</b>한다. 예전엔 0으로 시작해 아무도 채워주지 않으면
        // C를 눌러도 재고가 없어 조용히 무시됐다(포션이 아예 안 쓰이던 원인).
        // 이어하기는 RestorePotions가 저장값으로 덮어쓰므로 영향 없다.
        PotionCount = PotionCapacity;
    }

    public void Deactivate()
    {
        if (!IsActive) return;

        IsActive = false;
        OnHpChanged = null;
        OnGoldChanged = null;
        OnPotionChanged = null;
    }

    // ── 포션 ──────────────────────────────────────────────────

    /// <summary>포션 용량 설정(소모품 슬롯 보너스 등). 초과분은 잘린다.</summary>
    public void SetPotionCapacity(int capacity)
    {
        if (!IsActive) return;
        capacity = Math.Max(0, capacity);
        if (PotionCapacity == capacity) return;
        PotionCapacity = capacity;
        if (PotionCount > PotionCapacity) PotionCount = PotionCapacity;
        OnPotionChanged?.Invoke(PotionCount, PotionCapacity);
    }

    /// <summary>포션 지급(상점 구매·대기방 보충). 용량 상한까지만.</summary>
    public void AddPotion(int amount)
    {
        if (!IsActive || amount <= 0) return;
        int next = Math.Min(PotionCapacity, PotionCount + amount);
        if (next == PotionCount) return;
        PotionCount = next;
        OnPotionChanged?.Invoke(PotionCount, PotionCapacity);
    }

    /// <summary>포션을 용량까지 가득 채운다(대기방 보충).</summary>
    public void RefillPotions()
    {
        if (!IsActive || PotionCount >= PotionCapacity) return;
        PotionCount = PotionCapacity;
        OnPotionChanged?.Invoke(PotionCount, PotionCapacity);
    }

    /// <summary>포션 1개 소모 시도. 없으면 false(회복도 없음).</summary>
    public bool TryConsumePotion()
    {
        if (!IsActive || HealLocked || PotionCount <= 0) return false;
        PotionCount--;
        OnPotionChanged?.Invoke(PotionCount, PotionCapacity);
        return true;
    }

    /// <summary>세이브 복원용 — 이벤트 없이 개수/용량 직접 설정.</summary>
    public void RestorePotions(int count, int capacity)
    {
        PotionCapacity = Math.Max(0, capacity);
        PotionCount    = Math.Clamp(count, 0, PotionCapacity);
        OnPotionChanged?.Invoke(PotionCount, PotionCapacity);
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
        if (amount <= 0 || HealLocked) return;
        SetHp(Hp + amount);
    }
}
