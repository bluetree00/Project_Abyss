using UnityEngine;

/// <summary>
/// 아서왕의 서약 — 집중의 힘 (트레이드오프형: 범위↔정밀)
///
/// [선택]   범위 -50%, 단일 적중 판정 N회(피해 ×N로 근사).
/// [강화]   판정 횟수 증가. 같은 적 연속 적중 시 약점 누적.
/// [각성]   연속 임계 도달 시 약점 낙인 → 해당 적 판정 +1.
///
/// 데이터 인덱스: [0]범위배율(P2 미적용) [1]판정횟수 [2]연속임계(0=비활성) [3]낙인추가판정
/// ⚠️ P1 근사: 판정 N회를 ModifyOutgoingDamage ×N로 구현. 범위 -50%(다운사이드)와
///    적 행동간격 디버프는 P2 공격 변환 seam 필요 → 현재는 다운사이드 미적용으로 잠정 과강.
/// </summary>
public sealed class ArthurCovenant : CovenantBase
{
    private const int V_HITS   = 1;
    private const int V_STREAK = 2;
    private const int V_BRAND  = 3;

    public override string CovenantId => CovenantFactory.Arthur;
    public override CovenantCategory Category => CovenantCategory.Tradeoff;

    public override string DisplayName         => "아서왕의 서약";
    public override string LoreText            => "아서왕 — 집중의 힘을 서약으로 봉인했다";
    public override string BasicDescription    => "모든 공격 범위 -50%. 대신 단일 적 적중 시 판정 횟수 2회로 증가.";
    public override string EnhancedDescription => "판정 횟수 3회로 증가. 같은 적 10회 연속 적중 시 약점이 누적된다.";
    public override string EvolvedDescription  => "연속 15회 같은 적 적중 시 약점 낙인 — 이후 판정 횟수 +1 추가.";

    private GameObject _lastTarget;
    private int        _streak;
    private GameObject _branded;

    private int Hits         => Mathf.Max(1, VI(V_HITS, 2));
    private int StreakNeeded  => VI(V_STREAK, 0);
    private int BrandBonus    => VI(V_BRAND, 0);

    public override void ModifyOutgoingDamage(ref float damage, CombatContext ctx)
    {
        int hits = Hits;
        if (BrandBonus > 0 && ctx.Target != null && ctx.Target == _branded)
            hits += BrandBonus;
        damage *= hits;
    }

    public override void OnAttackHit(GameObject target, float dmg)
    {
        if (target == null) return;
        if (target != _lastTarget) { _lastTarget = target; _streak = 1; return; }
        _streak++;
        if (StreakNeeded > 0 && _streak >= StreakNeeded)
            _branded = target; // 약점 낙인(각성). 강화의 행동간격 디버프는 P1 미구현.
    }

    public override void OnRoomEnter() { _lastTarget = null; _streak = 0; _branded = null; }
}
