#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [촬영 · 플레이 중] 11-02 랭킹 쪽 · 기록 카드 · 도감(사용자 「실시간 랭킹 팝업 — 제대로 된 연출과 구성 · 플레이어들에게 공유」 · 「도감도 진행됐는지 체크」).
/// 테스트 허브 → 자동 플레이 첫 방 → ① ESC 「랭킹」 탭 넷(해방기 · 악몽 · 보스전 챕터 1 · 챕터 전투 챕터 2 — 뒤끝 실제 값) + 줄이 들어오는 중 한 장
/// ② 도감 탭 넷(써 본 수 / 전체) ③ 보스 처치 기록 카드(보스전 줄은 해방기 표로 순위를 읽어 실제 서버 값이 채워지는지 · 챕터 전투 줄은 없는 표) 0.25 · 1.0 · 4.5초
/// ④ 영어 — 랭킹 · 도감 · 카드 ⑤ 랭킹방 비석(같은 쪽 · 해방기 탭). 결과 Temp/ranking1102.txt · Temp/ui_shots/RK1102_*.png · 로그 「[RK1102] 끝」.
/// </summary>
public static class Ranking1102ProbeEditor
{
    private const string Tag      = "[RK1102]";
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string ShotDir  = "Temp/ui_shots";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool s_armed;

    [MenuItem("RelicFairy/Debug/11-02 랭킹 쪽 · 기록 카드 · 도감 촬영 (테스트 허브, 플레이 중)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning($"{Tag} 플레이 모드에서만 동작한다."); return; }
        if (Object.FindFirstObjectByType<TestHubLauncher>() == null) { Debug.LogWarning($"{Tag} 테스트 허브에서 시작한다."); return; }
        s_armed = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (!s_armed || !msg.StartsWith("[RunAuto]") || !msg.Contains("방 진입")) return;
        s_armed = false;
        Application.logMessageReceived -= OnLog;
        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
            var p = GameRunBootstrapper.Instance?.Run?.Player;
            if (p == null) { Debug.LogWarning($"{Tag} 런 플레이어가 없다"); return; }
            RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("11-02 랭킹 쪽 · 기록 카드 · 도감\n");
        Directory.CreateDirectory(ShotDir);
        var lang = Loc.Current;
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            foreach (var mo in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (mo != null && !mo.IsDead) mo.gameObject.SetActive(false);
            typeof(UILayoutRuntimeProbeEditor).GetMethod("HideTestHubGui", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
            await Wait(1.2f, ct);

            var menu = Object.FindFirstObjectByType<UI_EscMenu>(FindObjectsInactive.Include);
            if (menu == null) { sb.AppendLine("ESC 메뉴 없음"); return; }

            await PassAsync(sb, menu, "ko", full: true, ct);
            Loc.SetLanguage(Loc.Language.English);
            await Wait(0.4f, ct);
            await PassAsync(sb, menu, "en", full: false, ct);
            Loc.SetLanguage(lang);
            await Wait(0.4f, ct);

            // ⑤ 랭킹방 비석 — 같은 쪽 · 해방기 탭
            RankingBoardOverlay.ShowAsync(StoryEra.Liberated).Forget();
            await Wait(2.6f, ct);
            await Shot("RK1102_stone");
            Dump(sb, "비석", GameObject.Find("@RankingBoard"));
            var board = GameObject.Find("@RankingBoard");
            if (board != null) foreach (var b in board.GetComponentsInChildren<Button>()) if (b.name == "Btn_돌아가기") { b.onClick.Invoke(); break; }
            await Wait(0.6f, ct);
            sb.AppendLine($"비석 닫힘 {!RankingBoardOverlay.IsOpen}");

            // ⑥ 대기방 서약 제단 — 「이미 썼다」면 조용히 걷히고 문 열림 신호 · 세이브 JSON에 실린다(11-02 세이브 구멍)
            await AltarCheckAsync(sb, p, ct);
        }
        catch (System.OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Loc.SetLanguage(lang);
            File.WriteAllText("Temp/ranking1102.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    /// <summary>한 언어 — ESC 랭킹(탭 넷 · 들어오는 중) · 도감(탭 넷) · 기록 카드.</summary>
    private static async UniTask PassAsync(StringBuilder sb, UI_EscMenu menu, string lang, bool full, CancellationToken ct)
    {
        sb.AppendLine($"\n## {lang}");
        menu.Open();
        await Wait(0.5f, ct);
        sb.AppendLine($"  Q32 A1 — ESC 메뉴 열림: 월드 입력 막힘 {UIInputGate.Blocked} · ESC 메뉴 {UI_EscMenu.AnyOpen}");
        typeof(UI_EscMenu).GetMethod("OnRanking", Inst)?.Invoke(menu, null);
        var page = typeof(UI_EscMenu).GetField("_rankingPage", Inst)?.GetValue(menu) as Component;
        if (page == null) { sb.AppendLine("랭킹 쪽 없음"); return; }
        var show = page.GetType().GetMethod("ShowTab", Inst);
        // 11-03 시기(1 해방기 · 2 악몽) · 종류(0 클리어 · 1 보스전 · 2 챕터 전투) · 챕터
        int[][] tabs = full ? new[] { new[] { 1, 0, 1 }, new[] { 2, 0, 1 }, new[] { 1, 1, 1 }, new[] { 2, 2, 2 } } : new[] { new[] { 1, 0, 1 }, new[] { 1, 1, 1 } };
        for (int i = 0; i < tabs.Length; i++)
        {
            var t = tabs[i];
            show?.Invoke(page, new object[] { (StoryEra)t[0], t[1], t[2] });
            if (full && i == 0) { await Wait(0.12f, ct); await Shot($"RK1102_{lang}_tab0_anim"); }
            await Wait(2.4f, ct);
            await Shot($"RK1102_{lang}_tab{i}");
            Dump(sb, $"랭킹 {(StoryEra)t[0]} 종류 {t[1]} 챕터 {t[2]}", page.gameObject);
        }
        menu.HandleEscape();
        await Wait(0.4f, ct);

        typeof(UI_EscMenu).GetMethod("OnCodex", Inst)?.Invoke(menu, null);
        var codex = typeof(UI_EscMenu).GetField("_codexPage", Inst)?.GetValue(menu) as Component;
        if (codex != null)
        {
            var cshow = codex.GetType().GetMethod("ShowTab", Inst);
            for (int tab = 0; tab < (full ? 4 : 1); tab++)
            {
                cshow?.Invoke(codex, new object[] { tab });
                await Wait(0.5f, ct);
                await Shot($"RK1102_{lang}_codex{tab}");
                if (tab == 0) Dump(sb, "도감", codex.gameObject, 12);
            }
        }
        else sb.AppendLine("도감 쪽 없음");
        menu.HandleEscape();
        await Wait(0.3f, ct);
        menu.Close();
        await Wait(0.5f, ct);
        sb.AppendLine($"  Q32 A1 — ESC 메뉴 닫힘: 월드 입력 막힘 {UIInputGate.Blocked} · ESC 메뉴 {UI_EscMenu.AnyOpen}");

        // 기록 카드 — 보스전 줄은 해방기 표(실제 서버 순위) · 챕터 전투 줄은 아직 없는 표
        var boss  = new UI_RecordCard.Line(Loc.T("보스전"), 5241, true, 6010, RankingService.LiberationTable);
        var rooms = new UI_RecordCard.Line(Loc.F("챕터 {0} 전투", 2), 10810, false, 10002, RecordBoard.TableOf(false, 2, StoryEra.Liberated));
        UI_RecordCard.Show(2, StoryEra.Liberated, boss, rooms, UniTask.CompletedTask);
        float t0 = Time.realtimeSinceStartup;
        float[] at = full ? new[] { 0.25f, 1.0f, 4.5f } : new[] { 4.5f };
        for (int i = 0; i < at.Length; i++)
        {
            while (Time.realtimeSinceStartup - t0 < at[i]) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            await Shot($"RK1102_{lang}_card{i + 1}");
        }
        Dump(sb, "기록 카드", GameObject.Find("@RecordCard"));
        while (GameObject.Find("@RecordCard") != null && Time.realtimeSinceStartup - t0 < 14f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        sb.AppendLine($"  카드 물러남 {Time.realtimeSinceStartup - t0:0.0}초");

        // 11-03 봉인기 카드 — 표 없음(랭킹 없음 · 기기 기록만)
        var sealedBoss = new UI_RecordCard.Line(Loc.T("보스전"), 6120, true, 0, RecordBoard.TableOf(true, 1, StoryEra.Sealed));
        UI_RecordCard.Show(1, StoryEra.Sealed, sealedBoss, default, UniTask.CompletedTask);
        await Wait(4.5f, ct);
        await Shot($"RK1102_{lang}_card_sealed");
        Dump(sb, "봉인기 기록 카드", GameObject.Find("@RecordCard"));
        var sealedCard = GameObject.Find("@RecordCard");
        if (sealedCard != null) UnityEngine.Object.Destroy(sealedCard);
    }

    private static async UniTask AltarCheckAsync(StringBuilder sb, PlayerController p, CancellationToken ct)
    {
        var run = GameRunBootstrapper.Instance?.Run;
        if (run == null) { sb.AppendLine("제단: 런 없음"); return; }
        int signals = 0;
        System.Action onSignal = () => signals++;
        WorldCovenantAltar.OnCovenantAssembled += onSignal;
        try
        {
            bool prev = run.WaitingAltarUsed;
            run.WaitingAltarUsed = true;
            var used = WorldCovenantAltar.SpawnAt(p.transform.position + p.transform.forward * 6f);
            await Wait(0.3f, ct);
            bool hidden = used == null || !used.gameObject.activeSelf;
            sb.AppendLine($"제단(이미 씀): 걷힘 {hidden} · 문 열림 신호 {signals}번 → {(hidden && signals == 1 ? "통과" : "결함")}");
            run.WaitingAltarUsed = false;
            var fresh = WorldCovenantAltar.SpawnAt(p.transform.position + p.transform.forward * 9f);
            await Wait(0.3f, ct);
            bool shown = fresh != null && fresh.gameObject.activeSelf;
            sb.AppendLine($"제단(새것): 서 있음 {shown} · 신호 늘지 않음 {signals == 1} → {(shown && signals == 1 ? "통과" : "결함")}");
            if (used != null) Object.Destroy(used.gameObject);
            if (fresh != null) Object.Destroy(fresh.gameObject);
            run.WaitingAltarUsed = prev;
        }
        finally { WorldCovenantAltar.OnCovenantAssembled -= onSignal; }

        var round = JsonUtility.FromJson<RunSaveData>(JsonUtility.ToJson(new RunSaveData { chapterStartPending = true, waitingAltarUsed = true }));
        var old   = JsonUtility.FromJson<RunSaveData>("{\"chapterStartPending\":true}");
        sb.AppendLine($"세이브 JSON: 실림 {round.waitingAltarUsed} · 옛 세이브(칸 없음) {old.waitingAltarUsed} → {(round.waitingAltarUsed && !old.waitingAltarUsed ? "통과" : "결함")}");
    }

    /// <summary>보이는 글 전부(넘침 표시).</summary>
    private static void Dump(StringBuilder sb, string what, GameObject root, int max = 60)
    {
        sb.AppendLine($"  [{what}]");
        if (root == null) { sb.AppendLine("    없음"); return; }
        int n = 0;
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.gameObject.activeInHierarchy || t.color.a < 0.05f) continue;
            t.ForceMeshUpdate();
            string s = t.GetParsedText().Replace("\n", " ⏎ ").Trim();
            if (s.Length == 0) continue;
            sb.AppendLine($"    {s}{(t.isTextOverflowing ? "  ⚠넘침" : "")}");
            if (++n >= max) break;
        }
    }

    private static async UniTask Shot(string name)
    {
        var runner = Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        await UniTask.WaitForEndOfFrame(runner);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (tex == null) return;
        File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    private static UniTask Wait(float s, CancellationToken ct)
        => UniTask.Delay(System.TimeSpan.FromSeconds(s), DelayType.Realtime, PlayerLoopTiming.Update, ct);
}
#endif
