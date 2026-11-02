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
using UnityEngine.SceneManagement;

/// <summary>
/// 11-02 Q27 사용자 「리치 패턴들이 프레임 드랍을 많이 낸다 — 페이지 전환 · 연출 · 이펙트 · 플레이어 추락 때 이펙트를 잘 볼 것」.
/// 리치 보스방(플레이 중)에서: 등장 대사를 넘겨 깨우고 플레이어를 무적으로 둔 뒤, 페이지마다 <see cref="PageSeconds"/>초 싸움을 지켜보고
/// 「다음 페이지」(정상 피해 경로)로 넘긴다 — 전환 연출 · 무대 변화 · 봉인 의식 · 추방(추락)까지. 프레임마다 Recorder로 주요 표식 시간을 읽고,
/// <see cref="SpikeMs"/> 넘는 프레임에 그때 리치 상태 · 표식 상위 · 살아 있는 입자계 수 · 그 즈음 로그를 남긴다. 구간(상태 묶음)별 중앙 · 99% · 최대 · 튐 수.
/// 결과: Logs/boss_fight_sim/lich_spike_*.txt · 로그 「[LichSpike] 끝」.
/// </summary>
public static class LichSpikeProbeEditor
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const float PageSeconds = 40f, SpikeMs = 50f, TailSeconds = 75f, RunCap = 420f;
    private static readonly string[] Markers =
    {
        "GC.Collect", "Instantiate", "Shader.CreateGPUProgram", "ParticleSystem.Update", "ParticleSystem.Update2",
        "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate", "FixedUpdate.PhysicsFixedUpdate",
        "PostLateUpdate.FinishFrameRendering", "Camera.Render", "Loading.ReadObject", "Loading.AwakeFromLoad",
        "GameObject.Activate", "GameObject.Deactivate", "Destroy", "Light.Render", "Mesh.Bake Scaled Mesh PhysX CollisionData",
        "AsyncUploadManager.ScheduleAsyncRead", "Shadows.RenderShadowmap",
    };

    [MenuItem("RelicFairy/Boss/Fight Sim/11-02 리치 프레임 튐 — 페이지 · 전환 · 추락 (보스방, Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[LichSpike] 리치 보스방 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        string path = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim", $"lich_spike_{DateTime.Now:MMdd_HHmmss}.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var sb = new StringBuilder($"리치 프레임 튐 — 페이지마다 {PageSeconds}초 · {SpikeMs} ms 넘는 프레임\n");
        var recent = new Queue<string>();
        void OnLog(string m, string s, LogType ty)
        {
            if (!(m.StartsWith("[Lich") || m.StartsWith("[Exile") || m.StartsWith("[Boss") || m.StartsWith("[Seal") || ty == LogType.Error || ty == LogType.Exception)) return;
            recent.Enqueue($"{Time.realtimeSinceStartup:0.00} {m.Split('\n')[0]}");
            while (recent.Count > 5) recent.Dequeue();
        }
        Application.logMessageReceived += OnLog;
        var recs = Markers.Select(n => (n, r: Recorder.Get(n))).ToList();
        foreach (var (_, r) in recs) r.enabled = true;
        var segs = new List<(string name, List<float> dts, int spikes)>();
        try
        {
            var lich = UnityEngine.Object.FindFirstObjectByType<LichMonster>();
            if (lich == null) { sb.AppendLine("리치 없음 — 리치 보스방에서"); return; }

            // 깨우기 — 등장 대사를 넘긴다(대사창이 넘김 입력을 기다린다)
            var run = GameRunBootstrapper.Instance?.Run;
            float w0 = Time.realtimeSinceStartup, lastSkip = 0f;
            TestHubDebugMenu.StepIntoBossEntrance();
            await UniTask.Delay(300, DelayType.Realtime);
            TestHubDebugMenu.StepPastBossEntrance();
            await UniTask.Delay(500, DelayType.Realtime);
            // 리치는 감지 거리 안에 들어와야 깬다 — 입구에선 멀었다(11-02 첫 실측: 70초 내내 잠듦). 리치 앞 6 m에 세운다
            var pl = run?.Player;
            if (pl != null)
            {
                Vector3 side = pl.transform.position - lich.transform.position; side.y = 0f;
                Vector3 to = lich.transform.position + (side.sqrMagnitude > 0.01f ? side.normalized : Vector3.back) * 6f;
                to.y = pl.transform.position.y;
                if (pl.TryGetComponent<Rigidbody>(out var prb)) { prb.position = to; prb.linearVelocity = Vector3.zero; }
                pl.transform.position = to;
            }
            while (Time.realtimeSinceStartup - w0 < 70f)
            {
                if (Time.realtimeSinceStartup - w0 > 3f && run != null && !run.InCutscene && StateOf(lich) != "LichDormantState") break;
                if (Time.realtimeSinceStartup - lastSkip > 1.2f && UnityEngine.Object.FindFirstObjectByType<UI_DialoguePopup>() != null) { lastSkip = Time.realtimeSinceStartup; TestHubDebugMenu.AdvanceDialogue(); }
                await UniTask.Delay(100, DelayType.Realtime);
            }
            sb.AppendLine($"깨움 {Time.realtimeSinceStartup - w0:0.0}초 · 컷신 {run?.InCutscene} · 상태 {StateOf(lich)}");

            var player = run?.Player;
            if (player != null) typeof(PlayerController).GetField("debugInvincible", Any)?.SetValue(player, true);

            float start = Time.realtimeSinceStartup, pageAt = start, lastSkip2 = 0f;
            int page = 1, lastGc = GC.CollectionCount(0), pushes = 0;
            string seg = "페이지1";
            var cur = (name: seg, dts: new List<float>(), spikes: 0);
            bool lichGone = false;
            float lastStoneHit = 0f;
            float goneAt = -1f;
            float lastBreakdown = -10f;
            while (Time.realtimeSinceStartup - start < RunCap)
            {
                await UniTask.Yield(PlayerLoopTiming.Update);
                float now = Time.realtimeSinceStartup, dt = Time.unscaledDeltaTime * 1000f;
                string st = lich != null ? StateOf(lich) : "(리치 없음)";
                bool exiling = s_exileActive?.GetValue(null) is true;
                string want = exiling ? "추방 추락" : lichGone ? "봉인 의식 · 그 뒤" : SegmentOf(st, page);
                if (want != cur.name) { segs.Add(cur); cur = (want, new List<float>(), 0); sb.AppendLine($"[{now - start,6:0.0}] ── {want} ({st})"); }
                cur.dts.Add(dt);
                int gc = GC.CollectionCount(0);
                if (dt > SpikeMs)
                {
                    cur.spikes++;
                    var top = recs.Select(x => (x.n, ms: x.r.isValid ? x.r.elapsedNanoseconds / 1e6f : -1f)).Where(x => x.ms > 1f).OrderByDescending(x => x.ms).Take(5).ToList();
                    var playing = UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Where(p => p.isPlaying).ToList();
                    int ps = playing.Count;
                    sb.AppendLine($"  [{now - start,6:0.00}] {dt:0} ms · {st} · 입자계 {ps} · GC +{gc - lastGc} · 표식 {string.Join(" · ", top.Select(x => $"{x.n} {x.ms:0.0}"))}");
                    foreach (var l in recent) sb.AppendLine("        " + l);
                    // 11-02 Q27 (a) 이펙트 양 끊김 — 입자계가 많을 때 2초에 한 번, 이펙트(입자계를 가진 가장 위 부모)별 개수 · 살아 있는 입자 수 상위
                    if (ps >= 100 && now - lastBreakdown > 2f)
                    {
                        lastBreakdown = now;
                        foreach (var g in playing.GroupBy(EffectRootName).Select(g => (name: g.Key, n: g.Count(), live: g.Sum(p => p.particleCount)))
                                                 .OrderByDescending(x => x.n).Take(10))
                            sb.AppendLine($"        · 이펙트 {g.name} — 입자계 {g.n} · 입자 {g.live}");
                    }
                }
                lastGc = gc;

                // 대사 · 컷신은 넘긴다(의식 · 추방 대사도)
                if (run != null && run.InCutscene && now - lastSkip2 > 1.5f && UnityEngine.Object.FindFirstObjectByType<UI_DialoguePopup>() != null) { lastSkip2 = now; TestHubDebugMenu.AdvanceDialogue(); }
                // 페이지마다 PageSeconds 뒤 다음 페이지로(전환 중이면 기다린다)
                if (lich != null && !lich.IsDead && now - pageAt > PageSeconds && !st.Contains("Phase2Entry") && !st.Contains("Dormant") && !(run?.InCutscene ?? false))
                {
                    pageAt = now; page++; pushes++;
                    TestHubDebugMenu.KillBoss();
                    sb.AppendLine($"[{now - start,6:0.0}] ▶ 다음 페이지 밀기 {pushes}");
                }
                if (!lichGone && (lich == null || lich.IsDead)) { lichGone = true; goneAt = now; sb.AppendLine($"[{now - start,6:0.0}] 리치 쓰러짐/사라짐 · 씬 {SceneManager.GetActiveScene().name}"); }
                // 봉인기 — 쓰러진 뒤 봉인 의식이 봉인석 타격을 기다린다. 2초마다 쳐서 붕괴 · 추방 추락까지 간다(11-02 2차 · 1차엔 의식에서 멈춰 추락을 못 쟀다)
                if (lichGone && !exiling && now - lastStoneHit > 2f) { lastStoneHit = now; TestHubDebugMenu.HitSealStones(); }
                if (lichGone && now - goneAt > TailSeconds) break;
            }
            segs.Add(cur);
            var all = segs.SelectMany(s => s.dts).OrderBy(x => x).ToList();
            var head = new StringBuilder($"요약 — 전체 프레임 {all.Count} · 중앙 {P(all, 0.5f):0.0} · 99% {P(all, 0.99f):0.0} · 최대 {(all.Count > 0 ? all[^1] : 0):0} ms\n구간별:\n");
            foreach (var g in segs.GroupBy(s => s.name))
            {
                var d = g.SelectMany(s => s.dts).OrderBy(x => x).ToList();
                head.AppendLine($"  {g.Key}: 프레임 {d.Count} · 중앙 {P(d, 0.5f):0.0} · 99% {P(d, 0.99f):0.0} · 최대 {(d.Count > 0 ? d[^1] : 0):0} ms · 튐 {g.Sum(s => s.spikes)}");
            }
            sb.Insert(0, head.ToString());
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var (_, r) in recs) r.enabled = false;
            Application.logMessageReceived -= OnLog;
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[LichSpike] 끝 — {path}");
        }
    }

    /// <summary>구간 이름 — 상태 이름으로 전환 · 의식 · 추방을 가르고, 나머지는 페이지 번호.</summary>
    private static string SegmentOf(string st, int page)
    {
        if (st.Contains("Phase2Entry")) return "페이지 전환";
        if (st.Contains("Seal") && st.Contains("Ritual")) return "봉인 의식";
        if (st.Contains("Death") || st.Contains("Dead")) return "쓰러짐";
        if (st == "(리치 없음)") return "추방 · 그 뒤";
        return $"페이지{page}";
    }

    private static readonly FieldInfo s_exileActive = typeof(ExileFallSequence).GetField("s_active", BindingFlags.Static | BindingFlags.NonPublic);

    private static float P(List<float> sorted, float q) => sorted.Count == 0 ? 0f : sorted[Mathf.Clamp((int)(sorted.Count * q), 0, sorted.Count - 1)];

    /// <summary>이펙트 이름 — 가장 가까운 만들어진 프리팹 루트(이름에 「(Clone)」 — 리치 몸에 붙은 이펙트도 리치로 뭉치지 않게). 없으면 입자계를 가진 가장 위 부모.</summary>
    private static string EffectRootName(ParticleSystem p)
    {
        Transform clone = null, top = p.transform;
        for (var t = p.transform; t != null; t = t.parent)
        {
            if (clone == null && t.name.EndsWith("(Clone)", StringComparison.Ordinal)) clone = t;
            if (t.GetComponent<ParticleSystem>() != null) top = t;
        }
        return (clone ?? top).name.Replace("(Clone)", string.Empty).Trim();
    }

    private static string StateOf(MonsterBase boss)
    {
        var fsm = typeof(MonsterBase).GetField("_fsm", Any)?.GetValue(boss);
        return (fsm?.GetType().GetProperty("CurrentType", Any)?.GetValue(fsm) as Type)?.Name ?? "-";
    }
}
#endif
