using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 보스 전투 시뮬레이션 — 보스 아레나에 들어간 상태에서 부른다(테스트 허브 → 대기방 → 아레나).
/// 플레이어를 정해진 거리(근접 3 m · 중거리 7 m · 원거리 11 m)에서 보스 둘레로 돌리며 일정 DPS로 때리고,
/// 전투 과정을 기록한다: 패턴 실행 · 패턴 시작 → 첫 피격까지 시간 · 피격 세기(약·중·강)와 피해 · 2초 최대 누적 ·
/// 보스가 화면 밖이거나 벽에 가린 시간 · HP 정체(게이트) · 사망 → 클리어 흐름 · 예외.
/// 등장 · 패턴별 첫 예고 · 페이즈 · 사망 · 팝업은 게임 안 ScreenCapture로 찍는다(MCP 스크린샷은 도메인 리로드를 부른다).
///
/// 무적은 쓰지 않는다 — 무적이면 TakeDamage가 맨 앞에서 끊겨 피격 이벤트가 안 온다.
/// 대신 HP가 60% 밑으로 떨어지면 가득 채운다(횟수 기록). 보스 내부(러너 · FSM · 휴면)는 리플렉션으로 읽기만 한다.
/// 결과: 프로젝트 Temp/boss_fight_sim/&lt;보스&gt;_&lt;행동&gt;_&lt;시각&gt;/ (summary.txt · hits.csv · patterns.csv · timeline.csv · 캡처).
///
/// 반응 모드(React …) — 패턴 하나씩 검증용: 예고 가이드(<see cref="BossGuideShapes"/>)가 뜨고 0.3초 뒤 걸어서 가장 가까운 밖으로 나간다
/// (회피 무적 없이 — 걸어서 못 피하면 회피를 써야 하는 패턴이다). 안전지대 표시가 뜨면 그 안으로 간다. 그리고 지금 조건 · 거리가 맞는데
/// 이번 판에 아직 안 나온 패턴을 보스가 쉴 차례에 하나씩 강제해 모든 패턴을 한 번씩 본다. 요약에 패턴별 「가이드 밖 피격」(예고 없이 ·
/// 가이드와 판정이 어긋남) · 맞은 순간 덮던 가이드가 떠 있던 시간(= 실제 예고 길이, 최소) · 최대 한 방(최대 HP 대비) · 미발동 패턴 목록.
/// </summary>
public static class BossFightSimEditor
{
    // ── Constants ──────────────────────────────────────────────
    private const string Root            = "RelicFairy/Boss/Fight Sim/";
    private const float  LogicStep       = 0.1f;
    private const float  CameraStep      = 0.25f;
    private const float  AttackInterval  = 0.5f;
    private const float  BearerReact     = 1f;     // 기사 악몽 「지휘」 — 기수가 선 뒤 알아채고 돌아서기까지(초)
    private const float  MaxSeconds      = 300f;   // 비스케일 — 팝업으로 시간이 멈춰도 끝난다
    private const float  HealBelow       = 0.60f;   // 리치 F4 실패 한 방이 최대 HP의 42% — 그 아래서 채우면 한 방에 죽는다
    private const float  StallSeconds    = 15f;
    private const float  StallGiveUp     = 35f;
    private const float  ReactStallGiveUp = 90f;   // 반응 모드 — 화룡 소환 게이트(휩쓸기 강제)가 35초를 넘는다
    private const int    MaxShots        = 24;
    private const float  RealDps         = 45f;    // 09-18 실측: 45분 런에 필요한 DPS 44 — 「현실 처치 시간」 계산용
    private const float  TargetFightSecs = 60f;    // 시뮬 DPS는 이 안에 끝나게 올린다(10-02 180 → 60 — 빠른 테스트)
    private const float  SimDpsFloor     = 135f;   // 시뮬 DPS 하한(10-02 45 → 135) — RealDps는 「현실 처치 시간」 계산에만 쓴다
    private const float  StrafeDegPerSec = 18f;
    private const float  MoveSpeed       = 6f;
    private const float  PlayerEyeY      = 1.4f;
    private const float  BurstWindow     = 2f;
    private const float  ReactionDelay   = 0.3f;   // 반응 모드 — 가이드를 보고 움직이기 시작할 때까지(사람 반응 + 입력)
    private const float  BodyRadius      = 0.4f;   // 가이드 안 판정 여유 = 플레이어 몸 반경
    private const float  EscapeMargin    = 0.9f;   // 피할 자리는 가이드 가장자리에서 이만큼 더 밖
    private const float  SweepGap        = 1f;     // 미발동 패턴 강제 사이 최소 간격(초)
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private enum Mode { Melee, Mid, Far, Close }

    private sealed class PatternStat
    {
        public int    Count;
        public int    Hits;
        public int    Damage;
        public int    Light, Medium, Heavy;
        public int    FirstHitSamples;
        public float  FirstHitSum;
        public float  FirstHitMin = float.MaxValue;
        public int    NoGuide;                       // 가이드 밖에서 맞음(예고 없이 · 가이드와 판정이 어긋남)
        public float  GuideAgeMin = float.MaxValue;  // 맞은 순간 덮던 가이드가 떠 있던 시간(최소) = 실제 예고 길이
        public int    MaxHit;                        // 가장 큰 한 방
    }

    // 흔들림 구간 — trauma가 켜졌다 꺼질 때까지(비스케일 시간). 감쇠 속도로 계산한 길이보다 한참 짧으면 중간에 끊긴 것.
    private sealed class ShakeTracker
    {
        public bool   On;
        public float  Start, FightT, Peak, Last, ExpectedEnd, Decay;
        public string Pattern;
        public int    Count, Short;
        public float  RatioSum;
        public readonly StringBuilder Csv = new("fightT,duration,peak,decayPerSec,expected,pattern\n");
    }

    // ── Static ─────────────────────────────────────────────────
    private static CancellationTokenSource s_cts;
    private static readonly List<string> s_errors = new();
    private static readonly StringBuilder s_escapeCsv = new();   // 가이드 보고 피하기 이동(시각 · 거리) — 2페이지 체감 점검(10-03 S3)
    private static BossStageHazard[] s_hazards = Array.Empty<BossStageHazard>();   // 무대 위험(가시 띠 · 흑염 띠 · 무대 붕괴) — 피할 곳(10-03)
    private static float s_hazardScanAt = -1f;
    private static int s_errorCount;
    // 에러 로그가 Error Pause를 걸면 시뮬 루프가 통째로 멈춘다(기사 흰 검 DOTS 에러) — 시뮬이 도는 동안만 풀고 기록한다
    private static int    s_unpauseCount;
    private static string s_lastError = "";
    private static string s_dir;   // 이번 판 결과 폴더 — 봉인 · 해방 장면 박자 촬영이 쓴다

    // 봉인 · 해방 장면 박자 촬영(10-03 봉인 연출 v2 점검) — 장면 시작 로그를 보고 정해진 박자에 게임 화면을 찍는다
    private static readonly (string tag, float at)[] SealBeats =
        { ("seal_1_cast", 0.9f), ("seal_2_chain3", 1.9f), ("seal_3_collapse", 2.9f), ("seal_4_bound", 4.6f), ("seal_5_dissolve", 6.3f), ("seal_6_gone", 7.5f) };
    private static readonly (string tag, float at)[] UnbindBeats =
        { ("unbind_1", 0.15f), ("unbind_2", 0.6f), ("unbind_3", 1.0f), ("unbind_4", 1.4f), ("unbind_5", 2.1f), ("unbind_6", 2.8f) };
    private static readonly (string tag, float at)[] FlushBeats =
        { ("flush_1", 0.1f), ("flush_2", 0.6f) };

    // ── Public Methods ─────────────────────────────────────────
    [MenuItem(Root + "Melee 3m (Play)")] public static void RunMelee() => Begin(Mode.Melee);
    [MenuItem(Root + "Mid 7m (Play)")]   public static void RunMid()   => Begin(Mode.Mid);
    [MenuItem(Root + "Far 11m (Play)")]  public static void RunFar()   => Begin(Mode.Far);
    [MenuItem(Root + "React Close 2m (Play)")] public static void RunReactClose() => Begin(Mode.Close, react: true);   // 숲 잡기처럼 2.5 m 안에서만 나오는 패턴
    [MenuItem(Root + "React Melee 3m (Play)")] public static void RunReactMelee() => Begin(Mode.Melee, react: true);
    [MenuItem(Root + "React Mid 7m (Play)")]   public static void RunReactMid()   => Begin(Mode.Mid,   react: true);
    // 봉인된 채 남은 보스를 「방을 나간다」로 치운다 — 챕터 게이트가 부르는 것과 같은 길(씬 로드 · 저장 없이 그 부분만)
    [MenuItem(Root + "Flush Sealed Boss (Play)")] public static void FlushSealed() => BossStoryScenes.FlushSealed();

    // ── Private Methods ────────────────────────────────────────
    private static void Begin(Mode mode, bool react = false)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[FightSim] 플레이 모드에서만 동작한다."); return; }
        s_cts?.Cancel();
        s_cts?.Dispose();
        s_cts = new CancellationTokenSource();
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        RunAsync(mode, react, s_cts.Token).Forget();
    }

    private static async UniTaskVoid RunAsync(Mode mode, bool react, CancellationToken ct)
    {
        s_errors.Clear();
        s_errorCount = 0;
        s_unpauseCount = 0;
        s_lastError = "";
        Application.logMessageReceived += OnLog;
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.update += OnEditorUpdate;
        PlayerController player = null;
        Action<HitWeight, Vector3, int> onHit = null;
        try
        {
            var runner  = UnityEngine.Object.FindFirstObjectByType<Managers>();
            var spawner = UnityEngine.Object.FindFirstObjectByType<BossSpawner>();
            // 기사 아레나(245 MB)는 셰이더 재컴파일 직후처럼 늦게 뜰 때가 있다 — 스포너를 30초까지 기다린다
            for (float waited = 0f; spawner == null && waited < 30f; waited += 0.5f)
            {
                await UniTask.Delay(500, ignoreTimeScale: true, cancellationToken: ct);
                spawner = UnityEngine.Object.FindFirstObjectByType<BossSpawner>();
            }
            player      = GameRunBootstrapper.Instance?.Run?.Player;
            var cam     = Camera.main;
            if (runner == null || spawner == null || player == null || cam == null)
            {
                Debug.LogWarning($"[FightSim] 준비 안 됨 — runner={runner != null} spawner={spawner != null} player={player != null} cam={cam != null}");
                return;
            }

            float radius = mode switch { Mode.Close => 2f, Mode.Melee => 3f, Mode.Mid => 7f, _ => 11f };
            var boss = spawner.SpawnedBoss;
            string bossName = boss != null ? Clean(boss.name) : "boss";
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim",   // Temp는 에디터 재시작 때 지워진다
                                     
                                      $"{bossName}_{mode}{(react ? "React" : "")}_{DateTime.Now:MMdd_HHmmss}");
            Directory.CreateDirectory(dir);
            s_dir = dir;

            // ── 기록 버퍼
            var hits      = new List<(float t, HitWeight w, int dmg, string pattern, float sincePattern, string guide, float guideAge)>();
            var patterns  = new Dictionary<string, PatternStat>();
            var timeline  = new StringBuilder("t,bossHp,playerHp,state,special,dist,bossOnScreen,bossOccluded,playerOccluded,playerAboveGround,bossBearing\n");
            var events    = new StringBuilder();
            var shotNames = new HashSet<string>();
            int shots = 0, heals = 0;
            string curPattern = "-";
            float  curPatternAt = -1f;
            bool   curPatternHit = true;
            float  lastPatternTime = -999f;
            var    patternGaps = new List<float>();

            onHit = (w, d, dmg) =>
            {
                float t = Time.time;
                float since = curPatternAt >= 0f ? t - curPatternAt : -1f;
                // 맞은 자리를 덮던 가이드 — 없으면 예고 없이 맞았거나 가이드와 판정이 어긋난 것
                bool covered = BossGuideShapes.Covering(BossGuideShapes.Scan(), player.transform.position, BodyRadius, out var cov);
                hits.Add((t, w, dmg, curPattern, since, covered ? BossGuideShapes.Describe(cov) : "-", covered ? cov.Age : -1f));
                if (!patterns.TryGetValue(curPattern, out var ps)) patterns[curPattern] = ps = new PatternStat();
                ps.Hits++; ps.Damage += dmg;
                ps.MaxHit = Mathf.Max(ps.MaxHit, dmg);
                if (covered) ps.GuideAgeMin = Mathf.Min(ps.GuideAgeMin, cov.Age); else ps.NoGuide++;
                if (w == HitWeight.Light) ps.Light++; else if (w == HitWeight.Heavy) ps.Heavy++; else ps.Medium++;
                if (!curPatternHit && since >= 0f)
                {
                    curPatternHit = true;
                    ps.FirstHitSamples++; ps.FirstHitSum += since;
                    ps.FirstHitMin = Mathf.Min(ps.FirstHitMin, since);
                }
            };
            player.OnHitTaken += onHit;
            BossCombatLog.Start(dir);   // 보스 행동 · 공격 판정 결과 · 플레이어 상태 로그(같은 폴더)
            BossGuideShapes.Reset();

            async UniTask Shot(string name)
            {
                if (shots >= MaxShots || !shotNames.Add(name)) return;
                shots++;
                await SaveShotAsync(runner, Path.Combine(dir, $"{shots:00}_{Safe(name)}.png"), ct);
            }

            void Event(string s)
            {
                string line = $"[{Time.time:0.0}] {s}";
                events.AppendLine(line);
                Debug.Log($"[FightSim] {line}");
            }

            // ── 1) 보스 깨우기 — 입구 트리거는 Enter 뒤 Exit가 있어야 발동한다(사이에 물리 스텝)
            TestHubDebugMenu.StepIntoBossEntrance();
            await UniTask.Delay(600, ignoreTimeScale: true, cancellationToken: ct);
            TestHubDebugMenu.StepPastBossEntrance();
            float wakeStart = Time.unscaledTime;
            while ((boss = spawner.SpawnedBoss) == null || !boss.gameObject.activeInHierarchy)
            {
                if (Time.unscaledTime - wakeStart > 20f) { Event("보스가 20초 안에 나타나지 않음"); break; }
                await UniTask.Delay(200, ignoreTimeScale: true, cancellationToken: ct);
            }
            if (boss == null) return;
            bossName = Clean(boss.name);
            // 기사는 유리벽 너머 단상에 서 있다 — 보스 둘레를 돌면 시뮬 플레이어가 벽을 뚫고 단상에 올라가 격자 패턴을 못 잰다(09-28)
            // → 플레이어 구역 가운데(PyramidStrikeAnchor)를 돌고, 공격은 거리와 무관하게 넣는다.
            Transform zoneCenter = (boss as DeathKnightBossMonster)?.PyramidStrikeAnchor;
            Event($"보스 등장 {bossName} · HP {boss.CurrentHp}/{boss.EffectiveMaxHp} · 행동 {mode}({radius} m)");
            await UniTask.Delay(1500, ignoreTimeScale: true, cancellationToken: ct);
            await Shot("entrance");

            int   maxHp     = Mathf.Max(1, boss.EffectiveMaxHp);
            float simDps    = Mathf.Max(SimDpsFloor, maxHp / TargetFightSecs);
            Event($"시뮬 DPS {simDps:0} (현실 DPS {RealDps} 기준 처치 시간 {maxHp / RealDps:0}초)");
            // 보스 피해식 = max(1, 한 방 × 보스 배율 − 방어) — 시뮬은 방어·배율을 보정해 넣고, 실제 한 방이 얼마나 남는지만 기록한다.
            float bossDef   = ReadFloat(boss, "_baseDefense", 0f) * ReadFloat(boss, "_defenseMulti", 1f);
            int   playerHit = player.RuntimeStats != null ? player.RuntimeStats.MeleeAttack : 0;
            Event($"보스 방어 {bossDef:0} · 플레이어 근접 공격력 {playerHit} → 한 방이 방어 차감 후 {Mathf.Max(1f, playerHit - bossDef):0}");

            var hud        = boss as IBossHudSource;
            int lastPage   = hud != null ? hud.HudPage : -1;
            bool halfShot  = false;
            float t0          = Time.time;
            float startUnscaled = Time.unscaledTime;
            float nextLogic = 0f, nextCam = 0f, nextAttack = 0f;
            float theta     = 180f;                      // 남쪽(카메라 쪽)에서 시작
            int   lastHp    = boss.CurrentHp;
            float lastHpDropAt = Time.time, lastDamageAt = -99f;
            bool  stallLogged = false, addsKilled = false, forced = false;
            float deathAt   = -1f, deathAtUnscaled = -1f, nextDeathCheck = 0f;
            bool  popupSeen = false;
            string lastState = "";
            int camSamples = 0, offScreen = 0, bossOcc = 0, playerOcc = 0;
            int occMask = LayerMask.GetMask("Wall", "Default");
            var shake   = new ShakeTracker();
            float nextAdds = 0f;
            bool    hasEscape = false;
            Vector3 escapeTarget = Vector3.zero;
            int     escapes = 0;
            s_escapeCsv.Clear().AppendLine("t,dist");
            float   nextSweep = 0f;
            var     forcedNames = new HashSet<string>();
            object  runnerObj   = FindField(boss.GetType(), "_runner")?.GetValue(boss);   // 보스가 풀로 돌아가도 러너 · 설정은 남는다(미발동 목록)
            Vector3 arenaCenter = zoneCenter != null ? zoneCenter.position : boss.transform.position;
            int     playerMaxHp = Mathf.Max(1, player.RuntimeStats?.MaxHp ?? 1);

            while (!ct.IsCancellationRequested)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);   // FixedUpdate는 timeScale 0(팝업)에서 멈춘다
                // 사망 뒤엔 보스가 풀로 돌아가도(파괴) 개화 팝업까지 기다린다
                if (boss == null && deathAt < 0f) { Event("보스 오브젝트가 사라짐"); break; }
                float now = Time.time;
                if (Time.unscaledTime - startUnscaled > MaxSeconds) { Event($"제한 시간 {MaxSeconds}초 초과 — 종료"); break; }
                TrackShake(shake, now - t0, curPattern);
                float dist = 0f;
                if (boss == null) goto AfterBoss;

                // ── 이동: 보스 둘레를 천천히 돈다(바닥이 없으면 반경을 줄인다)
                if (!boss.IsDead)
                {
                    theta += StrafeDegPerSec * Time.deltaTime;
                    Vector3 bp = zoneCenter != null ? zoneCenter.position : boss.transform.position;
                    float   r  = zoneCenter != null ? Mathf.Min(radius, 8f) : radius;
                    Vector3 target = bp;
                    bool    grounded = false;
                    for (int k = 0; k < 6; k++)
                    {
                        target = bp + new Vector3(Mathf.Sin(theta * Mathf.Deg2Rad), 0f, Mathf.Cos(theta * Mathf.Deg2Rad)) * r;
                        if (TryGroundY(target, player, out float gy)) { target.y = gy; grounded = true; break; }
                        r *= 0.75f;
                    }
                    // 바닥을 못 찾으면 높이는 지금 그대로 — 보스 높이(비행 중 7 m)를 따라 떠오르지 않게
                    if (!grounded) target.y = player.transform.position.y;
                    Vector3 cur  = player.transform.position;
                    Vector3 next = Vector3.MoveTowards(cur, target, MoveSpeed * Time.deltaTime);
                    if (react)
                    {
                        // 반응 모드 — 본 가이드(0.3초 넘게 떠 있던 것) 안이면 걸어서 가장 가까운 밖으로 · 안전지대가 뜨면 그 안으로.
                        // 빙결 · 넉백 · 띄움 중엔 못 움직인다(실제 플레이어처럼).
                        var   shapes = BossGuideShapes.Scan();
                        float speed  = ReactSpeed(player) * Time.deltaTime;
                        if (BossGuideShapes.TryNearestSafe(shapes, cur, ReactionDelay, out Vector3 safeAt, out bool inSafe))
                        {
                            next = inSafe ? cur : Vector3.MoveTowards(cur, safeAt, speed);
                            if (!inSafe && (!hasEscape || (safeAt - escapeTarget).sqrMagnitude > 1f))
                            {
                                // 새 안전 원으로 옮겨 가기 시작 — 「자리 읽기」 이동으로 센다(10-03 S3)
                                escapes++;
                                s_escapeCsv.AppendLine($"{now - t0:0.00},{Vector3.Distance(cur, safeAt):0.0}");
                                escapeTarget = safeAt;
                            }
                            hasEscape = !inSafe;   // 안전지대 안에선 다시 때린다(숲 심장 · 리치 비처럼 안에서 딜하는 패턴)
                        }
                        else if (BossGuideShapes.InsideAny(shapes, cur, BodyRadius, ReactionDelay) || InHazard(cur))
                        {
                            if (!hasEscape || BossGuideShapes.InsideAny(shapes, escapeTarget, EscapeMargin, 0f) || InHazard(escapeTarget))
                            {
                                hasEscape = TryFindEscape(shapes, cur, player, arenaCenter, out escapeTarget);
                                if (hasEscape)
                                {
                                    escapes++;
                                    s_escapeCsv.AppendLine($"{now - t0:0.00},{Vector3.Distance(cur, escapeTarget):0.0}");
                                }
                            }
                            next = hasEscape ? Vector3.MoveTowards(cur, escapeTarget, speed) : cur;
                        }
                        else
                        {
                            hasEscape = false;
                            if (BossGuideShapes.InsideAny(shapes, next, BodyRadius, 0f) || InHazard(next)) next = cur;   // 가이드 · 무대 위험 안으로 돌아 들어가지 않는다
                        }
                    }
                    MovePlayer(player, next);
                }

                // ── 공격: 사거리 안이면 일정 DPS
                dist = Vector3.Distance(Flat(player.transform.position), Flat(boss.transform.position));
                if (!boss.IsDead && now >= nextAttack)
                {
                    nextAttack = now + AttackInterval;
                    var bearer = StandingBearer(boss);   // 기사 악몽 「지휘」 — 기수가 서 있으면 기수부터 벤다
                    if (bearer != null && bearer.StoodSeconds >= BearerReact && !hasEscape)
                    {
                        if (bearer.SimulatePlayerHit(player.gameObject) && bearer.HitsLeft == 0)
                            Event($"기수 처치 — {bearer.StoodSeconds:0.0}초 버팀");
                    }
                    else if ((zoneCenter != null || dist <= radius + 2f) && !hasEscape)   // 피하는 중엔 때리지 않는다
                    {
                        float m = PreDefenseMult(boss);
                        boss.TakeDamage((simDps * AttackInterval + bossDef) / m, player.gameObject, 0.2f);
                        lastDamageAt = now;
                    }
                }

                // ── 논리 틱
                if (now >= nextLogic)
                {
                    nextLogic = now + LogicStep;

                    // HP 유지(무적 대신)
                    var stats = player.RuntimeStats;
                    if (stats != null && stats.Hp <= 0) { Event($"⚠️ 플레이어 사망 — 한 번에 최대 HP의 {100f * (1f - HealBelow):0}% 넘게 들어왔다"); break; }
                    if (stats != null && stats.MaxHp > 0 && stats.Hp < stats.MaxHp * HealBelow)
                    {
                        stats.SetHp(stats.MaxHp);
                        heals++;
                    }

                    // 패턴 실행 감지(리플렉션 — 러너의 마지막 패턴 · 시각)
                    if (ReadLastPattern(boss, out string pname, out float ptime) && ptime > lastPatternTime + 0.001f)
                    {
                        if (lastPatternTime > 0f) patternGaps.Add(ptime - lastPatternTime);
                        lastPatternTime = ptime;
                        curPattern = pname; curPatternAt = now; curPatternHit = false;
                        if (!patterns.TryGetValue(pname, out var ps)) patterns[pname] = ps = new PatternStat();
                        ps.Count++;
                        if (ps.Count == 1) { Event($"패턴 첫 실행 {pname}"); ShotLater(Shot, "pattern_" + pname, 450, ct); }
                    }

                    // 반응 모드 — 지금 조건 · 거리가 맞는데 이번 판에 아직 안 나온 패턴을 보스가 쉴 차례에 하나씩 강제한다(모든 패턴을 한 번씩 본다)
                    // 등장 연출(Dormant) 중이거나 보스가 아직 스스로 패턴을 한 번도 안 냈으면 강제하지 않는다 — 연출이 끊겨 러너가 멈췄다(10-01)
                    if (react && !boss.IsDead && !boss.IsInSpecialState && Time.time >= nextSweep
                        && patterns.Count > forcedNames.Count && !lastState.Contains("Dormant") && !lastState.Contains("Entrance")
                        && GameRunBootstrapper.Instance?.Run?.InCutscene != true
                        && TryForceUnseen(runnerObj, patterns, out string forcedName))
                    {
                        nextSweep = Time.time + SweepGap;
                        forcedNames.Add(forcedName);
                        Event($"미발동 패턴 강제 {forcedName}");
                    }

                    // FSM 상태 · 페이즈
                    string state = ReadState(boss);
                    if (state != lastState)
                    {
                        if (IsPhaseLike(state)) { Event($"상태 {lastState} → {state}"); ShotLater(Shot, "state_" + state, 600, ct); }
                        lastState = state;
                    }
                    if (hud != null && hud.HudPage != lastPage)
                    {
                        Event($"HUD 페이지 {lastPage} → {hud.HudPage}");
                        lastPage = hud.HudPage;
                        ShotLater(Shot, "page_" + lastPage, 900, ct);
                    }
                    if (!halfShot && boss.CurrentHp <= maxHp / 2) { halfShot = true; Event("HP 50%"); await Shot("hp50"); }

                    // HP 정체(게이트) — 때리는데 안 깎인다
                    int hp = boss.CurrentHp;
                    if (hp < lastHp) { lastHp = hp; lastHpDropAt = now; stallLogged = false; }
                    else if (hp > lastHp) { lastHp = hp; lastHpDropAt = now; }   // 다시 찼다(2페이지 차오름 · 검증 메뉴) — 새 기준에서 정체를 잰다(09-28: 가득 찬 뒤 줄곧 「정체」로 봐 소환물 처치가 보스에 넘어갔다)
                    if (!boss.IsDead && now - lastDamageAt < 1.5f)
                    {
                        float stalled = now - lastHpDropAt;
                        if (stalled > StallSeconds && !stallLogged)
                        {
                            stallLogged = true;
                            Event($"HP 정체 {stalled:0}초 @ {100f * hp / maxHp:0}% · 상태 {state} · 특수 {boss.IsInSpecialState}");
                            await Shot($"stall_{100 * hp / maxHp}");
                        }
                        // 보스가 안 깎이면 실제 플레이어처럼 소환물(미니 드래곤·기둥·검·봉인석 등)을 친다 — 3초마다
                        if (stalled > 5f && Time.unscaledTime >= nextAdds)
                        {
                            nextAdds = Time.unscaledTime + 3f;
                            int n = KillAdds(boss, player);
                            if (n > 0 || !addsKilled) Event($"정체 {stalled:0}초 — 소환물 {n}개 처치");
                            addsKilled = true;
                        }
                        if (stalled > (react ? ReactStallGiveUp : StallGiveUp) && !forced)   // 반응 모드는 게이트 길이 자체를 잰다
                        {
                            forced = true;
                            Event($"정체 {stalled:0}초 — 강제 처치로 넘어간다(게이트 미해결)");
                            TestHubDebugMenu.ForceKillBoss();
                        }
                    }

                    // 사망 → 클리어 흐름
                    if (boss.IsDead && deathAt < 0f)
                    {
                        deathAt = now;
                        deathAtUnscaled = Time.unscaledTime;
                        Event($"보스 사망 — 전투 {now - t0:0.0}초");
                        ShotLater(Shot, "death_1s", 1000, ct);
                        ShotLater(Shot, "death_4s", 4000, ct);
                    }
                }

                // ── 사망 뒤 — 팝업이 시간을 멈춰도(timeScale 0) 비스케일 시간으로 재고 끝낸다
                AfterBoss:
                if (deathAt >= 0f && Time.unscaledTime >= nextDeathCheck)
                {
                    nextDeathCheck = Time.unscaledTime + 0.25f;
                    if (!popupSeen && FindByTypeName("UI_RelicPartDraftPopup") != null)
                    {
                        popupSeen = true;
                        Event($"유물 개화 팝업 — 사망 뒤 {Time.unscaledTime - deathAtUnscaled:0.0}초(실시간)");
                        await Shot("draft_popup");
                    }
                    if (Time.unscaledTime - deathAtUnscaled > 20f) break;   // 처치 뒤 끝 장면(상한 10초) · 기억의 빛 · 회상 뒤에 창이 뜬다 — 기사 ~14초(10-03 d6)
                }

                // ── 카메라: 보스가 화면에 있나 · 가렸나 · 플레이어가 가렸나
                if (now >= nextCam && boss != null && !boss.IsDead)
                {
                    nextCam = now + CameraStep;
                    camSamples++;
                    Vector3 bc = boss.transform.position + Vector3.up * 1.5f;
                    Vector3 vp = cam.WorldToViewportPoint(bc);
                    bool on = vp.z > 0f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f;
                    if (!on) offScreen++;
                    bool bOcc = Physics.Linecast(cam.transform.position, bc, occMask, QueryTriggerInteraction.Ignore);
                    bool pOcc = Physics.Linecast(cam.transform.position, player.transform.position + Vector3.up * PlayerEyeY, occMask, QueryTriggerInteraction.Ignore);
                    if (bOcc) bossOcc++;
                    if (pOcc) playerOcc++;
                    // 부양 확인(지면 위 높이) · 보스가 카메라 정면에서 몇 도 옆인가(화면 밖 원인 가르기)
                    float above = TryGroundY(player.transform.position, player, out float gy2) ? player.transform.position.y - gy2 : -1f;
                    Vector3 toBoss = boss.transform.position - cam.transform.position; toBoss.y = 0f;
                    Vector3 camF   = cam.transform.forward; camF.y = 0f;
                    float bearing  = Vector3.SignedAngle(camF, toBoss, Vector3.up);
                    timeline.AppendLine($"{now - t0:0.00},{boss.CurrentHp},{player.RuntimeStats?.Hp},{lastState},{boss.IsInSpecialState},{dist:0.0},{on},{bOcc},{pOcc},{above:0.00},{bearing:0}");
                }
            }

            // ── 보고서
            WriteReports(dir, bossName, mode, radius, maxHp, simDps, t0, deathAt, popupSeen, heals, hits, patterns,
                         patternGaps, camSamples, offScreen, bossOcc, playerOcc, timeline, events, forced, shake,
                         react, escapes, forcedNames, NotRun(runnerObj, patterns), playerMaxHp);
            Debug.Log($"[FightSim] 완료 — {dir}");
        }
        catch (OperationCanceledException)
        {
            Debug.Log("[FightSim] 취소 — 플레이가 끝났다.");
        }
        finally
        {
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= OnEditorUpdate;
            if (player != null && onHit != null) player.OnHitTaken -= onHit;
            BossCombatLog.Stop();
        }
    }

    private static void WriteReports(string dir, string bossName, Mode mode, float radius, int maxHp, float simDps, float t0,
                                     float deathAt, bool popupSeen, int heals,
                                     List<(float t, HitWeight w, int dmg, string pattern, float sincePattern, string guide, float guideAge)> hits,
                                     Dictionary<string, PatternStat> patterns, List<float> gaps,
                                     int camSamples, int offScreen, int bossOcc, int playerOcc,
                                     StringBuilder timeline, StringBuilder events, bool forced, ShakeTracker shake,
                                     bool react, int escapes, HashSet<string> forcedNames, List<string> notRun, int playerMaxHp)
    {
        float fight = (deathAt > 0f ? deathAt : Time.time) - t0;
        int total = hits.Sum(h => h.dmg);
        int burst = 0;
        for (int i = 0; i < hits.Count; i++)
        {
            int sum = 0;
            for (int j = i; j < hits.Count && hits[j].t - hits[i].t <= BurstWindow; j++) sum += hits[j].dmg;
            burst = Mathf.Max(burst, sum);
        }
        var sb = new StringBuilder();
        sb.AppendLine($"보스 {bossName} · 행동 {mode}({radius} m) · 최대 HP {maxHp} · 시뮬 DPS {simDps:0}");
        sb.AppendLine($"전투 {fight:0.0}초 {(deathAt > 0f ? "(처치)" : "(미처치)")}{(forced ? " · ⚠️ 정체로 강제 처치" : "")} · 현실 DPS {RealDps} 기준 처치 {maxHp / RealDps:0}초");
        sb.AppendLine($"피격 {hits.Count}회 · 피해 {total} · 초당 {(fight > 0 ? total / fight : 0):0.0} · 2초 최대 {burst} · HP 채움 {heals}회");
        sb.AppendLine($"세기  약 {hits.Count(h => h.w == HitWeight.Light)} · 중 {hits.Count(h => h.w == HitWeight.Medium)} · 강 {hits.Count(h => h.w == HitWeight.Heavy)}");
        sb.AppendLine($"패턴 간격 평균 {(gaps.Count > 0 ? gaps.Average() : 0):0.00}초 · 최소 {(gaps.Count > 0 ? gaps.Min() : 0):0.00}초 · 실행 {patterns.Values.Sum(p => p.Count)}회 · 종류 {patterns.Count(p => p.Value.Count > 0)}");
        if (camSamples > 0)
            sb.AppendLine($"카메라  보스 화면 밖 {100f * offScreen / camSamples:0}% · 보스 가림 {100f * bossOcc / camSamples:0}% · 플레이어 가림 {100f * playerOcc / camSamples:0}%");
        sb.AppendLine($"클리어  사망 {(deathAt > 0f ? "예" : "아니오")} · 유물 개화 팝업 {(popupSeen ? "뜸" : "안 뜸")}");
        if (shake.Count > 0)
            sb.AppendLine($"흔들림  {shake.Count}회 · 실제/계산 길이 평균 {shake.RatioSum / shake.Count:0.00} · 계산의 60% 미만으로 끝남 {shake.Short}회 (shakes.csv)");
        if (react) sb.AppendLine($"반응 모드  가이드 보고 피하기 {escapes}회 · 미발동 패턴 강제 {forcedNames.Count}종 · 가이드 밖 피격 {hits.Count(h => h.guide == "-")}회");
        sb.AppendLine($"예외·오류 {s_errorCount}건");
        if (s_unpauseCount > 0) sb.AppendLine($"   ⚠️ Error Pause {s_unpauseCount}회 해제 — 직전 에러: {s_lastError}");
        foreach (var e in s_errors.Take(10)) sb.AppendLine("   " + e);
        sb.AppendLine();
        sb.AppendLine("패턴                                   실행  피격  피해   약/중/강   첫 피격(평균·최소)  가이드밖  예고(최소)  최대 한 방");
        foreach (var kv in patterns.OrderByDescending(p => p.Value.Count))
        {
            var p = kv.Value;
            string fh = p.FirstHitSamples > 0 ? $"{p.FirstHitSum / p.FirstHitSamples:0.00} · {p.FirstHitMin:0.00}" : "-";
            string ga = p.GuideAgeMin < float.MaxValue ? $"{p.GuideAgeMin:0.00}" : "-";
            string mx = p.MaxHit > 0 ? $"{p.MaxHit}({100f * p.MaxHit / playerMaxHp:0}%)" : "-";
            sb.AppendLine($"{kv.Key,-38} {p.Count,4} {p.Hits,5} {p.Damage,6}   {p.Light}/{p.Medium}/{p.Heavy,-6} {fh,-18} {p.NoGuide,6}  {ga,9}  {mx}{(forcedNames.Contains(kv.Key) ? "  (강제)" : "")}");
        }
        if (notRun.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"── 미발동 {notRun.Count}종(조건 키 · 패턴)");
            foreach (var n in notRun) sb.AppendLine("   " + n);
        }
        sb.AppendLine();
        sb.AppendLine("── 사건");
        sb.Append(events);
        File.WriteAllText(Path.Combine(dir, "summary.txt"), sb.ToString(), Encoding.UTF8);

        var hcsv = new StringBuilder("t,weight,damage,pattern,sincePatternStart,guide,guideAge\n");
        foreach (var h in hits) hcsv.AppendLine($"{h.t - t0:0.00},{h.w},{h.dmg},{h.pattern},{h.sincePattern:0.00},{h.guide},{h.guideAge:0.00}");
        File.WriteAllText(Path.Combine(dir, "hits.csv"), hcsv.ToString(), Encoding.UTF8);
        File.WriteAllText(Path.Combine(dir, "timeline.csv"), timeline.ToString(), Encoding.UTF8);
        File.WriteAllText(Path.Combine(dir, "shakes.csv"), shake.Csv.ToString(), Encoding.UTF8);
        File.WriteAllText(Path.Combine(dir, "escapes.csv"), s_escapeCsv.ToString(), Encoding.UTF8);
    }

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

    /// <summary>
    /// 러너가 쉬는 차례(쉬는 시간 0 · 기사 콤보 큐 빔)에 지금 조건 · 거리가 맞는데 이번 판에 아직 안 나온 패턴 하나를 낸다.
    /// 러너 내부 실행 경로(BossPatternRunner.ExecutePattern · DKComboRunner.FireNextFromQueue)를 리플렉션으로 그대로 부른다.
    /// </summary>
    private static bool TryForceUnseen(object runner, Dictionary<string, PatternStat> seen, out string name)
    {
        name = null;
        if (runner == null) return false;
        var rt = runner.GetType();
        if ((FindField(rt, "_patternBreakCooldown") ?? FindField(rt, "_breakCooldown"))?.GetValue(runner) is float cd && cd > 0.05f) return false;
        var queue = FindField(rt, "_comboQueue")?.GetValue(runner) as Queue<BossPatternSO>;
        if (queue != null && queue.Count > 0) return false;
        if (FindField(rt, "_ctx")?.GetValue(runner) is not BossPatternContext ctx) return false;
        foreach (var p in Candidates(runner, ctx, includeAll: false))
        {
            if (p == null || seen.ContainsKey(p.name) || p.GetRuntimeState() == null || !p.Available(ctx)) continue;
            if (queue == null)
                rt.GetMethod("ExecutePattern", Inst)?.Invoke(runner, new object[] { p });
            else
            {
                queue.Enqueue(p);
                rt.GetMethod("FireNextFromQueue", Inst)?.Invoke(runner, null);
            }
            name = p.name;
            return true;
        }
        return false;
    }

    /// <summary>설정의 패턴 — 기사 콤보 SO는 그 콤보가 뽑는 공격 풀로 푼다. includeAll = 조건 · 강제 항목 무시(미발동 목록용).</summary>
    private static IEnumerable<BossPatternSO> Candidates(object runner, BossPatternContext ctx, bool includeAll)
    {
        var config = FindField(runner.GetType(), "_config")?.GetValue(runner) as BossConfigSO;
        if (config?.patternEntries == null) yield break;
        foreach (var e in config.patternEntries)
        {
            if (e?.patterns == null) continue;
            if (!includeAll && (e.forceExecute || !e.EvaluateConditions(ctx))) continue;
            foreach (var p in e.patterns)
            {
                if (p is DKComboConfigSO combo) { foreach (var a in ComboPool(runner, combo, includeAll)) yield return a; }
                else yield return p;
            }
        }
    }

    private static IEnumerable<BossPatternSO> ComboPool(object runner, DKComboConfigSO combo, bool includeAll)
    {
        var rt = runner.GetType();
        List<BossPatternSO> Pool(string f) => FindField(rt, f)?.GetValue(runner) as List<BossPatternSO> ?? new List<BossPatternSO>();
        if (includeAll) return Pool("_basicPool").Concat(Pool("_phase1AreaPool")).Concat(Pool("_phase2AreaPool"));
        bool phase2 = FindField(rt, "_isPhase2")?.GetValue(runner) is Func<bool> f && f();
        var pool = !combo.usePhase1Position ? Pool("_basicPool") : phase2 ? Pool("_phase2AreaPool") : Pool("_phase1AreaPool");
        return pool.Count > 0 ? pool : Pool("_phase1AreaPool");
    }

    /// <summary>설정에 있는데 이번 판에 한 번도 안 나온 패턴 — 「[조건 키] 이름」.</summary>
    private static List<string> NotRun(object runner, Dictionary<string, PatternStat> seen)
    {
        var list = new List<string>();
        var config = runner != null ? FindField(runner.GetType(), "_config")?.GetValue(runner) as BossConfigSO : null;
        if (config?.patternEntries == null) return list;
        var added = new HashSet<string>();
        foreach (var e in config.patternEntries)
        {
            if (e?.patterns == null) continue;
            string keys = e.conditions != null && e.conditions.Count > 0 ? string.Join("+", e.conditions) : "항상";
            foreach (var p in e.patterns)
            {
                var flat = p is DKComboConfigSO combo ? ComboPool(runner, combo, includeAll: true) : new[] { p };
                foreach (var a in flat)
                    if (a != null && !a.EraLocked && !seen.ContainsKey(a.name) && added.Add(a.name))   // 봉인기의 해방기 전용 기술은 「미발동」이 아니다
                        list.Add($"[{keys}{(e.forceExecute ? " · 강제" : "")}] {a.name}");
            }
        }
        return list;
    }

    /// <summary>반응 모드 걸음 속도 = 캐릭터 기본 이동 × 이동 속도 배율 × 슬로우. 빙결 · 넉백 · 공중(띄움) 중엔 0.</summary>
    private static float ReactSpeed(PlayerController p)
    {
        object st = FindField(typeof(PlayerController), "_status")?.GetValue(p);
        var    stt = st?.GetType();
        if (stt != null && (stt.GetProperty("IsFrozen", Inst)?.GetValue(st) is true || stt.GetProperty("IsKnockback", Inst)?.GetValue(st) is true)) return 0f;
        string loco = p.LocoSM != null ? p.LocoSM.CurrentId.ToString() : "";
        if (loco.Contains("Launch") || loco.Contains("Air") || loco.Contains("Dead")) return 0f;
        float slow  = stt?.GetProperty("SlowScale", Inst)?.GetValue(st) is float s ? s : 1f;
        float baseV = p.CharacterData != null && p.CharacterData.baseMoveSpeed > 0.01f ? p.CharacterData.baseMoveSpeed : 5f;
        float mult  = p.RuntimeStats != null ? p.RuntimeStats.MoveSpeedMultiplier : 1f;
        return baseV * mult * slow;
    }

    /// <summary>모든 가이드 밖이면서 바닥이 있고 아레나 안(NavMesh)인 가장 가까운 자리 — 같은 거리면 아레나 가운데 쪽. NavMesh가 없으면 바닥만 본다.</summary>
    /// <summary>무대 위험 안인가 — 0.5초마다 다시 찾는다(위험은 페이지 전환 · 간판 때 생긴다).</summary>
    private static bool InHazard(Vector3 p)
    {
        if (Time.unscaledTime >= s_hazardScanAt)
        {
            s_hazardScanAt = Time.unscaledTime + 0.5f;
            s_hazards = UnityEngine.Object.FindObjectsByType<BossStageHazard>(FindObjectsSortMode.None);
        }
        foreach (var h in s_hazards)
            if (h != null && h.Contains(p)) return true;
        return false;
    }

    private static bool TryFindEscape(List<BossGuideShapes.Shape> shapes, Vector3 cur, PlayerController player, Vector3 center, out Vector3 target)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            for (float r = 1f; r <= 14f; r += 1f)
            {
                bool  found = false;
                float best  = float.MaxValue;
                target = cur;
                for (int k = 0; k < 16; k++)
                {
                    float   a = k * 22.5f * Mathf.Deg2Rad;
                    Vector3 c = cur + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * r;
                    if (BossGuideShapes.InsideAny(shapes, c, EscapeMargin, 0f)) continue;
                    if (InHazard(c)) continue;   // 무대 위험 안으로는 피하지 않는다
                    if (!TryGroundY(c, player, out float gy)) continue;
                    if (pass == 0 && !NavMesh.SamplePosition(c, out _, 1.5f, NavMesh.AllAreas)) continue;
                    c.y = gy;
                    float score = (c - center).sqrMagnitude;
                    if (score < best) { best = score; target = c; found = true; }
                }
                if (found) return true;
            }
        }
        target = cur;
        return false;
    }

    private static string ReadState(MonsterBase boss)
    {
        var fsm = FindField(typeof(MonsterBase), "_fsm")?.GetValue(boss);
        var t = fsm?.GetType().GetProperty("CurrentType", Inst)?.GetValue(fsm) as Type;
        return t?.Name ?? "-";
    }

    private static bool IsPhaseLike(string state)
    {
        if (string.IsNullOrEmpty(state)) return false;
        return state.Contains("Phase") || state.Contains("Transition") || state.Contains("Entry") ||
               state.Contains("Enrage") || state.Contains("Seal") || state.Contains("Stagger") ||
               state.Contains("Death") || state.Contains("Die") || state.Contains("Retreat");
    }

    /// <summary>이 기사의 서 있는 환영 기수(악몽 「지휘」) — 없으면 null.</summary>
    private static DKStandardBearer StandingBearer(MonsterBase boss)
    {
        var list = DKStandardBearer.Active;
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null && list[i].IsStanding && list[i].Boss == boss) return list[i];
        return null;
    }

    private static int KillAdds(MonsterBase boss, PlayerController player)
    {
        int n = 0;
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
        {
            if (mb == boss || mb.Grade == MonsterGrade.Boss || mb.IsDead || mb.CurrentHp <= 0) continue;
            mb.TakeDamage(mb.CurrentHp * 10f + 100000f, player.gameObject, 0f);
            n++;
        }
        // MonsterBase가 아닌 피격 대상 — 화룡 미니 드래곤·기사 영혼 기둥·떠 있는 검·리치 봉인석 등(점화형은 여러 번 쳐야 해 3회)
        foreach (var c in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (c == null || !c.isActiveAndEnabled || c is MonsterBase || c is PlayerController || c is not IDamageable d) continue;
            if (c.transform.IsChildOf(player.transform) || (boss != null && c.transform.IsChildOf(boss.transform))) continue;
            if (c is DKResonanceStone) continue;   // 공명석은 소환물이 아니라 기사에게 피해를 넘기는 통로 — 10만을 넣으면 기사가 한 번에 죽는다(09-28 2페이지 바닥 0)
            if (c is DKStandardBearer) continue;   // 환영 기수(악몽 「지휘」)는 타격 수로 쓰러진다 — 공격 박자에서 따로 친다
            for (int i = 0; i < 3; i++) d.TakeDamage(100000f, player.gameObject, 0f);
            n++;
        }
        return n;
    }

    private static readonly FieldInfo s_shakeExtField = typeof(HitFeelService).GetField("_shakeExt", BindingFlags.Static | BindingFlags.NonPublic);

    private static void TrackShake(ShakeTracker s, float fightT, string pattern)
    {
        var ext = s_shakeExtField?.GetValue(null) as CameraShakeExtension;
        if (ext == null) return;
        float u  = Time.unscaledTime;
        float tr = ReadFloat(ext, "_trauma", 0f);
        if (tr > 0.001f)
        {
            float decay = Mathf.Max(0.01f, ReadFloat(ext, "_decayPerSecond", 4f));
            if (!s.On) { s.On = true; s.Start = u; s.FightT = fightT; s.Peak = 0f; s.Last = 0f; s.ExpectedEnd = u; s.Pattern = pattern; }
            if (tr > s.Last + 0.001f)   // 새로 걸렸거나 더 센 흔들림이 덮었다 — 남은 길이 = trauma / 감쇠
            {
                s.Peak = Mathf.Max(s.Peak, tr);
                s.ExpectedEnd = Mathf.Max(s.ExpectedEnd, u + tr / decay);
                s.Decay = decay;
            }
            s.Last = tr;
        }
        else if (s.On)
        {
            s.On = false;
            float dur = u - s.Start, exp = Mathf.Max(0.001f, s.ExpectedEnd - s.Start);
            s.Count++;
            s.RatioSum += dur / exp;
            if (dur < exp * 0.6f) s.Short++;
            s.Csv.AppendLine($"{s.FightT:0.00},{dur:0.000},{s.Peak:0.000},{s.Decay:0.00},{exp:0.000},{s.Pattern}");
        }
    }

    private static float ReadFloat(object o, string field, float fallback)
        => FindField(o.GetType(), field)?.GetValue(o) is float f ? f : fallback;

    // 방어 차감 전에 곱해지는 보스별 배율 — 게임 코드의 리터럴을 옮겨 적었다(ForestGuardianMonster.TakeDamage · LichMonster._damageTakenMult).
    private static float PreDefenseMult(MonsterBase boss)
    {
        object bb = FindField(boss.GetType(), "_fgBB")?.GetValue(boss);
        if (bb != null)
        {
            if (bb.GetType().GetProperty("IsTransitioning", Inst)?.GetValue(bb) is true) return 0.01f;
            if (bb.GetType().GetProperty("IsPhase2", Inst)?.GetValue(bb) is true)        return 0.7f;
        }
        return Mathf.Max(0.01f, ReadFloat(boss, "_damageTakenMult", 1f));
    }

    private static FieldInfo FindField(Type t, string name)
    {
        for (var cur = t; cur != null; cur = cur.BaseType)
        {
            var f = cur.GetField(name, Inst | BindingFlags.DeclaredOnly);
            if (f != null) return f;
        }
        return null;
    }

    private static MonoBehaviour FindByTypeName(string typeName)
    {
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (mb != null && mb.isActiveAndEnabled && mb.GetType().Name == typeName) return mb;
        return null;
    }

    // 바닥 높이 — 모든 레이어로 재면 광선이 플레이어 자기 머리·보스 몸·던진 바위에 맞아 그 위로 올라간다(반복 공중 부양).
    // Ground 레이어만 먼저, 없을 때만 Default에서 플레이어·몬스터 콜라이더를 건너뛴 첫 면.
    private static readonly RaycastHit[] s_groundHits = new RaycastHit[16];

    private static bool TryGroundY(Vector3 at, PlayerController player, out float y)
    {
        Vector3 from = at + Vector3.up * 6f;
        if (Physics.Raycast(from, Vector3.down, out var gh, 14f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore))
        { y = gh.point.y; return true; }
        int n = Physics.RaycastNonAlloc(from, Vector3.down, s_groundHits, 14f, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore);
        float best = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            var c = s_groundHits[i].collider;
            if (c.transform.IsChildOf(player.transform) || c.GetComponentInParent<MonsterBase>() != null) continue;
            best = Mathf.Max(best, s_groundHits[i].point.y);
        }
        y = best;
        return n > 0 && best > float.MinValue;
    }

    private static void MovePlayer(PlayerController player, Vector3 pos)
    {
        player.transform.position = pos;
        if (player.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.position = pos;
            if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }
    }

    private static void ShotLater(Func<string, UniTask> shot, string name, int delayMs, CancellationToken ct)
        => ShotLaterAsync(shot, name, delayMs, ct).Forget();

    private static async UniTaskVoid ShotLaterAsync(Func<string, UniTask> shot, string name, int delayMs, CancellationToken ct)
    {
        try
        {
            await UniTask.Delay(delayMs, ignoreTimeScale: true, cancellationToken: ct);
            await shot(name);
        }
        catch (OperationCanceledException) { }
    }

    private static async UniTask SaveShotAsync(MonoBehaviour runner, string path, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            await UniTask.WaitForEndOfFrame(runner, ct);
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (tex == null) continue;
            bool black = true;
            for (int y = 0; y < tex.height && black; y += Mathf.Max(1, tex.height / 12))
                for (int x = 0; x < tex.width; x += Mathf.Max(1, tex.width / 16))
                    if (tex.GetPixel(x, y).maxColorComponent > 0.02f) { black = false; break; }
            if (!black || attempt == 19)
            {
                File.WriteAllBytes(black ? path.Replace(".png", "_black.png") : path, tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
                return;
            }
            UnityEngine.Object.Destroy(tex);
        }
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    private static string Clean(string s) => s.Replace("(Clone)", "").Trim();
    private static string Safe(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }

    // ── Event Handlers ─────────────────────────────────────────
    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Log && condition.StartsWith("[BossStory] ")) SceneBeats(condition);
        if (type != LogType.Exception && type != LogType.Error) return;
        s_errorCount++;
        s_lastError = condition.Split('\n')[0];
        if (s_errors.Count >= 20) return;
        string first = (stackTrace ?? "").Split('\n').FirstOrDefault(l => l.Contains("Assets/")) ?? "";
        string key = $"{type}: {condition.Split('\n')[0]}  {first.Trim()}";
        if (!s_errors.Contains(key)) s_errors.Add(key);
    }

    private static void SceneBeats(string line)
    {
        var beats = line.StartsWith("[BossStory] 봉인 장면")   ? SealBeats
                  : line.StartsWith("[BossStory] 해방기 등장") ? UnbindBeats
                  : line.StartsWith("[BossStory] 방을 나간다") ? FlushBeats : null;
        if (beats == null || s_dir == null || s_cts == null) return;
        foreach (var (tag, at) in beats) BeatShotAsync(Path.Combine(s_dir, $"scene_{tag}.png"), at, s_cts.Token).Forget();
    }

    private static async UniTaskVoid BeatShotAsync(string path, float at, CancellationToken ct)
    {
        try { await UniTask.Delay(TimeSpan.FromSeconds(at), ignoreTimeScale: true, cancellationToken: ct); }
        catch (OperationCanceledException) { return; }
        ScreenCapture.CaptureScreenshot(path);
    }

    private static void OnEditorUpdate()
    {
        if (!EditorApplication.isPlaying || !EditorApplication.isPaused) return;
        EditorApplication.isPaused = false;
        s_unpauseCount++;
        Debug.Log($"[FightSim] Error Pause 해제 {s_unpauseCount}회째 — 직전 에러: {s_lastError}");
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingPlayMode) return;
        s_cts?.Cancel();
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
    }
}
