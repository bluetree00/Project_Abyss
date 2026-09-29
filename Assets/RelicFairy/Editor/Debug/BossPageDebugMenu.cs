using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 2페이지 보스 검증 도구(에디터 전용) — 이야기 오버라이드(Test Run/Story/Override - Liberated · Nightmare Mode)와 함께 쓴다.
///   · Jump To Page 2 Boundary : 보스 체력을 2페이지 경계 바로 위로 — 다음 한 방에 전환 연출이 나온다.
///   · Jump To Signature       : 2페이지에서 체력을 간판 경계(2페이지 50%) 바로 위로.
///   · Log Pages               : 페이지 · 체력 · 전환/간판 여부.
/// 시뮬(Fight Sim)로 긴 1페이지를 다 깎지 않고 2페이지 패턴을 재기 위한 것이다.
/// </summary>
public static class BossPageDebugMenu
{
    private const string Root = "RelicFairy/Boss/Page2/";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem(Root + "Jump To Page 2 Boundary (Play)")]
    public static void JumpToPage2()
    {
        var boss = FindPagedBoss(out var pages);
        if (boss == null) return;
        if (pages.IsPage2) { Debug.Log("[Page2Debug] 이미 2페이지"); return; }
        SetHp(boss, pages.Page2Hp + Mathf.Max(20, Mathf.RoundToInt(boss.EffectiveMaxHp * 0.01f)));
        Debug.Log($"[Page2Debug] 2페이지 경계로 — HP {boss.CurrentHp}/{boss.EffectiveMaxHp} (경계 {pages.Page2Hp})");
    }

    [MenuItem(Root + "Jump To Signature (Play)")]
    public static void JumpToSignature()
    {
        var boss = FindPagedBoss(out var pages);
        if (boss == null) return;
        if (!pages.IsPage2) { Debug.LogWarning("[Page2Debug] 2페이지가 아니다"); return; }
        SetHp(boss, Mathf.CeilToInt(pages.Page2Hp * BossPages.SignatureAt) + 20);
        Debug.Log($"[Page2Debug] 간판 경계로 — HP {boss.CurrentHp}/{boss.EffectiveMaxHp}");
    }

    [MenuItem(Root + "Force Late - Skip Signature (Play)")]
    public static void ForceLate()
    {
        var boss = FindPagedBoss(out var pages);
        if (boss == null) return;
        if (!pages.IsPage2) { Debug.LogWarning("[Page2Debug] 2페이지가 아니다"); return; }
        pages.MarkSignatureDone();
        SetHp(boss, pages.Page2Hp);
        Debug.Log($"[Page2Debug] 후반으로 — 간판 건너뜀 · HP {boss.CurrentHp}/{boss.EffectiveMaxHp} (연계기 · 쉬는 시간 −25% 확인용)");
        BossCombatLog.Note("[후반] 강제 — 간판 건너뜀");
    }

    [MenuItem(Root + "Log Pages (Play)")]
    public static void LogPages()
    {
        var boss = FindPagedBoss(out var pages);
        if (boss == null) return;
        Debug.Log($"[Page2Debug] {boss.name} · 켜짐 {pages.Enabled} · 페이지 {pages.Page} · 전환 중 {pages.Transitioning} · 간판 {pages.SignatureDone} · " +
                  $"HP {boss.CurrentHp}/{boss.EffectiveMaxHp} · 2페이지 몫 {pages.Page2Hp} · 전환 {pages.TransitionDue(boss.CurrentHp)} · 간판 때 {pages.SignatureDue(boss.CurrentHp)}");
    }

    private static MonsterBase FindPagedBoss(out BossPages pages)
    {
        pages = null;
        if (!Application.isPlaying) { Debug.LogWarning("[Page2Debug] 플레이 모드에서만."); return null; }
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
        {
            if (m is not IPagedBoss paged || paged.Pages == null || m.IsDead) continue;
            pages = paged.Pages;
            if (!pages.Enabled) { Debug.LogWarning($"[Page2Debug] {m.name}: 2페이지 꺼짐(봉인기) — Test Run/Story/Override - Liberated 후 보스방에 다시 들어가라"); return null; }
            return m;
        }
        Debug.LogWarning("[Page2Debug] 2페이지 보스를 못 찾음");
        return null;
    }

    private static void SetHp(MonsterBase boss, int hp)
    {
        var runtime = typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(boss);
        var field   = runtime?.GetType().GetField("CurrentHp", Inst);
        if (field == null) { Debug.LogWarning("[Page2Debug] 체력 필드를 못 찾음"); return; }
        field.SetValue(runtime, Mathf.Clamp(hp, 1, boss.EffectiveMaxHp));
    }

    // ── 시나리오 장면 캡처(09-29 시기별 페이지 · 시나리오 연출 검증) ──
    // 전투(Fight Sim)는 그대로 두고, 전환이 시작되는 순간 · 보스가 쓰러지는 순간을 기다렸다가 정해 둔 시각에 화면을 찍는다.
    // 결과: 프로젝트 Logs/boss_story/<시각>_<장면>_<시기>/<초>s.png
    private static readonly float[] TransitionShots = { 0.1f, 0.35f, 1.5f, 2.2f, 2.65f, 4.0f, 5.3f, 6.0f };
    private static readonly float[] EndShots        = { 0.3f, 0.7f, 1.0f, 1.4f, 2.0f, 2.8f };
    private static CancellationTokenSource s_captureCts;

    [MenuItem(Root + "Arm Transition Captures (Play)")]
    public static void ArmTransitionCaptures()
    {
        var boss = FindPagedBoss(out var pages);
        if (boss == null) return;
        Arm("transition", () => pages.Transitioning, TransitionShots);
    }

    [MenuItem(Root + "Arm End Captures (Play)")]
    public static void ArmEndCaptures()
    {
        var boss = FindStoryBoss();
        if (boss == null) { Debug.LogWarning("[StoryCapture] 살아 있는 보스가 없다"); return; }
        Arm("end", () => boss == null || boss.IsDead, EndShots);
    }

    /// <summary>체력을 마지막 줄 2%로 — 다음 몇 방에 쓰러진다(봉인기 1줄 · 해방기 2페이지에서).</summary>
    [MenuItem(Root + "Jump To Near Death (Play)")]
    public static void JumpToNearDeath()
    {
        var boss = FindStoryBoss();
        if (boss == null) { Debug.LogWarning("[Page2Debug] 살아 있는 보스가 없다"); return; }
        var pages = (boss as IPagedBoss)?.Pages;
        if (pages != null && pages.Enabled && !pages.IsPage2) { Debug.LogWarning("[Page2Debug] 아직 1페이지 — 2페이지로 넘긴 뒤에"); return; }
        SetHp(boss, Mathf.Max(1, Mathf.RoundToInt(boss.EffectiveMaxHp * 0.02f)));
        Debug.Log($"[Page2Debug] 빈사로 — HP {boss.CurrentHp}/{boss.EffectiveMaxHp}");
    }

    private static MonsterBase FindStoryBoss()
    {
        if (!Application.isPlaying) return null;
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m is IPagedBoss && !m.IsDead) return m;
        return null;
    }

    private static void Arm(string label, Func<bool> trigger, float[] shots)
    {
        s_captureCts?.Cancel();
        s_captureCts?.Dispose();
        s_captureCts = new CancellationTokenSource();
        CaptureSeriesAsync(label, trigger, shots, s_captureCts.Token).Forget();
        Debug.Log($"[StoryCapture] {label} 대기 — 시작되면 {shots.Length}장");
    }

    private static async UniTaskVoid CaptureSeriesAsync(string label, Func<bool> trigger, float[] shots, CancellationToken ct)
    {
        try
        {
            float armed = Time.realtimeSinceStartup;
            while (!trigger())
            {
                if (!Application.isPlaying || Time.realtimeSinceStartup - armed > 600f) { Debug.LogWarning($"[StoryCapture] {label} — 시작을 못 봤다"); return; }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_story", $"{DateTime.Now:MMdd_HHmmss}_{label}_{StoryProgress.Era}");
            Directory.CreateDirectory(dir);
            float t0 = Time.realtimeSinceStartup;
            var runner = UnityEngine.Object.FindFirstObjectByType<Managers>();
            foreach (float at in shots)
            {
                while (Time.realtimeSinceStartup - t0 < at) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                if (runner == null || !Application.isPlaying) break;
                await UniTask.WaitForEndOfFrame(runner, ct);
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(dir, $"{at:0.00}s.png"), tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
            }
            Debug.Log($"[StoryCapture] {label} 완료 — {dir}");
        }
        catch (OperationCanceledException) { }
    }
}
