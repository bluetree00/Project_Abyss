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
/// 10-17 권장안(재련소 · 정제소 · 상점 파워밸런스 진단 안 C) 반입 실측 — 테스트 허브 Ch1 런에서 한 번에 돈다.
/// ① 재화 방 순서: 실제 방 풀 · 런 구조로 시드 300 × 정예 보스 예정 켬/끔 — 상점 · 정제소 바로 뒤 상점 · 정제소 0 ·
///    재련소는 그 뒤에도 나오고 그 앞뒤로도 상점 · 정제소가 나온다 · 「재화 방 먼저(재련소 → 정제소 → 상점)」 고르기로 Ch1 재련소 들른 비율.
/// ② 정제소 재료 녹이기: 원석 3 · 강화재료 30으로 열어 [녹이기] 줄 · 누른 뒤 원석 = 이번 비용 · 재료 차감 · 「얻은 양」 이벤트 0 · 버튼 사라짐 · 재료가 모자라면 안 보임.
/// ③ 행상 포션: 포션이 가득일 때 포션 상품 [포션 가득] · 사면 Unavailable · 골드 그대로 / 칸을 늘리면 [산다] · 사면 포션 +.
/// 결과 Temp/balance1017_probe.txt · Temp/ui_shots/B17_*.png · 로그 「[B1017] 끝」. CHECK 줄이 전부 True면 합격.
/// </summary>
public static class Balance1017ProbeEditor
{
    private const string Tag     = "[B1017]";
    private const string OutTxt  = "Temp/balance1017_probe.txt";
    private const string ShotDir = "Temp/ui_shots";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-17 권장안 실측 — 재화 방 순서 · 재료 녹이기 · 행상 포션 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중(테스트 허브)에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-17 권장안 실측\n");
        Directory.CreateDirectory(ShotDir);
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
            if (run == null) { sb.AppendLine("런이 시작되지 않음"); return; }
            await UniTask.Delay(3000, DelayType.Realtime);

            await SequenceAsync(sb);
            await MeltAsync(run, sb);
            await PotionAsync(run, sb);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    // ── ① 재화 방 순서 ──
    private static async UniTask SequenceAsync(StringBuilder sb)
    {
        sb.AppendLine("[1 재화 방 순서 — Ch1 · 시드 300]");
        var ensure = typeof(ServiceRoomTourProbeEditor).GetMethod("EnsureProcRunAsync", BindingFlags.Static | BindingFlags.NonPublic);
        if (ensure != null) await (UniTask<bool>)ensure.Invoke(null, null);
        var flow = RunFlowController.Active;
        var pool = flow != null ? typeof(RunFlowController).GetField("_pool", Any)?.GetValue(flow) as List<ZonePoolEntry> : null;
        var cfg  = flow != null ? typeof(RunFlowController).GetField("_resolvedStructure", Any)?.GetValue(flow) as IRunStructure : null;
        if (pool == null || cfg == null) { sb.AppendLine("  방 풀 · 런 구조를 못 읽었다"); return; }

        static bool Guarded(RoomPlanKind k) => k == RoomPlanKind.Shop || k == RoomPlanKind.Refinery;
        bool allOk = true;
        foreach (bool mb in new[] { true, false })
        {
            int guardedAfterGuarded = 0, crucibleAfterGuarded = 0, guardedAfterCrucible = 0, crucibleRuns = 0, runs = 300;
            var visits = new Dictionary<RoomPlanKind, int> { [RoomPlanKind.Crucible] = 0, [RoomPlanKind.Refinery] = 0, [RoomPlanKind.Shop] = 0 };
            for (int seed = 1; seed <= runs; seed++)
            {
                var seq  = new RunSequencer(pool, cfg, seed) { GateCurrencyRooms = true, MidBossPlanned = mb };
                var rng  = new System.Random(seed * 31);
                var last = RoomPlanKind.Normal;
                bool cru = false;
                for (int guard = 0; guard < 64 && !seq.IsDone && !seq.InBossApproach; guard++)
                {
                    var exits = seq.RollExits();
                    if (exits.Count == 0) break;
                    foreach (var d in exits)
                    {
                        if (Guarded(last) && Guarded(d.kind)) guardedAfterGuarded++;
                        if (Guarded(last) && d.kind == RoomPlanKind.Crucible) crucibleAfterGuarded++;
                        if (last == RoomPlanKind.Crucible && Guarded(d.kind)) guardedAfterCrucible++;
                    }
                    // 재화 방 먼저(재련소 → 정제소 → 상점) — 진단 문서 「전부 들름」 정책과 같은 순서
                    DoorPlan pick = exits[rng.Next(exits.Count)];
                    foreach (var want in new[] { RoomPlanKind.Crucible, RoomPlanKind.Refinery, RoomPlanKind.Shop })
                    {
                        int i = exits.FindIndex(d => d.kind == want);
                        if (i >= 0) { pick = exits[i]; break; }
                    }
                    if (visits.ContainsKey(pick.kind)) visits[pick.kind]++;
                    if (pick.kind == RoomPlanKind.Crucible) cru = true;
                    last = pick.kind;
                    seq.CommitEntry(pick);
                }
                if (cru) crucibleRuns++;
            }
            sb.AppendLine($"  정예 보스 예정 {(mb ? "켬" : "끔")}: 상점·정제소 뒤 상점·정제소 {guardedAfterGuarded} · 상점·정제소 뒤 재련소 {crucibleAfterGuarded} · " +
                          $"재련소 뒤 상점·정제소 {guardedAfterCrucible} · 재련소 들른 런 {crucibleRuns}/{runs} · " +
                          $"방문 재련소 {visits[RoomPlanKind.Crucible]} 정제소 {visits[RoomPlanKind.Refinery]} 상점 {visits[RoomPlanKind.Shop]}");
            allOk &= guardedAfterGuarded == 0 && guardedAfterCrucible > 0;
        }
        sb.AppendLine($"  CHECK 상점·정제소 연달아 0 · 재련소 뒤 상점·정제소 나옴: {allOk}");
    }

    // ── ② 정제소 재료 녹이기 ──
    private static async UniTask MeltAsync(GameRunSession run, StringBuilder sb)
    {
        sb.AppendLine("[2 정제소 재료 녹이기]");
        var bank = run.FuelBank;
        var svc  = run.Refinery;
        if (bank == null || svc == null) { sb.AppendLine("  연료 은행 · 정제소 없음"); return; }
        int mat0 = bank.EnhanceMaterial, ore0 = bank.RuneOre;
        int gained = 0;
        void OnGained(FuelKind k, int n) => gained += n;
        try
        {
            bank.RestoreRaw(30, 3);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_RefineryPanel>();
            if (panel == null) { sb.AppendLine("  정제소 패널을 못 열었다"); return; }
            await UniTask.Delay(900, DelayType.Realtime);

            var melt  = Get(panel, "_iMelt") as Image;
            var label = Get(panel, "_iMeltLabel") as TMP_Text;
            var note  = Get(panel, "_iMeltNote") as TMP_Text;
            int cost  = svc.CurrentCost, need = cost - 3;
            bool shown = melt != null && melt.gameObject.activeInHierarchy;
            sb.AppendLine($"  원석 3 · 재료 30 · 비용 {cost}: [녹이기] 보임 {shown} · 「{label?.text}」 · 「{note?.text}」 · 칸 {Rect(melt)}");
            await ShotAsync("B17_1_RefMelt_before");

            bank.OnGained += OnGained;
            P(panel, "OnMeltClicked");
            await UniTask.Delay(700, DelayType.Realtime);
            bank.OnGained -= OnGained;
            bool after = melt != null && melt.gameObject.activeInHierarchy;
            sb.AppendLine($"  누른 뒤: 원석 {bank.RuneOre}(목표 {cost}) · 재료 {bank.EnhanceMaterial}(목표 {30 - need}) · 「얻은 양」 이벤트 {gained} · [녹이기] 보임 {after} · 정제 가능 {svc.CanAfford}");
            await ShotAsync("B17_2_RefMelt_after");
            sb.AppendLine($"  CHECK 녹이기: {shown && bank.RuneOre == cost && bank.EnhanceMaterial == 30 - need && gained == 0 && !after && svc.CanAfford}");

            bank.RestoreRaw(2, 3);   // 재료가 모자라 다 채울 수 없으면 안 보인다
            P(panel, "Refresh");
            await UniTask.Delay(200, DelayType.Realtime);
            bool hidden = melt == null || !melt.gameObject.activeInHierarchy;
            sb.AppendLine($"  CHECK 재료 2 · 원석 3(모자람 {cost - 3}) → [녹이기] 안 보임: {hidden}");
        }
        finally
        {
            bank.OnGained -= OnGained;
            Managers.UI.CloseAllPopupUI();
            bank.RestoreRaw(mat0, ore0);
            await UniTask.Delay(400, DelayType.Realtime);
        }
    }

    // ── ③ 행상 포션 가득 ──
    private static async UniTask PotionAsync(GameRunSession run, StringBuilder sb)
    {
        sb.AppendLine("[3 행상 포션]");
        var ps = run.PlayerState;
        if (ps == null) { sb.AppendLine("  플레이어 상태 없음"); return; }
        GameObject ctrlGo = null;
        int cap0 = ps.PotionCapacity;
        try
        {
            // 포션 상품이 있는 진열을 찾는다(방 시드처럼 고정 시드)
            AbyssPeddlerCatalog.Result stock = null;
            int idx = -1;
            for (int seed = 1; seed <= 200 && idx < 0; seed++)
            {
                stock = AbyssPeddlerCatalog.Build(new System.Random(seed));
                idx = stock.Products.FindIndex(p => p != null && p.Category == ShopProductCategory.Potion);
            }
            if (idx < 0) { sb.AppendLine("  포션 상품이 있는 진열을 못 찾았다"); return; }

            ps.AddTempGold(500);
            ps.AddPotion(cap0);   // 가득
            var shop = await Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>();
            if (shop == null) { sb.AppendLine("  상점 팝업 로드 실패"); return; }
            ctrlGo = new GameObject("~B1017ProbeShop");
            var sc = ctrlGo.AddComponent<ShopRoomController>();
            typeof(ShopRoomController).GetField("_run", Any)?.SetValue(sc, run);
            typeof(ShopRoomController).GetField("_peddler", Any)?.SetValue(sc, stock);
            shop.Bind(sc);
            await UniTask.Delay(900, DelayType.Realtime);
            P(shop, "Select", idx);
            await UniTask.Delay(300, DelayType.Realtime);

            var btn   = Get(shop, "_buyBtn") as Button;
            var label = Get(shop, "_buyLabel") as TMP_Text;
            int gold0 = ps.TempGold, pot0 = ps.PotionCount;
            sb.AppendLine($"  포션 {pot0}/{ps.PotionCapacity} · 「{sc.Products[idx].DisplayName}」 {sc.Products[idx].Price}G: 버튼 「{label?.text}」 누를 수 있음 {btn?.interactable}");
            await ShotAsync("B17_3_ShopPotionFull");
            var r = sc.TryBuyProduct(idx);
            sb.AppendLine($"  가득일 때 사기 → {r} · 골드 {gold0} → {ps.TempGold} · 포션 {ps.PotionCount}");
            sb.AppendLine($"  CHECK 가득이면 못 산다: {label?.text == "포션 가득" && btn != null && !btn.interactable && r == ShopPurchaseResult.Unavailable && ps.TempGold == gold0}");

            ps.SetPotionCapacity(cap0 + 2);   // 칸을 늘리면 다시 살 수 있다
            P(shop, "Select", idx);
            await UniTask.Delay(200, DelayType.Realtime);
            string free = label?.text;
            var r2 = sc.TryBuyProduct(idx);
            sb.AppendLine($"  칸 {ps.PotionCapacity}: 버튼 「{free}」 → {r2} · 골드 {gold0} → {ps.TempGold} · 포션 {pot0} → {ps.PotionCount}");
            sb.AppendLine($"  CHECK 빈 칸이면 산다: {r2 == ShopPurchaseResult.Success && ps.PotionCount > pot0 && ps.TempGold < gold0}");
        }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            if (ctrlGo != null) Object.Destroy(ctrlGo);
            ps.SetPotionCapacity(cap0);
            await UniTask.Delay(400, DelayType.Realtime);
        }
    }

    // ── 작은 도우미 ──
    private static object Get(object o, string field) => o?.GetType().GetField(field, Any)?.GetValue(o);
    private static void P(object o, string method, params object[] args) => o?.GetType().GetMethod(method, Any)?.Invoke(o, args.Length > 0 ? args : null);
    private static string Rect(Graphic g)
    {
        if (g == null) return "-";
        var rt = g.rectTransform;
        return $"{rt.anchoredPosition.x:0},{rt.anchoredPosition.y:0} {rt.sizeDelta.x:0}×{rt.sizeDelta.y:0}";
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
