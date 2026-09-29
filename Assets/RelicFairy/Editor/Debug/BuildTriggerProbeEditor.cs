using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 도구 · 플레이 중 · 전투방에서] 빌드 컨셉 「발동 계열」 T2 — 새 발동 룬 18종이 제 발동에서 터지는지 잰다.
///
/// <para>룬 하나씩 판에 올려(효과 재구성) 발동을 실제 훅 순서로 건다 — 대시 끝(OnRollEnd) · 적중 보고 · 처치 · 스킬 사용 · 피격.
/// 적은 HP 바닥 1로 세워 죽지 않게 하고, 룬마다 가까운 적 셋을 플레이어 곁 1.5m로 모은 뒤 체력을 채운다.
/// 폭발 룬은 속성을 불 · 얼음 · 번개 · 숲 · 빛 · 어둠 순으로 돌려 탄두 여섯 가지를 모두 지난다.</para>
///
/// <para>CDN 업로드 전엔 차트에 새 룬이 없다 — 그때는 Resources/ITEM_DATA.json의 행을 <b>메모리에만</b> 넣고, 끝나면 뺀다(저장 안 함).</para>
/// 결과: Temp/build_trigger_probe.txt
/// </summary>
public static class BuildTriggerProbeEditor
{
    private const string Root = "RelicFairy/Debug/발동 계열 실측/";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static readonly string[] Elements = { "FIRE", "ICE", "ELECTRIC", "GRASS", "LIGHT", "DARK" };

    private static readonly string[] RuneIds =
    {
        "item_t2_trig_dash", "item_t2_trig_dash_strike", "item_t2_trig_perfect", "item_t2_trig_crit", "item_t2_trig_crit_stack",
        "item_t2_trig_skill_hit", "item_t2_trig_skill_chain", "item_t2_trig_skill_cast", "item_t2_trig_kill", "item_t2_trig_multikill",
        "item_t2_trig_execute", "item_t2_trig_ranged", "item_t2_trig_same", "item_t2_trig_retaliate",
        "item_t2_dash_fury", "item_t2_crit_gale", "item_t2_kill_gale", "item_t2_aim_fury",
    };

    private static readonly List<BuffViewItem> s_views = new();

    [MenuItem(Root + "발동 룬 18종 발동 실측 (전투방에서)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[BuildTriggerProbe] 플레이 모드에서만"); return; }
        var run = GameRunBootstrapper.Instance?.Run;
        var player = run?.Player;
        if (player == null) { Debug.LogWarning("[BuildTriggerProbe] 런이 없다"); return; }

        var sb = new StringBuilder();
        var injected = InjectChartRows(sb);
        var inv = run.ItemInventory;
        var placed = (List<RuntimeItemData>)typeof(RunItemInventory).GetField("_placedItems", Inst).GetValue(inv);
        var mgr = run.EffectManager;
        typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(player, true);

        foreach (var m in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None)) m.HpFloorMin1 = true;
        int atk = player.RuntimeStats != null
            ? player.RuntimeStats.GetEffectiveAttack((player.WeaponManager?.CurrentWeaponData?.weaponType ?? WeaponType.None).GetAttackStatKind())
            : 0;
        sb.AppendLine($"유효 공격력 {atk} · 무기 {player.WeaponManager?.CurrentWeaponData?.weaponType} · 판에 있던 룬 {placed.Count}");

        int burstIndex = 0, pass = 0, fail = 0;
        foreach (var id in RuneIds)
        {
            var entries = Managers.ItemData?.GetItem(id);
            if (entries == null) { sb.AppendLine($"{id}: 차트 행 없음 ✗"); fail++; continue; }
            var item = RuntimeItemData.FromServer(entries);
            bool burst = entries[0].effect_type == "TriggerBurst";
            item.element = burst ? Elements[burstIndex++ % Elements.Length] : "FIRE";

            var targets = Gather(player, 3);
            if (targets.Count == 0) { sb.AppendLine("전투방이 아니다 — 적이 없다"); break; }
            Refill(targets);

            placed.Add(item);
            mgr.Rebuild();
            var fam = BuildFamilyRules.OfItem(item);
            var main = targets[0];
            long before = SumHp(player);

            string trig = entries[0].trigger;
            Fire(trig, mgr, player, main, item);
            mgr.OnTick(0f);

            long after = SumHp(player);
            long dealt = before - after;
            string status = Status(main);
            s_views.Clear(); mgr.CollectActiveBuffViews(s_views);
            int views = s_views.Count;

            // 최소 간격 — 같은 발동을 곧바로 한 번 더(대시 · 피격 · 치명 · 처치 · 스킬).
            string icd = "";
            if (burst && trig is "OnDash" or "AfterHit" or "OnCrit" or "OnKill" or "OnSkillUse")
            {
                long b2 = SumHp(player);
                Fire(trig, mgr, player, main, item);
                icd = $" · 곧바로 다시 {b2 - SumHp(player)}";
            }

            bool ok = burst ? dealt > 0 : (trig == "WithRangedWeapon" ? true : views > 0);
            if (ok) pass++; else fail++;
            sb.AppendLine($"{(ok ? "✓" : "✗")} {entries[0].item_name} [{BuildFamilyRules.Label(fam)}] {trig}" +
                          (burst ? $" · {item.element} · 피해 {dealt} (적 {targets.Count}) · 상태 {status}{icd}"
                                 : $" · 버프 표시 {views}{(trig == "WithRangedWeapon" ? " (근접 무기면 0이 맞다)" : "")}"));

            placed.Remove(item);
            mgr.Rebuild();
        }

        // 각인 단계 — 기동 룬 둘을 같이 올리면 1단계(×1.2)가 되는가.
        var a = RuntimeItemData.FromServer(Managers.ItemData.GetItem("item_t2_trig_dash"));
        var b = RuntimeItemData.FromServer(Managers.ItemData.GetItem("item_t2_dash_fury"));
        if (a != null && b != null)
        {
            a.element = "FIRE";
            placed.Add(a); placed.Add(b);
            mgr.Rebuild();
            float v = -1f;
            foreach (var eff in mgr.ActiveEffects)
                if (eff is TriggerBurstEffect tb) v = (float)typeof(ItemEffectBase).GetField("_value", Inst).GetValue(tb);
            sb.AppendLine($"각인: 기동 {BuildImprint.Count(BuildFamily.Mobility)} · {BuildImprint.Stage(BuildFamily.Mobility)}단계 · " +
                          $"내닫는 격발 value {v:0.###} (원래 0.9 → 기대 1.08)");

            // T3 — 단계 전용 스탯 · HUD 버프 줄
            var dyn = new ItemDynamicStats();
            BuildImprint.ContributeStats(null, ref dyn);
            s_views.Clear(); BuildImprint.CollectBuffViews(s_views);
            sb.AppendLine($"단계 스탯: 이동속도 +{dyn.moveSpeed:0.###} (기대 0.05) · 버프 줄 {s_views.Count}칸" +
                          (s_views.Count > 0 ? $" 「{s_views[0].Label}」" : ""));
            placed.Remove(a); placed.Remove(b);
            mgr.Rebuild();
        }

        // T4 — 무기 승급도 각인이다(메모리에서만 바꾸고 되돌린다).
        var w0 = player.WeaponManager?.Weapon0Data;
        if (w0 != null)
        {
            string oldLegend = w0.legendId;
            int critBefore = BuildImprint.Count(BuildFamily.Crit);
            w0.legendId = "excalibur";
            mgr.Rebuild();
            int critAfter = BuildImprint.Count(BuildFamily.Crit);
            w0.legendId = oldLegend;
            mgr.Rebuild();
            sb.AppendLine($"무기 승급(엑스칼리버) 필살 각인 {critBefore} → {critAfter} (기대 +1) · 되돌림 {BuildImprint.Count(BuildFamily.Crit)}");
        }

        foreach (var m in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None)) m.HpFloorMin1 = false;
        RemoveChartRows(injected);
        sb.Insert(0, $"발동 룬 실측 — 통과 {pass} · 실패 {fail}\n");
        File.WriteAllText(Path.Combine("Temp", "build_trigger_probe.txt"), sb.ToString());
        Debug.Log("[BuildTriggerProbe] 완료 — Temp/build_trigger_probe.txt\n" + sb);
    }

    // ── 발동 걸기 ─────────────────────────────────────────
    private static void Fire(string trig, ItemEffectManager mgr, PlayerController player, MonsterBase target, RuntimeItemData item)
    {
        var go = target.gameObject;
        switch (trig)
        {
            case "OnDash":
            case "AfterDash":       mgr.OnRollEnd(); break;
            case "AfterDashHit":    mgr.OnRollEnd(); mgr.OnPostDealDamage(Report(go)); break;
            case "OnPerfectDodge":
                foreach (var eff in mgr.ActiveEffects)
                    if (eff is TriggerBurstEffect)
                        typeof(TriggerBurstEffect).GetMethod("HandlePerfectDodge", Inst).Invoke(eff, new object[] { 1f });
                break;
            case "OnCrit":
            case "AfterCrit":       mgr.OnPostDealDamage(Report(go, crit: true)); break;
            case "OnCritCount":     for (int i = 0; i < 6; i++) mgr.OnPostDealDamage(Report(go, crit: true)); break;
            case "OnSkillHit":      mgr.OnPostDealDamage(Report(go, action: WeaponActionType.QSkill)); break;
            case "OnSkillChain":    mgr.OnSkillUse(SkillType.Q); mgr.OnSkillUse(SkillType.E); break;
            case "OnSkillUse":      mgr.OnSkillUse(SkillType.Q); break;
            case "OnKill":
            case "AfterKill":       mgr.OnKill(go); break;
            case "OnMultiKill":     for (int i = 0; i < 3; i++) mgr.OnKill(go); break;
            case "OnExecute":
                target.TakeSynergyDamage(Mathf.Max(1, target.CurrentHp - Mathf.FloorToInt(target.EffectiveMaxHp * 0.25f)),
                                         player.gameObject, 1f, false, DamageKind.Synergy);
                mgr.OnPostDealDamage(Report(go));
                break;
            case "OnRangedHit":     for (int i = 0; i < 5; i++) mgr.OnPostDealDamage(Report(go, ranged: true)); break;
            case "SameTarget":      for (int i = 0; i < 5; i++) mgr.OnPostDealDamage(Report(go)); break;
            case "AfterHit":        mgr.OnPostTakeDamage(new DamageReport { DamageDealt = 5f, Attacker = go, Target = player.gameObject }); break;
            case "WithRangedWeapon": break;
        }
    }

    private static DamageReport Report(GameObject target, bool crit = false, bool ranged = false, WeaponActionType action = WeaponActionType.GroundLight)
        => new DamageReport
        {
            DamageDealt = 10f, Target = target, IsCrit = crit, IsRanged = ranged, ActionType = action,
            HitPosition = target.transform.position,
            Attacker    = GameRunBootstrapper.Instance.Run.Player.gameObject,
        };

    // ── 적 ───────────────────────────────────────────────
    private static List<MonsterBase> Gather(PlayerController player, int count)
    {
        var list = new List<MonsterBase>();
        foreach (var mb in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (!mb.IsDead) list.Add(mb);
        Vector3 p = player.transform.position;
        list.Sort((a, b) => (a.transform.position - p).sqrMagnitude.CompareTo((b.transform.position - p).sqrMagnitude));
        if (list.Count > count) list.RemoveRange(count, list.Count - count);
        for (int i = 0; i < list.Count; i++)
        {
            float ang = i * Mathf.PI * 2f / Mathf.Max(1, list.Count);
            Vector3 to = p + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 1.5f;
            if (list[i].TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var agent) && agent.isOnNavMesh) agent.Warp(to);
            else list[i].transform.position = to;
        }
        Physics.SyncTransforms();
        return list;
    }

    /// <summary>체력을 가득 채우고 상태를 비운다 — 룬마다 같은 출발선.</summary>
    private static void Refill(List<MonsterBase> list)
    {
        var f = typeof(MonsterBase).GetField("_runtime", Inst);
        foreach (var mb in list)
        {
            if (f?.GetValue(mb) is MonsterRuntimeData rt) rt.CurrentHp = mb.EffectiveMaxHp;
            mb.Status.Reset();
        }
    }

    private static long SumHp(PlayerController player)
    {
        long s = 0;
        Vector3 p = player.transform.position;
        foreach (var mb in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (!mb.IsDead && Vector3.Distance(mb.transform.position, p) <= 12f) s += mb.CurrentHp;
        return s;
    }

    private static string Status(MonsterBase mb)
    {
        var parts = new List<string>();
        if (mb.Status.HasDot("ignite")) parts.Add("화상");
        if (mb.Status.HasDot("poison")) parts.Add("중독");
        if (mb.Status.GetSlowStacks("frost") > 0) parts.Add("둔화");
        if (mb.HasDamageTakenAmp) parts.Add("취약");
        return parts.Count > 0 ? string.Join("·", parts) : "-";
    }

    // ── 차트(메모리에만) ───────────────────────────────────
    private static List<string> InjectChartRows(StringBuilder sb)
    {
        var added = new List<string>();
        var chart = Managers.ItemData;
        if (chart == null) return added;
        var map = (Dictionary<string, List<ItemEntry>>)typeof(ItemDataManager).GetField("_itemById", Inst).GetValue(chart);
        var json = Resources.Load<TextAsset>("ITEM_DATA");
        if (json == null) return added;
        var all = JsonUtility.FromJson<ItemEntryCollection>(json.text);
        foreach (var e in all.items)
        {
            string id = string.IsNullOrEmpty(e.item_id) ? e.passive_id : e.item_id;
            if (System.Array.IndexOf(RuneIds, id) < 0) continue;
            if (map.ContainsKey(id) && !added.Contains(id)) continue;   // 차트가 이미 안다(CDN 업로드 후)
            if (!map.TryGetValue(id, out var list)) { list = new List<ItemEntry>(); map[id] = list; added.Add(id); }
            list.Add(e);
        }
        sb.AppendLine(added.Count > 0 ? $"차트에 없던 새 룬 {added.Count}종을 메모리에만 넣었다(CDN 업로드 전)" : "차트가 새 룬을 이미 안다");
        return added;
    }

    private static void RemoveChartRows(List<string> added)
    {
        var chart = Managers.ItemData;
        if (chart == null || added.Count == 0) return;
        var map = (Dictionary<string, List<ItemEntry>>)typeof(ItemDataManager).GetField("_itemById", Inst).GetValue(chart);
        foreach (var id in added) map.Remove(id);
    }
}
