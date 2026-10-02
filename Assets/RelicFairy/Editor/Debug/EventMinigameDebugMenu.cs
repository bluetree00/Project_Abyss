using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 이벤트방 놀이 확인 도구(에디터 전용, 10-01 설계 §8).
///   · Play Here/… (Play) — 지금 방에서 그 놀이를 바로 시작(방 입장 흐름 없이). 판 가운데 = 플레이어 자리.
///   · Demo/… — 자동 시연(완벽 / 보통 / 방치). 놀이마다 흉내 내는 방식이 다르다(판정 흡수 · 자동 밟기 · 자동 열기).
///   · Self Check - Grades — 등급 계산의 경계값.
///   · Force Next Door/… — 다음 출구 두 문을 그 방으로 고정(실제 방 흐름 확인 · 자동 런과 함께). Off로 끈다.
///   · Arm Captures (Play) — 도는 놀이를 1.5초마다 찍는다(Logs/minigame/).
/// </summary>
public static class EventMinigameDebugMenu
{
    private const string Root = "RelicFairy/Minigame/";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static CancellationTokenSource s_captureCts;

    // ── 여기서 시작 ────────────────────────────────────────────
    [MenuItem(Root + "Play Here/Barrage (Play)")] public static void PlayBarrage() => PlayHere<BarrageCorridorGame>();
    [MenuItem(Root + "Play Here/Memory (Play)")]  public static void PlayMemory()  => PlayHere<RuneMemoryGame>();
    [MenuItem(Root + "Play Here/Greed (Play)")]   public static void PlayGreed()   => PlayHere<GreedChestGame>();

    private static void PlayHere<T>() where T : EventMinigame
    {
        if (!Application.isPlaying) { Debug.LogWarning("[MinigameDebug] 플레이 모드에서만."); return; }
        if (EventMinigame.Active != null) { Debug.LogWarning("[MinigameDebug] 이미 놀이가 돌고 있다."); return; }
        var boot   = GameRunBootstrapper.Instance;
        var player = Managers.Player?.PlayerTransform;
        if (boot == null || player == null) { Debug.LogWarning("[MinigameDebug] 런 · 플레이어 없음"); return; }

        var t    = typeof(GameRunBootstrapper);
        var luck = t.GetField("luckRollTable", Inst)?.GetValue(boot) as LuckRollTableSO;
        var e1   = t.GetField("clearEndEffectPrefab", Inst)?.GetValue(boot) as GameObject;
        var e2   = t.GetField("clearEndEffect2Prefab", Inst)?.GetValue(boot) as GameObject;

        var go = new GameObject($"MinigameDebug_{typeof(T).Name}");
        go.transform.position = player.position;
        RoomScopedDrop.Mark(go);
        var game = go.AddComponent<T>();
        game.Initialize(boot.Run, luck, e1, e2, Environment.TickCount);
        game.StartNow();
        Debug.Log($"[MinigameDebug] {typeof(T).Name} 시작 — 시연 {EventMinigame.DebugDemo}");
    }

    // ── 자동 시연 ──────────────────────────────────────────────
    [MenuItem(Root + "Demo/Off")]     public static void DemoOff()     => SetDemo(MinigameDemo.None);
    [MenuItem(Root + "Demo/Perfect")] public static void DemoPerfect() => SetDemo(MinigameDemo.Perfect);
    [MenuItem(Root + "Demo/Normal")]  public static void DemoNormal()  => SetDemo(MinigameDemo.Normal);
    [MenuItem(Root + "Demo/Idle")]    public static void DemoIdle()    => SetDemo(MinigameDemo.Idle);

    private static void SetDemo(MinigameDemo mode)
    {
        EventMinigame.DebugDemo = mode;
        Debug.Log($"[MinigameDebug] 자동 시연 = {mode}");
    }

    // ── 다음 문 고정 ───────────────────────────────────────────
    [MenuItem(Root + "Force Next Door/Barrage (ch1)")] public static void ForceBarrage() => ForceDoor("ch1_event_barrage_01");
    [MenuItem(Root + "Force Next Door/Memory (ch1)")]  public static void ForceMemory()  => ForceDoor("ch1_event_memory_01");
    [MenuItem(Root + "Force Next Door/Greed (ch1)")]   public static void ForceGreed()   => ForceDoor("ch1_event_greed_01");
    [MenuItem(Root + "Force Next Door/Off")]           public static void ForceOff()     => ForceDoor("");

    /// <summary>다음 출구 두 문을 이 방 열쇠로 — 챕터가 다르면 그 챕터 열쇠로 바꿔 부른다(RunSequencer가 풀에서 못 찾으면 무시).</summary>
    public static void ForceDoor(string poolKey)
    {
        EditorPrefs.SetString(RunSequencer.DebugForceDoorPrefsKey, poolKey ?? "");
        // 새 방은 CDN에 올리기 전까지 로컬 CSV에만 있다 — 고정하는 동안 로컬 방 풀을 먼저 읽게 한다
        EditorPrefs.SetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, !string.IsNullOrEmpty(poolKey));
        Debug.Log(string.IsNullOrEmpty(poolKey) ? "[MinigameDebug] 다음 문 고정 끔" : $"[MinigameDebug] 다음 문 고정 = {poolKey}");
    }

    // ── 등급 점검 ──────────────────────────────────────────────
    [MenuItem(Root + "Self Check - Grades")]
    public static void SelfCheckGrades()
    {
        int fail = 0;
        // 탄막의 회랑 — 맞은 횟수 0 / 1 / 2~3 / 4~5 / 6+
        fail += Expect("회랑 0",  BarrageCorridorGame.GradeOf(0), ChallengeGrade.Platinum);
        fail += Expect("회랑 1",  BarrageCorridorGame.GradeOf(1), ChallengeGrade.Gold);
        fail += Expect("회랑 2",  BarrageCorridorGame.GradeOf(2), ChallengeGrade.Silver);
        fail += Expect("회랑 3",  BarrageCorridorGame.GradeOf(3), ChallengeGrade.Silver);
        fail += Expect("회랑 5",  BarrageCorridorGame.GradeOf(5), ChallengeGrade.Bronze);
        fail += Expect("회랑 6",  BarrageCorridorGame.GradeOf(6), ChallengeGrade.Fail);
        // 룬 기억 — 틀린 횟수 0 / 1 / 2 / 3 / 4+ · 시간 초과는 브론즈 천장 · 라운드 0이면 실패
        fail += Expect("기억 0 완주",       RuneMemoryGame.GradeOf(0, 3, 3, false), ChallengeGrade.Platinum);
        fail += Expect("기억 2 완주",       RuneMemoryGame.GradeOf(2, 3, 3, false), ChallengeGrade.Silver);
        fail += Expect("기억 4 완주",       RuneMemoryGame.GradeOf(4, 3, 3, false), ChallengeGrade.Fail);
        fail += Expect("기억 0 시간초과 1R", RuneMemoryGame.GradeOf(0, 1, 3, true),  ChallengeGrade.Bronze);
        fail += Expect("기억 0 시간초과 0R", RuneMemoryGame.GradeOf(0, 0, 3, true),  ChallengeGrade.Fail);
        fail += Expect("기억 4 시간초과 2R", RuneMemoryGame.GradeOf(4, 2, 3, true),  ChallengeGrade.Fail);
        // 욕심의 상자 — 0 브론즈 · 1 실버 · 2 골드 · 3~4 플래티넘
        fail += Expect("상자 0", GreedChestGame.GradeOf(0), ChallengeGrade.Bronze);
        fail += Expect("상자 1", GreedChestGame.GradeOf(1), ChallengeGrade.Silver);
        fail += Expect("상자 2", GreedChestGame.GradeOf(2), ChallengeGrade.Gold);
        fail += Expect("상자 3", GreedChestGame.GradeOf(3), ChallengeGrade.Platinum);
        fail += Expect("상자 4", GreedChestGame.GradeOf(4), ChallengeGrade.Platinum);
        Debug.Log(fail == 0 ? "[MinigameDebug] 등급 점검 PASS" : $"[MinigameDebug] 등급 점검 FAIL {fail}건");
    }

    private static int Expect(string label, ChallengeGrade actual, ChallengeGrade expected)
    {
        if (actual == expected) return 0;
        Debug.LogError($"[MinigameDebug] FAIL {label} — 실제 {actual} · 기대 {expected}");
        return 1;
    }

    // ── 캡처 ───────────────────────────────────────────────────
    [MenuItem(Root + "Arm Captures (Play)")]
    public static void ArmCaptures()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[MinigameDebug] 플레이 모드에서만."); return; }
        s_captureCts?.Cancel();
        s_captureCts?.Dispose();
        s_captureCts = new CancellationTokenSource();
        CaptureAsync(s_captureCts.Token).Forget();
    }

    private static async UniTaskVoid CaptureAsync(CancellationToken ct)
    {
        try
        {
            // 놀이가 나타날 때까지(최대 5분 — 자동 런이 그 방에 닿을 때까지)
            float waitEnd = Time.realtimeSinceStartup + 300f;
            while (EventMinigame.Active == null && Time.realtimeSinceStartup < waitEnd)
                await UniTask.Delay(250, DelayType.Realtime, cancellationToken: ct);
            var game = EventMinigame.Active;
            if (game == null) { Debug.LogWarning("[MinigameDebug] 캡처 — 놀이가 나타나지 않았다"); return; }

            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "minigame", $"{game.Id}_{DateTime.Now:MMdd_HHmmss}");
            Directory.CreateDirectory(dir);
            var runner = UnityEngine.Object.FindFirstObjectByType<Managers>();
            float t0 = Time.realtimeSinceStartup;
            int shots = 0;
            float until = float.MaxValue;
            while (Time.realtimeSinceStartup < until && Application.isPlaying && runner != null)
            {
                if (until == float.MaxValue && (game == null || game.IsResolved)) until = Time.realtimeSinceStartup + 4f;
                await UniTask.WaitForEndOfFrame(runner, ct);
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(dir, $"{Time.realtimeSinceStartup - t0:000.0}.png"), tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
                shots++;
                await UniTask.Delay(1500, DelayType.Realtime, cancellationToken: ct);
            }
            Debug.Log($"[MinigameDebug] 캡처 완료 — {shots}장 · {dir}");
        }
        catch (OperationCanceledException) { }
    }
}
