#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-11 보스 HUD · 서약 칸 톤 v2 실측 — 보스바 + 이름 · 대사 한 줄 · 기술 이름(짧은 것 · 가장 긴 외침 · 카운터) · 서약 칸(+ 퀘스트 완료 알림)을 5장 찍고,
/// 글꼴 · 크기 · 굵기 · 알파와 패턴 예고 문장 전부의 줄 수 · 폭 · 넘침을 적는다(f9 요청 — 기사 기술 이름 말고도 리치 외침 · 카운터 · 기사 규칙 자막이 같은 칸을 쓴다).
/// 테스트 허브(런 없이)에서도 돈다: 꺼진 HUD 사슬을 잠깐 켰다가 끝나면 되돌린다. 서약 칸은 표본 서약서를 HUD에만 묶고(런 상태는 안 바꾼다) 끝나면 원래 목록으로 되돌린다.
/// 결과: Temp/ui_shots/BossHUD_*.png · Temp/boss_hud_covenant_probe.txt · 로그 「[BossHudProbe] 끝」. ⚠️ 플레이 중.
/// </summary>
public static class BossHudCovenantShotProbeEditor
{
    private const string OutFile = "Temp/boss_hud_covenant_probe.txt";
    private const string SampleSentence = "sen:streak@gold>supernova@gold>ember@ruby~i";

    // 패턴 예고(PatternAnnounce)로 뜨는 고정 문구 — 코드 상수(출처 파일) · 카운터 표본. 대사 CSV 키는 아래 PatternKeys에서 읽는다.
    private static readonly (string src, string text)[] PatternTexts =
    {
        ("DK 큰 기술 · 검의 심판",        "검의 심판"),
        ("DK 큰 기술 · 무너지는 원탁",    "무너지는 원탁"),
        ("DK 큰 기술 · 세 번의 십자",     "세 번의 십자"),
        ("DK 규칙 · FloorRuleHint",       "붉게 차오르는 칸이 베인다"),
        ("DK 규칙 · CrossFlipHint",       "색이 뒤집힌다 — 마지막 색의 반대로"),
        ("DK 규칙 · TreasonColorHint",    "환영이 붉은 칸을 벤다 — 무채색 칸으로"),
        ("DK 규칙 · FlipToWhiteHint",     "검이 하얗게 물들었다"),
        ("DK 기수 · Hint",                "기수가 서 있는 동안 기사가 거세진다 — 기수부터 베어라"),
        ("리치 · ParryHintText",          "낫이 빛나는 순간 — 맞받아쳐라"),
        ("리치 · BindHint",               "금빛 마법탄은 묶는다 — 피해라"),
        ("리치 · AbyssalRend",            "심연이 너희의 땅을 갈라 삼킨다!"),
        ("리치 · ArcaneTorrent",          "원소의 격류가 너를 삼킨다!"),
        ("리치 · FallingStars",           "별이 떨어진다 — 설 곳은 남지 않으리라!"),
        ("리치 · FinalMagic 재시작",      "아직 끝나지 않았다 — 다시!"),
        ("리치 · ScytheMagic",            "낫이여 — 길을 불태워라!"),
        ("리치 · SealShards",             "네 봉인의 잔해다 — 이제 내 것이지."),
        ("리치 · SkyDebris",              "무너진 하늘이 네 위로."),
        ("리치 · SoulCopy",               "네 영혼, 조금만 빌리지."),
        ("리치 · SealShard 소등",         "봉인 조각 소등 — 5초"),
        ("카운터 · 봉인석 점화",          "봉인석 점화 (3 / 4)"),
        ("카운터 · 봉인 사슬",            "봉인 사슬 (3 / 4)"),
        ("카운터 · 기술 봉인(둘)",        "E · R 기술 봉인 — 6초 · 둔화"),
        ("카운터 · 기술 봉인(하나)",      "R 기술 봉인 — 6초"),
        ("화룡 · 검은 태양(기본값)",      "검은 태양이… 떠오른다."),
    };

    // 대사 CSV 첫 줄이 패턴 예고로 뜨는 시퀀스(멀린 · 그림자 줄은 내레이션이라 뺀다)
    private static readonly string[] PatternKeys =
    {
        "Lich_ChainBreak", "Lich_DarkRain", "Lich_Realign", "Lich_Ward", "Lich_Ward_Broken", "Lich_Ward_Timeout",
        "Lich_ReverseSeal", "Lich_MerlinVoice", "Lich_Legion", "DK_Skill_Strike", "DK_Skill_Pyramid", "DK_Skill_QuickP2",
    };

    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool s_running;

    [MenuItem("RelicFairy/Debug/10-11 보스 HUD·서약 칸 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BossHudProbe] 플레이 중에 실행해야 한다."); return; }
        if (s_running) { Debug.LogWarning("[BossHudProbe] 이미 도는 중"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        s_running = true;
        var sb = new StringBuilder("10-11 보스 HUD · 서약 칸 톤 v2 실측\n");
        var restore = new List<(GameObject go, bool active)>();
        var restoreAlpha = new List<(CanvasGroup cg, float alpha)>();
        var hub = Object.FindFirstObjectByType<TestHubLauncher>();
        bool hubWas = hub != null && hub.enabled;
        if (hub != null) hub.enabled = false;   // 허브 OnGUI 판이 사진을 가린다

        var boss     = Object.FindFirstObjectByType<BossPanelView>(FindObjectsInactive.Include);
        var covenant = Object.FindFirstObjectByType<CovenantPanelView>(FindObjectsInactive.Include);
        var notifier = Object.FindFirstObjectByType<QuestNotifierView>(FindObjectsInactive.Include);
        var bark     = UI_BossBark.Instance;
        bool bossWas = boss != null && boss.gameObject.activeSelf;   // ⑤에서 내린다 — 실제 보스전 중이었으면 되살린다
        CovenantBase sample = null;
        try
        {
            if (Managers.DialogueData != null && !Managers.DialogueData.IsInitialized) await Managers.DialogueData.InitializeAsync();
            sb.AppendLine($"톤 v2 글꼴 {(UISkin.ThemeFonts != null ? "올라옴" : "없음(기본 글꼴로 폴백 — 결과가 DNF로 보일 수 있다)")} · 런 {(GameRunBootstrapper.Instance?.Run != null ? "있음" : "없음(허브)")}");
            if (bark == null) sb.AppendLine("UI_BossBark 없음");
            if (boss == null) sb.AppendLine("BossPanelView 없음");
            if (covenant == null) sb.AppendLine("CovenantPanelView 없음");

            // ① 보스바 + 이름 + 짧은 기술 이름(이름 20 < 기술 32 위계가 한 장에)
            if (boss != null)
            {
                ForceVisible(boss.transform, restore, restoreAlpha);
                boss.ResetPageFx();
                boss.Init(42000, "리치");
                boss.SetHP(21000, 42000);
            }
            UI_BossBark.ShowLatest("검의 심판", BossBarkType.PatternAnnounce);
            await Shot("BossHUD_1_Bar_Pattern", 450, boss != null ? boss.transform : null, restore, restoreAlpha);
            LogBossPanel(boss, sb);
            LogBark(bark, "① 기술 이름(짧은 것)", sb);

            // 패턴 예고 문장 전부 — 지금 칸 설정(글꼴 · 크기 · 최대 폭 · 자동 축소) 그대로 줄 수 · 폭 · 넘침
            string longest = MeasurePatternTexts(bark, sb, out DialogueSpeaker longestSpeaker);

            // ② 대사 한 줄(화자 줄 포함)
            UI_BossBark.ShowLatest("늦었군. 결계는 이미 완성됐다. 네 빛은 여기서 꺼진다.", BossBarkType.Bark, DialogueSpeaker.Lich);
            await Shot("BossHUD_2_Bark", 450, boss != null ? boss.transform : null, restore, restoreAlpha);
            LogBark(bark, "② 대사", sb);

            // ③ 가장 긴 패턴 예고(줄바꿈 · 최대 폭 확인)
            UI_BossBark.ShowLatest(longest, BossBarkType.PatternAnnounce, longestSpeaker);
            await Shot("BossHUD_3_PatternLong", 450, boss != null ? boss.transform : null, restore, restoreAlpha);
            LogBark(bark, "③ 기술 이름(가장 긴 문장)", sb);

            // ④ 카운터
            UI_BossBark.ShowLatest("봉인석 점화 (3 / 4)", BossBarkType.PatternAnnounce);
            await Shot("BossHUD_4_Counter", 450, boss != null ? boss.transform : null, restore, restoreAlpha);
            LogBark(bark, "④ 카운터", sb);
            UI_BossBark.Dismiss(0f);

            // ⑤ 서약 칸(표본 서약서) + 퀘스트 완료 알림 — 보스바는 내린다
            if (boss != null) boss.gameObject.SetActive(false);
            if (covenant != null)
            {
                sample = CovenantFactory.Create(SampleSentence);
                ForceVisible(covenant.transform, restore, restoreAlpha);
                covenant.Refresh(sample != null ? new List<CovenantBase> { sample } : new List<CovenantBase>());
                if (sample == null) sb.AppendLine($"표본 서약서 해석 실패 {SampleSentence}");
            }
            if (notifier != null)
            {
                ForceVisible(notifier.transform, restore, restoreAlpha);
                notifier.PlayAsync(null, "첫 번째 시련을 넘어라", "퀘스트 완료", notifier.GetCancellationTokenOnDestroy()).Forget();
            }
            await Shot("BossHUD_5_Covenant", 700, covenant != null ? covenant.transform : null, restore, restoreAlpha);
            LogCovenant(covenant, sb);
            LogNotifier(notifier, sb);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            UI_BossBark.Dismiss(0f);
            if (covenant != null)
            {
                var run = GameRunBootstrapper.Instance?.Run;
                if (run?.CovenantHandler != null) covenant.Refresh(run.CovenantHandler.Covenants);
                else covenant.Clear();
            }
            sample?.Dispose();
            if (boss != null) boss.gameObject.SetActive(bossWas);
            for (int i = restoreAlpha.Count - 1; i >= 0; i--) if (restoreAlpha[i].cg != null) restoreAlpha[i].cg.alpha = restoreAlpha[i].alpha;
            for (int i = restore.Count - 1; i >= 0; i--) if (restore[i].go != null) restore[i].go.SetActive(restore[i].active);
            if (hub != null) hub.enabled = hubWas;
            File.WriteAllText(OutFile, sb.ToString());
            Debug.Log("[BossHudProbe] 끝\n" + sb);
            s_running = false;
        }
    }

    // ── 화면 ─────────────────────────────────────────────

    /// <summary>대상에서 캔버스까지 꺼진 것을 켜고 투명 CanvasGroup을 1로 — 원래 값은 기록해 두고 끝나면 되돌린다(허브에선 @HUD가 꺼져 있다).</summary>
    private static void ForceVisible(Transform t, List<(GameObject, bool)> restore, List<(CanvasGroup, float)> restoreAlpha)
    {
        for (var cur = t; cur != null; cur = cur.parent)
        {
            if (!cur.gameObject.activeSelf)
            {
                if (!restore.Exists(r => r.Item1 == cur.gameObject)) restore.Add((cur.gameObject, false));
                cur.gameObject.SetActive(true);
            }
            if (cur.TryGetComponent<CanvasGroup>(out var cg) && cg.alpha < 0.99f
                && (cur != t || t.GetComponent<QuestNotifierView>() == null))   // 알림은 자기 페이드를 쓴다
            {
                if (!restoreAlpha.Exists(r => r.Item1 == cg)) restoreAlpha.Add((cg, cg.alpha));
                cg.alpha = 1f;
            }
        }
    }

    /// <summary>찍기 직전 한 번 더 켠다 — HUD를 켜는 순간 모드 섹션이 패널을 다시 끌 수 있다.</summary>
    private static async UniTask Shot(string name, int settleMs, Transform keep, List<(GameObject, bool)> restore, List<(CanvasGroup, float)> restoreAlpha)
    {
        await UniTask.Delay(settleMs, ignoreTimeScale: true);
        if (keep != null) ForceVisible(keep, restore, restoreAlpha);
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        if (m != null) await (UniTask)m.Invoke(null, new object[] { name, 0 });
    }

    // ── 측정 ─────────────────────────────────────────────

    /// <summary>패턴 예고 문장 전부를 지금 칸 설정(복제 글 칸)으로 그려 줄 수 · 실제 크기 · 폭 · 넘침을 적는다. 가장 넓은 문장을 돌려준다.</summary>
    private static string MeasurePatternTexts(UI_BossBark bark, StringBuilder sb, out DialogueSpeaker longestSpeaker)
    {
        longestSpeaker = DialogueSpeaker.None;
        var label = bark != null ? typeof(UI_BossBark).GetField("_label", Inst)?.GetValue(bark) as TMP_Text : null;
        string longest = PatternTexts[0].text;
        if (label == null) { sb.AppendLine("패턴 예고 측정 못 함 — 글 칸 없음"); return longest; }

        var items = new List<(string src, string text, DialogueSpeaker sp)>();
        foreach (var (src, text) in PatternTexts) items.Add((src, text, DialogueSpeaker.None));
        var dlg = Managers.DialogueData;
        foreach (var key in PatternKeys)
        {
            var lines = dlg?.GetLines(key);
            if (lines == null || lines.Length == 0) { sb.AppendLine($"  (대사 CSV {key} 없음)"); continue; }
            var l = lines[0];
            if (l == null || string.IsNullOrEmpty(l.text) || l.speaker == DialogueSpeaker.Merlin || l.speaker == DialogueSpeaker.Shadow) continue;
            items.Add(($"CSV {key}[0]", l.text, l.speaker));
        }

        var clone = Object.Instantiate(label.gameObject, label.transform.parent);
        clone.name = "~ProbeMeasure";
        var t = clone.GetComponent<TMP_Text>();
        t.alpha = 0f;
        float best = -1f;
        int over = 0, twoLine = 0;
        sb.AppendLine($"\n── 패턴 예고 문장 {items.Count}개 — 글 칸 {label.rectTransform.rect.width:0}×{label.rectTransform.rect.height:0} · 여백 좌우 {label.margin.x:0} · 크기 {label.fontSizeMax:0.#}(자동 축소 하한 {label.fontSizeMin:0.#}) · 글꼴 {Name(label.font)}");
        foreach (var (src, text, sp) in items)
        {
            t.text = text;
            t.ForceMeshUpdate(true, true);
            float w = t.textBounds.size.x;
            int lines = t.textInfo.lineCount;
            if (lines >= 2) twoLine++;
            if (t.isTextOverflowing) over++;
            sb.AppendLine($"  {lines}줄 · 크기 {t.fontSize,4:0.#} · 폭 {w,5:0} · {(t.isTextOverflowing ? "넘침 " : "")}{src} 「{text}」");
            if (w * lines > best) { best = w * lines; longest = text; longestSpeaker = sp; }
        }
        Object.Destroy(clone);
        sb.AppendLine($"  → 두 줄 이상 {twoLine}개 · 넘침 {over}개 · 가장 긴 문장 「{longest}」(③ 사진)");
        return longest;
    }

    private static void LogBark(UI_BossBark bark, string tag, StringBuilder sb)
    {
        if (bark == null) return;
        var label   = typeof(UI_BossBark).GetField("_label", Inst)?.GetValue(bark) as TMP_Text;
        var speaker = typeof(UI_BossBark).GetField("_speakerLabel", Inst)?.GetValue(bark) as TMP_Text;
        var divider = typeof(UI_BossBark).GetField("_speakerDivider", Inst)?.GetValue(bark) as UnityEngine.UI.Image;
        sb.AppendLine($"\n{tag}: {Text(label)}");
        if (label != null) sb.AppendLine($"   줄 {label.textInfo.lineCount} · 그린 폭 {label.textBounds.size.x:0} · 넘침 {label.isTextOverflowing} · 위치 y {label.rectTransform.parent.GetComponent<RectTransform>().anchoredPosition.y:0}");
        if (speaker != null && speaker.gameObject.activeInHierarchy) sb.AppendLine($"   화자 줄 {Text(speaker)} · 밑줄 두께 {(divider != null ? divider.rectTransform.sizeDelta.y : -1f):0.#}");
    }

    private static void LogBossPanel(BossPanelView boss, StringBuilder sb)
    {
        if (boss == null) return;
        var name = typeof(BossPanelView).GetField("nameText", Inst)?.GetValue(boss) as TMP_Text;
        var hp   = typeof(BossPanelView).GetField("hpText", Inst)?.GetValue(boss) as TMP_Text;
        var fill = typeof(BossPanelView).GetField("hpFillImage", Inst)?.GetValue(boss) as UnityEngine.UI.Image;
        sb.AppendLine($"\n보스 이름: {Text(name)}");
        sb.AppendLine($"보스 수치: {Text(hp)}");
        if (fill != null)
        {
            var c = new Vector3[4];
            ((RectTransform)fill.transform.parent).GetWorldCorners(c);
            sb.AppendLine($"보스바 창(화면 px): 폭 {c[2].x - c[0].x:0} · 두께 {c[1].y - c[0].y:0.#} · 화면 {Screen.width}×{Screen.height}");
        }
    }

    private static void LogCovenant(CovenantPanelView panel, StringBuilder sb)
    {
        if (panel == null) return;
        var slots = typeof(CovenantPanelView).GetField("_slots", Inst)?.GetValue(panel) as UI_CovenantSlot[];
        var count = typeof(CovenantPanelView).GetField("_countLabel", Inst)?.GetValue(panel) as TMP_Text;
        sb.AppendLine($"\n서약 칸 수: {Text(count)}");
        if (slots == null || slots.Length == 0 || slots[0] == null) { sb.AppendLine("서약 칸 없음"); return; }
        var desc = typeof(UI_CovenantSlot).GetField("_descText", Inst)?.GetValue(slots[0]) as TMP_Text;
        sb.AppendLine($"서약 제목: {Text(slots[0].NameText)}");
        sb.AppendLine($"서약 설명: {Text(desc)} · 줄 {(desc != null ? desc.textInfo.lineCount : 0)} · 넘침 {(desc != null && desc.isTextOverflowing)}");
        var rt = (RectTransform)slots[0].transform;
        sb.AppendLine($"서약 칸 크기 {rt.rect.width:0}×{rt.rect.height:0}");
    }

    private static void LogNotifier(QuestNotifierView view, StringBuilder sb)
    {
        if (view == null) { sb.AppendLine("\n퀘스트 알림 없음"); return; }
        var title = typeof(QuestNotifierView).GetField("titleText", Inst)?.GetValue(view) as TMP_Text;
        var label = typeof(QuestNotifierView).GetField("labelText", Inst)?.GetValue(view) as TMP_Text;
        var edge  = view.transform.Find("Background/ThinEdge");
        sb.AppendLine($"\n퀘스트 알림 제목: {Text(title)}");
        sb.AppendLine($"퀘스트 알림 줄: {Text(label)}");
        sb.AppendLine($"퀘스트 알림 테: {(edge != null ? "1px 금 30% 있음" : "없음")}");
    }

    private static string Text(TMP_Text t)
        => t == null ? "없음"
         : $"글꼴 {Name(t.font)} · 크기 {t.fontSize:0.#} · 스타일 {t.fontStyle} · 색 #{ColorUtility.ToHtmlStringRGBA(t.color)} · 「{t.text}」";

    private static string Name(TMP_FontAsset f) => f != null ? f.name : "없음";
}
#endif
