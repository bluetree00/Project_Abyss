#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using Steamworks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-06 사용자 「스팀 클라우드로 유저 데이터 관리 · 도전과제 등록 · 작동 · 테스트 검증」 실측(플레이 중 · 테스트 허브 · 런 밖).
/// <list type="number">
/// <item>클라우드 · 진행 기록 — Steam 상태 · 진행 세이브 폴더(SaveRoot)가 계정 폴더인가 · 진행 파일은 안 · 차트 캐시는 밖 ·
/// 옮기기 시험(Temp 안 가짜 폴더: 진행 파일만 복사 · 지운 파일 되살아나지 않음 · 클라우드가 먼저 내려 준 폴더는 안 덮음) ·
/// ProgressPrefs 시험(옛 PlayerPrefs 옮겨 옴 · 파일로 남음 · 「다른 PC」 = 레지스트리 없이도 값 · 지우면 둘 다) · 슬롯 지우기가 진행 기록 둘 다 지우나.</item>
/// <item>도전과제 — Steam에 등록된 도전과제 목록 ↔ 게임 업적(ACH_ + codeName) 맞대기 · 빈 2번 슬롯에서 기록을 흘려 업적을 채우면 「막 채움」 사건 →
/// Steam 해제(등록돼 있으면 열림 확인 뒤 되돌림 · 아니면 경고만) · 따라잡기(SyncAll) · 끝나면 2번 슬롯을 비우고 원래 슬롯으로.</item>
/// </list>
/// 결과 Temp/steam1106_cloud.txt · Temp/steam1106_ach.txt · 로그 「[ST1106] 클라우드 끝」 · 「[ST1106] 도전과제 끝」.
/// </summary>
public static class Steam1106ProbeEditor
{
    private const string Tag = "[ST1106]";
    private const int TestSlot = 2;
    private const string T = "zz_cloud1106_";
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static StringBuilder s_sb;
    private static int s_fail;

    // ── 1 클라우드 · 진행 기록 ────────────────────────────────
    [MenuItem("RelicFairy/Debug/11-06 스팀 클라우드 · 진행 기록 실측 (플레이 중)")]
    private static void Cloud()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에"); return; }
        Begin("11-06 스팀 클라우드 · 진행 기록");
        try
        {
            string root = Application.persistentDataPath;
            Line($"Steam 켜짐 {SteamManager.Initialized}{(SteamManager.Initialized ? $" · 계정 {SteamUser.GetSteamID().GetAccountID().m_AccountID} · {SteamFriends.GetPersonaName()}" : "")}");
            Line($"진행 세이브 폴더 {SaveRoot.Dir} · 계정 폴더 {SaveRoot.PerAccount}");
            Check(!SteamManager.Initialized || (SaveRoot.PerAccount && SaveRoot.Dir == Path.Combine(root, SaveRoot.Account)), "Steam이 켜져 있으면 진행 세이브 = persistentDataPath/계정 번호");
            Check(Same(SaveFileIO.Dir, SaveRoot.Dir) && Same(Path.GetDirectoryName(ProgressPrefs.FilePath), SaveRoot.Dir), "메타 · 퀘스트 · 진행 기록이 같은 폴더");
            if (SaveRoot.PerAccount)
            {
                var inside = Directory.GetFiles(SaveRoot.Dir).Select(Path.GetFileName).ToList();
                Line($"  계정 폴더 파일 {inside.Count}: {string.Join(", ", inside.Take(24))}");
                Check(inside.All(f => SaveRoot.IsProgressFile(f) || f == SaveRoot.MigratedMarker), "계정 폴더엔 진행 파일만(차트 캐시 · 뒤끝 파일 0)");
                Check(File.Exists(Path.Combine(SaveRoot.Dir, SaveRoot.MigratedMarker)), "옮김 표식이 있다(다시 옮기지 않는다)");
                var rootCache = Directory.GetFiles(root).Select(Path.GetFileName).Where(f => !SaveRoot.IsProgressFile(f)).ToList();
                Line($"  바깥(동기화 안 함) {rootCache.Count}: {string.Join(", ", rootCache.Take(12))}…");
            }
            var rpm = RunProgressManager.Instance;
            for (int s = 0; s < 3 && rpm != null; s++)
                Line($"  슬롯 {s} — 런 세이브 {rpm.HasLocalRun(s)} · 파일 {File.Exists(Path.Combine(SaveRoot.Dir, $"run_save_{s}.json"))} · 인트로 완료 {IntroCompletionTracker.IsCompletedForSlot(s)}");

            MigrationTest();
            PrefsTest();
            SlotClearTest();
        }
        catch (System.Exception e) { s_fail++; Line("예외: " + e); }
        End("Temp/steam1106_cloud.txt", "클라우드 끝");
    }

    /// <summary>Temp 안 가짜 폴더로 옮기기 규칙만 잰다(진짜 세이브는 안 건드린다).</summary>
    private static void MigrationTest()
    {
        string baseDir = Path.GetFullPath("Temp/cloud1106");
        if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
        string rootA = Path.Combine(baseDir, "pcA");
        Directory.CreateDirectory(rootA);
        string[] progress = { "run_save_0.json", "run_save_0.bak", "meta_save_1.json", "meta_save_1.json.sig", "quest_save_2.json", "progress_prefs.json" };
        string[] caches   = { "item_data.json", "backend_cdn.dat", "backend.dat", "zone_layout_CH2.json", "Player.log" };
        foreach (var f in progress.Concat(caches)) File.WriteAllText(Path.Combine(rootA, f), "old:" + f);

        string dirA = SaveRoot.Resolve(rootA, "12345", out int c1);
        var got = Directory.GetFiles(dirA).Select(Path.GetFileName).ToList();
        Check(c1 == progress.Length && progress.All(got.Contains) && !caches.Any(got.Contains), $"처음 — 진행 파일 {c1}/{progress.Length}만 복사 · 캐시 0");
        Check(progress.All(f => File.Exists(Path.Combine(rootA, f))), "원본은 바깥에 남는다(되돌릴 수 있게)");

        File.Delete(Path.Combine(dirA, "run_save_0.json"));   // 플레이어가 슬롯을 지웠다
        SaveRoot.Resolve(rootA, "12345", out int c2);
        Check(c2 == 0 && !File.Exists(Path.Combine(dirA, "run_save_0.json")), "다시 켜도 옮기지 않는다 — 지운 슬롯이 바깥 옛 파일로 되살아나지 않음");

        // 다른 PC — 클라우드가 계정 폴더(표식 포함)를 먼저 내려 줬고 그 PC 바깥에도 옛 세이브가 있다
        string rootB = Path.Combine(baseDir, "pcB");
        string dirB = Path.Combine(rootB, "12345");
        Directory.CreateDirectory(dirB);
        File.WriteAllText(Path.Combine(dirB, "meta_save_1.json"), "cloud");
        File.WriteAllText(Path.Combine(dirB, SaveRoot.MigratedMarker), "cloud");
        File.WriteAllText(Path.Combine(rootB, "meta_save_1.json"), "old-pcB");
        File.WriteAllText(Path.Combine(rootB, "run_save_2.json"), "old-pcB");
        SaveRoot.Resolve(rootB, "12345", out int c3);
        Check(c3 == 0 && File.ReadAllText(Path.Combine(dirB, "meta_save_1.json")) == "cloud" && !File.Exists(Path.Combine(dirB, "run_save_2.json")),
              "클라우드가 먼저 내려 준 계정 폴더는 그대로 — 그 PC의 옛 파일을 섞지 않는다(클라우드가 정본)");
        Check(SaveRoot.Resolve(rootA, null, out _) == rootA, "Steam이 꺼져 있으면 바깥 폴더 그대로");
        Directory.Delete(baseDir, true);
    }

    /// <summary>진짜 진행 기록 파일에 시험 키만 넣었다 뺀다.</summary>
    private static void PrefsTest()
    {
        string legacy = T + "legacy", fresh = T + "new", str = T + "str";
        PlayerPrefs.SetInt(legacy, 7); PlayerPrefs.Save();
        Check(ProgressPrefs.GetInt(legacy) == 7, "옛 PlayerPrefs 값을 읽어 옮긴다");
        ProgressPrefs.SetInt(fresh, 3);
        ProgressPrefs.SetString(str, "a,b");
        ProgressPrefs.Save();
        string file = File.Exists(ProgressPrefs.FilePath) ? File.ReadAllText(ProgressPrefs.FilePath) : "";
        Check(file.Contains(legacy) && file.Contains(fresh) && file.Contains(str), $"파일에 남는다({Path.GetFileName(ProgressPrefs.FilePath)} · {file.Length}B)");
        Check(!PlayerPrefs.HasKey(fresh), "새 값은 레지스트리에 안 쓴다");

        // 「다른 PC」 — 레지스트리는 비고 클라우드가 준 파일만 있다
        PlayerPrefs.DeleteKey(legacy); PlayerPrefs.Save();
        ProgressPrefs.Reload();
        Check(ProgressPrefs.GetInt(legacy) == 7 && ProgressPrefs.GetInt(fresh) == 3 && ProgressPrefs.GetString(str) == "a,b", "레지스트리 없이(다른 PC) 파일만으로 같은 값");

        PlayerPrefs.SetInt(legacy, 9); PlayerPrefs.Save();   // 옛 레지스트리에 다른 값이 남아 있어도
        Check(ProgressPrefs.GetInt(legacy) == 7, "파일 값이 먼저(옛 레지스트리 값이 덮지 않음)");
        ProgressPrefs.DeleteKey(legacy);
        ProgressPrefs.Save();
        ProgressPrefs.Reload();
        Check(!ProgressPrefs.HasKey(legacy) && !PlayerPrefs.HasKey(legacy), "지우면 파일 · 레지스트리 둘 다(되살아나지 않음)");

        ProgressPrefs.DeleteKey(fresh); ProgressPrefs.DeleteKey(str); ProgressPrefs.Save();
        ProgressPrefs.Reload();
        Check(!ProgressPrefs.HasKey(fresh) && !ProgressPrefs.HasKey(str), "시험 키 정리");
    }

    /// <summary>슬롯 지우기(새 게임 · 삭제)가 그 슬롯 진행 기록을 파일 · 레지스트리 둘 다 지우나 — 빈 2번 슬롯만.</summary>
    private static void SlotClearTest()
    {
        var rpm = RunProgressManager.Instance;
        if (rpm == null || rpm.HasLocalRun(TestSlot) || IntroCompletionTracker.IsCompletedForSlot(TestSlot)) { Line($"  (2번 슬롯이 비어 있지 않아 슬롯 지우기 시험은 건너뜀)"); return; }
        string key = "game_intro_done_v1_slot" + TestSlot;
        PlayerPrefs.SetInt(key, 1); PlayerPrefs.Save();             // 업데이트 전 기록
        Check(IntroCompletionTracker.IsCompletedForSlot(TestSlot), "인트로 완료 — 옛 레지스트리 기록을 읽는다");
        IntroCompletionTracker.ClearForSlot(TestSlot);
        ProgressPrefs.Reload();
        Check(!IntroCompletionTracker.IsCompletedForSlot(TestSlot) && !PlayerPrefs.HasKey(key), "슬롯 지우기 — 파일 · 레지스트리 둘 다 지움");
    }

    // ── 2 도전과제 ────────────────────────────────────────────
    [MenuItem("RelicFairy/Debug/11-06 스팀 도전과제 실측 (테스트 허브, 플레이 중)")]
    private static void Achievements() { if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에"); return; } AchievementsAsync().Forget(); }

    private static async UniTaskVoid AchievementsAsync()
    {
        Begin("11-06 스팀 도전과제");
        var rpm = RunProgressManager.Instance;
        var qm = Managers.Quest;
        int origSlot = rpm != null ? rpm.ActiveSlotIndex : 0;
        bool switched = false;
        string testApi = SteamAchievements.ApiName("kill_100_any");
        bool hadBefore = false, registered = false;
        try
        {
            var pool = (typeof(QuestManager).GetField("_achievementPool", Any)?.GetValue(qm) as List<Quest>) ?? new List<Quest>();
            var ours = pool.Where(q => q != null).Select(q => SteamAchievements.ApiName(q.CodeName)).OrderBy(x => x).ToList();
            Line($"게임 업적 {ours.Count}개 · Steam 켜짐 {SteamManager.Initialized}");
            Check(ours.Count == 22, "게임 업적 22개(QuestDefinition.csv)");
            if (SteamManager.Initialized)
            {
                uint n = SteamUserStats.GetNumAchievements();
                var steam = Enumerable.Range(0, (int)n).Select(i => SteamUserStats.GetAchievementName((uint)i)).ToList();
                var missing = ours.Where(a => !steam.Contains(a)).ToList();
                var extra = steam.Where(a => !ours.Contains(a)).ToList();
                Line($"  Steam 등록 {n}개 · 빠짐 {missing.Count} [{string.Join(", ", missing.Take(8))}{(missing.Count > 8 ? "…" : "")}] · 남는 것 {extra.Count} [{string.Join(", ", extra)}]");
                Check(missing.Count == 0, missing.Count == 0 ? "Steam 등록 = 게임 업적(빠짐 0)" : "Steam 등록 빠짐 — Steamworks 「도전과제」에 등록 · 게시해야 열린다(등록표 참고)");
                registered = SteamUserStats.GetAchievement(testApi, out hadBefore);
            }

            // 빈 2번 슬롯에서 「처치 100」을 채운다 → 막 채움 사건 → Steam 해제
            if (rpm == null || qm == null || rpm.HasLocalRun(TestSlot) || File.Exists(SaveFileIO.PathFor($"quest_save_{TestSlot}.json")) || !MetaIsFresh(SaveFileIO.PathFor($"meta_save_{TestSlot}.json")))
            { Check(false, "2번 슬롯에 진행이 있다 — 사건 시험을 건너뜀(지우지 않으려고)"); return; }
            rpm.ActiveSlotIndex = TestSlot; BackendGameData.Instance?.ApplyActiveSlot(); qm.ReloadForActiveSlot(); switched = true;
            var achieved = new List<string>();
            void OnAch(Quest q) => achieved.Add(q.CodeName);
            qm.onAchievementAchieved += OnAch;
            QuestEvents.ReportRecord("kills", 99);
            Check(achieved.Count == 0, "99마리 — 아직 안 채움 · 사건 0");
            QuestEvents.ReportRecord("kills", 100);
            QuestEvents.ReportRecord("kills", 120);   // 다시 흘려도 두 번 쏘지 않는다
            qm.onAchievementAchieved -= OnAch;
            Check(achieved.Count == 1 && achieved[0] == "kill_100_any", $"100마리 — 막 채움 사건 한 번 [{string.Join(", ", achieved)}]");
            var a100 = qm.ActiveAchievements.FirstOrDefault(q => q.CodeName == "kill_100_any");
            Check(a100 != null && a100.IsCompltable, "업적은 「받기 기다림」(제단에서 받는 흐름 그대로)");

            await UniTask.Delay(1500, DelayType.Realtime);   // Tick이 StoreStats를 보내고 응답이 올 시간
            if (SteamManager.Initialized && registered)
            {
                SteamUserStats.GetAchievement(testApi, out bool now);
                Check(now, $"Steam 「{testApi}」 열림(전에 {(hadBefore ? "이미 열려 있었음" : "닫혀 있었음")})");
                Check(SteamAchievements.SyncAll() == 0, "따라잡기 — 이미 연 것은 다시 열지 않는다");
                if (!hadBefore) { SteamUserStats.ClearAchievement(testApi); SteamUserStats.StoreStats(); Line($"  시험으로 연 「{testApi}」 되돌림"); }
            }
            else Line(SteamManager.Initialized ? $"  Steam에 「{testApi}」가 아직 없다 — 경고 한 번 남기고 넘어감(등록 · 게시 뒤 다시 재면 열림까지 확인)" : "  Steam이 꺼져 있다 — 사건까지만 확인(Steam 켜고 다시)");
        }
        catch (System.Exception e) { s_fail++; Line("예외: " + e); }
        finally
        {
            if (switched)
            {
                qm.ClearSlot(TestSlot);
                rpm.ResetSlot(TestSlot);
                rpm.ActiveSlotIndex = origSlot; BackendGameData.Instance?.ApplyActiveSlot(); qm.ReloadForActiveSlot();
                Line($"  정리 — 2번 슬롯 비움 · 원래 슬롯 {origSlot}");
            }
            End("Temp/steam1106_ach.txt", "도전과제 끝");
        }
    }

    // ── 조각 ──────────────────────────────────────────────────
    /// <summary>경로가 같은가(구분자 · 끝 슬래시 차이 무시 — persistentDataPath는 「/」, Path.Combine은 「\」).</summary>
    private static bool Same(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), System.StringComparison.OrdinalIgnoreCase);

    /// <summary>메타가 없거나 시작값(빌드 세이브 초기화가 세 슬롯 모두 시작값으로 써 둔다)인가 — 진짜 진행이 있으면 시험이 슬롯을 비우지 않게.</summary>
    private static bool MetaIsFresh(string path)
    {
        if (!File.Exists(path)) return true;
        string j = File.ReadAllText(path).Replace(" ", "");
        return j.Contains("\"totalRuns\":0,") && j.Contains("\"abyssEssence\":0,") && j.Contains("\"records\":\"\"") && j.Contains("\"unlockedIds\":\"\"");
    }

    private static void Begin(string title) { s_sb = new StringBuilder(title + "\n"); s_fail = 0; }
    private static void Line(string s) => s_sb.AppendLine(s);
    private static void Check(bool ok, string what) { if (!ok) s_fail++; s_sb.AppendLine($"{(ok ? "PASS" : "FAIL")} {what}"); }
    private static void End(string path, string tail)
    {
        s_sb.Insert(0, s_fail == 0 ? "PASS\n" : $"FAIL {s_fail}\n");
        File.WriteAllText(path, s_sb.ToString());
        Debug.Log($"{Tag} {tail}\n{s_sb}");
    }
}
#endif
