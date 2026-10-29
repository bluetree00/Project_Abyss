#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 10-23 「중간보스가 플레이어의 이동에 밀린다」 — 첫 전투방에 황소(Ch1)만 불러 몸으로 밀어 본다.
/// A 평소(추적 · 공격 중) 밀기 · B 휘청(포이즈 깨짐 = 넉백) 뒤 밀기 없음 · C 휘청 뒤 밀기 — 밀기 = 플레이어 5 m/s로 황소 쪽 1.5초.
/// 황소가 밀기 방향으로 간 거리 · 그동안 Rigidbody kinematic이었던 비율 · 에이전트 켜짐을 남긴다. 결과 로그 「[PushProbe] …」 · 끝 「[PushProbe] 끝」.
/// </summary>
public static class MidbossPushProbeEditor
{
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const float PushSpeed = 5f, PushSeconds = 1.5f, FreeAhead = 7f;
    private static bool s_armed;

    [MenuItem("RelicFairy/Boss/BullTank/10-23 중간보스 밀림 실측 (테스트 허브, Play)")]
    private static void Begin()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[PushProbe] 플레이 중에"); return; }
        var launcher = UnityEngine.Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Debug.LogWarning("[PushProbe] 테스트 허브에서"); return; }
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
            if (p == null) { Debug.LogWarning("[PushProbe] 런 플레이어가 없다"); return; }
            RunAsync(p).Forget();
        };
    }

    private static async UniTaskVoid RunAsync(PlayerController p)
    {
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            await UniTask.Delay(4000, DelayType.Realtime);   // 첫 전투방 부감 카메라
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (m != null) m.gameObject.SetActive(false);
            Vector3 home = p.transform.position, fwd = p.transform.forward;
            fwd.y = 0f; fwd.Normalize();
            var bull = await Managers.ObjectPooler.SpawnAsync<MonsterBase>("BullTank/BullTank_Ch1", ObjectPoolerManager.PoolType.Monster, home + fwd * 5f, Quaternion.LookRotation(-fwd));
            if (bull == null) { Debug.LogWarning("[PushProbe] 황소 부르기 실패"); return; }
            await UniTask.Delay(3500, DelayType.Realtime);   // 깨어나게(포효)

            var prb = p.GetComponent<Rigidbody>();
            var brb = bull.GetComponent<Rigidbody>();
            Debug.Log($"[PushProbe] 질량 — 플레이어 {prb.mass} · 황소 {brb.mass} · 황소 kinematic {brb.isKinematic} · 등급 {bull.Grade}");
            var pc = p.GetComponent<Collider>();
            Debug.Log($"[PushProbe] 플레이어 몸 {pc.GetType().Name} 층 {LayerMask.LayerToName(p.gameObject.layer)} · 반경 {pc.bounds.extents.x:0.00} · 제외 {pc.excludeLayers.value} · 포함 {pc.includeLayers.value}");
            foreach (var c in bull.GetComponentsInChildren<Collider>(true))
            {
                bool ignore = Physics.GetIgnoreLayerCollision(p.gameObject.layer, c.gameObject.layer);
                Debug.Log($"[PushProbe] 황소 충돌체 {c.name}({c.GetType().Name}) 층 {LayerMask.LayerToName(c.gameObject.layer)} · 켜짐 {c.enabled && c.gameObject.activeInHierarchy} · 트리거 {c.isTrigger} · " +
                          $"플레이어와 층 충돌 {(ignore ? "꺼짐" : "켜짐")} · 제외 {c.excludeLayers.value} · 포함 {c.includeLayers.value} · 범위 {c.bounds.extents}");
            }

            await Trial(p, bull, "A0 평소(밀기 없음)", stagger: false, push: false);
            await Trial(p, bull, "A 평소 밀기", stagger: false, push: true);
            await UniTask.Delay(2500, DelayType.Realtime);
            await Trial(p, bull, "A2 평소 밀기(다시)", stagger: false, push: true);
            await UniTask.Delay(3500, DelayType.Realtime);   // 포이즈 회복(3초)
            await Trial(p, bull, "B 휘청만(밀기 없음)", stagger: true, push: false);
            await UniTask.Delay(3500, DelayType.Realtime);
            await Trial(p, bull, "C 휘청 뒤 밀기", stagger: true, push: true);
        }
        catch (Exception e) { Debug.LogException(e); }
        finally { Debug.Log("[PushProbe] 끝"); }
    }

    private static async UniTask Trial(PlayerController p, MonsterBase bull, string label, bool stagger, bool push)
    {
        if (bull == null || bull.IsDead) { Debug.LogWarning($"[PushProbe] {label}: 황소 없음"); return; }
        var prb = p.GetComponent<Rigidbody>();
        var brb = bull.GetComponent<Rigidbody>();
        var agent = bull.GetComponent<NavMeshAgent>();
        Vector3 from = bull.transform.position;
        Vector3 dir = FreeDir(from);
        float gap = bull.GetComponent<Collider>().bounds.extents.x + 0.6f;
        Vector3 at = from - dir * gap;
        if (NavMesh.SamplePosition(at, out var s, 1.5f, NavMesh.AllAreas)) at = s.position;
        p.transform.position = at; prb.position = at; prb.linearVelocity = Vector3.zero;
        p.transform.rotation = Quaternion.LookRotation(dir);
        await UniTask.WaitForFixedUpdate();

        if (stagger)
        {
            if (bull.Poise == null) { Debug.LogWarning($"[PushProbe] {label}: 포이즈 없음(등급 {bull.Grade})"); return; }
            bull.Poise.Exposed = true;   // 다음 타격에 바로 휘청 — 공격 확약 중이면 끝날 때까지 다시 친다
            float t1 = Time.realtimeSinceStartup;
            while (!bull.Poise.IsBroken && Time.realtimeSinceStartup - t1 < 8f)
            {
                bull.TakeDamage(1f, p.gameObject);
                if (!bull.Poise.IsBroken) await UniTask.Delay(200, DelayType.Realtime);
            }
            bull.Poise.Exposed = false;
            if (!bull.Poise.IsBroken) { Debug.LogWarning($"[PushProbe] {label}: 휘청 못 냄"); return; }
            from = bull.transform.position;
            dir = (from - p.transform.position); dir.y = 0f; dir.Normalize();   // 넉백 방향 = 플레이어 → 황소
        }

        Vector3 start = bull.transform.position;
        int n = 0, kin = 0, agentOn = 0;
        float v1 = 0f, minGap = float.MaxValue;
        int stopped = 0;
        string states = "";
        float t0 = Time.time;
        while (Time.time - t0 < PushSeconds && bull != null && !bull.IsDead)
        {
            if (push) prb.linearVelocity = new Vector3(dir.x * PushSpeed, prb.linearVelocity.y, dir.z * PushSpeed);
            await UniTask.WaitForFixedUpdate();
            n++;
            if (brb.isKinematic) kin++;
            else if (n == 1) v1 = Vector3.Dot(brb.linearVelocity, dir);
            if (agent != null && agent.enabled) agentOn++;
            if (agent != null && agent.enabled && agent.isOnNavMesh && agent.isStopped) stopped++;
            if (n == 1 || n % 25 == 0) states += (typeof(MonsterBase).GetField("_debugState", Inst)?.GetValue(bull) as string) + " ";
            Vector3 gap2 = bull.transform.position - p.transform.position; gap2.y = 0f;
            minGap = Mathf.Min(minGap, gap2.magnitude);
        }
        Vector3 rel = p.transform.position - bull.transform.position; rel.y = 0f;
        float side = Vector3.Cross(dir, rel).y;   // 밀기 줄에서 옆으로 비낀 거리
        Vector3 moved = bull.transform.position - start;
        float along = Vector3.Dot(moved, dir);
        Debug.Log($"[PushProbe] {label} — 황소가 밀기 방향으로 {along:+0.00;-0.00} m(수평 전체 {new Vector2(moved.x, moved.z).magnitude:0.00} m) · " +
                  $"kinematic {kin}/{n} · 에이전트 켜짐 {agentOn}/{n} · 멈춤 {stopped}/{n} · 상태 {states}·첫 스텝 속도(밀기 방향) {v1:+0.00;-0.00} m/s · 질량 {brb.mass} · 플레이어-황소 거리 끝 {rel.magnitude:0.00} m(앞뒤 {Vector3.Dot(rel, dir):+0.00;-0.00} · 옆 {side:+0.00;-0.00}) · 가장 가까움 {minGap:0.00} m(황소 몸 반경 {bull.GetComponent<Collider>().bounds.extents.x:0.00})");
    }

    /// <summary>황소 앞이 NavMesh로 FreeAhead m 넘게 트인 방향(8방향 중 가장 트인 쪽).</summary>
    private static Vector3 FreeDir(Vector3 from)
    {
        Vector3 best = Vector3.forward; float bestFree = -1f;
        for (int i = 0; i < 8; i++)
        {
            Vector3 d = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
            float free = NavMesh.Raycast(from, from + d * FreeAhead, out var hit, NavMesh.AllAreas) ? Vector3.Distance(from, hit.position) : FreeAhead;
            float back = NavMesh.Raycast(from, from - d * 3f, out var hb, NavMesh.AllAreas) ? Vector3.Distance(from, hb.position) : 3f;
            if (back < 2f) continue;
            if (free > bestFree) { bestFree = free; best = d; }
        }
        return best;
    }
}
#endif
