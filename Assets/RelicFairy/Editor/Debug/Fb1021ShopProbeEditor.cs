#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-21 상점 새 레이아웃(흑요석 · 힉스필드 창 틀 · 행상 초상 · 가호 문장) 실측 — 사용자 「상점의 새로운 레이아웃부터 필요한 리소스를 제작해서 만들고 빌드」.
/// ① 프리팹 비우기(루트만 — 새 레이아웃은 런타임에 짓는다, 플레이 밖) ② 테스트 허브 플레이 중 촬영 · 점검:
/// 창 크기 · 진열 칸 종류(가호 2 · 룬 2 · 재료 1 · 포션 1) · 문장 판 · 글자 겹침/말줄임 · 잠금(다음 상점 같은 칸 · 같은 값) ·
/// 새로고침 값 20 → 40 → 80 · 구매 · 특가 품절. 결과 Temp/fb1021_shop.txt · Temp/ui_shots/SHOP21_*.png · 로그 「[SHOP21] 끝」.
/// </summary>
public static class Fb1021ShopProbeEditor
{
    private const string Tag      = "[SHOP21]";
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string OutTxt   = "Temp/fb1021_shop.txt";
    private const string ShotDir  = "Temp/ui_shots";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool s_armed;
    private static int s_pass, s_fail;

    [MenuItem("RelicFairy/Debug/10-21 상점 새 레이아웃 — 프리팹 비우기(루트만 · 플레이 밖)")]
    private static void StripPrefab()
    {
        if (Application.isPlaying) { Debug.LogWarning($"{Tag} 플레이 밖에서 한다."); return; }
        string path = AssetDatabase.FindAssets("UI_ShopPanel t:Prefab").Select(AssetDatabase.GUIDToAssetPath)
                                   .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == "UI_ShopPanel");
        if (path == null) { Debug.LogError($"{Tag} 프리팹 없음"); return; }
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            int n = root.transform.childCount;
            for (int i = n - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
            Debug.Log($"{Tag} 프리팹 비움 — 자식 {n}개 지움 · 저장 {ok} · {path}");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    [MenuItem("RelicFairy/Debug/10-21 상점 새 레이아웃 촬영 · 점검 (테스트 허브, 플레이 중)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning($"{Tag} 플레이 모드에서만 동작한다."); return; }
        if (Object.FindFirstObjectByType<TestHubLauncher>() == null) { Debug.LogWarning($"{Tag} 테스트 허브에서 시작한다."); return; }
        s_armed = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (!s_armed || !msg.StartsWith("[RunAuto]") || !msg.Contains("방 진입")) return;
        s_armed = false;
        Application.logMessageReceived -= OnLog;
        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
            var p = GameRunBootstrapper.Instance?.Run?.Player;
            if (p == null) { Debug.LogWarning($"{Tag} 런 플레이어가 없다"); return; }
            RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        s_pass = s_fail = 0;
        var sb = new StringBuilder("10-21 상점 새 레이아웃 점검\n");
        Directory.CreateDirectory(ShotDir);
        var go = new GameObject("~Fb1021Shop");
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            foreach (var mo in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (mo != null && !mo.IsDead) mo.gameObject.SetActive(false);
            await Wait(1.5f, ct);
            var run = GameRunBootstrapper.Instance.Run;
            run.PlayerState?.AddTempGold(900);
            HubGui("HideTestHubGui");

            // 진열 칸 종류(데이터만) — 가호 2 · 룬 2 · 재료 1 · 포션 1
            var stock = AbyssPeddlerCatalog.Build(new System.Random(7));
            string kinds = string.Join(" ", stock.Products.Select(x => x.Category.ToString()));
            Check(sb, $"진열 칸 종류 「{kinds}」 + 특가 {stock.Special?.Category}",
                  stock.Products.Count == 6 && stock.Products[0].Category == ShopProductCategory.Buff && stock.Products[1].Category == ShopProductCategory.Buff
                  && stock.Products[2].Category == ShopProductCategory.Rune && stock.Products[3].Category == ShopProductCategory.Rune
                  && stock.Products[4].Category == ShopProductCategory.Material && stock.Products[5].Category == ShopProductCategory.Potion
                  && stock.Special != null);
            Check(sb, $"가호 둘이 다름 「{stock.Products[0].DisplayName}」 · 「{stock.Products[1].DisplayName}」", stock.Products[0].DisplayName != stock.Products[1].DisplayName);

            var sc = go.AddComponent<ShopRoomController>();
            typeof(ShopRoomController).GetField("_run", Inst)?.SetValue(sc, run);
            typeof(ShopRoomController).GetField("_rerollEnabled", Inst)?.SetValue(sc, true);
            typeof(ShopRoomController).GetField("_peddler", Inst)?.SetValue(sc, stock);

            var shop = await Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>();
            if (shop == null) { sb.AppendLine("상점 — 로드 실패"); return; }
            shop.Bind(sc);
            await Wait(1.2f, ct);

            var win = F<RectTransform>(shop, "_win");
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4];
            win.GetWorldCorners(corners);
            float ww = (corners[2].x - corners[0].x) / Screen.width * 100f, wh = (corners[2].y - corners[0].y) / Screen.height * 100f;
            var frame = win.GetComponent<Image>();
            Check(sb, $"창 {ww:0}% × {wh:0}% · 틀 「{frame.sprite?.name}」", ww > 76f && ww < 90f && wh > 86f && frame.sprite != null && frame.sprite.name.Contains("Shop"));
            var portrait = win.Find("Portrait")?.GetComponent<Image>();
            Check(sb, $"행상 초상 「{portrait?.sprite?.name}」", portrait != null && portrait.gameObject.activeSelf && portrait.sprite != null);
            var card0 = win.Find("Card0");
            var plate = card0?.Find("Content/Plate")?.GetComponent<Image>();
            var icon  = plate != null ? plate.transform.Find("Icon")?.GetComponent<Image>() : null;
            Check(sb, $"가호 문장 「{plate?.sprite?.name}」 · 아이콘 「{icon?.sprite?.name}」", plate != null && plate.sprite != null && plate.sprite.name.Contains("Emblem") && icon != null && icon.sprite != null);
            var cardFrame = card0?.GetComponent<Image>();
            Check(sb, $"카드 틀 「{cardFrame?.sprite?.name}」", cardFrame != null && cardFrame.sprite != null && cardFrame.sprite.name.Contains("RuneCard"));
            int active = 0;
            for (int i = 0; i < 6; i++) if (win.Find($"Card{i}")?.gameObject.activeSelf == true) active++;
            Check(sb, $"카드 {active}/6 · 특가 {win.Find("Deal")?.gameObject.activeSelf}", active == 6 && win.Find("Deal")?.gameObject.activeSelf == true);
            Report(sb, "열림", win);
            foreach (var n in new[] { "Name", "BlessHead", "ShelfHead", "Selected/Name" })
            {
                var tx = win.Find(n)?.GetComponent<TMP_Text>();
                tx?.ForceMeshUpdate();
                Check(sb, $"글 보임 — {n} 「{tx?.text}」", tx != null && tx.isActiveAndEnabled && !tx.isTextTruncated && tx.textInfo.characterCount > 0
                      && tx.textInfo.characterInfo[0].isVisible);
            }
            await Shot("SHOP21_1_open");

            // 룬 고르기 → 상세
            Call(shop, "Select", 2);
            await Wait(0.5f, ct);
            var detail = F<TMP_Text>(shop, "_selEffect");
            Check(sb, $"룬 상세 「{Short(detail?.text)}」", detail != null && detail.text.Contains("▲"));
            await Shot("SHOP21_2_rune");

            // 잠금 → 다음 상점 같은 칸 · 같은 값
            var rune = sc.Products[2];
            sc.ToggleLock(2);
            await Wait(0.4f, ct);
            Check(sb, $"잠금 — {rune.DisplayName} {rune.Price} · 잠긴 수 {sc.LockedCount}/{sc.LockCapacity}", sc.IsLocked(rune) && sc.LockedCount == 1);
            await Shot("SHOP21_3_locked");
            var next = AbyssPeddlerCatalog.Build(new System.Random(99), ShopEvent.None, rune);
            int at = next.Products.IndexOf(rune);
            Check(sb, $"다음 상점 — 잠근 룬 칸 {at}(첫 룬 칸 2) · 값 {next.Products[Mathf.Max(0, at)].Price}", at == 2 && next.Products[at].Price == rune.Price);

            // 새로고침 20 → 40 → 80
            int g0 = sc.PlayerGold;
            Check(sb, $"새로고침 값 {sc.RerollCost} · 다음 {sc.NextRerollCost}", sc.RerollCost == 20 && sc.NextRerollCost == 40);
            Call(shop, "OnRerollClicked");
            await Wait(0.9f, ct);
            Check(sb, $"1번째 — 골드 {g0} → {sc.PlayerGold} · 다음 값 {sc.RerollCost} · 잠근 룬 남음 {sc.Products.Contains(rune)}", sc.PlayerGold == g0 - 20 && sc.RerollCost == 40 && sc.Products.Contains(rune));
            await Shot("SHOP21_4_reroll");
            Call(shop, "OnRerollClicked");
            await Wait(0.6f, ct);
            Call(shop, "OnRerollClicked");
            await Wait(0.6f, ct);
            Check(sb, $"3번 뒤 — 골드 {sc.PlayerGold}(−140) · 남은 {sc.RerollsLeft} · 더 못 함 {!sc.RerollAvailable}", sc.PlayerGold == g0 - 140 && !sc.RerollAvailable);

            // 구매(잠근 룬) → 잠금 풀림 · 품절
            int ri = sc.Products.ToList().IndexOf(rune);
            Call(shop, "Select", ri);
            int g1 = sc.PlayerGold;
            Call(shop, "OnBuyClicked");
            await Wait(1.0f, ct);
            Managers.UI.CloseAllPopupUI();   // 룬을 사면 룬판이 열린다 — 닫고 상점을 다시 연다
            await Wait(0.4f, ct);
            shop = await Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>();
            shop.Bind(sc);
            await Wait(1.0f, ct);
            win = F<RectTransform>(shop, "_win");
            var sold = win.Find($"Card{ri}/Sold");
            Check(sb, $"잠근 룬 구매 — 골드 {g1} → {sc.PlayerGold} · 잠금 {sc.LockedCount} · 품절 글자 {sold?.gameObject.activeSelf}", sc.PlayerGold == g1 - rune.Price && sc.LockedCount == 0 && sold != null && sold.gameObject.activeSelf);

            // 특가 구매 → 품절 뒤 사라짐
            Call(shop, "Select", -2);
            Call(shop, "OnBuyClicked");
            await Wait(0.3f, ct);
            await Shot("SHOP21_5_deal_sold");
            bool dealSoldShown = win.Find("Deal/Sold")?.gameObject.activeSelf == true;
            await Wait(1.0f, ct);
            Check(sb, $"특가 — 품절 보임 {dealSoldShown} · 뒤에 사라짐 {win.Find("Deal")?.gameObject.activeSelf == false}", dealSoldShown && win.Find("Deal")?.gameObject.activeSelf == false);
            // 특가가 룬이면 룬판이 열린다 — 닫고 상점을 다시 열어 구매 뒤 모습을 찍는다
            Managers.UI.CloseAllPopupUI();
            if (UI_GridPanel.Instance != null && UI_GridPanel.Instance.gameObject.activeSelf) Managers.UI.HideOverlayUI<UI_GridPanel>();
            await Wait(0.5f, ct);
            shop = await Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>();
            shop.Bind(sc);
            await Wait(1.0f, ct);
            win = F<RectTransform>(shop, "_win");
            Report(sb, "구매 뒤", win);
            await Shot("SHOP21_6_after");
        }
        catch (System.OperationCanceledException) { }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); s_fail++; }
        finally
        {
            Managers.UI?.CloseAllPopupUI();
            Object.Destroy(go);
            sb.Insert(0, $"통과 {s_pass} · 실패 {s_fail}\n");
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝 — 통과 {s_pass} · 실패 {s_fail}\n{sb}");
        }
    }

    private static void Check(StringBuilder sb, string what, bool ok)
    {
        if (ok) s_pass++; else s_fail++;
        sb.AppendLine($"[{(ok ? "O" : "X")}] {what}");
    }

    /// <summary>보이는 글의 겹침 · 말줄임 · 작은 글(화면 px 14 미만).</summary>
    private static void Report(StringBuilder sb, string what, Transform root)
    {
        Canvas.ForceUpdateCanvases();
        var texts = new List<TMP_Text>();
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(false))
            if (t != null && t.isActiveAndEnabled && t.color.a > 0.05f && t.GetParsedText().Trim().Length > 0) texts.Add(t);
        var trunc = new List<string>();
        var small = new List<string>();
        foreach (var t in texts)
        {
            t.ForceMeshUpdate();
            float px = t.fontSize * t.transform.lossyScale.y * 1080f / Mathf.Max(1f, Screen.height);   // 1080p 화면 px로 환산
            if (px < 13.5f) small.Add($"{t.name}({px:0.#})");
            if (t.isTextTruncated) trunc.Add($"{t.transform.parent?.name}/{t.name}");
        }
        var hits = new List<string>();
        for (int i = 0; i < texts.Count; i++)
            for (int j = i + 1; j < texts.Count; j++)
            {
                if (texts[i].transform.IsChildOf(texts[j].transform) || texts[j].transform.IsChildOf(texts[i].transform)) continue;
                var a = WorldRect(texts[i]); var b = WorldRect(texts[j]);
                if (Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > 1f && Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > 1f)
                    hits.Add($"{texts[i].transform.parent?.name}/{texts[i].name}×{texts[j].transform.parent?.name}/{texts[j].name}");
            }
        sb.AppendLine($"{what}: 보이는 글 {texts.Count} · 겹침 {hits.Count} {string.Join(" ", hits.Take(6))}");
        sb.AppendLine($"  말줄임 {trunc.Count} {string.Join(" ", trunc.Take(8))} · 작은 글 {small.Count} {string.Join(" ", small.Take(6))}");
    }

    private static Rect WorldRect(TMP_Text t)
    {
        var b = t.textBounds;
        Vector3 a = t.transform.TransformPoint(b.min), c = t.transform.TransformPoint(b.max);
        return Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
    }

    private static string Short(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("\n", " / ").Substring(0, Mathf.Min(70, s.Length));

    private static T F<T>(object o, string field) where T : class => o?.GetType().GetField(field, Inst)?.GetValue(o) as T;

    private static void Call(object o, string method, params object[] args)
        => o?.GetType().GetMethod(method, Inst)?.Invoke(o, args.Length == 0 ? null : args);

    private static void HubGui(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);

    private static UniTask Wait(float s, CancellationToken ct) => UniTask.Delay(System.TimeSpan.FromSeconds(s), DelayType.Realtime, cancellationToken: ct);

    private static async UniTask Shot(string name)
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
