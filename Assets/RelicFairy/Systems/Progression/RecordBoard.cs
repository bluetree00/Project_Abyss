using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 보스전 · 챕터 전투 기록(11-02 Q14 사용자 「랭킹을 더 확대 — 방 클리어 기록과 보스 클리어 기록을 신기록 기점으로 · 지금 1등 · 2등 클리어 시간도」).
/// 보스 처치 순간 <see cref="Capture"/> — 보스전 시간(보스방에 든 뒤 싸운 시간) · 챕터 전투 시간(그 챕터 방들에서 싸운 시간 합 · 전투 시계)을
/// 개인 최고(메타 기록 · 작을수록 좋다)와 견주고, 신기록이면 뒤끝 표(<c>RANK_BOSS_n_LIB</c> · <c>RANK_ROOMS_n_NM</c> 등, 콘솔에 있으면)에 올린다.
/// 11-03 사용자 「(챕터 기록을 시기별로) 나눌 거고」 — 표 · 개인 최고 모두 런을 시작한 시기별(해방기 LIB · 악몽 NM). 봉인기는 기기 기록만(랭킹 없음).
/// 유물 기억 카드 뒤 <see cref="ShowAsync"/> — 기록 카드(<see cref="UI_RecordCard"/> · 11-02 사용자 「실시간 랭킹 팝업 — 제대로 된 연출과 구성」):
/// 시간 · 신기록 도장 · 모든 플레이어 중 내 순위(전체 인원) · 1위. 표가 없으면 내 최고만.
/// </summary>
public static class RecordBoard
{
    private const string BossKey  = "bestBoss";    // + 챕터 번호 + _시기 · 값 = 1/100초
    private const string RoomsKey = "bestRooms";
    private const string BossTablePrefix  = "RANK_BOSS_";
    private const string RoomsTablePrefix = "RANK_ROOMS_";

    private static int  s_chapter, s_bossCentis, s_roomsCentis;
    private static StoryEra s_era;
    private static bool s_bossNew, s_roomsNew, s_pending;
    private static UniTask<bool> s_bossSubmit, s_roomsSubmit;   // 올리기 — 카드가 순위를 읽기 전에 기다린다

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_pending = false;

    /// <summary>보스 처치 순간 — 시간을 떠 개인 최고와 견준다 · 신기록이면 올린다(기다리지 않는다).</summary>
    public static void Capture(GameRunSession run)
    {
        if (run?.RunStats == null) return;
        s_chapter = (int)run.CurrentChapter;
        s_era     = run.StartEra;   // 런을 시작한 시기 — 봉인기 리치 붕괴로 시기가 바뀌어도 이 런은 봉인기
        float boss = run.RunStats.CombatSeconds - run.BossCombatStart;
        s_bossCentis  = boss > 0.5f ? Mathf.RoundToInt(boss * 100f) : 0;
        s_roomsCentis = Mathf.RoundToInt(run.RunStats.CombatSeconds * 100f);
        var data = BackendGameData.Instance?.Data;
        s_bossNew  = data != null && data.SetRecordMin(KeyOf(true, s_chapter, s_era), s_bossCentis);
        s_roomsNew = data != null && data.SetRecordMin(KeyOf(false, s_chapter, s_era), s_roomsCentis);
        if (s_bossNew || s_roomsNew) BackendGameData.Instance.SaveAsync().Forget();
        s_pending = s_bossCentis > 0 || s_roomsCentis > 0;

        var w = run.Player != null && run.Player.WeaponManager != null ? run.Player.WeaponManager.CurrentWeaponData : null;
        string label = w != null ? w.displayName : "";   // 표가 시기별이라 무기 이름만(해방기 · 악몽 표와 같다)
        string bossTable = TableOf(true, s_chapter, s_era), roomsTable = TableOf(false, s_chapter, s_era);
        s_bossSubmit  = s_bossNew  && bossTable  != null ? RankingService.SubmitTableAsync(bossTable,  s_bossCentis,  label).Preserve() : UniTask.FromResult(false);
        s_roomsSubmit = s_roomsNew && roomsTable != null ? RankingService.SubmitTableAsync(roomsTable, s_roomsCentis, label).Preserve() : UniTask.FromResult(false);
        Debug.Log($"[Record] Ch{s_chapter} {s_era} 보스전 {RankingService.Format(s_bossCentis)}{(s_bossNew ? " 신기록" : "")} · 챕터 전투 {RankingService.Format(s_roomsCentis)}{(s_roomsNew ? " 신기록" : "")}");
    }

    /// <summary>유물 기억 카드 뒤 기록 카드(보스전 · 챕터 전투). 순위는 카드가 뒤끝에서 읽어 채우므로 부르는 쪽은 기다리지 않는다(Forget).</summary>
    public static UniTask ShowAsync()
    {
        if (!s_pending) return UniTask.CompletedTask;
        s_pending = false;
        int ch = s_chapter;
        var era = s_era;
        var boss  = new UI_RecordCard.Line(Loc.T("보스전"), s_bossCentis, s_bossNew, PersonalBest(true, ch, era), TableOf(true, ch, era));
        var rooms = new UI_RecordCard.Line(Loc.F("챕터 {0} 전투", ch), s_roomsCentis, s_roomsNew, PersonalBest(false, ch, era), TableOf(false, ch, era));
        UI_RecordCard.Show(ch, era, boss, rooms, UniTask.WhenAll(s_bossSubmit, s_roomsSubmit));
        return UniTask.CompletedTask;
    }

    /// <summary>기기에 남은 내 최고(1/100초 · 없으면 0) — 보스전 / 챕터 전투 · 시기별.</summary>
    public static int PersonalBest(bool boss, int chapter, StoryEra era)
        => BackendGameData.Instance?.Data?.GetRecord(KeyOf(boss, chapter, era)) ?? 0;

    /// <summary>뒤끝 표 이름(예: <c>RANK_BOSS_1_LIB</c> · <c>RANK_ROOMS_4_NM</c>) — 봉인기는 랭킹이 없어 null.</summary>
    public static string TableOf(bool boss, int chapter, StoryEra era)
        => era == StoryEra.Sealed ? null : (boss ? BossTablePrefix : RoomsTablePrefix) + chapter + "_" + EraCode(era);

    private static string KeyOf(bool boss, int chapter, StoryEra era) => (boss ? BossKey : RoomsKey) + chapter + "_" + EraCode(era);

    private static string EraCode(StoryEra era) => era switch
    {
        StoryEra.Liberated     => "LIB",
        StoryEra.NightmareMode => "NM",
        _                      => "SEAL",
    };
}
