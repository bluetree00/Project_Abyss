using UnityEngine;

/// <summary>
/// 가웨인 — 태양의 열기(화상 부착). 황혼 적중 시 잔열 화상을 부여한다.
///
///   황혼(쿨다운): <b>잔열</b>. 대지가 아직 뜨거워 <b>오래</b> 탄다 — 예전엔 황혼에 아무것도 붙지 않아 15초가 통째로 죽은 시간이었다.
///   정오       : 타오르는 정오 진화만 — 강한 화상, 길게(넘친 공속만큼 세진다 · <see cref="RelicEvolution.BurnMul"/>).
///   여명       : 붙이지 않는다 — 여명 적중은 허브의 태양흔이 맡는다(10-22 D11 「화상 심기 → 정오 폭발」을 태양흔 · 개화 하나로).
/// </summary>
public sealed class GawainSolarBurnPassive : CharacterPassiveBase
{
    private const float BurnTickInterval = 0.5f;

    // 구간별 화상 — (초당 피해 = 공격력 × ratio, 지속시간)
    private const float NoonDps = 0.15f, NoonDur = 6f;   // 정오(타오르는 정오 진화) — 강하게, 길게
    private const float TwiDps  = 0.10f, TwiDur  = 8f;   // 황혼 — 잔열: 오래 탄다

    public override string         PassiveName => "태양의 열기";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnAttackHit;

    public override bool CanApply(PlayerController ctrl, in PassiveContext ctx)
        => ctx.target != null && ctx.damage > 0f
           && ctrl.RelicBehavior is GawainZenithRelic g
           && (g.TwilightMode || (g.FlameMode && BlazingNoon));

    /// <summary>타오르는 정오 진화 — 정오 화상은 진화 몫으로 남긴다(진화 글 「넘친 공격속도만큼 태양 화상이 세진다」).</summary>
    private static bool BlazingNoon => AppBootstrapper.Instance?.Loadout?.RelicEvolutionId == "evo_gawain_combo";

    public override void Apply(PlayerController ctrl, in PassiveContext ctx)
    {
        if (ctrl.RelicBehavior is not GawainZenithRelic g) return;

        float effAtk = ctrl.RuntimeStats.AttackPower * RelicEvolution.BurnMul(ctrl);   // 유물 피해 = 근접 · 원거리 중 큰 값(10-14 A5) · 타오르는 정오 진화면 넘친 공속만큼(C)

        float ratio = g.FlameMode ? NoonDps : TwiDps;
        float dur   = g.FlameMode ? NoonDur : TwiDur;

        MonsterBurnHandler.Apply(ctx.target, effAtk * ratio, dur, BurnTickInterval, ctrl.gameObject);
    }
}
