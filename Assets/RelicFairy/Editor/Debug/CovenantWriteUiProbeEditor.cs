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
/// 10-02 「한 장의 서약서」 U1 실측 — 제단 판 세 가지 모습을 게임 화면으로 찍는다(Temp/ui_shots/CovWrite_*).
/// ① 첫 쓰기(문장 없음 — 조건 × 첫 결과) ② 이어 쓰기(2절 문장 · 둘째 카드 고름) ③ 고쳐 쓰기(첫 결과 줄 [고쳐]) ④ 가득 찬 문장(5절).
/// 판은 닫고 서약 목록은 원래대로 돌린다. 로그 「[CovWriteProbe] 끝」. ⚠️ 런 중(대기방이면 충분), 플레이 중.
///
/// 10-11 서약서 개편 실측(<see cref="RunUx1011"/>) — 조립 판 3 · 쓰기 판 3 = 여섯 장(Temp/ui_shots/CovUx_*) +
/// 상태 · 글자 점검 글(Temp/covenant_ux_1011_probe.txt). 로그 「[CovUx1011Probe] 끝」.
/// </summary>
public static class CovenantWriteUiProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-02 서약서 쓰기 판 촬영 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.CovenantHandler == null)
        {
            Debug.LogWarning("[CovWriteProbe] 런 중에 실행해야 한다.");
            return;
        }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("서약서 쓰기 판 촬영\n");
        var handler = GameRunBootstrapper.Instance.Run.CovenantHandler;
        var list = (List<CovenantBase>)typeof(CovenantHandler).GetField("_covenants", Inst).GetValue(handler);
        var backup = new List<string>();
        foreach (var c in list) backup.Add(c.CovenantId);
        try
        {
            // ① 첫 쓰기
            Clear(list);
            var cts = new CancellationTokenSource();
            CovenantAssembleUI.ChooseAsync(false, new System.Random(7), cts.Token).Forget();
            await UniTask.Delay(1200, ignoreTimeScale: true);
            await Shot("CovWrite_1_First");
            sb.AppendLine("① 첫 쓰기 판 " + (Popup() != null ? "열림" : "없음"));
            ClosePopup(); cts.Cancel();
            await UniTask.Delay(400, ignoreTimeScale: true);

            // ② 이어 쓰기 — 2절 문장
            Clear(list);
            handler.TryWriteSentence("sen:streak@gold>supernova@silver>ember@ruby~i");
            var board = new CovenantWriteBoard(11);
            cts = new CancellationTokenSource();
            CovenantAssembleUI.WriteAsync(handler.Sentence, board, cts.Token).Forget();
            await UniTask.Delay(1200, ignoreTimeScale: true);
            var popup = Popup();
            sb.AppendLine("② 이어 쓰기 판 " + (popup != null ? "열림" : "없음") + $" · 카드 {board.Append?.Count ?? 0}");
            if (popup != null) Invoke(popup, "SelectWrite", 1);
            await UniTask.Delay(300, ignoreTimeScale: true);
            await Shot("CovWrite_2_Append");

            // ③ 고쳐 쓰기 — 첫 결과 줄
            if (popup != null) Invoke(popup, "OnFix", 1);
            await UniTask.Delay(300, ignoreTimeScale: true);
            await Shot("CovWrite_3_Rewrite");
            sb.AppendLine("③ 고쳐 쓰기 카드 " + (board.Rewrite.TryGetValue(1, out var rw) ? rw.Count : 0));
            ClosePopup(); cts.Cancel();
            await UniTask.Delay(400, ignoreTimeScale: true);

            // ④ 가득 찬 문장
            Clear(list);
            handler.TryWriteSentence("sen:streak@silver>ember@silver>detonate@gold~i>fury@silver~i>supernova@ruby~w>curse@silver~i");
            cts = new CancellationTokenSource();
            CovenantAssembleUI.WriteAsync(handler.Sentence, new CovenantWriteBoard(13), cts.Token).Forget();
            await UniTask.Delay(1200, ignoreTimeScale: true);
            await Shot("CovWrite_4_Full");
            sb.AppendLine("④ 가득 찬 문장 판 " + (Popup() != null ? "열림" : "없음"));
            ClosePopup(); cts.Cancel();
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Clear(list);
            foreach (var id in backup) if (id.StartsWith(CovenantSentence.Prefix)) handler.TryWriteSentence(id); else handler.TryAdd(id);
            File.WriteAllText("Temp/covenant_write_ui_probe.txt", sb.ToString());
            Debug.Log("[CovWriteProbe] 끝\n" + sb);
        }
    }

    // ── 10-11 서약서 조립 · 쓰기 판 개편 실측 ─────────────────
    // 가득 찬 문장의 결과 사슬 — 앞에서부터 「지금 결과 상한(MaxResultsNow)」만큼 쓴다(제단 노드에 따라 1~5절).
    // 앞 절만 잘라도 이음 규칙이 깨지지 않는 사슬이다(10-02 ④와 같은 문장).
    private static readonly string[] FullChain = { "ember@silver", "detonate@gold~i", "fury@silver~i", "supernova@ruby~w", "curse@silver~i" };

    [MenuItem("RelicFairy/Debug/10-11 서약서 조립·쓰기 판 촬영 (런 중)")]
    private static void RunUx1011()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.CovenantHandler == null)
        {
            Debug.LogWarning("[CovUx1011Probe] 런 중에 실행해야 한다.");
            return;
        }
        RunUx1011Async().Forget();
    }

    /// <summary>
    /// 조립 판: A1 빈 상태 → A2 원인만(둘째 원인) → A3 둘 다(셋째 효과).
    /// 쓰기 판(가득 찬 문장): W1 가득 참 → W2 줄 고름(첫 결과 줄) → W3 카드 고름(둘째 카드).
    /// 사진마다 상태 한 줄(자리표시 · 버튼 잠금 · 안내 문구 · 열 머리)과 판 전체 글자 점검(DNF · 기울임 · 가짜 굵게 · 「[골드]」)을 남긴다.
    /// </summary>
    private static async UniTaskVoid RunUx1011Async()
    {
        var sb = new StringBuilder("10-11 서약서 조립 · 쓰기 판 촬영 — Temp/ui_shots/CovUx_*.png\n");
        var handler = GameRunBootstrapper.Instance.Run.CovenantHandler;
        var list = (List<CovenantBase>)typeof(CovenantHandler).GetField("_covenants", Inst).GetValue(handler);
        var backup = new List<string>();
        foreach (var c in list) backup.Add(c.CovenantId);
        try
        {
            // ── 조립 판 ──
            Clear(list);
            var cts = new CancellationTokenSource();
            CovenantAssembleUI.ChooseAsync(false, new System.Random(7), cts.Token).Forget();
            await UniTask.Delay(1200, ignoreTimeScale: true);
            var popup = Popup();
            sb.AppendLine("조립 판 " + (popup != null ? "열림" : "없음"));
            if (popup != null)
            {
                await Shot("CovUx_A1_Empty");
                sb.AppendLine("A1 빈 상태  — " + AssembleState(popup));
                InvokeArgs(popup, "Select", true, 1);
                await UniTask.Delay(400, ignoreTimeScale: true);   // 두루마리 글 스밈 0.18초가 끝난 뒤
                await Shot("CovUx_A2_Cause");
                sb.AppendLine("A2 원인만   — " + AssembleState(popup));
                InvokeArgs(popup, "Select", false, 2);
                await UniTask.Delay(400, ignoreTimeScale: true);
                await Shot("CovUx_A3_Both");
                sb.AppendLine("A3 둘 다    — " + AssembleState(popup));
                sb.AppendLine("조립 판 글자 — " + FontAudit(popup));
            }
            ClosePopup(); cts.Cancel();
            await UniTask.Delay(400, ignoreTimeScale: true);

            // ── 쓰기 판(가득 찬 문장) ──
            Clear(list);
            int k = Mathf.Clamp(CovenantGrammar.MaxResultsNow, 1, FullChain.Length);
            string sid = CovenantSentence.Prefix + "streak@silver>" + string.Join(">", FullChain, 0, k);
            bool wrote = handler.TryWriteSentence(sid);
            sb.AppendLine($"가득 찬 문장 {sid} · 결과 상한 {CovenantGrammar.MaxResultsNow} · 새김 {wrote}");
            cts = new CancellationTokenSource();
            CovenantAssembleUI.WriteAsync(handler.Sentence, new CovenantWriteBoard(13), cts.Token).Forget();
            await UniTask.Delay(1200, ignoreTimeScale: true);
            popup = Popup();
            sb.AppendLine("쓰기 판 " + (popup != null ? "열림" : "없음"));
            if (popup != null)
            {
                await Shot("CovUx_W1_Full");
                sb.AppendLine("W1 가득 참   — " + WriteState(popup));
                Invoke(popup, "OnFix", 1);
                await UniTask.Delay(300, ignoreTimeScale: true);
                await Shot("CovUx_W2_Line");
                sb.AppendLine("W2 줄 고름   — " + WriteState(popup));
                Invoke(popup, "SelectWrite", 1);
                await UniTask.Delay(300, ignoreTimeScale: true);
                await Shot("CovUx_W3_Card");
                sb.AppendLine("W3 카드 고름 — " + WriteState(popup));
                sb.AppendLine("쓰기 판 글자 — " + FontAudit(popup));
            }
            ClosePopup(); cts.Cancel();
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Clear(list);
            foreach (var id in backup) if (id.StartsWith(CovenantSentence.Prefix)) handler.TryWriteSentence(id); else handler.TryAdd(id);
            File.WriteAllText("Temp/covenant_ux_1011_probe.txt", sb.ToString());
            Debug.Log("[CovUx1011Probe] 끝\n" + sb);
        }
    }

    /// <summary>조립 판 한 줄 — 자리표시 두 칸 · [서약 새기기] 잠금 · 등급 줄 · 경고.</summary>
    private static string AssembleState(UI_CovenantAssemble p)
    {
        var causeSlot  = Field<Image>(p, "_causeSlot");
        var effectSlot = Field<Image>(p, "_effectSlot");
        var forge      = Field<Button>(p, "_forgeButton");
        var title      = Field<TMP_Text>(p, "_previewTitle");
        var summary    = Field<TMP_Text>(p, "_forgeSummary");
        return $"원인 칸 {On(causeSlot)} · 효과 칸 {On(effectSlot)} · 새기기 {(forge != null && forge.interactable ? "켜짐" : "잠김")}"
             + $" · 등급 줄 「{Plain(title)}」 · 경고 「{Plain(summary)}」";
    }

    /// <summary>쓰기 판 한 줄 — 단계 안내 · 오른쪽 열(머리 · 자리표시 · 카드 수) · [새긴다] 잠금 · 보이는 줄 수 · 취소선.</summary>
    private static string WriteState(UI_CovenantAssemble p)
    {
        var guide = Field<TMP_Text>(p, "_guideText");
        var empty = Field<Image>(p, "_emptySlot");
        var head  = Field<TMP_Text>(p, "_effectHeaderText");
        var forge = Field<Button>(p, "_forgeButton");
        var rows  = Field<List<TMP_Text>>(p, "_rowTexts");
        var cards = Field<UI_AssembleCard[]>(p, "_effectCards");
        int shownRows = 0, struck = 0, cardsOn = 0;
        if (rows != null)
            foreach (var r in rows)
                if (r != null && r.gameObject.activeInHierarchy) { shownRows++; if (r.text.Contains("<s>")) struck++; }
        if (cards != null) foreach (var c in cards) if (c != null && c.gameObject.activeInHierarchy) cardsOn++;
        return $"안내 「{Plain(guide)}」 · 머리 {(head != null && head.gameObject.activeInHierarchy ? "「" + head.text + "」" : "숨김")}"
             + $" · 자리표시 {(empty != null && empty.gameObject.activeInHierarchy ? "켜짐" : "꺼짐")} · 카드 {cardsOn}"
             + $" · 새긴다 {(forge != null && forge.interactable ? "켜짐" : "잠김")} · 줄 {shownRows}(취소선 {struck})";
    }

    /// <summary>
    /// 판 전체 글자 점검(켜진 글만) — DNF 글꼴 수(제목 1이어야 한다) · 기울임 · 가짜 굵게(Bold 스타일) · 「[골드]」식 등급 날글자 · 글꼴별 개수.
    /// </summary>
    private static string FontAudit(UI_CovenantAssemble p)
    {
        int total = 0, dnf = 0, italic = 0, bold = 0, rawTier = 0;
        var fonts = new Dictionary<string, int>();
        var dnfNames = new List<string>();
        foreach (var t in p.GetComponentsInChildren<TMP_Text>(false))
        {
            if (string.IsNullOrEmpty(t.text)) continue;
            total++;
            string f = t.font != null ? t.font.name : "(없음)";
            fonts[f] = fonts.TryGetValue(f, out var n) ? n + 1 : 1;
            if (f.Contains("DNF")) { dnf++; dnfNames.Add(t.name); }
            if ((t.fontStyle & FontStyles.Italic) != 0 || t.text.Contains("<i>")) italic++;
            if ((t.fontStyle & FontStyles.Bold) != 0 || t.text.Contains("<b>")) bold++;
            if (t.text.Contains("[골드]") || t.text.Contains("[실버]") || t.text.Contains("[루비]")) rawTier++;
        }
        var fs = new StringBuilder();
        foreach (var kv in fonts) fs.Append(kv.Key).Append(' ').Append(kv.Value).Append(" / ");
        return $"글 {total} · DNF {dnf}({string.Join(",", dnfNames)}) · 기울임 {italic} · 가짜 굵게 {bold} · 등급 날글자 {rawTier} · 글꼴 {fs}";
    }

    private static T Field<T>(object target, string name) where T : class
        => target.GetType().GetField(name, Inst)?.GetValue(target) as T;

    private static string On(Behaviour b) => b != null && b.enabled && b.gameObject.activeInHierarchy ? "켜짐" : "꺼짐";

    /// <summary>리치 텍스트 태그를 뗀 글(로그용).</summary>
    private static string Plain(TMP_Text t)
        => t == null ? "-" : System.Text.RegularExpressions.Regex.Replace(t.text ?? string.Empty, "<[^>]*>", string.Empty).Replace("\n", " / ");

    private static void InvokeArgs(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, Inst)?.Invoke(target, args);

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

    private static void Invoke(object target, string method, int arg)
        => target.GetType().GetMethod(method, Inst)?.Invoke(target, new object[] { arg });

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 300 }) : UniTask.CompletedTask;
    }
}
#endif
