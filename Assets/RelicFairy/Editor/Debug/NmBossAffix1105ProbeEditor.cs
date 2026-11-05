#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-05 사용자 「악몽은 기본 스탯을 해방기보다 조금만 높게 · 보스도 정예 버프를 보스용으로 무작위 몇 개」 실측(보스방 · 플레이 중 · 악몽 오버라이드).
/// 강제 메뉴로 조합을 정한 뒤 보스방에 들어가 「측정」 — 붙는 때(전투 시작) · 보스바 줄 · 안내 · 가시(6타 → 예고 → 폭발) · 박격(8초) · 수호막(깎임 %) · 속성 저항.
/// 결과 Temp/nmboss1105.txt · Temp/ui_shots/NB1105_*.png · 로그 「[NB1105] 끝」.
/// </summary>
public static class NmBossAffix1105ProbeEditor
{
    private const string Tag  = "[NB1105]";
    private const string Root = "RelicFairy/Debug/11-05 악몽 보스 특성/";
    private const string ShotDir = "Temp/ui_shots";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem(Root + "강제 — 가시 · 박격")]
    private static void ForceActive() => Force(new[] { MonsterTraitKind.Thorns, MonsterTraitKind.Mortar });

    [MenuItem(Root + "강제 — 수호막 · 속성 저항")]
    private static void ForceDefense() => Force(new[] { MonsterTraitKind.Ward, MonsterTraitKind.Resist });

    [MenuItem(Root + "강제 끔(무작위)")]
    private static void ForceOff() => Force(null);

    private static void Force(MonsterTraitKind[] kinds)
    {
        MonsterTraitTable.DebugForceBossAffixes = kinds;
        Debug.Log($"{Tag} 강제 {(kinds == null ? "끔" : string.Join(", ", kinds))}");
    }

    [MenuItem(Root + "측정 (보스방, 플레이 중)")]
    private static void Measure()
    {
        if (!Application.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만"); return; }
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        if (p == null) { Debug.LogWarning($"{Tag} 런 플레이어가 없다"); return; }
        RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
    }

    // 자동 — 테스트 허브에서 그 챕터 보스 대기방 직행 → 보스방 → 보스 12 m 앞(깨움) → 대사 넘기기 → 측정. 악몽 오버라이드는 칸 스크립트가 플레이 전에 건다.
    [MenuItem(Root + "자동 A — 챕터 1 · 가시 · 박격 (테스트 허브, 플레이 중)")]
    private static void AutoA() { ForceActive(); AutoAsync(1).Forget(); }

    [MenuItem(Root + "자동 B — 챕터 2 · 수호막 · 속성 저항 (테스트 허브, 플레이 중)")]
    private static void AutoB() { ForceDefense(); AutoAsync(2).Forget(); }

    [MenuItem(Root + "자동 C — 챕터 4 · 무작위(리치) (테스트 허브, 플레이 중)")]
    private static void AutoC() { ForceOff(); AutoAsync(4).Forget(); }

    private static async UniTaskVoid AutoAsync(int chapter)
    {
        var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Debug.LogWarning($"{Tag} 테스트 허브에서 시작한다."); return; }
        launcher.SelectChapter(chapter);
        if (!launcher.ToggleBossApproach()) launcher.ToggleBossApproach();   // 보스 대기방 직행
        if (!await WaitAsync(launcher.TryLaunch, 30f)) { Debug.LogWarning($"{Tag} 시작 실패(초기화 중?)"); return; }
        Debug.Log($"{Tag} 챕터 {chapter} 보스 대기방 직행 · 시기 {StoryProgress.Era}");
        if (!await WaitAsync(() => GameRunBootstrapper.Instance?.Run?.Player != null && (GameRunBootstrapper.Instance?.Run?.IsRunning ?? false)
                                    && Object.FindFirstObjectByType<TestHubLauncher>() == null, 90f)) { Debug.LogWarning($"{Tag} 런 진입 실패"); return; }
        await UniTask.Delay(3000, DelayType.Realtime);
        if (Object.FindFirstObjectByType<BossRoomController>() == null)
        {
            // 대기방 출구가 늦게 열린다(챕터 1 — 대사 · 장비 고르기 뒤 · 11-05 첫 실측 3초에 「열린 출구가 없다」) — 대사를 넘기며 열릴 때까지 다시 누른다
            float e0 = Time.realtimeSinceStartup;
            while (Object.FindFirstObjectByType<BossRoomController>() == null && Time.realtimeSinceStartup - e0 < 60f)
            {
                if (Object.FindFirstObjectByType<UI_DialoguePopup>() != null) TestHubDebugMenu.AdvanceDialogue();
                else TestHubDebugMenu.EnterOpenExit();
                await UniTask.Delay(2000, DelayType.Realtime);
            }
            if (Object.FindFirstObjectByType<BossRoomController>() == null) { Debug.LogWarning($"{Tag} 보스방에 못 들어갔다(60초)"); return; }
        }
        await UniTask.Delay(2500, DelayType.Realtime);
        TestHubDebugMenu.StepIntoBossEntrance();
        await UniTask.Delay(400, DelayType.Realtime);
        TestHubDebugMenu.StepPastBossEntrance();
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        p?.SetInvincible(3600f);
        // 보스가 깨어날 때까지(특성은 전투 시작에 붙는다) — 대사는 넘기고 12 m 앞으로
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 90f)
        {
            var b = Object.FindFirstObjectByType<BossSpawner>()?.SpawnedBoss;
            if (b != null && b.TryGetComponent<MonsterTraits>(out _)) break;
            if (Object.FindFirstObjectByType<UI_DialoguePopup>() != null) TestHubDebugMenu.AdvanceDialogue();
            else if (b != null && b.isActiveAndEnabled && Time.realtimeSinceStartup - t0 > 6f) TestHubDebugMenu.StepNearBoss();
            await UniTask.Delay(1500, DelayType.Realtime);
        }
        p = GameRunBootstrapper.Instance?.Run?.Player;
        if (p == null) { Debug.LogWarning($"{Tag} 플레이어 없음"); return; }
        RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
    }

    private static async UniTask<bool> WaitAsync(System.Func<bool> cond, float seconds)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            try { if (cond()) return true; }
            catch (System.Exception) { }
            await UniTask.Delay(100, DelayType.Realtime);
        }
        return false;
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("11-05 악몽 보스 특성\n");
        Directory.CreateDirectory(ShotDir);
        try
        {
            var lib = EraBalance.For(StoryEra.Liberated);
            var nm  = EraBalance.For(StoryEra.NightmareMode);
            sb.AppendLine($"시기 {StoryProgress.Era} · 스탯 해방 ×{lib.StatScale} / 악몽 ×{nm.StatScale} · 체력 해방 ×{lib.HpScale} / 악몽 ×{nm.HpScale}");
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);

            var spawner = Object.FindFirstObjectByType<BossSpawner>();
            MonsterTraits traits = null;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 90f)
            {
                var b = spawner != null ? spawner.SpawnedBoss : null;
                if (b != null && b.TryGetComponent(out traits)) break;
                await Wait(0.25f, ct);
            }
            var boss = spawner != null ? spawner.SpawnedBoss : null;
            if (boss == null || traits == null) { sb.AppendLine($"FAIL 특성이 안 붙었다 — 보스 {(boss != null ? boss.name : "없음")} · {Time.realtimeSinceStartup - t0:0}초"); return; }
            sb.AppendLine($"PASS 붙음 — {boss.name} · {string.Join(", ", traits.Kinds)} · 기다림 {Time.realtimeSinceStartup - t0:0.0}초 · 이름 「{boss.DisplayName}」");
            sb.AppendLine($"  보스바 줄: {traits.BossAffixLine()}");
            await Wait(1.0f, ct);
            await Shot("NB1105_bar");

            // 플레이어를 보스 옆으로(가시 원 · 박격 원이 닿게)
            Vector3 bp = boss.transform.position;
            Vector3 side = boss.transform.right * 2.2f;
            p.transform.position = bp + side;
            if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = p.transform.position; rb.linearVelocity = Vector3.zero; }

            if (traits.Has(MonsterTraitKind.Ward) || traits.Has(MonsterTraitKind.Resist))
            {
                string before = traits.BossAffixLine();
                boss.TakeDamage(Mathf.Max(1f, boss.EffectiveMaxHp * 0.05f), p.gameObject);
                await Wait(0.3f, ct);
                string after = traits.BossAffixLine();
                sb.AppendLine($"{(before != after ? "PASS" : "CHECK")} 수호막 — 최대 체력 5% 피해 → 줄 「{Strip(before)}」 → 「{Strip(after)}」");
            }
            if (traits.Has(MonsterTraitKind.Thorns))
            {
                for (int i = 0; i < 6; i++) { boss.TakeDamage(1f, p.gameObject); await Wait(0.15f, ct); }
                sb.AppendLine($"  가시 6타 뒤 줄: {Strip(traits.BossAffixLine())}");
                await Wait(1.2f, ct);
                await Shot("NB1105_thorns");
                sb.AppendLine("  (가시 예고 · 판정은 로그 [ElitePrefix] 가시 — 칸 스크립트가 센다)");
            }
            if (traits.Has(MonsterTraitKind.Mortar))
            {
                float m0 = Time.realtimeSinceStartup;
                await Wait(17f, ct);   // 첫 발 6초 · 다음 8초 — 두 번은 나와야
                sb.AppendLine($"  박격 기다림 {Time.realtimeSinceStartup - m0:0}초(로그 [BossAffix] 박격 · [ElitePrefix] 박격 판정을 스크립트가 센다)");
                await Shot("NB1105_mortar");
            }
        }
        catch (System.OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/nmboss1105.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    private static string Strip(string rich) => System.Text.RegularExpressions.Regex.Replace(rich ?? "", "<[^>]+>", "");

    private static UniTask Wait(float seconds, CancellationToken ct)
        => UniTask.Delay(System.TimeSpan.FromSeconds(seconds), ignoreTimeScale: true, cancellationToken: ct);

    private static async UniTask Shot(string name)
    {
        ScreenCapture.CaptureScreenshot($"{ShotDir}/{name}.png");
        await UniTask.Yield();
        await UniTask.Yield();
    }
}
#endif
