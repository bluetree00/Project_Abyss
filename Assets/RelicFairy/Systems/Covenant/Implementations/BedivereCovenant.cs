using UnityEngine;

/// <summary>
/// 베디비어의 서약 — 스킬을 포기하고 일반 공격을 극대화 (트레이드오프형: 쿨↔속도)
///
/// [선택]   일반 공격이 N체 동시 타격(인근 추가 대상 근사).
/// [강화]   동시 타격 대상 증가 + 일정 횟수마다 스킬 쿨타임 감소.
/// [각성]   일정 횟수마다 스킬 1회 즉시 발동 가능(쿨 리셋).
///
/// 데이터 인덱스: [0]쿨증가(P2 미적용) [1]동시타겟 [2]주기치(0=비활성) [3]모드(0쿨감/1즉발) [4]쿨감소초
/// ⚠️ P1 근사: 다중 타격을 인근 소량 AoE로, 주기 효과를 CooldownTracker로 구현.
///    스킬쿨 +50% 다운사이드는 StartCooldown이 Clamp01이라 미적용(P2 후속).
/// </summary>
public sealed class BedivereCovenant : CovenantBase
{
    private const int V_TARGETS = 1;
    private const int V_PERIOD  = 2;
    private const int V_MODE    = 3;
    private const int V_CDRED   = 4;

    public override string CovenantId => CovenantFactory.Bedivere;
    public override CovenantCategory Category => CovenantCategory.Tradeoff;

    public override string DisplayName         => "베디비어의 서약";
    public override string LoreText            => "베디비어 — 스킬을 포기하고 일반 공격을 극대화하는 서약을 전달했다";
    public override string BasicDescription    => "스킬 쿨타임 증가. 대신 일반 공격이 적 2체를 동시에 타격.";
    public override string EnhancedDescription => "동시 타격 대상 3체로 증가. 20회 공격마다 스킬 쿨타임 2초 감소.";
    public override string EvolvedDescription  => "일반 공격 30회마다 스킬 1회 즉시 발동 가능.";

    private int _count;

    private int   Targets => Mathf.Max(1, VI(V_TARGETS, 2));
    private int   Period  => VI(V_PERIOD, 0);
    private int   Mode    => VI(V_MODE, 0);
    private float CdRed   => V(V_CDRED, 0f);

    public override void OnAttackHit(GameObject target, float dmg)
    {
        // 다중 타격 근사: 추가 대상에게 소량 범위 피해
        if (target != null && Targets > 1)
            DealAoe(target.transform.position, 1.5f, 0.5f);

        if (Period <= 0) return;
        _count++;
        if (_count < Period) return;
        _count = 0;

        var tracker = Ctx?.Player?.CooldownTracker;
        if (tracker == null) return;
        if (Mode >= 1) tracker.ResetCooldown(SkillType.Q);   // 즉발(각성)
        else if (CdRed > 0f) tracker.ReduceAllCooldowns(CdRed); // 쿨 감소(강화)
    }

    public override void OnRoomEnter() => _count = 0;
}
