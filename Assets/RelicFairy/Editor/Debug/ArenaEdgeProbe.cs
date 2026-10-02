#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 아레나 가장자리 가림 확인(에디터 전용) — 플레이어를 아레나 가장자리 몇 곳에 세우고 게임 카메라 그대로 찍는다.
/// 카메라 가림 페이더(<see cref="CameraOcclusionFader"/>)가 지금 흐리게 하는 렌더러 이름도 같이 남긴다.
/// 10-01 f5: 기사 아레나 회랑 기둥(SM_PillarMiddle, x = ±10.2)이 가장자리로 밀리면 화면을 가린다(09-25 G21).
/// 결과: Logs/arena_edge/&lt;시각&gt;/&lt;번호&gt;_&lt;자리&gt;.png · 콘솔 [ArenaEdge]
/// </summary>
public static class ArenaEdgeProbe
{
    private const float SettleSeconds = 1.8f;
    private static readonly Vector2[] Spots = { new(8f, 0f), new(-8f, 0f), new(8f, -8f), new(-8f, 8f), new(8f, 8f), new(-8f, -8f) };
    private static CancellationTokenSource s_cts;

    [MenuItem("RelicFairy/Debug/Arena Edge Probe (Play)")]
    public static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[ArenaEdge] 플레이 모드에서만."); return; }
        s_cts?.Cancel();
        s_cts?.Dispose();
        s_cts = new CancellationTokenSource();
        RunAsync(s_cts.Token).Forget();
    }

    private static async UniTaskVoid RunAsync(CancellationToken ct)
    {
        var runner = UnityEngine.Object.FindFirstObjectByType<Managers>();
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        var room   = UnityEngine.Object.FindFirstObjectByType<BossRoomController>();
        if (runner == null || player == null || room == null) { Debug.LogWarning("[ArenaEdge] 보스방 · 플레이어가 아니다."); return; }

        Vector3 center = room.TryGetComponent<Collider>(out var trig) ? trig.bounds.center : room.transform.position;
        var arena = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                                      .FirstOrDefault(t => t.name.StartsWith("Arena_Boss_") && t.parent != null);
        if (arena != null) center = arena.position;

        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "arena_edge", DateTime.Now.ToString("MMdd_HHmmss"));
        Directory.CreateDirectory(dir);
        player.SetInvincible(60f);
        var fader = UnityEngine.Object.FindFirstObjectByType<CameraOcclusionFader>();

        try
        {
            for (int i = 0; i < Spots.Length; i++)
            {
                Vector3 target = center + new Vector3(Spots[i].x, 0f, Spots[i].y);
                target.y = Physics.Raycast(target + Vector3.up * 30f, Vector3.down, out var hit, 60f,
                                           LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore)
                    ? hit.point.y + 0.25f : center.y + 0.25f;
                player.transform.position = target;
                if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = target; rb.linearVelocity = Vector3.zero; }

                await UniTask.Delay(TimeSpan.FromSeconds(SettleSeconds), DelayType.Realtime, cancellationToken: ct);
                await UniTask.WaitForEndOfFrame(runner, ct);
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                string label = $"{i:00}_x{Spots[i].x:+0;-0}_z{Spots[i].y:+0;-0}";
                File.WriteAllBytes(Path.Combine(dir, label + ".png"), tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
                Debug.Log($"[ArenaEdge] {label} 플레이어 {target} · 페이더가 흐리게 하는 것 {FadingNames(fader)}");
            }
            Debug.Log($"[ArenaEdge] 완료 — {dir}");
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 깜박임 확인 — 플레이어를 회랑 기둥 앞에서 좌우로 6초 오가게 하며(0.1초마다 0.15 m) 페이더가 흐리게 하는 렌더러가
    /// 들어오고 나간 횟수를 센다. 같은 렌더러가 짧은 사이 여러 번 들락날락하면 깜박임이다.
    /// </summary>
    [MenuItem("RelicFairy/Debug/Arena Edge Sweep (Play)")]
    public static void Sweep()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[ArenaEdge] 플레이 모드에서만."); return; }
        s_cts?.Cancel();
        s_cts?.Dispose();
        s_cts = new CancellationTokenSource();
        SweepAsync(s_cts.Token).Forget();
    }

    private static async UniTaskVoid SweepAsync(CancellationToken ct)
    {
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        var room   = UnityEngine.Object.FindFirstObjectByType<BossRoomController>();
        var fader  = UnityEngine.Object.FindFirstObjectByType<CameraOcclusionFader>();
        if (player == null || room == null || fader == null) { Debug.LogWarning("[ArenaEdge] 보스방 · 플레이어 · 페이더가 아니다."); return; }
        var arena = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                                      .FirstOrDefault(t => t.name.StartsWith("Arena_Boss_") && t.parent != null);
        Vector3 center = arena != null ? arena.position : room.transform.position;
        var field = typeof(CameraOcclusionFader).GetField("_fading", BindingFlags.Instance | BindingFlags.NonPublic);
        player.SetInvincible(60f);

        var prev = new System.Collections.Generic.HashSet<Renderer>();
        var toggles = new System.Collections.Generic.Dictionary<string, int>();
        int frames = 0;
        try
        {
            foreach (float z in new[] { 0f, -8f })
            {
                for (int step = 0; step < 60; step++)
                {
                    float k = Mathf.PingPong(step * 0.15f, 3.6f);   // x −6.0 ↔ −9.6
                    Vector3 target = center + new Vector3(-6f - k, 0f, z);
                    target.y = Physics.Raycast(target + Vector3.up * 30f, Vector3.down, out var hit, 60f,
                                               LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore) ? hit.point.y + 0.25f : center.y + 0.25f;
                    player.transform.position = target;
                    if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = target; rb.linearVelocity = Vector3.zero; }
                    float until = Time.realtimeSinceStartup + 0.1f;
                    while (Time.realtimeSinceStartup < until)
                    {
                        await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                        frames++;
                        var now = new System.Collections.Generic.HashSet<Renderer>(
                            (field?.GetValue(fader) as IDictionary)?.Keys.Cast<Renderer>().Where(r => r != null) ?? Enumerable.Empty<Renderer>());
                        foreach (var r in now) if (!prev.Contains(r)) Bump(toggles, r.name);
                        foreach (var r in prev) if (r != null && !now.Contains(r)) Bump(toggles, r.name);
                        prev = now;
                    }
                }
            }
            int total = toggles.Values.Sum();
            string worst = string.Join(", ", toggles.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Key}×{kv.Value}"));
            Debug.Log($"[ArenaEdge] 좌우 걷기 {frames}프레임 · 흐림 들고남 {total}회 · 많은 것 {worst}");
        }
        catch (OperationCanceledException) { }
    }

    private static void Bump(System.Collections.Generic.Dictionary<string, int> map, string key)
        => map[key] = map.TryGetValue(key, out int n) ? n + 1 : 1;

    private static string FadingNames(CameraOcclusionFader fader)
    {
        if (fader == null) return "(페이더 없음)";
        var f = typeof(CameraOcclusionFader).GetField("_fading", BindingFlags.Instance | BindingFlags.NonPublic);
        if (f?.GetValue(fader) is not IDictionary map) return "(필드 없음)";
        if (map.Count == 0) return "0개";
        var names = map.Keys.Cast<Renderer>().Where(r => r != null).Select(r => r.name).Distinct().Take(8);
        return $"{map.Count}개 — {string.Join(", ", names)}";
    }
}
#endif
