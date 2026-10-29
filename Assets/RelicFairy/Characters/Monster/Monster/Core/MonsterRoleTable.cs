using System.Collections.Generic;

/// <summary>
/// 해방기 재미 F3(10-29 설계 §6-2 · 2a) — 몬스터 역할(원거리 · 돌진)과 시기별 뽑기 가중치.
/// 봉인기는 스폰 표 가중치 그대로, 해방기 · 악몽은 원거리 · 돌진 몹을 더 자주 섞는다 — 붙으면 끝나던 무리에서 거리를 재야 하는 방으로.
/// 역할은 공격 모양으로 가른 표다(원거리 = MonsterRangedAttackSO를 쓰는 몹 · 돌진 = 대시/돌진 원뿔 · 10-29). 키 = 스폰 표 displayName.
/// </summary>
public static class MonsterRoleTable
{
    public enum Role { Melee, Ranged, Charger }

    // ── Constants ──
    private const float LiberatedMixWeight = 1.5f;
    private const float NightmareMixWeight = 1.8f;

    // ── Static ──
    private static readonly Dictionary<string, Role> Roles = new()
    {
        { "SpiderMonster",     Role.Ranged },
        { "FishmanMonster",    Role.Ranged },
        { "CyclopsMonster",    Role.Ranged },
        { "EvilMageMonster",   Role.Ranged },
        { "NagaWizardMonster", Role.Ranged },
        { "DragonMonster",     Role.Ranged },
        { "BattleBeeMonster",  Role.Charger },
        { "SalamanderMonster", Role.Charger },
    };

    // ── Public Methods ──
    public static Role Of(string displayName)
        => displayName != null && Roles.TryGetValue(displayName, out var r) ? r : Role.Melee;

    /// <summary>뽑기 가중치 배율 — 봉인기 1 · 해방기 · 악몽은 원거리 · 돌진만 올린다.</summary>
    public static float EraWeight(SpawnEntry e, StoryEra era)
    {
        if (e == null || era == StoryEra.Sealed || Of(e.displayName) == Role.Melee) return 1f;
        return era == StoryEra.NightmareMode ? NightmareMixWeight : LiberatedMixWeight;
    }
}
