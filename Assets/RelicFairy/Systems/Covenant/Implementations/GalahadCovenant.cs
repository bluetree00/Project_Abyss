using UnityEngine;

/// <summary>
/// 갤러해드의 서약 — 확률을 버리고 확신으로 싸운다 (트레이드오프형: 치명타↔안정)
///
/// [선택]   치명타 확률 0% 고정. 모든 공격의 최소 피해가 최대 피해의 floor%로 보장.
/// [강화]   최소 피해 보장 상승. N회 연속 공격 시 다음 1회 확정 치명타.
/// [각성]   확정 치명타 주기 단축. 확정 치명타 직후 maxWindow초간 모든 공격 최대 피해 고정.
///
/// 데이터 인덱스: [0]예약 [1]최소피해비 [2]확정치명타 주기(0=비활성) [3]최대고정창(초)
/// 동작: CombatCalculator.RollCrit이 TryProvideCritOverride를 질의 → 일반 굴림 대체.
/// </summary>
public sealed class GalahadCovenant : CovenantBase
{
    private const int V_FLOOR  = 1;
    private const int V_STREAK = 2;
    private const int V_WINDOW = 3;

    public override string CovenantId => CovenantFactory.Galahad;
    public override CovenantCategory Category => CovenantCategory.Tradeoff;

    public override string DisplayName         => "갤러해드의 서약";
    public override string LoreText            => "갤러해드 — 확률을 버리고 확신으로 싸우는 서약을 전달했다";
    public override string BasicDescription    => "치명타 확률 0% 고정. 모든 공격의 최소 피해가 최대 피해의 80%로 보장.";
    public override string EnhancedDescription => "최소 피해 보장 90%로 상승. 20회 연속 공격 시 다음 1회 확정 치명타.";
    public override string EvolvedDescription  => "확정 치명타 주기 15회로 단축. 직후 3초간 모든 공격이 최대 피해로 고정.";

    // ── 런타임 상태 ──────────────────────────────────────
    private int   _streak;
    private float _maxWindowEnd;

    private float FloorRatio   => V(V_FLOOR, 0.80f);
    private int   StreakNeeded => VI(V_STREAK, 0);   // 0 = 확정 치명타 비활성(선택 단계)
    private float MaxWindow    => V(V_WINDOW, 0f);

    // ── 치명타 오버라이드 ────────────────────────────────
    public override bool TryProvideCritOverride(WeaponData weapon, out bool forceCrit, out float minFloorRatio)
    {
        // 각성: 확정 치명타 직후 창 동안 모든 공격 최대 피해 고정
        if (MaxWindow > 0f && Time.time < _maxWindowEnd)
        {
            forceCrit = true; minFloorRatio = 0f; return true;
        }

        // 강화+: 누적 N회 도달 시 이번 1회 확정 치명타 (소비 후 카운터 리셋)
        if (StreakNeeded > 0 && _streak >= StreakNeeded)
        {
            _streak = 0;
            if (MaxWindow > 0f) _maxWindowEnd = Time.time + MaxWindow;
            forceCrit = true; minFloorRatio = 0f; return true;
        }

        // 기본: 치명타 억제 + 최소피해 하한
        forceCrit = false; minFloorRatio = FloorRatio; return true;
    }

    // ── 이벤트 ──────────────────────────────────────────
    public override void OnAttackHit(GameObject target, float dmg)
    {
        // 확정 치명타 주기 카운트 (강화 이상에서만)
        if (StreakNeeded > 0) _streak++;
    }
}
