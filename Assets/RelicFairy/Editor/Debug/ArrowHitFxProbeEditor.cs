#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 도구 · 보스방 플레이 중] 10-29 사용자 「챕터2 보스에서 화살을 쓸 때 타격 연출이 화살별로 다 살아 있어 게임이 제대로 안 돌아간다」 재현.
/// 보스를 깨운 뒤(전투 시뮬 등) 부른다. 플레이어 무적 · 활로 세 판:
///   ① 활 T1(파츠 없음) — 보스에게 12초 연사 ② 같은 활 — 보스 반대쪽 빈 곳에 6초(맞음 없음 대조) ③ 활 + 원거리 파츠 전부 최대 — 보스에게 12초
/// 0.5초마다 잰다: 프레임(평균 · 최대 ms) · 시간 배율 1 미만 프레임 · 살아 있는(재생 중) 파티클 시스템 수와 이름 상위 · 켜진 라이트 · 울리는 소리 ·
/// 날아가는 화살 · 데미지 숫자. 판 끝에 보스 적중 수. 결과 Temp/arrow_hitfx_probe.txt · 로그 「[ArrowHitFx] 끝」.
/// </summary>
public static class ArrowHitFxProbeEditor
{
    private const string BowPath = "Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset";
    private const string OutPath = "Temp/arrow_hitfx_probe.txt";
    private const float FireGap = 0.08f, SampleGap = 0.5f;
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Boss/Fight Sim/10-29 Arrow Hit FX Probe (Play)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[ArrowHitFx] 플레이 모드에서만"); return; }
        RunAsync(Application.exitCancellationToken).Forget();
    }

    private static async UniTaskVoid RunAsync(System.Threading.CancellationToken ct)
    {
        var sb = new StringBuilder();
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        var spawner = UnityEngine.Object.FindFirstObjectByType<BossSpawner>();
        var boss = spawner != null ? spawner.SpawnedBoss : null;
        if (p == null || boss == null || !boss.gameObject.activeInHierarchy) { Debug.LogWarning("[ArrowHitFx] 끝 — 플레이어 · 보스 없음(보스를 깨운 뒤에)"); return; }
        typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
        int hits = 0;
        double dmgSum = 0;
        Action<HitInfo> onHit = h => { if (h.Target != null && (h.Target == boss.gameObject || h.Target.transform.IsChildOf(boss.transform))) { hits++; dmgSum += h.Damage; } };
        HitFeedbackService.OnHit += onHit;
        try
        {
            var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(BowPath);
            if (so != null) await GameRunBootstrapper.EquipWeaponToPlayerAsync(so, p, 1);
            await UniTask.Delay(TimeSpan.FromSeconds(1f), ignoreTimeScale: true, cancellationToken: ct);
            var run = GameRunBootstrapper.Instance?.Run;
            float w0 = Time.realtimeSinceStartup;
            while (run != null && run.InCutscene && Time.realtimeSinceStartup - w0 < 60f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            sb.AppendLine($"보스 {boss.name} · 무기 {p.WeaponManager?.CurrentWeaponData?.weaponSOKey} · 연출 대기 {Time.realtimeSinceStartup - w0:0.0}초");

            // D1 원거리 보스 반감 — 같은 판에서 반감 켬 · 끔 각 4초 평균 피해(치명 섞임)
            hits = 0; dmgSum = 0;
            await Phase(sb, "D1-a 활 T1 → 보스(반감 켬)", p, boss, true, 4f, () => hits, ct);
            double onAvg = hits > 0 ? dmgSum / hits : 0; int onN = hits;
            CombatDamage.ProbeNoBossRangedHalf = true;
            hits = 0; dmgSum = 0;
            await Phase(sb, "D1-b 활 T1 → 보스(반감 끔)", p, boss, true, 4f, () => hits, ct);
            CombatDamage.ProbeNoBossRangedHalf = false;
            double offAvg = hits > 0 ? dmgSum / hits : 0;
            sb.AppendLine($"D1 적중당 피해 — 반감 켬 {onAvg:0.0}({onN}번) · 끔 {offAvg:0.0}({hits}번) · 비율 {(offAvg > 0 ? onAvg / offAvg : 0):0.00}(0.5 근처) · 챕터 {GameRunBootstrapper.Instance?.Run?.CurrentChapter} · 등급 {boss.Grade}");
            hits = 0;
            await Phase(sb, "① 활 T1 → 보스", p, boss, true, 12f, () => hits, ct);
            hits = 0;
            await Phase(sb, "② 활 T1 → 빈 곳(대조)", p, boss, false, 6f, () => hits, ct);

            var state = RangedPartsState.Current;
            var parts = new StringBuilder();
            if (Managers.WeaponParts?.All != null)
                foreach (var e in Managers.WeaponParts.All)
                {
                    int max = Mathf.Max(1, e.max_level);
                    if (state.LevelOf(e.part_id) <= 0) state.Equip(e.part_id, max);
                    else state.LevelUp(e.part_id, max - state.LevelOf(e.part_id));
                    parts.Append(e.part_id).Append('×').Append(max).Append(' ');
                }
            sb.AppendLine($"파츠 전부 최대: {parts}");
            await UniTask.Delay(TimeSpan.FromSeconds(0.5f), ignoreTimeScale: true, cancellationToken: ct);
            hits = 0;
            await Phase(sb, "③ 활 + 파츠 최대 → 보스", p, boss, true, 12f, () => hits, ct);
            // 쏘기를 멈춘 뒤 3초 — 남는 것이 빠지나
            await Phase(sb, "④ 멈춘 뒤 3초(남는 것)", p, boss, null, 3f, () => hits, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            CombatDamage.ProbeNoBossRangedHalf = false;
            HitFeedbackService.OnHit -= onHit;
            if (p != null) typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, false);
        }
        Directory.CreateDirectory("Temp");
        File.WriteAllText(OutPath, sb.ToString(), Encoding.UTF8);
        Debug.Log("[ArrowHitFx] 끝\n" + sb);
    }

    /// <summary>한 판 — atBoss true = 보스에게 · false = 반대쪽 빈 곳 · null = 쏘지 않음. 0.5초마다 한 줄.</summary>
    private static async UniTask Phase(StringBuilder sb, string title, PlayerController p, MonsterBase boss, bool? atBoss, float seconds,
                                       Func<int> hitCount, System.Threading.CancellationToken ct)
    {
        sb.AppendLine($"── {title} ({seconds:0}초)");
        float t0 = Time.realtimeSinceStartup, nextFire = 0f, nextSample = SampleGap;
        float frameSum = 0f, frameMax = 0f; int frames = 0, slowFrames = 0;
        while (Time.realtimeSinceStartup - t0 < seconds && boss != null && !boss.IsDead)
        {
            float now = Time.realtimeSinceStartup - t0;
            float dt = Time.unscaledDeltaTime * 1000f;
            frameSum += dt; frameMax = Mathf.Max(frameMax, dt); frames++;
            if (Time.timeScale < 0.999f) slowFrames++;
            if (atBoss.HasValue && now >= nextFire)
            {
                nextFire = now + FireGap;
                Vector3 to = boss.transform.position - p.transform.position; to.y = 0f;
                if (!atBoss.Value) to = -to;
                if (to.sqrMagnitude > 0.01f) p.transform.rotation = Quaternion.LookRotation(to.normalized);
                if (p.InputBuffer.Count < 2) p.InputBuffer.Push(Command.Light);
            }
            if (now >= nextSample)
            {
                nextSample += SampleGap;
                sb.AppendLine($"  {now:0.0}초 · 프레임 평균 {(frames > 0 ? frameSum / frames : 0f):0.0}ms 최대 {frameMax:0.0}ms · 느린 시간 {slowFrames}/{frames} · {Census()} · 보스 적중 {hitCount()}");
                frameSum = 0f; frameMax = 0f; frames = 0; slowFrames = 0;
            }
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }

    /// <summary>지금 살아 있는 것 세기 — 재생 중 파티클 시스템(최상위 이름별 상위 6) · 켜진 라이트 · 울리는 소리 · 화살 · 데미지 숫자.</summary>
    private static string Census()
    {
        int ps = 0;
        var byRoot = new Dictionary<string, int>();
        foreach (var s in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
        {
            if (s == null || !s.gameObject.activeInHierarchy || !s.IsAlive(false)) continue;
            ps++;
            var root = s.transform;
            while (root.parent != null && root.parent.GetComponent<ParticleSystem>() != null) root = root.parent;
            string n = root.name.Replace("(Clone)", "");
            byRoot[n] = byRoot.TryGetValue(n, out var c) ? c + 1 : 1;
        }
        int lights = 0;
        foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l != null && l.isActiveAndEnabled && l.intensity > 0.01f && l.type != LightType.Directional) lights++;
        int audio = 0;
        foreach (var a in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            if (a != null && a.isPlaying) audio++;
        int arrows = 0;
        foreach (var a in UnityEngine.Object.FindObjectsByType<BasicArrow>(FindObjectsSortMode.None))
            if (a != null && a.gameObject.activeInHierarchy) arrows++;
        int popups = 0;
        foreach (var d in UnityEngine.Object.FindObjectsByType<DamagePopup>(FindObjectsSortMode.None))
            if (d != null && d.gameObject.activeInHierarchy) popups++;
        string top = string.Join(" ", byRoot.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Key}×{kv.Value}"));
        return $"파티클 {ps}({top}) · 라이트 {lights} · 소리 {audio} · 화살 {arrows} · 숫자 {popups}";
    }
}
#endif
