#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 도구 · 보스방 플레이 중] 「파츠 · 룬을 많이 넣은 화살을 퍼부으면 보스에 피해가 제대로 안 들어간다」(10-03 사용자 제보) 재현.
/// 원거리 빌드(석궁 T3 + 원거리 파츠 전부 최대 레벨 + 룬 시너지 전부)로 보스에게 정해진 시간 연사하고, 같은 시간 동안
///   · 발사 화살 수 · 보스 적중 수(HitFeedbackService.OnHit — 무적에 막힌 타격은 안 들어온다) · 적중 원 피해 합(방어 전)
///   · 보스 체력 감소 합 — 경로별(일반 타격 · 시너지 · 지속 피해, MonsterBase.LastDamageKind)
///   · 원 피해가 방어 이하라 하한 1로 뭉개진 적중 수 · 보스 무적 프레임 비율 · 체력 하한(페이지 · 봉인)에 걸린 횟수
/// 를 세어 「들어가야 할 피해 ↔ 들어간 피해」 차이를 Temp/boss_ranged_probe.txt에 남긴다. 플레이어는 재는 동안 무적.
/// </summary>
public static class BossRangedDamageProbeEditor
{
    private const string CrossbowPath = "Assets/RelicFairy/Weapon/Crossbow/Data/T3_Crossbow.asset";
    private const string SynergyCsv   = "Assets/RelicFairy/Docs/MERLIN_RUNE_SYNERGY_DATA.csv";
    private const string OutPath      = "Temp/boss_ranged_probe.txt";
    private const float  Seconds      = 15f;
    private const float  FireGap      = 0.12f;
    private const BindingFlags Inst   = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>보스방에 들어서자마자 — 등장 연출 · 대기 동안 쓰러지지 않게 플레이어 무적을 켠다(실측이 끝나면 끈다).</summary>
    [MenuItem("RelicFairy/Boss/Fight Sim/Ranged Probe - Arm Invincible (Play)")]
    private static void Arm()
    {
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        if (p != null) typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
        Debug.Log($"[RangedProbe] 플레이어 무적 {(p != null ? "켬" : "— 플레이어 없음")}");
    }

    [MenuItem("RelicFairy/Boss/Fight Sim/Ranged Build Damage Probe (Play)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[RangedProbe] 플레이 모드에서만"); return; }
        RunAsync(Application.exitCancellationToken).Forget();
    }

    private static async UniTaskVoid RunAsync(System.Threading.CancellationToken ct)
    {
        var sb = new StringBuilder();
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        var spawner = UnityEngine.Object.FindFirstObjectByType<BossSpawner>();
        var boss = spawner != null ? spawner.SpawnedBoss : null;
        if (p == null || boss == null || !boss.gameObject.activeInHierarchy) { Debug.LogWarning("[RangedProbe] 플레이어 · 보스 없음 — 보스를 깨운 뒤에"); return; }

        typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
        Action<HitInfo> onHit = null;
        Action<int, int> onHp = null;
        try
        {
            // ── 빌드: 석궁 T3 · 원거리 파츠 전부 최대 · 룬 시너지 전부
            var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(CrossbowPath);
            if (so != null) await GameRunBootstrapper.EquipWeaponToPlayerAsync(so, p, 1);
            await UniTask.Delay(TimeSpan.FromSeconds(0.8f), ignoreTimeScale: true, cancellationToken: ct);
            var state = RangedPartsState.Current;
            var parts = new StringBuilder();
            if (Managers.WeaponParts?.All != null)
                foreach (var e in Managers.WeaponParts.All)
                {
                    int max = Mathf.Max(1, e.max_level);
                    if (state.LevelOf(e.part_id) <= 0) state.Equip(e.part_id, max);
                    else state.LevelUp(e.part_id, max - state.LevelOf(e.part_id));
                    parts.Append($"{e.part_id}({e.Kind})×{max} ");
                }
            int runes = ActivateAllSynergies(p);
            float def = ReadDefense(boss);
            sb.AppendLine($"보스 {boss.name} · 방어 {def:0.#} · HP {boss.CurrentHp}/{boss.EffectiveMaxHp} · 시기 {StoryProgress.Era}");
            sb.AppendLine($"무기 {p.WeaponManager?.CurrentWeaponData?.weaponSOKey} · 파츠 {parts} · 룬 시너지 {runes}개 켬");

            // ── 계측
            int fired = 0, hits = 0, crushed = 0, immuneFrames = 0, frames = 0, floorHits = 0;
            double rawSum = 0, normal = 0, synergy = 0, dot = 0, otherKind = 0;
            var activeArrows = new HashSet<int>();
            int lastHp = boss.CurrentHp;
            onHit = h =>
            {
                if (h.Target == null || !(h.Target == boss.gameObject || h.Target.transform.IsChildOf(boss.transform))) return;
                hits++;
                rawSum += h.Damage;
                if (h.Damage <= def + 1f) crushed++;
            };
            onHp = (hp, max) =>
            {
                int d = lastHp - hp;
                lastHp = hp;
                if (d <= 0) return;
                switch (boss.LastDamageKind)
                {
                    case DamageKind.Normal:  normal  += d; break;
                    case DamageKind.Synergy: synergy += d; break;
                    case DamageKind.Dot:     dot     += d; break;
                    default:                 otherKind += d; break;
                }
            };
            HitFeedbackService.OnHit += onHit;
            boss.OnHPChanged += onHp;

            // 등장 연출(컷신 HUD)이 끝날 때까지 기다린다 — 그동안은 입력이 막혀 화살이 안 나간다(기사 1차 실측 0발)
            var run = GameRunBootstrapper.Instance?.Run;
            float w0 = Time.realtimeSinceStartup;
            while (run != null && run.InCutscene && Time.realtimeSinceStartup - w0 < 60f)
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            sb.AppendLine($"연출 대기 {Time.realtimeSinceStartup - w0:0.0}초");

            var fsmField = typeof(MonsterBase).GetField("_fsm", Inst);
            string StateName() => (fsmField?.GetValue(boss) as MonsterFSM)?.CurrentType?.Name ?? "?";
            var timeline = new StringBuilder();
            bool wasImmune = boss.IsDamageImmuneNow;
            string lastState = StateName();
            timeline.AppendLine($"  0.0초 시작 · 무적 {wasImmune} · 상태 {lastState} · HP {boss.CurrentHp}");

            float t0 = Time.realtimeSinceStartup, nextFire = 0f;
            while (Time.realtimeSinceStartup - t0 < Seconds && boss != null && !boss.IsDead)
            {
                float now = Time.realtimeSinceStartup - t0;
                bool immune = boss.IsDamageImmuneNow;
                string st = StateName();
                if (immune != wasImmune || st != lastState)
                {
                    timeline.AppendLine($"  {now:0.0}초 무적 {immune} · 상태 {st} · HP {boss.CurrentHp}({100f * boss.CurrentHp / Mathf.Max(1, boss.EffectiveMaxHp):0}%)");
                    wasImmune = immune; lastState = st;
                }
                if (run != null && run.InCutscene) { await UniTask.Yield(PlayerLoopTiming.Update, ct); continue; }
                if (now >= nextFire)
                {
                    nextFire = now + FireGap;
                    Vector3 to = boss.transform.position - p.transform.position; to.y = 0f;
                    if (to.sqrMagnitude > 0.01f) p.transform.rotation = Quaternion.LookRotation(to.normalized);
                    p.InputBuffer.Push(Command.Light);
                }
                frames++;
                if (boss.IsDamageImmuneNow) immuneFrames++;
                foreach (var a in UnityEngine.Object.FindObjectsByType<BasicArrow>(FindObjectsSortMode.None))
                {
                    int id = a.GetInstanceID();
                    if (a.gameObject.activeInHierarchy) { if (activeArrows.Add(id)) fired++; }
                    else activeArrows.Remove(id);
                }
                if (boss.CurrentHp > 0 && boss.CurrentHp <= FloorOf(boss)) floorHits++;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            double dealt = normal + synergy + dot + otherKind;
            sb.AppendLine($"── {Seconds:0}초 연사");
            sb.AppendLine($"발사 화살 {fired} · 보스 적중 {hits}({(fired > 0 ? 100.0 * hits / fired : 0):0}%) · 무적 프레임 {(frames > 0 ? 100.0 * immuneFrames / frames : 0):0}% · 체력 하한에 걸린 프레임 {floorHits}");
            sb.AppendLine($"적중 원 피해 합(방어 전) {rawSum:0} · 방어 이하라 1로 뭉개진 적중 {crushed}/{hits}");
            sb.AppendLine($"보스 체력 감소 {dealt:0} = 일반 타격 {normal:0} · 시너지 {synergy:0} · 지속 피해 {dot:0} · 기타 {otherKind:0}");
            sb.AppendLine($"일반 타격 손실 = 원 피해 {rawSum:0} − 들어간 일반 {normal:0} = {rawSum - normal:0}({(rawSum > 0 ? 100.0 * (rawSum - normal) / rawSum : 0):0}%) — 방어 {def:0.#} × 적중 {hits} = {def * hits:0}");
            sb.AppendLine("── 무적 · 상태 흐름");
            sb.Append(timeline);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (onHit != null) HitFeedbackService.OnHit -= onHit;
            if (onHp != null && boss != null) boss.OnHPChanged -= onHp;
            if (p != null) typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, false);
        }
        Directory.CreateDirectory("Temp");
        File.WriteAllText(OutPath, sb.ToString(), Encoding.UTF8);
        Debug.Log("[RangedProbe] 결과\n" + sb);
    }

    /// <summary>룬 시너지 표의 모든 단계를 켠다(룬판을 가득 채운 것과 같은 효과 목록). 켠 수.</summary>
    private static int ActivateAllSynergies(PlayerController p)
    {
        var fx = p.RuneEffects;
        if (fx == null || !File.Exists(SynergyCsv)) return 0;
        var lines = File.ReadAllLines(SynergyCsv, Encoding.UTF8);
        var head = new List<string>(lines[0].TrimStart('﻿').Split(','));
        int Col(string n) => head.IndexOf(n);
        int n = 0;
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var c = SplitCsv(lines[i]);
            if (c.Count < head.Count) continue;
            var e = new RuneSynergyEntry
            {
                zone_id = c[Col("zone_id")], zone_name = c[Col("zone_name")], threshold = int.Parse(c[Col("threshold")]),
                effect_type = c[Col("effect_type")], trigger = c[Col("trigger")],
                value = F(c[Col("value")]), value2 = F(c[Col("value2")]), value3 = F(c[Col("value3")]),
                max_stack = (int)F(c[Col("max_stack")]), duration = F(c[Col("duration")]),
                description = c[Col("description")], stat_version = 1,
            };
            fx.Activate(e);
            n++;
        }
        return n;
    }

    private static float F(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;

    private static List<string> SplitCsv(string line)
    {
        var r = new List<string>();
        var cur = new StringBuilder();
        bool q = false;
        foreach (char ch in line)
        {
            if (ch == '"') { q = !q; continue; }
            if (ch == ',' && !q) { r.Add(cur.ToString()); cur.Clear(); continue; }
            cur.Append(ch);
        }
        r.Add(cur.ToString());
        return r;
    }

    private static float ReadDefense(MonsterBase m)
    {
        var bd = typeof(MonsterBase).GetField("_baseDefense", Inst)?.GetValue(m);
        var dm = typeof(MonsterBase).GetField("_defenseMulti", Inst)?.GetValue(m);
        return (bd is float b ? b : 0f) * (dm is float d ? d : 1f);
    }

    private static int FloorOf(MonsterBase m)
    {
        var prop = typeof(MonsterBase).GetProperty("DamageHpFloor", Inst);
        return prop?.GetValue(m) is int f ? f : 0;
    }
}
#endif
