using UnityEngine;

/// <summary>
/// 가웨인 패시브2(잔열) — <b>처치가 다음 해를 앞당긴다.</b>
///
/// 정오가 아닌 구간(황혼·여명)에서 적을 처치하면 충전 속도가 빨라진다.
/// 게이지를 '채우는' 게 아니라 <b>시간을 '당기는'</b> 것 — 적중마다 스택을 쌓는 유물과 구조가 다르다.
/// 잘 싸우면 해가 빨리 뜨고, 손을 놓으면 정해진 시간이 그대로 흐른다.
///
/// ⚠️ 예전엔 황혼 한정이었고, 그마저도 ZenithGauge가 충전 진입 시 가속을 0으로 지워서
///    <b>단 1%도 반영되지 않았다</b>(잔열이 100% 무효). 그 버그를 고치고 여명까지 열었다.
///
/// 수치: RELIC_STAT_DATA(gawain) 슬롯 15(처치 충전 가속).
/// </summary>
public sealed class GawainAfterglowPassive : CharacterPassiveBase
{
    private const string RelicKey = "gawain";
    private const int V_TWILIGHT_ACCEL = 15;

    public override string         PassiveName => "태양의 잔열";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnKill;

    public override bool CanApply(PlayerController ctrl, in PassiveContext ctx)
        => ctrl.RelicBehavior is GawainZenithRelic gz && gz.Gauge != null
           && !gz.Gauge.IsNoon;   // 정오 중엔 이미 보상을 받는 중이라 의미 없다

    public override void Apply(PlayerController ctrl, in PassiveContext ctx)
    {
        if (ctrl.RelicBehavior is not GawainZenithRelic gz || gz.Gauge == null) return;

        float accel = Managers.RelicStatData != null
            ? Managers.RelicStatData.Get(RelicKey, V_TWILIGHT_ACCEL, 0.2f)
            : 0.2f;

        gz.Gauge.AddChargeAccel(accel);
    }
}
