using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 아레나 이탈 점검 — 보스 아레나에서 플레이어가 경계를 넘어 밖으로 나갈 수 있는지 실제 이동으로 확인한다(에디터 전용).
///   ① 바닥(Ground 레이어 콜라이더)으로 아레나 사각형을 구한다.
///   ② 방사 시험: 48방향 — 안쪽에서 바깥으로 걷기 3초 + 대시 3번.
///   ③ 가장자리 시험: 네 변 안쪽 1.5 m에서 변을 따라 양쪽으로 걷기 + 대시(모서리 틈 찾기).
///   이탈 = 바닥 사각형 +3 m 밖 · 바닥보다 3 m 아래로 떨어짐 · 낙하 복구가 순간이동시킴(2 m 이상 튐).
/// 보스는 깨우지 않는다(입구 트리거만 지나 벽·결계는 켠다). 플레이어는 시험 동안 무적.
/// 메뉴: RelicFairy/Boss/Arena Escape Probe (Play) — 아레나 안에서 실행.
///       RelicFairy/Boss/Room Escape Probe - Current Room (Play) — 플레이어가 선 방(보스 대기방 등). 보스 입구 단계 없음,
///       시험 동안 방 안 트리거를 꺼 둔다(출구·문 트리거로 방이 바뀌지 않게) — 끝나면 되돌린다. 결과: Logs/arena_escape/&lt;방&gt;_&lt;시각&gt;/report.txt + 이탈 지점 캡처.
/// </summary>
public static class ArenaEscapeProbeEditor
{
    // ── Constants ──────────────────────────────────────────────
    private const int   RadialDirs     = 24;
    private const float WalkSeconds    = 2.5f;
    private const int   Dashes         = 2;
    private const float DashGap        = 0.6f;
    private const float OutsideMargin  = 3f;
    private const float FallDepth      = 3f;
    private const float JumpTeleport   = 2f;
    private const float EdgeInset      = 1.5f;
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private sealed class Trial
    {
        public string Name;
        public Vector3 Start, End, Dir, Exit;   // Exit = 낙하 복구가 옮기기 직전 위치(빠져나간 곳)
        public float MinY;
        public int HpBefore, HpAfter, MaxHp;
        public bool Teleported, Outside, Fell;
        public bool Escaped => Outside || Fell || Teleported;
    }

    // ── Static ─────────────────────────────────────────────────
    private static CancellationTokenSource s_cts;

    // ── Public Methods ─────────────────────────────────────────
    [MenuItem("RelicFairy/Boss/Arena Escape Probe (Play)")]
    public static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[EscapeProbe] 플레이 모드에서만."); return; }
        s_cts?.Cancel(); s_cts?.Dispose();
        s_cts = new CancellationTokenSource();
        RunAsync(s_cts.Token, null).Forget();
    }

    [MenuItem("RelicFairy/Boss/Room Escape Probe - Current Room (Play)")]
    public static void RunCurrentRoom()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[EscapeProbe] 플레이 모드에서만."); return; }
        var room = ArenaTerrainInspectorEditor.CurrentRoom(GameRunBootstrapper.Instance?.Run?.Player);
        if (room == null) { Debug.LogWarning("[EscapeProbe] 준비 안 됨 — 플레이어 발밑에 방(ProcRoom) 바닥이 없다"); return; }
        s_cts?.Cancel(); s_cts?.Dispose();
        s_cts = new CancellationTokenSource();
        RunAsync(s_cts.Token, room).Forget();
    }

    /// <summary>
    /// 근접 도달 점검 — 보스를 깨운 뒤 플레이어가 보스를 향해 실제 이동으로 걸어가 어디서 막히는지, 거기서 기본 공격(Light)을
    /// 6번 넣어 보스 HP가 줄어드는지 본다(보스가 유리벽 너머에 있는지 · 근접으로 칠 수 있는지). 결과는 콘솔 [MeleeProbe] + 전투 로그.
    /// </summary>
    [MenuItem("RelicFairy/Boss/Melee Reach Probe (Play)")]
    public static void RunMeleeReach()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[MeleeProbe] 플레이 모드에서만."); return; }
        s_cts?.Cancel(); s_cts?.Dispose();
        s_cts = new CancellationTokenSource();
        MeleeReachAsync(s_cts.Token).Forget();
    }

    /// <summary>
    /// 편집 모드 — 활성 씬을 디스크 상태로 다시 연다(저장 안 함). 다른 세션이 임시 오브젝트를 만들었다 지워 dirty만 남은 씬은
    /// MCP 씬 로드가 거부해 점검 러너가 테스트 허브로 못 간다. 씬을 저장하지 않고 dirty 표시만 걷어낸다.
    /// </summary>
    [MenuItem("RelicFairy/Boss/Reload Active Scene From Disk (Edit)")]
    public static void ReloadActiveSceneFromDisk()
    {
        if (Application.isPlaying) { Debug.LogWarning("[EscapeProbe] 편집 모드에서만."); return; }
        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(scene.path)) return;
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scene.path, UnityEditor.SceneManagement.OpenSceneMode.Single);
        Debug.Log($"[EscapeProbe] 디스크에서 다시 열었다 — {scene.path}");
    }

    // ── Private Methods ────────────────────────────────────────
    private static async UniTaskVoid MeleeReachAsync(CancellationToken ct)
    {
        PlayerController player = null;
        object savedInput = null;
        FieldInfo inputField = null, moveField = null, invField = null;
        bool savedInv = false;
        try
        {
            player = GameRunBootstrapper.Instance?.Run?.Player;
            var spawner = UnityEngine.Object.FindFirstObjectByType<BossSpawner>();
            if (player == null || spawner == null) { Debug.LogWarning("[MeleeProbe] 준비 안 됨"); return; }
            if (!BossCombatLog.IsOn) BossCombatLog.Start(null);

            TestHubDebugMenu.StepIntoBossEntrance();
            await UniTask.Delay(600, ignoreTimeScale: true, cancellationToken: ct);
            TestHubDebugMenu.StepPastBossEntrance();
            float wait = Time.realtimeSinceStartup + 20f;
            while ((spawner.SpawnedBoss == null || !spawner.SpawnedBoss.gameObject.activeInHierarchy) && Time.realtimeSinceStartup < wait)
                await UniTask.Delay(200, ignoreTimeScale: true, cancellationToken: ct);
            var boss = spawner.SpawnedBoss;
            if (boss == null) { Debug.LogWarning("[MeleeProbe] 보스가 안 나타남"); return; }
            await UniTask.Delay(3000, ignoreTimeScale: true, cancellationToken: ct);   // 등장 연출

            // 막는 것들 — 유리벽(SoftBarrier)·결계
            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb == null || !mb.isActiveAndEnabled) continue;
                string tn = mb.GetType().Name;
                if (tn != "SoftBarrier" && tn != "BarrierVolume" && tn != "CombatBarrier") continue;
                var col = mb.GetComponent<Collider>();
                Debug.Log($"[MeleeProbe] 장벽 {tn} '{mb.name}' 레이어 {LayerMask.LayerToName(mb.gameObject.layer)} · " +
                          (col != null ? $"콜라이더 {col.bounds.center} 크기 {col.bounds.size} 활성 {col.enabled}" : "콜라이더 없음"));
            }

            inputField = FindField(typeof(PlayerController), "_inputActions");
            moveField  = FindField(typeof(PlayerController), "_moveDirection");
            invField   = FindField(typeof(PlayerController), "debugInvincible");
            savedInput = inputField?.GetValue(player);
            inputField?.SetValue(player, null);
            if (invField != null) { savedInv = (bool)invField.GetValue(player); invField.SetValue(player, true); }

            // 보스를 향해 6초 걷기 — 막히면 그 자리
            Vector3 start = player.transform.position;
            float until = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < until && !boss.IsDead)
            {
                Vector3 d = boss.transform.position - player.transform.position; d.y = 0f;
                if (d.magnitude < 1.6f) break;
                moveField?.SetValue(player, d.normalized);
                await UniTask.Yield(PlayerLoopTiming.FixedUpdate, ct);
            }
            moveField?.SetValue(player, Vector3.zero);
            await UniTask.Delay(300, ignoreTimeScale: true, cancellationToken: ct);
            Vector3 stop = player.transform.position;
            float dist = Flat(stop - boss.transform.position).magnitude;
            Debug.Log($"[MeleeProbe] {boss.name}: 출발 {Flat(start - boss.transform.position).magnitude:0.0} m → 멈춘 거리 {dist:0.0} m · 이동 {Flat(stop - start).magnitude:0.0} m");

            // 그 자리에서 보스를 보고 기본 공격 6번
            int hp0 = boss.CurrentHp;
            for (int i = 0; i < 6; i++)
            {
                player.RequestFacing(Quaternion.LookRotation(Flat(boss.transform.position - player.transform.position).normalized));
                player.InputBuffer.Push(Command.Light);
                await UniTask.Delay(450, ignoreTimeScale: true, cancellationToken: ct);
            }
            await UniTask.Delay(500, ignoreTimeScale: true, cancellationToken: ct);
            int hp1 = boss.CurrentHp;
            Debug.Log($"[MeleeProbe] {boss.name}: 기본 공격 6회 — 보스 HP {hp0} → {hp1} ({hp1 - hp0}) · 거리 {dist:0.0} m · 면역 {boss.IsDamageImmuneNow}" +
                      (hp1 < hp0 ? " — 근접으로 닿는다" : " — ⚠️ 근접으로 안 닿는다"));
        }
        catch (OperationCanceledException) { Debug.Log("[MeleeProbe] 취소"); }
        finally
        {
            if (player != null) { inputField?.SetValue(player, savedInput); invField?.SetValue(player, savedInv); }
        }
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    private static async UniTaskVoid RunAsync(CancellationToken ct, Transform rootOverride)
    {
        PlayerController player = null;
        object savedInput = null;
        FieldInfo inputField = null, moveField = null, invField = null;
        bool savedInv = false;
        var disabledTriggers = new List<Collider>();
        try
        {
            player = GameRunBootstrapper.Instance?.Run?.Player;
            Transform room = rootOverride;
            if (room == null)
            {
                var spawner = UnityEngine.Object.FindFirstObjectByType<BossSpawner>();
                if (player == null || spawner == null) { Debug.LogWarning($"[EscapeProbe] 준비 안 됨 — player={player != null} spawner={spawner != null}"); return; }
                room = spawner.transform;
                while (room.parent != null && !room.name.StartsWith("ProcRoom")) room = room.parent;
            }
            if (player == null) { Debug.LogWarning("[EscapeProbe] 준비 안 됨 — 플레이어 없음"); return; }

            // ① 바닥 사각형 — 방 아래 Ground 레이어 콜라이더(트리거 제외)의 합
            int ground = LayerMask.NameToLayer("Ground");
            var floors = room.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger && c.gameObject.layer == ground).ToList();
            if (floors.Count == 0) { Debug.LogWarning("[EscapeProbe] Ground 바닥이 없다"); return; }
            Bounds fb = floors[0].bounds;
            foreach (var c in floors) fb.Encapsulate(c.bounds);
            // 가장 큰 바닥 하나를 기준으로(통로·대기방 바닥이 섞이면 너무 커진다)
            var mainCol = floors.OrderByDescending(c => c.bounds.size.x * c.bounds.size.z).First();
            if (rootOverride != null
                && Physics.Raycast(player.transform.position + Vector3.up, Vector3.down, out var under, 6f, 1 << ground, QueryTriggerInteraction.Ignore)
                && floors.Contains(under.collider))
                mainCol = under.collider;   // 방 모드 — 플레이어가 선 바닥이 기준
            var main = ArenaTerrainInspectorEditor.FloorBounds(floors, mainCol);   // 타일 바닥이면 같은 높이 타일의 합
            Vector3 center = main.center; float floorTop = mainCol.bounds.max.y;
            float hx = main.extents.x, hz = main.extents.z;

            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "arena_escape", $"{Clean(room.name)}_{DateTime.Now:MMdd_HHmmss}");
            Directory.CreateDirectory(dir);
            var log = new StringBuilder();
            log.AppendLine($"방 {room.name} · 바닥 기준 {floors.Count}개 중 최대 · 중심 ({center.x:0.0},{center.z:0.0}) · 반폭 {hx:0.0} x {hz:0.0} · 윗면 y {floorTop:0.00}");
            log.AppendLine($"이탈 판정: 바닥 사각형 +{OutsideMargin} m 밖 · 윗면 −{FallDepth} m 아래 · 낙하 복구 순간이동(>{JumpTeleport} m)");

            // ⓪ 바닥 점검 — 1 m 격자로 발밑을 분류(움직임 없음). 벽이 켜지기 전·후 둘 다 의미가 있어 벽을 켠 뒤에 한 번 더 한다.
            log.AppendLine();
            if (rootOverride == null)
            {
                log.Append(FloorMap(center, hx, hz, floorTop, "벽 켜기 전"));
                // 입구 트리거만 지나 결계·벽을 켠다(보스는 깨우지 않게 곧바로 보스를 재운 채 둔다 — 깨어나도 무적이라 피해 없음)
                TestHubDebugMenu.StepIntoBossEntrance();
                await UniTask.Delay(600, ignoreTimeScale: true, cancellationToken: ct);
                TestHubDebugMenu.StepPastBossEntrance();
                await UniTask.Delay(800, ignoreTimeScale: true, cancellationToken: ct);
                log.Append(FloorMap(center, hx, hz, floorTop, "벽 켠 뒤(전투 상태)"));
            }
            else
            {
                log.Append(FloorMap(center, hx, hz, floorTop, "방"));
                // 출구·문 트리거를 밟아 방이 바뀌면 시험이 끝난다 — 동안만 끈다(벽은 트리거가 아니라 막힘 판정에 영향 없음)
                foreach (var c in room.GetComponentsInChildren<Collider>(false))
                    if (c.enabled && c.isTrigger) { c.enabled = false; disabledTriggers.Add(c); }
            }
            File.WriteAllText(Path.Combine(dir, "report.txt"), log.ToString(), new UTF8Encoding(true));   // 이탈 시험 도중 런이 끝나도 바닥 점검은 남는다

            // 입력 가로채기 + 무적
            inputField = FindField(typeof(PlayerController), "_inputActions");
            moveField  = FindField(typeof(PlayerController), "_moveDirection");
            invField   = FindField(typeof(PlayerController), "debugInvincible");
            savedInput = inputField?.GetValue(player);
            inputField?.SetValue(player, null);
            if (invField != null) { savedInv = (bool)invField.GetValue(player); invField.SetValue(player, true); }

            var trials = new List<Trial>();
            float r0 = Mathf.Min(hx, hz) * 0.5f;
            for (int i = 0; i < RadialDirs; i++)
            {
                float a = i * 360f / RadialDirs * Mathf.Deg2Rad;
                Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 s = center + d * r0;
                trials.Add(await TrialAsync(player, moveField, $"방사 {i * 360f / RadialDirs:000}°", s, d, center, hx, hz, floorTop, ct));
            LogTrial(trials[^1], center, floorTop);
            }
            // ③ 가장자리 — 네 변 안쪽에서 변을 따라 양방향
            var edges = new (Vector3 start, Vector3 dir, string name)[]
            {
                (center + new Vector3(-hx + EdgeInset, 0, 0), Vector3.forward, "서쪽 변 → 북"), (center + new Vector3(-hx + EdgeInset, 0, 0), Vector3.back, "서쪽 변 → 남"),
                (center + new Vector3( hx - EdgeInset, 0, 0), Vector3.forward, "동쪽 변 → 북"), (center + new Vector3( hx - EdgeInset, 0, 0), Vector3.back, "동쪽 변 → 남"),
                (center + new Vector3(0, 0, -hz + EdgeInset), Vector3.right,   "남쪽 변 → 동"), (center + new Vector3(0, 0, -hz + EdgeInset), Vector3.left, "남쪽 변 → 서"),
                (center + new Vector3(0, 0,  hz - EdgeInset), Vector3.right,   "북쪽 변 → 동"), (center + new Vector3(0, 0,  hz - EdgeInset), Vector3.left, "북쪽 변 → 서"),
            };
            foreach (var e in edges)
            {
                trials.Add(await TrialAsync(player, moveField, e.name, e.start, e.dir, center, hx, hz, floorTop, ct));
                LogTrial(trials[^1], center, floorTop);
            }

            int shots = 0;
            log.AppendLine();
            log.AppendLine($"시험 {trials.Count}회 · 이탈 {trials.Count(t => t.Escaped)}회");
            foreach (var t in trials)
            {
                string why = t.Escaped
                    ? "⚠️ 이탈 — " + string.Join("·", new[] { t.Outside ? "경계 밖" : null, t.Fell ? $"낙하(최저 y {t.MinY:0.0})" : null, t.Teleported ? "낙하 복구 순간이동" : null }.Where(x => x != null))
                    : "막힘";
                log.AppendLine($"{t.Name,-14} 시작 ({t.Start.x - center.x:0.0},{t.Start.z - center.z:0.0}) → 끝 ({t.End.x - center.x:0.0},{t.End.z - center.z:0.0}, y {t.End.y - floorTop:+0.0;-0.0}) {why}");
            }
            // 이탈 지점 다시 가서 찍기(최대 8장)
            foreach (var t in trials.Where(t => t.Escaped).Take(8))
            {
                Teleport(player, t.End + Vector3.up * 0.2f);
                await UniTask.Delay(500, ignoreTimeScale: true, cancellationToken: ct);
                await CaptureAsync(Path.Combine(dir, $"{++shots:00}_{Clean(t.Name)}.png"), ct);
            }
            File.WriteAllText(Path.Combine(dir, "report.txt"), log.ToString(), new UTF8Encoding(true));
            Debug.Log($"[EscapeProbe] 완료 — 이탈 {trials.Count(t => t.Escaped)}/{trials.Count} — {dir}");
        }
        catch (OperationCanceledException) { Debug.Log("[EscapeProbe] 취소"); }
        finally
        {
            if (player != null)
            {
                inputField?.SetValue(player, savedInput);
                invField?.SetValue(player, savedInv);
            }
            foreach (var c in disabledTriggers)
                if (c != null) c.enabled = true;
        }
    }

    private static async UniTask<Trial> TrialAsync(PlayerController player, FieldInfo moveField, string name, Vector3 start, Vector3 dir,
                                                   Vector3 center, float hx, float hz, float floorTop, CancellationToken ct)
    {
        start.y = GroundY(start, floorTop);
        // 낙하 복구는 무적과 상관없이 최대 HP 10%를 깎는다 — 시험마다 채워 런이 끝나지 않게(09-26 숲 아레나에서 사망으로 런 종료)
        var stats = player.RuntimeStats;
        if (stats != null && stats.MaxHp > 0) stats.SetHp(stats.MaxHp);
        Teleport(player, start + Vector3.up * 0.1f);
        player.RequestFacing(Quaternion.LookRotation(dir));
        await UniTask.Delay(250, ignoreTimeScale: true, cancellationToken: ct);

        var t = new Trial { Name = name, Start = start, Dir = dir, MinY = start.y, HpBefore = stats?.Hp ?? -1, MaxHp = stats?.MaxHp ?? -1 };
        Vector3 prev = player.transform.position;
        float end = Time.realtimeSinceStartup + WalkSeconds + Dashes * DashGap + 0.5f;
        float nextDash = Time.realtimeSinceStartup + WalkSeconds;
        int dashes = 0;
        while (Time.realtimeSinceStartup < end)
        {
            moveField?.SetValue(player, dir);
            if (dashes < Dashes && Time.realtimeSinceStartup >= nextDash)
            {
                player.RequestFacing(Quaternion.LookRotation(dir));
                player.InputBuffer.Push(Command.Dodge);
                dashes++; nextDash += DashGap;
            }
            await UniTask.Yield(PlayerLoopTiming.FixedUpdate, ct);
            Vector3 p = player.transform.position;
            t.MinY = Mathf.Min(t.MinY, p.y);
            if ((p - prev).magnitude > JumpTeleport)   // 낙하 복구가 옮겼다 — 직전 위치가 빠져나간 곳. 더 걸으면 또 떨어져 HP가 바닥난다(09-27 화룡 아레나 런 종료)
            {
                t.Teleported = true; t.Exit = prev;
                break;
            }
            prev = p;
        }
        moveField?.SetValue(player, Vector3.zero);
        t.End = player.transform.position;
        t.HpAfter = stats?.Hp ?? -1;
        t.Outside = Mathf.Abs(t.End.x - center.x) > hx + OutsideMargin || Mathf.Abs(t.End.z - center.z) > hz + OutsideMargin;
        t.Fell = t.MinY < floorTop - FallDepth;
        return t;
    }

    // 바닥 점검 — 아레나 바닥 사각형 +MapMargin m를 0.5 m 격자로 훑는다(지도 출력은 1 m).
    //   '.' 설 수 있는 바닥(발밑 0.4 m 안에 Ground) · '#' 벽(윗면이 바닥보다 2 m 넘게 높다) · '^' 낮은 턱(0.4~2 m)
    //   'o' 바닥 아닌 면(아래에 Ground 없음 — 여기 서면 공중 상태, 조작 불가) · ' ' 구멍(떨어짐)
    //   위험 = 바닥('.') 바로 옆(8방향)의 구멍·바닥 아닌 면 — 벽 없이 바닥이 끊기는 가장자리.
    //   1 m 격자는 바닥 끝(±14)과 기둥 줄(±15) 사이 0.6 m 틈을 못 봤다(09-26 숲 아레나 낙하) → 0.5 m.
    private const float MapMargin = 6f;
    private const float MapStep   = 0.5f;
    private static string FloorMap(Vector3 center, float hx, float hz, float floorTop, string label)
    {
        int groundMask = LayerMask.GetMask("Ground");
        int ignore = ~(LayerMask.GetMask("Player", "Monster", "MonsterHit", "Ignore Raycast", "UI"));
        int nx = Mathf.CeilToInt((hx + MapMargin) * 2f / MapStep) + 1, nz = Mathf.CeilToInt((hz + MapMargin) * 2f / MapStep) + 1;
        var cell = new char[nx, nz];
        for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
            {
                var p = new Vector3(center.x - hx - MapMargin + ix * MapStep, floorTop + 150f, center.z - hz - MapMargin + iz * MapStep);
                char c = ' ';
                if (Physics.Raycast(p, Vector3.down, out var hit, 300f, ignore, QueryTriggerInteraction.Ignore))
                {
                    bool groundBelow = Physics.Raycast(p, Vector3.down, out var g, 300f, groundMask, QueryTriggerInteraction.Ignore)
                                       && hit.point.y - g.point.y <= 0.4f;
                    if (hit.point.y > floorTop + 2f) c = '#';
                    else if (hit.point.y > floorTop + 0.4f) c = '^';
                    else if (groundBelow) c = '.';
                    else if (hit.point.y < floorTop - FallDepth) c = ' ';
                    else c = 'o';
                }
                cell[ix, iz] = c;
            }
        var sb = new StringBuilder();
        var risky = new List<string>();
        for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
            {
                if (cell[ix, iz] != ' ' && cell[ix, iz] != 'o') continue;
                bool nextToFloor = false;
                for (int dx = -1; dx <= 1 && !nextToFloor; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int ax = ix + dx, az = iz + dz;
                        if (ax < 0 || az < 0 || ax >= nx || az >= nz) continue;
                        if (cell[ax, az] == '.') { nextToFloor = true; break; }
                    }
                if (nextToFloor)
                    risky.Add($"({ix * MapStep - hx - MapMargin:0.0},{iz * MapStep - hz - MapMargin:0.0}){(cell[ix, iz] == ' ' ? "구멍" : "바닥아닌면")}");
            }
        int every = Mathf.RoundToInt(1f / MapStep);
        sb.AppendLine($"── 바닥 점검({label}) — {MapStep} m 격자 {nx}x{nz}(지도는 1 m) · 위쪽 = 북(+z) · '.'바닥 '#'벽 '^'낮은 턱 'o'바닥아닌면 ' '구멍");
        for (int iz = nz - 1; iz >= 0; iz -= every)
        {
            var row = new StringBuilder(nx);
            for (int ix = 0; ix < nx; ix += every) row.Append(cell[ix, iz]);
            sb.AppendLine("|" + row + "|");
        }
        sb.AppendLine($"정상 바닥 바로 옆 구멍·바닥아닌면 {risky.Count}칸" + (risky.Count > 0 ? ": " + string.Join(" ", risky.Take(80)) : ""));
        Debug.Log($"[EscapeProbe] 바닥 점검({label}) — 바닥 옆 위험 칸 {risky.Count}");
        return sb.ToString();
    }

    // 시험 하나 끝날 때마다 콘솔에 — 도중에 런이 끝나도 어디서 빠졌는지 남는다
    private static void LogTrial(Trial t, Vector3 center, float floorTop)
    {
        if (!t.Escaped) { Debug.Log($"[EscapeProbe] {t.Name} 막힘 · HP {t.HpBefore}→{t.HpAfter}/{t.MaxHp}"); return; }
        Vector3 e = t.Teleported ? t.Exit : t.End;
        Debug.LogWarning($"[EscapeProbe] ⚠️ {t.Name} 이탈 — 빠져나간 곳 로컬 ({e.x - center.x:0.0}, {e.z - center.z:0.0}) · y {e.y - floorTop:+0.0;-0.0} · " +
                         $"{(t.Outside ? "경계 밖 " : "")}{(t.Fell ? "낙하 " : "")}{(t.Teleported ? "낙하 복구" : "")} · HP {t.HpBefore}→{t.HpAfter}/{t.MaxHp}");
    }

    private static float GroundY(Vector3 p, float fallback)
        => Physics.Raycast(p + Vector3.up * 8f, Vector3.down, out var hit, 20f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore)
            ? hit.point.y : fallback;

    private static void Teleport(PlayerController player, Vector3 pos)
    {
        player.transform.position = pos;
        if (player.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.position = pos;
            if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }
    }

    private static async UniTask CaptureAsync(string path, CancellationToken ct)
    {
        var runner = UnityEngine.Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        await UniTask.WaitForEndOfFrame(runner, ct);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.Destroy(tex);
    }

    private static string Clean(string s) => string.Concat(s.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_').Replace("(Clone)", "");

    private static FieldInfo FindField(Type t, string name)
    {
        for (var cur = t; cur != null; cur = cur.BaseType)
        {
            var f = cur.GetField(name, Inst | BindingFlags.DeclaredOnly);
            if (f != null) return f;
        }
        return null;
    }
}
