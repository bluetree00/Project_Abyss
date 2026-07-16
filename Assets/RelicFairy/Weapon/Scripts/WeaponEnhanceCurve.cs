// WeaponEnhanceCurve.cs
using System;

/// <summary>
/// 강화 레벨/승급 → 유효 공격력 곡선의 정적 진입점.
/// WeaponData 가 씬 전환/복원 중에도 Managers 의존 없이 유효 공격력을 재계산할 수 있도록
/// 곡선을 정적 함수로 노출한다.
///
/// 기본은 내장 선형 곡선(레벨당 +10%). 재련소 데이터(EnhanceTableSO)가 로드되면
/// WeaponEnhanceService 가 Evaluator 를 실제 곡선으로 교체한다(P3).
/// legendId(승급 전설)별 배수는 P3에서 Evaluator 교체 시 함께 반영된다.
/// </summary>
public static class WeaponEnhanceCurve
{
    /// <summary>(rawAttack, level, legendId) → 유효 공격력. 기본 = 레벨당 +10% 선형.</summary>
    public static Func<float, int, string, float> Evaluator = DefaultEvaluator;

    public static float Evaluate(float rawAttack, int level, string legendId)
        => (Evaluator ?? DefaultEvaluator)(rawAttack, level, legendId);

    private static float DefaultEvaluator(float rawAttack, int level, string legendId)
    {
        if (level <= 0) return rawAttack;
        return rawAttack * (1f + 0.10f * level);
    }
}
