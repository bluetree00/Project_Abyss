using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// [실측 도구 · 플레이 중 · 전투방에서] 룬 속성 시너지 10-01 조정분(전기 3·4단계 · 어둠 4단계)이 적힌 대로 도는지 잰다.
///
/// <para>단계 효과를 <b>저장소 표</b>(Docs/MERLIN_RUNE_SYNERGY_DATA.csv — 채택한 값)로 직접 켠다. CDN 업로드 전이라
/// 게임이 읽는 차트는 옛 값일 수 있으므로, 표와 차트가 다른 행도 같이 적는다.</para>
/// <para>적은 HP 바닥 1로 세워 죽지 않게 한다. 판에 켜져 있던 시너지는 끝나고 원래 표로 다시 켠다(스택 · 게이지는 비워진다).</para>
/// 결과: Temp/rune_synergy_probe.txt
/// </summary>
public static class RuneSynergyProbeEditor
{
    private const string CsvPath = "Assets/RelicFairy/Docs/MERLIN_RUNE_SYNERGY_DATA.csv";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    // Ch1 몹은 체력이 10 안팎이라 큰 피해는 HP 바닥(1)에 걸려 횟수를 못 센다 → 체인 한 번이 피해 1이 되게 작게 친다.
    private const float HitDamage = 2f;

    private static readonly List<BuffViewItem> s_views = new();

    [MenuItem("RelicFairy/Debug/룬 시너지 실측 — 전기·어둠 조정분 (전투방에서)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[RuneSynergyProbe] 플레이 모드에서만"); return; }
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        var fx = player != null ? player.RuneEffects : null;
        var stats = player != null ? player.RuntimeStats : null;
        if (fx == null || stats == null) { Debug.LogWarning("[RuneSynergyProbe] 런이 없다"); return; }

        var table = LoadCsv();
        var sb = new StringBuilder();
        int pass = 0, fail = 0;
        void Check(bool ok, string line) { if (ok) pass++; else fail++; sb.AppendLine((ok ? "✓ " : "✗ ") + line); }

        sb.AppendLine($"표 {table.Count}행 · 중앙 공명 배수 {fx.Amplifier:0.##}");
        DiffAgainstChart(table, sb);

        // 켜져 있던 시너지는 끝나고 되돌린다.
        var sourceField = typeof(RuneEffectDispatcher).GetField("_sourceEntries", Inst);
        var zoneAmpField = typeof(RuneEffectDispatcher).GetField("_zoneAmp", Inst);
        var restore = new List<RuneSynergyEntry>(((Dictionary<string, RuneSynergyEntry>)sourceField.GetValue(fx)).Values);
        var restoreZoneAmp = new Dictionary<string, float>((Dictionary<string, float>)zoneAmpField.GetValue(fx));
        float restoreAmp = fx.Amplifier;

        typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(player, true);
        var all = new List<MonsterBase>();
        foreach (var m in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (!m.IsDead) { m.HpFloorMin1 = true; all.Add(m); }
        Vector3 p = player.transform.position;
        all.Sort((a, b) => (a.transform.position - p).sqrMagnitude.CompareTo((b.transform.position - p).sqrMagnitude));
        if (all.Count == 0)
        {
            sb.AppendLine("전투방이 아니다 — 적이 없다");
            Finish(sb, pass, fail);
            return;
        }
        int atk = stats.GetEffectiveAttack((player.WeaponManager?.CurrentWeaponData?.weaponType ?? WeaponType.None).GetAttackStatKind());
        sb.AppendLine($"적 {all.Count} · 유효 공격력 {atk}");

        // ── 전기 4단계 과부하: 체인 수(value2) ─────────────────────────
        fx.Clear();
        var overload = Row(table, "ElecOverload");
        fx.Activate(overload);
        int chain = Mathf.Max(1, (int)overload.value2);

        // (가) 무리 — 맞은 적 곁 5m 안의 다른 적에게 50%씩
        var near = Place(player, all, Mathf.Min(all.Count, chain + 2), 1.5f);
        Reset(near);
        var target = near[0];
        Reset(all);
        var hp0 = Snapshot(all);
        fx.NotifyHit(Report(player, target));
        int others = 0; long othersDealt = 0, selfDealt = 0;
        for (int i = 0; i < all.Count; i++)
        {
            long d = hp0[i] - all[i].CurrentHp;
            if (all[i] == target) { selfDealt = d; continue; }
            if (d > 0) { others++; othersDealt += d; }
        }
        int expectOthers = Mathf.Min(chain, near.Count - 1);
        long each = Mathf.Max(1, Mathf.FloorToInt(HitDamage * overload.value));
        if (near.Count > 1)
            Check(others == expectOthers && othersDealt == expectOthers * each,
                  $"과부하(무리): 곁의 적 {others}명에게 {othersDealt} (기대 {expectOthers}명 × {each}) · 맞은 적 추가 피해 {selfDealt}");
        else
            sb.AppendLine("· 과부하(무리): 적이 하나뿐이라 재지 못했다");

        // (나) 혼자 — 곁에 아무도 없으면 같은 적을 체인 수만큼
        if (Isolate(player, target, all))
        {
            Reset(all);
            long before = target.CurrentHp;
            fx.NotifyHit(Report(player, target));
            long dealt = before - target.CurrentHp;
            Check(dealt == chain * each, $"과부하(혼자): 같은 적 추가 피해 {dealt} (기대 {chain} × {each} = {chain * each})");
        }
        else sb.AppendLine("· 과부하(혼자): 다른 적을 5m 밖으로 떼어 놓지 못해 재지 못했다");

        // ── 전기 3단계 감전: 공속 대신 그 적이 받는 피해 ────────────────
        fx.Clear();
        near = Place(player, all, Mathf.Min(all.Count, 3), 1.5f);
        Reset(all);
        var stat = Row(table, "ElecStatic");
        var shock = Row(table, "ElecShock");
        fx.Activate(stat);
        fx.Activate(Row(table, "ElecDischarge"));
        fx.Activate(shock);
        fx.Resources.Add("ElecStatic", stat.max_stack, stat.max_stack, stat.duration);
        fx.Tick(0f);
        float asFull = AttackSpeedLayer(stats);
        fx.NotifySkillUsed();
        fx.Tick(0f);
        float asAfter = AttackSpeedLayer(stats);
        MonsterBase shocked = null;
        foreach (var m in all) if (m.Status.HasCc("stun")) { shocked = m; break; }
        Check(Mathf.Approximately(asFull, stat.value + stat.max_stack * stat.value2),
              $"정전기 {stat.max_stack}중첩 공속 층 +{asFull:0.###} (기대 {stat.value + stat.max_stack * stat.value2:0.###})");
        Check(shocked != null, $"감전: 방전 맞은 적 기절 {(shocked != null ? "예" : "아니오")}");
        if (shocked != null)
        {
            Check(Mathf.Abs(shocked.DamageTakenAmpTotal - shock.value3) < 0.0005f,
                  $"감전: 그 적이 받는 피해 +{shocked.DamageTakenAmpTotal * 100f:0.#}% (기대 +{shock.value3 * 100f:0.#}%) · 남은 {shocked.DamageTakenAmpRemaining:0.0}초 (기대 {shock.value2:0.#}초)");
            s_views.Clear(); shocked.CollectStatuses(s_views);
            var labels = new List<string>();
            foreach (var v in s_views) labels.Add(v.Label);
            Check(labels.Contains("감전 취약") && !labels.Contains("shocked"), $"감전: 디버프 줄 「{string.Join(" · ", labels)}」");
        }
        Check(Mathf.Approximately(asAfter, stat.value), $"감전 뒤 공속 층 +{asAfter:0.###} (기대 {stat.value:0.###} — 감전 공속 +8%는 없어졌다)");
        Check(EffectDescriptionFormatter.IconKeyForStatus("shocked") == "lightning",
              $"감전 취약 아이콘 키 「{EffectDescriptionFormatter.IconKeyForStatus("shocked")}」");

        // ── 어둠 4단계 심연 각성: 확정 3종 ─────────────────────────────
        fx.Clear();
        near = Place(player, all, Mathf.Min(all.Count, 3), 1.5f);
        Reset(all);
        var erosion = Row(table, "DarkErosion");
        var release = Row(table, "DarkRelease");
        var abyss = Row(table, "DarkAbyss");
        fx.Activate(erosion);
        fx.Activate(release);
        fx.Activate(Row(table, "DarkAfterimage"));
        fx.Activate(abyss);
        fx.Resources.AddGauge("darkGauge", erosion.max_stack, erosion.max_stack);   // 게이지 가득 → 암흑 해방
        fx.Tick(0f);
        fx.Tick(0f);   // 잠식(합산 소유자)이 해방보다 먼저 틱한다 — 해방 몫은 다음 틱에 실린다
        float until = (float)typeof(DarkReleaseEffect).GetField("_releaseUntil", Inst).GetValue(fx.GetActive("DarkRelease"));
        float left = until - Time.time;
        float atkPct = (float)typeof(PlayerRuntimeStats).GetField("_synergyDynAttackPct", Inst).GetValue(stats);
        float dr = (float)typeof(PlayerRuntimeStats).GetField("_synergyDynDamageReduction", Inst).GetValue(stats);
        Check(Mathf.Abs(left - (release.duration + abyss.value3)) < 0.1f,
              $"암흑 해방 지속 {left:0.0}초 (기대 {release.duration:0.#} + {abyss.value3:0.#})");
        Check(Mathf.Abs(atkPct - (release.value + abyss.value)) < 0.0005f,
              $"해방 중 공격력 층 +{atkPct * 100f:0.#}% (기대 해방 {release.value * 100f:0.#} + 각성 {abyss.value * 100f:0.#})");
        Check(Mathf.Abs(dr - release.value2) < 0.0005f, $"해방 중 받는 피해 −{dr * 100f:0.#}% (기대 {release.value2 * 100f:0.#} — 각성 몫 없음)");
        int vuln = 0;
        foreach (var m in near) if (Mathf.Abs(m.DamageTakenAmpTotal - abyss.value2) < 0.0005f) vuln++;
        Check(vuln == near.Count, $"주변 적 {vuln}/{near.Count}명 받는 피해 +{abyss.value2 * 100f:0.#}% · 남은 {near[0].DamageTakenAmpRemaining:0.0}초");
        Check(fx.Resources.GetGauge("darkGauge") == 0, $"해방 뒤 게이지 {fx.Resources.GetGauge("darkGauge")} (기대 0)");

        // ── 되돌리기 ────────────────────────────────────────────────
        fx.Clear();
        foreach (var e in restore) fx.Activate(e);
        if (!Mathf.Approximately(restoreAmp, 1f)) fx.SetAmplifier(restoreAmp);
        if (restoreZoneAmp.Count > 0) fx.SetZoneAmplifiers(restoreZoneAmp);
        fx.Tick(0f);
        Reset(all);
        foreach (var m in all) if (m != null) m.HpFloorMin1 = false;
        sb.AppendLine($"되돌림 — 켜져 있던 시너지 {restore.Count}단계 다시 켬");

        Finish(sb, pass, fail);
    }

    private static void Finish(StringBuilder sb, int pass, int fail)
    {
        sb.Insert(0, $"룬 시너지 실측 — 통과 {pass} · 실패 {fail}\n");
        File.WriteAllText(Path.Combine("Temp", "rune_synergy_probe.txt"), sb.ToString());
        Debug.Log("[RuneSynergyProbe] 완료 — Temp/rune_synergy_probe.txt\n" + sb);
    }

    // ── 표 ───────────────────────────────────────────────────────
    private static Dictionary<string, RuneSynergyEntry> LoadCsv()
    {
        var map = new Dictionary<string, RuneSynergyEntry>();
        var lines = File.ReadAllLines(CsvPath, Encoding.UTF8);
        var head = Split(lines[0].TrimStart('﻿'));
        int Col(string name) => head.IndexOf(name);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var c = Split(lines[i]);
            var e = new RuneSynergyEntry
            {
                zone_id = c[Col("zone_id")], zone_name = c[Col("zone_name")], threshold = int.Parse(c[Col("threshold")]),
                effect_type = c[Col("effect_type")], trigger = c[Col("trigger")],
                value = F(c[Col("value")]), value2 = F(c[Col("value2")]), value3 = F(c[Col("value3")]),
                max_stack = (int)F(c[Col("max_stack")]), duration = F(c[Col("duration")]),
                description = c[Col("description")], stat_version = 1,
            };
            map[e.effect_type] = e;
        }
        return map;
    }

    private static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    private static List<string> Split(string line)
    {
        var res = new List<string>();
        var cur = new StringBuilder();
        bool quoted = false;
        foreach (char ch in line)
        {
            if (ch == '"') quoted = !quoted;
            else if (ch == ',' && !quoted) { res.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(ch);
        }
        res.Add(cur.ToString());
        return res;
    }

    private static RuneSynergyEntry Row(Dictionary<string, RuneSynergyEntry> table, string type) => table[type];

    /// <summary>게임이 지금 읽는 차트(CDN)와 저장소 표가 다른 행 — 업로드 전이면 여기 나온다.</summary>
    private static void DiffAgainstChart(Dictionary<string, RuneSynergyEntry> table, StringBuilder sb)
    {
        var chart = Managers.RuneData?.GetAllZoneSynergies();
        if (chart == null) { sb.AppendLine("차트를 읽지 못했다"); return; }
        int n = 0;
        foreach (var kv in chart)
            foreach (var e in kv.Value)
            {
                if (!table.TryGetValue(e.effect_type, out var t)) continue;
                if (Mathf.Approximately(e.value, t.value) && Mathf.Approximately(e.value2, t.value2) &&
                    Mathf.Approximately(e.value3, t.value3) && e.description == t.description) continue;
                n++;
                sb.AppendLine($"  차트≠표 {e.effect_type}: 차트 {e.value:0.###}/{e.value2:0.###}/{e.value3:0.###} → 표 {t.value:0.###}/{t.value2:0.###}/{t.value3:0.###}");
            }
        sb.AppendLine(n == 0 ? "게임 차트 = 저장소 표 (업로드됨)" : $"게임 차트와 다른 행 {n} — CDN 업로드 전");
    }

    // ── 적 ───────────────────────────────────────────────────────
    private static DamageReport Report(PlayerController player, MonsterBase target) => new DamageReport
    {
        DamageDealt = HitDamage, Target = target.gameObject, Attacker = player.gameObject,
        HitPosition = target.transform.position, ActionType = WeaponActionType.GroundLight,
    };

    /// <summary>가까운 적 count명을 플레이어 곁 radius로 모은다.</summary>
    private static List<MonsterBase> Place(PlayerController player, List<MonsterBase> all, int count, float radius)
    {
        var list = all.GetRange(0, Mathf.Min(count, all.Count));
        Vector3 p = player.transform.position;
        for (int i = 0; i < list.Count; i++)
        {
            float ang = i * Mathf.PI * 2f / Mathf.Max(1, list.Count);
            Warp(list[i], p + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * radius);
        }
        Physics.SyncTransforms();
        return list;
    }

    /// <summary>target만 플레이어 곁에 두고 나머지를 8m 넘게 떼어 놓는다. 다 떼어 놓았으면 true.</summary>
    private static bool Isolate(PlayerController player, MonsterBase target, List<MonsterBase> all)
    {
        Vector3 p = player.transform.position;
        Warp(target, p + Vector3.forward * 1.5f);
        int k = 0;
        foreach (var m in all)
        {
            if (m == target) continue;
            bool moved = false;
            for (int tries = 0; tries < 12 && !moved; tries++, k++)
            {
                float ang = k * Mathf.PI * 2f / 12f;
                Vector3 want = p + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 10f;
                if (!NavMesh.SamplePosition(want, out var hit, 2f, NavMesh.AllAreas)) continue;
                if (Vector3.Distance(hit.position, target.transform.position) < 8f) continue;
                Warp(m, hit.position);
                moved = true;
            }
            if (!moved) return false;
        }
        Physics.SyncTransforms();
        foreach (var m in all)
            if (m != target && Vector3.Distance(m.transform.position, target.transform.position) < 6f) return false;
        return true;
    }

    private static void Warp(MonsterBase m, Vector3 to)
    {
        if (m.TryGetComponent<NavMeshAgent>(out var agent) && agent.isOnNavMesh) agent.Warp(to);
        else m.transform.position = to;
    }

    /// <summary>체력을 채우고 상태 · 받는 피해 증폭을 비운다 — 재는 것마다 같은 출발선.</summary>
    private static void Reset(List<MonsterBase> list)
    {
        var rt = typeof(MonsterBase).GetField("_runtime", Inst);
        var amp = typeof(MonsterBase).GetField("_dmgTakenAmpSlots", Inst);
        foreach (var mb in list)
        {
            if (mb == null) continue;
            if (rt?.GetValue(mb) is MonsterRuntimeData data) data.CurrentHp = mb.EffectiveMaxHp;
            mb.Status.Reset();
            (amp?.GetValue(mb) as IDictionary)?.Clear();
        }
    }

    private static long[] Snapshot(List<MonsterBase> list)
    {
        var hp = new long[list.Count];
        for (int i = 0; i < list.Count; i++) hp[i] = list[i].CurrentHp;
        return hp;
    }

    private static float AttackSpeedLayer(PlayerRuntimeStats stats)
        => (float)typeof(PlayerRuntimeStats).GetField("_synergyDynAttackSpeed", Inst).GetValue(stats);
}
