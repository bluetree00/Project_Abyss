// WeaponEnhanceService.cs
using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 무기 강화/승급의 순수 계산·적용 로직(설계 #1 §4·§5). MonoBehaviour 아님(테스트 용이·컨벤션).
/// 결정성: 롤 RNG(System.Random)를 호출측(재련소 컨트롤러)이 주입 — masterSeed·visitCount·attemptIndex
/// 파생 시드로 save-scum을 무력화한다. 재료 소비는 RunFuelBank(강화재료).
/// 로드된 EnhanceTableSO 를 WeaponEnhanceCurve 에 설치해 씬 전환 중 유효 스탯 재계산에 사용한다.
/// </summary>
public static class WeaponEnhanceService
{
    private const string TableAddress = "EnhanceTable";

    private static EnhanceTableSO _table;
    public static EnhanceTableSO Table => _table;

    /// <summary>테이블을 설치하고 강화 곡선을 WeaponEnhanceCurve 에 연결한다(씬 전환 재계산용).</summary>
    public static void InstallTable(EnhanceTableSO table)
    {
        if (table == null) return;
        _table = table;
        WeaponEnhanceCurve.Evaluator = (raw, level, legendId) => raw * table.AttackMult(level, legendId);
    }

    /// <summary>테이블 1회 로드(Addressable) + 곡선 설치. 이미 로드됐으면 즉시 반환.</summary>
    public static async UniTask<EnhanceTableSO> EnsureLoadedAsync()
    {
        if (_table != null) return _table;
        try
        {
            var t = await Managers.AddressableManager.TryLoadAssetAsync<EnhanceTableSO>(TableAddress);
            if (t != null) InstallTable(t);
            else Debug.LogWarning($"[WeaponEnhanceService] '{TableAddress}' 로드 실패 — 기본 곡선 사용");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[WeaponEnhanceService] 테이블 로드 예외: {e.Message}");
        }
        return _table;
    }

    // ── 조회 ─────────────────────────────────────────────────────────

    public static int MaxEnhance(WeaponData w, EnhanceTableSO t)
        => (w == null || t == null) ? 0 : t.MaxFor(w.rarity);

    public static bool IsMaxed(WeaponData w, EnhanceTableSO t)
        => w != null && t != null && w.enhanceLevel >= MaxEnhance(w, t);

    public static float SuccessChance(WeaponData w, EnhanceTableSO t)
        => (w == null || t == null) ? 0f : t.SuccessAt(w.enhanceLevel);

    /// <summary>강화/승급 반영된 유효 공격력(연출/미리보기용).</summary>
    public static float EffectiveAttack(WeaponData w, EnhanceTableSO t)
    {
        if (w == null) return 0f;
        float raw = w.baseAttackRaw > 0f ? w.baseAttackRaw : w.baseAttack;
        if (t == null) return WeaponEnhanceCurve.Evaluate(raw, w.enhanceLevel, w.legendId);
        return raw * t.AttackMult(w.enhanceLevel, w.legendId);
    }

    private static bool IsSword(WeaponType type)
        => type == WeaponType.Katana || type == WeaponType.Greatsword;

    // ── 강화 ─────────────────────────────────────────────────────────

    /// <summary>돌발 이벤트 재료비 배수 반영 비용(≥0). 미리보기·차감 일관성 위해 공용.</summary>
    public static int CostWith(EnhanceTableSO t, int level, float costMult)
        => t == null ? 0 : Mathf.Max(0, Mathf.CeilToInt(t.CostAt(level) * Mathf.Max(0f, costMult)));

    /// <summary>
    /// 강화 1회 시도. 재료 차감 → 결정적 롤 → 성공(+1) / 실패(하락, 제물 흡수 가능).
    /// 안전장치 A(제물 흡수) + B(하한 0·파괴 없음). rng·fuel 은 호출측 소유.
    /// costMult/successBonus: 돌발 이벤트(할인/성공부스트) 반영. 기본 1/0(무보정).
    /// </summary>
    public static EnhanceResult TryEnhance(WeaponData target,
                                           EnhanceTableSO t, System.Random rng, RunFuelBank fuel,
                                           float costMult = 1f, float successBonus = 0f)
    {
        if (target == null || t == null || rng == null || fuel == null)
            return EnhanceResult.Reject(EnhanceOutcome.RejectInvalid);

        int max = MaxEnhance(target, t);
        if (target.enhanceLevel >= max)
            return EnhanceResult.Reject(EnhanceOutcome.RejectMaxed);

        int level = target.enhanceLevel;
        int cost  = CostWith(t, level, costMult);
        if (!fuel.TrySpend(FuelKind.EnhanceMaterial, cost))
            return EnhanceResult.Reject(EnhanceOutcome.RejectNoFuel);

        float chance = Mathf.Clamp01(t.SuccessAt(level) + successBonus);
        double roll  = rng.NextDouble();   // RNG 소비 1회 불변(결정성 유지) — 값만 결과에 실어 연출에 전달
        bool success = roll < chance;
        if (success)
        {
            int before = target.enhanceLevel;
            target.enhanceLevel = Mathf.Min(before + 1, max);
            target.RecomputeEnhancedStats();
            return new EnhanceResult
            {
                outcome = EnhanceOutcome.Success,
                beforeLevel = before, afterLevel = target.enhanceLevel,
                spent = cost, roll = roll, chance = chance,
            };
        }

        // 실패 — 대상 단계 하락(하한 0, 파괴 없음)
        int drop  = t.DropAt(level);
        int start = target.enhanceLevel;
        target.enhanceLevel = Mathf.Max(0, start - drop);
        target.RecomputeEnhancedStats();
        return new EnhanceResult
        {
            outcome = EnhanceOutcome.FailDropped,
            beforeLevel = start, afterLevel = target.enhanceLevel,
            spent = cost, roll = roll, chance = chance,
        };
    }

    // ── 승급(전설 분기) ───────────────────────────────────────────────

    /// <summary>
    /// 승급 시도. 조건: 강화 MAX + 미승급 + 검류 + 전설/타입 정합 + 재료 대량.
    /// 확정 성공(도박 아님) — 재료 대량 소비로 legendId 부여.
    /// </summary>
    public static PromoteResult TryPromote(WeaponData target, string legendId,
                                           EnhanceTableSO t, RunFuelBank fuel)
    {
        if (target == null || t == null || fuel == null || string.IsNullOrEmpty(legendId))
            return PromoteResult.Reject(PromoteOutcome.RejectInvalidLegend);

        if (!string.IsNullOrEmpty(target.legendId))
            return PromoteResult.Reject(PromoteOutcome.RejectAlreadyLegend);

        if (target.enhanceLevel < MaxEnhance(target, t))
            return PromoteResult.Reject(PromoteOutcome.RejectNotMaxed);

        if (!t.TryGetLegend(legendId, out var legend))
            return PromoteResult.Reject(PromoteOutcome.RejectInvalidLegend);

        // 검류만 승급(활은 파츠 계열). 전설에 타입 필터가 있으면 정확 일치 요구.
        if (!IsSword(target.weaponType))
            return PromoteResult.Reject(PromoteOutcome.RejectInvalidLegend);
        if (legend.weaponFilter != WeaponType.None && legend.weaponFilter != target.weaponType)
            return PromoteResult.Reject(PromoteOutcome.RejectInvalidLegend);

        int cost = Mathf.Max(0, legend.promoteCost);
        if (!fuel.TrySpend(FuelKind.EnhanceMaterial, cost))
            return PromoteResult.Reject(PromoteOutcome.RejectNoFuel);

        target.legendId = legend.legendId;
        target.RecomputeEnhancedStats();
        return new PromoteResult { outcome = PromoteOutcome.Success, legendId = legend.legendId, spent = cost };
    }
}
