#if UNITY_EDITOR
using System;
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
/// 10-29 사용자 「여전히 플레이어가 보스나 몬스터들을 몸으로 밀어버릴 수 있다」 — 첫 전투방에서 일반 몬스터 하나 · 중간 보스(황소)에
/// 플레이어를 3 m 앞에서 6 m/s로 2초 밀어 넣는다(물리 단계마다 속도). 몬스터가 플레이어 미는 쪽으로 밀린 거리 · 가장 가까운 중심 거리 · 뚫고 지나갔는지.
/// 결과: Logs/boss_fight_sim/monster_push_*.txt · 로그 「[MonsterPush] 끝」.
/// </summary>
public static class MonsterPushProbeEditor
{
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool s_armed;

    [MenuItem("RelicFairy/Debug/10-29 몬스터 몸으로 밀기 실측 (테스트 허브, Play)")]
    private static void Begin()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[MonsterPush] 플레이 중에"); return; }
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
        string path = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim", $"monster_push_{DateTime.Now:MMdd_HHmmss}.txt");
        var sb = new StringBuilder("몬스터 몸으로 밀기 — 3 m 앞에서 6 m/s로 2초\n");
        try
        {
            var p = GameRunBootstrapper.Instance?.Run?.Player;
            if (p == null) { sb.AppendLine("런 플레이어 없음"); return; }
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            await UniTask.Delay(5000, DelayType.Realtime);
            var all = UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None).Where(m => m != null && !m.IsDead).ToList();
            var normal = all.FirstOrDefault(m => m.Grade == MonsterGrade.Common && !m.IsEliteBoss);
            foreach (var m in all) if (m != normal) m.gameObject.SetActive(false);
            if (normal != null) await Trial(p, normal, "일반 " + normal.name, sb);
            if (normal != null) normal.gameObject.SetActive(false);

            MonsterSpawner best = null; float bd = float.MaxValue;
            foreach (var sp in UnityEngine.Object.FindObjectsByType<MonsterSpawner>(FindObjectsSortMode.None))
            {
                if (sp == null || !sp.isActiveAndEnabled) continue;
                float d = (sp.transform.position - p.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = sp; }
            }
            var bull = best != null ? await best.SpawnMidBossAsync(BullTankMonster.Kind, p.GetCancellationTokenOnDestroy()) : null;
            if (bull != null)
            {
                for (int i = 0; i < 50 && bull.EffectiveMaxHp <= 0; i++) await UniTask.Delay(100, DelayType.Realtime);
                await UniTask.Delay(3500, DelayType.Realtime);   // 깨어남 포효
                await Trial(p, bull, "중간 보스 황소", sb);
                if (!bull.IsDead) bull.TakeSynergyDamage(bull.CurrentHp + 10, null, 1f);
            }
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[MonsterPush] 끝 — {path}\n{sb}");
        }
    }

    private static async UniTask Trial(PlayerController p, MonsterBase m, string label, StringBuilder sb)
    {
        var agent = m.GetComponent<NavMeshAgent>();
        float radius = agent != null ? agent.radius : 0.5f;
        Vector3 mPos = m.transform.position;
        Vector3 dir = Flat(p.transform.position - mPos);
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
        Vector3 start = mPos + dir * 3f;
        if (NavMesh.SamplePosition(start, out var s, 2f, NavMesh.AllAreas)) start = s.position;
        p.transform.position = start;
        p.Rigid.position = start; p.Rigid.linearVelocity = Vector3.zero;
        await UniTask.Delay(300, DelayType.Realtime);
        Vector3 m0 = m.transform.position;
        Vector3 push = Flat(m0 - p.Rigid.position).normalized;   // 플레이어가 미는 쪽
        float minD = float.MaxValue; bool passed = false;
        float t0 = Time.time;
        while (Time.time - t0 < 2f && m != null && !m.IsDead)
        {
            await UniTask.WaitForFixedUpdate();
            p.Rigid.linearVelocity = new Vector3(push.x * 6f, p.Rigid.linearVelocity.y, push.z * 6f);
            float d = Vector3.Distance(Flat(p.Rigid.position), Flat(m.transform.position));
            minD = Mathf.Min(minD, d);
            if (Vector3.Dot(Flat(p.Rigid.position - m.transform.position), push) > 0.1f) passed = true;   // 몬스터 너머로 나감
        }
        p.Rigid.linearVelocity = Vector3.zero;
        float shoved = Vector3.Dot(Flat(m.transform.position - m0), push);
        sb.AppendLine($"{label} — 반경 {radius:0.00} · 몬스터가 미는 쪽으로 밀림 {shoved:0.00} m · 가장 가까운 중심 거리 {minD:0.00} m(몸 반경 + 0.35 = {radius + 0.35f:0.00}) · 뚫고 지나감 {passed} · 상태 {typeof(MonsterBase).GetField("_debugState", Inst)?.GetValue(m)}");
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
#endif
