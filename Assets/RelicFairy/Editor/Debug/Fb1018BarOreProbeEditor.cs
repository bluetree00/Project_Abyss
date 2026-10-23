#if UNITY_EDITOR
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-18 ⑲ 바 옷 · ⑳ 원석 새 그림 확인 — 테스트 허브 Ch1 런에서.
/// ① 체력바: 옷(위아래 금선 · 양 끝 마름모) · 채움 결(BarGrain) — 가득 · 위험(25%) 두 장.
/// ② 보스바: 같은 옷 · 결(실제 보스 없이 Init · SetHP — 10-08 명조형 실측과 같은 길).
/// ③ 원석: HUD 원석 칸 · 정제소 제단 가운데 · 비용 아이콘이 새 그림(512px)인지 + 정제소 사진.
/// 결과 Temp/fb1018_barore.txt · Temp/ui_shots/FB18_bar_*.png · 로그 「[FB18barore] 끝」.
/// </summary>
public static class Fb1018BarOreProbeEditor
{
    private const string Tag = "[FB18barore]";

    [MenuItem("RelicFairy/Debug/10-18 바 옷 · 원석 새 그림 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중(테스트 허브)에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-18 바 옷 · 원석 새 그림\n");
        Directory.CreateDirectory("Temp/ui_shots");
        try
        {
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher != null && GameRunBootstrapper.Instance?.Run?.Player == null)
            {
                launcher.SelectChapter(1);
                launcher.TryLaunch();
            }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 60f && GameRunBootstrapper.Instance?.Run?.Player == null)
                await UniTask.Delay(500, DelayType.Realtime);
            var run = GameRunBootstrapper.Instance?.Run;
            if (run == null) { sb.AppendLine("런이 시작되지 않음"); return; }
            EditorApplication.ExecuteMenuItem("RelicFairy/Debug/서약 발동 실측/대사 전부 넘기기 (플레이 중)");
            await UniTask.Delay(3000, DelayType.Realtime);
            run.Player.SetInvincible(60f);

            var hud = Object.FindFirstObjectByType<CombatPanelView>();
            if (hud == null) { sb.AppendLine("HUD 없음"); return; }

            // ① 체력바
            var hpBar = Find(hud.transform, "HpBar") as RectTransform;
            var dress = hpBar != null ? hpBar.Find("BarDress") : null;
            var fill  = hpBar != null ? hpBar.GetComponentInChildren<Slider>(true)?.fillRect?.GetComponent<Image>() : null;
            hud.SetHp(1214, 1260);
            await Shot("FB18_bar_hp_full", 600);
            sb.AppendLine($"체력바 {hpBar?.rect.width:0}×{hpBar?.rect.height:0} · 옷 {dress != null} · 끝 {dress?.Find("CapL") != null}/{dress?.Find("CapR") != null} · 금선 {dress?.Find("LineTop") != null}/{dress?.Find("LineBottom") != null} · 채움 「{fill?.sprite?.name}」");
            sb.AppendLine($"CHECK 체력바 옷 · 결: {dress != null && dress.Find("CapL") != null && dress.Find("LineTop") != null && fill != null && fill.sprite != null && fill.sprite.name == "BarGrain"}");
            hud.SetHp(315, 1260);
            await Shot("FB18_bar_hp_low", 900);
            sb.AppendLine($"위험 25%: 채움 색 {fill?.color} · 결 「{fill?.sprite?.name}」");
            hud.SetHp(1260, 1260);

            // ② 보스바
            var presenter = Object.FindFirstObjectByType<HudPresenter>();
            var bp = Object.FindFirstObjectByType<BossPanelView>(FindObjectsInactive.Include);
            if (presenter != null && bp != null)
            {
                presenter.SetMode(HUDIds.Mode.Boss);
                var opened = new System.Collections.Generic.List<GameObject>();
                for (var t = bp.transform; t != null && t != hud.transform.root; t = t.parent)
                    if (!t.gameObject.activeSelf) { t.gameObject.SetActive(true); opened.Add(t.gameObject); }
                bp.Init(1000, "검은 숲의 수호자");
                bp.SetHP(620, 1000);
                await Shot("FB18_bar_boss", 500);
                var slider = bp.GetComponentInChildren<Slider>(true);
                var bDress = slider != null ? slider.transform.Find("BarDress") : null;
                var bFill  = slider?.fillRect?.GetComponent<Image>();
                sb.AppendLine($"보스바 창 {(slider?.fillRect?.parent as RectTransform)?.rect.height:0.0}px · 옷 {bDress != null} · 채움 「{bFill?.sprite?.name}」");
                sb.AppendLine($"CHECK 보스바 옷 · 결: {bDress != null && bDress.Find("CapR") != null && bFill != null && bFill.sprite != null && bFill.sprite.name == "BarGrain"}");
                foreach (var g in opened) g.SetActive(false);
                presenter.SetMode(HUDIds.Mode.Combat);
            }
            else sb.AppendLine("보스바 없음");

            // ③ 원석
            var orePill = Find(hud.transform.root, "Pill_RuneOre");
            var oreIcon = orePill != null ? Find(orePill, "Icon")?.GetComponent<Image>() : null;
            int tw = oreIcon != null && oreIcon.sprite != null ? oreIcon.sprite.texture.width : -1;
            sb.AppendLine($"HUD 원석 칸 그림 「{oreIcon?.sprite?.name}」 {tw}px");
            run.FuelBank.RestoreRaw(run.FuelBank.EnhanceMaterial, 40);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_RefineryPanel>();
            await UniTask.Delay(1200, DelayType.Realtime);
            await Shot("FB18_bar_refinery", 0);
            var altarOre = panel != null ? Find(panel.transform, "Ore")?.GetComponent<Image>() : null;
            sb.AppendLine($"정제소 제단 원석 「{altarOre?.sprite?.name}」 {(altarOre != null && altarOre.sprite != null ? altarOre.sprite.texture.width : -1)}px");
            sb.AppendLine($"CHECK 원석 새 그림(512px): {tw == 512 && altarOre != null && altarOre.sprite != null && altarOre.sprite.texture.width == 512}");
            Managers.UI.CloseAllPopupUI();
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/fb1018_barore.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    private static Transform Find(Transform t, string name)
    {
        if (t == null) return null;
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++) { var hit = Find(t.GetChild(i), name); if (hit != null) return hit; }
        return null;
    }

    private static async UniTask Shot(string name, int waitMs)
    {
        if (waitMs > 0) await UniTask.Delay(waitMs, DelayType.Realtime);
        ScreenCapture.CaptureScreenshot($"Temp/ui_shots/{name}.png");
        await UniTask.Delay(200, DelayType.Realtime);
    }
}
#endif
