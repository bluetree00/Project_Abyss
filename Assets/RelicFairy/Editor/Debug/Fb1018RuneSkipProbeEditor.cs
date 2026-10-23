#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-18 룬 획득 팝업 분석 P1 반입 실측(테스트 허브 · 플레이 중).
/// <list type="number">
/// <item>공개 연출 중 [넘기기] — 창이 닫히지 않고 연출만 끝 상태로 간다(보상을 보기 전에 버리지 않는다).</item>
/// <item>연출이 끝나고 잠시 뒤 [넘기기] — 그때는 넘긴다.</item>
/// <item>정제소에서 연 창 — 넘기기 글 「원석 +3 환불」 · 방 확률 막대 숨김.</item>
/// <item>방 보상 창 — 넘기기 글 「원석 +1」 · 확률 막대 보임.</item>
/// </list>
/// 결과 Temp/fb1018_rune.txt · Temp/ui_shots/FB18_rune_*.png · 로그 「[FB18rune] 끝」. 보관함 · 판 · 세이브는 건드리지 않는다(고르지 않고 넘긴다).
/// </summary>
public static class Fb1018RuneSkipProbeEditor
{
    private const string Tag     = "[FB18rune]";
    private const string OutTxt  = "Temp/fb1018_rune.txt";
    private const string ShotDir = "Temp/ui_shots";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-18 룬 획득 넘기기 · 정제소 글 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중(테스트 허브)에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-18 룬 획득 팝업 P1 실측\n");
        Directory.CreateDirectory(ShotDir);
        try
        {
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher != null && GameRunBootstrapper.Instance?.Run?.Player == null)
            {
                launcher.SelectChapter(1);
                typeof(TestHubLauncher).GetField("_bossApproach", Any)?.SetValue(launcher, false);
                launcher.TryLaunch();
            }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 60f && GameRunBootstrapper.Instance?.Run?.Player == null)
                await UniTask.Delay(500, DelayType.Realtime);
            var run = GameRunBootstrapper.Instance?.Run;
            if (run == null || Managers.ItemData == null) { sb.AppendLine("런이 시작되지 않음"); return; }
            await Wait(3f);
            run.Player?.SetInvincible(120f);
            var inv = run.ItemInventory;
            var cands = Candidates(3);
            sb.AppendLine($"후보 {cands.Count}장");
            if (cands.Count == 0) return;

            // ① 공개 연출 중 [넘기기]
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
            popup.Setup(new List<(RuntimeItemData, ItemSO)>(cands), inv);
            await Wait(0.15f);
            bool revealingAtClick = GetField(popup, "_revealing") is true;
            Click(popup);
            await Wait(0.3f);
            bool alive = popup != null && popup.gameObject.activeInHierarchy;
            Check(sb, $"① 연출 중 [넘기기] — 창이 그대로 · 넘김 아님(누른 때 연출 중 {revealingAtClick})", revealingAtClick && alive && !popup.Skipped);
            await Wait(0.6f);
            await ShotAsync("FB18_rune_1_skip_during_reveal");

            // ② 연출이 끝나고 잠시 뒤 [넘기기]
            await Wait(0.5f);
            Click(popup);
            await Wait(0.4f);
            Check(sb, "② 연출 뒤 [넘기기] — 넘긴다(창 닫힘)", popup == null || popup.Skipped);
            Managers.UI.CloseAllPopupUI();
            Time.timeScale = 1f;
            await Wait(0.5f);

            // ③ 정제소에서 연 창
            popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
            popup.Setup(new List<(RuntimeItemData, ItemSO)>(cands), inv, refinerySpent: 3);
            await Wait(1.6f);
            string hint = HintOf(popup);
            var bar = popup.GetComponentInChildren<OddsBarView>(true);
            Check(sb, $"③ 정제소 — 넘기기 글 「{hint}」 · 확률 막대 숨김", hint.Contains("+3") && hint.Contains("환불") && (bar == null || !bar.gameObject.activeSelf));
            await ShotAsync("FB18_rune_2_refinery");
            Managers.UI.CloseAllPopupUI();
            Time.timeScale = 1f;
            await Wait(0.5f);

            // ④ 방 보상 창
            popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
            popup.Setup(new List<(RuntimeItemData, ItemSO)>(cands), inv);
            await Wait(1.6f);
            hint = HintOf(popup);
            bar = popup.GetComponentInChildren<OddsBarView>(true);
            Check(sb, $"④ 방 보상 — 넘기기 글 「{hint}」 · 확률 막대 보임", hint.Contains($"+{ClearRewardTrigger.SkipOreReward}") && !hint.Contains("환불") && bar != null && bar.gameObject.activeSelf);
            await ShotAsync("FB18_rune_3_room");
            Managers.UI.CloseAllPopupUI();
            Time.timeScale = 1f;
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    /// <summary>데이터에 있는 룬 앞쪽 n개(실측용 — 무엇이든 카드만 서면 된다).</summary>
    private static List<(RuntimeItemData, ItemSO)> Candidates(int n)
    {
        var list = new List<(RuntimeItemData, ItemSO)>(n);
        foreach (var kv in Managers.ItemData.GetAllItems())
        {
            var d = RuntimeItemData.FromServer(kv.Value);
            if (d == null) continue;
            list.Add((d, ItemSORegistry.Find(kv.Key)));
            if (list.Count >= n) break;
        }
        return list;
    }

    private static void Click(UI_RuneSelectPopup popup)
        => typeof(UI_RuneSelectPopup).GetMethod("OnSkipClicked", Any)?.Invoke(popup, null);

    private static string HintOf(UI_RuneSelectPopup popup)
    {
        foreach (var t in popup.GetComponentsInChildren<TMP_Text>(true))
            if (t.name == "SkipHint") return t.text;
        return "(없음)";
    }

    private static object GetField(object o, string field) => o?.GetType().GetField(field, Any)?.GetValue(o);

    private static void Check(StringBuilder sb, string what, bool ok) => sb.AppendLine($"CHECK {ok} — {what}");

    private static UniTask Wait(float seconds) => UniTask.Delay(System.TimeSpan.FromSeconds(seconds), DelayType.Realtime);

    private static async UniTask ShotAsync(string name)
    {
        var runner = Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        await UniTask.WaitForEndOfFrame(runner);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (tex == null) return;
        File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
        Object.Destroy(tex);
    }
}
#endif
