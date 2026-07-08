using System;
using System.Collections.Generic;

/// <summary>
/// 런 지속 연료 저장소(강화재료·원석). 이벤트방이 생산, #1 무기강화/#2 룬재련이 소비(TrySpend).
/// 방·챕터를 넘어 런 내내 유지되며, 미소비분은 런 종료 시 abyssEssence(메타)로 환산된다(GameRunSession.EndRun).
/// GameRunSession이 프로퍼티로 보유하는 순수 C# 클래스(풀-안전, Unity API 무의존).
/// </summary>
public sealed class RunFuelBank
{
    private readonly Dictionary<FuelKind, int> _amounts = new();

    /// <summary>연료 잔량 변경 시 발행(HUD 연동).</summary>
    public event Action OnFuelChanged;

    public int Get(FuelKind kind) => _amounts.TryGetValue(kind, out var v) ? v : 0;

    /// <summary>연료 추가(음수는 감산, 0 미만으로는 안 내려감).</summary>
    public void Add(FuelKind kind, int amount)
    {
        if (amount == 0) return;
        _amounts[kind] = Math.Max(0, Get(kind) + amount);
        OnFuelChanged?.Invoke();
    }

    /// <summary>차감 시도 — 잔량 부족이면 false(무변경). #1/#2 소비처가 호출.</summary>
    public bool TrySpend(FuelKind kind, int amount)
    {
        if (amount <= 0) return true;
        if (Get(kind) < amount) return false;
        _amounts[kind] = Get(kind) - amount;
        OnFuelChanged?.Invoke();
        return true;
    }

    // ── 세이브 캡처/복원 (종류별 스칼라) ─────────────────
    public int EnhanceMaterial => Get(FuelKind.EnhanceMaterial);
    public int RuneOre         => Get(FuelKind.RuneOre);

    /// <summary>세이브 복원용 무이벤트 세팅(≥0 클램프).</summary>
    public void RestoreRaw(int enhanceMaterial, int runeOre)
    {
        _amounts[FuelKind.EnhanceMaterial] = Math.Max(0, enhanceMaterial);
        _amounts[FuelKind.RuneOre]         = Math.Max(0, runeOre);
    }

    /// <summary>런 종료 메타 환산용 총 잔량(환산율은 호출측 결정).</summary>
    public int TotalRemaining()
    {
        int sum = 0;
        foreach (var kv in _amounts) sum += kv.Value;
        return sum;
    }
}
