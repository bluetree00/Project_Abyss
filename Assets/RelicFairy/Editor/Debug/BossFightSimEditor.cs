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
/// </summary>
public static class BossFightSimEditor
{
    // ── Constants ──────────────────────────────────────────────
    private const string Root            = "RelicFairy/Boss/Fight Sim/";
    private const float  LogicStep       = 0.1f;
    private const float  CameraStep      = 0.25f;
    private const float  AttackInterval  = 0.5f;
    private const float  MaxSeconds      = 300f;   // 비스케일 — 팝업으로 시간이 멈춰도 끝난다
    private const float  HealBelow       = 0.60f;   // 리치 F4 실패 한 방이 최대 HP의 42% — 그 아래서 채우면 한 방에 죽는다
    private const float  StallSeconds    = 15f;
    private const float  StallGiveUp     = 35f;
    private const int    MaxShots        = 24;
    private const float  RealDps         = 45f;    // 09-18 실측: 45분 런에 필요한 DPS 44 — 「현실 처치 시간」 계산용
    private const float  TargetFightSecs = 180f;   // 시뮬 DPS는 이 안에 끝나게 올린다
    private const float  StrafeDegPerSec = 18f;
    private const float  MoveSpeed       = 6f;
    private const float  PlayerEyeY      = 1.4f;
    private const float  BurstWindow     = 2f;
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private enum Mode { Melee, Mid, Far }

    private sealed class PatternStat
    {
        public int    Count;
        public int    Hits;
        public int    Damage;
        public int    Light, Medium, Heavy;
        public int    FirstHitSamples;
        public float  FirstHitSum;
        public float  FirstHitMin = float.MaxValue;
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
    private static int s_errorCount;
    // 에러 로그가 Error Pause를 걸면 시뮬 루프가 통째로 멈춘다(기사 흰 검 DOTS 에러) — 시뮬이 도는 동안만 풀고 기록한다
    private static int    s_unpauseCount;
    private static string s_lastError = "";

    // ── Public Methods ─────────────────────────────────────────
    [MenuItem(Root + "Melee 3m (Play)")] public static void RunMelee() => Begin(Mode.Melee);
    [MenuItem(Root + "Mid 7m (Play)")]   public static void RunMid()   => Begin(Mode.Mid);
    [MenuItem(Root + "Far 11m (Play)")]  public static void RunFar()   => Begin(Mode.Far);

    // ── Private Methods ────────────────────────────────────────
    private static void Begin(Mode mode)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[FightSim] 플레이 모드에서만 동작한다."); return; }
        s_cts?.Cancel();
        s_cts?.Dispose();
        s_cts = new CancellationTokenSource();
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        RunAsync(mode, s_cts.Token).Forget();
    }

    private static async UniTaskVoid RunAsync(Mode mode, CancellationToken ct)
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

            float radius = mode switch { Mode.Melee => 3f, Mode.Mid => 7f, _ => 11f };
            var boss = spawner.SpawnedBoss;
            string bossName = boss != null ? Clean(boss.name) : "boss";
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim",   // Temp는 에디터 재시작 때 지워진다
                                     
                                      $"{bossName}_{mode}_{DateTime.Now:MMdd_HHmmss}");
            Directory.CreateDirectory(dir);

            // ── 기록 버퍼
            var hits      = new List<(float t, HitWeight w, int dmg, string pattern, float sincePattern)>();
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
                hits.Add((t, w, dmg, curPattern, since));
                if (!patterns.TryGetValue(curPattern, out var ps)) patterns[curPattern] = ps = new PatternStat();
                ps.Hits++; ps.Damage += dmg;
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
            float simDps    = Mathf.Max(RealDps, maxHp / TargetFightSecs);
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
                    MovePlayer(player, next);
                }

                // ── 공격: 사거리 안이면 일정 DPS
                dist = Vector3.Distance(Flat(player.transform.position), Flat(boss.transform.position));
                if (!boss.IsDead && now >= nextAttack)
                {
                    nextAttack = now + AttackInterval;
                    if (zoneCenter != null || dist <= radius + 2f)
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
                        if (stalled > StallGiveUp && !forced)
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
                    if (Time.unscaledTime - deathAtUnscaled > 9f) break;
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
                         patternGaps, camSamples, offScreen, bossOcc, playerOcc, timeline, events, forced, shake);
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
                                     List<(float t, HitWeight w, int dmg, string pattern, float sincePattern)> hits,
                                     Dictionary<string, PatternStat> patterns, List<float> gaps,
                                     int camSamples, int offScreen, int bossOcc, int playerOcc,
                                     StringBuilder timeline, StringBuilder events, bool forced, ShakeTracker shake)
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
        sb.AppendLine($"예외·오류 {s_errorCount}건");
        if (s_unpauseCount > 0) sb.AppendLine($"   ⚠️ Error Pause {s_unpauseCount}회 해제 — 직전 에러: {s_lastError}");
        foreach (var e in s_errors.Take(10)) sb.AppendLine("   " + e);
        sb.AppendLine();
        sb.AppendLine("패턴                                   실행  피격  피해   약/중/강   첫 피격(평균·최소)");
        foreach (var kv in patterns.OrderByDescending(p => p.Value.Count))
        {
            var p = kv.Value;
            string fh = p.FirstHitSamples > 0 ? $"{p.FirstHitSum / p.FirstHitSamples:0.00} · {p.FirstHitMin:0.00}" : "-";
            sb.AppendLine($"{kv.Key,-38} {p.Count,4} {p.Hits,5} {p.Damage,6}   {p.Light}/{p.Medium}/{p.Heavy,-6} {fh}");
        }
        sb.AppendLine();
        sb.AppendLine("── 사건");
        sb.Append(events);
        File.WriteAllText(Path.Combine(dir, "summary.txt"), sb.ToString(), Encoding.UTF8);

        var hcsv = new StringBuilder("t,weight,damage,pattern,sincePatternStart\n");
        foreach (var h in hits) hcsv.AppendLine($"{h.t - t0:0.00},{h.w},{h.dmg},{h.pattern},{h.sincePattern:0.00}");
        File.WriteAllText(Path.Combine(dir, "hits.csv"), hcsv.ToString(), Encoding.UTF8);
        File.WriteAllText(Path.Combine(dir, "timeline.csv"), timeline.ToString(), Encoding.UTF8);
        File.WriteAllText(Path.Combine(dir, "shakes.csv"), shake.Csv.ToString(), Encoding.UTF8);
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
        if (type != LogType.Exception && type != LogType.Error) return;
        s_errorCount++;
        s_lastError = condition.Split('\n')[0];
        if (s_errors.Count >= 20) return;
        string first = (stackTrace ?? "").Split('\n').FirstOrDefault(l => l.Contains("Assets/")) ?? "";
        string key = $"{type}: {condition.Split('\n')[0]}  {first.Trim()}";
        if (!s_errors.Contains(key)) s_errors.Add(key);
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
