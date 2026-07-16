using UnityEngine;

/// <summary>
/// 가웨인 — 태양의 열기(화상 부착). 공격 적중 시 태양 화상을 부여한다.
///
/// 구간마다 성격이 다르다 — '하루의 순환'이라는 정체성을 화상 하나로 표현한다.
///   여명(충전) : 씨앗을 심는다. 약한 화상, 짧게. (정오 진입 시 전부 터진다)
///   정오       : 절정. 강한 화상, 길게.
///   황혼(쿨다운): <b>잔열</b>. 대지가 아직 뜨거워 <b>가장 오래</b> 탄다. 피해는 여명 수준.
///
/// 예전엔 황혼에 아무것도 붙지 않아 15초가 통째로 죽은 시간이었다.
/// </summary>
public sealed class GawainSolarBurnPassive : CharacterPassiveBase
{
    private const float BurnTickInterval = 0.5f;

    // 구간별 화상 — (초당 피해 = 공격력 × ratio, 지속시간)
    private const float NoonDps = 0.15f, NoonDur = 6f;   // 정오 — 강하게, 길게
    private const float DawnDps = 0.10f, DawnDur = 4f;   // 여명 — 심는 단계
    private const float TwiDps  = 0.10f, TwiDur  = 8f;   // 황혼 — 잔열: 오래 탄다

    public override string         PassiveName => "태양의 열기";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnAttackHit;

    public override bool CanApply(PlayerController ctrl, in PassiveContext ctx)
        => ctx.target != null && ctx.damage > 0f
           && ctrl.RelicBehavior is GawainZenithRelic g
           && (g.DawnMode || g.FlameMode || g.TwilightMode);

    public override void Apply(PlayerController ctrl, in PassiveContext ctx)
    {
        if (ctrl.RelicBehavior is not GawainZenithRelic g) return;

        float effAtk = ctrl.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee);

        float ratio, dur;
        if      (g.FlameMode)    { ratio = NoonDps; dur = NoonDur; }
        else if (g.TwilightMode) { ratio = TwiDps;  dur = TwiDur;  }
        else                     { ratio = DawnDps; dur = DawnDur; }

        MonsterBurnHandler.Apply(ctx.target, effAtk * ratio, dur, BurnTickInterval, ctrl.gameObject);
    }
}
