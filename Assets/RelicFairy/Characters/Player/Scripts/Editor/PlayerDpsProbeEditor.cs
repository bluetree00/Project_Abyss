using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// [밸런스 실측 · 플레이 중] 허수아비를 세워 두고 <b>무기·강화 조합별 실 DPS</b>를 잰다.
///
/// 플레이타임 설계서의 시간 모델(<c>방 시간 = 25초 + 방 총HP ÷ DPS</c>)에서 DPS는 한 번도 측정된 적이 없다.
/// 사람의 조작 대신 <see cref="InputBuffer"/>에 평타 입력을 밀어 넣어 <b>상한값</b>을 잰다(실전은 이보다 낮다).
///
/// 재는 법: 허수아비 HP를 매 틱 큰 값으로 채우고 그 사이 떨어진 양을 더한다.
/// HP 델타라서 평타뿐 아니라 화상·출혈·서약 폭발 같은 <b>간접 피해까지</b> 같은 기준으로 들어온다.
///
/// 결과: Temp/player_dps_probe.json + 콘솔 표
/// </summary>
public static class PlayerDpsProbeEditor
{
    private const string Root           = "RelicFairy/Debug/DPS 실측/";
    private const string TargetKey      = "Slime/Slime";   // 표적 몬스터 — 키 낮은 몹(원거리 기울여 쏘기 검증용)
    private const double TickGap        = 0.05;
    private const double SettleSeconds  = 4.0;    // 장착·자세 정착(집계 제외)
    private const double MeasureSeconds = 12.0;
    private const int    DummyHp        = 1000000;
    private const float  MeleeDistance  = 2.0f;
    private const float  RangedDistance = 6.0f;
    private const BindingFlags Inst     = BindingFlags.Instance | BindingFlags.NonPublic;
    // 룬 2단계 측정 대상 효과(하나라도 있으면 잰다). 조건부 CondAllDamage 등은 제외 — 조건이 허수아비에선 안 걸린다.
    private const string OffensiveEffects =
        @"(^|\+)(AllDamage|CritChance|CritDamage|AttackSpeed|SkillDamage|FirstHitBonus|PoisonOnHit|ExtraAttack|Freeze|Stun|ProjectileCount|ProjectilePierce|\w*Legend\w*)(\+|$)";

    /// <summary>재는 조합 한 칸. 무기 SO 경로 + 강화 단계.</summary>
    private sealed class Case
    {
        public string label, soPath;
        public int    enhance;
        public string itemId;       // 룬 전수 2단계: 이 룬 한 장만 놓고 잰다(null = 룬 없이 · "-" = 기준선으로 전부 뺀다)
    }

    private sealed class Result
    {
        public string label;
        public int    enhance, hits, crits;
        public float  damage, maxHit, attackPower;
    }

    private static readonly string[] WeaponPaths =
    {
        "Assets/RelicFairy/Weapon/Nameless/Data/T0_Nameless.asset",
        "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset",
        "Assets/RelicFairy/Weapon/Katana/Data/T2_Katana.asset",
        "Assets/RelicFairy/Weapon/Katana/Data/T3_Katana.asset",
        "Assets/RelicFairy/Weapon/Greatsword/Data/T1_Greatsword.asset",
        "Assets/RelicFairy/Weapon/Greatsword/Data/T2_Greatsword.asset",
        "Assets/RelicFairy/Weapon/Greatsword/Data/T3_Greatsword.asset",
        "Assets/RelicFairy/Weapon/Crossbow/Data/T1_Crossbow.asset",
        "Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset",
    };
    private static readonly int[] EnhanceLevels = { 0, 3, 6 };

    private static readonly List<Case>   s_cases   = new();
    private static readonly List<Result> s_results = new();
    private static MonsterBase s_dummy;
    private static Vector3 s_dummyHome;
    private static bool    s_spawning;
    private static Result  s_cur;
    private static int     s_index;
    private static int     s_phase;           // 0 준비 · 1 정착 · 2 측정
    private static double  s_next, s_phaseEnd, s_start;
    private static bool    s_running;
    private static double  s_measure = MeasureSeconds;   // 메뉴별 측정 길이(룬 2단계는 16초)
    private static bool    s_retriedFresh;                // 타격 0 칸을 새 표적으로 한 번 다시 쟀는가

    [MenuItem(Root + "조합 전수 — 무기 9종 × 강화 0·3·6 (플레이 중)")]
    private static void RunAll()
    {
        var cases = new List<Case>();
        foreach (string path in WeaponPaths)
            foreach (int lv in EnhanceLevels)
                cases.Add(new Case { label = Path.GetFileNameWithoutExtension(path), soPath = path, enhance = lv });
        Begin(cases);
    }

    [MenuItem(Root + "무기만 — 9종 강화 0 (플레이 중)")]
    private static void RunWeaponsOnly()
    {
        var cases = new List<Case>();
        foreach (string path in WeaponPaths)
            cases.Add(new Case { label = Path.GetFileNameWithoutExtension(path), soPath = path, enhance = 0 });
        Begin(cases);
    }

    [MenuItem(Root + "의심 칸 재측정 — 무형검·T1 대검·활·석궁 + 대조군 (플레이 중)")]
    private static void RunSuspects()
    {
        string[] paths =
        {
            "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset",           // 대조군(정상이던 칸)
            "Assets/RelicFairy/Weapon/Nameless/Data/T0_Nameless.asset",
            "Assets/RelicFairy/Weapon/Greatsword/Data/T1_Greatsword.asset",
            "Assets/RelicFairy/Weapon/Greatsword/Data/T2_Greatsword.asset",   // 대조군(같은 어빌리티 팩)
            "Assets/RelicFairy/Weapon/Crossbow/Data/T1_Crossbow.asset",
            "Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset",
        };
        var cases = new List<Case>();
        foreach (string path in paths)
            cases.Add(new Case { label = Path.GetFileNameWithoutExtension(path), soPath = path, enhance = 0 });
        Begin(cases);
    }

    /// <summary>
    /// 공격력 대비 DPS 곡선 — 강화 단계로 무기 공격력을 1~3배까지 벌려 같은 무기에서 잰다(두 번 반복).
    /// 성장 곡선 설계의 기준: 공격력이 오른 만큼 DPS가 오르는가(카타나 T1→T3 공격력 +73%에 DPS +8%로 나와 확인이 필요했다).
    /// </summary>
    [MenuItem(Root + "공격력 곡선 — 카타나·대검·석궁 T1 × 강화 0·5·10·20 ×2회 (플레이 중)")]
    private static void RunAttackCurve()
    {
        string[] paths =
        {
            "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset",
            "Assets/RelicFairy/Weapon/Greatsword/Data/T1_Greatsword.asset",
            "Assets/RelicFairy/Weapon/Crossbow/Data/T1_Crossbow.asset",
        };
        int[] levels = { 0, 5, 10, 20 };
        var cases = new List<Case>();
        for (int rep = 0; rep < 2; rep++)
            foreach (string path in paths)
                foreach (int lv in levels)
                    cases.Add(new Case { label = Path.GetFileNameWithoutExtension(path), soPath = path, enhance = lv });
        Begin(cases);
    }

    /// <summary>
    /// 룬 전수 점검 2단계 — 1단계 결과(Temp/rune_stat_pass.json)에서 기술형·코드 없음 판정 룬과 전설 전부를 한 장씩 장착해 잰다.
    /// 기준(룬 없음)을 8장마다 다시 재서 흔들림을 본다. 투사체 관련 룬은 석궁으로 한 번 더.
    /// ⚠️ 조건부 룬(체력 %·회피 직후 등)은 무적·만피 허수아비 측정에선 조건이 안 걸린다 — 결과 표에 「조건」으로 따로 표시해 판단한다.
    /// </summary>
    [MenuItem(Root + "룬 2단계 — 기술형 룬 DPS (1단계 결과 기준, 플레이 중)")]
    private static void RunRunePass()
    {
        string path = Path.Combine("Temp", "rune_stat_pass.json");
        if (!File.Exists(path)) { Debug.LogWarning("[DpsProbe] 1단계 결과가 없다 — 「룬 전수 점검/1단계」부터"); return; }
        string json = File.ReadAllText(path);
        const string Katana   = "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset";
        const string Crossbow = "Assets/RelicFairy/Weapon/Crossbow/Data/T1_Crossbow.asset";
        var cases = new List<Case> { new Case { label = "기준(룬 없음)", soPath = Katana, enhance = 0, itemId = "-" } };
        var ranged = new List<Case>();
        int n = 0;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(json,
                 "\\{\"id\":\"([^\"]+)\",\"name\":\"([^\"]*)\",\"rarity\":\"([^\"]+)\"[^}]*?\"effects\":\"([^\"]*)\",\"kind\":\"([^\"]+)\""))
        {
            string id = m.Groups[1].Value, name = m.Groups[2].Value, rarity = m.Groups[3].Value, eff = m.Groups[4].Value, kind = m.Groups[5].Value;
            if (kind == "stat" && rarity != "Legendary") continue;          // 순수 스탯형은 1단계 값으로 충분
            // 공격 기여가 있는 효과만 잰다 — 방어·이동·조건부(Cond*)는 무적·만피 허수아비 상대로 DPS가 안 바뀐다.
            if (!System.Text.RegularExpressions.Regex.IsMatch(eff, OffensiveEffects)) continue;
            cases.Add(new Case { label = name + "(" + id + ")", soPath = Katana, enhance = 0, itemId = id });
            // 원거리 무기 전용 효과(투사체 수·관통)만 석궁으로 한 번 더 — 전설 투사체 스킬은 무기와 무관하게 스스로 쏜다.
            if (eff.Contains("ProjectileCount") || eff.Contains("ProjectilePierce"))
                ranged.Add(new Case { label = name + "(" + id + ")·석궁", soPath = Crossbow, enhance = 0, itemId = id });
            if (++n % 6 == 0) cases.Add(new Case { label = "기준(룬 없음)", soPath = Katana, enhance = 0, itemId = "-" });
        }
        if (ranged.Count > 0)
        {
            cases.Add(new Case { label = "기준(룬 없음)·석궁", soPath = Crossbow, enhance = 0, itemId = "-" });
            cases.AddRange(ranged);
        }
        Begin(cases, 16.0);
    }

    /// <summary>배치된 룬을 모두 빼고 지정한 룬 한 장만 놓는다. "-"면 모두 뺀다. 이미 그 상태면 true.</summary>
    private static bool EnsureOnlyRune(string itemId)
    {
        var inv = GameRunBootstrapper.Instance?.Run?.ItemInventory;
        if (inv == null) return false;
        bool ok = itemId == "-" ? inv.PlacedItems.Count == 0
                                : inv.PlacedItems.Count == 1 && inv.PlacedItems[0].itemId == itemId;
        if (ok) return true;
        foreach (var it in new List<RuntimeItemData>(inv.PlacedItems)) inv.RemovePlaced(it);
        if (itemId == "-") return false;                          // 다음 틱에 확인
        var db = AssetDatabase.LoadAssetAtPath<ItemSODatabase>("Assets/RelicFairy/Shared/Item/SOdata/ItemSODatabase.asset");
        ItemSO so = null;
        if (db != null) foreach (var x in db.Items) if (x != null && x.itemId == itemId) { so = x; break; }
        if (so == null) { Debug.LogWarning("[DpsProbe] 룬 없음: " + itemId); return true; }   // 없는 룬은 룬 없이 잰다(결과에 표시됨)
        var item = RuntimeItemData.FromSO(so);
        if (inv.AddToStaging(item)) inv.PlaceItem(item);
        return false;                                             // 효과 재생성·스탯 갱신 뒤 다음 틱에 확인
    }

    /// <summary>
    /// 재현 — 09-19 룬 2단계에서 「심연 잠식」 이후 timeScale이 0.10에 묶여 타격이 0이 됐다.
    /// 기준 → 심연 잠식 → 기준 → 기준 순으로 재며 매 칸 준비 로그에 시간 배율 보유자를 남긴다.
    /// </summary>
    [MenuItem(Root + "재현 — 심연 잠식 뒤 시간 고착 (플레이 중, 약 1.5분)")]
    private static void RunTimeStuckRepro()
    {
        const string Katana = "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset";
        Begin(new List<Case>
        {
            new Case { label = "기준(룬 없음)", soPath = Katana, enhance = 0, itemId = "-" },
            new Case { label = "심연 잠식(item_t4_dark_aoe)", soPath = Katana, enhance = 0, itemId = "item_t4_dark_aoe" },
            new Case { label = "기준(룬 없음)", soPath = Katana, enhance = 0, itemId = "-" },
            new Case { label = "기준(룬 없음)", soPath = Katana, enhance = 0, itemId = "-" },
        }, 16.0);
    }

    [MenuItem(Root + "현재 상태 1회 — 지금 장착·서약 그대로 (플레이 중)")]
    private static void RunCurrent()
        => Begin(new List<Case> { new Case { label = "현재 상태", soPath = null, enhance = -1 } });

    [MenuItem(Root + "중지·기록")]
    private static void StopAndWrite() => Finish("manual");

    private static void Begin(List<Case> cases, double measureSeconds = MeasureSeconds)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[DpsProbe] 플레이 모드에서만"); return; }
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null) { Debug.LogWarning("[DpsProbe] 플레이어가 없다(런에 들어간 뒤 실행)"); return; }

        s_cases.Clear();
        s_cases.AddRange(cases);
        s_results.Clear();
        s_index   = 0;
        s_phase   = 0;
        s_start   = EditorApplication.timeSinceStartup;
        s_next    = s_start;
        s_running = true;
        s_measure = measureSeconds;
        s_retriedFresh = false;
        typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(player, true);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Debug.Log($"[DpsProbe] 시작 — {s_cases.Count}칸 · 칸당 {SettleSeconds + s_measure:0}초 " +
                  $"(예상 {s_cases.Count * (SettleSeconds + s_measure) / 60:0.0}분) · 유물 태양 주기 고정(충전 0)");
    }

    private static void Tick()
    {
        if (!s_running) { EditorApplication.update -= Tick; return; }
        if (!Application.isPlaying) { Finish("play-stopped"); return; }
        // 에디터가 멈춘 동안(콘솔 Error Pause 등) 게임은 안 돌고 이 도구만 입력을 넣는다 → 「Attack 고착 · 타격 0」으로 보인다.
        // 09-19 cb 재현: 씬 뷰가 GrabPass 이펙트를 못 그려 _GrabTexture 오류 → Error Pause. 그 칸은 무효로 적고 멈춘다.
        if (EditorApplication.isPaused)
        {
            string at = s_index < s_cases.Count ? s_cases[s_index].label : "-";
            Debug.LogWarning($"[DpsProbe] 에디터 일시정지로 무효 — 「{at}」부터 측정 중단 (콘솔 Error Pause · 씬 뷰 셰이더 오류 확인)");
            Finish("editor-paused");
            return;
        }

        double now = EditorApplication.timeSinceStartup;
        if (now < s_next) return;
        s_next = now + TickGap;

        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null) return;
        HoldRelicCycleNeutral(player);

        if (s_index >= s_cases.Count) { Finish("done"); return; }
        var c = s_cases[s_index];

        // 0) 준비 — 무기 장착 · 허수아비 배치
        if (s_phase == 0)
        {
            if (!EnsureDummy(player)) return;
            if (c.soPath != null && !TryEquip(player, c)) return;
            if (c.itemId != null && !EnsureOnlyRune(c.itemId)) return;
            ResetDummy();
            s_cur = new Result { label = c.label, enhance = c.enhance, attackPower = CurrentAttackPower(player) };
            var eq = player.WeaponManager?.Weapon0Data;
            var rs = player.RuntimeStats;
            Debug.Log($"[DpsProbe] 준비 {c.label} +{c.enhance} — 장착 {eq?.weaponSOKey ?? "없음"} " +
                      $"type={eq?.weaponType} 공격력 {eq?.baseAttack:0.0}(원본 {eq?.baseAttackRaw:0.0}) 강화 {eq?.enhanceLevel} " +
                      $"· Melee {rs?.MeleeAttack} Ranged {rs?.RangedAttack} · timeScale {Time.timeScale:0.00}" +
                      (Time.timeScale < 0.999f ? $" · 보유자 {TimeScaleArbiter.DescribeHolders()}" : "") +
                      // 심연 잠식 뒤 타격 0 추적(cb 단서): 도구가 쥔 플레이어가 중간에 파괴·교체되는지
                      $" · 플레이어 #{player.GetInstanceID()} ×{Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None).Length}");
            s_phase    = 1;
            s_phaseEnd = now + SettleSeconds;
            return;
        }

        // 1·2) 정착 → 측정. 두 단계 모두 같은 방식으로 때린다.
        if (s_dummy == null || s_dummy.IsDead) { s_phase = 0; return; }
        ClearOtherMonsters(player);                     // 방 웨이브가 계속 나와 화살을 가로막고 타격을 가져간다
        FreezeDummy();                                  // 넉백으로 밀려나면 사거리를 벗어나 타격이 끊긴다
        FacePlayerToDummy(player, IsRanged(c));
        AimAtDummy();                                   // 원거리는 마우스 방향으로 쏜다 — 조준점을 표적으로
        player.InputBuffer.Push(Command.Light);
        if (IsRanged(c)) TraceActState(player, now);

        int hp   = s_dummy.CurrentHp;
        int drop = DummyHp - hp;
        if (drop > 0) SetDummyHp(DummyHp);

        if (s_phase == 1)
        {
            if (now < s_phaseEnd) return;
            ResetDummy();
            s_phase    = 2;
            s_phaseEnd = now + s_measure;
            return;
        }

        if (drop > 0)
        {
            if (s_cur.hits < 3)
            {
                var rsh = player.RuntimeStats;
                Debug.Log($"[DpsProbe] {s_cur.label} +{s_cur.enhance} 타격 {s_cur.hits + 1}: {drop} " +
                          $"· Melee {rsh?.MeleeAttack} Ranged {rsh?.RangedAttack} · type={player.WeaponManager?.Weapon0Data?.weaponType}");
            }
            s_cur.damage += drop;
            s_cur.hits++;                                   // 피해가 들어온 틱 수(0.05초) — 실타수의 근사
            if (drop > s_cur.maxHit) s_cur.maxHit = drop;
        }
        if (now < s_phaseEnd) return;

        if (s_cur.hits == 0)
        {
            var eq0 = player.WeaponManager?.Weapon0Data;
            Debug.LogWarning($"[DpsProbe] 타격 0 — {s_cur.label} +{s_cur.enhance} · 장착 {eq0?.weaponSOKey ?? "없음"} " +
                             $"· 표적까지 {Vector3.Distance(player.transform.position, s_dummy.transform.position):0.0}m " +
                             $"· 표적 HP {s_dummy.CurrentHp} · timeScale {Time.timeScale:0.00} · 버퍼 {player.InputBuffer?.Count} " +
                             $"· 행동 상태 {typeof(PlayerController).GetField("actStateDebug", Inst)?.GetValue(player)} · {DescribeDummy()} " +
                             $"· 시간 보유자 {TimeScaleArbiter.DescribeHolders()}");
            // 몬스터 쪽이 막혔는지(맞은 표적이 더는 피해를 안 받음) 플레이어 쪽이 막혔는지 가른다 — 새 표적으로 같은 칸을 한 번 더.
            if (!s_retriedFresh)
            {
                s_retriedFresh = true;
                s_dummy.gameObject.SetActive(false);
                s_dummy = null;
                s_phase = 0;
                Debug.LogWarning($"[DpsProbe] 타격 0 → 새 표적으로 재측정: {s_cur.label}");
                return;
            }
        }
        s_retriedFresh = false;
        s_results.Add(s_cur);
        Debug.Log($"[DpsProbe] {s_cur.label} +{s_cur.enhance} → DPS {s_cur.damage / s_measure:0.0} " +
                  $"· 타수 {s_cur.hits} · 평균 {(s_cur.hits > 0 ? s_cur.damage / s_cur.hits : 0):0.0} " +
                  $"· 최대 {s_cur.maxHit:0}");
        s_index++;
        s_phase = 0;
    }

    private static string s_lastAct;
    private static double s_lastActAt;

    /// <summary>원거리 칸의 행동 상태 변화를 찍는다 — 한 상태에 오래 머물면 공격이 안 끝나는 것이다.</summary>
    private static void TraceActState(PlayerController player, double now)
    {
        string act = typeof(PlayerController).GetField("actStateDebug", Inst)?.GetValue(player)?.ToString() ?? "?";
        if (act != s_lastAct)
        {
            Debug.Log($"[DpsProbe] 행동 상태 {s_lastAct ?? "-"} → {act} ({(s_lastAct == null ? 0 : now - s_lastActAt):0.00}초 머묾) · 버퍼 {player.InputBuffer?.Count}");
            s_lastAct   = act;
            s_lastActAt = now;
        }
    }

    // ── 허수아비 ─────────────────────────────────────────

    /// <summary>
    /// 표적을 세운다 — 실제 몬스터를 스포너와 <b>같은 경로</b>(풀러)로 불러 세워 두고 움직임만 막는다.
    /// (`TrainingDummy.prefab`은 MonsterBase가 아닌 옛 구현이고, MonsterBase형 허수아비는 씬에만 있어 런타임에 못 쓴다.)
    /// 방어력·상태이상·받는 피해 배율이 실전과 같아야 DPS가 의미를 가진다.
    /// </summary>
    private static bool EnsureDummy(PlayerController player)
    {
        if (s_dummy != null && !s_dummy.IsDead) { FreezeDummy(); return true; }
        if (s_spawning) return false;

        s_spawning = true;
        SpawnTargetAsync(player).Forget();
        return false;   // 스폰·설정 로드가 끝나는 다음 틱부터 쓴다
    }

    private static async UniTaskVoid SpawnTargetAsync(PlayerController player)
    {
        try
        {
            Vector3 pos = OpenSpotAround(player);
            var mb = await Managers.ObjectPooler.SpawnAsync<MonsterBase>(
                TargetKey, ObjectPoolerManager.PoolType.Monster, pos, Quaternion.identity);
            if (mb == null) { Debug.LogError("[DpsProbe] 표적 스폰 실패: " + TargetKey); Finish("no-dummy"); return; }

            mb.name       = "@DpsProbeTarget";
            mb.HpFloorMin1 = true;              // 죽지 않는다 — 측정이 끊기지 않게
            s_dummyHome   = mb.transform.position;
            s_dummy       = mb;
        }
        catch (System.Exception e)
        {
            Debug.LogError("[DpsProbe] 표적 스폰 예외: " + e.Message);
            Finish("no-dummy");
        }
        finally { s_spawning = false; }
    }

    /// <summary>
    /// 표적 자리를 고른다 — 세 조건을 모두 만족하는 가장 먼 점:
    ///  ① 내비메시 위(몬스터가 설 수 있는 바닥) ② 발밑에 바닥 콜라이더 ③ 플레이어 가슴 높이에서 표적까지 막힘 없음.
    /// 벽에 붙은 표적은 화살이 벽에 막혀 원거리가 0이 되고(2차), 레이만 보고 고르면 떠 있는 방의 가장자리 너머를
    /// 골라 근접까지 0이 된다(3차). 조건을 못 채우면 예전처럼 정면 2m.
    /// </summary>
    private static Vector3 OpenSpotAround(PlayerController player)
    {
        Vector3 from = player.transform.position;
        float[] dists = { RangedDistance + 0.5f, 4f, MeleeDistance + 0.5f };
        foreach (float d in dists)
        {
            for (int i = 0; i < 16; i++)
            {
                // 정면부터 좌우로 번갈아 넓혀 간다(0, +22.5, -22.5, +45 …)
                float yaw = ((i + 1) / 2) * 22.5f * (i % 2 == 0 ? 1f : -1f);
                Vector3 dir  = Quaternion.Euler(0f, yaw, 0f) * Flat(player.transform.forward);
                Vector3 want = from + dir * d;

                if (!UnityEngine.AI.NavMesh.SamplePosition(want, out var nav, 0.8f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                Vector3 spot = nav.position;
                if (Mathf.Abs(spot.y - from.y) > 0.6f) continue;                                   // 다른 층·단차
                if (!Physics.Raycast(spot + Vector3.up, Vector3.down, 2.5f, ~0, QueryTriggerInteraction.Ignore)) continue;   // 바닥 없음
                Vector3 eye = from + Vector3.up * 1.2f + dir * 0.6f;                                // 플레이어 자신을 피해 조금 앞에서
                if (Physics.Linecast(eye, spot + Vector3.up * 1.0f, ~0, QueryTriggerInteraction.Ignore)) continue;         // 벽에 막힘

                Debug.Log($"[DpsProbe] 표적 자리 — 정면 기준 {yaw:+0;-0;0}° · {Vector3.Distance(from, spot):0.0}m (바닥·시야 확인)");
                return spot;
            }
        }
        Debug.LogWarning("[DpsProbe] 조건에 맞는 자리가 없다 — 정면 2m에 세운다(원거리는 막힐 수 있음)");
        return from + Flat(player.transform.forward) * MeleeDistance;
    }

    /// <summary>
    /// 조준점을 표적에 둔다 — 원거리는 캐릭터 회전이 아니라 마우스 방향으로 쏘기 때문이다.
    /// 마우스 위치 <b>입력 이벤트</b>를 큐에 넣는다(다음 플레이어 업데이트에서 처리돼 게임이 읽는다).
    /// ⚠️ 에디터 update 문맥에서 InputState.Change로 상태를 직접 쓰면 게임이 못 읽는다((0,0)으로 남음, 09-18 확인).
    /// </summary>
    private static void AimAtDummy()
    {
        var mouse = Mouse.current;
        var cam   = Camera.main;
        if (mouse == null || cam == null || s_dummy == null) return;
        Vector3 screen = cam.WorldToScreenPoint(s_dummy.transform.position + Vector3.up * 1.0f);
        if (screen.z <= 0f) return;                     // 카메라 뒤 — 화면 좌표가 뒤집힌다
        InputSystem.QueueDeltaStateEvent(mouse.position, (Vector2)screen);
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }

    /// <summary>표적 외에 살아 있는 몬스터를 모두 쓰러뜨린다(측정 방해 제거). 0.5초 간격이면 충분하다.</summary>
    private static double s_nextClear;
    private static void ClearOtherMonsters(PlayerController player)
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < s_nextClear) return;
        s_nextClear = now + 0.5;
        foreach (var mb in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
        {
            if (mb == s_dummy || mb.IsDead || !mb.isActiveAndEnabled || mb.CurrentHp <= 0) continue;
            mb.TakeDamage(mb.CurrentHp * 10f + 100000f, player.gameObject, 0f);
        }
    }

    /// <summary>표적을 제자리에 묶는다 — 추격·넉백으로 거리가 변하면 타수가 흔들린다.</summary>
    private static void FreezeDummy()
    {
        if (s_dummy.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var agent) && agent.enabled)
            agent.enabled = false;
        s_dummy.transform.position = s_dummyHome;
        if (s_dummy.TryGetComponent<Rigidbody>(out var rb)) { rb.position = s_dummyHome; if (!rb.isKinematic) rb.linearVelocity = Vector3.zero; }
    }

    private static void ResetDummy() => SetDummyHp(DummyHp);

    /// <summary>
    /// 가웨인 「정오의 맹세」 게이지는 시간으로 돈다(충전 20초 → 정오 10초 → 황혼 15초). 정오엔 공속·치명·모든 피해가,
    /// 각인(충전 80%+)엔 공격력이 오른다 — 칸마다 걸리는 구간이 달라 같은 룬끼리 +8%·+50%로 갈렸다(09-19 1차 무효 원인).
    /// 측정 동안 충전 0으로 묶어 유물 기여를 0으로 고정한다. 게이지가 없는 유물이면 아무것도 안 한다.
    /// </summary>
    private static void HoldRelicCycleNeutral(PlayerController player)
    {
        if (player == null || !player.TryGetComponent<ZenithGauge>(out var g)) return;
        if (g.CurrentPhase == ZenithGauge.ZPhase.Charging && g.ChargeFill < 0.05f) return;
        g.Restore(new RelicResourceState { fill = 0f, phase = (int)ZenithGauge.ZPhase.Charging, aux = 0f });
    }

    /// <summary>표적 상태 한 줄 — 타격 0일 때 몬스터 쪽이 막혔는지 본다.</summary>
    private static string DescribeDummy()
    {
        if (s_dummy == null) return "표적 없음";
        var fsm = typeof(MonsterBase).GetField("_fsm", Inst)?.GetValue(s_dummy);
        var cons = fsm?.GetType().GetProperty("CurrentConstraints")?.GetValue(fsm);
        var state = fsm?.GetType().GetProperty("CurrentState")?.GetValue(fsm);
        int cols = 0, colsOn = 0;
        foreach (var col in s_dummy.GetComponentsInChildren<Collider>(true)) { cols++; if (col.enabled && col.gameObject.activeInHierarchy) colsOn++; }
        return $"표적 dead={s_dummy.IsDead} active={s_dummy.gameObject.activeInHierarchy} state={state} 제약={cons} " +
               $"콜라이더 {colsOn}/{cols} layer={LayerMask.LayerToName(s_dummy.gameObject.layer)}";
    }

    private static void SetDummyHp(int hp)
    {
        var runtime = typeof(MonsterBase).GetField("_runtime", Inst | BindingFlags.Public)?.GetValue(s_dummy);
        runtime?.GetType().GetField("CurrentHp")?.SetValue(runtime, hp);   // 프로퍼티가 아니라 필드다
    }



    // ── 플레이어 ─────────────────────────────────────────

    private static bool IsRanged(Case c)
        => c.soPath != null && (c.soPath.Contains("Bow") || c.soPath.Contains("Crossbow"));

    private static void FacePlayerToDummy(PlayerController player, bool ranged)
    {
        Vector3 to = s_dummy.transform.position - player.transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f) return;

        float want = ranged ? RangedDistance : MeleeDistance;
        Vector3 pos = s_dummy.transform.position - to.normalized * want;
        pos.y = player.transform.position.y;
        player.transform.position = pos;
        player.transform.rotation = Quaternion.LookRotation(to.normalized);
        if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = pos; if (!rb.isKinematic) rb.linearVelocity = Vector3.zero; }
    }

    /// <summary>무기 SO를 직접 읽어 장착한다(T0·T1은 SO가 Addressable이 아니라 에디터 경로로 읽는다).</summary>
    private static bool TryEquip(PlayerController player, Case c)
    {
        var wm = player.WeaponManager;
        if (wm == null) return false;

        string key = Path.GetFileNameWithoutExtension(c.soPath);
        if (wm.Weapon0Data != null && wm.Weapon0Data.weaponSOKey == key && wm.Weapon0Data.enhanceLevel == c.enhance)
            return true;

        // 장착 요청은 비동기다 — 이미 요청해 둔 무기가 도착하는 중이면 기다린다.
        if (wm.Weapon0Data != null && wm.Weapon0Data.weaponSOKey == key)
        {
            wm.Weapon0Data.enhanceLevel = c.enhance;
            wm.Weapon0Data.RecomputeEnhancedStats();
            wm.RaiseEquippedWeaponRefreshed();
            return true;
        }

        var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(c.soPath);
        if (so == null) { Debug.LogWarning("[DpsProbe] 무기 SO 없음: " + c.soPath); s_index++; return false; }

        var runtime = WeaponData.FromSO(so);
        typeof(PlayerWeaponManager)
            .GetMethod("ApplyServerOverrideIfAvailable", BindingFlags.Static | BindingFlags.NonPublic)
            ?.Invoke(null, new object[] { runtime, key });   // 차트(EQUIPMENT_DATA) 값이 정본이다
        runtime.enhanceLevel = c.enhance;
        runtime.RecomputeEnhancedStats();
        wm.AcquireWeaponAsync(runtime, autoEquip: true).Forget();
        return false;
    }

    private static float CurrentAttackPower(PlayerController player)
    {
        // PlayerRuntimeStats는 MonoBehaviour가 아니라 PlayerController가 들고 있는 순수 객체다.
        var stats = player.RuntimeStats;
        return stats != null ? stats.AttackPower : 0f;   // 근접·원거리 중 높은 쪽
    }

    // ── 기록 ─────────────────────────────────────────────

    private static void Finish(string reason)
    {
        if (!s_running) return;
        s_running = false;
        EditorApplication.update -= Tick;
        if (s_dummy != null) s_dummy.gameObject.SetActive(false);   // 풀러 소유 — 파괴하지 않는다
        s_dummy   = null;
        s_spawning = false;

        var sb = new StringBuilder();
        sb.Append("{\"reason\":\"").Append(reason)
          .Append("\",\"measureSeconds\":").Append(s_measure.ToString("0.0"))
          .Append(",\"cases\":[");
        var table = new StringBuilder("[DpsProbe] 결과\n  무기                 강화   DPS    타수  평균타   최대타  크리%  공격력\n");
        for (int i = 0; i < s_results.Count; i++)
        {
            var r = s_results[i];
            float dps  = (float)(r.damage / s_measure);
            float avg  = r.hits > 0 ? r.damage / r.hits : 0f;

            if (i > 0) sb.Append(',');
            sb.Append("{\"weapon\":\"").Append(r.label)
              .Append("\",\"enhance\":").Append(r.enhance)
              .Append(",\"dps\":").Append(dps.ToString("0.0"))
              .Append(",\"damage\":").Append(r.damage.ToString("0"))
              .Append(",\"hits\":").Append(r.hits)
              .Append(",\"avgHit\":").Append(avg.ToString("0.0"))
              .Append(",\"maxHit\":").Append(r.maxHit.ToString("0"))
              .Append(",\"attackPower\":").Append(r.attackPower.ToString("0.0")).Append('}');
            table.Append($"  {r.label,-20} +{r.enhance,-4} {dps,6:0.0} {r.hits,6} {avg,6:0.0} {r.maxHit,6:0} {r.attackPower,7:0.0}\n");
        }
        sb.Append("]}");
        File.WriteAllText(Path.Combine("Temp", "player_dps_probe.json"), sb.ToString());
        Debug.Log(table.ToString());
        Debug.Log($"[DpsProbe] 종료({reason}) — {s_results.Count}칸 · {EditorApplication.timeSinceStartup - s_start:0}초");
    }
}
