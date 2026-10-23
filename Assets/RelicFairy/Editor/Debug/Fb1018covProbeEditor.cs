#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-18 빌드 피드백 ⑤ ⑥ 실측 — 서약서 쓰기 판(테스트 허브, 플레이 중). 런이 없으면 허브에서 Ch1 런을 띄운다.
/// 문장 2줄(「5연속 처치 시 → 초신성」 — 피드백 사진 05와 같은 문장)로 판을 열고 차례로 찍는다:
///  1 이어 쓰기(첫 카드) · 2 [고쳐 쓰기] 탭(줄 고르기 전) · 3 결과 줄 고침(첫 카드 · 라벨 [취소]) · 4 [취소] 뒤(이어 쓰기로)
///  · 5 ESC 길(LeaveRewrite — 사진 없음) · 6 조건 고침(첫 원인 카드 — 결과 수치가 새 원인으로) · 7 가득 찬 문장(쓰러지면 · 그동안 이음 · [이어 쓰기] 잠김).
/// 단계마다 모드 · 탭 · 안내 · 줄 글(글자 크기 · 넘침) · 「카드 수치 = 줄 수치」 대조를 Temp/fb1018_cov.txt에, 사진은 Temp/ui_shots/FB18_cov_*.png.
/// 서약 목록은 끝나면 원래대로 돌린다. 로그 「[FB18cov] 끝」. 실제 ESC 키(첫 ESC = 고쳐 쓰기 풀기 · 둘째 = 닫기)는 손으로 한 번 확인할 것.
/// </summary>
public static class Fb1018covProbeEditor
{
    private const string Tag     = "[FB18cov]";
    private const string OutTxt  = "Temp/fb1018_cov.txt";
    private const string ShotDir = "Temp/ui_shots";
    private const string TwoLine = "sen:slaughter@silver>supernova@silver";
    // 가득 찬 문장의 결과 사슬 — 앞에서부터 「지금 결과 상한(MaxResultsNow)」만큼. 쓰러지면(~d) · 그동안(~w) 이음이 머리말 길이를 잰다.
    private static readonly string[] FullChain = { "ember@silver", "detonate@gold~d", "fury@silver~i", "supernova@ruby~w", "curse@silver~i" };
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-18 서약서 이어 쓰기 · 고쳐 쓰기 탭 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중(테스트 허브)에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-18 서약서 쓰기 판 — ⑤ 수치 줄 · ⑥ 모드 탭\n");
        Directory.CreateDirectory(ShotDir);
        CovenantHandler handler = null;
        List<CovenantBase> list = null;
        var backup = new List<string>();
        try
        {
            handler = await EnsureRunAsync(sb);
            if (handler == null) return;
            list = (List<CovenantBase>)typeof(CovenantHandler).GetField("_covenants", Any).GetValue(handler);
            foreach (var c in list) backup.Add(c.CovenantId);

            // ── 문장 2줄 ──
            Clear(list);
            sb.AppendLine($"문장 {TwoLine} · 새김 {handler.TryWriteSentence(TwoLine)}");
            var cts = new CancellationTokenSource();
            CovenantAssembleUI.WriteAsync(handler.Sentence, new CovenantWriteBoard(18), cts.Token).Forget();
            await UniTask.Delay(1200, ignoreTimeScale: true);
            var p = Popup();
            if (p == null) { sb.AppendLine("판이 안 열림"); cts.Cancel(); return; }

            Call(p, "SelectWrite", 0);
            await Step(p, sb, "1_Append", "이어 쓰기 · 첫 카드");
            Call(p, "OnTab", false);
            await Step(p, sb, "2_RewriteTab", "[고쳐 쓰기] 탭 · 줄 고르기 전");
            Call(p, "OnFix", 1);
            Call(p, "SelectWrite", 0);
            await Step(p, sb, "3_RewriteLine", "결과 줄 고침 · 첫 카드");
            Call(p, "OnFix", 1);   // 고치는 줄을 다시 누름 = [취소]
            await Step(p, sb, "4_Cancel", "[취소] 뒤");

            Call(p, "OnFix", 1);
            object first = Call(p, "LeaveRewrite");
            string after = State(p);
            object second = Call(p, "LeaveRewrite");
            sb.AppendLine($"5_Esc ESC 길 — 고쳐 쓰기에서 풀기 {first} → {after} · 한 번 더 {second}(false = 다음 ESC는 창을 닫는다)");

            Call(p, "OnFix", 0);
            Call(p, "SelectWrite", 0);
            await Step(p, sb, "6_Cause", "조건 고침 · 첫 원인 카드(결과 수치 = 새 원인)");
            ClosePopup(); cts.Cancel();
            await UniTask.Delay(400, ignoreTimeScale: true);

            // ── 가득 찬 문장 ──
            Clear(list);
            int k = Mathf.Clamp(CovenantGrammar.MaxResultsNow, 1, FullChain.Length);
            string full = CovenantSentence.Prefix + "streak@silver>" + string.Join(">", FullChain, 0, k);
            sb.AppendLine($"가득 찬 문장 {full} · 결과 상한 {CovenantGrammar.MaxResultsNow} · 새김 {handler.TryWriteSentence(full)}");
            cts = new CancellationTokenSource();
            CovenantAssembleUI.WriteAsync(handler.Sentence, new CovenantWriteBoard(19), cts.Token).Forget();
            await UniTask.Delay(1200, ignoreTimeScale: true);
            p = Popup();
            if (p != null)
            {
                await Step(p, sb, "7_Full", "가득 참(이음 머리말 · [이어 쓰기] 잠김)");
                Call(p, "OnTab", true);
                sb.AppendLine("  잠긴 [이어 쓰기] 누름 → " + State(p));
            }
            ClosePopup(); cts.Cancel();
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (handler != null && list != null)
            {
                Clear(list);
                foreach (var id in backup) if (id.StartsWith(CovenantSentence.Prefix)) handler.TryWriteSentence(id); else handler.TryAdd(id);
            }
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    /// <summary>런이 있으면 그대로, 없으면 테스트 허브에서 Ch1 런(보스 앞 아님)을 띄워 서약 처리기 · 플레이어가 설 때까지 기다린다(60초).</summary>
    private static async UniTask<CovenantHandler> EnsureRunAsync(StringBuilder sb)
    {
        var h = GameRunBootstrapper.Instance?.Run?.CovenantHandler;
        if (h != null) return h;
        var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { sb.AppendLine("런도 없고 테스트 허브도 아니다"); return null; }
        launcher.SelectChapter(1);
        typeof(TestHubLauncher).GetField("_bossApproach", Any)?.SetValue(launcher, false);
        if (!launcher.TryLaunch()) { sb.AppendLine("런 시작 실패"); return null; }
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 60f)
        {
            await UniTask.Delay(500, ignoreTimeScale: true);
            var run = GameRunBootstrapper.Instance?.Run;
            if (run?.CovenantHandler == null || run.Player == null) continue;
            await UniTask.Delay(2500, ignoreTimeScale: true);   // 대기방 · HUD가 선 뒤
            return run.CovenantHandler;
        }
        sb.AppendLine("런이 60초 안에 안 섰다");
        return null;
    }

    private static async UniTask Step(UI_CovenantAssemble p, StringBuilder sb, string shot, string label)
    {
        await UniTask.Delay(350, ignoreTimeScale: true);
        await ShotAsync("FB18_cov_" + shot);
        sb.AppendLine($"{shot} {label} — {State(p)}");
        Rows(p, sb);
        CardMatch(p, sb);
    }

    /// <summary>모드 한 줄 — 편집 칸 · 탭(금 = 지금 모드 · 잠김) · 단계 안내 · 오른쪽 열 머리 · [새긴다].</summary>
    private static string State(UI_CovenantAssemble p)
    {
        var tabs   = Field<Button[]>(p, "_tabs");
        var labels = Field<TMP_Text[]>(p, "_tabLabels");
        var fills  = Field<Image[]>(p, "_tabFills");
        var ts = new StringBuilder();
        if (tabs != null && labels != null && fills != null)
            for (int i = 0; i < tabs.Length; i++)
                if (tabs[i] != null)
                    ts.Append('[').Append(labels[i].text).Append(fills[i].color.a > 0.01f ? " 금" : string.Empty)
                      .Append(tabs[i].interactable ? string.Empty : " 잠김").Append("] ");
        var guide = Field<TMP_Text>(p, "_guideText");
        var head  = Field<TMP_Text>(p, "_effectHeaderText");
        var forge = Field<Button>(p, "_forgeButton");
        return $"편집 {p.GetType().GetField("_editIndex", Any)?.GetValue(p)} · 탭 {ts}· 안내 「{Plain(guide)}」"
             + $" · 머리 {(head != null && head.gameObject.activeInHierarchy ? "「" + head.text + "」" : "숨김")}"
             + $" · 새긴다 {(forge != null && forge.interactable ? "켜짐" : "잠김")}";
    }

    /// <summary>보이는 줄마다 — 글(태그 뗌) · 줄 라벨(고치기 · 취소) · 자동 크기 결과 · 넘침.</summary>
    private static void Rows(UI_CovenantAssemble p, StringBuilder sb)
    {
        var rows  = Field<List<TMP_Text>>(p, "_rowTexts");
        var chips = Field<List<TMP_Text>>(p, "_fixLabels");
        var fills = Field<List<Image>>(p, "_fixFills");
        if (rows == null) return;
        for (int i = 0; i < rows.Count; i++)
        {
            var t = rows[i];
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            t.ForceMeshUpdate();
            string chip = chips != null && fills != null && i < chips.Count && fills[i].gameObject.activeSelf ? $" [{chips[i].text}]" : string.Empty;
            sb.AppendLine($"    줄{i} 「{Plain(t)}」{chip} · 글자 {t.fontSize:0.#}{(t.isTextOverflowing ? " · 넘침!" : string.Empty)}");
        }
    }

    /// <summary>고른 카드의 수치 글(「곧바로 — 」 뒤)이 문장 줄에 그대로 있는가 — 카드 = 줄 대조(⑤).</summary>
    private static void CardMatch(UI_CovenantAssemble p, StringBuilder sb)
    {
        int sel = p.GetType().GetField("_selWrite", Any)?.GetValue(p) is int s ? s : -1;
        var cards = Field<UI_AssembleCard[]>(p, "_effectCards");
        if (sel < 0 || cards == null || sel >= cards.Length || cards[sel] == null) return;
        if (typeof(UI_AssembleCard).GetField("_subRaw", Any)?.GetValue(cards[sel]) is not string raw || raw.Length == 0) return;
        int dash = raw.IndexOf(" — ", System.StringComparison.Ordinal);
        string amount = dash >= 0 ? raw.Substring(dash + 3) : raw;
        bool found = false;
        var rows = Field<List<TMP_Text>>(p, "_rowTexts");
        if (rows != null)
            foreach (var t in rows)
                if (t != null && t.gameObject.activeInHierarchy && Plain(t).Contains(amount)) found = true;
        sb.AppendLine($"    카드 = 줄: 「{amount}」 → {(found ? "통과" : "실패(줄에 없다)")}");
    }

    private static T Field<T>(object target, string name) where T : class
        => target.GetType().GetField(name, Any)?.GetValue(target) as T;

    private static object Call(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, Any)?.Invoke(target, args);

    /// <summary>리치 텍스트 태그를 뗀 글(로그용).</summary>
    private static string Plain(TMP_Text t)
        => t == null ? "-" : System.Text.RegularExpressions.Regex.Replace(t.text ?? string.Empty, "<[^>]*>", string.Empty).Replace("\n", " / ");

    private static void Clear(List<CovenantBase> list)
    {
        foreach (var c in list) c.Dispose();
        list.Clear();
    }

    private static UI_CovenantAssemble Popup() => Object.FindFirstObjectByType<UI_CovenantAssemble>();

    private static void ClosePopup()
    {
        var p = Popup();
        if (p != null) p.ClosePopupUI();
    }

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
