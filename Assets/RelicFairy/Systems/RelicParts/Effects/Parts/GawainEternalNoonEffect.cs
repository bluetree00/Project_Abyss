using UnityEngine;

/// <summary>
/// gawain_eternal_noon(영원한 정오) — 코어 진화. 황혼(쿨다운)을 지워 정오를 상시 유지한다.
///
/// ZenithGauge를 정오 고정(SetHoldNoon)으로 만든다. 유물 부착이 파츠 활성보다 늦을 수 있어
/// OnAcquire에서 실패하면 Tick에서 게이지가 준비되는 즉시 다시 적용한다.
/// </summary>
public sealed class GawainEternalNoonEffect : RelicPartEffect
{
    private bool _applied;

    public GawainEternalNoonEffect() : base("gawain_eternal_noon") { }

    public override void OnAcquire(PlayerController player) => TryApply(player);

    public override void Tick(float dt, PlayerController player)
    {
        if (!_applied) TryApply(player);
    }

    public override void OnRemove(PlayerController player)
    {
        if (!_applied) return;
        (player.RelicBehavior as GawainZenithRelic)?.Gauge?.SetHoldNoon(false);
        _applied = false;
    }

    private void TryApply(PlayerController player)
    {
        var gauge = (player.RelicBehavior as GawainZenithRelic)?.Gauge;
        if (gauge == null) return;
        gauge.SetHoldNoon(true);
        _applied = true;
    }
}
