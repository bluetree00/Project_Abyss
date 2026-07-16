using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// 랜슬롯 메커닉 — 광기(Madness). 공격 적중마다 광기 스택 획득.
/// 스택 누적/감쇠/광란 진입은 MadnessStack이, 적중 트리거는 이 패시브가 담당.
///
/// ⚠️ <b>적중 1회당 +1로 두면 보스전에서 게이지가 사실상 안 찬다.</b>
/// OnAttackHit은 <b>적중한 대상마다</b> 발동하므로, 잡몹 5마리를 한 번 베면 +5인 반면
/// 보스는 같은 스윙에 +1뿐이다 — 충전 속도가 대상 수에 정비례해 버린다.
/// 그래서 <b>상대의 격(MonsterGrade)</b>으로 가중치를 준다. 잡몹 수급은 그대로 두고
/// 보스·엘리트만 끌어올리므로, 일반 스테이지의 체감(대량 학살 → 즉시 충전)은 손상되지 않는다.
/// </summary>
public sealed class LancelotMadnessPassive : CharacterPassiveBase
{
    // 잡몹 한 번에 여럿 베는 상황과 비슷한 체감이 나도록 보스를 크게 잡았다.
    private const int StackCommon = 1;
    private const int StackRare   = 2;
    private const int StackElite  = 3;
    private const int StackBoss   = 5;

    public override string         PassiveName => "광기";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnAttackHit;

    public override bool CanApply(PlayerController ctrl, in PassiveContext ctx)
        => ctx.damage > 0f && ctrl.RelicBehavior is LancelotMadnessRelic;

    public override void Apply(PlayerController ctrl, in PassiveContext ctx)
    {
        if (ctrl.RelicBehavior is LancelotMadnessRelic lm)
            lm.AddStack(StacksFor(ctx.target));
    }

    /// <summary>대상 등급 → 획득 스택. 등급을 못 읽으면 기본 1(회귀 0).</summary>
    private static int StacksFor(GameObject target)
    {
        if (target == null) return StackCommon;

        var mb = target.GetComponentInParent<MonsterBase>();
        if (mb != null) return StacksForGrade(mb.Grade);

        // 훈련용 허수아비도 등급을 가진다 — 보스 더미를 세워 수급 밸런스를 검증할 수 있도록.
        var dummy = target.GetComponentInParent<TrainingDummy>();
        if (dummy != null) return StacksForGrade(dummy.Grade);

        return StackCommon;
    }

    private static int StacksForGrade(MonsterGrade grade) => grade switch
    {
        MonsterGrade.Boss  => StackBoss,
        MonsterGrade.Elite => StackElite,
        MonsterGrade.Rare  => StackRare,
        _                  => StackCommon,
    };
}
