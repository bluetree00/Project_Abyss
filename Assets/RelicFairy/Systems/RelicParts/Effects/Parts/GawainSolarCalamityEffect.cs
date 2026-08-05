using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// gawain_solar_calamity(태양의 재앙) — 코어 진화. 화상 걸린 적이 죽으면 쌓인 화상이 주변 무리에 번진다.
///
/// 전염(gawain_burn_spread)의 광역 상위판: 1체가 아니라 반경 내 다수에게 죽은 적의 화상을 복사한다.
/// </summary>
public sealed class GawainSolarCalamityEffect : RelicPartEffect
{
    private const float CalamityRadius = 6f;
    private const int   MaxTargets     = 5;

    private static readonly List<MonsterBase> s_buffer = new();

    public GawainSolarCalamityEffect() : base("gawain_solar_calamity") { }

    public override void OnKill(GameObject deadEnemy, PlayerController player)
    {
        if (deadEnemy == null) return;
        if (!deadEnemy.TryGetComponent<MonsterBurnHandler>(out var burn) || burn.Remaining <= 0f) return;

        // 광역 발화 버스트 — 판정 반경(6m)과 크기 일치.
        ElementVfxPlayer.PlayBurst(RuneElement.Fire, deadEnemy.transform.position, CalamityRadius);

        int n = CombatQuery.GetNearbyEnemies(deadEnemy.transform.position, CalamityRadius, deadEnemy, MaxTargets, s_buffer);
        for (int i = 0; i < n; i++)
            MonsterBurnHandler.SpreadTo(deadEnemy, s_buffer[i].gameObject, player.gameObject);
    }
}
