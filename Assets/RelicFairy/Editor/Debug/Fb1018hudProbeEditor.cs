#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-18 빌드 피드백 ⑨ · ⑬ 실측(테스트 허브, 플레이 중) — 가웨인 유물 HUD.
/// ⑨ 「HUD 오른쪽 아래에 정오 텍스트가 계속 깜빡이는 버그」: 원인 = 하루 띠의 붙듦 안내(「새벽을 붙들었다 · 낙일로 정오」)를 SetActive로 숨겨
///    HUD 하드 가드(HudBootstrapper.LateUpdate — 0.2초마다 전투 패널 글자를 켠다)가 켠 채 그 프레임이 그려지고 다음 Update에 꺼지던 것.
///    하드 가드와 같은 때(PostLateUpdate)에 ForceTextsVisible을 직접 불러 깜빡임 0을 잰다.
///    정오 진입 직후 · 1초 · 2초 사진 + 「정오!」 크기 · 글 · 가운데 빛 알파를 매 프레임 기록(진입 때 한 번 커졌다 자리 · 그 뒤 고정).
/// ⑬ 하루 띠 이름 · 조각 수(「여명 1/4」) · 처음 안내 한 줄 · 띠가 게이지 끝과 안 겹침 · 물약 C · E 칸과 안 겹침.
/// 런이 없으면 Ch1 가웨인으로 띄운다(Temp/fb1018_hud_args.txt = 「lancelot」이면 랜슬롯 — 계단 문턱 값 · 「원한 N/5」만 찍는다).
/// 조각(가웨인 g_zenith · g_sundial · g_dawn_to_noon · g_ember_path / 랜슬롯 l_split_oath · l_black_afterimage)을 런에 더한다 — 테스트 런에 남는다.
/// 처음 안내 키(relic_hud_dial_hint_v1)는 지웠다가 다시 1이 된다(에디터 PlayerPrefs).
/// 결과 Temp/fb1018_hud.txt · Temp/ui_shots/FB18_hud_*.png · 로그 「[FB18hud] 끝」. CHECK 줄이 전부 True면 합격.
/// </summary>
public static class Fb1018hudProbeEditor
{
    private const string Tag     = "[FB18hud]";
    private const string ArgsTxt = "Temp/fb1018_hud_args.txt";
    private const string OutTxt  = "Temp/fb1018_hud.txt";
    private const string ShotDir = "Temp/ui_shots";
    private const string HintKey = "relic_hud_dial_hint_v1";
    private const int    GuardEvery = 12;   // 이 프레임마다 하드 가드를 직접 부른다(0.2초 ≈ 12프레임)
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly string[] GawainParts   = { "g_zenith", "g_sundial", "g_dawn_to_noon", "g_ember_path" };   // 여명 1 · 정오 3 · 황혼 1
    private static readonly string[] LancelotParts = { "l_split_oath", "l_black_afterimage" };                       // 광기 10 · 20

    [MenuItem("RelicFairy/Debug/10-18 가웨인 HUD 정오 · 하루 띠 이름 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중(테스트 허브)에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-18 가웨인 HUD 정오 · 하루 띠 이름\n");
        Directory.CreateDirectory(ShotDir);
        ZenithGauge zen = null;
        try
        {
            bool lancelot = File.Exists(ArgsTxt) && File.ReadAllText(ArgsTxt).Trim() == "lancelot";
            var p = await EnsureRunAsync(lancelot ? RelicId.Lancelot : RelicId.Gawain, sb);
            if (p == null) return;
            p.SetInvincible(120f);
            // 대기방 도착 대사가 HUD를 덮고 게임 시간을 멈춘다 — 다 넘긴 뒤 잰다(10-18 첫 실측이 여기서 멈췄다)
            for (int i = 0; i < 12 && Object.FindFirstObjectByType<UI_DialoguePopup>() != null; i++)
            {
                EditorApplication.ExecuteMenuItem("RelicFairy/Debug/서약 발동 실측/대사 전부 넘기기 (플레이 중)");
                await UniTask.Delay(700, DelayType.Realtime);
            }
            sb.AppendLine($"대사 넘김 뒤 · 대사 창 {(Object.FindFirstObjectByType<UI_DialoguePopup>() != null ? "남음" : "없음")} · timeScale {Time.timeScale:0.##}");
            await EnsureHudAsync(sb);
            var hud       = Object.FindFirstObjectByType<CombatPanelView>();
            var presenter = Object.FindFirstObjectByType<RelicMemoryHudPresenter>();
            var view      = presenter != null ? presenter.GetComponent<RelicMemoryHudView>() : null;
            if (hud == null || presenter == null || view == null) { sb.AppendLine($"HUD {hud != null} · 유물 HUD {presenter != null} · View {view != null}"); return; }

            if (p.RelicBehavior is GawainZenithRelic g) { zen = g.Gauge; await GawainAsync(p, g, hud, presenter, view, sb); }
            else if (p.RelicBehavior is LancelotMadnessRelic) await LancelotAsync(p, hud, view, sb);
            else sb.AppendLine($"유물 {p.RelicBehavior?.GetType().Name ?? "없음"} — 건너뜀");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (zen != null) zen.Paused = false;
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    // ── 가웨인 ──
    private static async UniTask GawainAsync(PlayerController p, GawainZenithRelic relic, CombatPanelView hud,
                                             RelicMemoryHudPresenter presenter, RelicMemoryHudView view, StringBuilder sb)
    {
        var zen     = relic.Gauge;
        var loadout = AppBootstrapper.Instance?.Loadout;
        if (zen == null || loadout == null) { sb.AppendLine($"게이지 {zen != null} · 로드아웃 {loadout != null}"); return; }
        foreach (var id in GawainParts) if (!loadout.HasRelicPart(id)) loadout.AddRelicPart(id);
        var hub = GawainMemoryHub.Ensure(p);
        zen.Paused = true;
        zen.Restore(new RelicResourceState { phase = (int)ZenithGauge.ZPhase.Charging, fill = zen.DurationOf(ZenithGauge.ZPhase.Charging) * 0.5f });
        await UniTask.Delay(500, DelayType.Realtime);

        // ⑬ 처음 안내 — 키를 지우고 허브를 다시 묶는다(첫 조각을 집은 순간과 같은 길)
        PlayerPrefs.DeleteKey(HintKey);
        typeof(RelicMemoryHudPresenter).GetMethod("OnGawainHub", Any)?.Invoke(presenter, new object[] { hub });
        await UniTask.Delay(1000, DelayType.Realtime);
        var hint      = Get(view, "_hint") as TMP_Text;
        var hintGroup = Get(view, "_hintGroup") as CanvasGroup;
        string hintText = hint != null ? hint.GetParsedText() : "";
        float  hintA    = hintGroup != null ? hintGroup.alpha : -1f;
        await ShotAsync("FB18_hud_1_hint");
        sb.AppendLine($"[⑬ 처음 안내] 「{hintText}」 · 알파 {hintA:0.00} · 키 {PlayerPrefs.GetInt(HintKey, 0)}");
        sb.AppendLine($"  CHECK 처음 안내가 뜬다 · 키 1: {hintText.Length > 0 && hintA > 0.9f && PlayerPrefs.GetInt(HintKey, 0) == 1}");
        var eSlot = Find(hud.transform, "HUD_ESkile") as RectTransform;
        var cSlot = Find(hud.transform, "HUD_Active_01") as RectTransform;
        var hintRect = TextRect(hint);
        sb.AppendLine($"  CHECK 처음 안내가 E · 물약 칸과 안 겹침: {!Overlaps(hintRect, WorldRect(eSlot)) && !Overlaps(hintRect, WorldRect(cSlot))} (안내 {hintRect} · E {WorldRect(eSlot)} · C {WorldRect(cSlot)})");

        // ⑬ 띠 이름 · 조각 수 · 자리
        var labels = Get(view, "_arcLabel") as TextMeshProUGUI[];
        int[] counts =
        {
            RelicResonance.GawainCount(loadout, RelicPartAnchor.Dawn),
            RelicResonance.GawainCount(loadout, RelicPartAnchor.Noon),
            RelicResonance.GawainCount(loadout, RelicPartAnchor.Dusk),
        };
        string[] names = { "여명", "정오", "황혼" };
        bool namesOk = labels != null && labels.Length == 3, apartOk = namesOk;
        var line = new StringBuilder();
        for (int i = 0; namesOk && i < 3; i++)
        {
            string want = $"{names[i]}\n조각 {Mathf.Min(counts[i], 4)}/4";   // 10-18 두 줄(「여명」 / 「조각 1/4」)
            string got  = labels[i] != null ? labels[i].GetParsedText() : "-";
            namesOk &= got == want;
            line.Append($"「{got}」(기대 「{want}」) ");
            if (i > 0 && labels[i] != null && labels[i - 1] != null) apartOk &= TextRect(labels[i - 1]).xMax < TextRect(labels[i]).xMin;
        }
        sb.AppendLine($"[⑬ 띠 이름] {line}");
        sb.AppendLine($"  CHECK 띠 이름 · 조각 수: {namesOk}");
        sb.AppendLine($"  CHECK 이름끼리 안 겹침: {apartOk}");
        var overlay  = Get(view, "_overlay") as RectTransform;
        var sunGauge = (Get(hud, "_sunRoot") as RectTransform)?.Find("SunGauge") as RectTransform;
        var star     = Get(view, "_star") as Image;
        float gaugeRight = WorldRect(sunGauge).xMax, dialLeft = WorldRect(overlay).xMin;
        float dialRight  = Mathf.Max(WorldRect(overlay).xMax, star != null && star.gameObject.activeInHierarchy ? WorldRect(star.rectTransform).xMax : float.MinValue);
        sb.AppendLine($"  CHECK 하루 띠가 게이지 끝과 안 겹침: {dialLeft > gaugeRight} (게이지 끝 {gaugeRight:0} · 띠 시작 {dialLeft:0})");
        sb.AppendLine($"  CHECK 하루 띠가 물약 C 칸 왼쪽: {cSlot == null || dialRight < WorldRect(cSlot).xMin} (띠 끝 {dialRight:0} · C 왼쪽 {WorldRect(cSlot).xMin:0})");

        // 안내가 걷힐 때까지(6초 · 게임 시간) — 여명 96%에서 이름만 남은 사진.
        // 대기방 대사 등으로 게임 시간이 멈춰 있으면 안내도 그대로라(설계대로) 실시간 9초까지만 기다린다(예전엔 멈춘 채 끝나지 않았다).
        float hintWait = Time.realtimeSinceStartup, gameT0 = Time.time;
        while (Time.realtimeSinceStartup - hintWait < 9f && Time.time - gameT0 < 6.5f)
            await UniTask.Delay(250, DelayType.Realtime);
        if (Time.time - gameT0 < 6.5f) sb.AppendLine($"  (게임 시간이 멈춰 있었다 · timeScale {Time.timeScale:0.##} — 안내 6초는 게임 시간이라 걷히지 않은 채 다음 사진)");
        zen.Restore(new RelicResourceState { phase = (int)ZenithGauge.ZPhase.Charging, fill = zen.DurationOf(ZenithGauge.ZPhase.Charging) * 0.96f });
        await UniTask.Delay(400, DelayType.Realtime);
        hintText = hint != null ? hint.GetParsedText() : "";
        await ShotAsync("FB18_hud_2_labels");
        sb.AppendLine($"  CHECK 6초 뒤 안내가 걷힘(글 비움 · 켜진 채): {hintText.Length == 0 && hint != null && hint.gameObject.activeSelf}");

        // ⑨ 정오 진입 → 2.2초 동안 매 프레임(그리기 직전) 기록. 12프레임마다 하드 가드를 직접 부른다.
        var sunLabel = Get(hud, "_sunLabel") as TMP_Text;
        var glow     = Get(hud, "_wGlowSun") as Image;
        var hold     = Get(view, "_holdLabel") as TMP_Text;
        var guard    = typeof(HudBootstrapper).GetMethod("ForceTextsVisible", Any);
        zen.Paused = false; zen.OpenNoonNow(); zen.Paused = true;
        float t0 = Time.realtimeSinceStartup;
        int frames = 0, guards = 0, holdFlash = 0, labelOff = 0, textChanges = 0;
        float popMax = 0f, lateScaleDev = 0f, glowMin = 9f, glowMax = -9f;
        string lastText = null;
        bool shot0 = false, shot1 = false, shot2 = false;
        while (Time.realtimeSinceStartup - t0 < 2.2f)
        {
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
            float t = Time.realtimeSinceStartup - t0;
            frames++;
            if (guard != null && frames % GuardEvery == 0) { guard.Invoke(null, new object[] { hud.transform }); guards++; }
            if (hold != null && hold.gameObject.activeInHierarchy && hold.GetParsedText().Length > 0 && !zen.IsHoldingDawn) holdFlash++;
            if (sunLabel != null)
            {
                string s = sunLabel.GetParsedText();
                if (!sunLabel.gameObject.activeInHierarchy) labelOff++;
                if (lastText != null && s != lastText) textChanges++;
                lastText = s;
                float sc = sunLabel.rectTransform.localScale.x;
                if (t < 0.3f) popMax = Mathf.Max(popMax, sc);
                else if (t > 0.5f) lateScaleDev = Mathf.Max(lateScaleDev, Mathf.Abs(sc - 1f));
            }
            if (glow != null && t > 0.5f) { glowMin = Mathf.Min(glowMin, glow.color.a); glowMax = Mathf.Max(glowMax, glow.color.a); }
            if (!shot0)                { shot0 = true; await ShotAsync("FB18_hud_3_noon_0s"); }
            else if (!shot1 && t >= 1f) { shot1 = true; await ShotAsync("FB18_hud_4_noon_1s"); }
            else if (!shot2 && t >= 2f) { shot2 = true; await ShotAsync("FB18_hud_5_noon_2s"); }
        }
        sb.AppendLine($"[⑨ 정오 2.2초] 정오 {zen.IsNoon} · 프레임 {frames} · 하드 가드 {guards}번 · 「{lastText}」");
        sb.AppendLine($"  CHECK 붙듦 안내(…낙일로 정오) 깜빡임 0: {holdFlash == 0 && guards > 0} (보인 프레임 {holdFlash} · 안내 켜진 채 {hold != null && hold.gameObject.activeSelf})");
        sb.AppendLine($"  CHECK 「정오!」 진입 때 한 번 커짐(≥ 1.15): {popMax >= 1.15f} (최대 {popMax:0.00})");
        sb.AppendLine($"  CHECK 「정오!」 0.5초 뒤 고정(크기 1 · 글 그대로 · 꺼짐 0): {lateScaleDev < 0.01f && textChanges == 0 && labelOff == 0 && lastText != null && lastText.StartsWith("정오!")} (크기 오차 {lateScaleDev:0.000} · 글 바뀜 {textChanges} · 꺼진 프레임 {labelOff})");
        sb.AppendLine($"  CHECK 정오 가운데 빛 고정(0.5초 뒤 알파 변화 < 0.02): {glowMax - glowMin < 0.02f && glowMin > 0.2f} (알파 {glowMin:0.00}~{glowMax:0.00})");
    }

    // ── 랜슬롯(이름만) ──
    private static async UniTask LancelotAsync(PlayerController p, CombatPanelView hud, RelicMemoryHudView view, StringBuilder sb)
    {
        var loadout = AppBootstrapper.Instance?.Loadout;
        if (loadout == null) { sb.AppendLine("로드아웃 없음"); return; }
        foreach (var id in LancelotParts) if (!loadout.HasRelicPart(id)) loadout.AddRelicPart(id);
        var hub = LancelotMemoryHub.Ensure(p);
        if (hub == null) { sb.AppendLine("랜슬롯 허브 없음"); return; }
        await UniTask.Delay(800, DelayType.Realtime);
        await ShotAsync("FB18_hud_6_lancelot");
        var rungs  = Get(view, "_rungLabel") as TextMeshProUGUI[];
        var grudge = Find(hud.transform, "GrudgeCount")?.GetComponent<TMP_Text>();
        string gt  = grudge != null ? grudge.GetParsedText() : "";
        var line   = new StringBuilder();
        if (rungs != null) foreach (var r in rungs) line.Append($"「{(r != null ? r.GetParsedText() : "-")}」 ");
        bool ok = rungs != null && rungs.Length == 4 && rungs[0] != null && rungs[3] != null
               && rungs[0].GetParsedText() == $"광기 {hub.Rung(1)}" && rungs[3].GetParsedText() == hub.Rung(4).ToString();
        sb.AppendLine($"[랜슬롯] 문턱 값 {line}· 원한 「{gt}」");
        sb.AppendLine($"  CHECK 계단 문턱 값 · 「원한 N/M」: {ok && gt.StartsWith("원한 ")}");
    }

    // ── 런 · HUD 준비 ──
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

    /// <summary>대기방은 HUD를 숨겨 둔다 — 전투 진입과 같은 길로 켠다.</summary>
    private static async UniTask EnsureHudAsync(StringBuilder sb)
    {
        var hud = Object.FindFirstObjectByType<CombatPanelView>();   // 꺼진 것은 못 찾는다
        if (hud != null && hud.isActiveAndEnabled) return;
        var hb = Object.FindFirstObjectByType<HudBootstrapper>();
        if (hb == null) { sb.AppendLine("HUD 부트 없음"); return; }
        sb.AppendLine("HUD가 꺼져 있어(대기방) 전투 진입처럼 켠다");
        await hb.FadeInStartRoomAsync(0f);
        await UniTask.Delay(500, DelayType.Realtime);
    }

    // ── 작은 도우미 ──
    private static object Get(object o, string field) => o?.GetType().GetField(field, Any)?.GetValue(o);

    private static Transform Find(Transform t, string name)
    {
        if (t == null) return null;
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++) { var hit = Find(t.GetChild(i), name); if (hit != null) return hit; }
        return null;
    }

    private static Rect WorldRect(RectTransform rt)
    {
        if (rt == null) return Rect.zero;
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
    }

    /// <summary>글자가 실제로 그려진 칸(월드) — 칸 크기가 아니라 글 폭.</summary>
    private static Rect TextRect(TMP_Text t)
    {
        if (t == null) return Rect.zero;
        t.ForceMeshUpdate();
        var b  = t.textBounds;
        var lo = t.rectTransform.TransformPoint(b.min);
        var hi = t.rectTransform.TransformPoint(b.max);
        return Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
    }

    private static bool Overlaps(Rect a, Rect b) => a.width > 0f && b.width > 0f && a.Overlaps(b);

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
