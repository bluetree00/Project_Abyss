using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
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

        // 방 시간표(10-01 방 체류 압축 분해) — 절대 시각(EditorApplication.timeSinceStartup), 없으면 -1.
        // 이동 = 방을 다 짓고 플레이어를 옮김 · 활성 = 입장 연출·봉인이 끝나 웨이브 시작 · 열림 = 출구가 처음 armed.
        public double tMove = -1, tAct = -1, tSpawn0 = -1, tSpawnN = -1, tKillN = -1, tClear = -1, tReward = -1, tExitOpen = -1, tExitGo = -1;
        public int    spawns, deaths, corpseMax;
        public double budgetWait;                          // 스폰 중 동시 상한(MonsterBudget)에 막혀 있던 누적 초
        public readonly List<double> deathToPool = new();  // 사망 → 풀 반환(비활성)까지 초 — 그동안 상한 한 칸을 차지한다
        public int    hpKill, hpEff;                       // 처치 직전 CurrentHp 합(실제 체력) · 그때 EffectiveMaxHp 합
        // 입장 하위 단계(RunFlowController 「입장 단계」 로그) — 덮기 · 짓기 · 드러내기 · 디졸브 대기 · 대사 · 부감·봉인. 없으면 null
        public double[] entry;
        public bool     quickEntry;
    }

    private static readonly List<RoomRec> s_rooms = new();
    private static RoomRec s_cur;
    private static string  s_curKey;
    private static double  s_next, s_start, s_blockedSince = -1, s_startGateOpenedAt = -1, s_bossStepAt, s_chapterGateSeenAt = -1, s_stairStepAt = -1;
    private static double  s_dialogueSince = -1, s_noRunSince = -1, s_bossLogAt, s_bossKillAt, s_bossHurtAt, s_bossNudgeAt;
    private static double  s_rewardAt = -1;   // 보상 위로 옮긴 시각(팝업 대기 중)
    private static bool    s_rewardPressed;   // [F]를 대신 눌렀는지
    private static bool    s_holdProbe;       // 출구 보류 확인 모드 — 처치 위치를 닿을 수 없는 자리로 바꾸고, 보상을 늦게 받는다
    /// <summary>확인 모드에서 [F]를 누르기까지 기다리는 시간(초). 「보상을 받으면 길이 열린다」 안내(6초)가 뜬 뒤에 받는다.</summary>
    private const double   HoldProbePressDelay = 8.0;
    private static Vector3 s_probeKillPos;    // 확인 모드에서 「마지막 처치 위치」로 넣은 점
    private static ProcRoomGate s_probeDoorGate; // 확인 모드에서 보상을 받는 동안 서 있던 문 — 열리면 그 문으로 나간다
    private static ProcRoomGate s_exitGate;   // 지금 밟은 출구
    private static double  s_exitAt = -1;     // 출구를 밟은 시각(안 넘어가면 다시 밟는다)
    private static bool    s_timeline;        // 방 시간표 모드 — 정예 문 우선 · 로컬 방 풀 우선(끝나면 원래 값으로)
    private static bool    s_prevPreferLocal; // 시간표 모드가 켜기 전 「로컬 방 풀 우선」 값
    private static MinigameDemo s_prevDemo;   // 시간표 모드가 켜기 전 놀이 자동 시연 값(로컬 풀엔 새 놀이 방이 섞인다)
    // 방 시간표 — 매 에디터 프레임 이벤트로 찍는다(틱 0.25초로는 스폰 간격 0.15~0.4초를 못 가른다)
    private static GameObject s_tlRoom;
    private static readonly List<MonsterSpawner> s_tlSpawners = new();
    private static RoomWaveController s_tlWave;
    private static readonly Dictionary<MonsterBase, double> s_tlDead = new();   // 죽었지만 아직 풀에 안 돌아간 몬스터 → 사망 시각
    private static readonly List<MonsterBase> s_tlDone = new();
    private static readonly HashSet<int> s_tlKilled = new();
    private static double  s_tlLast = -1;
    private static readonly FieldInfo s_fCurrent   = typeof(RunFlowController).GetField("_current", Inst);
    private static readonly FieldInfo s_fSequencer = typeof(RunFlowController).GetField("_sequencer", Inst);
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

    /// <summary>
    /// 「보상을 받아야 출구가 열린다」 확인용.
    ///   · 방마다 「마지막 처치 위치」를 닿을 수 없는 자리로 바꿔 둔다(짝수 방 = 맵 밖 60 m, 홀수 방 = 공중 1.5 m) → 보상 자리 보정
    ///   · 보상 쪽으로 옮겨 가지 않고 8초 뒤에 받는다 → 보정된 보상이 플레이어에게서 얼마나 떨어져 섰는지, 그동안 출구가 닫혀 있는지, 안내가 뜨는지
    /// 보정이 안 되면 런이 그 방에서 멈추고(notes: reward-stuck), 받기 전에 출구가 열리면 notes에 exit-open-before-reward가 남는다.
    /// </summary>
    [MenuItem(Root + "시작 — 출구 보류 확인 (처치 위치 맵 밖 · 보상 늦게 받기)")]
    private static void BeginHoldProbe() => BeginWith(false, 1, holdProbe: true);

    /// <summary>
    /// 방 시간표(10-01 방 체류 압축) — 방마다 이동 → 활성 → 첫 스폰 → 마지막 스폰 → 마지막 처치 → 클리어 → 보상 → 출구 열림 → 출구 → 다음 방을
    /// 프레임 단위로 찍는다(처치도 매 프레임 = 즉사 바닥). 정예 문이 열리면 정예로 가 정예방을 모으고,
    /// 방 풀은 업로드 예정인 로컬 CSV로 읽는다(「로컬 방 풀 우선」을 켰다가 끝나면 원래 값으로).
    /// 결과: Temp/room_timeline.txt(챕터 × 일반·정예·이벤트 단계 중앙값) + 평소 json(방마다 tl 필드).
    /// ⚠️ 새 플레이에서 시작할 것 — 방 풀은 한 번 읽으면 캐시되어 도중에 켠 「로컬 우선」이 안 먹는다.
    /// </summary>
    [MenuItem(Root + "시작 — 방 시간표 (정예 우선 · 로컬 방 풀)")]
    private static void BeginTimeline() => BeginWith(false, 1, timeline: true);

    /// <summary>방 시간표 Ch4만 — 기억의 제단 「Ch4 해금」 전 세이브는 Ch3 보스에서 런이 끝나 Ch4를 못 잰다.</summary>
    [MenuItem(Root + "시작 — 방 시간표 Ch4부터 (정예 우선 · 로컬 방 풀)")]
    private static void BeginTimelineCh4() => BeginWith(false, 4, timeline: true);

    private static void BeginWith(bool bossApproach, int chapter, bool holdProbe = false, bool timeline = false)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[RunAuto] 플레이 모드에서만"); return; }
        var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Debug.LogWarning("[RunAuto] 테스트 허브가 아니다"); return; }
        var t = typeof(TestHubLauncher);
        t.GetField("_chapter", Inst).SetValue(launcher, chapter);
        t.GetField("_bossApproach", Inst).SetValue(launcher, bossApproach);
        if (timeline)
        {
            s_prevPreferLocal = EditorPrefs.GetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, false);
            EditorPrefs.SetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, true);
        }
        if (!launcher.TryLaunch())
        {
            if (timeline) EditorPrefs.SetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, s_prevPreferLocal);
            Debug.LogWarning("[RunAuto] 시작 실패(초기화 중?)");
            return;
        }

        s_rooms.Clear();
        s_events.Clear();
        s_cur = null;
        s_curKey = null;
        s_rng = new System.Random(20260918);
        s_start = EditorApplication.timeSinceStartup;
        s_next = s_start + 3;
        s_blockedSince = s_startGateOpenedAt = s_chapterGateSeenAt = s_dialogueSince = s_noRunSince = -1;
        s_rewardAt = -1;
        s_holdProbe = holdProbe;
        s_timeline = timeline;
        if (timeline)
        {
            // 로컬 방 풀엔 새 놀이 3종이 섞인다 — 자동 실측은 놀이를 못 하므로 「완벽」 시연으로 돌게 한다(끝나면 원래 값으로).
            s_prevDemo = EventMinigame.DebugDemo;
            EventMinigame.DebugDemo = MinigameDemo.Perfect;
        }
        UnsubscribeTimeline();
        s_tlDead.Clear();
        s_tlKilled.Clear();
        s_tlLast = -1;
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
        Log($"시작 ch{chapter}" + (bossApproach ? " (보스 대기방부터)" : "") + (holdProbe ? " (출구 보류 확인)" : "") + (timeline ? " (방 시간표 · 정예 우선 · 로컬 방 풀)" : ""));
    }

    /// <summary>
    /// 이벤트방 뽑기 시뮬레이션 — 챕터마다 런 2000번을 RunSequencer로만 굴려(방은 짓지 않는다) 이벤트방이 어떤 방으로 뽑히는지 센다.
    /// 문은 둘 중 무작위로 고른다(자동 실측과 같은 가정). 방 풀·런 구조는 실제 게임과 같은 경로(CDN → 로컬)로 읽는다.
    /// 상호작용방(난이도 0 — 도박·신탁·희생·금고·성역)의 비중과, 한 런에 두 번 이상 · 같은 방 두 번 나오는 빈도를 본다.
    /// 결과: Temp/event_pick_sim.txt
    /// </summary>
    [MenuItem(Root + "이벤트방 뽑기 시뮬레이션 (플레이 중 · 챕터별 2000런)")]
    private static void SimulateEventPicks()
    {
        if (!Application.isPlaying || Managers.ZoneLayout == null) { Debug.LogWarning("[RunAuto] 플레이 모드(매니저 초기화 뒤)에서만"); return; }
        SimulateEventPicksAsync().Forget();
    }

    private static async UniTaskVoid SimulateEventPicksAsync()
    {
        const int Runs = 2000;
        // 런 구조(RUN_STRUCTURE)는 런을 시작해야 읽힌다 — 허브에서 바로 부르면 비어 있어 전 방 Normal로 돈다.
        var structures = Managers.RunStructureData;
        if (structures != null && !structures.IsInitialized) await structures.InitializeAsync();
        var sb = new StringBuilder($"[이벤트방 뽑기 시뮬레이션] 챕터별 {Runs}런 · 문은 무작위{System.Environment.NewLine}");
        for (int ch = 1; ch <= 4; ch++)
        {
            var pool      = await Managers.ZoneLayout.LoadPoolAsync($"CHAPTER_{ch}_ROOM_POOL");
            var structure = structures?.Get((ChapterId)ch);
            if (pool == null || pool.Count == 0) { sb.AppendLine($"Ch{ch}: 방 풀 없음"); continue; }

            var counts = new Dictionary<string, int>();
            int events = 0, interaction = 0, runsTwoPlus = 0, runsSameTwice = 0;
            var rng = new System.Random(1000 + ch);
            for (int run = 0; run < Runs; run++)
            {
                var seq  = new RunSequencer(pool, structure, rng.Next(), 0);
                var seen = new HashSet<string>();
                int inRun = 0;
                bool same = false;
                for (int step = 0; step < 200 && !seq.IsDone; step++)
                {
                    var exits = seq.RollExits();
                    if (exits == null || exits.Count == 0) break;
                    var pick = exits[rng.Next(exits.Count)];
                    seq.CommitEntry(pick);
                    if (pick.kind != RoomPlanKind.Event || pick.entry == null) continue;

                    events++;
                    string k = pick.entry.pool_key;
                    counts[k] = counts.TryGetValue(k, out int n) ? n + 1 : 1;
                    if (pick.entry.difficulty_scale > 0f) continue;
                    interaction++;
                    inRun++;
                    if (!seen.Add(k)) same = true;
                }
                if (inRun >= 2) runsTwoPlus++;
                if (same) runsSameTwice++;
            }

            sb.AppendLine($"Ch{ch}: 이벤트방 {events}회(런당 {events / (float)Runs:0.00}) · 상호작용 {interaction}회({(events > 0 ? interaction * 100f / events : 0f):0.0}%) · " +
                          $"한 런에 상호작용 2회 이상 {runsTwoPlus * 100f / Runs:0.0}% · 같은 상호작용방 2회 {runsSameTwice * 100f / Runs:0.0}%");
            var list = new List<KeyValuePair<string, int>>(counts);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            foreach (var kv in list) sb.AppendLine($"    {kv.Key} {kv.Value}");
        }
        string text = sb.ToString();
        File.WriteAllText(Path.Combine("Temp", "event_pick_sim.txt"), text);
        Debug.Log("[RunAuto] " + text);
    }

    [MenuItem(Root + "중지·기록")]
    private static void StopAndWrite() => Finish("manual");

    private static void Tick()
    {
        if (!s_running) { EditorApplication.update -= Tick; return; }
        if (!Application.isPlaying) { Finish("play-stopped"); return; }
        double now = EditorApplication.timeSinceStartup;
        TimelineFrame(now);   // 매 프레임 — 방 기록 · 즉사 처치 · 시간표
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
        int chapter = (int)run.CurrentChapter;
        string key = SyncRoomKey(run, rfc, now);
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

        // 5) 몬스터 — 기록·즉시 처치는 매 프레임 TimelineFrame이 한다(예전엔 여기서 0.25초 틱마다). 보스는 6)에서 따로.
        var spawner = Object.FindFirstObjectByType<BossSpawner>();
        var boss    = spawner != null ? spawner.SpawnedBoss : null;

        // 5-b) 보상 자리 보정 확인 — 보상은 클리어 0.8초 뒤 「마지막 처치 위치」에 선다. 그 값을 닿을 수 없는 자리로 바꿔 둔다.
        //      처치가 값을 다시 덮어쓰므로 매 틱 다시 쓴다(틱 0.25초 < 0.8초라 마지막 처치 뒤에도 한 번은 들어간다).
        if (s_holdProbe)
        {
            s_probeKillPos = player.transform.position + (s_cur.order % 2 == 0 ? new Vector3(60f, 25f, 0f) : new Vector3(0f, 1.5f, 0f));
            foreach (var wave in Object.FindObjectsByType<RoomWaveController>(FindObjectsSortMode.None))
            {
                typeof(RoomWaveController).GetField("_lastKillPosition", Inst).SetValue(wave, s_probeKillPos);
                typeof(RoomWaveController).GetField("_hasKillPosition", Inst).SetValue(wave, true);
            }
        }

        // 6) 보스방 — 입구 트리거를 밟아 보스를 부르고, 나오면 쓰러뜨린다
        if (s_cur.kind == nameof(RoomPlanKind.Boss) && s_cur.clearT < 0 && GameObject.Find("@ChapterGate") == null && GameObject.Find("@ChapterStairway") == null)
            HandleBoss(player, spawner, boss, now);

        // 7) 상호작용 이벤트 — 2초 뒤 해결
        foreach (var ch in Object.FindObjectsByType<WorldInteractionChallenge>(FindObjectsSortMode.None))
        {
            var resolved = (bool)typeof(WorldInteractionChallenge).GetProperty("IsResolved", Inst).GetValue(ch);
            // 방 입장 연출이 끝나 RunFlowController가 OnResolved를 구독한 뒤에만 해결한다 — 그 전에 해결하면 클리어 통지를
            // 놓쳐 출구가 영영 안 열린다(10-01 방 시간표 Ch3 희생 제단에서 런이 멈췄다). 사람은 입력이 풀리는 프레임에 구독이 끝나 못 겪는다.
            bool listened = typeof(WorldInteractionChallenge).GetField("OnResolved", Inst)?.GetValue(ch) != null;
            if (resolved || !listened || now - s_cur.enterT < 2.0) continue;
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

        // 8b) 층계 회랑(S5, 10-02) — 계단 꼭대기 → (회랑으로 옮겨짐) → 회랑 끝 문. 걷기 대신 트리거로 옮긴다.
        var stairway = GameObject.Find("@ChapterStairway");
        if (stairway != null && !s_chapterGateTaken)
        {
            if (s_cur.clearT < 0) { s_cur.clearT = now; Log($"클리어 {key} (층계)"); }
            if (s_chapterGateSeenAt < 0) { s_chapterGateSeenAt = now; return; }
            if (now - s_chapterGateSeenAt < 1.5 || now - s_stairStepAt < 2.0) return;
            var corridor = GameObject.Find("@StairCorridor");
            bool inCorridor = corridor != null && player.transform.position.y > stairway.transform.position.y + 100f;
            var target = inCorridor ? corridor.transform.Find("DoorTrigger") : stairway.transform.Find("TopTrigger");
            if (target != null && target.TryGetComponent<Collider>(out var sc))
            {
                Teleport(player, sc.bounds);
                s_stairStepAt = now;
                if (inCorridor) { s_chapterGateTaken = true; s_cur.chosen = "ChapterStairway"; Log($"층계 회랑 통과 ch{chapter}"); }
                else Log($"계단 꼭대기로 ch{chapter}");
            }
            return;
        }

        // 9) 보상 줍기 — 클리어 보상을 받아야 출구가 열린다(수령 전 출구 보류, 09-30). 그래서 출구 검사보다 <b>먼저</b> 한다.
        //    예전 순서(출구가 열린 뒤 보상)로는 출구가 영영 안 열려 런이 멈춘다.
        //    팝업 자체는 위의 차단 팝업 처리가 눌러 준다 — 여기서는 <b>밟아 주고 [F]만 대신 누른다</b>.
        var reward = Object.FindFirstObjectByType<ClearRewardTrigger>();
        if (reward != null)
        {
            // 보상은 클리어 뒤 연출(약 2초)이 끝나야 바닥에 생긴다 — 보상 방의 「클리어」 시각은 예전(출구 공개 시점)보다 그만큼 늦게 찍힌다.
            if (s_cur.clearT < 0) { s_cur.clearT = now; Log($"클리어 {key} (보상 등장)"); }
            if (s_holdProbe && !s_cur.notes.Contains("exit-open-before-reward"))
                foreach (var g in Object.FindObjectsByType<ProcRoomGate>(FindObjectsSortMode.None))
                {
                    if (!g.IsArmed) continue;
                    s_cur.notes += "exit-open-before-reward ";
                    Log("보상을 안 받았는데 출구가 열려 있다");
                    break;
                }
            if (s_rewardAt < 0)
            {
                s_rewardAt = now;
                Vector3 rp = reward.transform.position;
                if (s_holdProbe)
                {
                    // 확인 모드: 옮겨 가지 않는다 — 보정된 보상이 플레이어에게서 얼마나 떨어져 섰는지가 확인 대상이다.
                    // 넣어 둔 처치 위치에서 가장 가까운 NavMesh와, 플레이어 자리에서 거기까지 길이 이어지는지도 같이 적는다(보정이 어느 갈래를 탔는지).
                    var nav = new StringBuilder();
                    for (int i = 0; i < UnityEngine.AI.NavMesh.GetSettingsCount(); i++)
                    {
                        var filter = new UnityEngine.AI.NavMeshQueryFilter
                        {
                            agentTypeID = UnityEngine.AI.NavMesh.GetSettingsByIndex(i).agentTypeID,
                            areaMask    = UnityEngine.AI.NavMesh.AllAreas,
                        };
                        nav.Append($" · 에이전트 {filter.agentTypeID}: ");
                        if (!UnityEngine.AI.NavMesh.SamplePosition(s_probeKillPos, out var near, 4f, filter)) { nav.Append("4m 안에 없음"); continue; }
                        var path = new UnityEngine.AI.NavMeshPath();
                        bool ok = UnityEngine.AI.NavMesh.SamplePosition(player.transform.position, out var self, 4f, filter)
                                  && UnityEngine.AI.NavMesh.CalculatePath(self.position, near.position, filter, path);
                        nav.Append($"가까운 면 {near.position} 길 {(ok ? path.status.ToString() : "계산 실패")}");
                    }
                    Log($"보상 등장 — 플레이어에서 {Vector3.Distance(rp, player.transform.position):0.0}m · 처치 위치 {s_probeKillPos}{nav}");

                    // 문간(개구부 선에서 방 쪽 1 m)에 서서 보상을 받는다 — 문이 열린 뒤 그 문으로 나갈 때
                    // 「들어옴」이 오는지 본다(트리거가 방 안까지 걸치면 이미 안에 있어 안 넘어간다 — 10-01 Ch4 멈춤).
                    s_probeDoorGate = Object.FindFirstObjectByType<ProcRoomGate>();
                    if (s_probeDoorGate != null && s_probeDoorGate.TryGetComponent<Collider>(out var dc))
                    {
                        Vector3 line = s_probeDoorGate.transform.position;
                        line.y = dc.bounds.min.y;
                        Place(player, line + RoomInward(rfc, line) * 1f + Vector3.up * 0.3f);
                        Log($"문간에 섬 — 문 {line}에서 방 쪽 1m");
                    }
                    return;
                }
                player.transform.position = rp;
                if (player.TryGetComponent<Rigidbody>(out var prb)) { prb.position = rp; prb.linearVelocity = Vector3.zero; }
                Log("보상으로 이동");
                return;
            }

            // 근접만으로는 안 열린다 — 게임은 [F] 입력을 기다린다(ClearRewardTrigger.Update의 Input.GetKeyDown).
            // 실측은 키를 누를 수 없으니 <b>그 한 지점만</b> 대신 누른다. 이후(팝업·선택·보관함)는 게임 경로 그대로다.
            if (!s_rewardPressed && now - s_rewardAt > (s_holdProbe ? HoldProbePressDelay : 1.0))
            {
                s_rewardPressed = true;
                var mi = typeof(ClearRewardTrigger).GetMethod("OpenRewardFlowAsync", Inst);
                if (mi != null) { mi.Invoke(reward, null); Log("보상 열기([F] 대신)"); }
                else Log("보상 열기 실패 — OpenRewardFlowAsync를 못 찾았다");
            }
            // 예전엔 6초 뒤 포기하고 출구로 갔지만 이제는 보상을 받아야 출구가 열린다 — 포기할 수 없으니 흔적만 남긴다.
            else if (now - s_rewardAt > 20 && !s_cur.notes.Contains("reward-stuck"))
            {
                s_cur.notes += "reward-stuck ";
                Log("보상 수령이 20초 넘게 안 끝난다 — 출구가 안 열린다");
            }
            return;   // 수령이 끝나면 보상 오브젝트가 사라지고, 그때 출구가 열린다
        }
        if (s_rewardAt >= 0)
        {
            s_cur.rewardT = now - s_rewardAt;
            Log($"보상 처리 {s_cur.rewardT:F1}초");
            s_rewardAt = -1;
            s_rewardPressed = false;
        }

        // 10) 출구 — 열리면 0.8초 뒤 무작위로 하나
        var armed = new List<ProcRoomGate>();
        foreach (var g in Object.FindObjectsByType<ProcRoomGate>(FindObjectsSortMode.None))
            if (g.IsArmed) armed.Add(g);
        if (armed.Count == 0) return;
        if (s_cur.clearT < 0) { s_cur.clearT = now; Log($"클리어 {key}"); }
        if (now - s_cur.clearT < 0.8) return;
        if (!string.IsNullOrEmpty(s_cur.chosen))
        {
            CheckExitStuck(player, rfc, now);
            return;
        }

        var names = new List<string>();
        foreach (var g in armed) names.Add(g.Kind.ToString());
        s_cur.offered = string.Join("/", names);
        // 확인 모드는 보상을 받는 동안 서 있던 문으로 나간다(문간 재현)
        // 시간표 모드는 정예 문이 있으면 그리로 — 정예방을 챕터마다 여럿 모은다
        var elite = s_timeline ? armed.Find(g => g.Kind == RoomPlanKind.Elite) : null;
        var pick = s_holdProbe && s_probeDoorGate != null && armed.Contains(s_probeDoorGate)
            ? s_probeDoorGate
            : elite != null ? elite : armed[s_rng.Next(armed.Count)];
        s_cur.chosen = pick.Kind.ToString();
        s_exitGate = pick;
        s_exitAt = now;
        s_cur.tExitGo = now;
        if (pick.TryGetComponent<Collider>(out var col)) Teleport(player, col.bounds);
        Log($"출구 {s_cur.offered} → {s_cur.chosen}");
    }

    /// <summary>
    /// 출구를 밟았는데 5초가 지나도 방이 안 바뀌면 — 트리거 안에 이미 서 있던 채로 문이 열리면 「들어옴」이 다시 오지 않는다(10-01 Ch4 9분 멈춤).
    /// 흔적(notes: exit-stuck)을 남기고, 방 쪽으로 4 m 물렸다가 1초 뒤 다시 밟는다(한 번).
    /// </summary>
    private static void CheckExitStuck(PlayerController player, RunFlowController rfc, double now)
    {
        if (s_exitGate == null || s_exitAt < 0) return;
        double waited = now - s_exitAt;
        if (waited > 5 && !s_cur.notes.Contains("exit-stuck"))
        {
            s_cur.notes += "exit-stuck ";
            Vector3 line = s_exitGate.transform.position;
            line.y = player.transform.position.y;
            Place(player, line + RoomInward(rfc, line) * 4f);
            Log($"출구를 밟았는데 5초 동안 안 넘어간다 — 방 쪽으로 물렸다가 다시 밟는다 (문 {line})");
        }
        else if (waited > 6 && s_cur.notes.Contains("exit-stuck") && !s_cur.notes.Contains("exit-retry")
                 && s_exitGate.TryGetComponent<Collider>(out var col))
        {
            s_cur.notes += "exit-retry ";
            Teleport(player, col.bounds);
            Log("출구 다시 밟기");
        }
    }

    /// <summary>문 자리에서 방 중심(방 루트) 쪽으로 가는 수평 방향.</summary>
    private static Vector3 RoomInward(RunFlowController rfc, Vector3 doorPos)
    {
        var cur    = typeof(RunFlowController).GetField("_current", Inst).GetValue(rfc) as ProcRoomResult;
        Vector3 to = (cur?.roomGO != null ? cur.roomGO.transform.position : doorPos) - doorPos;
        to.y = 0f;
        return to.sqrMagnitude > 0.01f ? to.normalized : Vector3.back;
    }

    private static void Place(PlayerController player, Vector3 pos)
    {
        player.transform.position = pos;
        if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
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
                // 실제로 서약을 맺는다(예전엔 문만 강제로 열어 서약 0개로 돌았다 — Ch2+ 서약 불발 · 대기방 소프트락이 가려졌다, 10-02).
                // 맺을 수 없는 대기방(가득 참 · 부서진 맹세)은 손대지 않고 문이 스스로 열리는지 본다.
                if (!(bool)typeof(StartRoomGate).GetField("_covenantDone", Inst).GetValue(gate))
                {
                    if (WorldCovenantAltar.AssembleBlockedHere)
                        Log($"대기방 서약 불가(가득 · 부서진 맹세) — 문이 스스로 열리는지 본다 ch{chapter}");
                    else
                    {
                        string id = AutoAssembleCovenant(GameRunBootstrapper.Instance?.Run);
                        Log($"대기방 서약 {(id != null ? "맺음 " + id : "실패 — 문만 연다")} ch{chapter}");
                        typeof(StartRoomGate).GetMethod("HandleCovenantAssembled", Inst).Invoke(gate, null);
                    }
                }
                LogCovenantBinding(GameRunBootstrapper.Instance?.Run, chapter);
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

        // 방 시간표 — 방을 다 짓고 플레이어를 옮긴 때 · 입장 연출이 끝나 웨이브가 시작한 때
        if (s_cur != null && type == LogType.Log)
        {
            if (s_cur.tMove < 0 && msg.StartsWith("[RunFlow] 플레이어 이동")) s_cur.tMove = EditorApplication.timeSinceStartup;
            else if (s_cur.entry == null && msg.StartsWith("[RunFlow] 입장 단계")) ParseEntrySteps(msg, s_cur);
            else if (s_cur.tAct < 0 && (msg.StartsWith("[RoomWave] 웨이브 모드 활성화") || msg.StartsWith("[RoomWave] 레거시 모드 활성화")))
                s_cur.tAct = EditorApplication.timeSinceStartup;
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
    /// <summary>
    /// 제단 팝업 없이 서약서에 한 줄 — 문장이 없으면 첫 원인 × 첫 효과(첫 쓰기), 있으면 이어 쓰기 첫 카드(설계서 §7-2 ⑤).
    /// 가득 찬 문장은 고쳐 쓰지 않는다(그대로 둔다 — 문은 새긴 것으로 친다). 실패면 null.
    /// </summary>
    private static string AutoAssembleCovenant(GameRunSession run)
    {
        var handler = run?.CovenantHandler;
        if (handler == null) return null;
        var sentence = handler.Sentence;
        if (sentence == null)
        {
            CovenantAssembleService.DraftBoard(3, null, false, handler.Covenants, out var causes, out var effects, CovenantBuildStatus.Collect(run));
            if (causes == null || effects == null || causes.Count == 0 || effects.Count == 0) return null;
            string first = CovenantSentence.MakeId(causes[0].id, causes[0].tier,
                new List<(string, CovenantTier, ClauseLink)> { (effects[0].id, effects[0].tier, ClauseLink.Immediate) });
            return handler.TryWriteSentence(first) ? first : null;
        }

        var effectsNow = new List<string>();
        var links = new List<ClauseLink>();
        var parts = new List<(string, CovenantTier, ClauseLink)>();
        for (int i = 0; i < sentence.ResultCount; i++)
        {
            effectsNow.Add(sentence.ResultId(i)); links.Add(sentence.ResultLink(i));
            parts.Add((sentence.ResultId(i), sentence.ResultTier(i), sentence.ResultLink(i)));
        }
        var cards = CovenantSentenceService.DraftAppend(sentence.CauseId, effectsNow, links, 3, null, CovenantBuildStatus.Collect(run), MemoryAltarService.CovenantPartStep);
        if (cards.Count == 0) return sentence.CovenantId;   // 가득 참 — 그대로
        parts.Add((cards[0].effectId, cards[0].tier, cards[0].link));
        string id = CovenantSentence.MakeId(sentence.CauseId, sentence.CauseTier, parts);
        return handler.TryWriteSentence(id) ? id : null;
    }

    /// <summary>보유 서약마다 문맥이 지금 플레이어에 묶였는지 — Ch2+ 서약 불발(문맥이 Ch1 플레이어에 남음) 확인용(10-02).</summary>
    private static void LogCovenantBinding(GameRunSession run, int chapter)
    {
        var handler = run?.CovenantHandler;
        if (handler == null || handler.Covenants.Count == 0) { Log($"서약 문맥 ch{chapter}: 보유 0"); return; }
        var ctxProp = typeof(CovenantBase).GetProperty("Ctx", Inst);
        int bound = 0, stale = 0;
        foreach (var c in handler.Covenants)
        {
            var ctx = ctxProp?.GetValue(c) as CovenantContext;
            bool ok = ctx != null && ctx.Player != null && ctx.Player == run.Player && ctx.Stats == run.Player.RuntimeStats;
            if (ok) bound++; else stale++;
        }
        Log($"서약 문맥 ch{chapter}: 보유 {handler.Covenants.Count} · 지금 플레이어에 묶임 {bound} · 옛 플레이어 {stale}");
    }

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
        UnsubscribeTimeline();
        if (s_timeline)
        {
            EditorPrefs.SetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, s_prevPreferLocal);
            EventMinigame.DebugDemo = s_prevDemo;
            Debug.Log($"[RunAuto] 로컬 방 풀 우선 → {s_prevPreferLocal} · 놀이 시연 → {s_prevDemo}(원래 값으로)");
        }
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
              .Append(",\"hpKill\":").Append(r.hpKill)
              .Append(",\"hpEff\":").Append(r.hpEff)
              .Append(",\"tl\":").Append(TimelineJson(r))
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
        if (s_timeline)
        {
            string table = TimelineTable(reason);
            File.WriteAllText(Path.Combine("Temp", "room_timeline.txt"), table);
            Debug.Log("[RunAuto] " + table);
            s_timeline = false;
        }
        Debug.Log($"[RunAuto] 종료({reason}) 방 {s_rooms.Count} · {(end - s_start):0}초");
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 방 시간표
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>(챕터, 방문 순번)이 바뀌었으면 새 방 기록을 연다. 매 프레임(TimelineFrame)과 틱 양쪽에서 부른다.</summary>
    private static string SyncRoomKey(GameRunSession run, RunFlowController rfc, double now)
    {
        var seq     = s_fSequencer.GetValue(rfc) as RunSequencer;
        int visit   = seq != null ? seq.VisitCount : -1;
        int chapter = (int)run.CurrentChapter;
        string key  = chapter + ":" + visit;
        if (key == s_curKey) return key;

        if (s_cur != null && s_cur.leaveT < 0) s_cur.leaveT = now;
        s_cur = new RoomRec { order = s_rooms.Count, chapter = chapter, visit = visit, enterT = now };
        s_rewardAt = -1;
        s_rewardPressed = false;
        s_probeDoorGate = null;
        s_exitGate = null;
        s_exitAt = -1;
        s_tlDead.Clear();
        s_tlKilled.Clear();
        s_rooms.Add(s_cur);
        s_curKey = key;
        s_bossStep = 0;
        s_bossStepAt = now + 2.5;
        s_bossSeen = false;
        s_bossLogAt = s_bossKillAt = s_bossHurtAt = s_bossNudgeAt = now;
        if (s_chapterGateTaken) { s_chapterGateTaken = false; s_chapterGateSeenAt = -1; }
        Log($"방 진입 {key}");
        return key;
    }

    /// <summary>
    /// 매 에디터 프레임 — 방 기록을 맞추고, 보스가 아닌 몬스터를 보이는 즉시 처치하고(즉사 바닥), 시간표 사건을 찍는다.
    /// 처치 직전 CurrentHp가 실제 체력이다(EffectiveMaxHp는 초기화 도중 읽으면 난이도 배율 전 값일 수 있다).
    /// </summary>
    private static void TimelineFrame(double now)
    {
        double dt = s_tlLast > 0 ? now - s_tlLast : 0;
        s_tlLast = now;

        var run = GameRunBootstrapper.Instance?.Run;
        var rfc = RunFlowController.Active;
        if (run == null || run.Player == null || rfc == null) return;
        var cur = s_fCurrent.GetValue(rfc) as ProcRoomResult;
        if (cur == null) return;                                   // 대기방
        SyncRoomKey(run, rfc, now);
        if (s_cur == null) return;
        if (cur.roomGO != s_tlRoom) SubscribeTimeline(cur.roomGO);

        var spawner = Object.FindFirstObjectByType<BossSpawner>();
        var boss    = spawner != null ? spawner.SpawnedBoss : null;
        foreach (var mb in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
        {
            if (mb.IsDead || !mb.isActiveAndEnabled) continue;
            int id = mb.GetInstanceID();
            if (!s_cur.mons.TryGetValue(id, out var rec) || rec.hp == 0)
                s_cur.mons[id] = (Clean(mb.name), mb.Grade, mb.EffectiveMaxHp);
            if (mb == boss || mb.Grade == MonsterGrade.Boss) continue;
            int hp = mb.CurrentHp;
            if (hp <= 0) continue;                                  // 아직 초기화 중
            if (s_tlKilled.Add(id)) { s_cur.hpKill += hp; s_cur.hpEff += mb.EffectiveMaxHp; }
            mb.TakeDamage(hp * 10f + 100000f, run.Player.gameObject, 0f);
        }

        // 사망 → 풀 반환(비활성). 그 사이 시체는 MonsterBudget 한 칸을 차지한다(스포너 AliveCount는 활성 여부로 센다).
        if (s_tlDead.Count > 0)
        {
            if (s_tlDead.Count > s_cur.corpseMax) s_cur.corpseMax = s_tlDead.Count;
            s_tlDone.Clear();
            foreach (var kv in s_tlDead)
                if (kv.Key == null || !kv.Key.gameObject.activeInHierarchy) { s_cur.deathToPool.Add(now - kv.Value); s_tlDone.Add(kv.Key); }
            foreach (var m in s_tlDone) s_tlDead.Remove(m);
        }

        if (s_cur.tSpawn0 >= 0 && s_cur.tClear < 0 && !MonsterBudget.CanSpawn) s_cur.budgetWait += dt;
        if (s_cur.tReward < 0 && Object.FindFirstObjectByType<ClearRewardTrigger>() != null) s_cur.tReward = now;
        if (s_cur.tExitOpen < 0)
            foreach (var g in Object.FindObjectsByType<ProcRoomGate>(FindObjectsSortMode.None))
                if (g.IsArmed) { s_cur.tExitOpen = now; break; }
    }

    private static void SubscribeTimeline(GameObject room)
    {
        UnsubscribeTimeline();
        s_tlRoom = room;
        if (room == null) return;
        room.GetComponentsInChildren(true, s_tlSpawners);
        foreach (var sp in s_tlSpawners) sp.OnMonsterSpawned += OnTlSpawned;
        if (room.TryGetComponent(out s_tlWave)) s_tlWave.OnRoomCleared += OnTlCleared;
    }

    private static void UnsubscribeTimeline()
    {
        foreach (var sp in s_tlSpawners) if (sp != null) sp.OnMonsterSpawned -= OnTlSpawned;
        s_tlSpawners.Clear();
        if (s_tlWave != null) s_tlWave.OnRoomCleared -= OnTlCleared;
        s_tlWave = null;
        s_tlRoom = null;
    }

    private static void OnTlSpawned(MonsterBase m)
    {
        if (s_cur == null || m == null) return;
        double t = EditorApplication.timeSinceStartup;
        if (s_cur.tSpawn0 < 0) s_cur.tSpawn0 = t;
        s_cur.tSpawnN = t;
        s_cur.spawns++;
        m.OnDied -= OnTlDied;
        m.OnDied += OnTlDied;
    }

    private static void OnTlDied(MonsterBase m)
    {
        if (m != null) m.OnDied -= OnTlDied;
        if (s_cur == null || m == null) return;
        double t = EditorApplication.timeSinceStartup;
        s_cur.tKillN = t;
        s_cur.deaths++;
        s_tlDead[m] = t;
    }

    private static void OnTlCleared()
    {
        if (s_cur != null && s_cur.tClear < 0) s_cur.tClear = EditorApplication.timeSinceStartup;
    }

    /// <summary>방 하나의 시간표 — 진입(문 통과) 기준 경과 초. 없는 사건은 -1.</summary>
    private static string TimelineJson(RoomRec r)
    {
        string T(double t) => t >= 0 ? (t - r.enterT).ToString("0.00") : "-1";
        var sb = new StringBuilder("{");
        sb.Append("\"move\":").Append(T(r.tMove)).Append(",\"act\":").Append(T(r.tAct))
          .Append(",\"spawn0\":").Append(T(r.tSpawn0)).Append(",\"spawnN\":").Append(T(r.tSpawnN))
          .Append(",\"killN\":").Append(T(r.tKillN)).Append(",\"clear\":").Append(T(r.tClear))
          .Append(",\"reward\":").Append(T(r.tReward)).Append(",\"open\":").Append(T(r.tExitOpen))
          .Append(",\"go\":").Append(T(r.tExitGo)).Append(",\"leave\":").Append(T(r.leaveT))
          .Append(",\"spawns\":").Append(r.spawns).Append(",\"deaths\":").Append(r.deaths)
          .Append(",\"budgetWait\":").Append(r.budgetWait.ToString("0.00"))
          .Append(",\"deathToPool\":").Append(Median(r.deathToPool).ToString("0.00"))
          .Append(",\"corpseMax\":").Append(r.corpseMax)
          .Append('}');
        return sb.ToString();
    }

    /// <summary>챕터 × 일반·정예·이벤트별 단계 중앙값 표(전투가 있던 방만).</summary>
    private static string TimelineTable(string reason)
    {
        var sb = new StringBuilder($"[방 시간표] {System.DateTime.Now:MM-dd HH:mm} · 종료 {reason} · 즉사 기준(매 프레임 처치) · 단계 중앙값(초)\n");
        sb.Append("단계: 짓기=문 통과→이동 · 연출=이동→웨이브 시작 · 첫스폰=시작→첫 스폰 · 스폰=첫→마지막 스폰 · 꼬리=마지막 스폰→마지막 처치 · ")
          .Append("클리어=마지막 처치→OnRoomCleared · 보상=클리어→보상 등장 · 수령=보상→출구 열림 · 출구=열림→출구 밟음 · 전환=밟음→다음 방\n");
        sb.Append("입장 하위(「입장 단계」 로그): 덮기=화면 덮음 · 짓기=방 빌드 · 드러내기=화면 복귀 · 디졸브=조립 대기 · 대사 · 부감=부감 팬·석문 낙하. 「첫방」=방 모양 보여 주기, 「빠른」=빠른 입장\n");
        string[] kinds = { "Normal", "Elite", "Event" };
        for (int ch = 1; ch <= 4; ch++)
        foreach (var kind in kinds)
        for (int mode = 0; mode < 2; mode++)
        {
            bool quick = mode == 1;
            var rows = s_rooms.FindAll(r => r.chapter == ch && r.kind == kind && r.tSpawn0 >= 0 && r.quickEntry == quick);
            if (rows.Count == 0) continue;
            double M(System.Func<RoomRec, double> f)
            {
                var v = new List<double>();
                foreach (var r in rows) { double x = f(r); if (x >= 0) v.Add(x); }
                return Median(v);
            }
            double D(double a, double b) => a >= 0 && b >= 0 ? b - a : -1;
            double E(int i) => M(r => r.entry != null && r.entry.Length > i ? r.entry[i] : -1);
            sb.Append($"Ch{ch} {kind}·{(quick ? "빠른" : "첫방")} n={rows.Count} · 마릿수 {M(r => r.spawns):0} · ")
              .Append($"짓기 {M(r => D(r.enterT, r.tMove)):0.0} · 연출 {M(r => D(r.tMove, r.tAct)):0.0} · 첫스폰 {M(r => D(r.tAct, r.tSpawn0)):0.0} · ")
              .Append($"스폰 {M(r => D(r.tSpawn0, r.tSpawnN)):0.0} · 꼬리 {M(r => D(r.tSpawnN, r.tKillN)):0.0} · 클리어 {M(r => D(r.tKillN, r.tClear)):0.0} · ")
              .Append($"보상 {M(r => D(r.tClear, r.tReward)):0.0} · 수령 {M(r => D(r.tReward, r.tExitOpen)):0.0} · 출구 {M(r => D(r.tExitOpen, r.tExitGo)):0.0} · ")
              .Append($"전환 {M(r => D(r.tExitGo, r.leaveT)):0.0} · ‖ 문→클리어 {M(r => D(r.enterT, r.tClear)):0.0} · 체류 {M(r => D(r.enterT, r.leaveT)):0.0} · ")
              .Append($"상한막힘 {M(r => r.budgetWait):0.00} · 사망→풀 {M(r => r.deathToPool.Count > 0 ? Median(r.deathToPool) : -1):0.00} · 시체최대 {M(r => r.corpseMax):0} · ")
              .Append($"마리당 HP 실제 {M(r => r.spawns > 0 ? (double)r.hpKill / r.spawns : -1):0.0} / Effective {M(r => r.spawns > 0 ? (double)r.hpEff / r.spawns : -1):0.0} / 첫발견 {M(r => r.mons.Count > 0 ? (double)SumSeen(r) / r.mons.Count : -1):0.0}")
              .Append($" ‖ 입장 하위 덮기 {E(0):0.00} · 짓기 {E(1):0.00} · 드러내기 {E(2):0.00} · 디졸브 {E(3):0.00} · 대사 {E(4):0.00} · 부감 {E(5):0.00}\n");
        }
        return sb.ToString();
    }

    /// <summary>「[RunFlow] 입장 단계(초) — 덮기 a · 짓기 b · 드러내기 c · 디졸브 대기 d · 대사 e · 부감·봉인 f · 모양」에서 숫자 6개를 뽑는다.</summary>
    private static void ParseEntrySteps(string msg, RoomRec r)
    {
        var nums = new List<double>(6);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(msg, @"-?\d+\.\d+"))
            if (double.TryParse(m.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
                nums.Add(v);
        r.entry      = nums.ToArray();
        r.quickEntry = msg.Contains("빠른 입장");
    }

    private static int SumSeen(RoomRec r)
    {
        int n = 0;
        foreach (var m in r.mons.Values) if (m.grade != MonsterGrade.Boss) n += m.hp;
        return n;
    }

    private static double Median(List<double> v)
    {
        if (v == null || v.Count == 0) return -1;
        var c = new List<double>(v);
        c.Sort();
        int k = c.Count / 2;
        return c.Count % 2 == 1 ? c[k] : (c[k - 1] + c[k]) * 0.5;
    }
}
