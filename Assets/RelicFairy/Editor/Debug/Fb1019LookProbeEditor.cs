#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
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
/// 10-19 사용자 「상점 · 재련소도 같은 내용 체크 — 배경과 내부가 적절한지 · 여유가 있는지」 실측(테스트 허브 · 플레이 중).
/// 화면마다 창 크기(화면 대비 %) · 창 바탕 그림 · 보이는 글 수 · 글자 겹침 · 말줄임 · 작은 글(16 미만, 화면 px) · 사진.
/// 화면: 상점(심연의 행상) · 재련소 첫 화면 · 무기 재련 · 파츠 재련. 결과 Temp/fb1019_look.txt · Temp/ui_shots/LOOK_*.png · 로그 「[FB19look] 끝」.
/// </summary>
public static class Fb1019LookProbeEditor
{
    private const string Tag      = "[FB19look]";
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string Bow      = "Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset";
    private const string OutTxt   = "Temp/fb1019_look.txt";
    private const string ShotDir  = "Temp/ui_shots";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool s_armed;

    [MenuItem("RelicFairy/Debug/10-19 상점 · 재련소 화면 여유 점검 (테스트 허브, 플레이 중)")]
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
        var sb = new StringBuilder("10-19 상점 · 재련소 화면 여유 점검\n");
        Directory.CreateDirectory(ShotDir);
        var go = new GameObject("~Fb1019Look");
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            foreach (var mo in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (mo != null && !mo.IsDead) mo.gameObject.SetActive(false);
            await Wait(1.5f, ct);
            var run = GameRunBootstrapper.Instance.Run;
            var wm  = p.WeaponManager;
            if (wm.Weapon1Data == null)
            {
                var bow = AssetDatabase.LoadAssetAtPath<WeaponSO>(Bow);
                if (bow != null) await wm.AcquireWeaponToSlotAsync(WeaponData.FromSO(bow), PlayerWeaponManager.Slot1, setActive: false, playAppear: false);
            }
            run.PlayerState?.AddTempGold(300);
            run.FuelBank.Add(FuelKind.EnhanceMaterial, 200);
            HubGui("HideTestHubGui");

            // ① 상점(심연의 행상)
            var shop = await Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>();
            if (shop != null)
            {
                var sc = go.AddComponent<ShopRoomController>();
                typeof(ShopRoomController).GetField("_run", Inst)?.SetValue(sc, run);
                typeof(ShopRoomController).GetField("_peddler", Inst)?.SetValue(sc, AbyssPeddlerCatalog.Build(new System.Random(7)));
                shop.Bind(sc);
                await Wait(1.2f, ct);
                Report(sb, "상점", shop.transform.Find("Window") ?? shop.transform);
                await Shot("LOOK_1_shop");
                Managers.UI.CloseAllPopupUI();
                await Wait(0.5f, ct);
            }
            else sb.AppendLine("상점 — 로드 실패");

            // ② ~ ④ 재련소
            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(7), null);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await Wait(1.0f, ct);
            var root = typeof(UI_CruciblePanel).GetField("_v3Root", Inst)?.GetValue(panel) as Transform ?? panel.transform;
            Report(sb, "재련소 첫 화면", root);
            await Shot("LOOK_2_cru_chooser");
            (typeof(UI_CruciblePanel).GetField("_chooseCard", Inst)?.GetValue(panel) as Button[])?[0]?.onClick.Invoke();
            Call(panel, "RefreshAll");
            await Wait(0.8f, ct);
            Report(sb, "재련소 무기 재련", root);
            await Shot("LOOK_3_cru_melee");
            Call(panel, "SelectTab", 1);
            Call(panel, "RefreshAll");
            await Wait(0.8f, ct);
            Report(sb, "재련소 파츠 재련", root);
            await Shot("LOOK_4_cru_parts");
        }
        catch (System.OperationCanceledException) { }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Managers.UI?.CloseAllPopupUI();
            Object.Destroy(go);
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    /// <summary>창 크기 · 바탕 그림 · 글 수 · 겹침 · 말줄임 · 작은 글(화면 px 16 미만).</summary>
    private static void Report(StringBuilder sb, string what, Transform root)
    {
        Canvas.ForceUpdateCanvases();
        var rt = root as RectTransform;
        var c = new Vector3[4];
        string size = "-";
        if (rt != null)
        {
            rt.GetWorldCorners(c);
            float w = c[2].x - c[0].x, h = c[2].y - c[0].y;
            size = $"{w:0}×{h:0}px(화면 {w / Screen.width * 100f:0}% × {h / Screen.height * 100f:0}%)";
        }
        var bg = root.GetComponent<Image>();
        var texts = new List<TMP_Text>();
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(false))
            if (t != null && t.isActiveAndEnabled && t.color.a > 0.05f && t.GetParsedText().Trim().Length > 0) texts.Add(t);
        var small = new List<string>();
        var trunc = new List<string>();
        foreach (var t in texts)
        {
            t.ForceMeshUpdate();
            float px = t.fontSize * t.transform.lossyScale.y * (Screen.height / 1080f > 0f ? 1f : 1f);
            if (px < 15.5f) small.Add($"{t.name}({px:0.#})");
            if (t.isTextTruncated || t.isTextOverflowing) trunc.Add(t.name);
        }
        var hits = new List<string>();
        for (int i = 0; i < texts.Count; i++)
            for (int j = i + 1; j < texts.Count; j++)
            {
                if (texts[i].transform.IsChildOf(texts[j].transform) || texts[j].transform.IsChildOf(texts[i].transform)) continue;
                var a = WorldRect(texts[i]); var b = WorldRect(texts[j]);
                if (Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > 1f && Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > 1f)
                    hits.Add($"{texts[i].name}×{texts[j].name}");
            }
        sb.AppendLine($"{what}: 창 {size} · 바탕 「{(bg != null && bg.sprite != null ? bg.sprite.name : "색 판")}」 · 보이는 글 {texts.Count}");
        sb.AppendLine($"  겹침 {hits.Count} {string.Join(" ", hits.GetRange(0, Mathf.Min(6, hits.Count)))}");
        sb.AppendLine($"  넘침 · 말줄임 {trunc.Count} {string.Join(" ", trunc.GetRange(0, Mathf.Min(8, trunc.Count)))}");
        sb.AppendLine($"  16px 미만 {small.Count} {string.Join(" ", small.GetRange(0, Mathf.Min(8, small.Count)))}");
    }

    private static Rect WorldRect(TMP_Text t)
    {
        var b = t.textBounds;
        Vector3 a = t.transform.TransformPoint(b.min), c = t.transform.TransformPoint(b.max);
        return Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
    }

    private static void Call(UI_CruciblePanel panel, string method, params object[] args)
        => typeof(UI_CruciblePanel).GetMethod(method, Inst)?.Invoke(panel, args.Length == 0 ? null : args);

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
