using UnityEngine;

/// <summary>
/// 랜슬롯 패시브 — 광란의 학살. 광란(Frenzy) 중 적을 처치하면 광란 지속을 소폭 연장한다.
/// (구 '배신의 대가' 빈틈 해제 → 리워크로 빈틈 제거되어 '처치 시 광란 유지'로 재활용)
/// </summary>
public sealed class LancelotBetrayalPassive : CharacterPassiveBase
{
    private const float ExtendPerKill = 0.5f;

    public override string         PassiveName => "광란의 학살";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnKill;

    public override bool CanApply(PlayerController ctrl, in PassiveContext ctx)
        => ctrl.RelicBehavior is LancelotMadnessRelic lm && lm.IsFrenzy;

    public override void Apply(PlayerController ctrl, in PassiveContext ctx)
    {
        if (ctrl.RelicBehavior is LancelotMadnessRelic lm)
            lm.Madness?.ExtendFrenzy(ExtendPerKill);
    }
}
