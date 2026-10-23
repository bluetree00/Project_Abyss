#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-18 게이지 고침 실측(사용자 「게이지 고치고」 · 리서치 4건) — 테스트 허브 Ch1 런.
/// 가웨인: 여명 40% = 청동(금 아님) · 여명 90%(각인) = 금 · 정오 = 흰 금 · 황혼 30% 지남 = 노을 보라로 남은 70%(가짜 가득 없음) · Q 고리도 같은 색 · 채움.
/// 랜슬롯(Temp/fb1018_gauge_args.txt = 「lancelot」): 평시 = 빨강 아트 · 외곽 빛 고정(맥동 없음) · 90%부터 옅게 · 광란 = 보라 아트 · 남은 시간만큼 줄어듦.
/// 결과 Temp/fb1018_gauge.txt · Temp/ui_shots/FB18_gauge_*.png · 로그 「[FB18gauge] 끝」.
/// </summary>
public static class Fb1018GaugeProbeEditor
{
    private const string Tag     = "[FB18gauge]";
    private const string ArgsTxt = "Temp/fb1018_gauge_args.txt";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-18 유물 게이지 색 · 황혼 · 광란 남은 시간 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중(테스트 허브)에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-18 유물 게이지 고침\n");
        Directory.CreateDirectory("Temp/ui_shots");
        ZenithGauge zen = null;
        try
        {
            bool lancelot = File.Exists(ArgsTxt) && File.ReadAllText(ArgsTxt).Trim() == "lancelot";
            var p = await EnsureRunAsync(lancelot ? RelicId.Lancelot : RelicId.Gawain, sb);
            if (p == null) return;
            p.SetInvincible(120f);
            for (int i = 0; i < 12 && Object.FindFirstObjectByType<UI_DialoguePopup>() != null; i++)
            {
                EditorApplication.ExecuteMenuItem("RelicFairy/Debug/서약 발동 실측/대사 전부 넘기기 (플레이 중)");
                await UniTask.Delay(700, DelayType.Realtime);
            }
            await EnsureHudAsync(sb);
            var hud = Object.FindFirstObjectByType<CombatPanelView>();
            if (hud == null) { sb.AppendLine("HUD 없음"); return; }

            if (p.RelicBehavior is GawainZenithRelic g) { zen = g.Gauge; await GawainAsync(zen, hud, sb); }
            else if (p.RelicBehavior is LancelotMadnessRelic l) await LancelotAsync(l.Madness, hud, sb);
            else sb.AppendLine($"유물 {p.RelicBehavior?.GetType().Name ?? "없음"} — 건너뜀");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (zen != null) zen.Paused = false;
            File.WriteAllText("Temp/fb1018_gauge.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    // ── 가웨인 ──
    private static async UniTask GawainAsync(ZenithGauge zen, CombatPanelView hud, StringBuilder sb)
    {
        if (zen == null) { sb.AppendLine("게이지 없음"); return; }
        zen.Paused = true;
        var gold = UITheme.Thin.Gold;

        var dawn = await PhaseAsync(zen, hud, ZenithGauge.ZPhase.Charging, 0.4f, "FB18_gauge_g1_dawn40", sb);
        bool dawnOk = !Near(dawn.col, gold) && dawn.col.r > dawn.col.g && Mathf.Abs(dawn.fill - 0.4f) < 0.03f && Near(dawn.ring, dawn.col) && Mathf.Abs(dawn.ringFill - 0.4f) < 0.03f;
        sb.AppendLine($"  CHECK 여명 40% = 청동(금 아님) · 고리 같음: {dawnOk}");

        var mark = await PhaseAsync(zen, hud, ZenithGauge.ZPhase.Charging, 0.9f, "FB18_gauge_g2_mark90", sb);
        sb.AppendLine($"  CHECK 여명 90%(각인) = 금: {Near(mark.col, gold) && zen.IsMarkReady}");

        var noon = await PhaseAsync(zen, hud, ZenithGauge.ZPhase.Noon, 0.5f, "FB18_gauge_g3_noon50", sb);
        sb.AppendLine($"  CHECK 정오 = 흰 금 · 고리 가득: {noon.col.b > 0.6f && noon.col.r > 0.95f && noon.ringFill > 0.99f}");

        var dusk = await PhaseAsync(zen, hud, ZenithGauge.ZPhase.Cooldown, 0.3f, "FB18_gauge_g4_dusk30", sb);
        bool duskOk = Mathf.Abs(dusk.fill - 0.7f) < 0.03f && dusk.col.a < 0.6f && dusk.col.b > dusk.col.g && Near(dusk.ring, dusk.col);
        sb.AppendLine($"  CHECK 황혼 30% 지남 = 노을 보라 남은 70%(금 아님): {duskOk}");

        var duskEnd = await PhaseAsync(zen, hud, ZenithGauge.ZPhase.Cooldown, 0.97f, "FB18_gauge_g5_dusk97", sb);
        sb.AppendLine($"  CHECK 황혼 끝 = 거의 빔(가짜 가득 없음): {duskEnd.fill < 0.06f}");
    }

    private static async UniTask<(Color col, float fill, Color ring, float ringFill)> PhaseAsync(
        ZenithGauge zen, CombatPanelView hud, ZenithGauge.ZPhase phase, float progress, string shot, StringBuilder sb)
    {
        zen.Restore(new RelicResourceState { phase = (int)phase, fill = zen.DurationOf(phase) * progress });
        await UniTask.Delay(400, DelayType.Realtime);
        var half = Get(hud, "_wGaugeL") as Image;
        var ring = Get(hud, "_wQRing") as Image;
        var r = (half != null ? half.color : Color.clear, half != null ? half.fillAmount : -1f,
                 ring != null ? ring.color : Color.clear, ring != null ? ring.fillAmount : -1f);
        sb.AppendLine($"[{phase} {progress:P0}] 게이지 색 {Fmt(r.Item1)} · 채움 {r.Item2:0.00} · 고리 색 {Fmt(r.Item3)} · 고리 {r.Item4:0.00}");
        await ShotAsync(shot);
        return r;
    }

    // ── 랜슬롯 ──
    private static async UniTask LancelotAsync(MadnessStack m, CombatPanelView hud, StringBuilder sb)
    {
        if (m == null) { sb.AppendLine("광기 없음"); return; }
        var fillImg = Get(hud, "_relicGaugeFill") as Image;
        var outline = Get(hud, "_relicBarGlow") as Outline;

        m.AddStack(Mathf.RoundToInt(m.MaxStacks * 0.5f) - Mathf.RoundToInt(m.Ratio * m.MaxStacks));
        // 적중 계단 — 오른 직후 칼날 채움 끝에 밝은 칸(0.25초)
        await UniTask.Delay(60, DelayType.Realtime);
        var tick = Get(hud, "_lHitTick") as Image;
        float tickA = tick != null && tick.enabled ? tick.color.a : 0f;
        float tickX = tick != null ? tick.rectTransform.anchoredPosition.x : 0f;
        float barW  = (Get(hud, "_relicBar") as RectTransform)?.rect.width ?? 0f;
        await ShotAsync("FB18_gauge_l0_hit");
        await UniTask.Delay(400, DelayType.Realtime);
        bool tickGone = tick == null || !tick.enabled;
        sb.AppendLine($"[적중 계단] 칸 알파 {tickA:0.00} · 자리 {tickX:0}(기대 {(m.Ratio - 0.5f) * barW:0}) · 0.46초 뒤 꺼짐 {tickGone}");
        sb.AppendLine($"  CHECK 적중 계단 = 오른 직후 칼날 끝에 켜졌다 꺼짐: {tickA > 0.5f && Mathf.Abs(tickX - (m.Ratio - 0.5f) * barW) < 6f && tickGone}");
        await UniTask.Delay(50, DelayType.Realtime);
        float a0 = outline != null ? outline.effectColor.a : -1f;
        await UniTask.Delay(330, DelayType.Realtime);
        float a1 = outline != null ? outline.effectColor.a : -1f;
        string art50 = fillImg?.sprite?.name ?? "-";
        sb.AppendLine($"[광기 {m.Ratio:P0}] 아트 「{art50}」 · 채움 {fillImg?.fillAmount:0.00} · 외곽 빛 {a0:0.00} → {a1:0.00}");
        await ShotAsync("FB18_gauge_l1_half");
        sb.AppendLine($"  CHECK 평시 = 빨강 아트 · 외곽 빛 꺼짐(맥동 없음): {art50.Contains("빨강") && a0 < 0.01f && a1 < 0.01f}");

        m.AddStack(Mathf.CeilToInt(m.MaxStacks * 0.92f) - Mathf.RoundToInt(m.Ratio * m.MaxStacks));
        await UniTask.Delay(250, DelayType.Realtime);
        float b0 = outline != null ? outline.effectColor.a : -1f;
        await UniTask.Delay(330, DelayType.Realtime);
        float b1 = outline != null ? outline.effectColor.a : -1f;
        sb.AppendLine($"[광기 {m.Ratio:P0}] 외곽 빛 {b0:0.00} → {b1:0.00} · 광란 {m.IsFrenzy}");
        await ShotAsync("FB18_gauge_l2_near");
        sb.AppendLine($"  CHECK 90% 이상 = 외곽 빛 고정(두 번 잰 값이 같음 · 켜짐): {b0 > 0.2f && Mathf.Abs(b0 - b1) < 0.01f}");

        if (!m.IsFrenzy) m.EnterFrenzy(6f);
        // 광란 점화 — 0.4초 동안 보라가 칼끝 → 칼자루로(채움 원점 오른쪽 · 밑에 붉은 칼 · 칼끝 불씨)
        await UniTask.Delay(150, DelayType.Realtime);
        var under = Get(hud, "_lIgniteUnder") as Image;
        var spark = Get(hud, "_lSpark") as Image;
        int   originMid = fillImg != null ? fillImg.fillOrigin : -1;
        float amountMid = fillImg != null ? fillImg.fillAmount : -1f;
        bool  underMid  = under != null && under.enabled;
        float sparkMid  = spark != null ? spark.color.a : 0f;
        await ShotAsync("FB18_gauge_l2b_ignite");
        await UniTask.Delay(600, DelayType.Realtime);
        int  originEnd = fillImg != null ? fillImg.fillOrigin : -1;
        bool underEnd  = under != null && under.enabled;
        sb.AppendLine($"[광란 점화 0.15초] 원점 {originMid}(1 = 오른쪽) · 채움 {amountMid:0.00} · 밑 붉은 칼 {underMid} · 불씨 {sparkMid:0.00} → [0.75초] 원점 {originEnd} · 밑 {underEnd}");
        sb.AppendLine($"  CHECK 광란 점화 = 칼끝에서 번지고 0.4초 뒤 제자리: {originMid == 1 && amountMid > 0.2f && amountMid < 0.95f && underMid && sparkMid > 0.3f && originEnd == 0 && !underEnd}");
        await UniTask.Delay(1250, DelayType.Realtime);
        float want = m.FrenzyRemaining01;
        string artF = fillImg?.sprite?.name ?? "-";
        float c0 = outline != null ? outline.effectColor.a : -1f;
        await UniTask.Delay(330, DelayType.Realtime);
        float c1 = outline != null ? outline.effectColor.a : -1f;
        sb.AppendLine($"[광란 남은 {m.FrenzyRemaining:0.0}초] 아트 「{artF}」 · 채움 {fillImg?.fillAmount:0.00}(남은 비율 {want:0.00}) · 외곽 빛 {c0:0.00} → {c1:0.00}");
        await ShotAsync("FB18_gauge_l3_frenzy");
        bool frenzyOk = artF.Contains("보라") && fillImg != null && fillImg.fillAmount < 0.95f && fillImg.fillAmount > 0.3f && Mathf.Abs(c0 - c1) < 0.01f;
        sb.AppendLine($"  CHECK 광란 = 보라 아트 · 남은 시간만큼 줄어듦 · 빛 고정: {frenzyOk}");
        var label = Get(hud, "_relicBarLabel") as TMPro.TMP_Text;
        string lt = label != null ? label.GetParsedText() : "-";
        string want2 = $"광란 {m.FrenzyRemaining:0.0}초";
        sb.AppendLine($"  글 「{lt}」 · 남은 시간 「{want2}」");
        sb.AppendLine($"  CHECK 「광란 N.N초」가 흐른다(진입 값에 안 멈춤): {lt != "광란 6.0초" && lt.StartsWith("광란 ")}");
    }

    // ── 도우미 ──
    private static async UniTask<PlayerController> EnsureRunAsync(RelicId relic, StringBuilder sb)
    {
        var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher != null && GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            if (typeof(TestHubLauncher).GetField("relics", Any)?.GetValue(launcher) is RelicClassSO[] list)
                for (int i = 0; i < list.Length; i++)
                    if (list[i] != null && list[i].Id == relic) { typeof(TestHubLauncher).GetField("_relicIndex", Any)?.SetValue(launcher, i); break; }
            launcher.SelectChapter(1);
            typeof(TestHubLauncher).GetField("_bossApproach", Any)?.SetValue(launcher, false);
            if (!launcher.TryLaunch()) { sb.AppendLine("런 시작 실패"); return null; }
        }
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 60f && GameRunBootstrapper.Instance?.Run?.Player == null)
            await UniTask.Delay(500, DelayType.Realtime);
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        if (p == null) { sb.AppendLine("런이 시작되지 않음(60초)"); return null; }
        await UniTask.Delay(3000, DelayType.Realtime);
        sb.AppendLine($"유물 {p.RelicBehavior?.GetType().Name ?? "없음"}");
        return p;
    }

    private static async UniTask EnsureHudAsync(StringBuilder sb)
    {
        var hud = Object.FindFirstObjectByType<CombatPanelView>();
        if (hud != null && hud.isActiveAndEnabled) return;
        var hb = Object.FindFirstObjectByType<HudBootstrapper>();
        if (hb == null) { sb.AppendLine("HUD 부트 없음"); return; }
        await hb.FadeInStartRoomAsync(0f);
        await UniTask.Delay(500, DelayType.Realtime);
    }

    private static object Get(object o, string field) => o?.GetType().GetField(field, Any)?.GetValue(o);
    private static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;
    private static string Fmt(Color c) => $"({c.r:0.00},{c.g:0.00},{c.b:0.00},{c.a:0.00})";

    private static async UniTask ShotAsync(string name)
    {
        ScreenCapture.CaptureScreenshot($"Temp/ui_shots/{name}.png");
        await UniTask.Delay(250, DelayType.Realtime);
    }
}
#endif
