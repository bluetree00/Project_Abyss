#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-22 ⑪ 재화 획득 목록 실측 — 골드 · 강화재료 · 원석 · 정수를 실제 지급 길(AddTempGold · FuelBank.Add · AddEssence)로 넣어
/// 줄이 뜨는지 · 같은 재화는 숫자만 더해지는지 · 2.5초 뒤 사라지는지 · 쓰면(줄면) 안 뜨는지 잰다.
/// 사진: Temp/ui_shots/FEED22_*.png · 결과: Temp/fb1022_feed.txt · 로그 「[Fb1022Feed] 끝」.
/// </summary>
public static class Fb1022CurrencyFeedProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-22 재화 획득 목록 촬영 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning("[Fb1022Feed] 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb  = new StringBuilder("재화 획득 목록 실측\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}"); }
        var run = GameRunBootstrapper.Instance.Run;
        var hub = Object.FindFirstObjectByType<TestHubLauncher>();
        bool hubWas = hub != null && hub.enabled;
        if (hub != null) hub.enabled = false;
        try
        {
            // 대화 · 컷신 중엔 HUD가 걷혀 얻은 재화를 모아 둔다(설계대로) — 걷힌 뒤에 잰다(1차 실측은 멀린 대화 중이었다)
            var hud = Object.FindFirstObjectByType<HudPresenter>();
            var modeField = typeof(HudPresenter).GetField("_currentMode", Inst);
            for (int i = 0; i < 40 && hud != null && modeField?.GetValue(hud)?.ToString() == "Cutscene"; i++) { TestHubDebugMenu.AdvanceDialogue(); await Wait(0.5f); }
            sb.AppendLine($"  HUD 모드 {modeField?.GetValue(hud)}");
            await Wait(4f);   // 이전 줄이 다 사라지게
            run.PlayerState.AddTempGold(37);
            await Wait(0.35f);
            Dump(sb, "골드 37");
            Check("골드 한 줄", Alive().Length == 1 && TextOf("골드").Contains("× 37"));
            await Shot("FEED22_A_gold");

            run.FuelBank.Add(FuelKind.EnhanceMaterial, 5);
            await Wait(0.15f);
            run.FuelBank.Add(FuelKind.RuneOre, 6);
            await Wait(0.15f);
            run.AddEssence(3);
            await Wait(0.35f);
            Dump(sb, "+ 강화재료 5 · 원석 6 · 정수");
            Check("네 줄(재화마다 하나)", Alive().Length == 4);
            Check("가장 최근(정수)이 맨 위", TopName() == "심연의 정수");
            await Shot("FEED22_B_four");

            run.PlayerState.AddTempGold(12);
            await Wait(0.2f);
            run.PlayerState.AddTempGold(12);
            await Wait(0.3f);
            Dump(sb, "+ 골드 12 · 12");
            Check("같은 재화는 숫자만 더함(골드 × 61) · 줄 그대로 넷", TextOf("골드").Contains("× 61") && Alive().Length == 4);
            run.PlayerState.TrySpendGold(30);
            await Wait(0.2f);
            Check("쓰면(줄면) 안 뜬다 — 골드 × 61 그대로", TextOf("골드").Contains("× 61"));
            await Shot("FEED22_C_merge");

            await Wait(3.4f);
            Dump(sb, "3.4초 뒤");
            Check("2.5초 + 사라짐 뒤 모두 걷힘", Alive().Length == 0);
            await Shot("FEED22_D_gone");
            run.PlayerState.TrySpendGold(31);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (hub != null) hub.enabled = hubWas;
            sb.AppendLine($"CHECK 합계 {pass}/{total}");
            File.WriteAllText("Temp/fb1022_feed.txt", sb.ToString());
            Debug.Log("[Fb1022Feed] 끝\n" + sb);
        }
    }

    private static CurrencyGainFeedView Feed() => Object.FindFirstObjectByType<CurrencyGainFeedView>(FindObjectsInactive.Include);

    private static object[] Rows()
    {
        var f = Feed();
        return f == null ? new object[0] : ((System.Array)typeof(CurrencyGainFeedView).GetField("_rows", Inst).GetValue(f)).Cast<object>().ToArray();
    }

    private static object[] Alive() => Rows().Where(r => r != null && (float)r.GetType().GetField("ShownAt").GetValue(r) >= 0f).ToArray();

    private static TMP_Text Text(object row) => row.GetType().GetField("Text").GetValue(row) as TMP_Text;

    private static string TextOf(string name)
    {
        var r = Alive().FirstOrDefault(x => Text(x).text.StartsWith(name + " "));
        return r != null ? Text(r).text : string.Empty;
    }

    private static string TopName()
    {
        var top = Alive().OrderByDescending(r => (int)r.GetType().GetField("Order").GetValue(r)).FirstOrDefault();
        if (top == null) return string.Empty;
        string t = Text(top).text;
        int cut = t.IndexOf(" <color", System.StringComparison.Ordinal);
        return cut > 0 ? t.Substring(0, cut) : t;
    }

    private static void Dump(StringBuilder sb, string label)
    {
        var f = Feed();
        sb.AppendLine($"■ {label} — 목록 {(f == null ? "없음" : f.gameObject.activeInHierarchy ? "보임" : "꺼진 부모 아래")} · 줄 {Alive().Length}");
        foreach (var r in Alive())
        {
            var rt = (RectTransform)r.GetType().GetField("Rect").GetValue(r);
            var c = new Vector3[4]; rt.GetWorldCorners(c);
            sb.AppendLine($"   「{Text(r).text}」 화면 x {c[0].x:0}~{c[2].x:0} · y {c[0].y:0}~{c[1].y:0} · 알파 {((CanvasGroup)r.GetType().GetField("Group").GetValue(r)).alpha:0.00}");
        }
    }

    private static UniTask Wait(float s) => UniTask.Delay((int)(s * 1000), DelayType.Realtime);

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
