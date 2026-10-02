using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 전투 로그 — 테스트(직접 플레이 · Fight Sim) 중 보스와 플레이어 사이에 무슨 일이 있었는지 남긴다(에디터 전용).
///   · 보스   : 상태 전이 · 패턴 시작 · HP 변화 · 피해 면역(게이트) 켜짐/꺼짐 · 사망
///   · 공격   : 플레이어 TakeDamage 판정마다 — 명중 / 막힘(저스트 회피 · 무적 · 무효화 · 사망 무효), 원래→최종 피해,
///              가해자, 그 순간의 보스 패턴과 패턴 시작 뒤 경과
///   · 패턴 끝: 다음 패턴이 시작될 때 직전 패턴의 명중·막힘 수 — 0이면 판정 호출 자체가 없었다(빗나감)
///   · 플레이어: 행동·이동 상태 · 무적 전환 + 2초마다 스냅숏(HP · 위치 · 보스와 거리)
///   · 화면   : 패턴마다 처음 2번은 시작 0.3초·0.75초 뒤 게임 화면(예고 가이드가 차오르는 중) → shots/&lt;패턴&gt;_&lt;회차&gt;_&lt;n&gt;.png
///   · 가이드 : 예고 가이드(Guide_* · *Warning*)가 생기는 순간을 잡아 패턴·가이드 종류마다 처음 2번 +0.15초·+0.6초 캡처(shots/guide_…) +
///              종류(데칼/대체 도형)·위치·화면 안/밖·발밑 충돌면이 가이드보다 높은지(묻힘) 기록 — 윈드업 뒤에 뜨는 가이드도 잡힌다
/// 메뉴: RelicFairy/Boss/Combat Log/Start (Play) · Stop — Fight Sim은 자동으로 켠다.
/// 파일: Logs/boss_combat/combat_&lt;시각&gt;.log (Temp는 에디터 재시작 때 지워져서 Logs에 둔다).
/// 핵심 줄(패턴 · 공격 · 상태 전이)은 콘솔에도 [BossLog]로 낸다.
/// </summary>
public static class BossCombatLog
{
    // ── Constants ──────────────────────────────────────────────
    private const string Root             = "RelicFairy/Boss/Combat Log/";
    private const float  SnapshotInterval = 2f;
    private const float  BossHpInterval   = 1f;
    private const int    ShotRuns         = 2;
    private static readonly float[] ShotDelays = { 0.3f, 0.75f };
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private sealed class PatternStat { public int Runs, Applied, Blocked, Damage; }

    // ── Static ─────────────────────────────────────────────────
    private static bool             s_on;
    private static StreamWriter     s_file;
    private static string           s_path;
    private static float            s_t0, s_nextFind, s_nextSnap, s_bossHpAt;
    private static PlayerController s_player;
    private static MonsterBase      s_boss;
    private static BossSpawner      s_spawner;
    private static bool             s_bossDeadLogged, s_bossImmune, s_playerInv;
    private static string           s_bossState = "", s_playerAct = "", s_playerLoco = "";
    private static string           s_pattern = "-";
    private static float            s_patternAt = -1f, s_lastPatternTime = -999f;
    private static int              s_patApplied, s_patBlocked, s_patDamage;
    private static int              s_bossHpLogged = -1;
    private static readonly Dictionary<string, PatternStat> s_stats = new();
    private static int              s_totApplied, s_totBlocked, s_totDamage;
    private static readonly Dictionary<PlayerController.DamageOutcome, int> s_outcomes = new();
    private static string           s_shotDir;
    private static readonly List<(float at, string file)> s_shots = new();
    private static readonly HashSet<int> s_guideSeen = new();
    private static readonly Dictionary<string, int> s_guideShots = new();
    private static float s_nextGuideScan;

    // ── Properties ─────────────────────────────────────────────
    public static bool   IsOn => s_on;
    public static string Path => s_path;

    // ── Public Methods ─────────────────────────────────────────
    [MenuItem(Root + "Start (Play)")] private static void MenuStart() => Start(null);
    [MenuItem(Root + "Stop")]         private static void MenuStop()  => Stop();

    /// <summary>로그를 켠다. <paramref name="dir"/>가 null이면 Logs/boss_combat. 파일 경로를 돌려준다.</summary>
    public static string Start(string dir)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[BossLog] 플레이 모드에서만 켤 수 있다."); return null; }
        Stop();
        dir ??= System.IO.Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_combat");
        Directory.CreateDirectory(dir);
        s_path = System.IO.Path.Combine(dir, $"combat_{DateTime.Now:MMdd_HHmmss}.log");
        s_file = new StreamWriter(s_path, false, new UTF8Encoding(true)) { AutoFlush = true };
        s_shotDir = System.IO.Path.Combine(dir, "shots");
        s_shots.Clear();
        s_guideSeen.Clear(); s_guideShots.Clear(); s_nextGuideScan = 0f;
        BossGuideShapes.Reset();

        s_player = null; s_boss = null; s_spawner = null;
        s_bossDeadLogged = s_bossImmune = s_playerInv = false;
        s_bossState = s_playerAct = s_playerLoco = "";
        s_pattern = "-"; s_patternAt = -1f; s_lastPatternTime = -999f;
        s_patApplied = s_patBlocked = s_patDamage = 0;
        s_bossHpLogged = -1;
        s_stats.Clear(); s_outcomes.Clear();
        s_totApplied = s_totBlocked = s_totDamage = 0;
        s_t0 = Time.time; s_nextFind = 0f; s_nextSnap = 0f; s_bossHpAt = 0f;
        s_on = true;

        EditorApplication.update               -= Tick;
        EditorApplication.update               += Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        Write("로그 시작", true);
        return s_path;
    }

    /// <summary>로그를 끄고 패턴별 요약을 파일 끝에 붙인다.</summary>
    public static void Stop()
    {
        if (!s_on) return;
        FlushPattern();
        Write("── 요약", false);
        Write($"공격 판정 {s_totApplied + s_totBlocked}회 · 명중 {s_totApplied}(피해 {s_totDamage}) · 막힘 {s_totBlocked}" +
              (s_outcomes.Count > 0 ? " · " + string.Join(" · ", s_outcomes.Select(kv => $"{OutcomeText(kv.Key)} {kv.Value}")) : ""), true);
        Write("패턴                                   실행  명중  막힘  피해   판정 0인 실행 = 빗나감", false);
        foreach (var kv in s_stats.OrderByDescending(k => k.Value.Runs))
            Write($"{kv.Key,-38} {kv.Value.Runs,4} {kv.Value.Applied,5} {kv.Value.Blocked,5} {kv.Value.Damage,6}", false);

        Unhook();
        EditorApplication.update               -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        s_on = false;
        s_file?.Dispose(); s_file = null;
        Debug.Log($"[BossLog] 로그 종료 — {s_path}");
    }

    // ── Private Methods ────────────────────────────────────────
    private static void Tick()
    {
        if (!s_on) return;
        if (!Application.isPlaying) { Stop(); return; }
        float now = Time.time;

        if (Time.realtimeSinceStartup >= s_nextFind)
        {
            s_nextFind = Time.realtimeSinceStartup + 1f;
            Resolve();
        }
        TickBoss(now);
        TickPlayer();
        TickShots(now);
        if (Time.realtimeSinceStartup >= s_nextGuideScan) { s_nextGuideScan = Time.realtimeSinceStartup + 0.1f; ScanGuides(now); BossGuideShapes.Scan(); }   // 가이드가 뜬 시각을 잡아 둔다(맞은 순간 예고 길이)
        if (now >= s_nextSnap) { s_nextSnap = now + SnapshotInterval; Snapshot(); }
    }

    private static void Resolve()
    {
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        if (p != s_player)
        {
            Unhook();
            s_player = p;
            if (s_player != null)
            {
                s_player.OnDamageResolved += OnDamageResolved;
                s_player.OnHitTaken       += OnHitTaken;
                Write($"[플레이어] 연결 — HP {s_player.RuntimeStats?.Hp}/{s_player.RuntimeStats?.MaxHp}", true);
            }
        }

        if (s_spawner == null) s_spawner = UnityEngine.Object.FindFirstObjectByType<BossSpawner>();
        var b = s_spawner != null ? s_spawner.SpawnedBoss : null;
        if (b != null && b != s_boss)
        {
            s_boss = b; s_bossDeadLogged = false; s_bossHpLogged = b.CurrentHp;
            Write($"[보스] 등장 {b.name} · HP {b.CurrentHp}/{b.EffectiveMaxHp}", true);
        }
    }

    private static void Unhook()
    {
        if (s_player == null) return;
        s_player.OnDamageResolved -= OnDamageResolved;
        s_player.OnHitTaken       -= OnHitTaken;
    }

    private static void TickBoss(float now)
    {
        if (s_boss == null) return;

        string state = ReadState(s_boss);
        if (state != s_bossState)
        {
            Write($"[보스] 상태 {Empty(s_bossState)} → {state}", true);
            s_bossState = state;
        }

        if (ReadLastPattern(s_boss, out string pname, out float ptime) && ptime > s_lastPatternTime + 0.001f)
        {
            FlushPattern();
            s_lastPatternTime = ptime;
            s_pattern = pname; s_patternAt = now;
            if (!s_stats.TryGetValue(pname, out var st)) s_stats[pname] = st = new PatternStat();
            st.Runs++;
            Write($"[패턴] {pname} 시작 · 보스 HP {HpPct(s_boss)} · 거리 {Dist():0.0} m", true);
            if (st.Runs <= ShotRuns)
                for (int i = 0; i < ShotDelays.Length; i++)
                    s_shots.Add((now + ShotDelays[i], $"{Clean(pname)}_{st.Runs}_{i + 1}.png"));
        }

        bool immune = s_boss.IsDamageImmuneNow;
        if (immune != s_bossImmune)
        {
            Write($"[보스] 피해 면역 {(immune ? "켜짐" : "꺼짐")} · 상태 {state}", true);
            s_bossImmune = immune;
        }

        int hp = s_boss.CurrentHp;
        if (hp != s_bossHpLogged && now - s_bossHpAt >= BossHpInterval)
        {
            Write($"[보스 HP] {s_bossHpLogged} → {hp} ({hp - s_bossHpLogged:+0;-0}) · {HpPct(s_boss)}", false);
            s_bossHpLogged = hp; s_bossHpAt = now;
        }

        if (s_boss.IsDead && !s_bossDeadLogged)
        {
            s_bossDeadLogged = true;
            FlushPattern();
            Write($"[보스] 사망 — 전투 {now - s_t0:0.0}초", true);
        }
    }

    // 예약한 화면 찍기 — ScreenCapture는 그 프레임 끝에 파일을 쓴다(게임 화면 그대로 · 도메인 리로드 없음)
    /// <summary>새로 생긴 예고 가이드(씬 루트의 Guide_* · *Warning*)를 찾아 기록하고, 패턴·종류마다 처음 2번은 캡처를 예약한다.</summary>
    private static void ScanGuides(float now)
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!scene.IsValid()) return;
        foreach (var go in scene.GetRootGameObjects())
        {
            if (!go.activeInHierarchy) continue;
            string n = go.name;
            if (!(n.StartsWith("Guide_") || n.Contains("Warning") || go.GetComponent<DragonBossWarningZone>() != null)) continue;
            if (!s_guideSeen.Add(go.GetInstanceID())) continue;

            var mr = go.GetComponentInChildren<MeshRenderer>();
            string kind = mr == null ? "렌더러 없음"
                        : mr.sharedMaterial == null ? "재질 없음"
                        : go.GetComponentInChildren<MeshFilter>()?.sharedMesh?.name == "Quad" || mr.sharedMaterial.shader.name.Contains("Indicator") ? $"데칼({mr.sharedMaterial.name})"
                        : $"대체 도형({mr.sharedMaterial.shader.name})";
            Vector3 p = go.transform.position;
            string buried = "";
            if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 6f, ~LayerMask.GetMask("Player", "Monster", "MonsterHit", "Ignore Raycast", "UI"), QueryTriggerInteraction.Ignore)
                && hit.point.y > p.y + 0.01f)
                buried = $" · ⚠️발밑 충돌면 {hit.collider.name}이 {hit.point.y - p.y:0.00} m 위(묻힘)";
            var cam = Camera.main;
            string screen = "-";
            if (cam != null)
            {
                Vector3 v = cam.WorldToViewportPoint(p);
                screen = v.z > 0f && v.x > 0f && v.x < 1f && v.y > 0f && v.y < 1f ? $"화면 ({v.x:0.00},{v.y:0.00})" : "화면 밖";
            }
            // 가이드 면을 뚫고 올라온 메시(바닥 타일 등) — 가이드 바닥 원 안에서 윗면이 가이드보다 높고 아랫면은 낮은 렌더러
            float gr = Mathf.Max(go.transform.lossyScale.x, go.transform.lossyScale.z) * 0.5f;
            var cover = new List<string>();
            foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r == null || !r.enabled || r.transform.IsChildOf(go.transform)) continue;
                if (r.GetComponentInParent<MonsterBase>() != null || r.GetComponentInParent<PlayerController>() != null) continue;
                var rb = r.bounds;
                if (rb.max.y <= p.y + 0.005f || rb.min.y > p.y || rb.size.y > 3f) continue;
                Vector2 near = new(Mathf.Clamp(p.x, rb.min.x, rb.max.x), Mathf.Clamp(p.z, rb.min.z, rb.max.z));
                if ((near - new Vector2(p.x, p.z)).sqrMagnitude > gr * gr) continue;
                cover.Add($"{r.name}+{rb.max.y - p.y:0.00}");
                if (cover.Count >= 4) break;
            }
            if (cover.Count > 0) buried += $" · ⚠️가이드 위로 솟은 메시 {string.Join(", ", cover)}";
            Write($"[가이드] {n} · 패턴 {s_pattern} +{(s_patternAt >= 0f ? now - s_patternAt : 0f):0.00}초 · {kind} · 위치 ({p.x:0.0},{p.y:0.00},{p.z:0.0}) 크기 {go.transform.lossyScale.x:0.0}×{go.transform.lossyScale.y:0.0} · {screen}{buried}", buried.Length > 0);

            string key = s_pattern + "|" + n;
            s_guideShots.TryGetValue(key, out int c);
            if (c >= 2) continue;
            s_guideShots[key] = ++c;
            s_shots.Add((now + 0.15f, $"guide_{Clean(s_pattern)}_{Clean(n)}_{c}_a.png"));
            s_shots.Add((now + 0.6f,  $"guide_{Clean(s_pattern)}_{Clean(n)}_{c}_b.png"));
        }
    }

    private static void TickShots(float now)
    {
        for (int i = s_shots.Count - 1; i >= 0; i--)
        {
            if (now < s_shots[i].at) continue;
            Directory.CreateDirectory(s_shotDir);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(s_shotDir, s_shots[i].file));
            s_shots.RemoveAt(i);
        }
    }

    private static string Clean(string s)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }

    private static void TickPlayer()
    {
        if (s_player == null) return;

        string act  = ReadField(s_player, "actStateDebug");
        string loco = s_player.LocoSM != null ? s_player.LocoSM.CurrentId.ToString() : "-";
        if (act != s_playerAct)
        {
            Write($"[플레이어] 행동 {Empty(s_playerAct)} → {act}", false);
            s_playerAct = act;
        }
        if (loco != s_playerLoco)
        {
            // 이동 상태는 자주 바뀐다 — 회피·날아감·사망 같은 굵직한 것만 콘솔에
            bool notable = loco.Contains("Dodge") || loco.Contains("Launch") || loco.Contains("Dead") || loco.Contains("Air");
            Write($"[플레이어] 이동 {Empty(s_playerLoco)} → {loco}", notable);
            s_playerLoco = loco;
        }
        bool inv = s_player.IsInvincible;
        if (inv != s_playerInv)
        {
            Write($"[플레이어] 무적 {(inv ? "켜짐" : "꺼짐")}", false);
            s_playerInv = inv;
        }
    }

    private static void Snapshot()
    {
        if (s_player == null && s_boss == null) return;
        var ps = s_player != null ? s_player.RuntimeStats : null;
        var pp = s_player != null ? s_player.transform.position : Vector3.zero;
        var bp = s_boss != null ? s_boss.transform.position : Vector3.zero;
        string boss = s_boss != null
            ? $"보스 HP {HpPct(s_boss)} · {s_bossState}{StateDetail(s_boss)} · 패턴 {s_pattern}{(s_bossImmune ? " · 면역" : "")} · 위치 ({bp.x:0.0}, {bp.y:0.0}, {bp.z:0.0})"
            : "보스 없음";
        string player = s_player != null
            ? $"플레이어 HP {ps?.Hp}/{ps?.MaxHp} · {s_playerAct}/{s_playerLoco}{(s_playerInv ? " · 무적" : "")} · 위치 ({pp.x:0.0}, {pp.y:0.0}, {pp.z:0.0})"
            : "플레이어 없음";
        Write($"[스냅숏] {boss} || {player} · 거리 {Dist():0.0} m", false);
    }

    /// <summary>상태 내부 단계(_phase) · 애니메이터(상태 해시 · 진행 · 전환 중) · CC — 패턴이 어디서 멈췄는지 가르는 진단(10-01 화룡 N1).</summary>
    private static string StateDetail(MonsterBase boss)
    {
        var sb  = new StringBuilder();
        var fsm = FindField(typeof(MonsterBase), "_fsm")?.GetValue(boss);
        var cur = fsm != null ? FindField(fsm.GetType(), "_current")?.GetValue(fsm) : null;
        var phase = cur != null ? FindField(cur.GetType(), "_phase")?.GetValue(cur) : null;
        if (phase != null) sb.Append($"[{phase}]");
        var anim = boss.GetComponentInChildren<Animator>();
        if (anim != null && anim.isActiveAndEnabled)
        {
            var info = anim.GetCurrentAnimatorStateInfo(0);
            sb.Append($" 애니 {info.shortNameHash} {info.normalizedTime:0.00}{(anim.IsInTransition(0) ? " 전환중" : "")} 속도 {anim.speed:0.00}");
        }
        if (boss.Status != null && boss.Status.IsCcActive) sb.Append(" · CC");
        return sb.ToString();
    }

    private static void FlushPattern()
    {
        if (s_patternAt < 0f) return;
        string verdict = s_patApplied + s_patBlocked == 0 ? "판정 0 — 빗나감(맞을 위치가 아니었다)" : $"명중 {s_patApplied}(피해 {s_patDamage}) · 막힘 {s_patBlocked}";
        Write($"[패턴 끝] {s_pattern} — {verdict} · {Time.time - s_patternAt:0.0}초", true);
        s_patternAt = -1f; s_patApplied = s_patBlocked = s_patDamage = 0;
    }

    /// <summary>검증 메뉴가 전투 기록에 남기는 한 줄(예: 2페이지 후반 강제) — 기록 중이 아니면 무시.</summary>
    public static void Note(string line)
    {
        if (s_on) Write(line, false);
    }

    private static void Write(string line, bool console)
    {
        string text = $"[{Time.time - s_t0,7:0.00}] {line}";
        s_file?.WriteLine(text);
        if (console) Debug.Log("[BossLog] " + text);
    }

    /// <summary>보스 정면과 플레이어 방향 사이 각 — 판정이 보스 정면 기준인 부채꼴 패턴 진단용.</summary>
    private static float BossFacingAngle()
    {
        if (s_player == null || s_boss == null) return -1f;
        Vector3 d = s_player.transform.position - s_boss.transform.position;
        d.y = 0f;
        Vector3 f = s_boss.transform.forward;
        f.y = 0f;
        return Vector3.Angle(f, d);
    }

    private static float Dist()
    {
        if (s_player == null || s_boss == null) return -1f;
        Vector3 a = s_player.transform.position, b = s_boss.transform.position;
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private static string HpPct(MonsterBase b)
        => $"{b.CurrentHp}/{b.EffectiveMaxHp} ({100f * b.CurrentHp / Mathf.Max(1, b.EffectiveMaxHp):0}%)";

    private static string Empty(string s) => string.IsNullOrEmpty(s) ? "-" : s;

    private static string OutcomeText(PlayerController.DamageOutcome o) => o switch
    {
        PlayerController.DamageOutcome.Applied      => "명중",
        PlayerController.DamageOutcome.PerfectDodge => "막힘 — 저스트 회피",
        PlayerController.DamageOutcome.Invincible   => "막힘 — 무적(회피·피격 무적)",
        PlayerController.DamageOutcome.Negated      => "막힘 — 아이템 무효화",
        PlayerController.DamageOutcome.DeathNegated => "막힘 — 사망 무효",
        _                                           => o.ToString(),
    };

    private static string Attacker(GameObject go)
    {
        if (go == null) return "(가해자 미상)";
        if (s_boss != null && go.transform.IsChildOf(s_boss.transform)) return $"보스 본체({go.name})";
        return go.name;
    }

    // 리플렉션 — BossFightSimEditor와 같은 읽기(러너의 마지막 패턴 · FSM 현재 상태)
    private static bool ReadLastPattern(MonsterBase boss, out string name, out float time)
    {
        name = null; time = 0f;
        var runner = FindField(boss.GetType(), "_runner")?.GetValue(boss);
        if (runner == null) return false;
        var rt = runner.GetType();
        var so = (FindField(rt, "_lastPatternSO") ?? FindField(rt, "_lastFiredAttack"))?.GetValue(runner) as UnityEngine.Object;
        var tf = FindField(rt, "_lastPatternTime") ?? FindField(rt, "_lastFiredTime");
        if (so == null || tf == null) return false;
        name = so.name;
        time = (float)tf.GetValue(runner);
        return true;
    }

    private static string ReadState(MonsterBase boss)
    {
        var fsm = FindField(typeof(MonsterBase), "_fsm")?.GetValue(boss);
        var t = fsm?.GetType().GetProperty("CurrentType", Inst)?.GetValue(fsm) as Type;
        return t?.Name ?? "-";
    }

    private static string ReadField(object o, string field) => FindField(o.GetType(), field)?.GetValue(o)?.ToString() ?? "-";

    private static FieldInfo FindField(Type t, string name)
    {
        for (var cur = t; cur != null; cur = cur.BaseType)
        {
            var f = cur.GetField(name, Inst | BindingFlags.DeclaredOnly);
            if (f != null) return f;
        }
        return null;
    }

    // ── Event Handlers ─────────────────────────────────────────
    private static void OnDamageResolved(GameObject attacker, int raw, int final, PlayerController.DamageOutcome outcome)
    {
        if (!s_on) return;
        bool applied = outcome == PlayerController.DamageOutcome.Applied;
        if (applied) { s_patApplied++; s_patDamage += final; s_totApplied++; s_totDamage += final; }
        else         { s_patBlocked++; s_totBlocked++; }
        s_outcomes[outcome] = s_outcomes.TryGetValue(outcome, out int n) ? n + 1 : 1;
        if (s_stats.TryGetValue(s_pattern, out var st)) { if (applied) { st.Applied++; st.Damage += final; } else st.Blocked++; }

        var ps = s_player != null ? s_player.RuntimeStats : null;
        string since = s_patternAt >= 0f ? $"+{Time.time - s_patternAt:0.00}초" : "";
        string res = applied && final == 0 ? "명중(실드가 전부 흡수)" : OutcomeText(outcome);
        // 맞은 자리를 덮던 예고 가이드와 그 가이드가 떠 있던 시간 — 「가이드 밖」이면 예고 없이 맞았거나 가이드와 판정이 어긋났다
        string guide = s_player != null && BossGuideShapes.Covering(BossGuideShapes.Scan(), s_player.transform.position, 0.4f, out var cov)
            ? $"가이드 {BossGuideShapes.Describe(cov)} {cov.Age:0.00}초"
            : s_player != null ? $"가이드 밖({BossGuideShapes.NearestMiss(BossGuideShapes.Scan(), s_player.transform.position)} · 보스 정면에서 {BossFacingAngle():0}°)" : "가이드 밖";
        Write($"[공격] {res} · {Attacker(attacker)} · 패턴 {s_pattern} {since} · 피해 {raw} → {final} · {guide} · 플레이어 HP {ps?.Hp}/{ps?.MaxHp} · 거리 {Dist():0.0} m", true);
    }

    private static void OnHitTaken(HitWeight weight, Vector3 dir, int dmg)
    {
        if (!s_on) return;
        Write($"   └ 피격 연출 {weight}", false);
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode) Stop();
    }
}
