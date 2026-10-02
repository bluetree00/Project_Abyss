using System;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-01 재련소 「무엇을 벼릴까」 첫 화면 실측 — 카드 숫자가 실제 비용과 같은지, 카드 · 탭 불, 근거리 · 원거리로 들어간 화면.
/// 재료는 잠깐 더했다가 끝나면 원래대로 돌린다. 결과: Temp/crucible_chooser_probe.txt · 화면은 Temp/ui_shots/S1001_Crucible_*.png.
/// </summary>
public static class CrucibleChooserProbeEditor
{
    private const BindingFlags NonPub = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Out = "Temp/crucible_chooser_probe.txt";

    [MenuItem("RelicFairy/UI/10-01 재련소 첫 선택 실측 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            Debug.LogWarning("[CrucibleChooserProbe] 런에 들어간 뒤 실행해야 한다.");
            return;
        }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb   = new StringBuilder();
        var run  = GameRunBootstrapper.Instance.Run;
        var bank = run.FuelBank;
        int before = bank?.EnhanceMaterial ?? 0;
        GameObject go = null;
        Invoke("HideTestHubGui");
        try
        {
            go = new GameObject("~CrucibleChooserProbe");
            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(7), null);
            bank?.Add(FuelKind.EnhanceMaterial, 40);

            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await UniTask.Delay(1200, ignoreTimeScale: true);
            await Shot("S1001_Crucible_Chooser", 0);
            Dump(sb, "첫 화면(재료 +40)", panel, ctrl);

            // 근거리로 들어가기 — 카드 클릭
            Card(panel, 0)?.onClick.Invoke();
            await Shot("S1001_Crucible_Melee", 600);
            sb.AppendLine($"근거리 카드 → 탭 {Field<int>(panel, "_activeTab")} · 선택 화면 {Active(panel)} · 원거리 탭 불 {Glow(panel, "_tabRangedImg")}");

            // 다시 열고 원거리로
            Call(panel, "ShowChooser");
            Call(panel, "RefreshAll");
            Card(panel, 1)?.onClick.Invoke();
            await Shot("S1001_Crucible_Ranged", 600);
            sb.AppendLine($"원거리 카드 → 탭 {Field<int>(panel, "_activeTab")} · 선택 화면 {Active(panel)} · 근거리 탭 불 {Glow(panel, "_tabWeaponImg")}");

            // 재료가 모자랄 때 — 붉은 비용 · 불 꺼짐
            int now = bank?.EnhanceMaterial ?? 0;
            bank?.TrySpend(FuelKind.EnhanceMaterial, now);
            Call(panel, "ShowChooser");
            Call(panel, "RefreshAll");
            await Shot("S1001_Crucible_Chooser_Poor", 600);
            Dump(sb, "첫 화면(재료 0)", panel, ctrl);
            bank?.Add(FuelKind.EnhanceMaterial, now);
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Managers.UI?.CloseAllPopupUI();
            if (go != null) UnityEngine.Object.Destroy(go);
            if (bank != null)
            {
                int diff = (bank.EnhanceMaterial) - before;
                if (diff > 0) bank.TrySpend(FuelKind.EnhanceMaterial, diff);
                else if (diff < 0) bank.Add(FuelKind.EnhanceMaterial, -diff);
                sb.AppendLine($"재료 원복 {before} → {bank.EnhanceMaterial}");
            }
            Invoke("RestoreTestHubGui");
            File.WriteAllText(Out, sb.ToString());
            Debug.Log($"[CrucibleChooserProbe] 끝 → {Out}");
        }
    }

    /// <summary>
    /// 진화 선택 · 연출 — 지금 근접 무기를 첫 진화 조건(강화 단계)까지 올린 뒤 선택 화면 → 첫 카드 고르기 → 벼리기 → 연출 순간별로 찍는다.
    /// ⚠️ 이 테스트 런의 무기는 실제로 진화한다(되돌리지 않음) — 테스트 허브 런에서만.
    /// </summary>
    [MenuItem("RelicFairy/UI/10-01 재련소 진화 실측 (런 중 · 무기가 실제로 진화)")]
    private static void RunEvolve()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            Debug.LogWarning("[CrucibleChooserProbe] 런에 들어간 뒤 실행해야 한다.");
            return;
        }
        RunEvolveAsync().Forget();
    }

    private static async UniTaskVoid RunEvolveAsync()
    {
        var sb  = new StringBuilder();
        var run = GameRunBootstrapper.Instance.Run;
        GameObject go = null;
        Invoke("HideTestHubGui");
        try
        {
            var wm = run.Player.WeaponManager;
            var wd = wm?.CurrentWeaponData;
            if (wm != null && wd?.evolution == null)
            {
                // 진화 갈래가 있는 무기(무형검)가 아니면 0번 칸에 끼운다 — 매 런 시작 무기와 같다.
                var so = AssetDatabase.LoadAssetAtPath<WeaponSO>("Assets/RelicFairy/Weapon/Nameless/Data/T0_Nameless.asset");
                if (so != null) await wm.AcquireWeaponToSlotAsync(WeaponData.FromSO(so), PlayerWeaponManager.Slot0);
                await UniTask.Delay(500, ignoreTimeScale: true);
                wd = wm.CurrentWeaponData;
                sb.AppendLine($"무형검을 0번 칸에 끼움 → 지금 무기 {wd?.displayName}");
            }
            var branches = wd?.evolution != null ? wd.evolution.Branches : null;
            if (branches == null || branches.Count == 0) { sb.AppendLine($"진화 분기 없음 — 지금 무기 {wd?.displayName}"); return; }
            int need = 0;
            foreach (var b in branches) need = Mathf.Max(need, b.requiredEnhanceLevel);
            int before = wd.enhanceLevel;
            if (wd.enhanceLevel < need) { wd.enhanceLevel = need; wd.RecomputeEnhancedStats(); }
            sb.AppendLine($"지금 무기 {wd.displayName} 강화 {before} → {wd.enhanceLevel}(진화 조건)");

            go = new GameObject("~CrucibleEvolveProbe");
            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(7), null);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await UniTask.Delay(800, ignoreTimeScale: true);
            Card(panel, 0)?.onClick.Invoke();            // 첫 화면 → 근거리
            Call(panel, "ShowEvolvePanel");
            await Shot("S1001_Evolve_Choice", 700);
            DumpEvolve(sb, "선택 화면", panel);

            typeof(UI_CruciblePanel).GetMethod("PickEvolveCard", NonPub)?.Invoke(panel, new object[] { 0 });
            await Shot("S1001_Evolve_Picked", 400);
            DumpEvolve(sb, "첫 카드 고름", panel);

            var confirm = Field<Button>(panel, "_evolveConfirm");
            sb.AppendLine($"확정 버튼 열림 {confirm != null && confirm.interactable}");
            confirm?.onClick.Invoke();
            // 망치 두 번(약 1.4초) → 공개(판정 빛 · 새 그림) → 도장 → 각인 제안 순서
            await Shot("S1001_Evolve_Strike", 450);
            await Shot("S1001_Evolve_Reveal", 1050);
            await Shot("S1001_Evolve_Stamp", 300);
            await Shot("S1001_Evolve_Done", 1200);
            var after = wm.CurrentWeaponData;
            sb.AppendLine($"진화 뒤 무기 {after?.displayName} · 강화 {after?.enhanceLevel} · 공격 {after?.baseAttack:0} · 속도 {after?.attackSpeed:0.##}");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Managers.UI?.CloseAllPopupUI();
            if (go != null) UnityEngine.Object.Destroy(go);
            Invoke("RestoreTestHubGui");
            File.WriteAllText("Temp/crucible_evolve_probe.txt", sb.ToString());
            Debug.Log("[CrucibleChooserProbe] 진화 실측 끝 → Temp/crucible_evolve_probe.txt");
        }
    }

    private static void DumpEvolve(StringBuilder sb, string label, UI_CruciblePanel panel)
    {
        sb.AppendLine($"== {label}");
        var content = Field<RectTransform>(panel, "_evolveContent");
        if (content == null) { sb.AppendLine("  진화 화면 없음"); return; }
        foreach (var t in content.GetComponentsInChildren<TMP_Text>(false))
        {
            if (string.IsNullOrEmpty(t.text)) continue;
            var r = t.rectTransform.rect;
            bool over = t.preferredWidth > r.width + 1f || t.isTextOverflowing;
            sb.AppendLine($"  {t.name} {t.fontSize:0}px{(over ? " ★넘침" : "")}: 「{t.GetParsedText()}」");
        }
    }

    private static void Dump(StringBuilder sb, string label, UI_CruciblePanel panel, CrucibleRoomController ctrl)
    {
        sb.AppendLine($"== {label}");
        var chooser = Field<GameObject>(panel, "_chooser");
        if (chooser == null) { sb.AppendLine("  선택 화면 없음"); return; }
        foreach (var t in chooser.GetComponentsInChildren<TMP_Text>(false))
        {
            if (string.IsNullOrEmpty(t.text)) continue;
            var r = t.rectTransform.rect;
            bool over = t.preferredWidth > r.width + 1f || t.isTextOverflowing;
            sb.AppendLine($"  {t.transform.parent.parent.name}/{t.name} {t.fontSize:0}px{(over ? " ★넘침" : "")}: 「{t.GetParsedText()}」");
        }
        sb.AppendLine($"  카드 불: 근거리 {Glow(panel, "_chooseCard", 0)} · 원거리 {Glow(panel, "_chooseCard", 1)}");

        // 실제 값 — 카드 숫자와 대조
        int m = PlayerWeaponManager.Slot0;
        sb.AppendLine($"  실제: 재료 {ctrl.FuelAmount} · 근거리 비용 {ctrl.CostAt(m)} · 성공률 {ctrl.SuccessChanceAt(m) * 100f:0}% · 하락 {ctrl.DropAt(m)} · 최대 {ctrl.MaxAt(m)}");
        var all = Managers.WeaponParts?.All;
        if (all != null)
        {
            var line = new StringBuilder("  실제 파츠 비용:");
            foreach (var p in all) line.Append($" {p.part_name} Lv{RangedPartsState.Current.LevelOf(p.part_id)}={ctrl.PartCostAt(p.part_id)}");
            sb.AppendLine(line.ToString());
        }
    }

    private static Button Card(UI_CruciblePanel panel, int k)
        => (typeof(UI_CruciblePanel).GetField("_chooseCard", NonPub)?.GetValue(panel) as Button[])?[k];

    private static string Active(UI_CruciblePanel panel)
    {
        var c = Field<GameObject>(panel, "_chooser");
        return c == null ? "없음" : c.activeSelf ? "켜짐" : "꺼짐";
    }

    private static string Glow(UI_CruciblePanel panel, string field, int index = -1)
    {
        object v = typeof(UI_CruciblePanel).GetField(field, NonPub)?.GetValue(panel);
        Component c = index >= 0 ? (v as Array)?.GetValue(index) as Component : v as Component;
        if (c == null) return "대상 없음";
        return c.TryGetComponent<UIAffordGlow>(out var g) && g.enabled ? "켜짐" : "꺼짐";
    }

    private static T Field<T>(object o, string name) => (T)typeof(UI_CruciblePanel).GetField(name, NonPub)?.GetValue(o);

    private static void Call(object o, string method) => typeof(UI_CruciblePanel).GetMethod(method, NonPub)?.Invoke(o, null);

    private static UniTask Shot(string name, int settleMs)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, settleMs }) : UniTask.CompletedTask;
    }

    private static void Invoke(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
}
