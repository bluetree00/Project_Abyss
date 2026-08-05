using UnityEngine;

/// <summary>
/// lancelot_lasting_madness(식지 않는 광기) — 광란이 끝나도 광기 스택이 절반 남는다.
///
/// MadnessStack의 광란 종료 시 초기화 비율을 0.5로 설정한다. 유물 부착이 파츠 활성보다
/// 늦을 수 있어 OnAcquire 실패 시 Tick에서 준비되는 즉시 다시 적용한다.
/// </summary>
public sealed class LancelotLastingMadnessEffect : RelicPartEffect
{
    private const float RetainRatio = 0.5f;

    private bool _applied;

    public LancelotLastingMadnessEffect() : base("lancelot_lasting_madness") { }

    public override void OnAcquire(PlayerController player) => TryApply(player);

    public override void Tick(float dt, PlayerController player)
    {
        if (!_applied) TryApply(player);
    }

    public override void OnRemove(PlayerController player)
    {
        if (!_applied) return;
        (player.RelicBehavior as LancelotMadnessRelic)?.Madness?.SetRetainRatio(0f);
        _applied = false;
    }

    private void TryApply(PlayerController player)
    {
        var madness = (player.RelicBehavior as LancelotMadnessRelic)?.Madness;
        if (madness == null) return;
        madness.SetRetainRatio(RetainRatio);
        _applied = true;
    }
}
