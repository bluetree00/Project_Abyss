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
/// 10-07 서약 U2 실측 — HUD 진척(연격 ●●○ · 심장박동 남은 초 · 포위 인접 수) · 발동 글자(「서약 발동」 · 「연쇄 ×N」) · 등급 소리 경로.
/// 문장마다 서약 목록을 비우고 맺은 뒤 사건을 한 단계씩 만들며 HUD를 찍는다. 플레이어 무적.
/// 결과: Temp/covenant_hud_progress.txt · Temp/ui_shots/U2_*.png · 로그 「[U2Probe] 끝」. ⚠️ 전투방(적이 있는 방)에서, 플레이 중.
/// </summary>
public static class CovenantHudProgressProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-07 서약 HUD 진척 실측 (전투방, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) return;
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("서약 HUD 진척 실측\n");
        var run = GameRunBootstrapper.Instance.Run;
        var player = run.Player;
        var handler = run.CovenantHandler;
        typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(player, true);
        var list = (List<CovenantBase>)typeof(CovenantHandler).GetField("_covenants", Inst).GetValue(handler);
        try
        {
            // A — 연격(골드) > 초신성(골드) > 잔불(루비): 1 · 2 · 3타 진척, 첫 발동 글자, 두 번째 연쇄 글자
            var a = Write(handler, list, "sen:streak@gold>supernova@gold>ember@ruby~i", sb);
            var mons = Gather(player, 3);
            if (a != null && mons.Count > 0)
            {
                foreach (var m in mons) m.HpFloorMin1 = true;
                var target = mons[0];
                await Wait(400); await Shot("U2_A0", a, sb);
                for (int k = 1; k <= 2; k++)
                {
                    handler.OnAttackHit(target.gameObject, 10f);
                    await Wait(250); await Shot($"U2_A{k}", a, sb);
                }
                handler.OnAttackHit(target.gameObject, 10f);
                await Wait(180); await Shot("U2_A3_fire", a, sb);
                await Wait(1500);
                for (int k = 0; k < 3; k++) handler.OnAttackHit(target.gameObject, 10f);
                await Wait(180); await Shot("U2_A4_chain", a, sb);
                foreach (var m in mons) if (m != null) m.HpFloorMin1 = false;
            }

            // B — 심장박동: 남은 초
            var b = Write(handler, list, "sen:heartbeat@silver>fury@silver", sb);
            if (b != null) { await Wait(1600); await Shot("U2_B_heartbeat", b, sb); }

            // C — 포위: 인접 수(2/3)
            var c = Write(handler, list, "sen:besiege@silver>aegis@silver", sb);
            if (c != null) { Gather(player, 2); await Wait(900); await Shot("U2_C_besiege", c, sb); }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var cv in list) cv.Dispose();
            list.Clear();
            File.WriteAllText("Temp/covenant_hud_progress.txt", sb.ToString());
            Debug.Log("[U2Probe] 끝\n" + sb);
        }
    }

    private static CovenantSentence Write(CovenantHandler handler, List<CovenantBase> list, string id, StringBuilder sb)
    {
        foreach (var cv in list) cv.Dispose();
        list.Clear();
        if (!handler.TryAdd(id)) { sb.AppendLine($"맺기 실패 {id}"); return null; }
        var s = list.Count > 0 ? list[0] as CovenantSentence : null;
        if (s == null) { sb.AppendLine($"문장이 아님 {id}"); return null; }
        s.ClauseFired += i => sb.AppendLine($"    절 {i} 일어남");
        sb.AppendLine($"맺음 {id}");
        return s;
    }

    private static async UniTask Shot(string name, CovenantSentence s, StringBuilder sb)
    {
        var p = s.CauseProgress;
        sb.AppendLine($"  {name}: 진척 {p.kind} {p.cur}/{p.max} · {p.seconds:0.0}초 · 절0 쿨 {s.ResultCooldownLeft(0):0.0}초");
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        if (m != null) await (UniTask)m.Invoke(null, new object[] { name, 0 });
    }

    private static UniTask Wait(int ms) => UniTask.Delay(ms, ignoreTimeScale: true);

    /// <summary>가까운 적 count마리를 플레이어 둘레 1.5 m로 옮긴다(NavMeshAgent는 Warp).</summary>
    private static List<MonsterBase> Gather(PlayerController player, int count)
    {
        var list = new List<MonsterBase>();
        foreach (var mb in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (!mb.IsDead && mb.Grade != MonsterGrade.Boss) list.Add(mb);
        Vector3 p = player.transform.position;
        list.Sort((x, y) => (x.transform.position - p).sqrMagnitude.CompareTo((y.transform.position - p).sqrMagnitude));
        if (list.Count > count) list.RemoveRange(count, list.Count - count);
        for (int i = 0; i < list.Count; i++)
        {
            float ang = i * Mathf.PI * 2f / Mathf.Max(1, list.Count);
            Vector3 to = p + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 1.5f;
            if (list[i].TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var ag) && ag.enabled) ag.Warp(to);
            else list[i].transform.position = to;
        }
        return list;
    }
}
#endif
