#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 10-29 F5 해방기 황소 「사슬 휘두르기」 확인 — 해방기 오버라이드 · 테스트 허브에서. 첫 전투방에서 황소를 실제 길(SpawnMidBossAsync)로 내고
/// 가: 플레이어가 황소 둘레 3.5 m를 초당 80°로 35초 돈다(옆 · 뒤) · 나: 정면 2.5 m에 15초 선다. 패턴 시작 종류 · 사슬 판정(원 안) · 사슬 시작 때 플레이어 각도를 적고
/// 사슬 예고 0.7초에 두 장. 결과 Logs/boss_fight_sim/f5_chain_*.txt · Temp/f5/chain_*.png · 로그 「[F5Chain] 끝」.
/// </summary>
public static class F5ChainProbeEditor
{
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool s_armed;

    [MenuItem("RelicFairy/Debug/10-29 F5 황소 사슬 휘두르기 확인 (테스트 허브 · 해방기, Play)")]
    private static void Begin()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[F5Chain] 플레이 중에"); return; }
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
        Directory.CreateDirectory("Temp/f5");
        string path = Path.Combine(dir, $"f5_chain_{DateTime.Now:MMdd_HHmmss}.txt");
        var sb = new StringBuilder($"황소 사슬 휘두르기 — 시기 {StoryProgress.Era}\n");
        int chainHits = 0, chainStrikes = 0;
        void OnLog(string m, string s, LogType ty)
        {
            if (!m.StartsWith("[BullTank] 사슬 휘두르기 판정")) return;
            chainStrikes++;
            if (m.Contains("명중")) chainHits++;
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
            await UniTask.Delay(2500, DelayType.Realtime);   // 깨어남 포효
            sb.AppendLine($"스폰 {bull.name} · {bull.DisplayName}");

            var stateField = typeof(MonsterBase).GetField("_debugState", Inst);
            int shots = 0;
            foreach (var (label, seconds, orbit) in new[] { ("가 둘레 돌기 3.5 m · 초당 80°", 35f, true), ("나 정면 2.5 m", 15f, false) })
            {
                var counts = new Dictionary<string, int>();
                var angles = new List<float>();
                string prev = null;
                float start = Time.time, theta = 0f, chainAt = -1f;
                while (Time.time - start < seconds && bull != null && !bull.IsDead)
                {
                    Vector3 c = bull.transform.position;
                    Vector3 at;
                    if (orbit) { theta += 80f * Time.deltaTime; at = c + Quaternion.Euler(0f, theta, 0f) * Vector3.forward * 3.5f; }
                    else at = c + Flat(bull.transform.forward).normalized * 2.5f;
                    Place(p, at, c);

                    string cur = stateField?.GetValue(bull) as string;
                    if (cur != prev && cur != null && cur.EndsWith("State") && cur != "ChaseState" && cur != "IdleState")
                    {
                        counts[cur] = counts.TryGetValue(cur, out int n) ? n + 1 : 1;
                        if (cur == "ChainSwingState")
                        {
                            angles.Add(Vector3.Angle(bull.transform.forward, Flat(p.transform.position - c)));
                            chainAt = Time.realtimeSinceStartup;
                        }
                    }
                    if (chainAt > 0f && shots < 2 && Time.realtimeSinceStartup - chainAt >= 0.7f)
                    {
                        ScreenCapture.CaptureScreenshot($"Temp/f5/chain_{++shots}.png");
                        chainAt = -1f;
                    }
                    prev = cur;
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }
                sb.AppendLine($"■ {label}");
                foreach (var kv in counts) sb.AppendLine($"  {kv.Key} {kv.Value}");
                if (angles.Count > 0) sb.AppendLine($"  사슬 시작 때 플레이어 각도(정면 0°) {string.Join(" · ", angles.ConvertAll(a => a.ToString("0")))}");
            }
            sb.AppendLine($"사슬 판정 {chainStrikes} · 원 안 {chainHits}");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Application.logMessageReceived -= OnLog;
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[F5Chain] 끝\n{sb}");
        }
    }

    private static void Place(PlayerController p, Vector3 at, Vector3 lookAt)
    {
        if (Physics.Raycast(at + Vector3.up * 6f, Vector3.down, out var hit, 20f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore)) at.y = hit.point.y + 0.05f;
        var look = Flat(lookAt - at);
        var rot = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look.normalized) : p.transform.rotation;
        p.transform.SetPositionAndRotation(at, rot);
        if (p.Rigid != null) { p.Rigid.position = at; p.Rigid.rotation = rot; p.Rigid.linearVelocity = Vector3.zero; }
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
#endif
