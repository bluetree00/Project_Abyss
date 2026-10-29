#if UNITY_EDITOR
using System.IO;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-29 사용자 H 확인 — 자동 실측 중 시기 특성이 셋(접두어 + 둘)인 정예가 나오면 플레이어를 그 앞 5 m로 옮겨 한 장(이름표 · 특성 아이콘).
/// 결과 Temp/era_elite/elite3.png · 로그 「[EraEliteProbe] 끝」.
/// </summary>
public static class EraEliteProbeEditor
{
    [MenuItem("RelicFairy/Debug/10-29 H 특성 셋 정예 사진 (자동 실측 중, Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[EraEliteProbe] 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        Directory.CreateDirectory("Temp/era_elite");
        string found = "없음";
        for (float until = Time.realtimeSinceStartup + 300f; Time.realtimeSinceStartup < until;)
        {
            foreach (var t in Object.FindObjectsByType<MonsterTraits>(FindObjectsSortMode.None))
            {
                var kinds = t.Kinds;
                if (kinds == null || kinds.Count < 3) continue;
                if (!t.TryGetComponent<MonsterBase>(out var m) || m.IsDead) continue;
                var player = GameRunBootstrapper.Instance?.Run?.Player;
                if (player != null)
                {
                    Vector3 at = m.transform.position + m.transform.forward * 5f;
                    player.transform.position = at;
                    if (player.Rigid != null) { player.Rigid.position = at; player.Rigid.linearVelocity = Vector3.zero; }
                }
                found = $"{m.name}: {string.Join(", ", kinds)}";
                await UniTask.Delay(400, DelayType.Realtime);
                ScreenCapture.CaptureScreenshot("Temp/era_elite/elite3.png");
                await UniTask.Delay(600, DelayType.Realtime);
                Debug.Log($"[EraEliteProbe] 끝 — {found}");
                return;
            }
            await UniTask.Delay(100, DelayType.Realtime);
        }
        Debug.Log($"[EraEliteProbe] 끝 — {found}");
    }
}
#endif
