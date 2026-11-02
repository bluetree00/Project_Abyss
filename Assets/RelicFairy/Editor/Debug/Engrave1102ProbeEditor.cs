#if UNITY_EDITOR
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
/// [촬영 · 플레이 중] 11-02 각인 카드(사용자 「무기 스킬 각인 — 설명이 제대로 필요 · 있는 리소스로 표현」) — 진화 직후 각인 고르기 창을
/// 열린 뒤 0.55 · 1.05 · 1.75초에 한 장씩(카드 그림이 되풀이되는 중간 · 끝) · 한국어와 영어 · 카드 글(넘침 표시).
/// 길은 10-30 재련 글 점검과 같다(테스트 허브 → 자동 플레이 첫 방 → 재련소 창 → 무형검 재련 3회 → 진화(칼리번) → 각인 창).
/// 결과 Temp/engrave1102.txt · Temp/ui_shots/EG1102_*.png · 로그 「[EG1102] 끝」. 실측 뒤 플레이를 멈출 것.
/// </summary>
public static class Engrave1102ProbeEditor
{
    private const string Tag      = "[EG1102]";
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string OutTxt   = "Temp/engrave1102.txt";
    private const string ShotDir  = "Temp/ui_shots";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly float[] ShotTimes = { 0.55f, 1.05f, 1.75f };
    private static bool s_armed;

    [MenuItem("RelicFairy/Debug/11-02 각인 카드 그림 촬영 (테스트 허브, 플레이 중)")]
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
        var sb = new StringBuilder("11-02 각인 카드 — 그림 · 글\n");
        Directory.CreateDirectory(ShotDir);
        var go   = new GameObject("~Eg1102Probe");
        var lang = Loc.Current;
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            foreach (var mo in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (mo != null && !mo.IsDead) mo.gameObject.SetActive(false);
            await Wait(1.5f, ct);

            var wm  = p.WeaponManager;
            var run = GameRunBootstrapper.Instance.Run;
            var w0  = wm.Weapon0Data;
            w0.enhanceLevel = 9; w0.highestEnhanceReached = 9; w0.augments = ""; w0.reforgeLog = ""; w0.engravings = ""; w0.RecomputeEnhancedStats();
            run.FuelBank.Add(FuelKind.EnhanceMaterial, 300);
            typeof(UILayoutRuntimeProbeEditor).GetMethod("HideTestHubGui", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);

            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(7), null);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await Wait(0.9f, ct);
            F<Button[]>(panel, "_chooseCard")?[0]?.onClick.Invoke();
            Call(panel, "RefreshAll");
            await Wait(0.5f, ct);

            // 진화(첫 분기 = 칼리번) → 각인 창
            Call(panel, "ShowEvolvePanel");
            await Wait(0.8f, ct);
            Call(panel, "OnBranchClicked", 0);
            float te = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - te < 10f && !(F<GameObject>(panel, "_engravePanel")?.activeSelf ?? false))
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            var eg = F<GameObject>(panel, "_engravePanel");
            if (eg == null || !eg.activeSelf) { sb.AppendLine($"각인 창이 안 열렸다(형 {wm.Weapon0Data?.HasForm} · 무기 {wm.Weapon0Data?.displayName})"); return; }

            await ShootAsync(sb, eg, "ko", Time.realtimeSinceStartup, ct);

            // 영어 — 창을 다시 띄워 글을 지금 언어로 짓는다(그림도 처음부터)
            Loc.SetLanguage(Loc.Language.English);
            eg.SetActive(false);
            Call(panel, "TryShowEngraveOffer");
            await ShootAsync(sb, eg, "en", Time.realtimeSinceStartup, ct);
            Loc.SetLanguage(lang);

            Call(panel, "OnEngravePicked", 0);
            await Wait(0.6f, ct);
            sb.AppendLine($"고른 뒤 각인 {wm.Weapon0Data?.engravings} · 창 닫힘 {!eg.activeSelf}");

            // 11-02 전설 승급 모습 — 같은 검 그림 금빛 · 달굼 빛 금빛
            await Shot("EG1102_before_legend");
            wm.Weapon0Data.legendId = "excalibur";
            Call(panel, "RefreshAll");
            await Wait(1.2f, ct);
            await Shot("EG1102_legend");
            sb.AppendLine("전설 승급 모습 — 사진 EG1102_before_legend · EG1102_legend");
        }
        catch (System.OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Loc.SetLanguage(lang);
            if (go != null) Object.Destroy(go);
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    /// <summary>열린 때(<paramref name="t0"/>)부터 정한 시각마다 한 장 + 카드 글(넘침 표시) · 그림 칸 크기.</summary>
    private static async UniTask ShootAsync(StringBuilder sb, GameObject eg, string lang, float t0, CancellationToken ct)
    {
        sb.AppendLine($"\n## {lang}");
        foreach (var t in eg.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.gameObject.activeInHierarchy || t.color.a < 0.05f) continue;
            t.ForceMeshUpdate();
            string s = t.GetParsedText().Replace("\n", " ⏎ ").Trim();
            if (s.Length > 0) sb.AppendLine($"  [{t.transform.parent.name}/{t.name}] {s}{(t.isTextOverflowing ? "  ⚠넘침" : "")}");
        }
        foreach (var d in eg.GetComponentsInChildren<MonoBehaviour>(true))   // 이름으로 — 고치기 전(그림 칸 없음)에도 같은 도구로 찍는다
            if (d.GetType().Name == "UI_EngraveDiagram")
                sb.AppendLine($"  [그림] {d.transform.parent.name} 켜짐 {d.gameObject.activeInHierarchy} · 그린 것 {d.transform.childCount}개 · 칸 {((RectTransform)d.transform).rect.size}");
        for (int i = 0; i < ShotTimes.Length; i++)
        {
            while (Time.realtimeSinceStartup - t0 < ShotTimes[i]) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            await Shot($"EG1102_{lang}_{i + 1}");
            sb.AppendLine($"  사진 EG1102_{lang}_{i + 1} — 열린 뒤 {Time.realtimeSinceStartup - t0:0.00}초");
        }
    }

    private static async UniTask Shot(string name)
    {
        var runner = Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        await UniTask.WaitForEndOfFrame(runner);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (tex == null) return;
        File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    private static T F<T>(UI_CruciblePanel panel, string name) where T : class
        => typeof(UI_CruciblePanel).GetField(name, Inst)?.GetValue(panel) as T;

    private static void Call(UI_CruciblePanel panel, string method, params object[] args)
        => typeof(UI_CruciblePanel).GetMethod(method, Inst)?.Invoke(panel, args.Length == 0 ? null : args);

    private static UniTask Wait(float s, CancellationToken ct)
        => UniTask.Delay(System.TimeSpan.FromSeconds(s), DelayType.Realtime, PlayerLoopTiming.Update, ct);
}
#endif
