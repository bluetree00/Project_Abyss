using UnityEngine;

/// <summary>
/// lancelot_endless_frenzy(끝나지 않는 광란) — 코어 진화. 광란이 끝날 때 광기 스택을 소모하고 유지한다.
///
/// MadnessStack에 '광란 1회당 자동 재점화'를 켠다(스택 MAX 유지 → +40% 공격 보너스 보존).
/// 유물 부착이 파츠 활성보다 늦을 수 있어 OnAcquire 실패 시 Tick에서 준비되는 즉시 다시 적용한다.
/// </summary>
public sealed class LancelotEndlessFrenzyEffect : RelicPartEffect
{
    private bool _applied;

    public LancelotEndlessFrenzyEffect() : base("lancelot_endless_frenzy") { }

    public override void OnAcquire(PlayerController player) => TryApply(player);

    public override void Tick(float dt, PlayerController player)
    {
        if (!_applied) TryApply(player);
    }

    public override void OnRemove(PlayerController player)
    {
        if (!_applied) return;
        (player.RelicBehavior as LancelotMadnessRelic)?.Madness?.SetEndlessFrenzy(false);
        _applied = false;
    }

    private void TryApply(PlayerController player)
    {
        var madness = (player.RelicBehavior as LancelotMadnessRelic)?.Madness;
        if (madness == null) return;
        madness.SetEndlessFrenzy(true);
        _applied = true;
    }
}
