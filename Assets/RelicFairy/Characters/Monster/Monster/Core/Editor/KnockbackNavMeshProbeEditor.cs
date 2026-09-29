using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using RelicFairy.Monster;

/// <summary>
/// [실측 도구 · 플레이 중] 넉백 뒤 몬스터가 <b>NavMesh로 돌아오는지</b> 잰다.
///
/// 09-24 Editor.log에서 「Failed to create agent … not close enough to the NavMesh」 32건이
/// <c>GetHitState.RestoreAgent</c>에서 났다. 넉백으로 NavMesh 밖에 밀린 몬스터가 경직이 끝날 때
/// 그 자리에서 에이전트를 켜면 에이전트가 안 만들어지고, 추격 상태는 NavMesh 밖이면 이동·공격을 건너뛰어
/// <b>제자리에서 달리기만</b> 한다. 이 도구는 그게 실제로 얼마나 일어나는지 센다.
///
/// 흐름: 테스트 허브에서 런 구조 자동 실측으로 Ch1 첫 전투방까지 간 뒤(몬스터를 죽이기 전에) 자동 실측을 멈추고,
/// 살아 있는 몬스터들을 무리 중심에서 바깥쪽(벽 방향)으로 넉백 배율 1·2·3, 그리고 3배 3연타로 민다.
/// 매번 1초 뒤 에이전트 상태·NavMesh까지 거리·경고 수를 적는다. 결과: Temp/knockback_navmesh_probe.txt
/// </summary>
public static class KnockbackNavMeshProbeEditor
{
    private const string AutoRoot   = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string OutPath    = "Temp/knockback_navmesh_probe.txt";
    private const string Title      = "넉백NavMesh";
    private const string AgentWarn  = "Failed to create agent";
    private const double RoomWait   = 90;     // 첫 전투방까지 기다리는 최대 시간
    private const double MonsterWait = 20;    // 몬스터가 나타나길 기다리는 최대 시간
    private const float  SettleSec  = 1.0f;   // 넉백 뒤 경직(0.4초) + 복구까지 넉넉히
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;

    private struct Case { public string name; public float mult; public int hits; }
    private static readonly Case[] Cases =
    {
        new Case { name = "배율 1",      mult = 1f, hits = 1 },
        new Case { name = "배율 2",      mult = 2f, hits = 1 },
        new Case { name = "배율 3",      mult = 3f, hits = 1 },
        new Case { name = "배율 3 ×3연타", mult = 3f, hits = 3 },
    };

    private enum Step { WaitRoom, WaitMonsters, Push, Settle, Done }

    private static Step   s_step;
    private static double s_since, s_next;
    private static int    s_case, s_hit, s_warns;
    private static StringBuilder s_sb;
    private static GameObject s_pusher;
    private static readonly List<MonsterBase> s_targets = new();
    private static readonly Dictionary<MonsterBase, Vector3> s_before = new();

    [MenuItem("RelicFairy/Debug/넉백 NavMesh 이탈 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[넉백NavMesh] 플레이 모드에서만 동작한다."); return; }
        if (Object.FindFirstObjectByType<TestHubLauncher>() == null) { Debug.LogWarning("[넉백NavMesh] 테스트 허브에서 시작한다."); return; }

        s_sb = new StringBuilder();
        ProbeOutput.Begin(OutPath, Title);
        s_step = Step.WaitRoom;
        s_since = EditorApplication.timeSinceStartup;
        s_case = 0;
        s_warns = 0;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (msg.Contains(AgentWarn)) s_warns++;
        // 자동 실측이 첫 절차 방에 들어서면 곧바로 멈춘다 — 몬스터를 죽이기 전에 넘겨받는다.
        if (s_step == Step.WaitRoom && msg.StartsWith("[RunAuto]") && msg.Contains("방 진입"))
        {
            EditorApplication.delayCall += () => EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
            s_step = Step.WaitMonsters;
            s_since = EditorApplication.timeSinceStartup;
        }
    }

    private static void Tick()
    {
        if (!Application.isPlaying) { Finish("플레이가 끝났다"); return; }
        double now = EditorApplication.timeSinceStartup;
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player != null) typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(player, true);

        switch (s_step)
        {
            case Step.WaitRoom:
                if (now - s_since > RoomWait) Finish("첫 전투방에 못 들어갔다");
                return;

            case Step.WaitMonsters:
                CollectTargets();
                if (s_targets.Count > 0 && now - s_since > 2.5)   // 등장 연출이 끝나고 NavMesh에 올라선 뒤
                {
                    s_sb.AppendLine($"대상 몬스터 {s_targets.Count}마리: {Names()}");
                    s_sb.AppendLine($"(방 진입 전까지 난 에이전트 경고 {s_warns}건 — 아래 수치에서 뺀다)");
                    s_warns = 0;
                    s_step = Step.Push;
                    s_hit = 0;
                    return;
                }
                if (now - s_since > MonsterWait) Finish("몬스터가 나오지 않았다(비전투 방?)");
                return;

            case Step.Push:
                if (now < s_next) return;
                if (s_hit == 0) BeginCase();
                PushAll(Cases[s_case].mult);
                s_hit++;
                if (s_hit < Cases[s_case].hits) { s_next = now + 0.15; return; }
                s_step = Step.Settle;
                s_next = now + SettleSec;
                return;

            case Step.Settle:
                if (now < s_next) return;
                RecordCase();
                s_case++;
                if (s_case >= Cases.Length) { Finish(null); return; }
                s_hit = 0;
                s_step = Step.Push;
                s_next = now + 0.3;
                return;
        }
    }

    private static void CollectTargets()
    {
        s_targets.Clear();
        foreach (var mb in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
        {
            if (mb.IsDead || !mb.isActiveAndEnabled || mb.Grade == MonsterGrade.Boss) continue;
            if (!mb.TryGetComponent<NavMeshAgent>(out var ag) || !ag.isActiveAndEnabled || !ag.isOnNavMesh) continue;
            s_targets.Add(mb);
        }
    }

    private static void BeginCase()
    {
        s_before.Clear();
        s_warns = 0;
        foreach (var mb in s_targets) if (mb != null && !mb.IsDead) s_before[mb] = mb.transform.position;

        // 무리 중심에서 바깥으로 민다 — 벽·가장자리 쪽으로 몰아붙이는 상황을 만든다.
        Vector3 c = Vector3.zero;
        foreach (var p in s_before.Values) c += p;
        c /= Mathf.Max(1, s_before.Count);
        if (s_before.Count == 1)   // 한 마리면 플레이어 반대쪽으로
        {
            var pl = GameRunBootstrapper.Instance?.Run?.Player;
            if (pl != null) c = pl.transform.position;
        }
        if (s_pusher == null) s_pusher = new GameObject("[KnockbackProbePusher]");
        s_pusher.transform.position = c;
    }

    private static void PushAll(float mult)
    {
        foreach (var mb in s_before.Keys)
        {
            if (mb == null || mb.IsDead) continue;
            if (mb.CurrentHp <= 1) continue;          // 1 피해로 죽이면 측정이 끝난다
            mb.TakeDamage(1f, s_pusher, mult);
        }
    }

    private static void RecordCase()
    {
        var cs = Cases[s_case];
        int off = 0, stuckFar = 0, n = 0;
        var lines = new StringBuilder();
        foreach (var kv in s_before)
        {
            var mb = kv.Key;
            if (mb == null || mb.IsDead) continue;
            n++;
            var pos = mb.transform.position;
            Vector3 d = pos - kv.Value; d.y = 0f;
            mb.TryGetComponent<NavMeshAgent>(out var ag);
            bool on = ag != null && ag.isActiveAndEnabled && ag.isOnNavMesh;
            string gap = "-";
            if (!on)
            {
                off++;
                if (NavMesh.SamplePosition(pos, out var hit, 10f, NavMesh.AllAreas))
                {
                    float g = Vector3.Distance(pos, hit.position);
                    gap = g.ToString("0.00") + "m";
                    if (g > 5f) stuckFar++;                  // 기존 스냅 도우미(5m)로도 못 돌아오는 거리
                }
                else { gap = ">10m"; stuckFar++; }
            }
            lines.AppendLine($"    {Clean(mb.name),-18} 밀린 거리 {d.magnitude,5:0.00}m · 높이 변화 {pos.y - kv.Value.y,6:0.00} · " +
                             $"에이전트 {(ag != null && ag.enabled ? "켜짐" : "꺼짐")} · NavMesh 위 {(on ? "O" : "X")} · NavMesh까지 {gap}");
        }
        s_sb.AppendLine($"── {cs.name} ── 살아 있는 {n}마리 중 NavMesh 밖 {off}마리(5m 밖 {stuckFar}) · 에이전트 경고 {s_warns}건");
        s_sb.Append(lines);
    }

    private static void Finish(string abortReason)
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        if (s_pusher != null) Object.Destroy(s_pusher);
        s_step = Step.Done;
        if (abortReason != null) s_sb?.AppendLine("중단: " + abortReason);
        string text = s_sb?.ToString() ?? "";
        ProbeOutput.Write(OutPath, Title, text);
        Debug.Log("[넉백NavMesh] 결과\n" + text);
    }

    private static string Names()
    {
        var sb = new StringBuilder();
        foreach (var mb in s_targets) sb.Append(Clean(mb.name)).Append(' ');
        return sb.ToString();
    }

    private static string Clean(string n) => n.Replace("(Clone)", "").Trim();
}
