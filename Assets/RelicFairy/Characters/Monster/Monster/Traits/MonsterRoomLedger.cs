using System.Collections.Generic;

/// <summary>
/// 방 하나에서 나온 시기 특성 · 정예 접두어 기록(10-11 정예 접두어 — 설계서 §7-2 ①). 방에 들어설 때(<see cref="GameRunSession.SetCurrentRoomKind"/>) 비운다.
/// 예전엔 스포너마다 따로 세어(한 방에 스포너 3~4개) 「방당 특성 종류 2」가 실제로는 스포너당이었다.
/// 클리어 보상(<see cref="RoomRewardTable"/>)이 「접두어 정예가 나온 일반방」을 여기서 읽는다.
/// </summary>
public static class MonsterRoomLedger
{
    // ── Static ─────────────────────────────────────────────────
    private static readonly HashSet<MonsterTraitKind> s_traits   = new();
    private static readonly HashSet<MonsterTraitKind> s_prefixes = new();

    // ── Properties ─────────────────────────────────────────────
    /// <summary>이 방에서 나온 시기 특성 종류(접두어 제외) — 방당 종류 2 제한.</summary>
    public static HashSet<MonsterTraitKind> Traits => s_traits;
    /// <summary>이 방에서 나온 정예 접두어 수(같은 접두어는 방당 하나라 종류 수 = 마리 수).</summary>
    public static int  PrefixCount       => s_prefixes.Count;
    public static bool HadPrefixedElite  => s_prefixes.Count > 0;

    // ── Public Methods ─────────────────────────────────────────
    public static bool HasPrefix(MonsterTraitKind k) => s_prefixes.Contains(k);
    public static bool HasTrait(MonsterTraitKind k)  => s_traits.Contains(k);
    public static void AddPrefix(MonsterTraitKind k) => s_prefixes.Add(k);

    public static void Reset()
    {
        s_traits.Clear();
        s_prefixes.Clear();
    }
}
