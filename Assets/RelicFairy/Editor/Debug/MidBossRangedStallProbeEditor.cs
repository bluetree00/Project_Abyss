#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 10-26 사용자 「여전히 원거리 공격 중에 중간보스가 움직이지 않는다」(10-25 ② 「화살로 때리다 보면 특정 거리에서 멈춘다」) — 첫 전투방에서
/// 중간 보스(황소)를 실제 길(<see cref="MonsterSpawner.SpawnMidBossAsync"/>)로 내고 멀리 세운 뒤 0.4초마다 화살 피격(경직 없음)을 넣는다.
/// 가: 플레이어 제자리 25초 · 나: 황소가 8 m 안에 오면 뒤로 물러나기 25초. 0.25초마다 상태 · 거리 · 이동 · 에이전트 · 쿨을 적고
/// 「추격 상태인데 4.6 m 밖에서 1초 넘게 거의 안 움직임」 구간을 따로 모은다. 결과: Logs/boss_fight_sim/midboss_ranged_*.txt · 로그 「[MidBossRanged] 끝」.
/// </summary>
public static class MidBossRangedStallProbeEditor
{
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const float PhaseSeconds = 25f, ShotEvery = 0.4f, LogEvery = 0.25f, KiteNear = 8f;
    private static bool s_armed;

    [MenuItem("RelicFairy/Debug/10-26 중간보스 원거리 멈춤 재현 (테스트 허브, Play)")]
    private static void Begin()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[MidBossRanged] 플레이 중에"); return; }
        s_armed = true;
        Application.logMessageReceived -= OnRunAuto;
        Application.logMessageReceived += OnRunAuto;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    private static void OnRunAuto(string msg, string stack, LogType type)
    {
        if (!s_armed || !msg.StartsWith("[RunAuto]") || !msg.Contains("방 진입")) return;
        s_armed = false;
        Application.logMessageReceived -= OnRunAuto;
        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
            RunAsync().Forget();
        };
    }

    private static async UniTaskVoid RunAsync()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"midboss_ranged_{DateTime.Now:MMdd_HHmmss}.txt");
        var sb = new StringBuilder("중간 보스 원거리 멈춤 재현 — 0.4초마다 화살 피격(경직 없음)\n");
        var events = new List<string>();
        float t0 = Time.time;
        void OnLog(string m, string s, LogType ty)
        {
            if (m.StartsWith("[BullTank]") || ty == LogType.Error || ty == LogType.Exception)
                events.Add($"[{Time.time - t0,6:0.00}] {m.Split('\n')[0]}");
        }
        Application.logMessageReceived += OnLog;
        try
        {
            var p = GameRunBootstrapper.Instance?.Run?.Player;
            if (p == null) { sb.AppendLine("런 플레이어 없음"); return; }
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            await UniTask.Delay(5000, DelayType.Realtime);
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (m != null && !m.IsDead) m.gameObject.SetActive(false);

            MonsterSpawner best = null; float bd = float.MaxValue;
            foreach (var sp in UnityEngine.Object.FindObjectsByType<MonsterSpawner>(FindObjectsSortMode.None))
            {
                if (sp == null || !sp.isActiveAndEnabled) continue;
                float d = (sp.transform.position - p.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = sp; }
            }
            if (best == null) { sb.AppendLine("스포너 없음"); return; }
            var bull = await best.SpawnMidBossAsync(BullTankMonster.Kind, p.GetCancellationTokenOnDestroy());
            if (bull == null) { sb.AppendLine("황소 스폰 실패"); return; }
            for (int i = 0; i < 50 && bull.EffectiveMaxHp <= 0; i++) await UniTask.Delay(100, DelayType.Realtime);
            await UniTask.Delay(1500, DelayType.Realtime);
            var agent = bull.GetComponent<NavMeshAgent>();
            Vector3 far = FarSpot(p.transform.position, 16f, out float farDist);
            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.Warp(far); else bull.transform.position = far;
            sb.AppendLine($"스폰 {bull.name} · 체력 {bull.EffectiveMaxHp} · 시작 거리 {Dist(p, bull):0.0} m(가장 먼 자리 {farDist:0.0})");
            await UniTask.Delay(500, DelayType.Realtime);

            var stalls = new List<string>();
            var gaps = new List<float>();          // 패턴이 끝나고 다음 패턴이 시작하기까지(초)
            var starts = new List<float>();        // 패턴 시작 시각
            float stallTotal = 0f;
            string prevSt = null; Vector3 prevPos = bull.transform.position; float lastEnd = -1f;
            foreach (var phase in new[] { "가 제자리", "나 물러나기" })
            {
                sb.AppendLine($"■ {phase}");
                float start = Time.time, nextShot = 0f, nextLog = 0f;
                Vector3 lastPos = bull.transform.position;
                float stillSince = -1f; string stillInfo = null;
                while (Time.time - start < PhaseSeconds && bull != null && !bull.IsDead)
                {
                    float t = Time.time - start;
                    // 프레임마다 — 패턴 시작 · 끝 시각 · 도약이 끝나는 프레임의 몸 이동(이륙점으로 되돌아가는지)
                    string cur = typeof(MonsterBase).GetField("_debugState", Inst)?.GetValue(bull) as string;
                    bool curPat = cur == "SwingState" || cur == "LeapState" || cur == "ChargeState";
                    bool prevPat = prevSt == "SwingState" || prevSt == "LeapState" || prevSt == "ChargeState";
                    if (cur != prevSt)
                    {
                        float now0 = Time.time - t0;
                        if (curPat && !prevPat) { starts.Add(now0); if (lastEnd >= 0f) gaps.Add(now0 - lastEnd); }
                        if (prevPat && !curPat) lastEnd = now0;
                        if (prevSt == "LeapState")
                            events.Add($"[{now0,6:0.00}] 도약 끝 → {cur} · 이 프레임 몸 이동 {Vector3.Distance(Flat(prevPos), Flat(bull.transform.position)):0.00} m · 플레이어까지 {Dist(p, bull):0.0} m");
                        if (curPat) events.Add($"[{now0,6:0.00}] 패턴 시작 {cur} · 거리 {Dist(p, bull):0.0} m");
                        prevSt = cur;
                    }
                    prevPos = bull.transform.position;
                    if (t >= nextShot)
                    {
                        nextShot += ShotEvery;
                        MonsterPoise.PendingNoFlinch = true;
                        try { bull.TakeDamage(1f, p.gameObject); }
                        finally { MonsterPoise.PendingNoFlinch = false; }
                    }
                    if (phase.StartsWith("나") && Dist(p, bull) < KiteNear)
                    {
                        Vector3 spot = FarSpot(bull.transform.position, 14f, out float got);
                        p.transform.position = spot;
                        if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = spot; rb.linearVelocity = Vector3.zero; }
                        events.Add($"[{Time.time - t0,6:0.00}] 플레이어 물러남 → 황소에서 {got:0.0} m");
                    }
                    var rs = p.RuntimeStats;
                    if (rs != null && rs.Hp < rs.MaxHp) rs.SetHp(rs.MaxHp);
                    if (t >= nextLog)
                    {
                        nextLog += LogEvery;
                        float moved = Vector3.Distance(Flat(bull.transform.position), Flat(lastPos));
                        lastPos = bull.transform.position;
                        string st = typeof(MonsterBase).GetField("_debugState", Inst)?.GetValue(bull) as string;
                        float now = Time.time;
                        float F(string f) => bull.GetType().GetField(f, Inst)?.GetValue(bull) is float v ? v - now : float.NaN;
                        bool awake = bull.GetType().GetField("_awake", Inst)?.GetValue(bull) is bool b && b;
                        string nav = agent == null ? "에이전트 없음"
                            : !agent.isActiveAndEnabled ? "에이전트 꺼짐"
                            : $"내비 {agent.isOnNavMesh} · 위치동기 {agent.updatePosition} · 멈춤 {(agent.isOnNavMesh && agent.isStopped)} · 경로 {(agent.isOnNavMesh ? agent.pathStatus.ToString() : "-")}/{agent.hasPath} · 남은 {(agent.isOnNavMesh ? agent.remainingDistance : -1f):0.0} · 속도 {agent.velocity.magnitude:0.0} · 정지거리 {agent.stoppingDistance:0.0} · 몸↔에이전트 {Vector3.Distance(Flat(agent.nextPosition), Flat(bull.transform.position)):0.00}";
                        string line = $"  t={t,5:0.00} {st,-14} 거리 {Dist(p, bull),5:0.0} · 0.25초 이동 {moved:0.00} · {nav} · CC {bull.IsCcActive} · 휘청 {bull.Poise?.IsBroken} · 깸 {awake} · 다음 행동 {F("_nextActAt"):0.0} · 돌진 쿨 {F("_chargeReadyAt"):0.0} · 도약 쿨 {F("_leapReadyAt"):0.0}";
                        sb.AppendLine(line);
                        bool still = st == nameof(ChaseState) && Dist(p, bull) > 4.0f && moved < 0.08f;
                        if (still) { if (stillSince < 0f) { stillSince = t; stillInfo = line; } }
                        else
                        {
                            if (stillSince >= 0f && t - stillSince >= 1f) { stalls.Add($"{phase} t={stillSince:0.00}~{t:0.00}({t - stillSince:0.0}초) 시작:{stillInfo}"); stallTotal += t - stillSince; }
                            stillSince = -1f;
                        }
                    }
                    await UniTask.Yield();
                }
                if (stillSince >= 0f && Time.time - start - stillSince >= 1f) { stalls.Add($"{phase} t={stillSince:0.00}~끝 시작:{stillInfo}"); stallTotal += Time.time - start - stillSince; }
            }
            var startGaps = starts.Zip(starts.Skip(1), (a, b) => b - a).ToList();
            sb.AppendLine($"■ 요약 — 패턴 {starts.Count}회 · 패턴 시작 간격 평균 {(startGaps.Count > 0 ? startGaps.Average() : 0f):0.00}초 · 끝 → 다음 시작 평균 {(gaps.Count > 0 ? gaps.Average() : 0f):0.00}초(최대 {(gaps.Count > 0 ? gaps.Max() : 0f):0.00}) · 멈춤 합 {stallTotal:0.0}초 / {PhaseSeconds * 2:0}초");
            sb.AppendLine($"■ 멈춤 구간(추격 상태 · 4 m 밖 · 1초 넘게 거의 안 움직임) {stalls.Count}개");
            foreach (var s in stalls) sb.AppendLine("  " + s);
            sb.AppendLine("■ 사건");
            foreach (var e in events) sb.AppendLine("  " + e);
            if (bull != null && !bull.IsDead) bull.TakeSynergyDamage(bull.CurrentHp + 10, null, 1f);
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Application.logMessageReceived -= OnLog;
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[MidBossRanged] 끝 — {path}");
        }
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    private static float Dist(PlayerController p, MonsterBase m) => Vector3.Distance(Flat(p.transform.position), Flat(m.transform.position));

    /// <summary><paramref name="from"/>에서 걸어서 이어진 가장 먼 바닥(16방향 · 40 m부터 4 m씩) — <paramref name="minFar"/> 밖, 경로가 완전한 자리만.</summary>
    private static Vector3 FarSpot(Vector3 from, float minFar, out float best)
    {
        best = 0f;
        Vector3 at = from;
        var path = new NavMeshPath();
        for (int i = 0; i < 16; i++)
        {
            Vector3 d = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
            for (float r = 40f; r >= minFar; r -= 4f)
            {
                if (!NavMesh.SamplePosition(from + d * r, out var s, 2f, NavMesh.AllAreas)) continue;
                if (!NavMesh.CalculatePath(from, s.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                float dist = Vector3.Distance(Flat(from), Flat(s.position));
                if (dist > best) { best = dist; at = s.position; }
                break;
            }
        }
        return at;
    }
}
#endif
