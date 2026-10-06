#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-06 시기 두 번째 파도(레벨디자인 설계서 §5) 실측.
/// ① 「시기 → 악몽」: 테스트 시기 오버라이드를 악몽으로(원래 값은 기억했다가 ③이 되돌린다).
/// ② 「실측 (런 중 · 전투방)」: 굴림 분포(시기 × 방 종류 2,000번) → 지금 방의 첫 파도를 처치 → 두 번째 파도 시작 · 알림 화면 ·
///    등급 구성 → 처치 → 방 클리어까지. 실행 동안 플레이어 무적.
///    결과: Temp/era_wave_probe.txt · Temp/ui_shots/Wave2_{notice|spawned}.png · 로그 「[EraWave] 끝」.
/// ③ 「시기 원래대로」.
/// </summary>
public static class EraWaveProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string SavedKey = "RelicFairy.Story.NightmareOverride.EraWaveProbeSaved";

    [MenuItem("RelicFairy/Debug/10-06 두 번째 파도 ① 시기 → 악몽 (테스트)")]
    private static void EraNightmare()
    {
        if (!EditorPrefs.HasKey(SavedKey))
            EditorPrefs.SetInt(SavedKey, EditorPrefs.GetInt(StoryProgress.DebugOverridePrefsKey, -1));
        EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, (int)StoryEra.NightmareMode);
        StoryProgress.RefreshDebugOverride();
        Debug.Log($"[EraWave] 시기 → {StoryProgress.Era}");
    }

    [MenuItem("RelicFairy/Debug/10-06 두 번째 파도 ③ 시기 원래대로")]
    private static void EraRestore()
    {
        int saved = EditorPrefs.GetInt(SavedKey, -1);
        EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, saved);
        EditorPrefs.DeleteKey(SavedKey);
        StoryProgress.RefreshDebugOverride();
        Debug.Log($"[EraWave] 시기 원래대로({saved}) → {StoryProgress.Era}");
    }

    [MenuItem("RelicFairy/Debug/10-06 두 번째 파도 ② 실측 (런 중 · 전투방)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) return;
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("시기 두 번째 파도 실측\n");
        try
        {
            // 굴림 분포
            foreach (var era in new[] { StoryEra.Sealed, StoryEra.Liberated, StoryEra.NightmareMode })
            foreach (var kind in new[] { RoomPlanKind.Normal, RoomPlanKind.Elite, RoomPlanKind.Event })
            {
                int hit = 0, elites = 0;
                for (int i = 0; i < 2000; i++)
                {
                    var (e, c) = EraBalance.RollExtraWave(kind, era);
                    if (e + c > 0) { hit++; elites += e; }
                }
                sb.AppendLine($"{era}/{kind}: 두 번째 파도 {hit / 20f:0.#}% · 정예 평균 {(hit > 0 ? elites / (float)hit : 0f):0.#}");
            }
            sb.AppendLine($"지금 시기 {StoryProgress.Era} · 방 종류 {GameRunBootstrapper.Instance?.Run?.CurrentRoomKind}");

            var player = Object.FindFirstObjectByType<PlayerController>();
            if (player == null) { sb.AppendLine("플레이어 없음"); return; }
            GameRunBootstrapper.Instance?.Run?.Player?.SetInvincible(90f);

            var room = ActiveRoom();
            if (room == null) { sb.AppendLine("진행 중인 전투방 없음"); return; }
            int baseWaves  = Get<int>(room, "_baseWaves");
            int totalWaves = Get<int>(room, "_totalWaves");
            sb.AppendLine($"방 {room.transform.parent?.name}/{room.name} · 토큰 파도 {baseWaves} · 전체 {totalWaves} · 두 번째 파도 정예 {Get<int>(room, "_eraElites")} · 일반 {Get<int>(room, "_eraCommons")}");
            if (totalWaves <= baseWaves) { sb.AppendLine("이 방엔 두 번째 파도가 없다"); return; }

            // 첫 파도 처치 → 두 번째 파도 시작까지
            float t0 = Time.unscaledTime;
            while (Get<int>(room, "_currentWave") < baseWaves)
            {
                if (Time.unscaledTime - t0 > 60f) { sb.AppendLine("첫 파도 60초 안에 안 끝남"); return; }
                KillAll(player);
                await UniTask.Delay(300, ignoreTimeScale: true);
            }
            sb.AppendLine($"두 번째 파도 시작 — 첫 파도 처치 시작부터 {Time.unscaledTime - t0:0.0}초");

            Directory.CreateDirectory("Temp/ui_shots");
            await UniTask.Delay(700, ignoreTimeScale: true);
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/Wave2_notice.png");

            float t1 = Time.unscaledTime;
            while (!Get<bool>(room, "_waveSpawningDone") && Time.unscaledTime - t1 < 20f)
                await UniTask.Delay(200, ignoreTimeScale: true);
            await UniTask.Delay(600, ignoreTimeScale: true);
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/Wave2_spawned.png");

            var byGrade = new Dictionary<MonsterGrade, int>();
            var traits  = new List<string>();
            foreach (var m in Get<List<MonsterBase>>(room, "_waveMonsters"))
            {
                if (m == null) continue;
                byGrade[m.Grade] = byGrade.TryGetValue(m.Grade, out var k) ? k + 1 : 1;
                if (m.TryGetComponent<MonsterTraits>(out var t) && t.Kinds.Count > 0) traits.Add($"{m.name}: {string.Join(",", t.Kinds)}");
            }
            var parts = new List<string>();
            foreach (var kv in byGrade) parts.Add($"{kv.Key} {kv.Value}");
            sb.AppendLine($"두 번째 파도 소환 {Time.unscaledTime - t1:0.0}초 · {string.Join(" · ", parts)}");
            foreach (var s in traits) sb.AppendLine("  특성 " + s);

            // 처치 → 클리어
            float t2 = Time.unscaledTime;
            while (!Get<bool>(room, "_cleared") && Time.unscaledTime - t2 < 30f)
            {
                KillAll(player);
                await UniTask.Delay(300, ignoreTimeScale: true);
            }
            sb.AppendLine(Get<bool>(room, "_cleared") ? $"방 클리어 — {Time.unscaledTime - t2:0.0}초" : "!! 30초 안에 클리어 안 됨");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/era_wave_probe.txt", sb.ToString());
            Debug.Log("[EraWave] 끝\n" + sb);
        }
    }

    private static RoomWaveController ActiveRoom()
    {
        foreach (var r in Object.FindObjectsByType<RoomWaveController>(FindObjectsSortMode.None))
            if (Get<bool>(r, "_active") && !Get<bool>(r, "_cleared") && Get<bool>(r, "_waveMode")) return r;
        return null;
    }

    private static void KillAll(PlayerController player)
    {
        foreach (var mb in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
        {
            if (mb == null || mb.IsDead || mb.Grade == MonsterGrade.Boss || !mb.gameObject.activeInHierarchy) continue;
            mb.HpFloorMin1 = false;
            mb.TakeDamage(mb.CurrentHp * 10f, player.gameObject, 0f);
        }
    }

    private static T Get<T>(RoomWaveController r, string field)
        => (T)typeof(RoomWaveController).GetField(field, Inst).GetValue(r);
}
#endif
