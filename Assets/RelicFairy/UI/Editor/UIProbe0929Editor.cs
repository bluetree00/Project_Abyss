using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 09-29 사용자 피드백 묶음 실측 — 서비스 NPC 서는 높이 · 상점(배치 · 명판 · 값 두 줄 · 불) · 재련소(성공률 분해 · 3줄 표 · 파츠 행).
/// 서비스 방으로 곧장 가는 메뉴가 없어 NPC는 <b>실제 프리팹을 지금 방 바닥에</b> NS 앵커와 같은 높이(바닥 윗면 −0.1)로 세워 잰다.
/// 결과: Temp/ui_probe_0929.txt · 화면은 Temp/ui_shots/S29_*.png.
/// </summary>
public static class UIProbe0929Editor
{
    private const BindingFlags NonPub = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Out = "Temp/ui_probe_0929.txt";
    private const int GroundMask = 1 << 3;
    private const float TokenBaseY = -0.1f;   // NS 앵커 높이 = 바닥 블록 중심(GameRunBootstrapper.blockBaseY)

    private static readonly string[] NpcPrefabs =
    {
        "Assets/RelicFairy/Systems/Stage/Shop/Prefabs/ShopNpc.prefab",
        "Assets/RelicFairy/Systems/Stage/Crucible/Prefabs/CrucibleNpc.prefab",
        "Assets/RelicFairy/Systems/Stage/Refinery/Prefabs/RefineryNpc.prefab",
    };

    [MenuItem("RelicFairy/UI/09-29 NPC 높이·상점·재련소 실측 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            Debug.LogWarning("[Probe0929] 런에 들어간 뒤 실행해야 한다.");
            return;
        }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder();
        Invoke("HideTestHubGui");
        try
        {
            await NpcAsync(sb);
            await BadgeAsync(sb);
            await ShopAsync(sb);
            await CrucibleAsync(sb);
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Managers.UI?.CloseAllPopupUI();
            Invoke("RestoreTestHubGui");
            File.WriteAllText(Out, sb.ToString());
            Debug.Log($"[Probe0929] 끝 → {Out}");
        }
    }

    // ── NPC ──────────────────────────────────────────────

    private static async UniTask NpcAsync(StringBuilder sb)
    {
        var player = GameRunBootstrapper.Instance.Run.Player.transform;
        var cam    = Camera.main;
        Vector3 right = cam != null ? Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized : Vector3.right;
        Vector3 fwd   = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : Vector3.forward;
        var spawned = new GameObject[NpcPrefabs.Length];
        Physics.SyncTransforms();
        try
        {
            for (int i = 0; i < NpcPrefabs.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NpcPrefabs[i]);
                if (prefab == null) { sb.AppendLine($"NPC 프리팹 없음: {NpcPrefabs[i]}"); continue; }

                Vector3 p = player.position + right * ((i - 1) * 2.4f) + fwd * 2.5f;
                if (!Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 8f, GroundMask, QueryTriggerInteraction.Ignore))
                { sb.AppendLine($"{prefab.name}: 바닥 못 찾음 @ {p}"); continue; }
                float floor = hit.point.y;

                Vector3 anchor = new(p.x, floor + TokenBaseY, p.z);          // NS 토큰 앵커 높이
                Vector3 stand  = ServiceRoomDecorPlacer.NpcStandPoint(anchor, 1f);
                var go = UnityEngine.Object.Instantiate(prefab, stand, Quaternion.LookRotation(-fwd));
                spawned[i] = go;

                var body  = go.transform.Find("Body");
                float feet = body != null ? body.position.y : float.NaN;
                float min  = float.PositiveInfinity, max = float.NegativeInfinity;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (r is ParticleSystemRenderer) continue;
                    min = Mathf.Min(min, r.bounds.min.y); max = Mathf.Max(max, r.bounds.max.y);
                }
                float lift = stand.y - anchor.y;
                sb.AppendLine($"{prefab.name}: 바닥 {floor:0.00} · 앵커 {anchor.y:0.00} → 선 곳 {stand.y:0.00} · 발(Body) − 바닥 {feet - floor:+0.00;-0.00} m" +
                              $" · 몸 {min - floor:0.00}~{max - floor:0.00} m (예전 앵커 그대로면 발 {feet - lift - floor:+0.00;-0.00} m)");
            }
            await UniTask.Delay(700, ignoreTimeScale: true);
            await Shot("S29_Npc_Stand", 0);
        }
        finally
        {
            foreach (var go in spawned) if (go != null) UnityEngine.Object.Destroy(go);
        }
    }

    // ── 출구 배지 — 긴 부제 폭 · 팝업 동안 걷힘 ─────────────────
    private static async UniTask BadgeAsync(StringBuilder sb)
    {
        var player = GameRunBootstrapper.Instance.Run.Player.transform;
        var a = new GameObject("~P29ExitA"); a.transform.position = player.position + new Vector3(12f, 0f, 18f);
        var b = new GameObject("~P29ExitB"); b.transform.position = player.position + new Vector3(-30f, 0f, -6f);
        var compass = ExitCompassHud.Create();
        try
        {
            compass.SetExits(new List<(Transform, string, string, Color)>
            {
                (a.transform, "◆ 정예", "희귀 이상 · 후보 4 · 강화재료 2", new Color(0.85f, 0.35f, 0.35f)),
                (b.transform, "◇ 상점", "포션 · 룬", new Color(0.95f, 0.78f, 0.35f)),
            });
            await UniTask.Delay(600, ignoreTimeScale: true);
            await Shot("S29_Badge_Long", 0);
            foreach (var t in compass.GetComponentsInChildren<TMP_Text>(true))
                if (t.name == "Sub" || t.name == "Title")
                    sb.AppendLine($"배지 {t.transform.parent.name}/{t.name}: 글 {t.preferredWidth:0} / 칸 {t.rectTransform.rect.width:0} · 글자 {t.fontSize:0.#}");

            var cg = compass.GetComponent<CanvasGroup>();
            await Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>();
            await UniTask.Delay(600, ignoreTimeScale: true);
            sb.AppendLine($"팝업 중 배지 알파 {(cg != null ? cg.alpha : -1f):0.00} (목표 0)");
            await Shot("S29_Badge_UnderPopup", 0);
            Managers.UI.CloseAllPopupUI();
            await UniTask.Delay(600, ignoreTimeScale: true);
            sb.AppendLine($"팝업 닫힌 뒤 배지 알파 {(cg != null ? cg.alpha : -1f):0.00} (목표 1)");
        }
        finally
        {
            UnityEngine.Object.Destroy(a);
            UnityEngine.Object.Destroy(b);
            if (compass != null) UnityEngine.Object.Destroy(compass.gameObject);
        }
    }

    // ── 상점 ─────────────────────────────────────────────

    private static async UniTask ShopAsync(StringBuilder sb)
    {
        var run = GameRunBootstrapper.Instance.Run;
        GameObject go = null;
        try
        {
            var ps = run.PlayerState;
            if (ps != null && ps.TempGold > 0) ps.TrySpendGold(ps.TempGold);
            ps?.AddTempGold(40);

            var shop = await Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>();
            go = new GameObject("~Probe0929Shop");
            var sc = go.AddComponent<ShopRoomController>();
            typeof(ShopRoomController).GetField("_run", NonPub)?.SetValue(sc, run);
            typeof(ShopRoomController).GetField("_peddler", NonPub)?.SetValue(sc, AbyssPeddlerCatalog.Build(new System.Random(11)));
            typeof(ShopRoomController).GetField("_rerollEnabled", NonPub)?.SetValue(sc, true);   // 새로고침 줄까지 보이게
            typeof(ShopRoomController).GetField("_rerollCost", NonPub)?.SetValue(sc, 50);        // 40골드 → 골드 부족 모양
            shop.Bind(sc);
            await UniTask.Delay(1000, ignoreTimeScale: true);
            await Shot("S29_Shop_Open", 0);

            var select = typeof(UI_ShopPanel).GetMethod("Select", NonPub);
            select?.Invoke(shop, new object[] { 1 });
            await Shot("S29_Shop_Select_Poor", 400);

            ps?.AddTempGold(900);
            typeof(UI_ShopPanel).GetMethod("RefreshAll", NonPub)?.Invoke(shop, null);
            await Shot("S29_Shop_Select_Rich", 900);   // 불이 숨 쉬는 중간

            DumpRects(sb, shop.transform.Find("Window") as RectTransform,
                      "TitleBoard", "Dialog", "SigilLine", "Gold", "Crest", "Deal", "Card0", "Card5", "Selected", "Reroll", "Exit",
                      "CornerTL", "CornerTR", "CornerBL", "CornerBR");
            DumpOverflow(sb, "상점", shop.transform);
        }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            if (go != null) UnityEngine.Object.Destroy(go);
            await UniTask.Delay(500, ignoreTimeScale: true);
        }
    }

    // ── 재련소 ───────────────────────────────────────────

    private static async UniTask CrucibleAsync(StringBuilder sb)
    {
        var run = GameRunBootstrapper.Instance.Run;
        GameObject go = null;
        try
        {
            go = new GameObject("~Probe0929Crucible");
            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(7), null);
            run.FuelBank?.Add(FuelKind.EnhanceMaterial, 40);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await UniTask.Delay(1200, ignoreTimeScale: true);
            await Shot("S29_Crucible_Weapon", 0);
            DumpOverflow(sb, "재련소 무기", panel.transform);

            // 화로 이벤트(열기) — 성공률 아래 줄 · 큰 숫자 색이 바로 바뀌어야 한다
            typeof(CrucibleRoomController).GetField("_event", NonPub)?.SetValue(ctrl, CrucibleEvent.Fever);
            typeof(UI_CruciblePanel).GetMethod("RefreshAll", NonPub)?.Invoke(panel, null);
            await Shot("S29_Crucible_Fever", 500);
            var rb = panel.transform.GetComponentsInChildren<TMP_Text>(true);
            foreach (var t in rb) if (t.name == "RateBreak") sb.AppendLine($"성공률 분해 줄: 「{t.text}」 · 폭 {t.preferredWidth:0}/{t.rectTransform.rect.width:0}");

            typeof(CrucibleRoomController).GetField("_event", NonPub)?.SetValue(ctrl, CrucibleEvent.Bounty);
            typeof(UI_CruciblePanel).GetMethod("RefreshAll", NonPub)?.Invoke(panel, null);
            await Shot("S29_Crucible_Bounty", 500);

            // 원거리 파츠 탭
            typeof(UI_CruciblePanel).GetMethod("SelectTab", NonPub)?.Invoke(panel, new object[] { 1 });
            await Shot("S29_Crucible_Ranged", 700);
            DumpOverflow(sb, "재련소 파츠", panel.transform);
        }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            if (go != null) UnityEngine.Object.Destroy(go);
        }
    }

    // ── 헬퍼 ─────────────────────────────────────────────

    private static void DumpRects(StringBuilder sb, RectTransform win, params string[] names)
    {
        if (win == null) { sb.AppendLine("창 없음"); return; }
        var size = win.rect.size;
        sb.AppendLine($"상점 창 {size.x:0}×{size.y:0} (목업 1178×837 기준 px로 환산)");
        float k = 1178f / Mathf.Max(1f, size.x);
        foreach (var n in names)
        {
            var t = win.Find(n) as RectTransform;
            if (t == null) { sb.AppendLine($"  {n}: 없음"); continue; }
            var c = new Vector3[4]; t.GetWorldCorners(c);
            Vector2 a = win.InverseTransformPoint(c[0]), b = win.InverseTransformPoint(c[2]);
            float x0 = (a.x - win.rect.xMin) * k, x1 = (b.x - win.rect.xMin) * k;
            float y0 = (win.rect.yMax - b.y) * k, y1 = (win.rect.yMax - a.y) * k;
            sb.AppendLine($"  {n}: x {x0:0}~{x1:0} · y {y0:0}~{y1:0}");
        }
    }

    private static void DumpOverflow(StringBuilder sb, string label, Transform root)
    {
        int n = 0;
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(false))
        {
            if (string.IsNullOrEmpty(t.text) || !t.gameObject.activeInHierarchy) continue;
            var r = t.rectTransform.rect;
            bool wide = t.textWrappingMode == TextWrappingModes.NoWrap && t.preferredWidth > r.width + 1f;
            bool tall = t.preferredHeight > r.height + 2f && t.overflowMode == TextOverflowModes.Overflow;
            if (!wide && !tall && !t.isTextOverflowing) continue;
            n++;
            sb.AppendLine($"  [{label}] 넘침 {t.name}: 글 {t.preferredWidth:0}×{t.preferredHeight:0} / 칸 {r.width:0}×{r.height:0} · 「{Trim(t.GetParsedText())}」");
        }
        sb.AppendLine($"{label} 넘침 {n}건");
    }

    private static string Trim(string s) => s == null ? "" : (s.Length > 40 ? s.Substring(0, 40) + "…" : s).Replace('\n', '/');

    private static UniTask Shot(string name, int settleMs)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, settleMs }) : UniTask.CompletedTask;
    }

    private static void Invoke(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
}
