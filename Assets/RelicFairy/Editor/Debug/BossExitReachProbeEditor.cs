#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 10-25 ① 「챕터3 보스 클리어 뒤 다음 방으로 넘어가는 길이 장벽 때문에 막힘」 — Ch3 보스방(전투가 열린 뒤)에서 기사를 실제로 쓰러뜨리고
/// (50% 관문은 풀어 둔다) 파츠 고르기를 넘겨 다음 챕터 문(@ChapterGate)이 서면, 유리 장벽 상태 · 문까지 광선에 걸리는 것 ·
/// 경로를 따라 걸어가 닿는지를 잰다. 결과 로그 「[ExitReach] …」 · 끝 「[ExitReach] 끝」.
/// </summary>
public static class BossExitReachProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const float WalkSpeed = 6f, WalkSeconds = 14f, ReachDist = 2.5f;

    [MenuItem("RelicFairy/Debug/10-25 Ch3 보스 클리어 뒤 문까지 걷기 (보스방, Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[ExitReach] 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        try
        {
            var player = GameRunBootstrapper.Instance?.Run?.Player;
            var boss   = UnityEngine.Object.FindObjectsByType<DeathKnightBossMonster>(FindObjectsSortMode.None).FirstOrDefault(b => !b.IsDead);
            if (player == null || boss == null) { Debug.LogWarning($"[ExitReach] 플레이어 {player != null} · 기사 {boss != null}"); return; }
            player.SetInvincible(600f);
            var barriers = typeof(DeathKnightBossMonster).GetField("_entranceEndBarriers", Inst)?.GetValue(boss) as GameObject[] ?? Array.Empty<GameObject>();
            s_exitPos = null; s_runClear = false;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            Debug.Log($"[ExitReach] 쓰러뜨리기 전 유리 장벽 {Describe(barriers)}");

            typeof(DeathKnightBossMonster).GetField("_soulGateCleared", Inst)?.SetValue(boss, true);   // 50% 관문 — 측정용으로 푼다
            string hp = typeof(TestRunOverlay).GetMethod("JumpBossNearDeath", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null) as string;
            Debug.Log($"[ExitReach] {hp}");
            for (int i = 0; i < 20 && !boss.IsDead; i++)
            {
                boss.TakeDamage(boss.EffectiveMaxHp, player.gameObject);
                await UniTask.Delay(250, DelayType.Realtime);
            }
            Debug.Log($"[ExitReach] 기사 쓰러짐 {boss.IsDead} · 유리 장벽 {Describe(barriers)}");

            // 출구 = 층계(S5) 또는 게이트 — 자리는 「[BossClear] 출구 완료. exitPos=…」 로그로 받는다(마지막 챕터면 길 없이 런 클리어)
            float t0 = Time.realtimeSinceStartup;
            while (s_exitPos == null && !s_runClear && Time.realtimeSinceStartup - t0 < 60f)
            {
                await UniTask.Delay(2000, DelayType.Realtime);
                EditorApplication.ExecuteMenuItem("RelicFairy/Debug/Confirm Draft Popup (Play)");
            }
            if (s_runClear) { Debug.LogWarning("[ExitReach] 마지막 챕터 — 길 없이 런 클리어(다음 챕터 해금 필요)"); return; }
            if (s_exitPos == null) { Debug.LogWarning("[ExitReach] 60초 안에 출구가 안 섰다"); return; }
            await UniTask.Delay(3500, DelayType.Realtime);   // 층계가 솟는 연출
            var gate = new GameObject("[ExitReach] 출구 자리");
            gate.transform.position = s_exitPos.Value;
            Debug.Log($"[ExitReach] 출구 {gate.transform.position} · 층계 {UnityEngine.Object.FindFirstObjectByType<ChapterStairway>() != null} · 게이트 {GameObject.Find("@ChapterGate") != null} · 플레이어 {player.transform.position} · 유리 장벽 {Describe(barriers)}");

            // 문까지 광선(가슴 높이) — 걸리는 것
            Vector3 from = player.transform.position + Vector3.up * 1f, to = gate.transform.position + Vector3.up * 1f;
            var hits = Physics.RaycastAll(from, (to - from).normalized, Vector3.Distance(from, to), ~0, QueryTriggerInteraction.Ignore)
                              .Where(h => !h.transform.IsChildOf(player.transform)).OrderBy(h => h.distance).Take(6)
                              .Select(h => $"{h.collider.name}{(h.collider.GetComponentInParent<SoftBarrier>() != null ? "(유리 장벽)" : "")} {h.distance:0.0} m");
            Debug.Log($"[ExitReach] 문까지 광선: {string.Join(" · ", hits.DefaultIfEmpty("걸림 없음"))}");

            // 경로를 따라 걷기
            var path = new NavMeshPath();
            bool hasPath = NavMesh.CalculatePath(player.transform.position, gate.transform.position, NavMesh.AllAreas, path);
            var rb = player.GetComponent<Rigidbody>();
            float minDist = float.MaxValue, w0 = Time.time;
            int corner = 1;
            while (Time.time - w0 < WalkSeconds && rb != null)
            {
                Vector3 p = rb.position;
                float d = Flat(gate.transform.position - p).magnitude;
                minDist = Mathf.Min(minDist, d);
                if (d <= ReachDist) break;
                Vector3 target = hasPath && corner < path.corners.Length ? path.corners[corner] : gate.transform.position;
                if (Flat(target - p).magnitude < 0.6f && corner < path.corners.Length) corner++;
                Vector3 dir = Flat(target - p).normalized;
                rb.linearVelocity = new Vector3(dir.x * WalkSpeed, rb.linearVelocity.y, dir.z * WalkSpeed);
                await UniTask.WaitForFixedUpdate();
            }
            if (rb != null) rb.linearVelocity = Vector3.zero;
            Debug.Log($"[ExitReach] 걷기 — 경로 {(hasPath ? path.status.ToString() : "없음")} · 모서리 {path.corners.Length} · 가장 가까움 {minDist:0.0} m · {(minDist <= ReachDist ? "닿음" : "못 닿음")} · 멈춘 자리 {rb?.position}");
        }
        catch (Exception e) { Debug.LogException(e); }
        finally { Application.logMessageReceived -= OnLog; Debug.Log("[ExitReach] 끝"); }
    }

    private static Vector3? s_exitPos;
    private static bool s_runClear;

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (msg.StartsWith("[GameRun] Ended") || msg.Contains("HandleRunClear")) s_runClear = true;
        const string key = "[BossClear] 출구 완료. exitPos=(";
        int i = msg.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return;
        var parts = msg.Substring(i + key.Length).TrimEnd(')').Split(',');
        if (parts.Length >= 3 && float.TryParse(parts[0], out float x) && float.TryParse(parts[1], out float y) && float.TryParse(parts[2].Trim().TrimEnd(')'), out float z))
            s_exitPos = new Vector3(x, y, z);
    }

    private static string Describe(GameObject[] barriers)
        => barriers.Length == 0 ? "없음" : string.Join(" · ", barriers.Select(b => b == null ? "(없음)" : $"{b.name} {(b.activeInHierarchy ? "켜짐" : "꺼짐")}"));

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
#endif
