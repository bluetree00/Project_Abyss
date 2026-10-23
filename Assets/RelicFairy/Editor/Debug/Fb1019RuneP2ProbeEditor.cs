#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-19 룬 획득 팝업 분석 P2 반입 실측(테스트 허브 · 플레이 중).
/// <list type="number">
/// <item>③ 카드의 「룬 n + 1 / 20」 없음(넘치지 않을 때) · 카운터에 「배치」 없음.</item>
/// <item>④ 효과 수치 원색 금(#FFD54A) 없음.</item>
/// <item>⑤ 효과 글자 크기 세 장 하나 · 효과 칸 높이 세 장 같음.</item>
/// <item>⑥ 계열 · 발동 칩 테 없음(바깥 투명 · 마름모 점).</item>
/// <item>⑦ 보관함 가득 — 카운터 「가득 · 넘침 칸」(보관함 목록에 잠깐 넣었다 되돌린다 · 저장 없음).</item>
/// <item>⑧ 챌린지 2/3 — 제목 「보상 2/3」 · 보통 창은 없음.</item>
/// </list>
/// 결과 Temp/fb1019_rune.txt · Temp/ui_shots/FB19_rune_*.png · 로그 「[FB19rune] 끝」. 고르지 않고 닫는다.
/// </summary>
public static class Fb1019RuneP2ProbeEditor
{
    private const string Tag     = "[FB19rune]";
    private const string OutTxt  = "Temp/fb1019_rune.txt";
    private const string ShotDir = "Temp/ui_shots";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-19 룬 획득 팝업 P2 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중(테스트 허브)에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-19 룬 획득 팝업 P2 실측\n");
        Directory.CreateDirectory(ShotDir);
        List<RuntimeItemData> staging = null;
        int stagedBefore = -1;
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
            var cands = Candidates();
            sb.AppendLine($"후보 {cands.Count}장: {string.Join(" · ", cands.ConvertAll(c => $"{c.Item1.displayName}(효과 {c.Item1.effects?.Count ?? 0})"))}");
            if (cands.Count == 0) return;

            // ③ ~ ⑥ 보통 창
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
            popup.Setup(new List<(RuntimeItemData, ItemSO)>(cands), inv);
            await Wait(2.4f);
            int badges = 0;
            foreach (var t in popup.GetComponentsInChildren<TMP_Text>(true)) if (t.name == "PlaceBadge" && t.gameObject.activeInHierarchy) badges++;
            string counter = Text(popup, "_counterText");
            Check(sb, $"③ 카드 룬 수 줄 {badges}개(넘침 없음 = 0) · 카운터 「{counter}」에 「배치」 없음", badges == 0 && !counter.Contains("배치"));
            var winRT = Field<Transform>(popup, "_windowRoot")?.parent as RectTransform;
            var card0 = Field<Transform>(popup, "_windowRoot")?.Find("CardLayer/Card0") as RectTransform;
            Check(sb, $"창 — {winRT?.rect.width:0}×{winRT?.rect.height:0}(화면 1920×1080의 {winRT?.rect.width / 19.2f:0}% × {winRT?.rect.height / 10.8f:0}%) · 3장 카드 폭 {card0?.rect.width:0} · 높이 {card0?.rect.height:0}",
                  winRT != null && winRT.rect.width > 1450f && winRT.rect.width < 1700f && card0 != null && card0.rect.width >= 265f && card0.rect.height >= 479f);
            s_cardW3 = card0 != null ? card0.rect.width : -1f;
            int maxOre = 1;
            foreach (var c in cands) maxOre = Mathf.Max(maxOre, RuneSalvage.OreValueOf(c.Item1));
            string hint0 = HintOf(popup);
            Check(sb, $"넘기기 = 가장 높은 분해 값 — 「{hint0}」 · SkipOre {popup.SkipOre} = {maxOre}", popup.SkipOre == maxOre && hint0.Contains($"+{maxOre}"));
            var winFill = Field<Transform>(popup, "_windowRoot")?.GetComponent<Image>();
            Check(sb, $"틀 = 힉스필드 창 틀 9칸 — 「{(winFill != null && winFill.sprite != null ? winFill.sprite.name : "없음")}」 · {winFill?.type}",
                  winFill != null && winFill.sprite != null && winFill.sprite.name.Contains("RuneSelectWindow") && winFill.type == Image.Type.Sliced);

            var sizes = new HashSet<float>();
            bool gold = false;
            foreach (var row in popup.GetComponentsInChildren<EffectRowWidget>(true))
            {
                if (row.Label == null) continue;
                sizes.Add(Mathf.Round(row.Label.fontSize * 10f) / 10f);
                if (row.Label.text.Contains("FFD54A")) gold = true;
            }
            Check(sb, "④ 효과 수치 원색 금(#FFD54A) 없음", !gold);
            Check(sb, $"⑤ 효과 글자 크기 세 장 하나 — {string.Join(" · ", sizes)}", sizes.Count == 1);
            var heights = new List<float>();
            foreach (var rt in popup.GetComponentsInChildren<RectTransform>(true)) if (rt.name == "EffectBox") heights.Add(Mathf.Round(rt.sizeDelta.y));
            bool sameH = heights.Count > 0 && heights.TrueForAll(h => Mathf.Abs(h - heights[0]) < 0.5f);
            Check(sb, $"⑤ 효과 칸 높이 세 장 같음 — {string.Join(" · ", heights)}", sameH);

            int chips = 0, framed = 0;
            foreach (var rt in popup.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt.name != "FamilyChip" && rt.name != "ProcChip") continue;
                chips++;
                var outer = rt.GetComponent<Image>();
                var fill  = rt.Find("Fill")?.GetComponent<Image>();
                if ((outer != null && outer.color.a > 0.01f) || (fill != null && fill.color.a > 0.01f) || rt.Find("Dot") == null) framed++;
            }
            Check(sb, $"⑥ 계열 · 발동 칩 {chips}개 — 테 · 채움 남은 것 {framed}", chips > 0 && framed == 0);
            await ShotAsync("FB19_rune_1_cards");
            Managers.UI.CloseAllPopupUI();
            Time.timeScale = 1f;
            await Wait(0.5f);

            // ⑦ 보관함 가득 — 목록에 잠깐 넣는다(저장 · 이벤트 없음) · 끝에 되돌린다
            staging = typeof(RunItemInventory).GetField("_stagingItems", Any)?.GetValue(inv) as List<RuntimeItemData>;
            if (staging != null)
            {
                stagedBefore = staging.Count;
                while (staging.Count < RunItemInventory.StagingCapacity) staging.Add(cands[0].Item1);
                popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
                popup.Setup(new List<(RuntimeItemData, ItemSO)>(cands), inv);
                await Wait(2.4f);
                var ct = Field<TMP_Text>(popup, "_counterText");
                counter = Text(popup, "_counterText");
                string confirm = Field<TMP_Text>(popup, "_confirmLabel")?.GetParsedText() ?? "";
                string skipLbl = SkipLabelOf(popup), hint = HintOf(popup);
                Check(sb, $"⑦ 보관함 가득 — 카운터 「{counter}」(붉은색 아님) · 버튼 「{confirm}」 / 「{skipLbl}」 · 「{hint}」 · 넘침 없음 {ct != null && !ct.isTextOverflowing}",
                      counter.Contains("가득") && ct != null && !ct.text.Contains("FF7A6A") && !ct.isTextOverflowing
                      && confirm == "룬판에서 바꾸기" && skipLbl == "분해해서 받기" && hint.Contains("원석 +"));
                await ShotAsync("FB19_rune_2_full");
                Managers.UI.CloseAllPopupUI();
                Time.timeScale = 1f;
                staging.RemoveRange(stagedBefore, staging.Count - stagedBefore);
                stagedBefore = -1;
                await Wait(0.5f);
            }
            else sb.AppendLine("⑦ 보관함 목록을 못 찾음");

            // ⑨ 등급 카드 틀 — 일반 · 희귀 · 영웅 · 전설 한 장씩(있는 만큼)
            var byRarity = RarityCandidates();
            popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
            popup.Setup(byRarity, inv);
            // 공개 연출을 0.15초마다 — 등급별 강조(희귀 틀 선 · 영웅 떨림 · 보석 빛 · 불티 · 전설)를 사진으로 본다
            for (int k = 0; k < 16; k++) { await Wait(0.15f); await ShotAsync($"FB19_rune_5_reveal_{k:00}"); }
            await Wait(0.8f);
            int framed4 = 0, gems = 0;
            var names = new List<string>();
            foreach (var img in popup.GetComponentsInChildren<Image>(true))
            {
                if (img.sprite != null && img.sprite.name.StartsWith("Frame_RuneCard_")) { framed4++; names.Add(img.sprite.name.Substring(15)); }
                if (img.name.StartsWith("Gem_") || img.name.StartsWith("RarityBar")) gems++;
            }
            Check(sb, $"⑨ 등급 카드 틀 {framed4}/{byRarity.Count}장({string.Join(" · ", names)}) · 옛 등급 선 · 보석 조각 {gems}개",
                  framed4 == byRarity.Count && gems == 0);
            Check(sb, $"⑤b 효과 줄 겹침 · 칸 밖 — {RowsReport(popup, out bool rowsOk)}", rowsOk);
            bool wordsOk = true;
            foreach (var t in popup.GetComponentsInChildren<TMP_Text>(true))
                if (t.name == "RarityWord" && t.gameObject.activeInHierarchy && t.isTextTruncated) wordsOk = false;
            Check(sb, "4장 — 등급 글자가 잘리지 않는다(칩에 안 가림)", wordsOk);
            await ShotAsync("FB19_rune_4_rarity");
            Managers.UI.CloseAllPopupUI();
            Time.timeScale = 1f;
            await Wait(0.5f);

            // ⑧ 챌린지 2/3 · 보통 창
            popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
            popup.Setup(new List<(RuntimeItemData, ItemSO)>(cands), inv, -1, 2, 3);
            await Wait(2.4f);
            string title = TitleOf(popup);
            Check(sb, $"⑧ 챌린지 제목 「{title}」에 「보상 2/3」", title.Contains("보상 2/3"));
            await ShotAsync("FB19_rune_3_round");
            Managers.UI.CloseAllPopupUI();
            Time.timeScale = 1f;
            await Wait(0.5f);
            popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
            popup.Setup(new List<(RuntimeItemData, ItemSO)>(cands), inv);
            await Wait(0.6f);
            title = TitleOf(popup);
            Check(sb, $"⑧ 보통 창 제목 「{title}」에 「보상」 없음(다시 쓴 창이 지난 라운드 글을 남기지 않음)", !title.Contains("보상"));
            Managers.UI.CloseAllPopupUI();
            Time.timeScale = 1f;
            await Wait(0.5f);

            // 2장(고행자의 인장 · 정제소 두 장) — 카드가 옆으로 늘어나지 않고 3장 폭 그대로 가운데
            popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
            popup.Setup(new List<(RuntimeItemData, ItemSO)>(cands.GetRange(0, 2)), inv);
            await Wait(2.0f);
            var c2 = Field<Transform>(popup, "_windowRoot")?.Find("CardLayer/Card0") as RectTransform;
            Check(sb, $"2장 — 카드 폭 {c2?.rect.width:0} = 3장 폭 {s_cardW3:0}(늘어나지 않음)", c2 != null && Mathf.Abs(c2.rect.width - s_cardW3) < 1f);
            await ShotAsync("FB19_rune_6_two");
            Managers.UI.CloseAllPopupUI();
            Time.timeScale = 1f;
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (staging != null && stagedBefore >= 0 && staging.Count > stagedBefore) staging.RemoveRange(stagedBefore, staging.Count - stagedBefore);
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    /// <summary>등급마다 하나(일반 → 전설 · 없는 등급은 건너뜀).</summary>
    private static List<(RuntimeItemData, ItemSO)> RarityCandidates()
    {
        var pick = new List<(RuntimeItemData, ItemSO)>(4);
        for (var r = ItemRarity.Rare; r <= ItemRarity.Legendary; r++)   // 3장 고정 — 희귀 · 영웅 · 전설(일반은 첫 창에 있다)
            foreach (var kv in Managers.ItemData.GetAllItems())
            {
                var d = RuntimeItemData.FromServer(kv.Value);
                if (d == null || d.rarity != r || d.effects == null || d.effects.Count == 0) continue;
                pick.Add((d, ItemSORegistry.Find(kv.Key)));
                break;
            }
        return pick;
    }

    /// <summary>효과가 가장 많은 룬 · 발동 계열이 있는 룬 · 효과 한 줄짜리 룬(세 장 맞춤을 보려고 길이가 다른 셋).</summary>
    private static List<(RuntimeItemData, ItemSO)> Candidates()
    {
        var all = new List<(RuntimeItemData d, string key)>();
        foreach (var kv in Managers.ItemData.GetAllItems())
        {
            var d = RuntimeItemData.FromServer(kv.Value);
            if (d != null && d.effects != null && d.effects.Count > 0) all.Add((d, kv.Key));
        }
        var pick = new List<(RuntimeItemData d, string key)>();
        if (all.Count == 0) return new List<(RuntimeItemData, ItemSO)>();
        var most = all[0];
        foreach (var a in all) if (a.d.effects.Count > most.d.effects.Count) most = a;
        pick.Add(most);
        foreach (var a in all) if (!pick.Contains(a) && BuildFamilyRules.OfItem(a.d) != BuildFamily.None) { pick.Add(a); break; }
        foreach (var a in all) if (!pick.Contains(a) && a.d.effects.Count == 1) { pick.Add(a); break; }
        foreach (var a in all) { if (pick.Count >= 3) break; if (!pick.Contains(a)) pick.Add(a); }
        return pick.ConvertAll(p => (p.d, ItemSORegistry.Find(p.key)));
    }

    private static T Field<T>(object o, string field) where T : class => o?.GetType().GetField(field, Any)?.GetValue(o) as T;

    /// <summary>카드마다 효과 줄이 서로 겹치지 않고 효과 칸 안에 있나(월드 좌표 · 1px 여유).</summary>
    private static string RowsReport(UI_RuneSelectPopup popup, out bool ok)
    {
        ok = true;
        var parts = new List<string>();
        var c = new Vector3[4];
        foreach (var box in popup.GetComponentsInChildren<RectTransform>(true))
        {
            if (box.name != "EffectBox" || !box.gameObject.activeInHierarchy) continue;
            box.GetWorldCorners(c);
            float bTop = c[1].y, bBot = c[0].y;
            var rows = new List<(float top, float bot)>();
            foreach (var row in box.GetComponentsInChildren<EffectRowWidget>(true))
            {
                if (row.Label == null) continue;
                row.Label.rectTransform.GetWorldCorners(c);
                float top = c[1].y, bot = top - row.Label.preferredHeight * row.Label.rectTransform.lossyScale.y;
                rows.Add((top, bot));
            }
            rows.Sort((a, b) => b.top.CompareTo(a.top));
            int over = 0, outside = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0 && rows[i].top > rows[i - 1].bot + 1f) over++;
                if (rows[i].top > bTop + 1f || rows[i].bot < bBot - 1f) outside++;
            }
            if (over > 0 || outside > 0) ok = false;
            var dbg = new System.Text.StringBuilder();
            var fx = box.Find("Effects") as RectTransform;
            foreach (var row in box.GetComponentsInChildren<EffectRowWidget>(true))
            {
                if (row.Label == null) continue;
                row.Label.ForceMeshUpdate();
                dbg.Append($"[f{row.Label.fontSize:0.#} 칸{((RectTransform)row.transform).rect.height:0} 글w{row.Label.rectTransform.rect.width:0} 렌더{row.Label.GetRenderedValues(false).y:0} 선호{row.Label.preferredHeight:0}]");
            }
            parts.Add($"{rows.Count}줄 겹침 {over} · 칸 밖 {outside} · 칸{box.rect.height:0} 묶음{(fx != null ? fx.rect.height : 0f):0}/선호{(fx != null ? LayoutUtility.GetPreferredHeight(fx) : 0f):0} {dbg}");
        }
        return string.Join(" / ", parts);
    }

    private static float s_cardW3 = -1f;

    private static string HintOf(UI_RuneSelectPopup popup)
    {
        var root = Field<Transform>(popup, "_windowRoot");
        return root != null && root.Find("SkipHint") is Transform t && t.TryGetComponent<TMP_Text>(out var tx) ? tx.GetParsedText() : "";
    }

    private static string SkipLabelOf(UI_RuneSelectPopup popup)
    {
        var root = Field<Transform>(popup, "_windowRoot");
        return root != null && root.Find("SkipBtn/Fill/Label") is Transform t && t.TryGetComponent<TMP_Text>(out var tx) ? tx.GetParsedText() : "";
    }

    private static string Text(UI_RuneSelectPopup popup, string field)
    {
        var t = Field<TMP_Text>(popup, field);
        return t != null ? t.GetParsedText() : "";
    }

    private static string TitleOf(UI_RuneSelectPopup popup)
    {
        var root = Field<Transform>(popup, "_windowRoot");
        return root != null && root.Find("Title") is Transform tt && tt.TryGetComponent<TMP_Text>(out var t) ? t.GetParsedText() : "";
    }

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
