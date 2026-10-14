#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-08 명조형 HUD 실측(런 중 · 전투방) — 쉬는 HUD · E 재사용 대기(소수) · 정오(가득 — 게이지가 모양을 바꿈) · Q 사용 직후(빛 줄기)를 찍고 배치를 잰다.
/// 확인: 체력바 얇게 가운데 · 유물 게이지가 체력 위 가운데 · 무기가 체력 왼쪽 · Q가 원형(원 테) · Q 게이지 고리 · E 남은 초 소수 한 자리 ·
/// 게이지 가운데에서 양옆 채움 · 정오에 게이지가 굵어지고 빛이 켜짐. 결과: Temp/ui_shots/WU_*.png · Temp/wuwa_hud_probe.txt · 로그 「[WuwaHud] 끝」.
/// </summary>
public static class WuwaHudProbeEditor
{
    [MenuItem("RelicFairy/Debug/10-08 명조형 HUD 실측 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) return;
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("명조형 HUD 실측\n");
        Directory.CreateDirectory("Temp/ui_shots");
        var zen = Object.FindFirstObjectByType<ZenithGauge>();
        try
        {
            var hud = Object.FindFirstObjectByType<CombatPanelView>();
            if (hud == null) { sb.AppendLine("HUD 없음"); return; }
            GameRunBootstrapper.Instance?.Run?.Player?.SetInvincible(60f);
            if (zen != null)
            {
                // 충전 62%로 맞춘다 — 정오 중에 시작하면 「충전 → 정오」 변화를 못 잰다(1차 실측)
                var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                float charge = (float)typeof(ZenithGauge).GetField("_chargeTime", bf).GetValue(zen);
                typeof(ZenithGauge).GetField("_phase", bf).SetValue(zen, ZenithGauge.ZPhase.Charging);
                typeof(ZenithGauge).GetField("_timer", bf).SetValue(zen, charge * 0.62f);
                zen.Paused = true;
            }
            await Shot("WU_0_rest", 300);

            var root = hud.transform;
            var canvasRt = (RectTransform)root;
            float W = canvasRt.rect.width;

            // 배치
            var hpBar = Find(root, "HpBar") as RectTransform;
            var gauge = (Find(root, "RelicSunGauge") as RectTransform) is { } s && s.gameObject.activeInHierarchy ? s : Find(root, "RelicIdentityBar") as RectTransform;
            var w1 = Find(root, "Weapon_01") as RectTransform;
            var q  = Find(root, "HUD_QSkile") as RectTransform;
            sb.AppendLine($"  CHECK 체력바 얇게(높이 ≤ 10 — 10-11 위계 6 → 10 · 폭 560~640): {hpBar != null && hpBar.rect.height <= 10f && hpBar.rect.width >= 560f && hpBar.rect.width <= 640f} ({hpBar?.rect.width:0}×{hpBar?.rect.height:0})");
            sb.AppendLine($"  CHECK 유물 게이지 = 체력 위 가운데: {gauge != null && hpBar != null && gauge.position.y > hpBar.position.y && Mathf.Abs(Local(canvasRt, gauge).x) < 4f} ({(gauge != null ? Local(canvasRt, gauge) : Vector2.zero)})");
            sb.AppendLine($"  CHECK 무기 = 체력 왼쪽: {w1 != null && hpBar != null && Local(canvasRt, w1).x < Local(canvasRt, hpBar).x - 300f && Local(canvasRt, w1).x > -W * 0.35f} ({(w1 != null ? Local(canvasRt, w1) : Vector2.zero)})");
            var qFrame = q != null ? q.Find("SkinFrame")?.GetComponent<Image>() : null;
            sb.AppendLine($"  CHECK Q 원형(원 테): {qFrame != null && qFrame.sprite != null && qFrame.sprite.name.StartsWith("ThinR")} ({qFrame?.sprite?.name})");
            var qRing = q != null ? Find(q, "QGaugeRing")?.GetComponent<Image>() : null;
            var res = hud.GetType().GetField("_relicResource", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(hud) as IRelicResource;
            float fill = res != null ? (res.IsSkillReady ? 1f : res.Fill) : -1f;
            sb.AppendLine($"  CHECK Q 게이지 고리 = 유물 게이지: {qRing != null && Mathf.Abs(qRing.fillAmount - fill) < 0.05f} (고리 {qRing?.fillAmount:0.00} · 게이지 {fill:0.00})");
            var half = gauge != null ? Find(gauge, "FillL")?.GetComponent<Image>() : null;
            sb.AppendLine($"  CHECK 게이지 가운데에서 양옆(반쪽 둘): {half != null && Find(gauge, "FillR") != null} ({half?.fillAmount:0.00})");

            // E 재사용 대기 — 남은 초 소수 한 자리
            hud.SetSkillCooldown(SkillType.E, 8.24f, 12f);
            await UniTask.Delay(80, ignoreTimeScale: true);
            var eTxt = Find(root, "HUD_ESkile")?.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "CooldownText");
            sb.AppendLine($"  CHECK E 남은 초 「8.2」: {eTxt != null && eTxt.GetParsedText() == "8.2"} (「{eTxt?.GetParsedText()}」)");
            await Shot("WU_1_cooldown", 60);
            hud.SetSkillCooldown(SkillType.E, 0f, 12f);

            // 정오 — 게이지가 모양을 바꾼다
            float thinH = gauge != null && Find(gauge, "SunGauge") is RectTransform g0 ? g0.rect.height : -1f;
            if (zen != null) { zen.Paused = false; zen.OpenNoonNow(); zen.Paused = true; }
            await UniTask.Delay(120, ignoreTimeScale: true);
            await Shot("WU_2_noon_enter", 0);
            await UniTask.Delay(500, ignoreTimeScale: true);
            await Shot("WU_3_noon", 0);
            float noonH = gauge != null && Find(gauge, "SunGauge") is RectTransform g1 ? g1.rect.height : -1f;
            var glow = gauge != null ? Find(gauge, "GaugeGlow")?.GetComponent<Image>() : null;
            sb.AppendLine($"  CHECK 정오 — 게이지 굵어짐 · 빛 켜짐: {noonH > thinH && glow != null && glow.color.a > 0.2f} (두께 {thinH:0} → {noonH:0} · 빛 {glow?.color.a:0.00})");

            // Q 사용 직후 — 빛 줄기
            hud.SetSkillCooldown(SkillType.Q, 12f, 15f);
            await Shot("WU_4_q_used", 120);
            var streak = Find(root, "QStreak")?.GetComponent<Image>();
            sb.AppendLine($"  CHECK Q 사용 — 빛 줄기: {streak != null} ");
            hud.SetSkillCooldown(SkillType.Q, 0f, 15f);

            // 보스바 — 보스 모드로 잠깐 바꿔 찍는다(실제 보스 없이 Init · SetHP)
            var presenter = Object.FindFirstObjectByType<HudPresenter>();
            var bp = Object.FindFirstObjectByType<BossPanelView>(FindObjectsInactive.Include);
            if (presenter != null && bp != null)
            {
                presenter.SetMode(HUDIds.Mode.Boss);
                // 보스가 붙지 않은 전투방이라 보스 칸이 꺼진 채다 — 찍는 동안만 조상까지 켠다(1차 실측: 화면에 안 떴다)
                var opened = new System.Collections.Generic.List<GameObject>();
                for (var t = bp.transform; t != null && t != hud.transform.root; t = t.parent)
                    if (!t.gameObject.activeSelf) { t.gameObject.SetActive(true); opened.Add(t.gameObject); }
                bp.Init(1000, "검은 숲의 수호자");
                bp.SetHP(620, 1000);
                await Shot("WU_5_boss", 80);
                // 진단 — 2차 실측에서 보스바가 화면에 안 떴다: 켜짐 · 화면 위치 · 그룹 알파
                var corners = new Vector3[4]; ((RectTransform)bp.transform).GetWorldCorners(corners);
                var alphas = new StringBuilder();
                foreach (var cg in bp.GetComponentsInParent<CanvasGroup>(true)) alphas.Append(cg.name).Append(' ').Append(cg.alpha.ToString("0.00")).Append(' ');
                sb.AppendLine($"  보스바 진단: 켜짐 {bp.gameObject.activeInHierarchy} · 화면 ({corners[0].x:0},{corners[0].y:0})~({corners[2].x:0},{corners[2].y:0}) · 그룹 알파 {alphas}");
                foreach (var g in opened) g.SetActive(false);
                var frame = bp.transform.Find("BossBarFrame")?.GetComponent<Image>();
                var fillArea = bp.GetComponentInChildren<Slider>(true)?.fillRect?.parent as RectTransform;
                sb.AppendLine($"  CHECK 보스바 — 테 없이 얇은 한 줄(≤ 10px): {(frame == null || !frame.enabled) && fillArea != null && fillArea.rect.height <= 10f} (높이 {fillArea?.rect.height:0.0})");
                presenter.SetMode(HUDIds.Mode.Combat);
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (zen != null) zen.Paused = false;
            File.WriteAllText("Temp/wuwa_hud_probe.txt", sb.ToString());
            Debug.Log("[WuwaHud] 끝\n" + sb);
        }
    }

    private static Vector2 Local(RectTransform canvas, RectTransform rt)
    {
        var c = new Vector3[4]; rt.GetWorldCorners(c);
        Vector2 center = (c[0] + c[2]) * 0.5f;
        return (Vector2)canvas.InverseTransformPoint(center) - canvas.rect.center;
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
        if (waitMs > 0) await UniTask.Delay(waitMs, ignoreTimeScale: true);
        ScreenCapture.CaptureScreenshot($"Temp/ui_shots/{name}.png");
        await UniTask.Yield();
    }
}
#endif
