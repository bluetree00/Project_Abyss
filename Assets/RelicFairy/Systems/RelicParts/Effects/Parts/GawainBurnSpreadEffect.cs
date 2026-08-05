using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// gawain_burn_spread(화상 전염) — 화상 걸린 적이 죽으면 근처 적 하나에게 화상이 옮는다.
///
/// OnKill(죽은 적)에서 그 적의 화상을 가장 가까운 살아있는 적 1체에게 그대로 복사한다.
/// </summary>
public sealed class GawainBurnSpreadEffect : RelicPartEffect
{
    private const float SpreadRadius = 4f;

    private static readonly List<MonsterBase> s_buffer = new();

    public GawainBurnSpreadEffect() : base("gawain_burn_spread") { }

    public override void OnKill(GameObject deadEnemy, PlayerController player)
    {
        if (deadEnemy == null) return;
        if (!deadEnemy.TryGetComponent<MonsterBurnHandler>(out var burn) || burn.Remaining <= 0f) return;

        int n = CombatQuery.GetNearbyEnemies(deadEnemy.transform.position, SpreadRadius, deadEnemy, 1, s_buffer);
        if (n > 0)
        {
            var target = s_buffer[0];
            MonsterBurnHandler.SpreadTo(deadEnemy, target.gameObject, player.gameObject);
            // 전염 연출: 죽은 적 → 옮은 적으로 불줄기 + 착탄 버스트.
            ElementVfxPlayer.PlayBeam(RuneElement.Fire, deadEnemy.transform.position, target.transform.position);
            ElementVfxPlayer.PlayBurst(RuneElement.Fire, target.transform.position, 1f);
        }
    }
}
