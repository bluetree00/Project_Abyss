using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 보스전 · 챕터 전투 기록(11-02 Q14 사용자 「랭킹을 더 확대 — 방 클리어 기록과 보스 클리어 기록을 신기록 기점으로 · 지금 1등 · 2등 클리어 시간도」).
/// 보스 처치 순간 <see cref="Capture"/> — 보스전 시간(보스방에 든 뒤 싸운 시간) · 챕터 전투 시간(그 챕터 방들에서 싸운 시간 합 · 전투 시계)을
/// 개인 최고(메타 기록 · 작을수록 좋다)와 견주고, 신기록이면 뒤끝 표(<c>RANK_BOSS_n</c> · <c>RANK_ROOMS_n</c>, 콘솔에 있으면)에 올린다.
/// 유물 기억 카드 뒤 <see cref="ShowAsync"/> — 「보스전 0:52.41 신기록 · 1등 0:31.00 · 2등 0:35.20」 알림 줄(표가 없으면 내 최고만).
/// </summary>
public static class RecordBoard
{
    private const string BossKey  = "bestBoss";    // + 챕터 번호 · 값 = 1/100초
    private const string RoomsKey = "bestRooms";
    public const string BossTablePrefix  = "RANK_BOSS_";
    public const string RoomsTablePrefix = "RANK_ROOMS_";

    private static int  s_chapter, s_bossCentis, s_roomsCentis;
    private static bool s_bossNew, s_roomsNew, s_pending;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_pending = false;

    /// <summary>보스 처치 순간 — 시간을 떠 개인 최고와 견준다 · 신기록이면 올린다(기다리지 않는다).</summary>
    public static void Capture(GameRunSession run)
    {
        if (run?.RunStats == null) return;
        s_chapter = (int)run.CurrentChapter;
        float boss = run.RunStats.CombatSeconds - run.BossCombatStart;
        s_bossCentis  = boss > 0.5f ? Mathf.RoundToInt(boss * 100f) : 0;
        s_roomsCentis = Mathf.RoundToInt(run.RunStats.CombatSeconds * 100f);
        var data = BackendGameData.Instance?.Data;
        s_bossNew  = data != null && data.SetRecordMin(BossKey + s_chapter, s_bossCentis);
        s_roomsNew = data != null && data.SetRecordMin(RoomsKey + s_chapter, s_roomsCentis);
        if (s_bossNew || s_roomsNew) BackendGameData.Instance.SaveAsync().Forget();
        s_pending = s_bossCentis > 0 || s_roomsCentis > 0;

        var w = run.Player != null && run.Player.WeaponManager != null ? run.Player.WeaponManager.CurrentWeaponData : null;
        string label = (w != null ? w.displayName : "") + " · " + StoryProgress.Era;
        if (s_bossNew)  RankingService.SubmitTableAsync(BossTablePrefix + s_chapter, s_bossCentis, label).Forget();
        if (s_roomsNew) RankingService.SubmitTableAsync(RoomsTablePrefix + s_chapter, s_roomsCentis, label).Forget();
        Debug.Log($"[Record] Ch{s_chapter} 보스전 {RankingService.Format(s_bossCentis)}{(s_bossNew ? " 신기록" : "")} · 챕터 전투 {RankingService.Format(s_roomsCentis)}{(s_roomsNew ? " 신기록" : "")}");
    }

    /// <summary>유물 기억 카드 뒤 알림 줄 둘(보스전 · 챕터 전투). 순위는 뒤끝에서 읽어 오므로 부르는 쪽은 기다리지 않는다(Forget).</summary>
    public static async UniTask ShowAsync()
    {
        if (!s_pending) return;
        s_pending = false;
        int ch = s_chapter;
        var bossTop  = await TopOrEmpty(BossTablePrefix + ch);
        var roomsTop = await TopOrEmpty(RoomsTablePrefix + ch);
        if (s_bossCentis > 0)  ItemEffectVfxHelper.ShowNotice(Line(Loc.T("보스전"), s_bossCentis, s_bossNew, BossKey + ch, bossTop));
        if (s_roomsCentis > 0) ItemEffectVfxHelper.ShowNotice(Line(Loc.F("챕터 {0} 전투", ch), s_roomsCentis, s_roomsNew, RoomsKey + ch, roomsTop));
    }

    /// <summary>순위 상위 둘 — 뒤끝이 2.5초 안에 답하지 않으면(오프라인 · 미로그인) 빈 목록(내 기록만 보인다 · 11-02 Q32).</summary>
    private static async UniTask<List<RankingService.Entry>> TopOrEmpty(string table)
    {
        var (answered, list) = await UniTask.WhenAny(RankingService.LoadTopAsync(table, 2), UniTask.Delay(2500, ignoreTimeScale: true));
        return answered && list != null ? list : new List<RankingService.Entry>();
    }

    private static string Line(string what, int centis, bool isNew, string key, List<RankingService.Entry> top)
    {
        var sb = new StringBuilder(64);
        sb.Append(what).Append(' ').Append(RankingService.Format(centis));
        if (isNew) sb.Append("  <color=").Append(UIPalette.GoldHex).Append('>').Append(Loc.T("신기록")).Append("</color>");
        else sb.Append(" · ").Append(Loc.T("내 최고")).Append(' ').Append(RankingService.Format(BackendGameData.Instance?.Data?.GetRecord(key) ?? 0));
        if (top.Count > 0) sb.Append(" · ").Append(Loc.T("1등")).Append(' ').Append(RankingService.Format(top[0].Centis));
        if (top.Count > 1) sb.Append(" · ").Append(Loc.T("2등")).Append(' ').Append(RankingService.Format(top[1].Centis));
        return sb.ToString();
    }
}
