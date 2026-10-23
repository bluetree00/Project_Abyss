using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// 랜슬롯 메커닉 — 광기(Madness). 공격 <b>행동</b>(휘두름 · 스킬 시전 · 화살 한 발)마다 광기 스택 획득.
/// 스택 누적/감쇠/광란 진입은 MadnessStack이, 적중 트리거는 이 패시브가 담당.
///
/// 10-19 사용자 「랜슬롯 스택이 타격마다 쌓여 너무 바로 찬다 — 공격 횟수로 보는 등」: 예전엔 맞은 대상 1명 × 1타마다
/// (일반 1 · 정예 2 · 엘리트 3 · 보스 5)라 광역 · 다타 스킬이 대상 수 × 타 수로 불어나 몇 번 휘두르면 찼다.
/// 이제 한 행동에 한 번 — 기본 +1, 그 행동이 정예 · 엘리트 · 보스를 맞혔으면 +2, 3명 이상 맞혔으면 +1 더(행동당 최대 3).
/// 같은 행동 = 같은 공격 실행(<see cref="PlayerController.ActiveExecution"/>) · 같은 콤보 단계.
/// 실행이 없거나(늦게 맞는 투사체) 원거리 무기면 첫 적중부터 <see cref="LooseWindow"/>초를 한 행동으로 묶는다(화살 한 발 · 갈래 화살 한 번).
/// </summary>
public sealed class LancelotMadnessPassive : CharacterPassiveBase
{
    private const int   PerAction    = 1;      // 행동 한 번
    private const int   StrongAction = 2;      // 그 행동이 정예 · 엘리트 · 보스를 맞혔다
    private const int   CrowdBonus   = 1;      // 그 행동이 CrowdTargets명 이상을 맞혔다
    private const int   CrowdTargets = 3;
    private const float LooseWindow  = 0.2f;   // 실행 키가 없을 때 한 행동으로 묶는 시간

    private AbilityExecution _actionExec;
    private int   _actionStep = -1;
    private float _actionAt   = -999f;
    private int   _actionBase;    // 이번 행동의 기본 몫(1 또는 2)
    private int   _actionGiven;   // 이번 행동에 이미 준 스택
    private readonly HashSet<int> _actionTargets = new();

    public override string         PassiveName => "광기";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnAttackHit;

    public override bool CanApply(PlayerController ctrl, in PassiveContext ctx)
        => ctx.damage > 0f && ctrl.RelicBehavior is LancelotMadnessRelic;

    public override void Apply(PlayerController ctrl, in PassiveContext ctx)
    {
        if (ctrl.RelicBehavior is not LancelotMadnessRelic lm) return;

        bool ranged = ctx.weaponType == WeaponType.Bow || ctx.weaponType == WeaponType.Crossbow;
        var  exec   = ranged ? null : ctrl.ActiveExecution;
        bool same   = exec != null
            ? ReferenceEquals(exec, _actionExec) && ctx.comboStep == _actionStep
            : _actionExec == null && Time.time - _actionAt < LooseWindow;
        if (!same)
        {
            _actionExec  = exec;
            _actionStep  = ctx.comboStep;
            _actionAt    = Time.time;
            _actionBase  = PerAction;
            _actionGiven = 0;
            _actionTargets.Clear();
        }

        if (ctx.target != null) _actionTargets.Add(ctx.target.GetInstanceID());
        if (IsStrong(ctx.target)) _actionBase = StrongAction;

        // 같은 행동 안에서 더 센 적 · 셋째 대상이 뒤늦게 맞으면 차액만 더 준다
        int want = _actionBase + (_actionTargets.Count >= CrowdTargets ? CrowdBonus : 0);
        if (want <= _actionGiven) return;
        lm.AddActionStack(want - _actionGiven);
        _actionGiven = want;
    }

    /// <summary>정예 · 엘리트 · 보스인가(허수아비도 등급을 가진다 — 보스 더미로 수급을 잴 수 있게).</summary>
    private static bool IsStrong(GameObject target)
    {
        if (target == null) return false;
        var mb = target.GetComponentInParent<MonsterBase>();
        if (mb != null) return mb.Grade != MonsterGrade.Common;
        var dummy = target.GetComponentInParent<TrainingDummy>();
        return dummy != null && dummy.Grade != MonsterGrade.Common;
    }
}
