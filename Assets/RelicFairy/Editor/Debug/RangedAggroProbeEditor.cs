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
/// 10-25 ② 「중간 보스를 화살로 때리면 특정 거리에서 멈춤 · 멀리서 몬스터를 때려도 안 따라옴」 — 첫 전투방에서
/// 일반 몬스터 하나 · 황소(Ch1)를 플레이어에게서 멀리(걸어서 이어진 가장 먼 자리) 세우고, 화살 피격(경직 없음)을 1초마다 넣으며
/// 0.5초마다 상태 · 거리 · 이동 속도 · 경로 · 멈춤을 남긴다. 결과 로그 「[RangedAggro] …」 · 끝 「[RangedAggro] 끝」.
/// </summary>
public static class RangedAggroProbeEditor
{
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const float WatchSeconds = 10f, HitEvery = 1f, MinFar = 16f;
    private static bool s_armed;

    [MenuItem("RelicFairy/Debug/10-25 원거리 피격 추적 실측 (테스트 허브, Play)")]
    private static void Begin()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[RangedAggro] 플레이 중에"); return; }
        var launcher = UnityEngine.Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Debug.LogWarning("[RangedAggro] 테스트 허브에서"); return; }
        typeof(TestHubLauncher).GetField("relics", Inst)?.SetValue(launcher, new RelicClassSO[] { null });
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
            if (p == null) { Debug.LogWarning("[RangedAggro] 런 플레이어가 없다"); return; }
            RunAsync(p).Forget();
        };
    }

    private static async UniTaskVoid RunAsync(PlayerController p)
    {
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            await UniTask.Delay(4000, DelayType.Realtime);   // 첫 전투방 부감 카메라
            var all = UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None).Where(m => m != null && !m.IsDead).ToList();
            var normal = all.FirstOrDefault(m => m.Grade == MonsterGrade.Common && !m.IsEliteBoss);
            foreach (var m in all) if (m != normal) m.gameObject.SetActive(false);

            if (normal != null) await Trial(p, normal, "일반 " + normal.name);
            else Debug.LogWarning("[RangedAggro] 방에 일반 몬스터가 없다");
            if (normal != null) normal.gameObject.SetActive(false);

            Vector3 far = FarSpot(p.transform.position, out float farDist);
            var bull = await Managers.ObjectPooler.SpawnAsync<MonsterBase>("BullTank/BullTank_Ch1", ObjectPoolerManager.PoolType.Monster, far, Quaternion.LookRotation(p.transform.position - far));
            if (bull == null) { Debug.LogWarning("[RangedAggro] 황소 부르기 실패"); return; }
            await UniTask.Delay(1500, DelayType.Realtime);
            await Trial(p, bull, "황소");
        }
        catch (Exception e) { Debug.LogException(e); }
        finally { Debug.Log("[RangedAggro] 끝"); }
    }

    private static async UniTask Trial(PlayerController p, MonsterBase m, string label)
    {
        Vector3 far = FarSpot(p.transform.position, out float farDist);
        if (m.TryGetComponent<NavMeshAgent>(out var agent) && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.Warp(far);
        else m.transform.position = far;
        var det = m.GetType().GetField("_config", Inst)?.GetValue(m) as MonsterConfigSO;
        Debug.Log($"[RangedAggro] {label} — 시작 거리 {Dist(p, m):0.0} m(가장 먼 자리 {farDist:0.0}) · 감지 {det?.detection.detectionRange:0.0} · 포기 {det?.detection.chaseGiveUpRange:0.0} · 등급 {m.Grade}");
        await UniTask.Delay(1000, DelayType.Realtime);

        var sb = new StringBuilder();
        float t0 = Time.time, nextHit = 0f, nextLog = 0f, minDist = float.MaxValue;
        while (Time.time - t0 < WatchSeconds && m != null && !m.IsDead && m.gameObject.activeInHierarchy)
        {
            float t = Time.time - t0;
            if (t >= nextHit)
            {
                nextHit += HitEvery;
                MonsterPoise.PendingNoFlinch = true;   // 화살 — 경직 · 넉백 없음(CombatDamage와 같게)
                try { m.TakeDamage(1f, p.gameObject); }
                finally { MonsterPoise.PendingNoFlinch = false; }
            }
            minDist = Mathf.Min(minDist, Dist(p, m));
            if (t >= nextLog)
            {
                nextLog += 0.5f;
                string st = m.GetType().GetField("_debugState", Inst)?.GetValue(m) as string
                            ?? typeof(MonsterBase).GetField("_debugState", Inst)?.GetValue(m) as string;
                string nav = agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh
                    ? $"속도 {agent.velocity.magnitude:0.0} · 경로 {agent.pathStatus} · 남은 {agent.remainingDistance:0.0} · 멈춤 {agent.isStopped} · 정지 거리 {agent.stoppingDistance:0.0}"
                    : "에이전트 없음/꺼짐";
                sb.Append($"\n   t={t:0.0} 상태 {st} · 거리 {Dist(p, m):0.0} · {nav}");
            }
            await UniTask.Yield();
        }
        Debug.Log($"[RangedAggro] {label} — 가장 가까움 {minDist:0.0} m{sb}");
    }

    private static float Dist(PlayerController p, MonsterBase m)
    {
        Vector3 d = m.transform.position - p.transform.position; d.y = 0f;
        return d.magnitude;
    }

    /// <summary>플레이어와 걸어서 이어진 가장 먼 바닥(8방향 × 4 · 8 · … 40 m) — 경로가 완전한 자리만.</summary>
    private static Vector3 FarSpot(Vector3 from, out float best)
    {
        best = 0f;
        Vector3 at = from;
        var path = new NavMeshPath();
        for (int i = 0; i < 16; i++)
        {
            Vector3 d = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
            for (float r = 40f; r >= MinFar; r -= 4f)
            {
                if (!NavMesh.SamplePosition(from + d * r, out var s, 2f, NavMesh.AllAreas)) continue;
                if (!NavMesh.CalculatePath(from, s.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                float dist = Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(s.position.x, 0f, s.position.z));
                if (dist > best) { best = dist; at = s.position; }
                break;
            }
        }
        return at;
    }
}
#endif
