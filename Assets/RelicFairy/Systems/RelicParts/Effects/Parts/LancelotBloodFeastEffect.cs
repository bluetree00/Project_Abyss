using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// lancelot_blood_feast(피의 만찬) — 코어 진화. 광란 중 적을 처치하면 그 적에 쌓인 출혈이 주변 적들에게 번진다.
///
/// OnKill(죽은 적)에서 광란 상태일 때만 발동. 죽은 적의 남은 출혈 총량을 반경 내 적들에게
/// 짧은 지속으로 재부여한다.
/// </summary>
public sealed class LancelotBloodFeastEffect : RelicPartEffect
{
    private const float SpreadRadius   = 5f;
    private const int   MaxTargets     = 4;
    private const float SpreadDuration = 3f;

    private static readonly List<MonsterBase> s_buffer = new();

    public LancelotBloodFeastEffect() : base("lancelot_blood_feast") { }

    public override void OnKill(GameObject deadEnemy, PlayerController player)
    {
        var madness = (player.RelicBehavior as LancelotMadnessRelic)?.Madness;
        if (madness == null || !madness.IsFrenzy || deadEnemy == null) return;

        float remaining = MonsterBleed.Remaining(deadEnemy);
        if (remaining <= 0f) return;

        // 출혈 분출 버스트 — 판정 반경(5m)과 크기 일치.
        ElementVfxPlayer.PlayBurst(RuneElement.Dark, deadEnemy.transform.position, SpreadRadius);

        int n = CombatQuery.GetNearbyEnemies(deadEnemy.transform.position, SpreadRadius, deadEnemy, MaxTargets, s_buffer);
        float dps = remaining / SpreadDuration;
        for (int i = 0; i < n; i++)
            MonsterBleed.Apply(s_buffer[i].gameObject, dps, SpreadDuration, player.gameObject);
    }
}
