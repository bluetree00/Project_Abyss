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
    private static CancellationTokenSource s_guardCts;   // 아레나 가드 확인(캡처와 따로 — 서로 취소하지 않게)

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

    // ── 아레나 가드 확인(09-30) — 보스가 맵 밖에 서면 안쪽으로 돌아오는가 ──

    /// <summary>보스를 플레이어 반대쪽으로 40 m 밀어 맵 밖에 세운다. 1초 남짓 안에 「[BossArenaGuard] … → 안쪽」 로그와 함께 돌아와야 한다.</summary>
    [MenuItem(Root + "Arena Guard - Push Boss Outside (Play)")]
    public static void PushBossOutside() => PushOutside(kill: false);

    /// <summary>맵 밖으로 민 직후 쓰러뜨린다 — 시체가 벽 너머에 남지 않고 안쪽에서 끝 장면이 나와야 한다.</summary>
    [MenuItem(Root + "Arena Guard - Push Outside And Kill (Play)")]
    public static void PushOutsideAndKill() => PushOutside(kill: true);

    /// <summary>보스가 공중(스폰 높이 +6 m 이상 — 선회 높이)에 오르는 순간 쓰러뜨린다 — 떠 있는 채 남지 않고 바닥으로 떨어져야 한다(화룡).
    /// 낮은 높이(2 m)에선 에이전트가 켜지며 바닥에 붙어 버려 낙하 처리를 재지 못한다.</summary>
    [MenuItem(Root + "Arena Guard - Kill When Airborne (Play)")]
    public static void KillWhenAirborne()
    {
        var boss = FindStoryBoss();
        if (boss == null) { Debug.LogWarning("[Page2Debug] 살아 있는 보스가 없다"); return; }
        var runtime = typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(boss);
        var spawn   = runtime?.GetType().GetField("SpawnPosition", Inst)?.GetValue(runtime);
        float floorY = spawn is Vector3 v ? v.y : boss.transform.position.y;

        KillWhenAirborneAsync(boss, floorY, NewGuardToken()).Forget();
        Debug.Log($"[Page2Debug] 공중에 오르면 쓰러뜨린다 — 바닥 y {floorY:0.0}");
    }

    private static async UniTaskVoid KillWhenAirborneAsync(MonsterBase boss, float floorY, CancellationToken ct)
    {
        try
        {
            float armed = Time.realtimeSinceStartup;
            while (boss != null && !boss.IsDead && boss.transform.position.y < floorY + 6f)
            {
                if (!Application.isPlaying || Time.realtimeSinceStartup - armed > 300f) { Debug.LogWarning("[Page2Debug] 공중에 오르지 않았다"); return; }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            if (boss == null || boss.IsDead) return;
            Vector3 at = boss.transform.position;
            TestHubDebugMenu.ForceKillBoss();
            Debug.Log($"[Page2Debug] 공중 처치 — 위치 {at} (바닥 y {floorY:0.0})");
            await UniTask.Delay(TimeSpan.FromSeconds(1.0), DelayType.Realtime, cancellationToken: ct);
            if (boss != null) Debug.Log($"[Page2Debug] 공중 처치 1초 뒤 — 위치 {boss.transform.position}");
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>보스가 지상에서 쉬는 순간(패턴 없음)을 기다렸다가 맵 밖으로 민다 — 화룡처럼 곧바로 패턴 · 이륙으로 넘어가는 보스용.</summary>
    [MenuItem(Root + "Arena Guard - Push Outside When Idle (Play)")]
    public static void PushOutsideWhenIdle()
    {
        var boss = FindStoryBoss();
        if (boss == null) { Debug.LogWarning("[Page2Debug] 살아 있는 보스가 없다"); return; }
        var runtime = typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(boss);
        var spawn   = runtime?.GetType().GetField("SpawnPosition", Inst)?.GetValue(runtime);
        float floorY = spawn is Vector3 v ? v.y : boss.transform.position.y;
        PushWhenIdleAsync(boss, floorY, NewGuardToken()).Forget();
        Debug.Log("[Page2Debug] 지상에서 쉬는 순간 맵 밖으로 민다");
    }

    private static async UniTaskVoid PushWhenIdleAsync(MonsterBase boss, float floorY, CancellationToken ct)
    {
        try
        {
            float armed = Time.realtimeSinceStartup;
            while (boss != null && !boss.IsDead && (boss.IsInSpecialState || boss.transform.position.y > floorY + 0.5f))
            {
                if (!Application.isPlaying || Time.realtimeSinceStartup - armed > 300f) { Debug.LogWarning("[Page2Debug] 쉬는 순간이 오지 않았다"); return; }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            if (boss == null || boss.IsDead) return;
            PushOutside(kill: false);
        }
        catch (OperationCanceledException) { }
    }

    private static CancellationToken NewGuardToken()
    {
        s_guardCts?.Cancel();
        s_guardCts?.Dispose();
        s_guardCts = new CancellationTokenSource();
        return s_guardCts.Token;
    }

    private static void PushOutside(bool kill)
    {
        var boss = FindStoryBoss();
        if (boss == null) { Debug.LogWarning("[Page2Debug] 살아 있는 보스가 없다"); return; }
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        Vector3 from = boss.transform.position;
        Vector3 away = player != null ? from - player.transform.position : boss.transform.forward;
        away.y = 0f;
        away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;

        var agent = boss.GetComponent<UnityEngine.AI.NavMeshAgent>();
        bool was = agent != null && agent.enabled;
        if (was) agent.enabled = false;
        boss.transform.position = from + away * 40f;
        if (was) agent.enabled = true;
        Debug.Log($"[Page2Debug] 보스를 맵 밖으로 — {from} → {boss.transform.position}{(kill ? " · 곧바로 처치" : "")}");
        if (kill) TestHubDebugMenu.ForceKillBoss();
    }
}
