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
/// 10-02 「한 장의 서약서」 E2 실측 — 문장 서약의 연쇄(즉시 · 쓰러지면 · 그동안 · 견디면 · 5절)가 실제 전투방에서 이어지는지.
/// 문장마다: 서약 목록을 비우고 그 문장 하나를 맺음 → 적 3마리를 곁으로 → 조건(연격 3타)을 건다 → 이음에 맞는 사건을 만든다
/// (쓰러지면 = 첫 대상을 처치 · 그동안 = 창 안에서 처치 · 견디면 = 무적이 끝나기를 기다림) → 일어난 절 번호를 적는다.
/// 결과: Temp/covenant_sentence_probe.txt · 로그 「[SentenceProbe] 끝」. ⚠️ 전투방(적이 있는 방)에서, 플레이 중.
/// </summary>
public static class CovenantSentenceProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;

    // (이름, 문장 id, 기대 절 번호, 사건)
    private static readonly (string name, string id, string expect, string evt)[] Cases =
    {
        ("즉시 3절",   "sen:streak@silver>supernova@silver>ember@silver~i>hemorrhage@silver~i",                 "0,1,2",     "none"),
        ("쓰러지면",   "sen:streak@silver>curse@silver>supernova@silver~d",                                "0,1",       "kill-first"),
        ("그동안",     "sen:streak@silver>fury@silver>supernova@silver~w",                                 "0,1",       "kill-any"),
        ("견디면",     "sen:streak@silver>aegis@silver>supernova@silver~e",                                "0,1",       "wait"),
        ("5절",        "sen:streak@silver>ember@silver>detonate@silver~i>fury@silver~i>supernova@silver~w>curse@silver~i", "0,1,2,3,4", "kill-any"),
    };

    [MenuItem("RelicFairy/Debug/10-02 서약서 연쇄 실측 (전투방, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            Debug.LogWarning("[SentenceProbe] 런 · 전투방에서 실행해야 한다.");
            return;
        }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("서약서 연쇄 실측\n");
        var run = GameRunBootstrapper.Instance.Run;
        var player = run.Player;
        var handler = run.CovenantHandler;
        typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(player, true);
        var list = (List<CovenantBase>)typeof(CovenantHandler).GetField("_covenants", Inst).GetValue(handler);
        int ok = 0;
        try
        {
            foreach (var (name, id, expect, evt) in Cases)
            {
                foreach (var c in list) c.Dispose();
                list.Clear();
                if (!handler.TryAdd(id)) { sb.AppendLine($"{name}: 맺기 실패 {id}"); continue; }
                var sen = list.Count > 0 ? list[0] as CovenantSentence : null;
                if (sen == null) { sb.AppendLine($"{name}: 문장이 아님"); continue; }

                var fired = new List<int>();
                sen.ClauseFired += i => fired.Add(i);

                var mons = Gather(player, 3);
                if (mons.Count == 0) { sb.AppendLine($"{name}: 적 없음"); break; }
                foreach (var m in mons) m.HpFloorMin1 = true;
                var target = mons[0];

                // 조건 — 같은 적 3타
                for (int k = 0; k < 3; k++) handler.OnAttackHit(target.gameObject, 10f);
                await UniTask.Delay(120, ignoreTimeScale: true);
                await Shot("SentenceHud_" + name.Replace(" ", ""));   // HUD 서약서 한 장 · 연쇄 표식

                switch (evt)
                {
                    case "kill-first":
                        Kill(target, player);
                        break;
                    case "kill-any":
                        await UniTask.Delay(300, ignoreTimeScale: true);
                        var victim = FirstAlive(mons);
                        if (victim != null) Kill(victim, player);
                        break;
                    case "wait":
                        await UniTask.Delay(1200, ignoreTimeScale: true);
                        break;
                }
                await UniTask.Delay(600, ignoreTimeScale: true);

                string got = string.Join(",", fired);
                bool pass = got == expect;
                if (pass) ok++;
                sb.AppendLine($"{(pass ? "통과" : "실패")} {name}: 일어난 절 [{got}] · 기대 [{expect}] · {sen.EffectText}");
                foreach (var m in mons) if (m != null) m.HpFloorMin1 = false;
                await UniTask.Delay(700, ignoreTimeScale: true);   // 연쇄 하한 · 쿨이 다음 문장에 걸리지 않게
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var c in list) c.Dispose();
            list.Clear();
            sb.AppendLine($"합계 {ok}/{Cases.Length}");
            File.WriteAllText("Temp/covenant_sentence_probe.txt", sb.ToString());
            Debug.Log("[SentenceProbe] 끝\n" + sb);
        }
    }

    private static UniTask Shot(string shotName)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { shotName, 0 }) : UniTask.CompletedTask;
    }

    private static void Kill(MonsterBase mb, PlayerController player)
    {
        if (mb == null || mb.IsDead) return;
        mb.HpFloorMin1 = false;
        mb.TakeDamage(mb.CurrentHp * 10f, player.gameObject, 0f);
    }

    private static MonsterBase FirstAlive(List<MonsterBase> mons)
    {
        foreach (var m in mons) if (m != null && !m.IsDead) return m;
        return null;
    }

    /// <summary>가까운 적 count마리를 플레이어 둘레 1.5 m로 옮긴다(NavMeshAgent는 Warp).</summary>
    private static List<MonsterBase> Gather(PlayerController player, int count)
    {
        var list = new List<MonsterBase>();
        foreach (var mb in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (!mb.IsDead && mb.Grade != MonsterGrade.Boss) list.Add(mb);
        Vector3 p = player.transform.position;
        list.Sort((a, b) => (a.transform.position - p).sqrMagnitude.CompareTo((b.transform.position - p).sqrMagnitude));
        if (list.Count > count) list.RemoveRange(count, list.Count - count);
        for (int i = 0; i < list.Count; i++)
        {
            float ang = i * Mathf.PI * 2f / count;
            Vector3 to = p + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 1.5f;
            if (list[i].TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var agent) && agent.isOnNavMesh) agent.Warp(to);
            else list[i].transform.position = to;
        }
        Physics.SyncTransforms();
        return list;
    }
}
#endif
