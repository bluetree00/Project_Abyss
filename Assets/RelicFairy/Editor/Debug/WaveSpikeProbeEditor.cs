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
using UnityEngine.Profiling;

/// <summary>
/// 10-29 풀링 점검 #8 · #9 앞 계측(2a) — 자동 실측(피해 모사)이 도는 동안 90초, 프레임마다 Recorder 표식(Instantiate · 활성화 · GC · 입자 · 스크립트)과
/// 살아 있는 몬스터 수 변화(그 프레임에 나온 수)를 읽는다. 50 ms 넘는 프레임에 표식 상위 · 스폰 수를 남기고, 스폰 프레임 · 아닌 프레임을 따로 요약한다.
/// 결과 Logs/boss_fight_sim/wave_spike_*.txt · 로그 「[WaveSpike] 끝」.
/// ⚠️ 에디터 프레임 시간은 에디터 몫이 커서(10-29 악몽 Ch4 몬스터 없이 중앙 71 ms) 전후 비교는 표식(ms) 쪽으로 · 프레임 자체는 개발 빌드에서.
/// </summary>
public static class WaveSpikeProbeEditor
{
    private const float RunSeconds = 90f, SpikeMs = 50f;
    private static readonly string[] Markers =
    {
        "Instantiate", "GameObject.Activate", "GameObject.Deactivate", "Destroy", "GC.Collect", "Shader.CreateGPUProgram",
        "ParticleSystem.Update", "ParticleSystem.Update2", "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate",
        "FixedUpdate.PhysicsFixedUpdate", "Loading.ReadObject", "AnimatorControllerPlayable.PrepareFrame",
    };

    [MenuItem("RelicFairy/Debug/10-29 풀링 #8 · #9 웨이브 프레임 계측 (자동 실측 중, Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[WaveSpike] 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        string path = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim", $"wave_spike_{DateTime.Now:MMdd_HHmmss}.txt");
        var sb = new StringBuilder($"웨이브 프레임 계측 — {StoryProgress.Era} · {RunSeconds}초 · {SpikeMs} ms 넘는 프레임\n");
        var recs = Markers.Select(n => (n, r: Recorder.Get(n))).ToList();
        foreach (var (_, r) in recs) r.enabled = true;
        // HashSet<T>는 비제네릭 ICollection이 아니다 — Count 속성을 직접 읽는다(10-29 첫 판 몬스터 0으로 읽힘)
        var set = typeof(MonsterBase).GetField("_activeMonsters", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
        var countProp = set?.GetType().GetProperty("Count");
        int Active() => countProp != null ? (int)countProp.GetValue(set) : 0;
        try
        {
            float start = Time.realtimeSinceStartup;
            int frames = 0, spikes = 0, spikesOnSpawn = 0, lastCount = Active(), spawned = 0, peak = 0;
            var dtSpawn = new List<float>(); var dtCalm = new List<float>();
            var inst = new List<float>();
            var blame = new Dictionary<string, int>();
            while (Time.realtimeSinceStartup - start < RunSeconds)
            {
                await UniTask.Yield(PlayerLoopTiming.Update);
                frames++;
                float dt = Time.unscaledDeltaTime * 1000f;
                int count = Active(), born = Mathf.Max(0, count - lastCount);
                spawned += born; peak = Mathf.Max(peak, count);
                (born > 0 ? dtSpawn : dtCalm).Add(dt);
                float instMs = recs[0].r.isValid ? recs[0].r.elapsedNanoseconds / 1e6f : 0f;
                inst.Add(instMs);
                if (dt > SpikeMs)
                {
                    spikes++;
                    if (born > 0) spikesOnSpawn++;
                    var top = recs.Select(x => (x.n, ms: x.r.isValid ? x.r.elapsedNanoseconds / 1e6f : -1f)).Where(x => x.ms > 1f).OrderByDescending(x => x.ms).Take(5).ToList();
                    if (top.Count > 0) blame[top[0].n] = blame.TryGetValue(top[0].n, out int c) ? c + 1 : 1;
                    sb.AppendLine($"  [{Time.realtimeSinceStartup - start,6:0.00}] {dt:0} ms · 몬스터 {count}(+{born}) · {string.Join(" · ", top.Select(x => $"{x.n} {x.ms:0.0}"))}");
                }
                lastCount = count;
            }
            string Stat(List<float> v) { if (v.Count == 0) return "-"; v.Sort(); return $"{v.Count}프레임 · 중앙 {v[v.Count / 2]:0.0} · 99% {v[(int)(v.Count * 0.99f)]:0.0} · 최대 {v[^1]:0} ms"; }
            inst.Sort();
            sb.Insert(0, $"요약 — 프레임 {frames} · 나온 몬스터 {spawned} · 동시 최대 {peak} · 튐 {spikes}번(스폰 프레임 {spikesOnSpawn})\n" +
                         $"  스폰 프레임: {Stat(dtSpawn)}\n  나머지: {Stat(dtCalm)}\n" +
                         $"  Instantiate 표식 99% {inst[(int)(inst.Count * 0.99f)]:0.0} ms · 최대 {inst[^1]:0.0} ms\n" +
                         $"  튐 프레임 1위 표식: {string.Join(" · ", blame.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"))}\n");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var (_, r) in recs) r.enabled = false;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[WaveSpike] 끝\n{sb}");
        }
    }
}
#endif
