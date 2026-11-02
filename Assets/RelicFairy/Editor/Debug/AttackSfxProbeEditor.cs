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
/// 10-29 박쥐 · 흑기사 공격음 실측 — 테스트 허브에서. 첫 전투방에서 방 몬스터를 끄고 흑기사를 플레이어 앞 1.6 m에 25초,
/// 박쥐를 1.4 m(근접) 12초 · 7 m(음파) 12초 부른다(플레이어 무적). 프레임마다 공격 상태 진입 · 공격음 재생(AudioSource 클립) · 피해 순간(AttackHitDealt)을 읽어
/// 흑기사 = 「소리 정점 − 피해」 · 박쥐 = 「날갯짓 − 공격 시작」을 적는다. 결과 Logs/boss_fight_sim/attack_sfx_*.txt · 로그 「[AttackSfxProbe] 끝」.
/// </summary>
public static class AttackSfxProbeEditor
{
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string KnightClip = "Assets/RelicFairy/Characters/Monster/Monster/DeathKnightBoss/Art/Sound/Slash2.mp3";
    private const string BatClip    = "Assets/RelicFairy/_Imported/Malbers Animations/Common/Audio/Wings/Wing Flap 02.wav";
    private const float  Slash2Peak = 0.59f;
    private static bool s_armed;

    [MenuItem("RelicFairy/Debug/10-29 박쥐 · 흑기사 공격음 확인 (테스트 허브, Play)")]
    private static void Begin()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[AttackSfxProbe] 플레이 중에"); return; }
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
        string path = Path.Combine(dir, $"attack_sfx_{DateTime.Now:MMdd_HHmmss}.txt");
        var sb = new StringBuilder("박쥐 · 흑기사 공격음 실측(시각은 Time.time 초)\n");
        try
        {
            var p = GameRunBootstrapper.Instance?.Run?.Player;
            if (p == null) { sb.AppendLine("런 플레이어 없음"); return; }
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            await UniTask.Delay(5000, DelayType.Realtime);
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (m != null && !m.IsDead) m.gameObject.SetActive(false);

            var slash = AssetDatabase.LoadAssetAtPath<AudioClip>(KnightClip);
            var flap  = AssetDatabase.LoadAssetAtPath<AudioClip>(BatClip);
            sb.AppendLine($"클립 — Slash2 {(slash != null ? slash.length.ToString("0.00") : "없음")}초 · 날갯짓 {(flap != null ? flap.length.ToString("0.00") : "없음")}초");

            await WatchAsync(sb, p, "BlackKnight/BlackKnight", slash, Slash2Peak, new[] { (1.6f, 25f) });
            await WatchAsync(sb, p, "FairyBat/FairyBat", flap, 0f, new[] { (1.4f, 12f), (7f, 12f) });
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[AttackSfxProbe] 끝\n{sb}");
        }
    }

    private static async UniTask WatchAsync(StringBuilder sb, PlayerController p, string key, AudioClip clip, float clipPeak, (float dist, float seconds)[] phases)
    {
        Vector3 fw = p.transform.forward; fw.y = 0f; fw = fw.sqrMagnitude > 0.01f ? fw.normalized : Vector3.forward;
        Vector3 at = p.transform.position + fw * phases[0].dist;
        if (NavMesh.SamplePosition(at, out var hit, 4f, NavMesh.AllAreas)) at = hit.position;
        MonsterBase m;
        try { m = await Managers.ObjectPooler.SpawnAsync<MonsterBase>(key, ObjectPoolerManager.PoolType.Monster, at, Quaternion.LookRotation(-fw)); }
        catch (Exception e) { sb.AppendLine($"\n■ {key} 부르기 실패 {e.Message}"); return; }
        if (m == null) { sb.AppendLine($"\n■ {key} 부르기 실패"); return; }
        var fsmF = typeof(MonsterBase).GetField("_fsm", Inst);
        for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 8f && (m.IsDamageImmuneNow || fsmF?.GetValue(m) == null); )
            await UniTask.Yield(PlayerLoopTiming.Update);
        await UniTask.Delay(600, DelayType.Realtime);

        var stateF = typeof(MonsterBase).GetField("_debugState", Inst);
        var rt = typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(m) as MonsterRuntimeData;
        var listener = UnityEngine.Object.FindFirstObjectByType<AudioListener>();
        sb.AppendLine($"\n■ {key} — 공격음 {(clip != null ? clip.name : "없음")}");

        foreach (var (dist, seconds) in phases)
        {
            sb.AppendLine($"  ▷ 거리 {dist} m · {seconds}초");
            var prevPlaying = new Dictionary<int, float>();
            string prevState = null;
            float enterAt = -1f, soundPeakAt = -1f, start = Time.time, nextPlace = 0f;
            bool prevHit = rt != null && rt.AttackHitDealt;
            int attacks = 0, sounds = 0;
            while (Time.time - start < seconds && m != null && !m.IsDead && m.gameObject.activeInHierarchy)
            {
                if (Time.time >= nextPlace) { nextPlace = Time.time + 1.5f; Place(p, m, dist); }
                float now = Time.time;
                string st = stateF?.GetValue(m) as string;
                if (st != prevState && st != null && (st == "PatternAttackState" || st.EndsWith("DualAttackState")))
                {
                    attacks++; enterAt = now;
                    sb.AppendLine($"    [{now - start,6:0.00}] 공격 시작 {st} · 거리 {Vector3.Distance(m.transform.position, p.transform.position):0.0} m");
                }
                prevState = st;

                foreach (var src in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                {
                    if (src == null || clip == null || src.clip != clip) continue;
                    int id = src.GetInstanceID();
                    bool started = src.isPlaying && (!prevPlaying.TryGetValue(id, out float lastT) || src.time + 0.01f < lastT);
                    prevPlaying[id] = src.isPlaying ? src.time : float.MaxValue;
                    if (!started) continue;
                    sounds++;
                    float pitch = Mathf.Max(0.01f, src.pitch);
                    soundPeakAt = now + (clipPeak - src.time) / pitch;
                    float ear = listener != null ? Vector3.Distance(listener.transform.position, src.transform.position) : -1f;
                    sb.AppendLine($"    [{now - start,6:0.00}] 공격음 재생 — 클립 {src.time:0.00}초부터 · 피치 {pitch:0.00} · 볼륨 {src.volume:0.00} · 3D {src.spatialBlend:0.0} · 귀까지 {ear:0.0} m"
                                  + (enterAt >= 0f ? $" · 공격 시작 뒤 {now - enterAt:0.00}초" : "")
                                  + (clipPeak > 0f ? $" · 정점 예정 {soundPeakAt - start:0.00}" : ""));
                }

                bool h = rt != null && rt.AttackHitDealt;
                if (h && !prevHit)
                    sb.AppendLine($"    [{now - start,6:0.00}] 피해 순간"
                                  + (enterAt >= 0f ? $" · 공격 시작 뒤 {now - enterAt:0.00}초" : "")
                                  + (soundPeakAt >= 0f && clipPeak > 0f ? $" · 소리 정점 − 피해 {soundPeakAt - now:+0.00;-0.00}초" : ""));
                prevHit = h;
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            sb.AppendLine($"    공격 {attacks} · 공격음 {sounds}");
        }
        if (m != null) m.gameObject.SetActive(false);
    }

    private static void Place(PlayerController p, MonsterBase m, float dist)
    {
        Vector3 c = m.transform.position;
        Vector3 dir = p.transform.position - c; dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : m.transform.forward;
        Vector3 at = c + dir * dist;
        if (NavMesh.SamplePosition(at, out var hit, 2f, NavMesh.AllAreas)) at = hit.position;
        var look = c - at; look.y = 0f;
        var rot = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look.normalized) : p.transform.rotation;
        p.transform.SetPositionAndRotation(at, rot);
        if (p.Rigid != null) { p.Rigid.position = at; p.Rigid.rotation = rot; p.Rigid.linearVelocity = Vector3.zero; }
    }
}
#endif
