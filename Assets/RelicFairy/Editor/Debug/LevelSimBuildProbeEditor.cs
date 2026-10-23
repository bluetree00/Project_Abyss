#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// [레벨디자인 시뮬 · 플레이 중] 성장 몬테카를로가 뽑은 「챕터별 대표 빌드」를 그대로 장착해 초당 피해를 잰다(10-15).
/// 빌드 목록 = 프로젝트 Temp/ldsim_builds.json(무기 · 진화 · 강화 · 형 단계/변주 · 증강 · 룬(속성) · 구역 칸 수 · 서약 문장).
/// 재는 법은 장비 형 실측 56 · 59(WeaponFormProbeEditor)와 같다 — 체력을 늘 채우는 슬라임, 단일 10초 + 무리 5마리(1.8 m 고리) 10초,
/// 평타 0.1초 입력 · E/R은 쿨이 돌 때마다. 유물은 끈다(정오 주기가 칸 순서대로 값을 흔든다 — 유물 몫은 따로 곱한다).
/// 결과: Temp/ldsim_build_dps.txt · Temp/ldsim_build_dps.json, 로그 「[LevelSimBuild] 끝」.
/// </summary>
public static class LevelSimBuildProbeEditor
{
    private const string AutoRoot  = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string BuildFile = "Temp/ldsim_builds.json";
    private const string OutTxt    = "Temp/ldsim_build_dps.txt";
    private const string OutJson   = "Temp/ldsim_build_dps.json";
    private const string SimDpsFile = "Temp/ldsim_simdps.txt";
    private const string TuningFile = "Temp/ldsim_tuning.txt";   // 「burst=0.5」 · 「syn=0.5」 — 재는 동안만 메모리 수치에 씌운다(차트 · 코드는 그대로)

    // 구역 시너지 중 「피해」를 내는 단계(공속 · 치명 · 피해 감소 같은 스탯 단계는 빼고)
    private static readonly HashSet<string> SynergyDamageTypes = new()
    {
        "FireEmber", "FireIgnite", "FireBlaze", "FireScorch", "IceShatter", "IceGlacier",
        "ElecDischarge", "ElecOverload", "GrassMist", "LightBurst", "DarkAfterimage",
    };
    private static readonly string[] Zones = { "FIRE", "ICE", "ELECTRIC", "GRASS", "LIGHT", "DARK" };
    private static float s_burstScale = 1f, s_synScale = 1f;
    private static float s_secs = MeasureSeconds;   // 「secs=30」 — 칸당 측정 길이(스킬 쿨이 창에 몇 번 드느냐로 흔들려 길게 잰다)
    private static bool  s_pack = true;             // 「pack=0」 — 무리 측정 건너뛰기
    private static int   s_castE, s_castR;          // 마지막 측정의 E · R 사용 횟수(준비 → 쿨 전환을 센다)
    private static readonly Dictionary<string, float> s_itemScale = new();   // 빌드별 룬 수치 배율(BuildSpec.itemScale)
    private static readonly Dictionary<string, float> s_synByType = new();   // 「syn.FireBlaze=0.75」 — 단계별 배율(없으면 syn)
    private const int    TankHp    = 50000;
    private const float  MeasureSeconds = 10f;
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string ProbeMouseName = "LevelSimProbeMouse";
    private const string StoneBuildFile = "Temp/ldsim_builds_stone.json";   // 10-22 A1b 성흔석 실측 빌드(ldsim_builds.json과 따로)
    private const string StoneOutTxt    = "Temp/ldsim_stone_dps.txt";
    private const float  StoneFront     = 1.4f;   // 가운데 석상 중심 → 플레이어(석상 반지름 0.9 + 0.5)
    private const float  SlimeAway      = 8f;     // 슬라임 자리는 석상에서 이만큼 바깥(광역이 석상을 안 치게)

    private static readonly Dictionary<string, string> WeaponPath = new()
    {
        ["nameless"]   = "Assets/RelicFairy/Weapon/Nameless/Data/T0_Nameless.asset",
        ["katana"]     = "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset",
        ["greatsword"] = "Assets/RelicFairy/Weapon/Greatsword/Data/T1_Greatsword.asset",
    };

    [Serializable] private class RuneSpec   { public string id; public string el; }
    [Serializable] private class ZoneSpec   { public string zone; public int cells; }
    [Serializable] private class ClauseSpec { public string id; public int tier; }
    [Serializable] private class BuildSpec
    {
        public string label, weapon, variants, augs, cause;
        public string itemScale;   // 「item_t2_crit_gale=0.4;…」 — 이 빌드에서만 그 룬 수치(value)에 곱한다
        public int enh, evo, form, causeTier;
        public List<RuneSpec> runes = new();
        public List<ZoneSpec> zones = new();
        public List<ClauseSpec> clauses = new();
    }
    [Serializable] private class BuildList { public List<BuildSpec> builds = new(); }
    [Serializable] private class Row
    {
        public string label;
        public float single, pack, atk, crit, atkSpeed;
        public float sBasic, sSkill, sRune, sDot, sCov, pBasic, pSkill, pRune, pDot, pCov;
        public int synergyTiers;
    }
    [Serializable] private class RowList { public List<Row> rows = new(); }

    private static bool   s_armed;
    private static Vector3 s_home;
    private static Mouse  s_probeMouse, s_realMouse;
    private static readonly List<HpTrack> s_tracks = new();
    private static MonsterBase s_keep;   // RefillAll이 숨기지 않는 몬스터(성흔석 실측의 기사)

    [MenuItem("RelicFairy/Debug/10-15 레벨디자인 시뮬 — 대표 빌드 장착 초당 피해 (테스트 허브, 플레이 중)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[LevelSimBuild] 플레이 모드에서만"); return; }
        var launcher = UnityEngine.Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Debug.LogWarning("[LevelSimBuild] 테스트 허브에서 시작한다"); return; }
        if (!File.Exists(BuildFile)) { Debug.LogWarning("[LevelSimBuild] 빌드 목록 없음: " + BuildFile); return; }
        // 유물 없음 — 장비 형 예산 실측과 같은 길(목록을 「없음」 하나로 바꾸면 허브가 유물 없이 런을 연다)
        typeof(TestHubLauncher).GetField("relics", Inst)?.SetValue(launcher, new RelicClassSO[] { null });
        s_armed = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    /// <summary>
    /// 피해 모사(런 구조 자동 실측)를 챕터별 DPS로 시작한다 — 값은 Temp/ldsim_simdps.txt 한 줄(「225,2035,3240,4930」 · 앞에 「ch4|」면 Ch4부터).
    /// 대표 빌드 실측값 × 유물 × 가동률을 넣는다(에디터 코드 실행 경로가 막혀 EditorPrefs를 여기서 쓴다).
    /// </summary>
    [MenuItem("RelicFairy/Debug/10-15 레벨디자인 시뮬 — 피해 모사 시작 (Temp/ldsim_simdps.txt · 테스트 허브, 플레이 중)")]
    private static void BeginDamageSim()
    {
        string txt = File.Exists(SimDpsFile) ? File.ReadAllText(SimDpsFile).Trim() : "";
        if (txt == "clear")   // 다 쓰고 원래대로(피해 모사 기본 80) — 다른 세션의 피해 모사에 남지 않게
        {
            EditorPrefs.DeleteKey(RunStructureAutoPlayEditor.SimDpsByChapterPrefsKey);
            Debug.Log("[LevelSimBuild] 피해 모사 DPS 설정 지움");
            return;
        }
        if (!Application.isPlaying) { Debug.LogWarning("[LevelSimBuild] 플레이 모드에서만"); return; }
        bool ch4 = txt.StartsWith("ch4|");
        if (ch4) txt = txt.Substring(4);
        if (string.IsNullOrEmpty(txt)) { Debug.LogWarning("[LevelSimBuild] DPS 값 없음: " + SimDpsFile); return; }
        EditorPrefs.SetString(RunStructureAutoPlayEditor.SimDpsByChapterPrefsKey, txt);
        Debug.Log($"[LevelSimBuild] 피해 모사 DPS = {txt} · {(ch4 ? "Ch4부터" : "Ch1부터")}");
        EditorApplication.ExecuteMenuItem(AutoRoot + (ch4
            ? "시작 — 피해 모사 Ch4부터 (DPS = EditorPrefs RunAuto.SimDpsByChapter · 없으면 RunAuto.SimDps 80)"
            : "시작 — 피해 모사 Ch1부터 (DPS = EditorPrefs RunAuto.SimDpsByChapter · 없으면 RunAuto.SimDps 80)"));
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (!s_armed || !msg.StartsWith("[RunAuto]") || !msg.Contains("방 진입")) return;
        s_armed = false;
        Application.logMessageReceived -= OnLog;
        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
            var p = GameRunBootstrapper.Instance?.Run?.Player;
            if (p == null) { Debug.LogWarning("[LevelSimBuild] 런 플레이어가 없다"); return; }
            RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("레벨디자인 시뮬 — 대표 빌드 장착 초당 피해(유물 없음 · 단일 10초 + 무리 5마리 10초 · E/R 쿨마다)\n");
        var outRows = new RowList();
        var run = GameRunBootstrapper.Instance.Run;
        var placed = typeof(RunItemInventory).GetField("_placedItems", Inst)?.GetValue(run.ItemInventory) as List<RuntimeItemData>;
        var savedPlaced = placed != null ? new List<RuntimeItemData>(placed) : null;
        string savedSentence = run.CovenantHandler?.Sentence?.CovenantId;
        var savedSyn = new List<(RuneSynergyEntry e, float v)>();
        try
        {
            if (placed == null) { sb.AppendLine("룬 판 목록을 못 읽음"); return; }
            ReadTuning();
            // 지금 런타임이 쓰는 차트 값(CDN → 캐시) — 업로드가 반영됐는지 본다(덮어쓰기 전)
            float LiveSyn(string zone, string type) => Managers.RuneData?.GetZoneSynergies(zone)?.Find(e => e.effect_type == type)?.value ?? -1f;
            var heavy = Managers.ItemData?.GetItem("item_t2_heavy_weight")?.Find(e => e.effect_type == "TriggerBurst");
            sb.AppendLine($"런타임 차트 값 — 강타 폭발 룬 {(heavy != null ? heavy.value : -1f):0.###} · 불씨 {LiveSyn("FIRE", "FireEmber"):0.###} · 과부하 {LiveSyn("ELECTRIC", "ElecOverload"):0.###} · 빙하 {LiveSyn("ICE", "IceGlacier"):0.###}");
            foreach (var z in Zones)
            {
                var entries = Managers.RuneData?.GetZoneSynergies(z);
                if (entries == null) continue;
                foreach (var e in entries)
                {
                    if (!SynergyDamageTypes.Contains(e.effect_type)) continue;
                    float k = s_synByType.TryGetValue(e.effect_type, out float t) ? t : s_synScale;
                    if (Mathf.Approximately(k, 1f)) continue;
                    savedSyn.Add((e, e.value));
                    e.value *= k;
                }
            }
            var byType = new StringBuilder();
            foreach (var kv in s_synByType) byType.Append($" · {kv.Key} ×{kv.Value:0.##}");
            sb.AppendLine($"조정 덮어쓰기 — 타격 발동 ×{s_burstScale:0.##} · 시너지 피해 ×{s_synScale:0.##}{byType}(단계 {savedSyn.Count}개)");
            var list = JsonUtility.FromJson<BuildList>(File.ReadAllText(BuildFile));
            TakeMouse();
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            HideAllMonsters();
            await Wait(5f, ct);   // 방 입장 입력 잠금

            // 첫 장착 무기는 평타 0회로 잰다(장비 형 R2 예열 함정) — 무형검으로 2초 휘둘러 둔다
            if (await EquipAsync(p, WeaponPath["nameless"], ct))
            {
                s_home = p.transform.position;
                float w0 = Time.time;
                while (Time.time - w0 < 2f) { p.InputBuffer.Push(Command.Light); await Wait(0.1f, ct); }
            }

            foreach (var b in list.builds)
            {
                var row = await MeasureBuildAsync(p, run, placed, b, sb, ct);
                if (row != null) outRows.rows.Add(row);
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); Debug.LogException(e); }
        finally
        {
            foreach (var (e, v) in savedSyn) e.value = v;
            s_burstScale = s_synScale = 1f;
            s_synByType.Clear();
            if (placed != null && savedPlaced != null) { placed.Clear(); placed.AddRange(savedPlaced); }
            MerlinRuneBridge.Instance?.OnZoneCellsUpdated(new Dictionary<string, int>());
            if (!string.IsNullOrEmpty(savedSentence)) run.CovenantHandler?.TryWriteSentence(savedSentence);
            run.EffectManager?.Rebuild();
            DetachAll();
            ReleaseMouse();
            File.WriteAllText(OutTxt, sb.ToString());
            File.WriteAllText(OutJson, JsonUtility.ToJson(outRows, true));
            Debug.Log("[LevelSimBuild] 끝 → " + OutTxt + "\n" + sb);
        }
    }

    private static async UniTask<Row> MeasureBuildAsync(PlayerController p, GameRunSession run, List<RuntimeItemData> placed, BuildSpec b, StringBuilder sb, CancellationToken ct,
                                                         bool keepMonsters = false, bool equipOnly = false)
    {
        Managers.UI?.CloseAllPopupUI();
        Time.timeScale = 1f;
        DetachAll();
        if (!keepMonsters) HideAllMonsters();
        if (!WeaponPath.TryGetValue(b.weapon ?? "", out var path) || !await EquipAsync(p, path, ct)) { sb.AppendLine($"장착 실패 {b.label}"); return null; }
        var w = p.WeaponManager.Weapon0Data;
        typeof(PlayerWeaponManager).GetMethod("ApplyServerOverrideIfAvailable", BindingFlags.Static | BindingFlags.NonPublic)
            ?.Invoke(null, new object[] { w, Path.GetFileNameWithoutExtension(path) });
        w.evolutionStage = b.evo;
        w.enhanceLevel = b.enh;
        w.highestEnhanceReached = b.enh;
        w.formTier = b.form;
        w.formVariants = b.variants ?? "";
        w.augments = b.augs ?? "";
        w.RecomputeEnhancedStats();
        WeaponFormRuntime.Of(p).Rebuild();
        p.WeaponManager.RaiseEquippedWeaponRefreshed();

        placed.Clear();
        s_itemScale.Clear();
        if (!string.IsNullOrEmpty(b.itemScale))
            foreach (var part in b.itemScale.Split(';'))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && float.TryParse(kv[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float k))
                    s_itemScale[kv[0].Trim()] = k;
            }
        foreach (var r in b.runes)
        {
            var d = ProjectRune(r.id);
            if (d == null) { sb.AppendLine($"   룬 없음 {r.id}"); continue; }
            if (string.IsNullOrEmpty(d.element)) d.element = r.el;
            placed.Add(d);
        }
        var clauses = new List<(string, CovenantTier, ClauseLink)>();
        foreach (var c in b.clauses) clauses.Add((c.id, (CovenantTier)c.tier, ClauseLink.Immediate));
        bool wrote = clauses.Count > 0 && run.CovenantHandler != null
                     && run.CovenantHandler.TryWriteSentence(CovenantSentence.MakeId(b.cause, (CovenantTier)b.causeTier, clauses));
        run.EffectManager.Rebuild();
        var bridge = MerlinRuneBridge.Instance;
        bridge?.OnZoneCellsUpdated(new Dictionary<string, int>());   // 앞 빌드 단계를 먼저 내린다
        var counts = new Dictionary<string, int>();
        foreach (var z in b.zones) counts[z.zone] = z.cells;
        bridge?.OnZoneCellsUpdated(counts);
        await Wait(0.5f, ct);
        if (equipOnly) return new Row { label = b.label };   // 입히기만(성흔석 실측)

        s_home = p.transform.position;
        Vector3 fw = Flat(p.transform.forward);
        Vector3 c0 = p.transform.position + fw * 1.7f;
        var one = await TrackAsync(c0, ct);
        if (one == null) { sb.AppendLine($"슬라임 부르기 실패 {b.label}"); return null; }
        var (single, sSrc) = await MeasureAsync(p, run, new List<HpTrack> { one }, one.M.transform.position, ct);
        one.M.gameObject.SetActive(false);
        DetachAll();
        var grp = new List<HpTrack>();
        var center = await TrackAsync(c0, ct);
        if (center != null) grp.Add(center);
        for (int k = 0; k < 4; k++)
        {
            float a = k * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
            var t = await TrackAsync(c0 + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.8f, ct);
            if (t != null) grp.Add(t);
        }
        int castE = s_castE, castR = s_castR;
        var (pack, pSrc) = s_pack ? await MeasureAsync(p, run, grp, c0, ct) : (0f, new float[RunStats.SourceCount]);
        foreach (var t in grp) if (t.M != null) t.M.gameObject.SetActive(false);

        var rs = p.RuntimeStats;
        var row = new Row
        {
            label = b.label, single = single, pack = pack,
            atk = rs.GetEffectiveAttack(AttackStatKind.Melee), crit = rs.CritChanceBonus, atkSpeed = rs.AttackSpeedMultiplier,
            sBasic = sSrc[0], sSkill = sSrc[1], sCov = sSrc[3], sRune = sSrc[4], sDot = sSrc[5],
            pBasic = pSrc[0], pSkill = pSrc[1], pCov = pSrc[3], pRune = pSrc[4], pDot = pSrc[5],
            synergyTiers = bridge != null ? bridge.ActiveSynergyCount : -1,
        };
        sb.AppendLine($"{b.label}{(string.IsNullOrEmpty(b.itemScale) ? "" : " [" + b.itemScale + "]")}: 단일 {single:0}/초 · 무리 {pack:0}/초 · 공격력 {row.atk:0} · 공속 ×{row.atkSpeed:0.00} · 시너지 단계 {row.synergyTiers} · 룬 {placed.Count} · 서약 {(wrote ? "씀" : "없음")}");
        sb.AppendLine($"   단일 몫 — 평타 {sSrc[0]:0} · 스킬 {sSrc[1]:0} · 룬 {sSrc[4]:0} · 지속 {sSrc[5]:0} · 서약 {sSrc[3]:0} · E {castE}번 · R {castR}번({s_secs:0}초)");
        sb.AppendLine($"   무리 몫 — 평타 {pSrc[0]:0} · 스킬 {pSrc[1]:0} · 룬 {pSrc[4]:0} · 지속 {pSrc[5]:0} · 서약 {pSrc[3]:0}");
        DetachAll();
        return row;
    }

    /// <summary>
    /// 10-22 A1b — 「챕터3 보스에게 근접 타격의 실제 피해가 제대로 안 들어간다」. 같은 빌드로 슬라임 단일 ↔ 공명 성흔석 너머 기사.
    /// 기사 보스방(전투가 열리고 성흔석이 선 뒤)에서. 기사는 숨기지 않고 체력만 0.5초마다 최대로 되돌린다(봉인 문턱 · 특수 상태를 안 밟게).
    /// </summary>
    [MenuItem("RelicFairy/Debug/10-22 대표 빌드 — 성흔석 너머 기사 초당 피해 (Ch3 보스방 · 플레이 중)")]
    private static void BeginStone()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[LevelSimBuild] 플레이 모드에서만"); return; }
        var p  = GameRunBootstrapper.Instance?.Run?.Player;
        var dk = UnityEngine.Object.FindFirstObjectByType<DeathKnightBossMonster>();
        var stones = UnityEngine.Object.FindObjectsByType<DKResonanceStone>(FindObjectsSortMode.None);
        if (p == null || dk == null || stones.Length == 0) { Debug.LogWarning("[LevelSimBuild] 기사 · 성흔석 · 런 플레이어가 있어야 한다(Ch3 보스방 전투 시작 뒤)"); return; }
        if (!File.Exists(StoneBuildFile)) { Debug.LogWarning("[LevelSimBuild] 빌드 목록 없음: " + StoneBuildFile); return; }
        RunStoneAsync(p, dk, stones, p.GetCancellationTokenOnDestroy()).Forget();
    }

    private static async UniTaskVoid RunStoneAsync(PlayerController p, DeathKnightBossMonster dk, DKResonanceStone[] stones, CancellationToken ct)
    {
        var sb = new StringBuilder($"10-22 A1b — 같은 빌드: 슬라임 단일 ↔ 공명 성흔석 너머 기사(가운데 석상 앞 {StoneFront} m · 평타 0.1초 · E/R 쿨마다 · 각 {MeasureSeconds}초 · 유물 그대로)\n");
        var run = GameRunBootstrapper.Instance.Run;
        var placed = typeof(RunItemInventory).GetField("_placedItems", Inst)?.GetValue(run.ItemInventory) as List<RuntimeItemData>;
        var savedPlaced = placed != null ? new List<RuntimeItemData>(placed) : null;
        string savedSentence = run.CovenantHandler?.Sentence?.CovenantId;
        var invField = typeof(PlayerController).GetField("debugInvincible", Inst);
        object savedInv = invField?.GetValue(p);
        s_keep = dk;
        try
        {
            if (placed == null) { sb.AppendLine("룬 판 목록을 못 읽음"); return; }
            s_burstScale = s_synScale = 1f; s_synByType.Clear(); s_secs = MeasureSeconds; s_pack = false;   // 조정 파일은 안 읽는다(차트 값 그대로)
            var list = JsonUtility.FromJson<BuildList>(File.ReadAllText(StoneBuildFile));
            TakeMouse();
            invField?.SetValue(p, true);

            // 가운데 석상 = 기사 → 석상 방향이 나머지 둘의 평균 방향과 가장 가까운 것
            Vector3 avg = Vector3.zero;
            foreach (var s in stones) avg += Flat(s.transform.position - dk.transform.position);
            DKResonanceStone mid = stones[0];
            float best = -2f;
            foreach (var s in stones)
            {
                float d = Vector3.Dot(Flat(s.transform.position - dk.transform.position), Flat(avg));
                if (d > best) { best = d; mid = s; }
            }
            Vector3 outward   = Flat(mid.transform.position - dk.transform.position);
            Vector3 stoneHome = mid.transform.position + outward * StoneFront; stoneHome.y = p.transform.position.y;
            Vector3 slimeHome = stoneHome + outward * SlimeAway;
            sb.AppendLine($"기사 {dk.transform.position} · 가운데 석상 {mid.transform.position} · 석상 {stones.Length}개 · 기사 최대 체력 {dk.EffectiveMaxHp}");

            foreach (var b in list.builds)
            {
                if (await MeasureBuildAsync(p, run, placed, b, sb, ct, keepMonsters: true, equipOnly: true) == null) continue;

                // ① 슬라임 단일(석상에서 떨어진 자리)
                Place(p, slimeHome);
                var one = await TrackAsync(slimeHome + outward * 1.7f, ct);
                float single = 0f;
                if (one != null)
                {
                    (single, _) = await MeasureAsync(p, run, new List<HpTrack> { one }, one.M.transform.position, ct);
                    one.M.gameObject.SetActive(false);
                }
                DetachAll();

                // ② 가운데 성흔석 → 기사
                var (viaStone, coolFrac, stoneBy) = await MeasureDkAsync(p, run, dk, stoneHome, mid.transform.position, mid, ct);
                int castE = s_castE, castR = s_castR;

                // ③ 기사 직접(기준) — 유리 결계 콜라이더를 잠시 끄고 기사 앞 1.7 m에서 친다. 기사 방어 · 무적 구간 몫을 성흔석 몫과 가른다.
                var glass = new List<Collider>();
                foreach (var sbar in UnityEngine.Object.FindObjectsByType<SoftBarrier>(FindObjectsSortMode.None))
                    foreach (var c in sbar.GetComponentsInChildren<Collider>())
                        if (c.enabled) { c.enabled = false; glass.Add(c); }
                float direct;
                float[] directBy;
                try
                {
                    Vector3 dkHome = dk.transform.position + outward * 1.7f;
                    (direct, _, directBy) = await MeasureDkAsync(p, run, dk, dkHome, dk.transform.position, null, ct);
                }
                finally { foreach (var c in glass) if (c != null) c.enabled = true; }
                sb.AppendLine($"{b.label}: 슬라임 단일 {single:0}/초 · 기사 직접 {direct:0}/초 · 성흔석 너머 기사 {viaStone:0}/초(직접의 {(direct > 0f ? viaStone / direct : 0f):P0} · 슬라임의 {(single > 0f ? viaStone / single : 0f):P0}) · 가운데 석상 식음 {coolFrac:P0} · E {castE}번 · R {castR}번 · 결계 콜라이더 {glass.Count}개 끔");
                sb.AppendLine($"   직접 몫 — 평타 {directBy[0]:0} · 스킬 {directBy[1]:0} · 룬 {directBy[4]:0} · 지속 {directBy[5]:0} · 서약 {directBy[3]:0}");
                sb.AppendLine($"   성흔석 몫 — 평타 {stoneBy[0]:0} · 스킬 {stoneBy[1]:0} · 룬 {stoneBy[4]:0} · 지속 {stoneBy[5]:0} · 서약 {stoneBy[3]:0}");
            }
        }
        catch (OperationCanceledException oce) { sb.AppendLine("취소됨 — " + string.Join(" | ", (oce.StackTrace ?? "").Split('\n'), 0, Math.Min(6, (oce.StackTrace ?? "").Split('\n').Length))); }
        catch (Exception e) { sb.AppendLine("예외: " + e); Debug.LogException(e); }
        finally
        {
            s_keep = null;
            s_pack = true;
            if (placed != null && savedPlaced != null) { placed.Clear(); placed.AddRange(savedPlaced); }
            MerlinRuneBridge.Instance?.OnZoneCellsUpdated(new Dictionary<string, int>());
            if (!string.IsNullOrEmpty(savedSentence)) run.CovenantHandler?.TryWriteSentence(savedSentence);
            run.EffectManager?.Rebuild();
            if (invField != null && p != null) invField.SetValue(p, savedInv);
            DetachAll();
            ReleaseMouse();
            File.WriteAllText(StoneOutTxt, sb.ToString());
            Debug.Log("[LevelSimBuild] 성흔석 끝 → " + StoneOutTxt + "\n" + sb);
        }
    }

    /// <summary>기사 체력 감소로 잰 초당 피해(0.1초마다 95% 아래면 최대로 되돌림) · <paramref name="watch"/> 석상 식음 비율.</summary>
    private static async UniTask<(float dps, float cool, float[] by)> MeasureDkAsync(PlayerController p, GameRunSession run, DeathKnightBossMonster dk,
                                                                        Vector3 home, Vector3 aim, DKResonanceStone watch, CancellationToken ct)
    {
        Place(p, home);
        float dealt = 0f;
        int last = dk.CurrentHp;
        void OnDk(int cur, int max) { if (cur < last) dealt += last - cur; last = cur; }
        dk.OnHPChanged += OnDk;
        int cool = 0, samples = 0;
        using var stop = new CancellationTokenSource();
        async UniTaskVoid WatchAsync()
        {
            while (!stop.IsCancellationRequested && dk != null)
            {
                samples++;
                if (watch != null && watch.IsCooling) cool++;
                if (dk.CurrentHp < dk.EffectiveMaxHp * 0.95f) { SetHp(dk, dk.EffectiveMaxHp); last = dk.EffectiveMaxHp; }
                await UniTask.Delay(100, DelayType.Realtime);
            }
        }
        WatchAsync().Forget();
        float t0 = Time.time;
        float[] by;
        try { (_, by) = await MeasureAsync(p, run, new List<HpTrack>(), aim, ct); }
        finally { stop.Cancel(); dk.OnHPChanged -= OnDk; }
        return (dealt / Mathf.Max(0.01f, Time.time - t0), samples > 0 ? (float)cool / samples : 0f, by);
    }

    private static void Place(PlayerController p, Vector3 at)
    {
        s_home = at;
        if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = at; rb.linearVelocity = Vector3.zero; }
        p.transform.position = at;
    }

    private static async UniTask<(float dps, float[] bySource)> MeasureAsync(PlayerController p, GameRunSession run, List<HpTrack> ts, Vector3 aim, CancellationToken ct)
    {
        float w0 = Time.time;
        while (Time.time - w0 < 2f) { RefillAll(); Steer(p, aim); p.InputBuffer.Push(Command.Light); await Wait(0.1f, ct); }
        p.CooldownTracker.ResetCooldown(SkillType.E);
        p.CooldownTracker.ResetCooldown(SkillType.R);
        foreach (var t in ts) t.Dealt = 0f;
        var st = run.RunStats;
        var src0 = new long[RunStats.SourceCount];
        for (int k = 0; k < RunStats.SourceCount; k++) src0[k] = st.DamageOf((DamageSource)k);
        float t0 = Time.time;
        s_castE = s_castR = 0;
        bool readyE = true, readyR = true;
        while (Time.time - t0 < s_secs)
        {
            bool nowE = p.CooldownTracker.IsReady(SkillType.E), nowR = p.CooldownTracker.IsReady(SkillType.R);
            if (readyE && !nowE) s_castE++;
            if (readyR && !nowR) s_castR++;
            readyE = nowE; readyR = nowR;
            RefillAll();
            if (!p.IsInSkillAction)   // 돌진 스킬이 표적을 지나가게 — 스킬 중엔 제자리로 끌지 않는다
            {
                Steer(p, aim);
                if (p.CooldownTracker.IsReady(SkillType.E)) p.InputBuffer.Push(Command.ESkill);
                else if (p.CooldownTracker.IsReady(SkillType.R)) p.InputBuffer.Push(Command.RSkill);
                else p.InputBuffer.Push(Command.Light);
            }
            await Wait(0.1f, ct);
        }
        float secs = Mathf.Max(0.01f, Time.time - t0);
        float sum = 0f;
        foreach (var t in ts) sum += t.Dealt;
        var by = new float[RunStats.SourceCount];
        for (int k = 0; k < RunStats.SourceCount; k++) by[k] = (st.DamageOf((DamageSource)k) - src0[k]) / secs;
        return (sum / secs, by);
    }

    // ── 표적 ──────────────────────────────────────────────────
    private sealed class HpTrack
    {
        public MonsterBase M;
        public float Dealt;
        private int _last;
        private readonly Vector3 _pin;
        public HpTrack(MonsterBase m) { M = m; _pin = m.transform.position; M.OnHPChanged += OnChanged; Refill(); }
        private void OnChanged(int cur, int max)
        {
            if (cur == max) { _last = cur; return; }
            if (cur < _last) Dealt += _last - cur;
            _last = cur;
        }
        public void Refill()
        {
            if (M == null || M.IsDead) return;
            SetHp(M, TankHp);
            _last = TankHp;
            if ((M.transform.position - _pin).sqrMagnitude > 0.04f)
            {
                if (M.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var ag) && ag.isActiveAndEnabled && ag.isOnNavMesh) ag.Warp(_pin);
                else M.transform.position = _pin;
            }
        }
        public void Detach() { if (M != null) M.OnHPChanged -= OnChanged; }
    }

    private static async UniTask<HpTrack> TrackAsync(Vector3 at, CancellationToken ct)
    {
        var m = await Managers.ObjectPooler.SpawnAsync<MonsterBase>("Slime/Slime", ObjectPoolerManager.PoolType.Monster, at, Quaternion.identity);
        if (m == null) return null;
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 6f && m.IsDamageImmuneNow) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        m.Status.ApplyCc("probe_hold", 9999f);
        var t = new HpTrack(m);
        s_tracks.Add(t);
        return t;
    }

    private static void SetHp(MonsterBase m, int hp)
    {
        var rt = typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(m);
        var f  = rt?.GetType().GetField("CurrentHp", Inst);
        if (f != null) f.SetValue(rt, Convert.ChangeType(hp, f.FieldType));
    }

    private static void RefillAll()
    {
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead && m != s_keep && !s_tracks.Exists(t => t.M == m)) m.gameObject.SetActive(false);
        foreach (var t in s_tracks) t.Refill();
    }

    private static void HideAllMonsters()
    {
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
    }

    private static void DetachAll()
    {
        foreach (var t in s_tracks) t.Detach();
        s_tracks.Clear();
    }

    private static RuntimeItemData ProjectRune(string id)
    {
        var ta  = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/RelicFairy/Resources/ITEM_DATA.json");
        var col = ta != null ? JsonUtility.FromJson<ItemEntryCollection>(ta.text) : null;
        if (col?.items == null) return null;
        var rows = new List<ItemEntry>();
        foreach (var e in col.items)
        {
            if (e == null) continue;
            if (string.IsNullOrEmpty(e.item_id) && !string.IsNullOrEmpty(e.passive_id)) e.item_id = e.passive_id;
            if (e.ResolvedId != id) continue;
            if (e.effect_type == "TriggerBurst") e.value *= s_burstScale;   // 매번 새로 읽은 행이라 원본에 남지 않는다
            if (s_itemScale.TryGetValue(id, out float ik)) e.value *= ik;
            rows.Add(e);
        }
        return rows.Count > 0 ? RuntimeItemData.FromServer(rows) : null;
    }

    private static void ReadTuning()
    {
        s_burstScale = s_synScale = 1f;
        s_synByType.Clear();
        s_secs = MeasureSeconds;
        s_pack = true;
        if (!File.Exists(TuningFile)) return;
        foreach (var line in File.ReadAllLines(TuningFile))
        {
            var kv = line.Split('=');
            if (kv.Length != 2 || !float.TryParse(kv[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) continue;
            string key = kv[0].Trim();
            if (key == "burst") s_burstScale = v;
            else if (key == "syn") s_synScale = v;
            else if (key == "secs") s_secs = Mathf.Clamp(v, 5f, 60f);
            else if (key == "pack") s_pack = v > 0f;
            else if (key.StartsWith("syn.")) s_synByType[key.Substring(4)] = v;
        }
    }

    // ── 장착 · 조준 ───────────────────────────────────────────
    private static async UniTask<bool> EquipAsync(PlayerController p, string soPath, CancellationToken ct)
    {
        var wm = p.WeaponManager;
        string key = Path.GetFileNameWithoutExtension(soPath);
        var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(soPath);
        if (so == null) return false;
        var runtime = WeaponData.FromSO(so);
        var preload = typeof(PlayerWeaponManager).GetMethod("PreloadWeaponClipsAsync", BindingFlags.Static | BindingFlags.NonPublic);
        if (preload != null) await (UniTask)preload.Invoke(null, new object[] { runtime, ct });
        typeof(PlayerWeaponManager).GetMethod("DestroySlotInstance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.Invoke(null, new object[] { wm.slots[PlayerWeaponManager.Slot0] });
        await UniTask.Yield(PlayerLoopTiming.Update, ct);
        await wm.AcquireWeaponToSlotAsync(runtime, PlayerWeaponManager.Slot0, setActive: true, playAppear: false);
        if (wm.CurrentSlotIndex != PlayerWeaponManager.Slot0) await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot0);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 8f)
        {
            var inst = wm.CurrentWeaponInstance;
            if (inst != null && inst.name.StartsWith(key) && inst.activeInHierarchy) return true;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        return false;
    }

    private static void Steer(PlayerController p, Vector3 at)
    {
        if ((p.transform.position - s_home).sqrMagnitude > 1f)
        {
            if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = s_home; rb.linearVelocity = Vector3.zero; }
            p.transform.position = s_home;
        }
        var mouse = Mouse.current;
        var cam = Camera.main;
        if (mouse != null && cam != null)
        {
            Vector3 screen = cam.WorldToScreenPoint(at + Vector3.up * 0.8f);
            if (screen.z > 0f) InputSystem.QueueDeltaStateEvent(mouse.position, (Vector2)screen);
        }
        p.RequestFacing(Quaternion.LookRotation(Flat(at - p.transform.position), Vector3.up));
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
    }

    // 실측 동안 진짜 마우스를 끄고 가상 마우스로 겨눈다(장비 형 실측과 같은 이유 — 커서가 게임 창 위에 있으면 넣은 조준을 덮는다).
    private static void TakeMouse()
    {
        if (s_probeMouse != null) return;
        s_realMouse = Mouse.current;
        s_probeMouse = InputSystem.AddDevice<Mouse>(ProbeMouseName);
        if (s_realMouse != null) InputSystem.DisableDevice(s_realMouse);
        s_probeMouse.MakeCurrent();
        EditorApplication.playModeStateChanged += ReleaseOnExit;
    }

    private static void ReleaseMouse()
    {
        EditorApplication.playModeStateChanged -= ReleaseOnExit;
        if (s_probeMouse != null) { InputSystem.RemoveDevice(s_probeMouse); s_probeMouse = null; }
        if (s_realMouse != null) { InputSystem.EnableDevice(s_realMouse); s_realMouse.MakeCurrent(); s_realMouse = null; }
    }

    private static void ReleaseOnExit(PlayModeStateChange s)
    {
        if (s == PlayModeStateChange.ExitingPlayMode) ReleaseMouse();
    }

    [InitializeOnLoadMethod]
    private static void RestoreMiceAfterReload()
    {
        foreach (var d in InputSystem.devices.ToArray())
        {
            if (d is not Mouse) continue;
            if (d.name == ProbeMouseName) InputSystem.RemoveDevice(d);
            else if (!d.enabled) InputSystem.EnableDevice(d);
        }
    }

    private static UniTask Wait(float s, CancellationToken ct)
        => UniTask.Delay(TimeSpan.FromSeconds(s), DelayType.Realtime, cancellationToken: ct);
}
#endif
