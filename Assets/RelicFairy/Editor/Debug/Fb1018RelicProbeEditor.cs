#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-18 빌드 피드백 ③ 「유물 진화의 설명이 거의 알아듣기 힘든 내용이야 — 제대로 플레이어가 이해할 수 있게 직관적으로 가이드가 필요해」 반입 실측.
/// 테스트 허브 Ch1 런에서 「되살아난 기억」 창을 보스 처치 없이 손으로 연다(카드를 정해 Setup).
/// <list type="number">
/// <item>가웨인 처음 안내 — 이미 본 세이브면 안내를 강제로 띄워 찍는다(기록은 건드리지 않는다).</item>
/// <item>가웨인 · 사용자 사진과 같은 세 장(과열 흐릿 · 여명의 맹세 선명 · 새벽 파종 선명 · 조각 0).</item>
/// <item>가웨인 · 새벽 파종을 가진 뒤 — 더 선명하게 · 아침 사냥(여명 2/2 바로 켜짐) · 메아리(정오).</item>
/// <item>랜슬롯 처음 안내 · 세 장(광기 10 조각 하나를 가진 뒤 — 광기 20 · 전이 · 찢는 심판).</item>
/// <item>랜슬롯 넷(진화 카드 덧붙임 — 카드 폭 276에서 등급 라벨 · 요약이 겹치지 않는지).</item>
/// </list>
/// 카드마다 등급 · 종류 · 요약 · ① · ②③ · 꼬리말과 용어 줄 · 안내 글을 적고 CHECK(요약 있음 · 「효과 n줄」 · 옛 「공명 0 → 1」 없음 ·
/// 글자 넘침 없음 · 요약이 이름 · 효과 상자와 안 겹침 · 등급 라벨이 종류 글자와 안 겹침).
/// 결과 Temp/fb1018_relic.txt · Temp/ui_shots/FB18_relic_*.png · 로그 「[FB18relic] 끝」. 끝나면 유물을 원래대로 돌린다(조각은 비워진다 — 실측 전용).
/// </summary>
public static class Fb1018RelicProbeEditor
{
    private const string Tag     = "[FB18relic]";
    private const string OutTxt  = "Temp/fb1018_relic.txt";
    private const string ShotDir = "Temp/ui_shots";
    private const string GawainAsset   = "Assets/RelicFairy/Systems/Relic/Data/RelicClass_Gawain.asset";
    private const string LancelotAsset = "Assets/RelicFairy/Systems/Relic/Data/RelicClass_Lancelot.asset";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-18 되살아난 기억 안내 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중(테스트 허브)에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-18 되살아난 기억 안내 실측\n");
        Directory.CreateDirectory(ShotDir);
        PlayerLoadout lo = null;
        RelicClassSO original = null;
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
            lo = AppBootstrapper.Instance?.Loadout;
            if (run == null || lo == null || Managers.RelicParts == null) { sb.AppendLine("런이 시작되지 않음"); return; }
            await UniTask.Delay(3000, DelayType.Realtime);
            run.Player?.SetInvincible(120f);
            original = lo.Relic;

            var gawain   = AssetDatabase.LoadAssetAtPath<RelicClassSO>(GawainAsset);
            var lancelot = AssetDatabase.LoadAssetAtPath<RelicClassSO>(LancelotAsset);
            sb.AppendLine($"유물 — 가웨인 {(gawain != null ? gawain.DisplayName : "(없음)")} · 랜슬롯 {(lancelot != null ? lancelot.DisplayName : "(없음)")} · 원래 {(original != null ? original.DisplayName : "-")}");
            // 10-22 E1 — 안내 기록이 유물마다(seen.relic_draft_guide.<유물>). 실측 동안은 세이브 대신 임시 집합에 적는다(진짜 세이브를 건드리지 않게)
            var data = BackendGameData.Instance?.Data;
            sb.AppendLine($"처음 안내 기록(실측 전) {RelicFirstTips.GuidePrefix}gawain = {data?.GetRecord(RelicFirstTips.GuidePrefix + "gawain") ?? -1} · " +
                          $"lancelot = {data?.GetRecord(RelicFirstTips.GuidePrefix + "lancelot") ?? -1}");
            RelicFirstTips.ProbeRecords = new System.Collections.Generic.HashSet<string>();
            if (gawain == null || lancelot == null) return;

            // ── 가웨인 ──
            UseRelic(lo, gawain);
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RelicPartDraftPopup>();
            if (popup == null) { sb.AppendLine("창을 못 열었다"); return; }
            Setup(popup, "gawain", gawain, Card(sb, "g_rx_overheat", RelicMemoryGrade.Faint), Card(sb, "g_dawn_oath", RelicMemoryGrade.Clear), Card(sb, "g_dawn_sowing", RelicMemoryGrade.Clear));
            sb.AppendLine($"안내 자동으로 뜸 = {GetField(popup, "_guideRoot") as GameObject != null}");
            await GuideShotAsync(sb, popup, "gawain", gawain, "FB18_relic_1_guide_gawain", "0/2", "여명엔 맞힌 적에게 태양흔이 쌓이고");   // 10-23 U7 — 옛 「여명(쳐서 쌓기)」
            await Wait(0.5f);
            await ShotAsync("FB18_relic_2_gawain_photo");
            Audit(sb, popup, "② 가웨인 · 사진과 같은 세 장(조각 0)");
            Check(sb, "② 과열 꼬리말 = 룬 조건(「전기 룬과 함께」 · 10-22 E1 B5)", FootOf(popup, 0).Contains("전기 룬과 함께"));
            Check(sb, "② 여명의 맹세 꼬리말 = 「여명 조각 1/2」(단계가 안 바뀌면 개수만 · 10-22 E1 B5)", FootOf(popup, 1).Contains("여명 조각 1/2"));
            Check(sb, "② 용어 줄에 태양흔 · 개화", GlossaryOf(popup).Contains("태양흔") && GlossaryOf(popup).Contains("개화"));

            lo.AddRelicPart("g_dawn_sowing", RelicMemoryGrade.Faint);
            Setup(popup, "gawain", gawain, Sharpen(sb, "g_dawn_sowing"), Card(sb, "g_morning_hunt", RelicMemoryGrade.Radiant), Echo(RelicPartAnchor.Noon));
            await Wait(0.6f);
            await ShotAsync("FB18_relic_3_gawain_progress");
            Audit(sb, popup, "③ 가웨인 · 새벽 파종을 가진 뒤");
            Check(sb, "③ 더 선명하게 꼬리말 = 「효과 1줄 → 2줄」", FootOf(popup, 0).Contains("효과 1줄 → 2줄"));
            Check(sb, "③ 아침 사냥 꼬리말 = 「여명 2개 → …」(단계가 바뀌는 카드만 그 단계 한 줄)", FootOf(popup, 1).Contains("여명 2개 →"));
            Check(sb, "③ 메아리 꼬리말 = 「정오 조각 1/2」", FootOf(popup, 2).Contains("정오 조각 1/2"));
            FrameAudit(sb, popup, "③", null, null);

            // ③b 가웨인 진화 창(10-18 창 2장 = 잇기 + 진화 · 힉스필드 진화 틀 · 해 문장)
            var gEvo = new RelicDraftCard { Kind = RelicDraftCardKind.Evolution, EvolutionId = "evo_gawain_combo", Grade = RelicMemoryGrade.Radiant };
            Setup(popup, "gawain", gawain, Card(sb, "g_morning_hunt", RelicMemoryGrade.Radiant), gEvo);
            await Wait(1.2f);
            await ShotAsync("FB18_relic_3b_gawain_evo4");
            Audit(sb, popup, "③b 가웨인 둘(진화 카드 — 카드 폭 340)");
            FrameAudit(sb, popup, "③b", "Frame_EvolutionCard_Gawain", "Crest_EvolutionCard_Gawain");
            Managers.UI.CloseAllPopupUI();
            Time.timeScale = 1f;
            await Wait(0.6f);

            // ── 랜슬롯 ──
            UseRelic(lo, lancelot);
            lo.AddRelicPart("l_split_oath", RelicMemoryGrade.Faint);
            popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RelicPartDraftPopup>();
            if (popup == null) { sb.AppendLine("랜슬롯 창을 못 열었다"); return; }
            Setup(popup, "lancelot", lancelot, Card(sb, "l_black_afterimage", RelicMemoryGrade.Clear), Card(sb, "l_rx_transfer", RelicMemoryGrade.Faint), Card(sb, "l_tearing_judgment", RelicMemoryGrade.Radiant));
            await GuideShotAsync(sb, popup, "lancelot", lancelot, "FB18_relic_4_guide_lancelot", "1/2", "광기 10 · 20 · 30 · 40");
            await Wait(0.5f);
            await ShotAsync("FB18_relic_5_lancelot");
            Audit(sb, popup, "⑤ 랜슬롯 · 광기 10 조각을 가진 뒤");
            Check(sb, "⑤ 검은 잔상 꼬리말 = 「광기 10~40 조각 2개 → …」(10-22 E1 D7 · 10-23 U7 이름)", FootOf(popup, 0).Contains("광기 10~40 조각 2개 →"));
            Check(sb, "⑤ 찢는 심판 꼬리말 = 「광기 10~40 조각 수에는 안 센다」", FootOf(popup, 2).Contains("광기 10~40 조각 수에는 안 센다"));
            Check(sb, "⑤ 용어 줄에 광기 · 낙인", GlossaryOf(popup).Contains("광기") && GlossaryOf(popup).Contains("낙인"));

            var evoCard = new RelicDraftCard { Kind = RelicDraftCardKind.Evolution, EvolutionId = "evo_lancelot_crit", Grade = RelicMemoryGrade.Radiant };
            Setup(popup, "lancelot", lancelot, Card(sb, "l_black_afterimage", RelicMemoryGrade.Clear), evoCard);   // 10-18 창 2장 = 잇기 + 진화
            await Wait(1.2f);   // 진화 금 고리 · 겹선이 그려진다(0.15 + 0.5 실시간)
            await ShotAsync("FB18_relic_6_lancelot_evo4");
            Audit(sb, popup, "⑥ 랜슬롯 둘(진화 카드 — 카드 폭 340)");
            FrameAudit(sb, popup, "⑥", "Frame_EvolutionCard_Lancelot", "Crest_EvolutionCard_Lancelot");
            Managers.UI.CloseAllPopupUI();
            Time.timeScale = 1f;
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            RelicFirstTips.ProbeRecords = null;
            if (lo != null && original != null) UseRelic(lo, original);
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    // ── 창 다루기 ──

    private static void UseRelic(PlayerLoadout lo, RelicClassSO relic)
    {
        lo.SetRelic(relic);
        typeof(PlayerLoadout).GetMethod("ClearRelicMemory", Any)?.Invoke(lo, null);   // 같은 유물이면 SetRelic이 안 비운다
    }

    private static void Setup(UI_RelicPartDraftPopup popup, string relicId, RelicClassSO relic, params RelicDraftCard[] cards)
    {
        var draft = new RelicDraft { RelicId = relicId };
        foreach (var c in cards) if (c != null) draft.Cards.Add(c);
        popup.Setup(draft, false, relic.DisplayName);
    }

    /// <summary>안내가 안 떠 있으면(이미 본 세이브) 강제로 띄워 찍고 글을 적은 뒤 닫는다.</summary>
    private static async UniTask GuideShotAsync(StringBuilder sb, UI_RelicPartDraftPopup popup, string relicId, RelicClassSO relic,
                                                string shot, string partsNeedle, string ruleNeedle)
    {
        if (GetField(popup, "_guideRoot") as GameObject == null)
            typeof(UI_RelicPartDraftPopup).GetMethod("ShowGuide", Any)?.Invoke(popup, new object[] { relicId, relic.DisplayName });
        await Wait(0.8f);
        await ShotAsync(shot);
        var root = GetField(popup, "_guideRoot") as GameObject;
        var body = Find(root != null ? root.transform : null, "GuideBody");
        string text = Parsed(body);
        sb.AppendLine($"[{shot}] 안내 판 {(root != null ? "있음" : "없음")} · 글 {body?.fontSize:0.#}px\n{text}");
        // 10-22 E1 — 봉인기 세 줄(진화 없음) · 해방기부터 네 줄(「지금 n/2」) · 옛 「두 번째 바퀴」 약속 없음
        bool evoOk = RelicEvolution.Unlocked ? text.Contains("지금 " + partsNeedle) : !text.Contains("진화");
        Check(sb, $"{shot} 안내(조각 · 등급 · 모으기{(RelicEvolution.Unlocked ? " · 진화 「지금 " + partsNeedle + "」" : " — 봉인기라 진화 없음")}) · 「흐릿 1줄 · 선명 2줄 · 찬란 3줄」 · 「{ruleNeedle}」 · 「두 번째 바퀴」 없음",
              text.Contains("카드 한 장 = 조각 하나") && text.Contains("흐릿 1줄 · 선명 2줄 · 찬란 3줄") && text.Contains(ruleNeedle)
              && evoOk && !text.Contains("두 번째 바퀴") && !text.Contains("각인"));
        Check(sb, $"{shot} 안내 글 넘침 없음", body != null && !body.isTextOverflowing);
        typeof(UI_RelicPartDraftPopup).GetMethod("CloseGuide", Any)?.Invoke(popup, null);
    }

    // ── 카드 ──

    private static RelicDraftCard Card(StringBuilder sb, string id, RelicMemoryGrade grade)
    {
        var e = Managers.RelicParts.GetById(id);
        if (e == null) { sb.AppendLine($"  ! 조각 데이터 없음: {id}"); return null; }
        return new RelicDraftCard { Kind = RelicDraftCardKind.NewFragment, Entry = e, Anchor = e.anchor, Grade = grade };
    }

    private static RelicDraftCard Sharpen(StringBuilder sb, string id)
    {
        var c = Card(sb, id, RelicMemoryGrade.Faint);
        if (c == null) return null;
        c.Kind  = RelicDraftCardKind.Sharpen;
        c.Grade = AppBootstrapper.Instance.Loadout.GetRelicPartGrade(id) + 1;
        return c;
    }

    private static RelicDraftCard Echo(string anchor)
        => new RelicDraftCard { Kind = RelicDraftCardKind.Echo, Anchor = anchor, Grade = RelicMemoryGrade.Faint };

    // ── 검사 ──

    private static void Audit(StringBuilder sb, UI_RelicPartDraftPopup popup, string label)
    {
        sb.AppendLine($"[{label}]");
        var cards = GetField(popup, "_cards") as IList;
        int n = cards?.Count ?? 0;
        for (int i = 0; i < n; i++)
        {
            var rt = cards[i]?.GetType().GetField("Rt")?.GetValue(cards[i]) as RectTransform;
            if (rt == null) continue;
            var grade = Find(rt, "Grade"); var kind = Find(rt, "Kind"); var name = Find(rt, "Name"); var sum = Find(rt, "Summary");
            var head = Find(rt, "Text");   var rest = Find(rt, "Rest"); var foot = Find(rt, "Foot");
            sb.AppendLine($"  카드 {i}: 등급 「{Parsed(grade)}」({grade?.fontSize:0.#}px) · 종류 「{Parsed(kind)}」 · 이름 「{Parsed(name)}」");
            sb.AppendLine($"    요약 「{Parsed(sum)}」({sum?.fontSize:0.#}px)");
            sb.AppendLine($"    본문 「{OneLine(head)}」({head?.fontSize:0.#}px)");
            if (rest != null) sb.AppendLine($"    ②③ 「{OneLine(rest)}」({rest.fontSize:0.#}px)");
            if (foot != null) sb.AppendLine($"    꼬리말 「{OneLine(foot)}」({foot.fontSize:0.#}px)");

            Check(sb, $"  카드 {i} 요약 있음", !string.IsNullOrEmpty(Parsed(sum)));
            bool graded = grade != null && (Parsed(grade).Contains("기억") || Parsed(grade).Contains("하게"));
            if (graded) Check(sb, $"  카드 {i} 등급 라벨에 「효과 n줄」", Parsed(grade).Contains("효과") && Parsed(grade).Contains("줄"));
            Check(sb, $"  카드 {i} 옛 「공명 n → n」 · 「단계)」 꼬리말 없음", foot == null || (!Parsed(foot).Contains("공명 ") && !Parsed(foot).Contains("에서 단계")));
            Check(sb, $"  카드 {i} 글자 넘침 없음(등급 · 요약 · 본문 · ②③ · 꼬리말)",
                  NotOver(grade) && NotOver(sum) && NotOver(head) && NotOver(rest) && NotOver(foot));
            Check(sb, $"  카드 {i} 요약이 이름 아래 · 효과 상자 위(겹침 없음)",
                  sum != null && name != null && head != null
                  && Bottom(name) >= Top(sum) - 0.5f && Bottom(sum) >= Top(head.transform.parent as RectTransform) - 0.5f);
            if (grade != null && kind != null)
            {
                float gradeRight = grade.rectTransform.TransformPoint(new Vector3(grade.rectTransform.rect.xMax, 0f, 0f)).x;
                float kindLeft   = kind.rectTransform.TransformPoint(new Vector3(kind.textBounds.min.x, 0f, 0f)).x;
                Check(sb, $"  카드 {i} 등급 라벨이 종류 글자와 안 겹침", gradeRight <= kindLeft + 0.5f);
            }
        }
        var gl = GetField(popup, "_glossaryText") as TMP_Text;
        sb.AppendLine($"  용어 줄 「{Parsed(gl)}」({gl?.fontSize:0.#}px · {(gl != null && gl.gameObject.activeSelf ? "보임" : "숨김")})");
        Check(sb, "  용어 줄 보임 · 넘침 없음", gl != null && gl.gameObject.activeSelf && NotOver(gl));
    }

    private static string FootOf(UI_RelicPartDraftPopup popup, int index)
    {
        var cards = GetField(popup, "_cards") as IList;
        if (cards == null || index >= cards.Count) return string.Empty;
        var rt = cards[index]?.GetType().GetField("Rt")?.GetValue(cards[index]) as RectTransform;
        return Parsed(Find(rt, "Foot"));
    }

    private static string GlossaryOf(UI_RelicPartDraftPopup popup) => Parsed(GetField(popup, "_glossaryText") as TMP_Text);

    private static bool NotOver(TMP_Text t)
    {
        if (t == null) return true;
        t.ForceMeshUpdate();
        return !t.isTextOverflowing;
    }

    private static float Top(RectTransform rt)    { var c = new Vector3[4]; rt.GetWorldCorners(c); return c[1].y; }
    private static float Bottom(RectTransform rt) { var c = new Vector3[4]; rt.GetWorldCorners(c); return c[0].y; }
    private static float Top(TMP_Text t)    => Top(t.rectTransform);
    private static float Bottom(TMP_Text t) => Bottom(t.rectTransform);

    private static void Check(StringBuilder sb, string what, bool ok) => sb.AppendLine($"CHECK {ok} — {what}");

    /// <summary>10-18 힉스필드 틀 — 기억 카드 = 기억 틀 · 진화 카드 = 그 유물의 진화 틀 + 위 문장(진화 카드가 없으면 기억 틀만 본다).</summary>
    private static void FrameAudit(StringBuilder sb, UI_RelicPartDraftPopup popup, string title, string evoFrame, string crest)
    {
        if (GetField(popup, "_cards") is not IList list) { sb.AppendLine($"{title} 틀 — 카드 목록 없음"); return; }
        var names = new System.Collections.Generic.List<string>();
        int memory = 0, evo = 0;
        bool crestOk = crest == null;
        foreach (var v in list)
        {
            var fill = GetField(v, "Fill") as UnityEngine.UI.Image;
            var rt   = GetField(v, "Rt") as RectTransform;
            string n = fill != null && fill.sprite != null ? fill.sprite.name : "(없음)";
            names.Add(n);
            if (n == "Frame_RelicMemoryCard") memory++;
            if (evoFrame != null && n == evoFrame) evo++;
            var ct = rt != null ? rt.Find("EvolutionCrest") : null;
            if (crest != null && ct != null && ct.TryGetComponent<UnityEngine.UI.Image>(out var ci) && ci.sprite != null && ci.sprite.name == crest) crestOk = true;
        }
        sb.AppendLine($"{title} 틀 — {string.Join(" · ", names)}");
        int wantEvo = evoFrame != null ? 1 : 0;
        Check(sb, $"{title} 틀 — 기억 카드 {list.Count - wantEvo}장 = 기억 틀" + (evoFrame != null ? $" · 진화 카드 = {evoFrame} + {crest}" : ""),
              memory == list.Count - wantEvo && evo == wantEvo && crestOk);
    }

    // ── 작은 도우미 ──

    private static object GetField(object o, string field) => o?.GetType().GetField(field, Any)?.GetValue(o);

    private static TMP_Text Find(Transform root, string name)
    {
        if (root == null) return null;
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            if (t.name == name) return t;
        return null;
    }

    private static string Parsed(TMP_Text t)
    {
        if (t == null) return string.Empty;
        t.ForceMeshUpdate();
        return t.GetParsedText();
    }

    private static string OneLine(TMP_Text t) => Parsed(t).Replace("\n", " / ");

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
