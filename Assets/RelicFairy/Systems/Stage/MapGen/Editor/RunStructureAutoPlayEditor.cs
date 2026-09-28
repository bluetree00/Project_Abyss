using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// [레벨디자인 실측 · 플레이 중] 테스트 허브에서 Ch1부터 런을 자동으로 끝까지 진행하며 방 구조를 기록한다.
///
/// 몬스터는 나오는 즉시 처치하고(무적 켠 플레이어가 서 있기만 한다), 출구가 열리면 무작위로 하나를 골라 통과한다.
/// 대사는 넘기고, 상호작용 이벤트는 2초 뒤 해결 처리하고, 보스방은 입구 트리거를 밟고, 챕터 포탈은 통과한다.
/// 사람의 전투 시간은 재지 않는다 — 방마다 <b>연출·전환에 드는 시간</b>과 <b>몬스터 구성(수·등급·종·총 HP)</b>을 잰다.
/// 전투 시간은 총 HP ÷ DPS로 따로 계산한다(플레이타임 설계서의 시간 모델).
///
/// 결과: Temp/run_structure_autoplay.json (방마다 한 줄)
/// </summary>
public static class RunStructureAutoPlayEditor
{
    private const string Root        = "RelicFairy/Debug/런 구조 자동 실측/";
    private const double TickGap     = 0.25;
    private const double MaxSeconds  = 45 * 60;
    private const int    MaxRooms    = 300;
    private const BindingFlags Inst  = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class RoomRec
    {
        public int    order, chapter, visit;
        public string kind = "?", pool = "", category = "";
        public float  diff;
        public double enterT, clearT = -1, leaveT = -1, rewardT = -1;
        public readonly Dictionary<int, (string name, MonsterGrade grade, int hp)> mons = new();
        public string offered = "", chosen = "", notes = "";
        /// <summary>이 방에서 난 경고·오류 — 메시지 앞머리별 횟수. 어느 방이 원인인지 좁히는 용도다.</summary>
        public readonly Dictionary<string, int> warns = new();
    }

    private static readonly List<RoomRec> s_rooms = new();
    private static RoomRec s_cur;
    private static string  s_curKey;
    private static double  s_next, s_start, s_blockedSince = -1, s_startGateOpenedAt = -1, s_bossStepAt, s_chapterGateSeenAt = -1;
    private static double  s_dialogueSince = -1, s_noRunSince = -1, s_bossLogAt, s_bossKillAt, s_bossHurtAt, s_bossNudgeAt;
    private static double  s_rewardAt = -1;   // 보상 위로 옮긴 시각(팝업 대기 중)
    private static bool    s_rewardPressed;   // [F]를 대신 눌렀는지
    private static string  s_buildTarget;     // 지금 「짓는」 방(room_id) — 서 있는 방과 다를 수 있다
    private static readonly Dictionary<string, Dictionary<string, int>> s_buildWarns = new();   // room_id → 짓는 중 경고
    private static int     s_bossStep, s_waitingChapter = -1, s_dialogueClicks;
    private static bool    s_chapterGateTaken, s_running, s_bossSeen, s_dialogueLongLogged;
    private static string  s_blockedName;
    private static System.Random s_rng;
    private static readonly StringBuilder s_events = new();

    [MenuItem(Root + "시작 (테스트 허브에서, 플레이 중)")]
    private static void Begin() => BeginWith(false, 1);

    [MenuItem(Root + "시작 — Ch1 보스 대기방부터 (테스트 허브에서, 플레이 중)")]
    private static void BeginAtBoss() => BeginWith(true, 1);

    [MenuItem(Root + "시작 — Ch4부터 (테스트 허브에서, 플레이 중)")]
    private static void BeginAtChapter4() => BeginWith(false, 4);

    private static void BeginWith(bool bossApproach, int chapter)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[RunAuto] 플레이 모드에서만"); return; }
        var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Debug.LogWarning("[RunAuto] 테스트 허브가 아니다"); return; }
        var t = typeof(TestHubLauncher);
        t.GetField("_chapter", Inst).SetValue(launcher, chapter);
        t.GetField("_bossApproach", Inst).SetValue(launcher, bossApproach);
        if (!launcher.TryLaunch()) { Debug.LogWarning("[RunAuto] 시작 실패(초기화 중?)"); return; }

        s_rooms.Clear();
        s_events.Clear();
        s_cur = null;
        s_curKey = null;
        s_rng = new System.Random(20260918);
        s_start = EditorApplication.timeSinceStartup;
        s_next = s_start + 3;
        s_blockedSince = s_startGateOpenedAt = s_chapterGateSeenAt = s_dialogueSince = s_noRunSince = -1;
        s_rewardAt = -1;
        s_buildTarget = null;
        s_buildWarns.Clear();
        s_blockedName = null;
        s_bossStep = 0;
        s_waitingChapter = -1;
        s_chapterGateTaken = false;
        s_running = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Log($"시작 ch{chapter}" + (bossApproach ? " (보스 대기방부터)" : ""));
    }

    [MenuItem(Root + "중지·기록")]
    private static void StopAndWrite() => Finish("manual");

    private static void Tick()
    {
        if (!s_running) { EditorApplication.update -= Tick; return; }
        if (!Application.isPlaying) { Finish("play-stopped"); return; }
        double now = EditorApplication.timeSinceStartup;
        if (now < s_next) return;
        s_next = now + TickGap;
        if (now - s_start > MaxSeconds) { Finish("time-limit"); return; }
        if (s_rooms.Count > MaxRooms) { Finish("room-limit"); return; }

        // 런이 끝나 허브로 돌아왔다
        if (s_rooms.Count > 0 && Object.FindFirstObjectByType<TestHubLauncher>() != null) { Finish("returned-to-hub"); return; }

        // 1) 대사창
        if (SkipDialogue(now)) return;

        // 2) 게임 정지 팝업(보상 선택 등)
        var ui = Managers.UI;
        if (ui != null && ui.IsGameplayBlocked)
        {
            string top = TopPopupName(ui);
            if (s_blockedSince < 0 || top != s_blockedName)
            {
                s_blockedSince = now;
                s_blockedName  = top;
                Log("차단 팝업 " + top);
            }
            else if (now - s_blockedSince > 2.0)
            {
                s_blockedSince = now;
                if (TryResolveChoicePopup(ui, out string how)) { Log("차단 팝업 " + top + " → " + how); return; }
                Log("차단 팝업 " + top + " → ESC");
                if (!ui.TryCloseTopPopupOnEscape())
                {
                    Log("차단 팝업 " + top + " → 전부 닫기");
                    ui.CloseAllPopupUI();
                }
            }
            return;
        }
        if (s_blockedSince >= 0) Log("차단 해제 (" + s_blockedName + ")");
        s_blockedSince = -1;
        s_blockedName  = null;

        var run    = GameRunBootstrapper.Instance?.Run;
        var player = run?.Player;
        var rfc    = RunFlowController.Active;
        if (run == null || player == null)   // 씬 전환 중
        {
            if (s_noRunSince < 0) s_noRunSince = now;
            return;
        }
        if (s_noRunSince >= 0)
        {
            Log($"런 참조 확보 ({now - s_noRunSince:0.0}초 대기)");
            s_noRunSince = -1;
        }
        typeof(PlayerController).GetField("debugInvincible", Inst).SetValue(player, true);

        // 3) 대기방(절차 방 진입 전) — 절차 진행 컨트롤러는 대기방 게이트를 지나야 생긴다
        if (rfc == null || typeof(RunFlowController).GetField("_current", Inst).GetValue(rfc) == null)
        {
            HandleWaitingRoom(player, now, (int)run.CurrentChapter);
            return;
        }

        // 4) 방 식별 — (챕터, 방문 순번). 방 계획은 입장 연출이 끝난 뒤에야 갱신되므로 매 틱 다시 읽는다.
        var seq   = typeof(RunFlowController).GetField("_sequencer", Inst).GetValue(rfc) as RunSequencer;
        int visit = seq != null ? seq.VisitCount : -1;
        int chapter = (int)run.CurrentChapter;
        string key = chapter + ":" + visit;
        if (key != s_curKey)
        {
            if (s_cur != null && s_cur.leaveT < 0) s_cur.leaveT = now;
            s_cur = new RoomRec { order = s_rooms.Count, chapter = chapter, visit = visit, enterT = now };
            s_rewardAt = -1;
            s_rewardPressed = false;
            s_rooms.Add(s_cur);
            s_curKey = key;
            s_bossStep = 0;
            s_bossStepAt = now + 2.5;
            s_bossSeen = false;
            s_bossLogAt = s_bossKillAt = s_bossHurtAt = s_bossNudgeAt = now;
            if (s_chapterGateTaken) { s_chapterGateTaken = false; s_chapterGateSeenAt = -1; }
            Log($"방 진입 {key}");
        }
        bool hasPlan = (bool)typeof(RunFlowController).GetField("_hasLastPlan", Inst).GetValue(rfc);
        if (hasPlan)
        {
            var plan = (DoorPlan)typeof(RunFlowController).GetField("_lastPlan", Inst).GetValue(rfc);
            string pool = plan.entry?.pool_key ?? "";
            if (pool != s_cur.pool) Log($"방 계획 {key} {plan.kind} {pool}");
            s_cur.kind     = plan.kind.ToString();
            s_cur.pool     = pool;
            s_cur.category = plan.entry?.category ?? "";
            s_cur.diff     = plan.entry?.difficulty_scale ?? 0f;
        }

        // 5) 몬스터 — 기록하고 즉시 처치. 보스는 6)에서 따로 다룬다.
        //    설정이 아직 안 읽힌 몬스터(비동기 초기화 중)는 등급·HP가 0이라 읽힌 뒤 다시 적는다.
        var spawner = Object.FindFirstObjectByType<BossSpawner>();
        var boss    = spawner != null ? spawner.SpawnedBoss : null;
        foreach (var mb in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
        {
            if (mb.IsDead || !mb.isActiveAndEnabled) continue;
            int id = mb.GetInstanceID();
            if (!s_cur.mons.TryGetValue(id, out var rec) || rec.hp == 0)
                s_cur.mons[id] = (Clean(mb.name), mb.Grade, mb.EffectiveMaxHp);
            if (mb == boss || mb.Grade == MonsterGrade.Boss) continue;
            if (mb.CurrentHp > 0) mb.TakeDamage(mb.CurrentHp * 10f + 100000f, player.gameObject, 0f);
        }

        // 6) 보스방 — 입구 트리거를 밟아 보스를 부르고, 나오면 쓰러뜨린다
        if (s_cur.kind == nameof(RoomPlanKind.Boss) && s_cur.clearT < 0 && GameObject.Find("@ChapterGate") == null)
            HandleBoss(player, spawner, boss, now);

        // 7) 상호작용 이벤트 — 2초 뒤 해결
        foreach (var ch in Object.FindObjectsByType<WorldInteractionChallenge>(FindObjectsSortMode.None))
        {
            var resolved = (bool)typeof(WorldInteractionChallenge).GetProperty("IsResolved", Inst).GetValue(ch);
            if (resolved || now - s_cur.enterT < 2.0) continue;
            typeof(WorldInteractionChallenge).GetMethod("FinishWith", Inst)
                .Invoke(ch, new object[] { ChallengeGrade.Bronze, player.transform.position });
            s_cur.notes += "interaction-forced(" + ch.GetType().Name + ") ";
        }

        // 8) 챕터 포탈
        var chapterGate = GameObject.Find("@ChapterGate");
        if (chapterGate != null && !s_chapterGateTaken)
        {
            if (s_cur.clearT < 0) { s_cur.clearT = now; Log($"클리어 {key} (챕터 포탈)"); }
            if (s_chapterGateSeenAt < 0) s_chapterGateSeenAt = now;
            else if (now - s_chapterGateSeenAt > 1.5 && chapterGate.TryGetComponent<Collider>(out var gc))
            {
                Teleport(player, gc.bounds);
                s_chapterGateTaken = true;
                s_cur.chosen = "ChapterGate";
                Log($"챕터 포탈 통과 ch{chapter}");
            }
            return;
        }

        // 9) 출구 — 열리면 0.8초 뒤 무작위로 하나
        var armed = new List<ProcRoomGate>();
        foreach (var g in Object.FindObjectsByType<ProcRoomGate>(FindObjectsSortMode.None))
            if (g.IsArmed) armed.Add(g);
        if (armed.Count == 0) return;
        if (s_cur.clearT < 0) { s_cur.clearT = now; Log($"클리어 {key}"); }
        if (now - s_cur.clearT < 0.8 || !string.IsNullOrEmpty(s_cur.chosen)) return;

        // 9-b) 보상 줍기 — 클리어 보상은 <b>근접 트리거</b>라 출구로 직행하면 안 열린다.
        //      예전에는 그냥 지나쳐서 보상 확인·선택 시간이 측정에서 통째로 빠졌다(09-22 실측: 31방에 팝업 0회).
        //      팝업 자체는 위의 차단 팝업 처리가 눌러 준다 — 여기서는 <b>밟아 주고 기다리기만</b> 한다.
        if (s_cur.rewardT < 0)
        {
            var reward = Object.FindFirstObjectByType<ClearRewardTrigger>();
            if (reward != null)
            {
                if (s_rewardAt < 0)
                {
                    s_rewardAt = now;
                    Vector3 rp = reward.transform.position;
                    player.transform.position = rp;
                    if (player.TryGetComponent<Rigidbody>(out var prb)) { prb.position = rp; prb.linearVelocity = Vector3.zero; }
                    Log("보상으로 이동");
                    return;
                }

                // 근접만으로는 안 열린다 — 게임은 [F] 입력을 기다린다(ClearRewardTrigger.Update의 Input.GetKeyDown).
                // 실측은 키를 누를 수 없으니 <b>그 한 지점만</b> 대신 누른다. 이후(팝업·선택·보관함)는 게임 경로 그대로다.
                if (!s_rewardPressed && now - s_rewardAt > 1.0)
                {
                    s_rewardPressed = true;
                    var mi = typeof(ClearRewardTrigger).GetMethod("OpenRewardFlowAsync", Inst);
                    if (mi != null) { mi.Invoke(reward, null); Log("보상 열기([F] 대신)"); }
                    else Log("보상 열기 실패 — OpenRewardFlowAsync를 못 찾았다");
                    return;
                }
                // 팝업 연출·선택이 끝날 시간을 준다. 6초가 지나도 남아 있으면 포기하고 출구로 간다.
                if (now - s_rewardAt < 6) return;
            }
            // 보상은 클리어 <b>연출이 끝난 뒤</b>에 바닥에 생긴다. 없다고 바로 포기하면 늘 지나친다
            // (09-22 실측: 이 대기가 없을 때 22방 중 1방만 보상을 잡았다). 4초까지 기다려 본다.
            else if (now - s_cur.clearT < 4) return;
            s_cur.rewardT = s_rewardAt < 0 ? 0 : now - s_rewardAt;
            if (s_cur.rewardT > 0) Log($"보상 처리 {s_cur.rewardT:F1}초");
            s_rewardAt = -1;
            s_rewardPressed = false;
        }

        var names = new List<string>();
        foreach (var g in armed) names.Add(g.Kind.ToString());
        s_cur.offered = string.Join("/", names);
        var pick = armed[s_rng.Next(armed.Count)];
        s_cur.chosen = pick.Kind.ToString();
        if (pick.TryGetComponent<Collider>(out var col)) Teleport(player, col.bounds);
        Log($"출구 {s_cur.offered} → {s_cur.chosen}");
    }

    private static void HandleBoss(PlayerController player, BossSpawner spawner, MonsterBase boss, double now)
    {
        if (spawner == null) return;

        // 보스가 아직 없다 — 입구 트리거를 밟는다(1.5초 간격 두 단계)
        if (spawner.SpawnedBoss is null)
        {
            if (s_bossStep >= 2 || now < s_bossStepAt) return;
            if (s_bossStep == 0) TestHubDebugMenu.StepIntoBossEntrance();
            else                 TestHubDebugMenu.StepPastBossEntrance();
            s_bossStep++;
            s_bossStepAt = now + 1.5;
            if (s_bossStep == 1) s_cur.notes += "boss-step ";
            return;
        }

        // 참조는 있는데 파괴됐다(Unity null)
        if (boss == null)
        {
            if (now >= s_bossLogAt) { Log("보스 오브젝트 파괴됨 — 클리어 신호 없음"); s_bossLogAt = now + 10; }
            return;
        }

        if (!s_bossSeen) { s_bossSeen = true; Log($"보스 등장 {Clean(boss.name)}"); }

        int hp = boss.CurrentHp;
        if (now >= s_bossLogAt)
        {
            s_bossLogAt = now + 2;
            var fsm     = typeof(MonsterBase).GetField("_fsm", Inst).GetValue(boss) as MonsterFSM;
            var dormant = boss.GetType().GetField("_dormantState", Inst)?.GetValue(boss);
            bool asleep = dormant != null && (bool)(dormant.GetType().GetProperty("IsActive")?.GetValue(dormant) ?? false);
            float dist  = Vector3.Distance(player.transform.position, boss.transform.position);
            Log($"보스 상태 active={boss.gameObject.activeInHierarchy} grade={boss.Grade} hp={hp}/{boss.EffectiveMaxHp} " +
                $"dead={boss.IsDead} state={fsm?.CurrentType?.Name ?? "null"} constraints={fsm?.CurrentConstraints} " +
                $"dormant={asleep} dist={dist:0.0}m");
        }

        if (boss.IsDead || hp <= 0) return;
        if (now >= s_bossKillAt)
        {
            s_bossKillAt = now + 1;
            TestHubDebugMenu.KillBoss();
            if (boss.CurrentHp < hp) s_bossHurtAt = now;
        }

        // 8초 넘게 피해가 안 들어간다 — 플레이어를 보스 3m 앞에 세워 본다(감지 반경 밖이면 깨어나지 않는 보스 대비)
        if (now - s_bossHurtAt > 8 && now >= s_bossNudgeAt)
        {
            s_bossNudgeAt = now + 8;
            Vector3 bp  = boss.transform.position;
            Vector3 dir = player.transform.position - bp;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : boss.transform.forward;
            Vector3 to = bp + dir * 3f;
            player.transform.position = to;
            if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = to; rb.linearVelocity = Vector3.zero; }
            Log("보스 피해 없음 8초 — 플레이어를 보스 3m 앞으로");
        }
    }

    private static void HandleWaitingRoom(PlayerController player, double now, int chapter)
    {
        if (chapter != s_waitingChapter) { s_waitingChapter = chapter; s_startGateOpenedAt = -1; }
        foreach (var gate in Object.FindObjectsByType<StartRoomGate>(FindObjectsSortMode.None))
        {
            if ((int)typeof(StartRoomGate).GetField("_fromZoneIndex", Inst).GetValue(gate) != -1) continue;
            if (s_startGateOpenedAt < 0)
            {
                if (!(bool)typeof(StartRoomGate).GetField("_covenantDone", Inst).GetValue(gate))
                    typeof(StartRoomGate).GetMethod("HandleCovenantAssembled", Inst).Invoke(gate, null);
                s_startGateOpenedAt = now;
                Log($"대기방 게이트 열기 ch{chapter}");
            }
            else if (now - s_startGateOpenedAt > 10.0 && gate.TryGetComponent<Collider>(out var col))
            {
                Teleport(player, col.bounds);
                s_startGateOpenedAt = now;   // 다음 시도까지 10초(열림 연출이 끝나야 통과된다)
                Log($"대기방 게이트로 이동 ch{chapter}");
            }
            return;
        }
        if (now >= s_bossLogAt)
        {
            s_bossLogAt = now + 10;
            Log($"대기방인데 시작 게이트(_fromZoneIndex=-1)가 없다 ch{chapter}");
        }
    }

    /// <summary>
    /// 경고·오류를 <b>그 순간의 방</b>에 적어 둔다. 메시지는 앞 60자만 키로 쓴다(같은 경고가 수십 번 난다).
    /// 방마다 다른 종류 5개까지만 — 기록이 로그 파일이 되면 읽지 않게 된다.
    /// </summary>
    /// <summary>방의 경고를 {"메시지 앞머리": 횟수} 로. 없으면 {}.</summary>
    private static string WarnsJson(RoomRec r) => DictJson(r.warns);

    private static string DictJson(Dictionary<string, int> d)
    {
        if (d == null || d.Count == 0) return "{}";
        var sb = new StringBuilder("{");
        bool first = true;
        foreach (var kv in d)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(kv.Key.Replace("\\", "").Replace("\"", "'")).Append("\":").Append(kv.Value);
        }
        return sb.Append('}').ToString();
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (string.IsNullOrEmpty(msg)) return;

        // 방을 짓기 직전 로그 — 지금 「짓는」 방을 기억한다. 방은 들어가기 전에 미리 지어지므로
        // 서 있는 방(s_cur)과 다르다. 이 구분이 없어 경고가 한 칸씩 밀렸다(09-22 보스 세션 지적).
        const string BuildTag = "[GameRunBootstrapper] BlockMap: ";
        if (msg.StartsWith(BuildTag))
        {
            int end = msg.IndexOf(" (", BuildTag.Length, System.StringComparison.Ordinal);
            s_buildTarget = end > 0 ? msg.Substring(BuildTag.Length, end - BuildTag.Length) : msg.Substring(BuildTag.Length);
            return;
        }

        if (type != LogType.Warning && type != LogType.Error && type != LogType.Exception) return;
        if (s_cur == null || msg.StartsWith("[RunAuto]")) return;   // 제 로그는 뺀다

        string key = msg.Length > 60 ? msg.Substring(0, 60) : msg;

        // 짓는 중(토큰·블록)에 난 경고는 짓는 방의 것이다 — 서 있는 방에 붙이지 않는다.
        bool building = stack != null && (stack.Contains("TokenParser") || stack.Contains("MapBuilder"));
        if (building && !string.IsNullOrEmpty(s_buildTarget))
        {
            if (!s_buildWarns.TryGetValue(s_buildTarget, out var bw)) s_buildWarns[s_buildTarget] = bw = new Dictionary<string, int>();
            Bump(bw, key);
            return;
        }
        Bump(s_cur.warns, key);
    }

    /// <summary>경고 횟수 +1. 종류는 5개까지만 — 기록이 로그 파일이 되면 읽지 않게 된다.</summary>
    private static void Bump(Dictionary<string, int> d, string key)
    {
        if (d.TryGetValue(key, out int n)) d[key] = n + 1;
        else if (d.Count < 5) d[key] = 1;
    }

    private static void Teleport(PlayerController player, Bounds b)
    {
        var pos = new Vector3(b.center.x, b.min.y + 0.3f, b.center.z);
        player.transform.position = pos;
        if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
    }

    private static bool SkipDialogue(double now)
    {
        var popup  = Object.FindFirstObjectByType<UI_DialoguePopup>();
        var button = popup != null && popup.isActiveAndEnabled
            ? new SerializedObject(popup).FindProperty("advanceButton").objectReferenceValue as UnityEngine.UI.Button
            : null;
        if (button == null)
        {
            if (s_dialogueSince >= 0) Log($"대사 끝 ({now - s_dialogueSince:0.0}초 · {s_dialogueClicks}번 넘김)");
            s_dialogueSince = -1;
            return false;
        }
        if (s_dialogueSince < 0)
        {
            s_dialogueSince      = now;
            s_dialogueClicks     = 0;
            s_dialogueLongLogged = false;
            Log("대사 시작");
        }
        else if (!s_dialogueLongLogged && now - s_dialogueSince > 20)
        {
            s_dialogueLongLogged = true;
            var tcs = typeof(UI_DialoguePopup).GetField("_advanceTcs", Inst)?.GetValue(popup);
            Log($"대사가 20초 넘게 안 끝남 — clicks={s_dialogueClicks} interactable={button.interactable} " +
                $"tcs={(tcs != null)} typing={typeof(UI_DialoguePopup).GetField("_isTyping", Inst)?.GetValue(popup)} " +
                $"timeScale={Time.timeScale}");
        }
        button.onClick.Invoke();
        s_dialogueClicks++;
        return true;
    }

    /// <summary>
    /// ESC로 닫히지 않는 보상 결정 팝업(파츠 드래프트·룬 선택)은 호출자가 결과를 기다리므로 닫기만 하면 흐름이 멈춘다 —
    /// 사람이 누르는 버튼 경로로 첫 카드를 고르거나 넘긴다.
    /// </summary>
    private static bool TryResolveChoicePopup(UIManager ui, out string how)
    {
        how = null;
        var stack = typeof(UIManager).GetField("_popupStack", Inst)?.GetValue(ui) as Stack<UI_Popup>;
        var top   = stack != null && stack.Count > 0 ? stack.Peek() : null;
        switch (top)
        {
            case UI_RelicPartDraftPopup draft:
                typeof(UI_RelicPartDraftPopup).GetMethod("SetSelected", Inst).Invoke(draft, new object[] { 0 });
                typeof(UI_RelicPartDraftPopup).GetMethod("OnConfirmClicked", Inst).Invoke(draft, null);
                how = "첫 파츠 장착";
                return true;
            case UI_RuneSelectPopup rune:
                typeof(UI_RuneSelectPopup).GetMethod("OnSkipClicked", Inst).Invoke(rune, null);
                how = "룬 선택 넘기기";
                return true;
            default:
                return false;
        }
    }

    private static string TopPopupName(UIManager ui)
    {
        var stack = typeof(UIManager).GetField("_popupStack", Inst)?.GetValue(ui) as Stack<UI_Popup>;
        return stack != null && stack.Count > 0 && stack.Peek() != null ? stack.Peek().GetType().Name : "?";
    }

    private static string Clean(string n)
    {
        int i = n.IndexOf('(');
        return (i > 0 ? n.Substring(0, i) : n).Trim();
    }

    /// <summary>콘솔과 결과 파일 양쪽에 경과 시각과 함께 남긴다.</summary>
    private static void Log(string msg)
    {
        string line = $"t={EditorApplication.timeSinceStartup - s_start:0.0} {msg}";
        Debug.Log("[RunAuto] " + line);
        if (s_events.Length > 0) s_events.Append(" | ");
        s_events.Append(line);
    }

    private static void Finish(string reason)
    {
        if (!s_running) return;
        s_running = false;
        Application.logMessageReceived -= OnLog;
        EditorApplication.update -= Tick;
        double end = EditorApplication.timeSinceStartup;
        if (s_cur != null && s_cur.leaveT < 0) s_cur.leaveT = end;

        var sb = new StringBuilder();
        sb.Append("{\"reason\":\"").Append(reason).Append("\",\"seconds\":").Append((end - s_start).ToString("0.0"))
          .Append(",\"events\":\"").Append(s_events.ToString().Replace("\"", "'")).Append("\",\"rooms\":[");
        for (int i = 0; i < s_rooms.Count; i++)
        {
            var r = s_rooms[i];
            if (i > 0) sb.Append(',');
            int c = 0, rr = 0, e = 0, b = 0, hp = 0;
            var species = new Dictionary<string, int>();
            foreach (var m in r.mons.Values)
            {
                hp += m.hp;
                switch (m.grade)
                {
                    case MonsterGrade.Common: c++; break;
                    case MonsterGrade.Rare:   rr++; break;
                    case MonsterGrade.Elite:  e++; break;
                    case MonsterGrade.Boss:   b++; break;
                }
                string sk = m.grade.ToString()[0] + ":" + m.name;
                species[sk] = species.TryGetValue(sk, out int n) ? n + 1 : 1;
            }
            var sp = new StringBuilder();
            foreach (var kv in species) { if (sp.Length > 0) sp.Append(','); sp.Append(kv.Key).Append('x').Append(kv.Value); }

            sb.Append("{\"order\":").Append(r.order)
              .Append(",\"ch\":").Append(r.chapter)
              .Append(",\"visit\":").Append(r.visit)
              .Append(",\"kind\":\"").Append(r.kind)
              .Append("\",\"pool\":\"").Append(r.pool)
              .Append("\",\"cat\":\"").Append(r.category)
              .Append("\",\"diff\":").Append(r.diff.ToString("0.00"))
              .Append(",\"toClear\":").Append(r.clearT >= 0 ? (r.clearT - r.enterT).ToString("0.0") : "-1")
              .Append(",\"stay\":").Append(r.leaveT >= 0 ? (r.leaveT - r.enterT).ToString("0.0") : "-1")
              .Append(",\"reward\":").Append(r.rewardT > 0 ? r.rewardT.ToString("0.0") : "0")
              .Append(",\"warns\":").Append(WarnsJson(r))
              .Append(",\"C\":").Append(c).Append(",\"R\":").Append(rr).Append(",\"E\":").Append(e).Append(",\"B\":").Append(b)
              .Append(",\"hp\":").Append(hp)
              .Append(",\"species\":\"").Append(sp)
              .Append("\",\"offered\":\"").Append(r.offered)
              .Append("\",\"chosen\":\"").Append(r.chosen)
              .Append("\",\"notes\":\"").Append(r.notes.Trim()).Append("\"}");
        }
        sb.Append("],\"buildWarns\":{");
        bool firstB = true;
        foreach (var kv in s_buildWarns)
        {
            if (!firstB) sb.Append(',');
            firstB = false;
            sb.Append('"').Append(kv.Key).Append("\":").Append(DictJson(kv.Value));
        }
        sb.Append("}}");
        string json = sb.ToString();
        File.WriteAllText(Path.Combine("Temp", "run_structure_autoplay.json"), json);
        // 같은 이름 하나뿐이면 다른 세션의 런이 덮어쓴다(09-24 실제로 유실) — 시각 붙은 사본을 남긴다.
        File.WriteAllText(Path.Combine("Temp", $"run_structure_autoplay_{System.DateTime.Now:yyyyMMdd_HHmmss}.json"), json);
        Debug.Log($"[RunAuto] 종료({reason}) 방 {s_rooms.Count} · {(end - s_start):0}초");
    }
}
