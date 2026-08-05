using UnityEngine;

/// <summary>
/// gawain_noon_bloom(정오의 개화) — 정오에 진입하는 순간 발밑에 화상 지대가 한 번 깔린다.
///
/// 매 Tick에서 정오 진입 엣지(비정오→정오)를 감지해 플레이어 발밑에 불씨 지대를 1회 스폰한다.
/// (이벤트 구독 대신 폴링 — 유물 부착 순서·해제 정리에 안전.)
/// </summary>
public sealed class GawainNoonBloomEffect : RelicPartEffect
{
    private const float FieldRadius = 3f;
    private const float FieldLife   = 4f;
    private const float DpsRatio    = 0.15f;  // 유효 공격력 대비 초당 화상(정오 화상 dps와 동급 — 무료 지대라 상한)
    private const float BurnDur     = 2f;

    private bool _wasNoon;

    public GawainNoonBloomEffect() : base("gawain_noon_bloom") { }

    public override void Tick(float dt, PlayerController player)
    {
        var gauge = (player.RelicBehavior as GawainZenithRelic)?.Gauge;
        if (gauge == null) return;

        bool noon = gauge.IsNoon;
        if (noon && !_wasNoon)
        {
            float dps = player.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee) * DpsRatio;
            FireField.SpawnAt(player.gameObject, player.transform.position, FieldRadius, FieldLife, dps, BurnDur);
        }
        _wasNoon = noon;
    }
}
