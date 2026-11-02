#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-31 사용자 「룬 실제 수치가 다 적용되는지 — 보스 피해 증가 같은 조건 스탯까지 · 설명과 실제 수치가 같은지 ·
/// 그리드에 등록하면 적용되고 빼면 완전히 사라지는지 · 시너지도」 실측(테스트 허브 런 중 · 약 2분).
/// <para><b>① 룬 하나씩</b> — 차트(런타임 ITEM_DATA)의 룬마다 보관함에 넣고 <b>진짜 경로</b>(룬판 고르기 → 칸 누르기)로 놓는다.
/// 놓기 전 · 놓은 뒤 · 뺀 뒤 플레이어 스탯 레이어(PlayerRuntimeStats 모든 칸 · 공개 값)를 찍어 비교한다.
/// 효과 인스턴스 값 = 차트 값 × 각인 배율인지, 정적 기여(ModifyStats)가 스탯 칸으로 들어갔는지,
/// 조건부(Cond* 등)는 그 조건 신호를 <b>그 효과에만</b> 넣어 기여 · 실제 스탯 칸 변화를 잰다(체력 조건은 잠깐 체력을 낮췄다 되돌린다).
/// 피해 보정(정예·보스 피해 · 원거리 · 구르기 뒤 첫 타 등)은 아이템 사전 보정(ItemEffectManager.OnPreDealDamage — 주 피해 ②단계)으로
/// 일반 적 · 정예/보스 대상 배율을 잰다. 뺀 뒤엔 아이템 칸 · 공개 값 · 효과 수 · 이벤트 구독 수 · 전설 생성물 · 정적 값이 놓기 전과 같은지 본다.</para>
/// <para><b>② 시너지</b> — 브리지(MerlinRuneBridge.OnZoneCellsUpdated — 판 점유 칸 수가 들어가는 바로 그 입구)에 존별 칸 수를 넣어
/// 6속성 × 4단계를 하나씩 켜고 · 내려 보고 · 끈다. 단계 효과 켜짐/꺼짐 · 스탯 레이어(전기 공속 · 빛 치확 · 어둠 공격력) · 자원 잔존 ·
/// 속성 반응 6쌍 · 중앙 공명 배수를 본다. 끝나면 실제 판 칸 수로 되돌린다.</para>
/// <para>전제: 테스트 허브 런 중(플레이어 있음). 판이 비어 있을수록 깨끗하다(각인 단계가 다른 룬과 섞이지 않는다).
/// 정예/보스가 있는 방이면 「정예·보스 피해」도 잰다. 끝에 넣은 룬은 회수 · 보관함에서 빼고 판 시너지 · 체력 · 무적을 되돌린다.</para>
/// 결과: Temp/rune_stat_audit1031.txt · 로그 「[RSA31] 끝」.
/// </summary>
public static class RuneStatAudit1031ProbeEditor
{
    // ── Constants ──
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Stat = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private const string LogTag  = "[RSA31]";
    private const string OutPath = "Temp/rune_stat_audit1031.txt";
    private const string JsonPath = "Assets/RelicFairy/Resources/ITEM_DATA.json";
    private const string SynJsonPath = "Assets/RelicFairy/ChartData/merlin_rune_synergy_data.json";
    private const double Eps = 0.0005;
    private static readonly string[] Elems = { "FIRE", "ICE", "ELECTRIC", "GRASS", "LIGHT", "DARK" };

    // 정적 효과 → (누적기 칸, 플레이어 스탯 칸, 공개 값, 공개 값 배수)
    private static readonly Dictionary<string, (string acc, string prs, string prop, float propMul)> s_static = new()
    {
        ["AllDamage%"]             = ("AllDamagePercent",       "_itemAllDamagePercent", null,                    1f),
        ["AllDamageFlat"]          = ("AllDamageFlat",          "_itemMelee",            null,                    1f),
        ["MaxHP"]                  = ("MaxHP",                  "_itemMaxHp",            "MaxHp",                 1f),
        ["Defense"]                = ("Defense",                "_itemDefense",          null,                    1f),
        ["CritChance"]             = ("CritChancePercent",      "_itemCritChance",       "CritChanceBonus",       100f),
        ["CritDamage"]             = ("CritDamagePercent",      "_itemCritDamage",       "CritDamageBonus",       1f),
        ["SkillDamage"]            = ("SkillDamagePercent",     "_itemSkillDamage",      "SkillDamageBonus",      1f),
        ["MaxHPPercent"]           = ("MaxHPPercent",           "_itemMaxHpPercent",     null,                    1f),
        ["MoveSpeed"]              = ("MoveSpeed",              "_itemMoveSpeed",        "MoveSpeedMultiplier",   1f),
        ["AttackSpeed"]            = ("AttackSpeed",            "_itemAttackSpeed",      "AttackSpeedMultiplier", 1f),
        ["SkillCooldownReduction"] = ("SkillCooldownReduction", "_itemSkillCdr",         "SkillCooldownReduction",1f),
        ["DefensePercent"]         = ("DefensePercent",         "_itemDefensePercent",   null,                    1f),
        ["ProjectilePierce"]       = ("ProjectilePierceBonus",  "_itemProjectilePierce", "ProjectilePierceBonus", 1f),
        ["ProjectileCount"]        = ("ProjectileCountBonus",   "_itemProjectileCount",  "BonusProjectile",       1f),
    };

    // 동적(조건부) 효과 → ItemDynamicStats 칸
    private static readonly Dictionary<string, string> s_dyn = new()
    {
        ["CondAttackPercent"] = "attackPercent", ["CondDefensePercent"] = "defensePercent", ["CondAttackSpeed"] = "attackSpeed",
        ["CondMoveSpeed"] = "moveSpeed", ["CondCritChance"] = "critChance", ["CondCritDamage"] = "critDamage",
        ["CondSkillDamage"] = "skillDamage", ["CondAllDamage"] = "allDamage", ["CondMaxHpPercent"] = "maxHpPercent",
        ["CondDamageReduction"] = "damageReduction", ["HurtGuardFrost"] = "damageReduction", ["HurtGuardShadow"] = "damageReduction",
        ["CritDamageStack"] = "critDamage", ["WardRampart"] = "maxHpPercent", ["WardFinisherGuard"] = "damageReduction",
    };

    // 동적 칸 → 공개 값(배수)
    private static readonly Dictionary<string, (string prop, float mul)> s_dynProp = new()
    {
        ["critChance"] = ("CritChanceBonus", 100f), ["critDamage"] = ("CritDamageBonus", 1f), ["attackSpeed"] = ("AttackSpeedMultiplier", 1f),
        ["moveSpeed"] = ("MoveSpeedMultiplier", 1f), ["skillDamage"] = ("SkillDamageBonus", 1f), ["damageReduction"] = ("DamageReduction", 1f),
    };

    // ── Static ──
    private static StringBuilder _sb;
    private static int _pass, _fail, _warn, _skip;
    private static bool _running;

    // ── Types ──
    [Serializable] private class ItemRow { public string item_id; public string passive_id; public int slot; public string effect_type; public string trigger; public float value; public float value2; public float value3; public int max_stack; public float duration; public string description; public int shape_id; public string grade; public string element; }
    [Serializable] private class ItemRows { public List<ItemRow> items; }
    [Serializable] private class SynRow { public string zone_id; public int threshold; public string effect_type; public float value; public float value2; public float value3; public int max_stack; public float duration; public string description; }
    [Serializable] private class SynRows { public List<SynRow> synergies; }

    private sealed class Ctx
    {
        public GameRunSession Run;
        public PlayerController Player;
        public PlayerRuntimeStats Stats;
        public ItemEffectManager Mgr;
        public ItemEffectContext ItemCtx;
        public RunItemInventory Inv;
        public UI_GridPanel Panel;
        public MerlinRuneBridge Bridge;
        public RuneEffectDispatcher Fx;
        public MerlinRuneHexGridView View;
        public MonsterBase Normal, Tough;
        public GameObject Dummy;
        public Dictionary<string, int> RealCounts;          // 시작 때 판 칸 수(뷰를 못 찾을 때 되돌림 폴백)
        public Dictionary<string, float> RealAmps;          // 시작 때 존핵 배수
        public readonly List<RuntimeItemData> Added = new();
        public readonly List<RuntimeItemData> Placed = new();
    }

    private sealed class Snap
    {
        public readonly Dictionary<string, double> Stats = new();
        public readonly Dictionary<string, int> Dels = new();
        public int Effects, Agents, Hud, Fields, Hp;
        public float KillBonus, NextBonus, Amp, PreNormal = -1f, PreTough = -1f;
        public string Mods, FxTypes;
        public readonly int[] Stages = new int[16];
        public readonly Dictionary<string, int> Tiers = new();
    }

    [MenuItem("RelicFairy/Debug/10-31 룬 실수치 전수 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{LogTag} 플레이 중(테스트 허브)에만 동작한다."); return; }
        if (_running) { Debug.LogWarning($"{LogTag} 이미 도는 중"); return; }
        RunAsync().Forget();
    }

    // ═════════════════════════════════════════════════════════
    // 진행
    // ═════════════════════════════════════════════════════════
    private static async UniTaskVoid RunAsync()
    {
        _running = true;
        _sb = new StringBuilder("10-31 룬 실수치 전수 — 놓기 · 빼기 · 조건 · 시너지\n");
        _pass = _fail = _warn = _skip = 0;
        var c = new Ctx();
        bool invWas = false;
        int hpStart = -1;
        HashSet<string> placedStart = null, stagingStart = null;
        try
        {
            if (GameRunBootstrapper.Instance?.Run?.Player == null)
            {
                var launcher = UnityEngine.Object.FindFirstObjectByType<TestHubLauncher>();
                if (launcher != null) { launcher.SelectChapter(1); launcher.TryLaunch(); }
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 60f && GameRunBootstrapper.Instance?.Run?.Player == null)
                    await UniTask.Delay(500, DelayType.Realtime);
                if (GameRunBootstrapper.Instance?.Run?.Player == null) { Line("런이 시작되지 않음(60초)"); return; }
                await UniTask.Delay(3000, DelayType.Realtime);
            }

            c.Run    = GameRunBootstrapper.Instance.Run;
            c.Player = c.Run.Player;
            c.Stats  = c.Player.RuntimeStats;
            c.Mgr    = c.Run.EffectManager;
            c.Inv    = c.Run.ItemInventory;
            c.Bridge = MerlinRuneBridge.Instance;
            c.Fx     = c.Player.RuneEffects;
            c.ItemCtx = GetField(c.Mgr, "_ctx") as ItemEffectContext;
            if (c.Stats == null || c.Mgr == null || c.Inv == null || c.ItemCtx == null || Managers.ItemData == null)
            { Line("런 구성 요소가 없다(스탯 · 효과 관리자 · 인벤토리 · 차트)"); return; }

            invWas = GetField(c.Player, "debugInvincible") is bool b0 && b0;
            SetField(c.Player, "debugInvincible", true);
            hpStart = c.Stats.Hp;
            placedStart  = new HashSet<string>(c.Inv.PlacedItems.Select(i => i.instanceId));
            stagingStart = new HashSet<string>(c.Inv.StagingItems.Select(i => i.instanceId));
            c.Dummy = new GameObject("RSA31_Dummy");
            c.Dummy.transform.position = c.Player.transform.position + new Vector3(0f, -50f, 0f);
            PickTargets(c);
            if (c.Bridge != null)
            {
                c.RealCounts = c.Bridge.GetZoneOccupiedCounts().ToDictionary(k => k.Key, k => k.Value);
                c.RealAmps = (GetField(c.Bridge, "_lastZoneAmps") as IReadOnlyDictionary<string, float>)?.ToDictionary(k => k.Key, k => k.Value);
            }

            Line($"플레이어 무기 {c.Player.WeaponManager?.CurrentWeaponData?.weaponType} · 판 룬 {c.Inv.PlacedCount}개(판이 비어 있을수록 각인이 섞이지 않는다) · 보관함 {c.Inv.StagingCount}개");
            Line($"대상: 일반 {(c.Normal != null ? c.Normal.name : "없음")} · 정예/보스 {(c.Tough != null ? $"{c.Tough.name}({c.Tough.Grade})" : "없음 — 「정예·보스 피해」 실측 건너뜀(정예/보스 방에서 다시)")}");
            Line($"각인 단계(시작): {Stages()}");

            CompareItemData();
            CompareSynergyData();

            // ── ① 룬 하나씩 ──
            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            c.Panel = UI_GridPanel.Instance;
            if (c.Panel == null) { Line("룬판(UI_GridPanel)을 열지 못했다 — ① 건너뜀"); }
            else
            {
                c.Panel.ShowWithNewItem(null);
                await Ui(1200);
                c.View = UnityEngine.Object.FindFirstObjectByType<MerlinRuneHexGridView>(FindObjectsInactive.Include);
                Line("\n════ ① 룬 하나씩 — 놓기 · 조건 · 빼기 ════");
                var ids = Managers.ItemData.GetAllItems()
                    .Where(kv => kv.Value != null && kv.Value.Count > 0 && kv.Value[0].shape_id > 0)
                    .OrderBy(kv => GradeOrder(kv.Value[0].ResolvedRarity)).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => kv.Key).ToList();
                foreach (var id in ids)
                {
                    if (!EditorApplication.isPlaying) return;
                    try { await AuditRuneAsync(c, id); }
                    catch (Exception e) { Fail($"{id} — 예외 {e.GetType().Name}: {e.Message}"); Debug.LogException(e); }
                    // 남은 것 정리(예외로 빠졌을 때)
                    foreach (var d in c.Added.ToList())
                    {
                        if (c.Inv.IsPlaced(d.instanceId)) { Call(c.Panel, "RemovePlacedItem", d); await Ui(200); }
                        Discard(c, d);
                    }
                    c.Added.Clear();
                }
                Call(c.Panel, "ClosePanel");
                await UniTask.Delay(600, DelayType.Realtime);
            }

            // ── ② 시너지 ──
            if (c.Bridge == null || c.Fx == null) Line("브리지/디스패처가 없다 — ② 건너뜀");
            else
            {
                try { await AuditSynergyAsync(c); }
                catch (Exception e) { Fail($"시너지 — 예외 {e.GetType().Name}: {e.Message}"); Debug.LogException(e); }
            }
        }
        catch (Exception e) { Fail("예외: " + e); }
        finally
        {
            try
            {
                if (c.Panel != null)
                    foreach (var d in c.Added.ToList())
                    {
                        if (c.Inv != null && c.Inv.IsPlaced(d.instanceId)) Call(c.Panel, "RemovePlacedItem", d);
                        Discard(c, d);
                    }
                RestoreBoardSynergy(c);
                if (c.Stats != null && hpStart > 0 && c.Stats.Hp != hpStart) { c.Stats.SetHp(Mathf.Min(hpStart, c.Stats.MaxHp)); c.Mgr?.RefreshHpRatio(); }
                if (c.Player != null) SetField(c.Player, "debugInvincible", invWas);
                if (c.Dummy != null) UnityEngine.Object.Destroy(c.Dummy);
                if (c.Inv != null && placedStart != null)
                {
                    var p = new HashSet<string>(c.Inv.PlacedItems.Select(i => i.instanceId));
                    var s = new HashSet<string>(c.Inv.StagingItems.Select(i => i.instanceId));
                    Check("끝 — 인벤토리 그대로(판 · 보관함 룬 같음)", p.SetEquals(placedStart) && s.SetEquals(stagingStart),
                          $"판 {placedStart.Count}→{p.Count} · 보관함 {stagingStart.Count}→{s.Count}");
                }
            }
            catch (Exception e) { Line("정리 예외: " + e.Message); }
            Line($"\n합계 — PASS {_pass} · FAIL {_fail} · WARN {_warn} · SKIP {_skip}");
            Directory.CreateDirectory("Temp");
            File.WriteAllText(OutPath, _sb.ToString());
            Debug.Log($"{LogTag} 끝 — PASS {_pass} · FAIL {_fail} · WARN {_warn} · SKIP {_skip} → {OutPath}");
            _running = false;
        }
    }

    // ═════════════════════════════════════════════════════════
    // ① 룬 하나
    // ═════════════════════════════════════════════════════════
    private static async UniTask AuditRuneAsync(Ctx c, string id)
    {
        var rows = Managers.ItemData.GetItem(id);
        var d = rows != null ? RuntimeItemData.FromServer(rows) : null;
        Line($"\n■ {id} 「{(d != null ? d.displayName : "?")}」 {(rows != null && rows.Count > 0 ? rows[0].ResolvedRarity : "")} · 모양 {(d != null ? d.shapeId : 0)} · 속성 {(d != null && !string.IsNullOrEmpty(d.element) ? d.element : "없음(어디든)")}");
        if (d == null || d.effects.Count == 0) { Skip("효과 없음"); return; }
        foreach (var r in rows) DescCheck(r);
        if (RuneBlockBudget.Used >= RuneBlockBudget.Max) { Skip($"판 가득({RuneBlockBudget.Used}/{RuneBlockBudget.Max})"); return; }
        if (!RuneBlockBudget.FitsSame(d)) { Skip("같은 룬 상한"); return; }

        var B = TakeSnap(c);
        if (!c.Inv.AddToStagingOverflow(d)) { Skip("보관함에 못 넣음"); return; }
        c.Added.Add(d);
        await Ui(200);
        var sq = FindFit(c, d, out bool tierSafe);
        if (sq == null) { Skip("놓을 자리 없음(속성 존이 찼거나 모양이 안 맞음)"); return; }
        Call(c.Panel, "OnStagingItemSelected", d);
        Call(c.Panel, "HandleSquareClicked", sq);
        await Ui(250);
        if (!c.Inv.IsPlaced(d.instanceId))
        {
            var toast = typeof(UI_GridPanel).GetField("_synergyToastText", Inst)?.GetValue(c.Panel) as TMPro.TMP_Text;
            var pt = typeof(UI_GridPanel).GetProperty("PlacementTarget", Inst)?.GetValue(c.Panel) as RuntimeItemData;
            var open = typeof(UI_GridPanel).GetField("_isOpen", Inst)?.GetValue(c.Panel);
            Fail($"진짜 경로로 놓기 실패(칸 누르기) — 알림 「{toast?.text}」 · 고른 룬 {(pt == d ? "이것" : pt == null ? "없음" : pt.displayName)} · 판 {RuneBlockBudget.Used}/{RuneBlockBudget.Max} · 열림 {open} · 칸 ({sq.col},{sq.row}) 존 {sq.zoneCode} · 맞는 자리 {tierSafe} · 보관함 {c.Inv.StagingItems?.Count}");
            return;
        }
        c.Placed.Add(d);
        var P = TakeSnap(c);

        bool stageChanged = !B.Stages.SequenceEqual(P.Stages);
        bool tierChanged  = Elems.Any(e => B.Tiers[e] != P.Tiers[e]) || !Mathf.Approximately(B.Amp, P.Amp);
        if (stageChanged) Warn($"놓자 각인 단계가 바뀜({Stages()}) — 다른 룬 값도 함께 바뀌어 스탯 칸 Δ는 참고만");
        if (tierChanged)  Line($"  (참고) 놓은 칸이 존 단계를 바꿈 {TierText(B)} → {TierText(P)} · 공명 {B.Amp:0.##}→{P.Amp:0.##}{(tierSafe ? "" : " — 단계를 안 바꾸는 자리가 없었다")}");

        // ── 슬롯마다 ──
        var expectPrs  = new Dictionary<string, double>();
        var expectProp = new Dictionary<string, double>();
        var dynExpect  = new Dictionary<string, double>();
        bool hpForced = false;
        int hpBefore = c.Stats.Hp;
        var forcedInst = new List<(IItemEffect inst, string ch, double exp, string label)>();
        foreach (var s in d.effects)
        {
            float mult = Mult(s, out var fam, out int stage);
            float ev2 = s.value2, ev3 = s.value3;
            if (stage > 0)
            {
                if (fam == BuildFamily.Combo && (s.trigger == "SameTarget" || s.trigger == "OnHitCount") && ev2 > 2f) ev2 = Mathf.Max(2f, ev2 - stage);
                if (fam == BuildFamily.Crit && s.trigger == "OnCritCount" && ev2 > 3f) ev2 = Mathf.Max(3f, ev2 - stage);
                if (fam == BuildFamily.Kill && s.effectType == "TriggerBurst" && ev3 > 0f) ev3 *= 1f + 0.15f * stage;
            }
            string head = $"  · [{s.effectType}/{s.trigger}] v={F(s.value)} v2={F(s.value2)} v3={F(s.value3)} dur={F(s.duration)} max={s.maxStack} · 계열 {BuildFamilyRules.Label(fam)}{(stage > 0 ? $" {stage}단계 ×{mult:0.##}" : "")} · 「{s.description}」";
            Line(head);
            var inst = FindInstance(c.Mgr, s);
            if (inst == null)
            {
                if (s.effectType == "AmplifyZone") Line("    존핵 — 효과 인스턴스 없음(정상 · 룬판이 존 배수로 적용)");
                else Fail("    효과 인스턴스가 없다(레지스트리 · Rebuild 확인)");
                continue;
            }
            if (inst is GenericStatEffect) Fail($"    미등록 effectType → GenericStatEffect 폴백(값 {s.value}을 공격력 고정 +로 더함)");
            if (inst is ConditionalStatBuffEffect && Convert.ToString(GetField(inst, "_channel")) == "None") Fail("    Cond 효과 채널 미인식 — 아무 스탯도 안 오른다");

            // 인스턴스 값 = 차트 × 각인
            float iv = F0(GetField(inst, "_value")), iv2 = F0(GetField(inst, "_value2")), iv3 = F0(GetField(inst, "_value3"));
            Check("    인스턴스 값 = 차트 × 각인", Near(iv, s.value * mult) && Near(iv2, ev2) && Near(iv3, ev3),
                  $"value {F(iv)}(기대 {F(s.value * mult)}) · v2 {F(iv2)}(기대 {F(ev2)}) · v3 {F(iv3)}(기대 {F(ev3)}) · {inst.GetType().Name}");

            // 정적 기여
            var acc = new AccumulatedStats();
            inst.ModifyStats(c.ItemCtx, ref acc);
            string accNz = NonZero(acc);
            string key = s.effectType == "AllDamage" ? (Mathf.Abs(s.value) >= 1f ? "AllDamageFlat" : "AllDamage%") : s.effectType;
            if (s_static.TryGetValue(key, out var map))
            {
                bool isInt = map.acc is "MaxHP" or "Defense" or "ProjectilePierceBonus" or "ProjectileCountBonus";
                double exp = isInt ? (int)iv : iv;
                double got = Convert.ToDouble(typeof(AccumulatedStats).GetField(map.acc).GetValue(acc));
                Check($"    정적 기여 {map.acc}", Near(got, exp), $"{F(got)} (기대 {F(exp)}){(accNz.Length > 0 ? " · 전체 " + accNz : "")}");
                Add(expectPrs, map.prs, exp);
                if (map.prop != null) Add(expectProp, map.prop, exp * map.propMul);
            }
            else if (accNz.Length > 0) Warn($"    예상 밖 정적 기여: {accNz}");

            // 동적(조건부) — 그 효과에만 조건 신호를 넣는다
            if (s_dyn.TryGetValue(s.effectType, out var ch))
            {
                string how = Force(c, inst, s, ref hpForced);
                var dyn = new ItemDynamicStats();
                inst.ContributeDynamicStats(c.ItemCtx, ref dyn);
                double got = Convert.ToDouble(typeof(ItemDynamicStats).GetField(ch).GetValue(dyn));
                double exp = iv;
                if (s.effectType == "CritDamageStack") exp = iv * Mathf.Max(1, Mathf.RoundToInt(iv2 > 0f ? iv2 : 3f));
                if (s.effectType == "WardRampart" && !MeleeHeld(c)) exp = 0;
                if (how == null)
                    Warn($"    조건 「{s.trigger}」은 강제 못 함 — 지금 기여 {ch} {F(got)}(조건이 서면 {F(exp)})");
                else
                {
                    Check($"    조건 기여 {ch} ({how})", Near(got, exp), $"{F(got)} (기대 {F(exp)})");
                    Add(dynExpect, ch, exp);
                }
            }
            else
            {
                var dyn = new ItemDynamicStats();
                inst.ContributeDynamicStats(c.ItemCtx, ref dyn);
                string nz = NonZero(dyn);
                if (nz.Length > 0) Line($"    (참고) 지금 동적 기여 {nz}");
            }

            DamageModCheck(c, inst, s, iv, iv2);
            if (inst is TriggerBurstEffect) WarheadReport(c, inst, s, iv);
            if (inst is KillBurstRadiusEffect) Check("    처치 폭발 반경 정적 값 Δ", Near(P.KillBonus - B.KillBonus, iv), $"{F(P.KillBonus - B.KillBonus)} (기대 {F(iv)})");
        }

        // ── 정적 · 동적이 실제 스탯 칸에 들어갔나 ──
        if (expectPrs.Count > 0)
        {
            foreach (var kv in expectPrs)
            {
                double dlt = Val(P, "f:" + kv.Key) - Val(B, "f:" + kv.Key);
                if (stageChanged) Line($"    (참고) 스탯 칸 {kv.Key} Δ {F(dlt)} (이 룬 몫 {F(kv.Value)})");
                else Check($"    스탯 칸 {kv.Key} Δ(놓기 전→후)", Near(dlt, kv.Value), $"{F(dlt)} (기대 {F(kv.Value)})");
            }
            foreach (var kv in expectProp)
            {
                double dlt = Val(P, "p:" + kv.Key) - Val(B, "p:" + kv.Key);
                bool capped = (kv.Key == "AttackSpeedMultiplier" && Val(P, "p:AttackSpeedMultiplier") >= PlayerRuntimeStats.MaxAttackSpeed - 1e-4)
                           || (kv.Key == "SkillCooldownReduction" && Val(P, "p:SkillCooldownReduction") >= PlayerRuntimeStats.MaxSkillCooldownReduction - 1e-4);
                if (stageChanged || tierChanged || capped) Line($"    (참고) 공개 값 {kv.Key} Δ {F(dlt)} (이 룬 몫 {F(kv.Value)}{(capped ? " · 상한" : "")})");
                else Check($"    공개 값 {kv.Key} Δ", Near(dlt, kv.Value, 0.002), $"{F(dlt)} (기대 {F(kv.Value)})");
            }
            if (expectPrs.TryGetValue("_itemAllDamagePercent", out var adp) && !stageChanged && !tierChanged)
            {
                int expM = ExpectedMelee(B, adp);
                int gotM = (int)Val(P, "p:MeleeAttack");
                Check("    근접 공격력(피해 계산의 기준) 변화", Math.Abs(gotM - expM) <= 1, $"{(int)Val(B, "p:MeleeAttack")} → {gotM} (기대 ≈{expM})");
            }
            if (expectPrs.TryGetValue("_itemMaxHp", out var mh) && !stageChanged)
                Line($"    (참고) 최대 체력 {Val(B, "p:MaxHp")} → {Val(P, "p:MaxHp")} · 현재 체력 {B.Hp} → {P.Hp}(최대 체력이 올라도 현재 체력은 안 오른다 — 회복 금지 정책)");
        }
        if (dynExpect.Count > 0)
        {
            await Ui(120);   // OnTick이 동적 레이어를 밀어 넣을 틈
            var F2 = TakeSnap(c);
            foreach (var kv in dynExpect)
            {
                double dlt = Val(F2, "f:_itemDyn." + kv.Key) - Val(P, "f:_itemDyn." + kv.Key);
                if (Near(dlt, kv.Value)) Pass($"    실경로 동적 칸 _itemDyn.{kv.Key} Δ {F(dlt)} (기대 {F(kv.Value)})");
                else if (Math.Abs(dlt) < 1e-6 && Math.Abs(kv.Value) > 1e-6) Fail($"    실경로 동적 칸 _itemDyn.{kv.Key} Δ 0 — 조건이 서도 플레이어 스탯에 안 들어간다(OnTick 미구동?) 기대 {F(kv.Value)}");
                else Warn($"    실경로 동적 칸 _itemDyn.{kv.Key} Δ {F(dlt)} (이 룬 몫 {F(kv.Value)} — 같은 조건의 다른 룬 · 각인이 섞였을 수 있음)");
                if (s_dynProp.TryGetValue(kv.Key, out var pp))
                    Line($"    (참고) 공개 값 {pp.prop} {F(Val(P, "p:" + pp.prop))} → {F(Val(F2, "p:" + pp.prop))} (이 룬 몫 {F(kv.Value * pp.mul)})");
                if (kv.Key is "allDamage" or "attackPercent")
                    Line($"    (참고) 근접 공격력 {Val(P, "p:MeleeAttack")} → {Val(F2, "p:MeleeAttack")} (기대 ≈{ExpectedMelee(P, kv.Value)})");
                if (kv.Key == "maxHpPercent")
                    Line($"    (참고) 최대 체력 {Val(P, "p:MaxHp")} → {Val(F2, "p:MaxHp")}");
            }
        }
        if (hpForced) { c.Stats.SetHp(Mathf.Min(hpBefore, c.Stats.MaxHp)); c.Mgr.RefreshHpRatio(); }

        // 관리자 전체 사전 보정(주 피해 ②) — 일반 · 정예/보스
        if (c.Normal != null || c.Tough != null)
        {
            var P2 = TakeSnap(c);
            string Ratio(float a, float b) => a < 0f ? "—" : $"×{a:0.###}→×{b:0.###}";
            if (!Mathf.Approximately(B.PreNormal, P2.PreNormal) || !Mathf.Approximately(B.PreTough, P2.PreTough))
                Line($"    주 피해 사전 보정(전체 룬) 일반 {Ratio(B.PreNormal, P2.PreNormal)} · 정예/보스 {Ratio(B.PreTough, P2.PreTough)}");
        }
        if (P.Agents != B.Agents || P.Fields != B.Fields) Line($"    (참고) 놓자 생성물 {B.Agents}→{P.Agents} · 장판 {B.Fields}→{P.Fields}");

        // ── 빼기(진짜 경로) ──
        Call(c.Panel, "RemovePlacedItem", d);
        await Ui(250);
        if (c.Inv.IsPlaced(d.instanceId)) { Fail("진짜 경로로 빼기 실패"); return; }
        c.Placed.Remove(d);
        Discard(c, d);
        c.Added.Remove(d);
        await Ui(150);
        if (c.Stats.Hp != B.Hp) { Line($"    (참고) 뺀 뒤 체력 {B.Hp}→{c.Stats.Hp}(최대 체력 감소로 깎임) — 되돌림"); c.Stats.SetHp(Mathf.Min(B.Hp, c.Stats.MaxHp)); c.Mgr.RefreshHpRatio(); await Ui(60); }
        var R = TakeSnap(c);
        CompareRemoval(B, R, tierChanged);
    }

    /// <summary>뺀 뒤 = 놓기 전인가. 아이템 칸 · 효과 수 · 구독 · 생성물 · 정적 값은 엄격히, 시너지 칸은 존 단계가 잠깐 바뀌었으면 참고만.</summary>
    private static void CompareRemoval(Snap B, Snap R, bool tierChanged)
    {
        var strict = new List<string>(); var loose = new List<string>();
        foreach (var k in B.Stats.Keys.Union(R.Stats.Keys))
        {
            if (k == "p:Hp" || k == "f:<Hp>k__BackingField") continue;
            double a = Val(B, k), b = Val(R, k);
            if (Math.Abs(a - b) <= 1e-5) continue;
            bool item = k.StartsWith("f:_item") || k == "f:_maxHpItemContribution";
            (item || (!tierChanged && k.StartsWith("p:")) ? strict : loose).Add($"{k} {F(a)}→{F(b)}");
        }
        Check("  빼기 → 아이템 스탯 칸 · 공개 값 원상", strict.Count == 0, strict.Count == 0 ? "" : string.Join(" · ", strict));
        if (loose.Count > 0) Line($"    (참고) 다른 칸 차이(시너지 재활성 · 시간 흐름): {string.Join(" · ", loose.Take(12))}");
        Check("  빼기 → 효과 수 · 각인 · 정적 값 원상", B.Effects == R.Effects && B.Stages.SequenceEqual(R.Stages) && Near(B.KillBonus, R.KillBonus) && Near(B.NextBonus, R.NextBonus) && B.Mods == R.Mods,
              $"효과 {B.Effects}→{R.Effects} · 처치반경 {F(B.KillBonus)}→{F(R.KillBonus)} · 다음타 {F(B.NextBonus)}→{F(R.NextBonus)}{(B.Mods != R.Mods ? " · 공격 변형 " + B.Mods + "→" + R.Mods : "")}");
        var dels = B.Dels.Keys.Union(R.Dels.Keys).Where(k => (B.Dels.TryGetValue(k, out var x) ? x : 0) != (R.Dels.TryGetValue(k, out var y) ? y : 0))
                    .Select(k => $"{k} {(B.Dels.TryGetValue(k, out var x) ? x : 0)}→{(R.Dels.TryGetValue(k, out var y) ? y : 0)}").ToList();
        Check("  빼기 → 이벤트 구독 수 원상", dels.Count == 0, string.Join(" · ", dels));
        Check("  빼기 → 전설 생성물 · 전설 HUD 원상", B.Agents == R.Agents && B.Hud == R.Hud, $"생성물 {B.Agents}→{R.Agents} · HUD {B.Hud}→{R.Hud}");
        if (B.Fields != R.Fields) Warn($"  빼기 뒤 활성 장판 {B.Fields}→{R.Fields}(수명 동안 남는 장판일 수 있음)");
        if (B.FxTypes != R.FxTypes) Warn($"  빼기 뒤 시너지 단계 효과 목록이 다름: [{B.FxTypes}] → [{R.FxTypes}]");
    }

    // ═════════════════════════════════════════════════════════
    // 조건 강제 · 피해 보정 · 탄두
    // ═════════════════════════════════════════════════════════
    /// <summary>그 효과 인스턴스에만 조건 신호를 넣는다. 강제 못 하면 null.</summary>
    private static string Force(Ctx c, IItemEffect inst, ItemEffectSlot s, ref bool hpForced)
    {
        var ctx = c.ItemCtx;
        if (s.effectType == "CritDamageStack")
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(s.value2 > 0f ? s.value2 : 3f));
            for (int i = 0; i < n; i++) inst.OnPostDealDamage(ctx, Rep(c, crit: true));
            return $"치명 적중 {n}번";
        }
        if (s.effectType is "HurtGuardFrost" or "HurtGuardShadow") { inst.OnPostTakeDamage(ctx, new DamageReport { DamageDealt = 1f, Target = c.Player.gameObject }); return "피격 신호"; }
        if (s.effectType == "WardRampart") return "근접 무기 듦 " + MeleeHeld(c);
        if (s.effectType == "WardFinisherGuard") return null;
        switch (s.trigger)
        {
            case "Always": return "상시";
            case "AfterHit": inst.OnPostTakeDamage(ctx, new DamageReport { DamageDealt = 1f, Target = c.Player.gameObject }); return "피격 신호";
            case "AfterSkill": inst.OnSkillUse(ctx, SkillType.E); return "스킬 신호";
            case "AfterSkillChain": inst.OnSkillUse(ctx, SkillType.E); inst.OnSkillUse(ctx, SkillType.R); return "E → R 신호";
            case "AfterDash": inst.OnRollEnd(ctx); return "대시 끝 신호";
            case "AfterKill": inst.OnKill(ctx, c.Dummy); return "처치 신호";
            case "AfterCrit": inst.OnPostDealDamage(ctx, Rep(c, crit: true)); return "치명 적중 신호";
            case "AfterHeavyHit": inst.OnPostDealDamage(ctx, Rep(c, heavy: true)); return "강타 적중 신호";
            case "DuringBoss": inst.OnBossEnter(ctx); return "보스 진입 신호";
            case "SameTarget":
            case "Consecutive":
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(s.value2 > 0f ? s.value2 : 1f));
                for (int i = 0; i < n; i++) inst.OnPostDealDamage(ctx, Rep(c));
                return $"같은 적 {n}타";
            }
            case "HPBelow30":
            case "HPBelow40":
            case "HPBelow50":
                if (!hpForced)
                {
                    c.Stats.SetHp(Mathf.Max(1, Mathf.FloorToInt(c.Stats.MaxHp * 0.25f)));
                    c.Mgr.RefreshHpRatio();
                    hpForced = true;
                }
                return "체력 25%로 낮춤";
            default: return null;
        }
    }

    /// <summary>피해 보정 효과 — 그 인스턴스의 사전 보정을 일반 · 정예/보스 · 더미 대상으로 잰다.</summary>
    private static void DamageModCheck(Ctx c, IItemEffect inst, ItemEffectSlot s, float iv, float iv2)
    {
        var ctx = c.ItemCtx;
        float Pre(GameObject target)
        {
            var pkt = new DamagePacket(100f, c.Player.gameObject, target);
            inst.OnPreDealDamage(ctx, ref pkt);
            return pkt.FinalDamage / 100f;
        }
        switch (s.effectType)
        {
            case "TargetGradeBonus":
                if (c.Tough != null) Check($"    정예/보스({c.Tough.Grade}) 주 피해 배율", Near(Pre(c.Tough.gameObject), 1f + iv), $"×{Pre(c.Tough.gameObject):0.###} (기대 ×{1f + iv:0.###})");
                else Skip("    정예/보스 대상 없음 — 배율 실측 못 함");
                if (c.Normal != null) Check("    일반 적 주 피해 배율(오르면 안 됨)", Near(Pre(c.Normal.gameObject), 1f), $"×{Pre(c.Normal.gameObject):0.###}");
                {
                    // 10-31 고침 — 룬 시너지 · 격발 · 전설 · 독/화상 · 서약 피해(TakeSynergyDamage)에도 같은 배율
                    float gm = GameRunBootstrapper.Instance?.Run?.EffectManager?.GradeDamageMultiplier() ?? 1f;
                    Check("    정예/보스 시너지 · 격발 · 전설 · 도트 배율(TakeSynergyDamage)", Near(gm, 1f + iv), $"×{gm:0.###} (기대 ×{1f + iv:0.###})");
                    if (c.Tough != null && !c.Tough.IsDead)
                    {
                        int hp0 = c.Tough.CurrentHp;
                        c.Tough.TakeSynergyDamage(200f, c.Player.gameObject, 1f, false, DamageKind.Synergy);
                        int dealt = hp0 - c.Tough.CurrentHp;
                        Line($"    정예/보스에게 시너지 200(방어 무시) → 실제 {dealt}(받는 배율 · 특성 포함 — 기대 약 {200f * (1f + iv):0})");
                    }
                }
                break;
            case "FarHitBonus":
            {
                var t = c.Tough != null ? c.Tough : c.Normal;
                if (t == null) { Skip("    대상 없음 — 원거리 배율 못 잼"); break; }
                Vector3 dv = t.transform.position - c.Player.transform.position; dv.y = 0f;
                float far = iv2 > 0f ? iv2 : 5f;
                float exp = dv.magnitude >= far ? 1f + iv : 1f;
                Check($"    거리 {dv.magnitude:0.0}m 대상 배율", Near(Pre(t.gameObject), exp), $"×{Pre(t.gameObject):0.###} (기대 ×{exp:0.###})");
                break;
            }
            case "FirstAttackAfterRoll":
            {
                inst.OnRollEnd(ctx);
                float r = Pre(c.Dummy);
                ProcCoefficient.ConsumeHeavyNext();   // 이 측정이 예약한 강타를 지운다
                Check("    구르기 뒤 첫 타 배율", Near(r, 1f + iv), $"×{r:0.###} (기대 ×{1f + iv:0.###})");
                Check("    두 번째 타는 보정 없음", Near(Pre(c.Dummy), 1f), "");
                break;
            }
            case "MoveChargeNextHit":
            {
                SetField(inst, "_armed", true);   // 6 m 이동은 프로브가 못 하므로 무장만 대신 건다
                float r = Pre(c.Dummy);
                Check("    무장된 다음 일격 배율", Near(r, 1f + iv), $"×{r:0.###} (기대 ×{1f + iv:0.###})");
                break;
            }
            case "FrostOnHit":
                if (c.Normal != null) Line($"    (참고) 서리 2중첩 적 +{iv:P0} — 지금 일반 대상 서리 {c.Normal.Status.GetSlowStacks("frost")}중첩 · 배율 ×{Pre(c.Normal.gameObject):0.###}");
                break;
        }
    }

    /// <summary>격발 룬 — 실제 탄두 피해(공격력 × 값 × 존 단계 배율)와 속성 탄두 배율.</summary>
    private static void WarheadReport(Ctx c, IItemEffect inst, ItemEffectSlot s, float iv)
    {
        var m = typeof(TriggerBurstEffect).GetMethod("WarheadDamage", Inst);
        if (m == null) { Warn("    WarheadDamage를 못 찾음"); return; }
        float dmg = Convert.ToSingle(m.Invoke(inst, new object[] { c.ItemCtx }));
        int atk = c.Stats.GetEffectiveAttack(c.ItemCtx.WeaponType.GetAttackStatKind());
        string elem = Convert.ToString(GetField(inst, "_element"));
        string extra = elem switch
        {
            "Light"    => " × 빛 1.3(즉발)",
            "Grass"    => " × 숲 0.6 즉발 + 0.8 독",
            "Fire"     => " + 화상 0.4",
            "Electric" => " + 2명 연쇄 각 0.5",
            "Dark"     => " + 받는 피해 +12% 3초",
            "Ice"      => " + 서리",
            _          => "",
        };
        float ratio = atk > 0 ? dmg / atk : 0f;
        Line($"    격발 탄두: 공격력 {atk} × {ratio:0.###} = {dmg:0.#}{extra} · 속성 {elem} (설명 「공격력 {s.value * 100f:0.#}%」 · 인스턴스 값 {iv:0.###} · 존 단계 배율 {(iv > 0f ? ratio / iv : 0f):0.##})");
        if (!Near(ratio, iv) ) Warn($"    탄두 피해 비율 {ratio:0.###} ≠ 설명 값 {iv:0.###} — 존 단계 배율(×1.2/1.45/1.75)이 설명에 없다");
    }

    // ═════════════════════════════════════════════════════════
    // ② 시너지
    // ═════════════════════════════════════════════════════════
    private static async UniTask AuditSynergyAsync(Ctx c)
    {
        Line("\n════ ② 시너지 — 존 칸 수 → 단계 켜기 · 내리기 · 끄기 ════");
        var fx = c.Fx;
        var res = fx.Resources;
        var realCounts = new Dictionary<string, int>(c.Bridge.GetZoneOccupiedCounts().ToDictionary(k => k.Key, k => k.Value));
        Line($"지금 판 칸 수: {string.Join(" · ", realCounts.Select(k => $"{k.Key} {k.Value}"))} · 켜진 단계: [{FxTypes(fx)}] (끝나면 이 칸 수로 되돌린다 — 쌓인 스택은 사라진다)");
        SetField(c.Bridge, "_reapplying", true);   // 판 터짐 · 활성 알림을 내지 않는다
        ElementVfxPlayer.SuppressBursts = true;
        try
        {
            c.Bridge.OnZoneAmplifiersUpdated(new Dictionary<string, float>());   // 판 존핵 배수를 잠깐 걷는다 — 표 값과 바로 비교하려고(끝에 되돌림)
            await Zone(c, new Dictionary<string, int>());
            Check("모두 끔 → 단계 효과 0 · 공명 1", fx.Active.Count == 0 && Mathf.Approximately(fx.Amplifier, 1f), $"효과 {fx.Active.Count} · 공명 {fx.Amplifier}");
            CheckLayersZero(c, "모두 끔");

            foreach (var zone in Elems)
            {
                var entries = (Managers.RuneData?.GetZoneSynergies(zone) ?? new List<RuneSynergyEntry>()).OrderBy(e => e.threshold).ToList();
                if (entries.Count == 0) { Skip($"{zone} 시너지 행 없음"); continue; }
                Line($"\n▶ {zone} — 임계 {string.Join("/", entries.Select(e => e.threshold))}");
                for (int t = 1; t <= entries.Count; t++)
                {
                    await Zone(c, new Dictionary<string, int> { [zone] = entries[t - 1].threshold });
                    bool ok = true; var sb = new StringBuilder();
                    for (int i = 0; i < entries.Count; i++)
                    {
                        bool on = fx.IsActive(entries[i].effect_type);
                        if (on != (i < t)) { ok = false; sb.Append($" {entries[i].effect_type}={(on ? "켜짐" : "꺼짐")}"); }
                        if (on)
                        {
                            var ent = fx.GetActive(entries[i].effect_type)?.Entry;
                            if (ent == null || !Near(ent.value, entries[i].value) || !Near(ent.value2, entries[i].value2)) { ok = false; sb.Append($" {entries[i].effect_type} 값 {ent?.value}≠{entries[i].value}"); }
                        }
                    }
                    int tier = c.Bridge.GetZoneTier(zone);
                    Check($"  {t}단계(칸 {entries[t - 1].threshold}) {entries[t - 1].effect_type} — 하위 단계 함께 켜짐 · 상위 꺼짐 · 값 = 표", ok && tier == t, $"단계 {tier}{sb}");
                    Line($"    표: {entries[t - 1].description} (v={F(entries[t - 1].value)} v2={F(entries[t - 1].value2)} v3={F(entries[t - 1].value3)} dur={F(entries[t - 1].duration)} max={entries[t - 1].max_stack})");
                    if (t == 1) await TierOneStats(c, zone, entries[0]);
                }
                // 내리기: 4단계 → 1단계 칸 수 — 상위만 꺼지고 1단계는 남아야
                await Zone(c, new Dictionary<string, int> { [zone] = entries[0].threshold });
                bool down = fx.IsActive(entries[0].effect_type) && entries.Skip(1).All(e => !fx.IsActive(e.effect_type));
                Check($"  내리기(최상 → 1단계 칸 수) — 상위만 꺼짐", down, $"[{FxTypes(fx)}]");
                // 끄기
                await Zone(c, new Dictionary<string, int>());
                Check($"  끄기 — 단계 효과 0", fx.Active.Count == 0, $"[{FxTypes(fx)}]");
                CheckLayersZero(c, $"{zone} 끈 뒤");
            }

            // ── 속성 반응 ──
            Line("\n▶ 속성 반응(이웃 두 존이 함께 켜짐)");
            foreach (var def in RuneReactionDef.All)
            {
                var ea = Managers.RuneData.GetZoneSynergies(def.ZoneA).OrderBy(e => e.threshold).ToList();
                var eb = Managers.RuneData.GetZoneSynergies(def.ZoneB).OrderBy(e => e.threshold).ToList();
                string field = "_reaction" + def.Stat switch
                {
                    RuneReactionDef.StatKind.CritChance => "CritChance", RuneReactionDef.StatKind.CritDamage => "CritDamage",
                    RuneReactionDef.StatKind.AttackSpeed => "AttackSpeed", RuneReactionDef.StatKind.DamagePercent => "DamagePercent",
                    RuneReactionDef.StatKind.SkillCdr => "SkillCdr", _ => "DamageReduction",
                };
                await Zone(c, new Dictionary<string, int> { [def.ZoneA] = ea[0].threshold, [def.ZoneB] = eb[0].threshold });
                float v1 = F0(GetField(c.Stats, field));
                await Zone(c, new Dictionary<string, int> { [def.ZoneA] = ea[ea.Count - 1].threshold, [def.ZoneB] = eb[Mathf.Min(1, eb.Count - 1)].threshold });
                float v2 = F0(GetField(c.Stats, field));
                await Zone(c, new Dictionary<string, int> { [def.ZoneA] = ea[0].threshold });
                float v0 = F0(GetField(c.Stats, field));
                Check($"  {def.DisplayName}({def.ZoneA}+{def.ZoneB} · {def.Stat} 단계당 {def.ValuePerTier})", Near(v1, def.ValuePerTier) && Near(v2, 2f * def.ValuePerTier) && Near(v0, 0f),
                      $"1·1단계 {F(v1)} · 4·2단계 {F(v2)}(기대 {F(2f * def.ValuePerTier)}) · 한쪽 끔 {F(v0)}");
            }
            await Zone(c, new Dictionary<string, int>());

            // ── 중앙 공명 ──
            Line("\n▶ 중앙 공명");
            var center = Managers.RuneData.GetZoneSynergies(ElementDef.CenterId)?.OrderBy(e => e.threshold).ToList() ?? new List<RuneSynergyEntry>();
            var fire = Managers.RuneData.GetZoneSynergies("FIRE").OrderBy(e => e.threshold).ToList();
            foreach (var ce in center)
            {
                await Zone(c, new Dictionary<string, int> { ["FIRE"] = fire[0].threshold, [ElementDef.CenterId] = ce.threshold });
                var ent = fx.GetActive(fire[0].effect_type)?.Entry;
                Check($"  중앙 {ce.threshold}칸 → 공명 ×{1f + ce.value:0.##} · 잔불 값 증폭", Near(fx.Amplifier, 1f + ce.value) && ent != null && Near(ent.value, fire[0].value * (1f + ce.value)),
                      $"공명 {fx.Amplifier:0.###} · 잔불 {(ent != null ? F(ent.value) : "없음")}(기대 {F(fire[0].value * (1f + ce.value))})");
            }
            if (center.Count > 0)
            {
                var all = Elems.ToDictionary(z => z, z => Managers.RuneData.GetZoneSynergies(z).Max(e => e.threshold));
                all[ElementDef.CenterId] = center[center.Count - 1].threshold;
                await Zone(c, all);
                Line($"  전부 4단계 + 중앙 최대(공명 ×{fx.Amplifier:0.##}) — 지속 · 크기 값까지 증폭되는 단계:");
                foreach (var t in new[] { "ElecShock", "IceFreeze", "GrassMistPlus", "GrassMistInsight", "GrassMistDominion", "LightSanctuary", "DarkRelease" })
                {
                    var src = Elems.SelectMany(z => Managers.RuneData.GetZoneSynergies(z)).FirstOrDefault(e => e.effect_type == t);
                    var ent = fx.GetActive(t)?.Entry;
                    if (src != null && ent != null) Line($"    {t}: value {F(src.value)}→{F(ent.value)} · v2 {F(src.value2)}→{F(ent.value2)} · v3 {F(src.value3)}→{F(ent.value3)} · dur {F(src.duration)}→{F(ent.duration)}");
                }
                await Zone(c, new Dictionary<string, int>());
                Check("  중앙 끈 뒤 공명 1 · 효과 0", Mathf.Approximately(fx.Amplifier, 1f) && fx.Active.Count == 0, $"공명 {fx.Amplifier} · 효과 {fx.Active.Count}");
                CheckLayersZero(c, "전부 끈 뒤");
            }
        }
        finally
        {
            SetField(c.Bridge, "_reapplying", false);
            ElementVfxPlayer.SuppressBursts = false;
            RestoreBoardSynergy(c);
            await UniTask.DelayFrame(3);
            Line($"\n되돌림 — 판 칸 수 {string.Join(" · ", c.Bridge.GetZoneOccupiedCounts().Select(k => $"{k.Key} {k.Value}"))} · 켜진 단계 [{FxTypes(fx)}]");
        }
    }

    /// <summary>1단계 스탯 레이어 — 전기 공속 · 빛 치확 · 어둠 공격력/받피를 단계 효과에 직접 신호를 넣어 잰다.</summary>
    private static async UniTask TierOneStats(Ctx c, string zone, RuneSynergyEntry e)
    {
        var fx = c.Fx; var res = fx.Resources;
        var eff = fx.GetActive(e.effect_type);
        if (eff == null) return;
        var info = new HitInfo(c.Player.gameObject, c.Dummy, c.Dummy.transform.position, Vector3.forward, 10f, false, WeaponActionType.GroundLight);
        var crit = new HitInfo(c.Player.gameObject, c.Dummy, c.Dummy.transform.position, Vector3.forward, 10f, true, WeaponActionType.GroundLight);
        switch (zone)
        {
            case "ELECTRIC":
            {
                await UniTask.DelayFrame(2);
                float a0 = F0(GetField(c.Stats, "_synergyDynAttackSpeed"));
                for (int i = 0; i < 3; i++) eff.OnHit(in info, c.Player);
                await UniTask.DelayFrame(2);
                float a3 = F0(GetField(c.Stats, "_synergyDynAttackSpeed"));
                Check("    정전기 공속 레이어(기본 · 3스택)", Near(a0, e.value) && Near(a3, e.value + 3f * e.value2), $"{F(a0)} · {F(a3)} (기대 {F(e.value)} · {F(e.value + 3f * e.value2)}) · 스택 {res.Get("ElecStatic")}");
                break;
            }
            case "LIGHT":
            {
                float before = c.Stats.CritChanceBonus;
                for (int i = 0; i < 2; i++) eff.OnCrit(in crit, c.Player);
                await UniTask.DelayFrame(2);
                float layer = F0(GetField(c.Stats, "_synergyDynCritChance"));
                Check("    광채 2스택 → 치확 레이어", Near(layer, 2f * e.value), $"{F(layer)} (기대 {F(2f * e.value)}) · 공개 치확 {before:0.##}→{c.Stats.CritChanceBonus:0.##}%p");
                break;
            }
            case "DARK":
            {
                for (int i = 0; i < 5; i++) eff.OnHit(in info, c.Player);
                eff.OnDamaged(in info, c.Player);
                await UniTask.DelayFrame(2);
                int g = res.Get("darkGauge");
                float exp = Mathf.Clamp01(g / (float)(e.max_stack > 0 ? e.max_stack : 100)) * e.value;
                float layer = F0(GetField(c.Stats, "_synergyDynAttackPct"));
                Check("    잠식 게이지 → 공격력 레이어", g == 5 * (int)e.value3 + (int)e.value2 && Near(layer, exp), $"게이지 {g}(기대 {5 * (int)e.value3 + (int)e.value2}) · 공격력 {F(layer)} (기대 {F(exp)})");
                break;
            }
        }
    }

    private static void CheckLayersZero(Ctx c, string when)
    {
        var res = c.Fx.Resources;
        var names = new[] { "_synergyDynAttackSpeed", "_synergyDynAttackPct", "_synergyDynCritChance", "_synergyDynCritDamage", "_synergyDynDamageReduction",
                            "_reactionCritChance", "_reactionCritDamage", "_reactionAttackSpeed", "_reactionDamagePercent", "_reactionSkillCdr", "_reactionDamageReduction" };
        var left = names.Where(n => Math.Abs(F0(GetField(c.Stats, n))) > 1e-6f).Select(n => $"{n}={F(F0(GetField(c.Stats, n)))}").ToList();
        foreach (var k in new[] { "ElecStatic", "LightRadiance", "darkGauge" }) if (res.Get(k) != 0) left.Add($"자원 {k}={res.Get(k)}");
        int thr = (GetField(res, "_thresholds") as IDictionary)?.Count ?? 0;
        if (thr > 0) left.Add($"임계 훅 {thr}");
        if (GetField(res, "_floats") is IDictionary fl) foreach (DictionaryEntry kv in fl) if (kv.Value is float f && Math.Abs(f) > 1e-6f) left.Add($"float {kv.Key}={F(f)}");
        Check($"  {when} → 시너지 스탯 레이어 · 자원 · 임계 훅 0", left.Count == 0, string.Join(" · ", left));
    }

    private static async UniTask Zone(Ctx c, Dictionary<string, int> counts)
    {
        c.Bridge.OnZoneCellsUpdated(counts);
        await UniTask.DelayFrame(3);
    }

    private static void RestoreBoardSynergy(Ctx c)
    {
        if (c?.Bridge == null) return;
        try
        {
            SetField(c.Bridge, "_reapplying", false);
            var view = c.View != null ? c.View : UnityEngine.Object.FindFirstObjectByType<MerlinRuneHexGridView>(FindObjectsInactive.Include);
            if (view != null)
            {
                c.Bridge.OnZoneCellsUpdated(view.GetZoneOccupiedCounts());
                c.Bridge.OnZoneAmplifiersUpdated(view.GetZoneAmplifiers());
                return;
            }
            if (c.RealCounts != null) c.Bridge.OnZoneCellsUpdated(new Dictionary<string, int>(c.RealCounts));
            c.Bridge.OnZoneAmplifiersUpdated(c.RealAmps ?? new Dictionary<string, float>());
        }
        catch (Exception e) { Line("판 시너지 되돌림 예외: " + e.Message); }
    }

    // ═════════════════════════════════════════════════════════
    // 데이터 대조
    // ═════════════════════════════════════════════════════════
    private static void CompareItemData()
    {
        var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
        if (ta == null) { Warn($"{JsonPath} 없음 — 데이터 대조 건너뜀"); return; }
        var rows = JsonUtility.FromJson<ItemRows>(ta.text)?.items ?? new List<ItemRow>();
        var proj = rows.ToDictionary(r => $"{(string.IsNullOrEmpty(r.item_id) ? r.passive_id : r.item_id)}|{r.slot}|{r.effect_type}", r => r);
        var rt = new Dictionary<string, ItemEntry>();
        foreach (var kv in Managers.ItemData.GetAllItems())
            foreach (var e in kv.Value) rt[$"{e.ResolvedId}|{e.slot}|{e.effect_type}"] = e;
        var diffs = new List<string>();
        foreach (var k in proj.Keys.Union(rt.Keys))
        {
            proj.TryGetValue(k, out var p); rt.TryGetValue(k, out var r);
            if (p == null) { diffs.Add($"런타임에만 {k}"); continue; }
            if (r == null) { diffs.Add($"프로젝트 JSON에만 {k}"); continue; }
            if (p.trigger != r.trigger || !Near(p.value, r.value) || !Near(p.value2, r.value2) || !Near(p.value3, r.value3) || !Near(p.duration, r.duration) || p.max_stack != r.max_stack || p.description != r.description)
                diffs.Add($"{k}: 런타임 {r.trigger} {r.value}/{r.value2}/{r.value3} 「{r.description}」 ≠ 프로젝트 {p.trigger} {p.value}/{p.value2}/{p.value3} 「{p.description}」");
        }
        Check($"런타임 ITEM_DATA(CDN · 캐시) = 프로젝트 Resources/ITEM_DATA.json(바탕화면 원본과 같은 판) — 행 {rt.Count}/{proj.Count}", diffs.Count == 0, $"다른 행 {diffs.Count}개 — 런타임이 옛 판이면 CDN 업로드 필요");
        foreach (var x in diffs.Take(40)) Line("    " + x);
    }

    private static void CompareSynergyData()
    {
        var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(SynJsonPath);
        if (ta == null || Managers.RuneData == null) { Warn($"{SynJsonPath} 없음 — 시너지 대조 건너뜀"); return; }
        var rows = JsonUtility.FromJson<SynRows>(ta.text)?.synergies ?? new List<SynRow>();
        var diffs = new List<string>();
        foreach (var p in rows)
        {
            var r = Managers.RuneData.GetZoneSynergies(p.zone_id)?.FirstOrDefault(e => e.threshold == p.threshold);
            if (r == null) { diffs.Add($"런타임에 없음 {p.zone_id}/{p.threshold}"); continue; }
            if (r.effect_type != p.effect_type || !Near(r.value, p.value) || !Near(r.value2, p.value2) || !Near(r.value3, p.value3) || !Near(r.duration, p.duration) || r.max_stack != p.max_stack)
                diffs.Add($"{p.zone_id}/{p.threshold}: 런타임 {r.effect_type} {r.value}/{r.value2}/{r.value3} ≠ 프로젝트 {p.effect_type} {p.value}/{p.value2}/{p.value3}");
        }
        Check($"런타임 시너지 표 = ChartData/merlin_rune_synergy_data.json — 행 {rows.Count}", diffs.Count == 0, $"다른 행 {diffs.Count}개");
        foreach (var x in diffs.Take(30)) Line("    " + x);
    }

    /// <summary>설명 글에 데이터 수치가 보이는가(휴리스틱 — v · v×100 중 하나가 글의 숫자와 같으면 있음).</summary>
    private static void DescCheck(ItemEntry r)
    {
        if (r == null || string.IsNullOrEmpty(r.description) || r.ResolvedRarity == "Legendary") return;
        var nums = Regex.Matches(r.description, @"\d+(?:\.\d+)?").Cast<Match>().Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
        var miss = new List<string>();
        void Probe(string name, float v)
        {
            if (Mathf.Approximately(v, 0f)) return;
            if (r.effect_type == "PoisonOnHit" && name == "value" && Mathf.Approximately(v, 1f)) return;
            if (!nums.Any(n => Math.Abs(n - v) < 0.051 || Math.Abs(n - v * 100.0) < 0.051)) miss.Add($"{name}={v}");
        }
        Probe("value", r.value); Probe("value2", r.value2); Probe("value3", r.value3); Probe("duration", r.duration);
        if (miss.Count > 0) Warn($"  설명 「{r.description}」에 안 보이는 수치({r.effect_type}): {string.Join(", ", miss)}");
    }

    // ═════════════════════════════════════════════════════════
    // 찍기
    // ═════════════════════════════════════════════════════════
    private static Snap TakeSnap(Ctx c)
    {
        var s = new Snap();
        foreach (var f in typeof(PlayerRuntimeStats).GetFields(Inst))
        {
            object v = f.GetValue(c.Stats);
            switch (v)
            {
                case float x: s.Stats["f:" + f.Name] = x; break;
                case int x: s.Stats["f:" + f.Name] = x; break;
                case bool x: s.Stats["f:" + f.Name] = x ? 1 : 0; break;
                case ItemDynamicStats dyn:
                    object boxed = dyn;
                    foreach (var g in typeof(ItemDynamicStats).GetFields(BindingFlags.Instance | BindingFlags.Public))
                        s.Stats["f:" + f.Name + "." + g.Name] = Convert.ToDouble(g.GetValue(boxed));
                    break;
            }
        }
        foreach (var p in typeof(PlayerRuntimeStats).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            if (p.PropertyType != typeof(float) && p.PropertyType != typeof(int) && p.PropertyType != typeof(bool)) continue;
            try { s.Stats["p:" + p.Name] = Convert.ToDouble(p.GetValue(c.Stats)); } catch { }
        }
        s.Hp = c.Stats.Hp;
        CountDelegates(c.Player, typeof(PlayerController), "player.", s.Dels);
        var pd = GetField(c.Player, "_perfectDodge");
        if (pd != null) CountDelegates(pd, pd.GetType(), "perfectDodge.", s.Dels);
        CountDelegates(null, typeof(QuestEvents), "QuestEvents.", s.Dels);
        CountDelegates(null, typeof(BuildImprint), "BuildImprint.", s.Dels);
        CountDelegates(null, typeof(LegendaryHud), "LegendaryHud.", s.Dels);
        s.Effects = c.Mgr.ActiveEffects.Count;
        s.KillBonus = KillBurstRadiusEffect.Bonus;
        s.NextBonus = F0(GetField(c.Mgr, "_nextAttackBonus"));
        s.Mods = JsonUtility.ToJson(ItemCombatMods.Current);
        s.Agents = (GetField(LegendaryRuntime.Instance, "_agents") as IList)?.Cast<object>().Count(a => a != null && !a.Equals(null)) ?? 0;
        s.Hud = (typeof(LegendaryHud).GetField("s_values", Stat)?.GetValue(null) as IDictionary)?.Count ?? 0;
        s.Fields = UnityEngine.Object.FindObjectsByType<GroundFieldBase>(FindObjectsSortMode.None).Count(f => f.isActiveAndEnabled);
        foreach (var fam in BuildFamilyRules.All) s.Stages[(int)fam] = BuildImprint.Stage(fam);
        foreach (var e in Elems) s.Tiers[e] = c.Bridge != null ? c.Bridge.GetZoneTier(e) : 0;
        s.Amp = c.Fx != null ? c.Fx.Amplifier : 1f;
        s.FxTypes = c.Fx != null ? FxTypes(c.Fx) : "";
        s.PreNormal = c.Normal != null && !c.Normal.IsDead ? MgrPre(c, c.Normal.gameObject) : -1f;
        s.PreTough  = c.Tough  != null && !c.Tough.IsDead  ? MgrPre(c, c.Tough.gameObject)  : -1f;
        return s;
    }

    /// <summary>모든 활성 룬의 주 피해 사전 보정(ItemEffectManager.OnPreDealDamage — CombatDamage ②단계 그대로). 근접 다음타 버퍼는 건드리지 않게 비근접으로.</summary>
    private static float MgrPre(Ctx c, GameObject target)
    {
        var pkt = new DamagePacket(100f, c.Player.gameObject, target);
        c.Mgr.OnPreDealDamage(ref pkt, false);
        return pkt.FinalDamage / 100f;
    }

    private static void CountDelegates(object o, Type t, string prefix, Dictionary<string, int> into)
    {
        var flags = (o == null ? BindingFlags.Static : BindingFlags.Instance) | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var tt = t; tt != null && tt != typeof(object) && tt != typeof(MonoBehaviour); tt = tt.BaseType)
            foreach (var f in tt.GetFields(flags))
                if (typeof(Delegate).IsAssignableFrom(f.FieldType))
                    into[prefix + f.Name] = (f.GetValue(o) as Delegate)?.GetInvocationList().Length ?? 0;
    }

    // ═════════════════════════════════════════════════════════
    // 놓기 자리
    // ═════════════════════════════════════════════════════════
    /// <summary>모양이 맞는 빈 자리. 존 단계 · 중앙 공명을 바꾸지 않는 자리를 먼저 고른다(시너지가 섞이지 않게).</summary>
    private static GridSquare FindFit(Ctx c, RuntimeItemData item, out bool tierSafe)
    {
        tierSafe = false;
        var shape = Staging(c.Panel)?.GetShapeForItem(item);
        var view  = c.View;
        var gm    = GridManager.Instance;
        var resolve = typeof(GridManager).GetMethod("ResolveFootprint", Inst);
        if (shape == null || view?.HexGrid == null || gm == null || resolve == null) return null;
        var cur = c.Bridge != null ? c.Bridge.GetZoneOccupiedCounts().ToDictionary(k => k.Key, k => k.Value) : new Dictionary<string, int>();
        GridSquare any = null;
        foreach (var sq in view.HexGrid.GetGridSquares())
        {
            if (sq == null || sq.isOccupied) continue;
            var args = new object[] { shape, sq, false };
            var foot = resolve.Invoke(gm, args) as List<GridSquare>;
            if (!(bool)args[2] || foot == null) continue;
            any ??= sq;
            var add = new Dictionary<string, int>();
            foreach (var f in foot)
            {
                string z = ElementDef.CodeToId(f.zoneCode);
                if (z == null) continue;
                add.TryGetValue(z, out int n); add[z] = n + 1;
            }
            bool same = add.All(kv => TierOf(kv.Key, cur.TryGetValue(kv.Key, out var a) ? a : 0) == TierOf(kv.Key, (cur.TryGetValue(kv.Key, out var b) ? b : 0) + kv.Value));
            if (same) { tierSafe = true; return sq; }
        }
        return any;
    }

    private static int TierOf(string zone, int count)
        => Managers.RuneData?.GetZoneSynergies(zone)?.Count(e => e != null && count >= e.threshold) ?? 0;

    private static void PickTargets(Ctx c)
    {
        var all = UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None).Where(m => m != null && !m.IsDead).ToList();
        Vector3 p = c.Player.transform.position;
        all.Sort((a, b) => (a.transform.position - p).sqrMagnitude.CompareTo((b.transform.position - p).sqrMagnitude));
        c.Normal = all.FirstOrDefault(m => m.Grade == MonsterGrade.Common || m.Grade == MonsterGrade.Rare);
        c.Tough  = all.FirstOrDefault(m => m.Grade == MonsterGrade.Elite || m.Grade == MonsterGrade.Boss);
    }

    // ═════════════════════════════════════════════════════════
    // 도우미
    // ═════════════════════════════════════════════════════════
    private static float Mult(ItemEffectSlot s, out BuildFamily fam, out int stage)
    {
        fam = BuildFamilyRules.FromSlot(s.effectType, s.trigger);
        stage = BuildFamilyRules.IsScalable(s.effectType) ? BuildImprint.Stage(fam) : 0;
        return stage > 0 ? BuildImprint.EffectMultiplier(fam) : 1f;
    }

    /// <summary>이 슬롯으로 만든 효과 인스턴스 — Rebuild가 판 순서대로 만들고 방금 놓은 룬이 끝이라 맞는 것 중 마지막.</summary>
    private static IItemEffect FindInstance(ItemEffectManager mgr, ItemEffectSlot s)
    {
        IItemEffect last = null;
        foreach (var e in mgr.ActiveEffects)
        {
            if (!(e is ItemEffectBase)) continue;
            if (!(GetField(e, "_slot") is ItemEffectSlot es)) continue;
            if (ReferenceEquals(es, s) || (es.effectType == s.effectType && es.trigger == s.trigger && es.slot == s.slot && es.description == s.description)) last = e;
        }
        return last;
    }

    private static DamageReport Rep(Ctx c, bool crit = false, bool heavy = false) => new DamageReport
    {
        DamageDealt = 10f, Attacker = c.Player.gameObject, Target = c.Dummy, IsCrit = crit, IsHeavy = heavy,
        HitPosition = c.Dummy.transform.position, ActionType = WeaponActionType.GroundLight, ProcCoef = 1f,
    };

    private static bool MeleeHeld(Ctx c)
    {
        var w = c.Player.WeaponManager != null ? c.Player.WeaponManager.CurrentWeaponData : null;
        return w != null && w.weaponType.GetAttackStatKind() == AttackStatKind.Melee;
    }

    /// <summary>근접 공격력 기대값 — % 보너스는 dmgMul 안에서 더해지므로 (D + Δ) / D 배.</summary>
    private static int ExpectedMelee(Snap s, double add)
    {
        double D = 1 + Val(s, "f:_itemAllDamagePercent") + Val(s, "f:_synergyDynAttackPct") + Val(s, "f:_itemDyn.attackPercent") + Val(s, "f:_itemDyn.allDamage")
                 + Val(s, "f:_reactionDamagePercent") + Val(s, "p:AttackSpeedOverflowDamage") + Val(s, "p:SwingSpeedOverflowDamage");
        return (int)Math.Round(Val(s, "p:MeleeAttack") * (D + add) / D);
    }

    private static string FxTypes(RuneEffectDispatcher fx) => string.Join(",", fx.Active.Select(a => a.EffectType).OrderBy(x => x, StringComparer.Ordinal));
    private static string TierText(Snap s) => string.Join("", Elems.Select(e => $"{e[0]}{s.Tiers[e]}"));
    private static string Stages() => string.Join(" · ", BuildFamilyRules.All.Where(f => BuildImprint.Count(f) > 0).Select(f => $"{BuildFamilyRules.Label(f)} {BuildImprint.Count(f)}개/{BuildImprint.Stage(f)}단계")) is var t && t.Length > 0 ? t : "없음";
    private static int GradeOrder(string g) => g switch { "Common" => 0, "Rare" => 1, "Epic" => 2, "Legendary" => 3, _ => 4 };

    private static string NonZero(object boxedStruct)
    {
        var parts = new List<string>();
        foreach (var f in boxedStruct.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            object v = f.GetValue(boxedStruct);
            double d = v is bool b ? (b ? 1 : 0) : Convert.ToDouble(v);
            if (Math.Abs(d) > 1e-7) parts.Add($"{f.Name}={F(d)}");
        }
        return string.Join(" ", parts);
    }

    private static void Add(Dictionary<string, double> d, string k, double v) { d.TryGetValue(k, out var cur); d[k] = cur + v; }
    private static double Val(Snap s, string k) => s.Stats.TryGetValue(k, out var v) ? v : 0;
    private static bool Near(double a, double b, double eps = Eps) => Math.Abs(a - b) <= eps;
    private static float F0(object o) => o is float f ? f : o is int i ? i : o is double d ? (float)d : 0f;
    private static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    private static object GetField(object o, string name)
    {
        for (var t = o?.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField(name, Inst | BindingFlags.DeclaredOnly);
            if (f != null) return f.GetValue(o);
        }
        return null;
    }

    private static void SetField(object o, string name, object v)
    {
        for (var t = o?.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField(name, Inst | BindingFlags.DeclaredOnly);
            if (f != null) { f.SetValue(o, v); return; }
        }
    }

    private static StagingAreaView Staging(UI_GridPanel panel)
        => panel != null ? typeof(UI_GridPanel).GetField("_stagingArea", Inst)?.GetValue(panel) as StagingAreaView : null;

    private static void Call(UI_GridPanel panel, string method, params object[] args)
        => typeof(UI_GridPanel).GetMethod(method, Inst)?.Invoke(panel, args.Length == 0 ? null : args);

    private static void Discard(Ctx c, RuntimeItemData d)
    {
        if (d == null || c.Inv == null || !c.Inv.IsStaging(d.instanceId)) return;
        Staging(c.Panel)?.RemoveShapeForItem(d);
        c.Inv.DiscardFromStaging(d);
    }

    private static UniTask Ui(int ms) => UniTask.Delay(ms, ignoreTimeScale: true);

    private static void Line(string s) => _sb.AppendLine(s);
    private static void Pass(string s) { _pass++; _sb.AppendLine("✓ " + s); }
    private static void Fail(string s) { _fail++; _sb.AppendLine("✗ FAIL " + s); }
    private static void Warn(string s) { _warn++; _sb.AppendLine("△ WARN " + s); }
    private static void Skip(string s) { _skip++; _sb.AppendLine("- SKIP " + s); }
    private static void Check(string what, bool ok, string detail)
    {
        if (ok) Pass(what + (string.IsNullOrEmpty(detail) ? "" : " — " + detail));
        else Fail(what + (string.IsNullOrEmpty(detail) ? "" : " — " + detail));
    }
}
#endif
