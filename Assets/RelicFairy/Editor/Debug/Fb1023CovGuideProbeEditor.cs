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
/// 10-23 사용자 「서약의 추가 작성은 여전히 보기가 불편 — 가이드 선 등」 실측 — 서약서 쓰기 판(테스트 허브, 플레이 중). 런이 없으면 허브에서 Ch1 런을 띄운다.
/// 상태마다 사진(Temp/ui_shots/COV23_*.png)과 점검(Temp/cov23.txt):
///  A 문장 1절 — 1 이어 쓸 자리(고르기 전 · 점선 가지) · 2 첫 카드 고름 · 3 끝 카드 고름 · 4 [고쳐 쓰기] 탭 · 5 1절 고침(고르기 전) · 6 1절 고침 + 카드 · 7 원인 고침 + 카드
///  B 문장 3절 — 8 이어 쓸 자리 · 9 가운데 카드 고름   C 가득 찬 문장 — 10 [고쳐 쓰기] 줄 고르기 전 · 11 끝 절 고침 + 카드
/// 점검: 줄 글 · 머리칸 · 안내 · 빈도 줄 넘침 · 글자 하한 / 글 ↔ 머리칸 ↔ 마디 ↔ [고치기] 겹침 · 줄끼리 겹침 · 글이 줄 밖으로 / 마디가 척추 위 /
///       가이드 선(줄기 · 기둥 · 가지)이 채울 줄 · 고른 카드에 닿는가 · 기둥이 지면과 카드 사이 틈에 있는가 · 선이 글을 가로지르지 않는가 / 고르면 이미 쓴 줄이 옅어지는가.
/// 서약 목록은 끝나면 원래대로 돌린다. 로그 「[Cov23] 끝」.
/// </summary>
public static class Fb1023CovGuideProbeEditor
{
    private const string Tag     = "[Cov23]";
    private const string OutTxt  = "Temp/cov23.txt";
    private const string ShotDir = "Temp/ui_shots";
    private const string OneClause   = "sen:slaughter@silver>supernova@silver";
    private const string ThreeClause = "sen:streak@silver>ember@silver>detonate@gold~d>fury@silver~i";
    private static readonly string[] FullChain = { "ember@silver", "detonate@gold~d", "fury@silver~i", "supernova@ruby~w", "curse@silver~i" };
    private const float RowMinSize = 20f;   // UI_CovenantAssemble.Write.cs RowMinSize
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Color LabelInk  = new(0.290f, 0.220f, 0.125f, 1f);   // #4A3820
    private static readonly Color InkOnBook = new(0.165f, 0.110f, 0.055f, 1f);   // #2A1C0E

    private static int s_pass, s_fail;

    [MenuItem("RelicFairy/Debug/10-23 서약서 이어 쓰기 가이드 선 촬영 · 겹침 점검 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중(테스트 허브)에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        s_pass = s_fail = 0;
        var sb = new StringBuilder("10-23 서약서 쓰기 판 — 가이드 선 · 머리칸 · 척추\n");
        Directory.CreateDirectory(ShotDir);
        CovenantHandler handler = null;
        List<CovenantBase> list = null;
        var backup = new List<string>();
        CancellationTokenSource cts = null;
        try
        {
            handler = await EnsureRunAsync(sb);
            if (handler == null) return;
            list = (List<CovenantBase>)typeof(CovenantHandler).GetField("_covenants", Any).GetValue(handler);
            foreach (var c in list) backup.Add(c.CovenantId);

            // ── A 문장 1절 ──
            var p = await OpenAsync(handler, list, OneClause, 2301, sb);
            if (p != null)
            {
                int n = CardCount(p);
                await Step(p, sb, "1_AppendEmpty", "이어 쓸 자리 · 고르기 전(점선 가지 → 카드 전부)");
                Call(p, "SelectWrite", 0);
                await Step(p, sb, "2_AppendPick0", "첫 카드 고름(금선 → 2절 자리 · 쓴 줄 옅게)");
                Call(p, "SelectWrite", Mathf.Max(0, n - 1));
                await Step(p, sb, "3_AppendPickLast", "끝 카드 고름");
                Call(p, "OnTab", false);
                await Step(p, sb, "4_RewriteTab", "[고쳐 쓰기] 탭 · 줄 고르기 전(가이드 선 없음)");
                Call(p, "OnFix", 1);
                await Step(p, sb, "5_RewriteLine", "1절 고침 · 고르기 전");
                Call(p, "SelectWrite", 1);
                await Step(p, sb, "6_RewritePick", "1절 고침 · 가운데 카드");
                Call(p, "OnFix", 0);
                Call(p, "SelectWrite", 0);
                await Step(p, sb, "7_CausePick", "원인 고침 · 첫 원인 카드");
            }
            cts = CloseBoard(cts);
            await UniTask.Delay(400, ignoreTimeScale: true);

            // ── B 문장 3절 ──
            p = await OpenAsync(handler, list, ThreeClause, 2302, sb);
            if (p != null)
            {
                await Step(p, sb, "8_Long_Append", "3절 문장 · 이어 쓸 자리(4절)");
                Call(p, "SelectWrite", 1);
                await Step(p, sb, "9_Long_Pick", "3절 문장 · 가운데 카드 고름");
            }
            cts = CloseBoard(cts);
            await UniTask.Delay(400, ignoreTimeScale: true);

            // ── C 가득 찬 문장 ──
            int k = Mathf.Clamp(CovenantGrammar.MaxResultsNow, 1, FullChain.Length);
            string full = CovenantSentence.Prefix + "streak@silver>" + string.Join(">", FullChain, 0, k);
            p = await OpenAsync(handler, list, full, 2303, sb);
            if (p != null)
            {
                await Step(p, sb, "10_Full", $"가득 참({k}절) · 줄 고르기 전");
                Call(p, "OnFix", k);
                Call(p, "SelectWrite", 0);
                await Step(p, sb, "11_Full_FixLast", $"가득 참 · {k}절 고침 + 첫 카드");
            }
            cts = CloseBoard(cts);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); s_fail++; }
        finally
        {
            CloseBoard(cts);
            if (handler != null && list != null)
            {
                Clear(list);
                foreach (var id in backup) if (id.StartsWith(CovenantSentence.Prefix)) handler.TryWriteSentence(id); else handler.TryAdd(id);
            }
            sb.Insert(0, $"통과 {s_pass} · 실패 {s_fail}\n");
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝 — 통과 {s_pass} · 실패 {s_fail}\n{sb}");
        }

        // 판은 OpenAsync가 연 것 하나 — 닫고 대기를 끊는다
        CancellationTokenSource CloseBoard(CancellationTokenSource c)
        {
            var open = Popup();
            if (open != null) open.ClosePopupUI();
            c?.Cancel();
            return null;
        }

        async UniTask<UI_CovenantAssemble> OpenAsync(CovenantHandler h, List<CovenantBase> l, string sentence, int seed, StringBuilder log)
        {
            Clear(l);
            bool ok = h.TryWriteSentence(sentence);
            log.AppendLine($"\n문장 {sentence} · 새김 {ok}");
            if (!ok) { s_fail++; return null; }
            cts = new CancellationTokenSource();
            CovenantAssembleUI.WriteAsync(h.Sentence, new CovenantWriteBoard(seed), cts.Token).Forget();
            await UniTask.Delay(1200, ignoreTimeScale: true);
            var opened = Popup();
            if (opened == null) { log.AppendLine("판이 안 열림"); s_fail++; }
            return opened;
        }
    }

    /// <summary>런이 있으면 그대로, 없으면 테스트 허브에서 Ch1 런(보스 앞 아님)을 띄워 서약 처리기 · 플레이어가 설 때까지 기다린다(60초). Fb1018cov와 같다.</summary>
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
        await UniTask.Delay(400, ignoreTimeScale: true);   // 카드 확대 · 줄 금선 전이(0.15~0.2초)가 끝난 뒤
        await ShotAsync("COV23_" + shot);
        sb.AppendLine($"── {shot} {label} — 편집 {Get<int>(p, "_editIndex")} · 채울 줄 {Get<int>(p, "_targetRow")} · 고른 카드 {Get<int>(p, "_selWrite")}");
        sb.AppendLine($"   안내 「{Plain(Field<TMP_Text>(p, "_guideText"))}」");
        Audit(p, sb);
    }

    // ── 점검 ─────────────────────────────────────────────
    private static void Audit(UI_CovenantAssemble p, StringBuilder sb)
    {
        var book    = Field<RectTransform>(p, "_bookRect");
        var page    = Field<RectTransform>(p, "_page");
        var buttons = Field<List<Button>>(p, "_rowButtons");
        var texts   = Field<List<TMP_Text>>(p, "_rowTexts");
        var gutters = Field<List<TMP_Text>>(p, "_gutterTexts");
        var nodes   = Field<List<TMP_Text>>(p, "_nodeTexts");
        var chips   = Field<List<Image>>(p, "_fixFills");
        var guide   = Field<TMP_Text>(p, "_guideText");
        var freq    = Field<TMP_Text>(p, "_freqText");
        var spine   = Field<Image>(p, "_spine");
        if (book == null || page == null || buttons == null || texts == null || gutters == null || nodes == null)
        {
            Check(sb, "쓰기 판 필드(머리칸 · 마디)를 못 찾음", false);
            return;
        }
        Rect pageR = WorldRect(book, page);
        int target = Get<int>(p, "_targetRow");
        int sel    = Get<int>(p, "_selWrite");
        bool picked = sel >= 0 && target >= 0;

        var textRects = new List<Rect>();
        Rect lastRow = default, prevRow = default;
        int used = 0;
        float spineX = spine != null && spine.enabled ? WorldRect(book, spine.rectTransform).center.x : float.NaN;
        for (int r = 0; r < buttons.Count; r++)
        {
            if (!buttons[r].gameObject.activeInHierarchy) continue;
            used++;
            var rowR = WorldRect(book, (RectTransform)buttons[r].transform);
            var t = texts[r];
            t.ForceMeshUpdate();
            gutters[r].ForceMeshUpdate();
            Rect tr = TextRect(book, t), gr = TextRect(book, gutters[r]), nr = TextRect(book, nodes[r]);
            textRects.Add(tr);
            textRects.Add(gr);

            string name = $"줄{r} 「{Short(Plain(t))}」 머리칸 「{Plain(gutters[r])}」";
            sb.AppendLine($"   {name} · 글자 {t.fontSize:0.#} · 줄 높이 {rowR.height:0}");
            Check(sb, $"{name} — 글 넘침 없음", !t.isTextOverflowing);
            Check(sb, $"{name} — 글자 {t.fontSize:0.#} ≥ {RowMinSize}", t.fontSize >= RowMinSize - 0.01f);
            Check(sb, $"{name} — 머리칸 넘침 없음", !gutters[r].isTextOverflowing);
            Check(sb, $"{name} — 글 ↔ 머리칸 겹침 없음", !Hit(tr, gr));
            if (nr.width > 0f)
            {
                Check(sb, $"{name} — 마디 ↔ 글 · 머리칸 겹침 없음", !Hit(nr, tr) && !Hit(nr, gr));
                if (!float.IsNaN(spineX)) Check(sb, $"{name} — 마디가 척추 위(어긋남 {Mathf.Abs(nr.center.x - spineX):0.0}px ≤ 2)", Mathf.Abs(nr.center.x - spineX) <= 2f);
            }
            if (chips != null && r < chips.Count && chips[r].gameObject.activeInHierarchy)
                Check(sb, $"{name} — 글 ↔ [고치기] 겹침 없음", !Hit(tr, WorldRect(book, chips[r].rectTransform)));
            if (tr.width > 0f)
                Check(sb, $"{name} — 글이 줄 안(위 {rowR.yMax - tr.yMax:0.0} · 아래 {tr.yMin - rowR.yMin:0.0}px)", tr.yMax <= rowR.yMax + 2f && tr.yMin >= rowR.yMin - 2f);
            Check(sb, $"{name} — 글 오른끝이 지면 안", tr.width <= 0f || tr.xMax <= pageR.xMax + 1f);
            if (used > 1) Check(sb, $"{name} — 윗줄과 겹침 없음", rowR.yMax <= prevRow.yMin + 0.5f);
            prevRow = rowR;
            lastRow = rowR;

            if (picked)
            {
                bool want = r == target;
                Color c = t.color;
                Color expect = want ? InkOnBook : LabelInk;
                Check(sb, $"{name} — {(want ? "채울 줄 = 먹" : "이미 쓴 줄 = 옅은 잉크")}", SameRgb(c, expect));
            }
        }

        if (guide != null)
        {
            guide.ForceMeshUpdate();
            Rect gr = TextRect(book, guide);
            Check(sb, $"안내 넘침 없음 · 글자 {guide.fontSize:0.#}", !guide.isTextOverflowing);
            Check(sb, "안내가 지면 폭 안", gr.width <= 0f || gr.xMax <= pageR.xMax + 1f);
            if (used > 0 && buttons[0].gameObject.activeInHierarchy)
                Check(sb, "안내 ↔ 첫 줄 겹침 없음", !Hit(gr, WorldRect(book, (RectTransform)buttons[0].transform)));
            textRects.Add(gr);
        }
        if (freq != null && freq.gameObject.activeInHierarchy && used > 0)
        {
            freq.ForceMeshUpdate();
            Rect fr = TextRect(book, freq);
            Check(sb, "빈도 줄 넘침 없음", !freq.isTextOverflowing);
            Check(sb, $"빈도 줄이 줄 묶음 아래 · 지면 안(아래 여백 {fr.yMin - pageR.yMin:0}px)", fr.yMax <= lastRow.yMin + 1f && fr.yMin >= pageR.yMin - 2f);
            textRects.Add(fr);
        }

        AuditWire(p, sb, book, pageR, target, sel, textRects);
    }

    /// <summary>가이드 선 — 채울 줄 ↔ 카드. 고르기 전 = 점선 가지가 카드 전부로 · 고르면 = 그 카드 길 하나(금 2px).</summary>
    private static void AuditWire(UI_CovenantAssemble p, StringBuilder sb, RectTransform book, Rect pageR, int target, int sel, List<Rect> textRects)
    {
        var stub     = Field<Image>(p, "_wireStub");
        var trunk    = Field<Image>(p, "_wireTrunk");
        var branches = Field<Image[]>(p, "_wireBranches");
        var cards    = Field<UI_AssembleCard[]>(p, "_effectCards");
        var buttons  = Field<List<Button>>(p, "_rowButtons");
        if (stub == null || trunk == null || branches == null || cards == null)
        {
            Check(sb, "가이드 선 필드를 못 찾음", false);
            return;
        }
        int n = CardCount(p);
        bool expect = target >= 0 && n > 0;
        Check(sb, $"가이드 선 {(expect ? "켜짐" : "꺼짐")}(채울 줄 {target} · 카드 {n})", stub.enabled == expect && trunk.enabled == expect);
        if (!expect)
        {
            int on = 0;
            foreach (var b in branches) if (b.enabled) on++;
            Check(sb, $"가지 0개(지금 {on})", on == 0);
            return;
        }

        bool picked = sel >= 0 && sel < n;
        Rect rowR   = WorldRect(book, (RectTransform)buttons[target].transform);
        Rect stubR  = WorldRect(book, stub.rectTransform);
        Rect trunkR = WorldRect(book, trunk.rectTransform);
        float cardLeft = float.MaxValue;
        for (int i = 0; i < n; i++) if (cards[i] != null && cards[i].gameObject.activeInHierarchy) cardLeft = Mathf.Min(cardLeft, WorldRect(book, (RectTransform)cards[i].transform).xMin);

        Check(sb, $"줄기가 채울 줄 높이(줄 {rowR.yMin:0}~{rowR.yMax:0} · 줄기 {stubR.center.y:0})", stubR.center.y > rowR.yMin && stubR.center.y < rowR.yMax);
        Check(sb, $"줄기가 줄 오른끝에서 시작(차 {Mathf.Abs(stubR.xMin - rowR.xMax):0.0}px ≤ 3)", Mathf.Abs(stubR.xMin - rowR.xMax) <= 3f);
        Check(sb, $"기둥이 지면({pageR.xMax:0}) ~ 카드({cardLeft:0}) 틈 안(x {trunkR.center.x:0})", trunkR.center.x > pageR.xMax && trunkR.center.x < cardLeft);

        int onCount = 0;
        for (int i = 0; i < branches.Length; i++)
        {
            if (!branches[i].enabled) continue;
            onCount++;
            Rect br = WorldRect(book, branches[i].rectTransform);
            Rect cr = WorldRect(book, (RectTransform)cards[i].transform);
            Check(sb, $"가지{i} 끝이 카드{i} 몸통 높이 · 카드 밑으로(y {br.center.y:0} ∈ {cr.yMin:0}~{cr.yMax:0} · 끝 x {br.xMax:0} > {cr.xMin:0})",
                  br.center.y > cr.yMin && br.center.y < cr.yMax && br.xMax > cr.xMin);
            Check(sb, $"가지{i} 기둥에서 시작(차 {Mathf.Abs(br.xMin - trunkR.xMin):0.0}px ≤ 2)", Mathf.Abs(br.xMin - trunkR.xMin) <= 2f);
            foreach (var t in textRects) if (Hit(br, t)) { Check(sb, $"가지{i}가 글을 가로지름", false); break; }
        }
        if (picked)
        {
            Check(sb, $"고름 — 가지는 고른 카드{sel} 하나(켜짐 {onCount})", onCount == 1 && branches[sel].enabled);
            Check(sb, $"고름 — 금선 진하게(α {stub.color.a:0.00}) · 실선 · 2px(높이 {stubR.height:0.0})", stub.color.a >= 0.99f && stub.sprite == null && stubR.height >= 1.9f);
        }
        else
        {
            Check(sb, $"고르기 전 — 가지 = 카드 수 {n}(켜짐 {onCount})", onCount == n);
            Check(sb, $"고르기 전 — 점선 · 옅게(α {stub.color.a:0.00})", stub.sprite != null && stub.color.a < 0.99f);
        }
        foreach (var t in textRects)
            if (Hit(stubR, t) || Hit(trunkR, t)) { Check(sb, "줄기 · 기둥이 글을 가로지름", false); return; }
        Check(sb, "줄기 · 기둥 · 가지가 글을 가로지르지 않음", true);
    }

    // ── 도구 ─────────────────────────────────────────────
    private static int CardCount(UI_CovenantAssemble p)
    {
        var cards = Field<UI_AssembleCard[]>(p, "_effectCards");
        int n = 0;
        if (cards != null)
            for (int i = 0; i < cards.Length; i++)
                if (cards[i] != null && cards[i].gameObject.activeInHierarchy) n = i + 1;
        return n;
    }

    /// <summary>RectTransform 네 귀 → 판(Book) 좌표 사각형.</summary>
    private static Rect WorldRect(RectTransform space, RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        return FromWorld(space, c[0], c[2]);
    }

    /// <summary>글이 실제로 그려진 상자(textBounds) → 판 좌표. 빈 글이면 빈 사각형.</summary>
    private static Rect TextRect(RectTransform space, TMP_Text t)
    {
        if (t == null || !t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text)) return default;
        var b = t.textBounds;
        if (b.size.x <= 0f || b.size.y <= 0f) return default;
        return FromWorld(space, t.transform.TransformPoint(b.min), t.transform.TransformPoint(b.max));
    }

    private static Rect FromWorld(RectTransform space, Vector3 a, Vector3 b)
    {
        Vector2 p = space.InverseTransformPoint(a), q = space.InverseTransformPoint(b);
        return Rect.MinMaxRect(Mathf.Min(p.x, q.x), Mathf.Min(p.y, q.y), Mathf.Max(p.x, q.x), Mathf.Max(p.y, q.y));
    }

    /// <summary>두 사각형이 0.5px 넘게 겹치는가(빈 사각형은 안 겹침).</summary>
    private static bool Hit(Rect a, Rect b)
    {
        const float tol = 0.5f;
        if (a.width <= 0f || b.width <= 0f) return false;
        return a.xMin < b.xMax - tol && b.xMin < a.xMax - tol && a.yMin < b.yMax - tol && b.yMin < a.yMax - tol;
    }

    private static bool SameRgb(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

    private static void Check(StringBuilder sb, string what, bool ok)
    {
        if (ok) s_pass++; else s_fail++;
        sb.AppendLine($"   [{(ok ? "O" : "X")}] {what}");
    }

    private static T Field<T>(object target, string name) where T : class
        => target.GetType().GetField(name, Any)?.GetValue(target) as T;

    private static T Get<T>(object target, string name) where T : struct
        => target.GetType().GetField(name, Any)?.GetValue(target) is T v ? v : default;

    private static object Call(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, Any)?.Invoke(target, args);

    /// <summary>리치 텍스트 태그를 뗀 글(로그용).</summary>
    private static string Plain(TMP_Text t)
        => t == null ? "-" : System.Text.RegularExpressions.Regex.Replace(t.text ?? string.Empty, "<[^>]*>", string.Empty).Replace("\n", " / ");

    private static string Short(string s) => s.Length <= 24 ? s : s.Substring(0, 24) + "…";

    private static void Clear(List<CovenantBase> list)
    {
        foreach (var c in list) c.Dispose();
        list.Clear();
    }

    private static UI_CovenantAssemble Popup() => Object.FindFirstObjectByType<UI_CovenantAssemble>();

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
