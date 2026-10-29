using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 해방기 · 악몽 클리어 → 랭킹 흐름(10-29 기획 설계 §5).
///   · 마지막 보스(리치)를 쓰러뜨린 순간 <see cref="OnFinalBossCleared"/> — 그 시기 · 클리어 시간 · 무기를 떠 두고 뒤끝 랭킹에 올린다(<see cref="RankingService"/>).
///   · 거점으로 돌아오면 랭킹방(2a 프리팹)이 <see cref="ConsumePendingRoom"/>로 「이번에 깬 시기」를 받아 그 방으로 옮기고 비석을 연다.
/// 봉인기 리치(봉인 → 붕괴)는 랭킹이 없다.
/// </summary>
public static class RankingFlow
{
    // ── Static ────────────────────────────────────────────────
    private static StoryEra? s_pendingRoom;

    private const string NightmareClearKey = "NightmareClear";   // 악몽을 한 번이라도 깼다(거점 「기록의 문」 악몽 갈래)

    // 10-29 설계 F6 — 클리어 시간 단계 칭호. 시간으로 바로 정해(서버 없이도 같다) 비석 기록 줄에 붙이고, 가장 좋은 단계를 세이브에 남긴다.
    // 목표 런 45분(플레이타임 설계 정본) 둘레 네 단계 · 악몽은 방이 단단해 5분씩 늦게. 실제 기록이 쌓이면 다시 맞춘다.
    private static readonly int[]    LiberationTierMinutes = { 30, 40, 50 };
    private static readonly int[]    NightmareTierMinutes  = { 35, 45, 55 };
    private static readonly string[] TierWords             = { "번개 같은 ", "날랜 ", "", "끈질긴 " };

    /// <summary>악몽을 한 번이라도 깼는가 — 거점 「기록의 문」에 악몽 방 갈래가 열린다(해방기 갈래는 <see cref="StoryProgress.HasEnded"/>).</summary>
    public static bool HasClearedNightmare => StoryProgress.IsSeen(NightmareClearKey);

    /// <summary>방금 깬 기록(비석의 「이번 기록」 줄) — 없으면 0.</summary>
    public static StoryEra LastEra { get; private set; }
    public static int LastCentis { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { s_pendingRoom = null; LastEra = StoryEra.Sealed; LastCentis = 0; }

    // ── Public Methods ────────────────────────────────────────

    /// <summary>마지막 보스 처치 — 해방기 · 악몽이면 기록을 올리고 귀환 뒤 랭킹방을 예약한다.</summary>
    public static void OnFinalBossCleared(GameRunSession run)
    {
        var era = StoryProgress.Era;
        if (era == StoryEra.Sealed || run?.RunStats == null) return;
        float seconds = run.RunStats.ClearSeconds;
        string weapon = run.Player != null && run.Player.WeaponManager != null && run.Player.WeaponManager.CurrentWeaponData != null
                      ? run.Player.WeaponManager.CurrentWeaponData.displayName : "";
        LastEra    = era;
        LastCentis = Mathf.RoundToInt(seconds * 100f);
        s_pendingRoom = era;
        if (era == StoryEra.NightmareMode) StoryProgress.MarkSeen(NightmareClearKey);
        int tier = TitleTier(era, LastCentis);
        StoryProgress.MarkSeen(TitleKey(era, tier));
        Debug.Log($"[Ranking] {era} 클리어 — {RankingService.Format(LastCentis)} · {weapon} · 칭호 {TitleName(era, tier)}");
        RankingService.SubmitAsync(era, seconds, weapon).Forget();
    }

    /// <summary>귀환 뒤 랭킹방으로 보낼 시기 — 한 번 읽으면 지운다(없으면 null).</summary>
    public static StoryEra? ConsumePendingRoom()
    {
        var e = s_pendingRoom;
        s_pendingRoom = null;
        return e;
    }

    /// <summary>클리어 시간 칭호 단계 — 0(가장 빠름) ~ 3.</summary>
    public static int TitleTier(StoryEra era, int centis)
    {
        var mins = TierMinutes(era);
        for (int i = 0; i < mins.Length; i++)
            if (centis <= mins[i] * 6000) return i;
        return mins.Length;
    }

    /// <summary>칭호 이름 — 「날랜 해방자」 · 「번개 같은 악몽 사냥꾼」.</summary>
    public static string TitleName(StoryEra era, int tier)
        => TierWords[Mathf.Clamp(tier, 0, TierWords.Length - 1)] + (era == StoryEra.NightmareMode ? "악몽 사냥꾼" : "해방자");

    /// <summary>한 단계 위 칭호의 시간(분) — 이미 맨 위면 0.</summary>
    public static int NextTierMinutes(StoryEra era, int tier)
    {
        var mins = TierMinutes(era);
        return tier <= 0 ? 0 : mins[Mathf.Min(tier, mins.Length) - 1];
    }

    /// <summary>이 시기에 얻은 가장 좋은 칭호 단계 — 없으면 -1.</summary>
    public static int BestTitleTier(StoryEra era)
    {
        for (int t = 0; t < TierWords.Length; t++)
            if (StoryProgress.IsSeen(TitleKey(era, t))) return t;
        return -1;
    }

    // ── Private Methods ───────────────────────────────────────

    private static int[] TierMinutes(StoryEra era) => era == StoryEra.NightmareMode ? NightmareTierMinutes : LiberationTierMinutes;

    private static string TitleKey(StoryEra era, int tier) => $"RankTitle{(int)era}_{tier}";
}
