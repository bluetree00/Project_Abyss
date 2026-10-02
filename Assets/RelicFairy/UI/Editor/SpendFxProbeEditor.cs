#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-02 재화 버튼 연출 실측 — 10-01에 연출을 넣은 세 버튼을 실제로 눌러 순간별로 찍는다
/// (사용자 「소모할 수 있는 재화가 있는 버튼은 연출이 있어야」).
///   ① 재련소 전설 승급: 택1 카드 불(재료 될 때만) → 누르면 망치 두 번 → 금빛 「전설 / 이름」 도장
///   ② 상점 새로고침: 동전이 빠지고 좌판이 다시 깔린다
///   ③ 정제소 돌리기: 누른 순간 원석 숫자가 줄어든다
/// 준비는 승급·서약 실측 · 상점·정제소 연출 실측과 같다. 테스트 런의 무기 · 골드 · 원석이 실제로 바뀐다.
/// 결과: Temp/spend_fx_probe.txt · 화면은 Temp/ui_shots/S1002_*.png
/// </summary>
public static class SpendFxProbeEditor
{
    private const BindingFlags NonPub  = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags SNonPub = BindingFlags.Static | BindingFlags.NonPublic;
    private const string Out = "Temp/spend_fx_probe.txt";

    private static readonly int[] PromoteShotMs = { 150, 500, 900, 1300, 1800, 2600 };
    private static readonly int[] RerollShotMs  = { 60, 200, 400, 700, 1100 };
    private static readonly int[] RefineShotMs  = { 40, 160 };

    [MenuItem("RelicFairy/UI/10-02 재화 버튼 연출 실측 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            Debug.LogWarning("[SpendFxProbe] 런에 들어간 뒤 실행해야 한다.");
            return;
        }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb  = new StringBuilder("재화 버튼 연출 실측\n");
        var run = GameRunBootstrapper.Instance.Run;
        Probe("HideTestHubGui");

        // ① 전설 승급
        GameObject go = null;
        try
        {
            string katana = typeof(UILayoutRuntimeProbeEditor).GetField("EngraveKatanaSO", SNonPub)?.GetValue(null) as string;
            var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(katana);
            await GameRunBootstrapper.EquipWeaponToPlayerAsync(so, run.Player, PlayerWeaponManager.Slot0);
            await UniTask.Delay(800, ignoreTimeScale: true);
            go = new GameObject("~ProbeSpendPromote");
            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(5), null);
            var wd = ctrl.GetSlot(PlayerWeaponManager.Slot0);
            if (wd != null) { wd.enhanceLevel = ctrl.MaxAt(PlayerWeaponManager.Slot0); wd.legendId = ""; wd.RecomputeEnhancedStats(); }
            // 강화를 단번에 올리면 밀린 스킬 각인 선택이 창 위로 먼저 떠 승급 연출을 덮는다(10-02 1차 실측) — 첫 안으로 미리 고른다.
            var offers = new System.Collections.Generic.List<SkillSO.EngravingDef>();
            for (int guard = 0; guard < 8 && SkillEngravingService.TryGetPendingOffer(wd, offers, out var skill, out _); guard++)
                SkillEngravingService.Choose(wd, skill, offers[0].id);
            var legends = ctrl.Legends;
            if (legends == null || legends.Length == 0) { sb.AppendLine("승급: 후보 없음"); }
            else
            {
                run.FuelBank?.Add(FuelKind.EnhanceMaterial, legends[0].promoteCost + 50);
                var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
                panel.Bind(ctrl);
                await UniTask.Delay(1000, ignoreTimeScale: true);
                // 실제 플레이처럼 「근거리 · 무기 강화」로 들어간 뒤 「◆ 진화」 버튼을 누른다 — 첫 선택 화면(불투명 막)이 열린 채
                // 승급 창을 바로 열면 연출이 막 뒤에서 돌아 안 보였다(10-02 2차 실측).
                var cards = typeof(UI_CruciblePanel).GetField("_chooseCard", NonPub)?.GetValue(panel) as UnityEngine.UI.Button[];
                if (cards != null && cards.Length > 0 && cards[0] != null) cards[0].onClick.Invoke();
                await UniTask.Delay(700, ignoreTimeScale: true);
                var evolve = typeof(UI_CruciblePanel).GetField("_evolveBtn", NonPub)?.GetValue(panel) as UnityEngine.UI.Button;
                if (evolve != null) evolve.onClick.Invoke();
                else typeof(UI_CruciblePanel).GetMethod("ShowEvolvePanel", NonPub)?.Invoke(panel, null);
                await UniTask.Delay(600, ignoreTimeScale: true);
                await Shot("S1002_Promote_Pick", 0);
                string id = legends[0].legendId;
                sb.AppendLine($"승급: {wd?.displayName} +{wd?.enhanceLevel} → {legends[0].displayName} (비용 {legends[0].promoteCost})");
                await Series("S1002_Promote", PromoteShotMs, () =>
                {
                    typeof(UI_CruciblePanel).GetMethod("HideLegendPanel", NonPub)?.Invoke(panel, null);
                    typeof(UI_CruciblePanel).GetMethod("OnPromoteClicked", NonPub)?.Invoke(panel, new object[] { id });
                });
                await UniTask.Delay(1200, ignoreTimeScale: true);
                sb.AppendLine($"  결과: 전설 {(string.IsNullOrEmpty(wd?.legendId) ? "없음" : wd.legendId)}");
            }
        }
        catch (Exception e) { sb.AppendLine("승급 예외: " + e); }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            if (go != null) UnityEngine.Object.Destroy(go);
        }
        await UniTask.Delay(600, ignoreTimeScale: true);

        // ② 상점 새로고침
        GameObject shopGo = null;
        try
        {
            run.PlayerState?.AddTempGold(300);
            var shop = await Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>();
            shopGo = new GameObject("~ProbeSpendShop");
            var sc = shopGo.AddComponent<ShopRoomController>();
            typeof(ShopRoomController).GetField("_run", NonPub)?.SetValue(sc, run);
            typeof(ShopRoomController).GetField("_peddler", NonPub)?.SetValue(sc, AbyssPeddlerCatalog.Build(new System.Random(11)));
            typeof(ShopRoomController).GetField("_rerollEnabled", NonPub)?.SetValue(sc, true);
            typeof(ShopRoomController).GetField("_rerollCost", NonPub)?.SetValue(sc, 10);
            shop.Bind(sc);
            await UniTask.Delay(900, ignoreTimeScale: true);
            int g0 = run.PlayerState?.TempGold ?? -1;
            await Shot("S1002_Reroll_Before", 0);
            await Series("S1002_Reroll", RerollShotMs, () => typeof(UI_ShopPanel).GetMethod("OnRerollClicked", NonPub)?.Invoke(shop, null));
            sb.AppendLine($"새로고침: 골드 {g0} → {run.PlayerState?.TempGold ?? -1}");
        }
        catch (Exception e) { sb.AppendLine("새로고침 예외: " + e); }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            if (shopGo != null) UnityEngine.Object.Destroy(shopGo);
        }
        await UniTask.Delay(600, ignoreTimeScale: true);

        // ③ 정제소 돌리기 — 누른 순간의 원석 숫자
        try
        {
            run.FuelBank?.Add(FuelKind.RuneOre, 30);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_RefineryPanel>();
            await UniTask.Delay(900, ignoreTimeScale: true);
            var ore = typeof(UI_RefineryPanel).GetField("_oreText", NonPub)?.GetValue(panel) as TMPro.TMP_Text;
            string before = ore != null ? ore.text : "?";
            await Shot("S1002_Refine_Before", 0);
            await Series("S1002_Refine", RefineShotMs, () => typeof(UI_RefineryPanel).GetMethod("OnSpinClicked", NonPub)?.Invoke(panel, null));
            sb.AppendLine($"정제소: 원석 표시 「{before}」 → 누른 직후 「{(ore != null ? ore.text : "?")}」 · 실제 {run.FuelBank?.RuneOre}");
            await UniTask.Delay(2500, ignoreTimeScale: true);
        }
        catch (Exception e) { sb.AppendLine("정제소 예외: " + e); }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            Probe("RestoreTestHubGui");
            File.WriteAllText(Out, sb.ToString());
            Debug.Log($"[SpendFxProbe] 끝 → {Out}");
        }
    }

    private static UniTask Shot(string name, int settleMs)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", SNonPub);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, settleMs }) : UniTask.CompletedTask;
    }

    private static UniTask Series(string tag, int[] shots, Action start)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotSeriesAsync", SNonPub);
        return m != null ? (UniTask)m.Invoke(null, new object[] { tag, shots, start }) : UniTask.CompletedTask;
    }

    private static void Probe(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, SNonPub)?.Invoke(null, null);
}
#endif
