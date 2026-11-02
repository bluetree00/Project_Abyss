#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q11 사용자 「영문에 한글이 나오지 않게 — 텍스트 있는 곳 다 점검 · 검증」 — 영어 순회 점검.
/// ① 시작: 점검표(LocTextGuard) 비우고 영어로 ② 창 순회(플레이 중): ESC 메뉴 · 도감 4탭 · 되찾은 기억 · 장비 · 설정 · 룬판 · 환전소 · 업적을 차례로 열고 사진
/// ③ 끝: 점검표 요약 → Temp/loc_sweep1102.txt(전체 줄 수 · 씬/창별 · 앞 200줄) · 한국어로 되돌림. 자동 플레이(런 구조 자동 실측)는 ①과 ③ 사이에 돌린다.
/// 사진 Temp/ui_shots/LS1102_*.png · 로그 「[LS1102] 끝」.
/// </summary>
public static class LocSweep1102ProbeEditor
{
    private const string Root = "RelicFairy/Debug/11-02 영어 점검/";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem(Root + "1 시작 — 점검표 비우고 영어로 (플레이 중)")]
    private static void Begin()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[LS1102] 플레이 중에만"); return; }
        BeginAsync().Forget();
    }

    [MenuItem(Root + "2 창 순회 (플레이 중 · 런 안)")]
    private static void Sweep()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[LS1102] 플레이 중에만"); return; }
        SweepAsync().Forget();
    }

    [MenuItem(Root + "3 끝 — 요약 · 한국어로 (플레이 중)")]
    private static void End()
    {
        var seen = LocTextGuard.Seen.ToList();
        var sb = new StringBuilder($"11-02 영어 점검 — 바꾸지 못한 한글 글 {seen.Count}줄\n");
        foreach (var g in seen.GroupBy(Group).OrderByDescending(g => g.Count()))
            sb.AppendLine($"  {g.Count(),4}  {g.Key}");
        sb.AppendLine("---");
        foreach (var line in seen.Take(400)) sb.AppendLine(line);
        File.WriteAllText("Temp/loc_sweep1102.txt", sb.ToString());
        Loc.SetLanguage(Loc.Language.Korean);
        Debug.Log($"[LS1102] 끝 → Temp/loc_sweep1102.txt ({seen.Count}줄)");
    }

    private static async UniTaskVoid BeginAsync()
    {
        await Loc.PreloadAsync();
        LocTextGuard.ClearSeen();
        Loc.SetLanguage(Loc.Language.English);
        Debug.Log("[LS1102] 시작 — 영어 · 점검표 비움");
    }

    private static async UniTaskVoid SweepAsync()
    {
        Directory.CreateDirectory("Temp/ui_shots");
        var log = new StringBuilder();
        UI_EscMenu menu = Object.FindFirstObjectByType<UI_EscMenu>(FindObjectsInactive.Include);
        try
        {
            if (menu != null)
            {
                menu.Open();
                await Shot("esc");
                await Page(menu, "OnCodex", "_codexPage", "codex", async page =>
                {
                    var show = page.GetType().GetMethod("ShowTab", Inst);
                    for (int tab = 0; tab < 4; tab++) { show?.Invoke(page, new object[] { tab }); await Shot($"codex{tab}"); }
                });
                await Page(menu, "OnMemory", "_memoryPage", "memory", null);
                await Page(menu, "OnEquipment", "_equipPage", "equip", null);
                UI_Settings.Open();
                await Shot("settings");
                UI_Settings.CloseIfOpen();
                await UniTask.Delay(200, ignoreTimeScale: true);
                menu.Close();
                await UniTask.Delay(300, ignoreTimeScale: true);
            }
            else log.AppendLine("ESC 메뉴 없음");

            // 룬판
            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            await UniTask.Delay(400, ignoreTimeScale: true);
            if (UI_GridPanel.Instance != null)
            {
                UI_GridPanel.Instance.Open();
                await Shot("grid");
                UI_GridPanel.Instance.Close();
                await UniTask.Delay(300, ignoreTimeScale: true);
            }
            else log.AppendLine("룬판 없음");

            // 환전소
            var run = GameRunBootstrapper.Instance?.Run;
            if (run != null)
            {
                var open = ExchangePopupView.OpenAsync(run, default);
                await Shot("exchange");
                var go = GameObject.Find("@Exchange");
                if (go != null) foreach (var b in go.GetComponentsInChildren<UnityEngine.UI.Button>()) if (b.name == "Btn_닫기") b.onClick.Invoke();
                await open;
            }

            // 업적(제단 조회)
            UI_AwakeningPanel.OpenAchievementsAsync(readOnlyView: true).Forget();
            await UniTask.Delay(900, ignoreTimeScale: true);
            await Shot("achieve");
            var ach = Object.FindFirstObjectByType<UI_AwakeningPanel>();
            if (ach != null) ach.ClosePopupUI();
            await UniTask.Delay(300, ignoreTimeScale: true);
        }
        catch (System.Exception e) { log.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/loc_sweep1102_windows.txt", log.ToString());
            Debug.Log($"[LS1102] 창 순회 끝 — 지금까지 점검표 {LocTextGuard.Seen.Count}줄");
        }
    }

    private static async UniTask Page(UI_EscMenu menu, string opener, string field, string shot, System.Func<Component, UniTask> inside)
    {
        typeof(UI_EscMenu).GetMethod(opener, Inst)?.Invoke(menu, null);
        await UniTask.Delay(500, ignoreTimeScale: true);
        var page = typeof(UI_EscMenu).GetField(field, Inst)?.GetValue(menu) as Component;
        await Shot(shot);
        if (page != null && inside != null) await inside(page);
        page?.GetType().GetMethod("Close", Inst, null, System.Type.EmptyTypes, null)?.Invoke(page, null);
        var panel = typeof(UI_EscMenu).GetField("_panel", Inst)?.GetValue(menu) as Component;
        if (panel != null) panel.gameObject.SetActive(true);
        await UniTask.Delay(200, ignoreTimeScale: true);
    }

    private static async UniTask Shot(string name)
    {
        await UniTask.Delay(450, ignoreTimeScale: true);
        ScreenCapture.CaptureScreenshot($"Temp/ui_shots/LS1102_{name}.png");
        await UniTask.Delay(150, ignoreTimeScale: true);
    }

    /// <summary>「씬:최상위/둘째/…」 → 「씬:최상위/둘째」 묶음.</summary>
    private static string Group(string line)
    {
        string path = line.Split('|')[0].Trim();
        var parts = path.Split('/');
        return parts.Length >= 2 ? parts[0] + "/" + parts[1] : parts[0];
    }
}
#endif
