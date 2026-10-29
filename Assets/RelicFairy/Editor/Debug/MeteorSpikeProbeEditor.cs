#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// 10-29 47 · 2a — 화룡전 프레임 튐(130~600 ms)이 화살 · 폭발 없는 구간(하늘 · 운석 즈음)에서 났다 → 무엇이 그 프레임을 먹는지.
/// 전투 시뮬이 돌고 있는 화룡방에서 90초 동안 프레임마다 Recorder로 주요 표식 시간(GC · Instantiate · 셰이더 첫 컴파일 · 입자 · 스크립트 · 물리 · 그리기)을 읽고,
/// 80 ms 넘는 프레임에 표식 상위 · 예고 칸 풀 수 · GC 횟수 변화 · 탑다운 · 그 즈음 로그를 남긴다. 결과: Logs/boss_fight_sim/meteor_spike_*.txt · 로그 「[MeteorSpike] 끝」.
/// </summary>
public static class MeteorSpikeProbeEditor
{
    private const float RunSeconds = 90f, SpikeMs = 80f;
    private static readonly string[] Markers =
    {
        "GC.Collect", "Instantiate", "Shader.CreateGPUProgram", "ParticleSystem.Update", "ParticleSystem.Update2",
        "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate", "FixedUpdate.PhysicsFixedUpdate",
        "FixedUpdate.ScriptRunBehaviourFixedUpdate", "PostLateUpdate.FinishFrameRendering", "Camera.Render", "Loading.ReadObject",
        "GameObject.Activate", "GameObject.Deactivate", "Destroy", "Light.Render",
    };

    [MenuItem("RelicFairy/Boss/Fight Sim/10-29 화룡 운석 프레임 튐 (Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[MeteorSpike] 화룡 전투 시뮬 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        string path = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim", $"meteor_spike_{DateTime.Now:MMdd_HHmmss}.txt");
        var sb = new StringBuilder($"화룡 운석 프레임 튐 — {RunSeconds}초 · {SpikeMs} ms 넘는 프레임\n");
        var recent = new Queue<string>();
        void OnLog(string m, string s, LogType ty)
        {
            if (!(m.StartsWith("[Dragon") || m.StartsWith("[Boss") || ty == LogType.Error || ty == LogType.Exception)) return;
            recent.Enqueue($"{Time.realtimeSinceStartup:0.00} {m.Split('\n')[0]}");
            while (recent.Count > 6) recent.Dequeue();
        }
        Application.logMessageReceived += OnLog;
        var recs = Markers.Select(n => (n, r: Recorder.Get(n))).ToList();
        foreach (var (_, r) in recs) r.enabled = true;
        try
        {
            var quadRoot = GameObject.Find("@QuadTilePool")?.transform;
            var cam = GameCameraController.Instance;
            int lastQuads = quadRoot != null ? quadRoot.childCount : 0, lastGc = GC.CollectionCount(0);
            float start = Time.realtimeSinceStartup;
            int frames = 0, spikes = 0;
            var dts = new List<float>();
            var blame = new Dictionary<string, int>();
            while (Time.realtimeSinceStartup - start < RunSeconds)
            {
                await UniTask.Yield(PlayerLoopTiming.Update);
                frames++;
                float dt = Time.unscaledDeltaTime * 1000f;
                dts.Add(dt);
                int quads = quadRoot != null ? quadRoot.childCount : 0, gc = GC.CollectionCount(0);
                if (dt > SpikeMs)
                {
                    spikes++;
                    var top = recs.Select(x => (x.n, ms: x.r.isValid ? x.r.elapsedNanoseconds / 1e6f : -1f)).Where(x => x.ms > 1f).OrderByDescending(x => x.ms).Take(5).ToList();
                    if (top.Count > 0) blame[top[0].n] = blame.TryGetValue(top[0].n, out int c) ? c + 1 : 1;
                    sb.AppendLine($"  [{Time.realtimeSinceStartup - start,6:0.00}] 프레임 {dt:0} ms · 예고 칸 풀 {quads}(+{quads - lastQuads}) · GC +{gc - lastGc} · 탑다운 {(cam != null && cam.IsDragonTopDownActive)} · 표식 {string.Join(" · ", top.Select(x => $"{x.n} {x.ms:0.0}"))}");
                    foreach (var l in recent) sb.AppendLine("        " + l);
                }
                lastQuads = quads; lastGc = gc;
            }
            dts.Sort();
            sb.Insert(0, $"요약 — 프레임 {frames} · 중앙 {dts[dts.Count / 2]:0.0} ms · 99% {dts[(int)(dts.Count * 0.99f)]:0.0} ms · 최대 {dts[^1]:0} ms · 튐 {spikes}번 · 튐 프레임 1위 표식: {string.Join(" · ", blame.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"))}\n");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var (_, r) in recs) r.enabled = false;
            Application.logMessageReceived -= OnLog;
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[MeteorSpike] 끝 — {path}");
        }
    }
}
#endif
