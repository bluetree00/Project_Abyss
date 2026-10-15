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
/// 10-15 돌발 이벤트 「들어가는 순간 안다」 실측(기획 「재련소·강화·돌발이벤트 개선안」 §6) — 테스트 허브에서 플레이 중에 돌린다.
///  ① 배너 — RoomEventNotice가 화면 위 가운데(구역 이름 배너)에 이름 + 한 줄을 띄운다 · 방이 드러난 순간(RoomRevealed) 재련소가 쏜다.
///  ② 정제소 — 반값(값 절반) · 고순도(Rare 0% · 한 번 Epic 이상 · 쓰면 꺼짐) · 두 장(두 장 중 하나) · 제단 화면 이벤트 줄 · 천장 줄.
///  ③ 상점 — 떨이(같은 진열 값 ×0.7) · 귀한 물건(맨 앞 Epic 룬 · 6칸 그대로) · 화면 이벤트 줄 · 상인 인사.
/// 사진 Temp/ui_shots/SE_*.png · 결과 Temp/service_event_probe.txt · 로그 「[SEProbe] 끝」.
/// </summary>
public static class ServiceEvent1015ProbeEditor
{
    private const string Tag     = "[SEProbe]";
    private const string OutTxt  = "Temp/service_event_probe.txt";
    private const float  MinFont = 15.95f;
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static int s_pass, s_all;

    [MenuItem("RelicFairy/Debug/10-15 돌발 이벤트 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-15 돌발 이벤트 실측\n");
        s_pass = s_all = 0;
        Directory.CreateDirectory("Temp/ui_shots");
        HubGui("HideTestHubGui");
        try
        {
            // 런이 없으면 테스트 허브에서 1장 런을 연다(정제소 뽑기 실측과 같은 길)
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
            if (run == null) { sb.AppendLine("런이 시작되지 않음"); return; }
            await Wait(3f);
            HubGui("HideTestHubGui");
            await BannerAsync(run, sb);
            await RefineryAsync(run, sb);
            await ShopAsync(run, sb);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Managers.UI?.CloseAllPopupUI();
            HubGui("RestoreTestHubGui");
            sb.AppendLine($"통과 {s_pass}/{s_all}");
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    // ── ① 배너 ──
    private static async UniTask BannerAsync(GameRunSession run, StringBuilder sb)
    {
        sb.AppendLine("[1 배너]");
        var banner = Object.FindFirstObjectByType<ZoneTitleBanner>();
        RoomEventNotice.Raise("시험 배너", "한 줄 설명 — 이 방에서만");
        await Wait(0.7f);
        var title = Field<TMP_Text>(banner, "_title");
        var desc  = Field<TMP_Text>(banner, "_desc");
        var group = Field<CanvasGroup>(banner, "_group");
        Check(sb, "RoomEventNotice → 화면 위 배너(이름 · 한 줄)", title != null && title.text == "시험 배너" && desc != null && desc.text.Contains("이 방에서만") && group != null && group.alpha > 0.9f,
              $"「{title?.text}」 「{desc?.text}」 알파 {group?.alpha:0.00}");
        await Shot("SE_1_Banner");
        await Wait(3.2f);   // 배너가 걷힐 때까지

        // 방이 드러난 순간 — 재련소(열기 화로)가 배너를 쏜다
        var go = new GameObject("~SEProbeCrucible");
        try
        {
            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(7), null);
            typeof(CrucibleRoomController).GetField("_event", Any)?.SetValue(ctrl, CrucibleEvent.Fever);
            RaiseRevealed(go);
            await Wait(0.7f);
            Check(sb, "재련소 — 방이 드러나면 「열기 오른 화로」 배너", title != null && title.text == "열기 오른 화로" && desc.text.Contains("+15"),
                  $"「{title?.text}」 「{desc?.text}」");
            await Shot("SE_1b_CrucibleBanner");

            // 다른 방이 드러나면 쏘지 않는다
            title.text = "-";
            RaiseRevealed(new GameObject("~SEProbeOtherRoom"));
            await Wait(0.3f);
            Check(sb, "재련소 — 다른 방이 드러나면 배너 없음", title.text == "-", $"「{title.text}」");
        }
        finally { Object.Destroy(go); }
        await Wait(3.2f);
    }

    // ── ② 정제소 ──
    private static async UniTask RefineryAsync(GameRunSession run, StringBuilder sb)
    {
        sb.AppendLine("[2 정제소]");
        var svc = run.Refinery;
        var owner = new object();
        run.FuelBank.Add(FuelKind.RuneOre, 300);

        svc.BeginRoomEvent(owner, RefineryEvent.None);
        int full = svc.CurrentCost;
        svc.BeginRoomEvent(owner, RefineryEvent.HalfPrice);
        Check(sb, "반값 정제 — 값 절반(다음 값도)", svc.CurrentCost == Mathf.Max(1, Mathf.RoundToInt(full * 0.5f)), $"{full} → {svc.CurrentCost} · 다음 {svc.NextCost}");
        await PanelAsync(sb, "SE_2_RefHalf", "반값 정제", run);

        svc.BeginRoomEvent(owner, RefineryEvent.HighPurity);
        var (rare, epic, leg) = svc.CurrentOdds();
        Check(sb, "고순도 — Rare 0% · Epic + Legend 100%", rare == 0f && Mathf.Abs(epic + leg - 1f) < 0.001f, $"{rare:P0} · {epic:P0} · {leg:P0}");
        await PanelAsync(sb, "SE_2b_RefPurity", "고순도 원석", run);
        var o = svc.Craft();
        Check(sb, "고순도 — 이번 한 번 Epic 이상 · 쓰면 꺼짐", o.Success && o.Rarity >= ItemRarity.Epic && !svc.PurityReady,
              $"성공 {o.Success} · {o.Rarity} · 남음 {svc.PurityReady} {o.FailReason}");
        if (o.Alt != null) svc.Refund(o.Spent);

        svc.BeginRoomEvent(owner, RefineryEvent.TwoCards);
        Check(sb, "두 장 정제 — 두 장 중 하나", svc.TwoCards, $"{svc.TwoCards}");
        await PanelAsync(sb, "SE_2c_RefTwo", "두 장 정제", run);
        var o2 = svc.Craft();
        Check(sb, "두 장 정제 — 다른 장이 나온다", o2.Success && o2.Alt != null, $"성공 {o2.Success} · 다른 장 {o2.Alt?.itemId} {o2.FailReason}");
        if (o2.Alt != null) svc.Refund(o2.Spent);

        var other = new object();
        svc.EndRoomEvent(other);
        Check(sb, "다른 방이 풀면 그대로", svc.RoomEvent == RefineryEvent.TwoCards, $"{svc.RoomEvent}");
        svc.EndRoomEvent(owner);
        Check(sb, "건 방이 풀면 꺼짐", svc.RoomEvent == RefineryEvent.None && svc.CurrentCost >= full, $"{svc.RoomEvent} · 값 {svc.CurrentCost}");
    }

    private static async UniTask PanelAsync(StringBuilder sb, string shot, string expect, GameRunSession run)
    {
        var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_RefineryPanel>();
        if (panel == null) { Check(sb, $"{expect} — 정제소 화면", false, "로드 실패"); return; }
        await Wait(1.0f);
        var ev   = Field<TMP_Text>(panel, "_iEvent");
        var pity = Field<TMP_Text>(panel, "_iPity");
        Check(sb, $"{expect} — 이벤트 줄 · 천장 줄", ev != null && ev.text.Contains(expect) && pity != null && pity.text.Contains("Legend"),
              $"「{ev?.text}」 「{pity?.text}」");
        var idle = Field<CanvasGroup>(panel, "_wIdle");
        ScreenCheck(sb, expect, idle != null ? idle.transform : panel.transform);
        await Shot(shot);
        panel.ClosePopupUI();
        await Wait(0.5f);
    }

    // ── ③ 상점 ──
    private static async UniTask ShopAsync(GameRunSession run, StringBuilder sb)
    {
        sb.AppendLine("[3 상점]");
        var none = AbyssPeddlerCatalog.Build(new System.Random(7));
        var clr  = AbyssPeddlerCatalog.Build(new System.Random(7), ShopEvent.Clearance);
        bool same = none.Products.Count == clr.Products.Count;
        var ratios = new StringBuilder();
        for (int i = 0; same && i < none.Products.Count; i++)
        {
            float k = (float)clr.Products[i].Price / Mathf.Max(1, none.Products[i].Price);
            ratios.Append($"{none.Products[i].Price}→{clr.Products[i].Price} ");
            if (none.Products[i].Price >= 10 && (k < 0.66f || k > 0.74f)) same = false;
        }
        Check(sb, "떨이 — 같은 진열 값 ×0.7", same, ratios.ToString());

        var rare = AbyssPeddlerCatalog.Build(new System.Random(7), ShopEvent.RareGoods);
        bool canEpic = MemoryAltarService.MaxRuneRarity >= ItemRarity.Epic;
        Check(sb, "귀한 물건 — 맨 앞 Epic 룬 · 진열 6칸", !canEpic || (rare.RareGoods != null && rare.Products.Count == AbyssPeddlerCatalog.ProductCount
              && rare.Products[0] == rare.RareGoods && rare.RareGoods.Rarity == ItemRarity.Epic && rare.RareGoods.Rune != null),
              $"제단 Epic {canEpic} · {rare.RareGoods?.DisplayName} {rare.RareGoods?.Rarity} · {rare.Products.Count}칸");

        run.PlayerState?.AddTempGold(200);
        await ShopPanelAsync(sb, run, ShopEvent.Clearance, clr, "SE_3_ShopClearance", "떨이");
        if (canEpic) await ShopPanelAsync(sb, run, ShopEvent.RareGoods, rare, "SE_3b_ShopRare", "귀한 물건");
    }

    private static async UniTask ShopPanelAsync(StringBuilder sb, GameRunSession run, ShopEvent ev, AbyssPeddlerCatalog.Result stock, string shot, string expect)
    {
        GameObject go = null;
        try
        {
            var shop = await Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>();
            if (shop == null) { Check(sb, $"{expect} — 상점 화면", false, "로드 실패"); return; }
            go = new GameObject("~SEProbeShop");
            var sc = go.AddComponent<ShopRoomController>();
            typeof(ShopRoomController).GetField("_run", Any)?.SetValue(sc, run);
            typeof(ShopRoomController).GetField("_peddler", Any)?.SetValue(sc, stock);
            typeof(ShopRoomController).GetField("_event", Any)?.SetValue(sc, ev);
            shop.Bind(sc);
            await Wait(1.0f);
            var line = Field<TMP_Text>(shop, "_eventText");
            var talk = Field<TMP_Text>(shop, "_dialogText");
            Check(sb, $"{expect} — 화면 이벤트 줄 · 상인 인사", line != null && line.gameObject.activeInHierarchy && line.text.Contains(expect)
                  && talk != null && talk.text == sc.EventGreeting, $"「{line?.text}」 「{talk?.text}」");
            var window = shop.transform.Find("Window");
            ScreenCheck(sb, expect, window != null ? window : shop.transform, minFont: 0f);   // 상점은 목업 배율(15 × 창 맞춤) — 크기는 상점 실측이 본다
            await Shot(shot);
        }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            if (go != null) Object.Destroy(go);
            await Wait(0.5f);
        }
    }

    // ── 화면 CHECK — 보이는 글자 넘침 · 겹침 · 최소 크기 ──
    private static void ScreenCheck(StringBuilder sb, string what, Transform root, float minFont = MinFont)
    {
        Canvas.ForceUpdateCanvases();
        var texts = new List<TMP_Text>();
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(false))
            if (Visible(t)) texts.Add(t);

        var small = new StringBuilder();
        var over  = new StringBuilder();
        foreach (var t in texts)
        {
            t.ForceMeshUpdate();
            if (minFont > 0f && t.fontSize < minFont) small.Append($"{t.name}({t.fontSize:0.#}) ");
            if (t.isTextTruncated) over.Append($"{t.name}(말줄임) ");
        }
        var hit = new StringBuilder();
        for (int i = 0; i < texts.Count; i++)
            for (int j = i + 1; j < texts.Count; j++)
            {
                if (texts[i].transform.IsChildOf(texts[j].transform) || texts[j].transform.IsChildOf(texts[i].transform)) continue;
                var a = WorldRect(texts[i]); var b = WorldRect(texts[j]);
                float ox = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
                float oy = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                if (ox > 1f && oy > 1f) hit.Append($"{texts[i].name}「{Short(texts[i])}」×{texts[j].name}「{Short(texts[j])}」 ");
            }
        if (minFont > 0f) Check(sb, $"{what} — 글자 최소 16", small.Length == 0, small.Length == 0 ? $"보이는 글 {texts.Count}" : small.ToString());
        Check(sb, $"{what} — 넘침 0", over.Length == 0, over.Length == 0 ? "없음" : over.ToString());
        Check(sb, $"{what} — 글자 겹침 0", hit.Length == 0, hit.Length == 0 ? "없음" : hit.ToString());
    }

    private static Rect WorldRect(TMP_Text t)
    {
        var b = t.textBounds;
        Vector3 a = t.transform.TransformPoint(b.min), c = t.transform.TransformPoint(b.max);
        return Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
    }

    private static bool Visible(TMP_Text t)
    {
        if (t == null || !t.isActiveAndEnabled || t.color.a < 0.05f) return false;
        if (t.GetParsedText().Trim().Length == 0) return false;
        float a = 1f;
        foreach (var g in t.GetComponentsInParent<CanvasGroup>(false))
        {
            a *= g.alpha;
            if (g.ignoreParentGroups) break;
        }
        return a >= 0.05f;
    }

    // ── 도우미 ──
    private static void RaiseRevealed(GameObject room)
    {
        var f = typeof(RunFlowController).GetField("RoomRevealed", BindingFlags.Static | BindingFlags.NonPublic);
        (f?.GetValue(null) as System.Delegate)?.DynamicInvoke(room);
    }

    private static T Field<T>(object o, string name) where T : class
        => o == null ? null : o.GetType().GetField(name, Any)?.GetValue(o) as T;

    private static string Short(TMP_Text t)
    {
        string s = t != null ? t.GetParsedText() : "";
        return s.Length > 14 ? s.Substring(0, 14) + "…" : s;
    }

    private static void Check(StringBuilder sb, string what, bool ok, string detail)
    {
        s_all++;
        if (ok) s_pass++;
        sb.AppendLine($"  CHECK {what}: {ok} ({detail})");
    }

    private static void HubGui(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 200 }) : UniTask.CompletedTask;
    }

    private static UniTask Wait(float s) => UniTask.Delay(System.TimeSpan.FromSeconds(s), DelayType.Realtime);
}
#endif
