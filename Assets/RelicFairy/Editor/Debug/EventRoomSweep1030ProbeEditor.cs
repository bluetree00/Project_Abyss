#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 10-30 사용자 「이벤트방이 제대로 작동하는 게 거의 없다 — 상자는 맵 밖 · 탄막은 안 나오고 끝나고 · 모든 이벤트방 자세히」 전수 실측(테스트 허브).
/// 챕터 하나를 런 구조 자동 진행으로 돌리면서 다음 문을 이벤트 방 7종(탄막 · 기억 · 욕심 · 보물고 · 신탁 · 제물 · 성소)으로 차례로 고정해
/// <b>실제 방 흐름 그대로</b> 들어간다. 방마다:
///   · 붙은 이벤트 종류(없으면 「없음」) · 방 가운데 · 놀이판 가운데/반경
///   · 이벤트가 세운 자리(필드 · 표식 · 소품)마다 — 걸을 수 있는 면(NavMesh 0.8 m) · 아래 바닥 콜라이더 · 플레이어에게서 걸어서 닿나 · 방 가운데까지 거리 → 「맵 밖」 판정
///   · 진행 시간표(1초마다 · 남은 시간 · 위험물 수 · 놀이 중인가) · 끝난 시각 · 등급 로그 · 경고 · 예외
///   · 사진: 들어선 뒤 · 진행 중 · 끝
/// 놀이는 자동 시연 「보통」, 상호작용 이벤트는 자동 진행이 2초 뒤 해결한다(자리 · 사진은 그 전에 찍는다).
/// 결과 Temp/event_sweep_ch{n}.txt · Temp/ui_shots/ES30_ch{n}_*.png · 로그 「[ES30] 끝」.
/// </summary>
public static class EventRoomSweep1030ProbeEditor
{
    private const string Tag = "[ES30]";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private static readonly string[] Types = { "barrage", "memory", "greed", "vault", "oracle", "sacrifice", "sanctum" };

    private static int s_ch, s_idx;
    private static StringBuilder s_sb;
    private static bool s_observing;
    private static string s_curKey;
    private static double s_deadline;
    private static readonly HashSet<string> s_done = new();
    private static readonly List<string> s_logs = new();

    [MenuItem("RelicFairy/Debug/10-30 이벤트 방 전수 실측/Ch1 (테스트 허브, 플레이 중)")] private static void Ch1() => Begin(1);
    [MenuItem("RelicFairy/Debug/10-30 이벤트 방 전수 실측/Ch2 (테스트 허브, 플레이 중)")] private static void Ch2() => Begin(2);
    [MenuItem("RelicFairy/Debug/10-30 이벤트 방 전수 실측/Ch3 (테스트 허브, 플레이 중)")] private static void Ch3() => Begin(3);
    [MenuItem("RelicFairy/Debug/10-30 이벤트 방 전수 실측/Ch4 (테스트 허브, 플레이 중)")] private static void Ch4() => Begin(4);

    private static string Key(int ch, string type) => $"ch{ch}_event_{type}_01";

    private static void Begin(int ch)
    {
        if (!EditorApplication.isPlaying || Object.FindFirstObjectByType<TestHubLauncher>() == null) { Debug.LogWarning($"{Tag} 테스트 허브 플레이 중에"); return; }
        s_ch = ch; s_idx = 0; s_observing = false; s_curKey = null;
        s_done.Clear(); s_logs.Clear();
        s_sb = new StringBuilder($"10-30 이벤트 방 전수 실측 — Ch{ch}\n");
        EventMinigame.DebugDemo = MinigameDemo.Normal;
        EventMinigameDebugMenu.ForceDoor(Key(ch, Types[0]));
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        // 10-30 빌드처럼 — 「Unlit/Color」가 빠진 셈 치고 기본 도형이 대체 사슬을 타게(빌드에서만 나던 예외 확인)
        PatternGuideHelper.DebugNoUnlitColor = true;
        ResetUnlitCache();
        s_deadline = EditorApplication.timeSinceStartup + 900.0;
        s_launchTries = 0;
        TryLaunchAuto();
        EditorApplication.update -= Watch;
        EditorApplication.update += Watch;
        Debug.Log($"{Tag} 시작 ch{ch} — 첫 문 {Key(ch, Types[0])} · 빌드처럼(Unlit/Color 없음)");
    }

    private static int s_launchTries;

    /// <summary>자동 진행을 건다 — 테스트 허브가 아직 초기화 중이면(「시작 실패」) 2초 뒤 다시(최대 20번).</summary>
    private static void TryLaunchAuto()
    {
        var auto = typeof(RunStructureAutoPlayEditor).GetMethod("BeginWith", AnyStatic);
        if (auto == null) { Debug.LogWarning($"{Tag} 자동 진행 BeginWith 없음"); return; }
        auto.Invoke(null, new object[] { false, s_ch, false, false, false });
        bool running = typeof(RunStructureAutoPlayEditor).GetField("s_running", AnyStatic)?.GetValue(null) is bool r && r;
        if (running || ++s_launchTries > 20) { if (!running) Debug.LogWarning($"{Tag} 자동 진행을 못 걸었다"); return; }
        double at = EditorApplication.timeSinceStartup + 2.0;
        void Later() { if (EditorApplication.timeSinceStartup < at) return; EditorApplication.update -= Later; if (EditorApplication.isPlaying) TryLaunchAuto(); }
        EditorApplication.update += Later;
    }

    private static void ResetUnlitCache()
    {
        var t = typeof(PatternGuideHelper);
        t.GetField("_unlitShader", AnyStatic)?.SetValue(null, null);
        t.GetField("_unlitShared", AnyStatic)?.SetValue(null, null);
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (s_curKey == null) return;
        bool bad = type == LogType.Exception || type == LogType.Error || type == LogType.Assert;
        bool warn = type == LogType.Warning && !msg.StartsWith("[RunAuto]");
        bool ev = msg.Contains("[Minigame") || msg.Contains("이벤트") || msg.Contains("[Challenge") || msg.Contains("[Vault") || msg.Contains("[Oracle")
                  || msg.Contains("[Sacrifice") || msg.Contains("놀이") || msg.Contains("[RoomClearGate") || msg.Contains("[Greed") || msg.Contains("[Barrage") || msg.Contains("[Memory");
        if (!(bad || warn || ev)) return;
        string line = msg.Replace("\n", " ⏎ ");
        if (line.Length > 260) line = line.Substring(0, 260) + "…";
        if (bad) line = "‼ " + line + (string.IsNullOrEmpty(stack) ? "" : "  @ " + stack.Split('\n').FirstOrDefault()?.Trim());
        else if (warn) line = "⚠ " + line;
        if (s_logs.Count < 60) s_logs.Add(line);
    }

    private static void Watch()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > s_deadline) { Finish(EditorApplication.isPlaying ? "시간 초과" : "플레이 끝"); return; }
        if (s_observing) return;
        var run = GameRunBootstrapper.Instance?.Run;
        var rfc = RunFlowController.Active;
        if (run == null || rfc == null) return;
        if (!(bool)(typeof(RunFlowController).GetField("_hasLastPlan", Any)?.GetValue(rfc) ?? false)) return;
        var plan = (DoorPlan)typeof(RunFlowController).GetField("_lastPlan", Any).GetValue(rfc);
        string pool = plan.entry?.pool_key ?? "";
        if (plan.kind != RoomPlanKind.Event || string.IsNullOrEmpty(pool) || s_done.Contains(pool)) return;
        var cur = typeof(RunFlowController).GetField("_current", Any)?.GetValue(rfc) as ProcRoomResult;
        if (cur?.roomGO == null) return;
        s_done.Add(pool);
        s_observing = true;
        s_curKey = pool;
        // 다음 방 문은 이 방이 끝날 때 정해진다 — 지금 다음 종류로 고정해 둔다
        int next = System.Array.IndexOf(Types, pool.Replace($"ch{s_ch}_event_", "").Replace("_01", "")) + 1;
        s_idx = Mathf.Max(s_idx, next);
        EventMinigameDebugMenu.ForceDoor(s_idx < Types.Length ? Key(s_ch, Types[s_idx]) : "");
        ObserveAsync(run, cur, pool).Forget();
    }

    private static async UniTaskVoid ObserveAsync(GameRunSession run, ProcRoomResult room, string pool)
    {
        var sb = s_sb;
        string shot = $"ES30_ch{s_ch}_{pool.Replace($"ch{s_ch}_event_", "").Replace("_01", "")}";
        sb.AppendLine($"\n## {pool}");
        double t0 = EditorApplication.timeSinceStartup;
        try
        {
            var roomGO = room.roomGO;
            Vector3 roomPos = roomGO.transform.position;
            // 이벤트 컴포넌트가 붙을 때까지(방 세우기 직후) 최대 6초
            Component ev = null;
            for (float t = 0f; t < 6f && ev == null; t += 0.2f)
            {
                ev = roomGO.GetComponentsInChildren<EventMinigame>(true).FirstOrDefault() as Component
                     ?? roomGO.GetComponentsInChildren<WorldInteractionChallenge>(true).FirstOrDefault() as Component
                     ?? roomGO.GetComponentsInChildren<GambleBoxChallenge>(true).FirstOrDefault() as Component
                     ?? roomGO.GetComponentsInChildren<CombatChallengeOverlay>(true).FirstOrDefault() as Component;
                if (ev == null) await Wait(0.2f);
            }
            sb.AppendLine($"  붙은 이벤트: {(ev != null ? ev.GetType().Name : "없음")} · 방 {roomGO.name} @ {Fmt(roomPos)} · 입장 {Fmt(room.entryPos)}");
            bool spotsDone = false;
            if (ev is WorldInteractionChallenge)
            {
                // 세워질 때까지(첫 Update) 기다렸다가 곧바로 잰다 — 자동 진행이 해결하기 전에
                for (float t = 0f; t < 3f && !(typeof(WorldInteractionChallenge).GetField("_placed", Any)?.GetValue(ev) is bool pl && pl); t += 0.1f) { HoldAuto(); await Wait(0.1f); }
                ReportSpots(sb, ev, run.Player != null ? run.Player.transform.position : room.entryPos);
                spotsDone = true;
                // 10-31 보물 창고 제한 시간이 방마다 6 ~ 18초로 짧아졌다 — 재는 동안 끝나지 않게 남은 시간을 넉넉히(실측 전용)
                if (ev is TreasureVaultChallenge) typeof(TreasureVaultChallenge).GetField("_timeLeft", Any)?.SetValue(ev, 999f);
                // 10-31 장치 측정은 세워진 바로 뒤에 — 조작이 풀리길 기다리는 동안 자동 진행이 방을 넘겨 버린 적이 있다(플레이어가 다음 방)
                if (ev is TreasureVaultChallenge && run.Player != null) await DeviceTestAsync(sb, run, (WorldInteractionChallenge)ev);
            }
            // 입장 연출이 끝날 때까지(조작 복원) — 놀이는 Playing, 상호작용은 OnResolved 구독
            for (float t = 0f; t < 20f; t += 0.25f)
            {
                HoldAuto();
                if (ev is EventMinigame mg && (mg.IsPlaying || mg.IsResolved)) break;
                if (ev is WorldInteractionChallenge wc && typeof(WorldInteractionChallenge).GetField("OnResolved", Any)?.GetValue(wc) != null) break;
                if (ev == null && t > 6f) break;
                await Wait(0.25f);
            }
            await Wait(0.4f);
            var player = run.Player != null ? run.Player.transform.position : Vector3.zero;
            sb.AppendLine($"  플레이어 {Fmt(player)} · 들어선 뒤 {EditorApplication.timeSinceStartup - t0:0.0}초");
            if (ev is EventMinigame m0)
            {
                var c = (Vector3)typeof(EventMinigame).GetField("Center", Any).GetValue(m0);
                float r = (float)typeof(EventMinigame).GetField("Radius", Any).GetValue(m0);
                float dur = (float)(typeof(EventMinigame).GetProperty("PlayDuration", Any)?.GetValue(m0) ?? -1f);
                sb.AppendLine($"  놀이판 가운데 {Fmt(c)}(방 가운데에서 {Flat(c, roomPos):0.0} m) · 반경 {r:0.0} m · 제한 {dur:0.0}초");
                if (run.Player != null) sb.AppendLine($"  놀이 시작 때 플레이어 — 판 가운데에서 {Flat(run.Player.transform.position, c):0.0} m(반경 {r:0.0})");
            }
            if (!spotsDone) ReportSpots(sb, ev, player);
            await ShotAsync(shot + "_1_enter");
            if (ev is WorldInteractionChallenge wic0) await ActAsync(sb, run, wic0);

            // 진행 시간표 — 끝날 때까지(최대 70초) 1초마다
            var tl = new StringBuilder();
            // 10-31 탄막은 진짜로 맞게 — 자동 진행이 틱마다 무적을 켜 「한 번도 안 맞았다 · 플래티넘」만 나왔다(사용자 「많이 맞아도 플래티넘」).
            // 시연 「보통」은 맞을 자리의 절반만 피한 셈 친다 — 나머지는 실제 피해로 센다.
            System.Action restoreHits = null;
            int hpBefore = run.Player != null ? run.Player.RuntimeStats.Hp : 0;
            if (ev is BarrageCorridorGame && run.Player != null) restoreHits = Vulnerable(run.Player);
            float endAt = -1f;
            for (int s = 0; s < 70; s++)
            {
                for (int k = 0; k < 4; k++) { HoldAuto(); await Wait(0.25f); }
                string st = State(ev);
                tl.Append($"{s + 1}s:{st} ");
                if (s == 6) await ShotAsync(shot + "_2_mid");
                if (s == 4 && ev is BarrageCorridorGame bg && bg.IsPlaying && run.Player != null)
                {
                    var c = (Vector3)typeof(EventMinigame).GetField("Center", Any).GetValue(bg);
                    float r = (float)typeof(EventMinigame).GetField("Radius", Any).GetValue(bg);
                    Vector3 outP = c + Vector3.right * (r + 3f); outP.y = run.Player.transform.position.y;
                    Place(run.Player, outP);
                    await Wait(0.3f);
                    sb.AppendLine($"  판 밖(반경 +3 m)으로 밀었다 → 0.3초 뒤 가운데에서 {Flat(run.Player.transform.position, c):0.0} m(반경 {r:0.0})");
                }
                if (Resolved(ev)) { endAt = s + 1; break; }
                if (ev == null && s >= 4) break;
            }
            restoreHits?.Invoke();
            if (ev is BarrageCorridorGame bgEnd && run.Player != null)
                sb.AppendLine($"  무적 끄고 잰 탄막 — 맞은 횟수 {typeof(BarrageCorridorGame).GetField("_hits", Any)?.GetValue(bgEnd)} · 등급 {bgEnd.CurrentGrade} · 체력 {hpBefore} → {run.Player.RuntimeStats.Hp}");
            sb.AppendLine("  시간표 " + tl);
            sb.AppendLine($"  해결 {(endAt > 0 ? endAt + "초" : "안 됨/없음")}");
            await Wait(1.2f);
            await ShotAsync(shot + "_3_end");
        }
        catch (System.Exception e) { sb.AppendLine("  실측 예외: " + e.Message); }
        finally
        {
            foreach (var l in s_logs) sb.AppendLine("  · " + l);
            s_logs.Clear();
            s_curKey = null;
            s_observing = false;
            File.WriteAllText($"Temp/event_sweep_ch{s_ch}.txt", s_sb.ToString());
            if (s_idx >= Types.Length) Finish("7종 끝");
        }
    }

    /// <summary>자동 진행의 「상호작용 이벤트 2초 뒤 해결」을 미룬다(그 방 입장 시각을 지금으로) — 실측이 직접 누른다.</summary>
    private static void HoldAuto()
    {
        var f = typeof(RunStructureAutoPlayEditor).GetField("s_cur", AnyStatic);
        var cur = f?.GetValue(null);
        cur?.GetType().GetField("enterT")?.SetValue(cur, EditorApplication.timeSinceStartup);
    }

    /// <summary>사람처럼 — 보물 창고: 상자를 하나씩 · 신탁: 첫 문 · 제물: 낼 수 있는 제단(유물 기억 창은 첫 칸으로 고른다).</summary>
    private static async UniTask ActAsync(StringBuilder sb, GameRunSession run, WorldInteractionChallenge wc)
    {
        for (float t = 0f; t < 20f && typeof(WorldInteractionChallenge).GetField("OnResolved", Any)?.GetValue(wc) == null; t += 0.25f) { HoldAuto(); await Wait(0.25f); }
        await Wait(1.0f);
        var markers = (typeof(WorldInteractionChallenge).GetField("_markers", Any)?.GetValue(wc) as IList)?.Cast<object>().ToList() ?? new List<object>();
        var act = wc.GetType().GetMethod("OnActivate", Any);
        string TagOf(object m) => m.GetType().GetField("Tag")?.GetValue(m)?.ToString() ?? "";
        sb.AppendLine("  표식: " + string.Join(" / ", markers.Select(m => $"{(m.GetType().GetField("Label")?.GetValue(m) as TMPro.TMP_Text)?.text}({TagOf(m)})")));
        int gold0 = run.PlayerState?.TempGold ?? 0, mat0 = run.FuelBank?.Get(FuelKind.EnhanceMaterial) ?? 0, pot0 = run.PlayerState?.PotionCount ?? 0;
        if (wc is TreasureVaultChallenge)
        {
            // 10-31 차례 — 3번 상자를 먼저 눌러 본다(안 열려야 한다)
            act.Invoke(wc, new object[] { 2 });
            sb.AppendLine($"  차례가 아닌 3번을 눌렀다 — 연 상자 {typeof(TreasureVaultChallenge).GetField("_opened", Any)?.GetValue(wc)}(기대 0)");
            for (int i = 0; i < markers.Count; i++) { HoldAuto(); act.Invoke(wc, new object[] { i }); await Wait(0.6f); }
        }
        else if (wc is OracleChoiceChallenge) act.Invoke(wc, new object[] { 0 });
        else if (wc is SacrificeAltarChallenge)
        {
            int idx = markers.FindIndex(m => TagOf(m) == (pot0 > 0 ? "potion" : "gold"));
            if (idx < 0) idx = 0;
            act.Invoke(wc, new object[] { idx });
            for (float t = 0f; t < 5f; t += 0.25f)
            {
                HoldAuto(); await Wait(0.25f);
                var draft = Object.FindFirstObjectByType<UI_RelicPartDraftPopup>();
                if (draft == null) continue;
                await Wait(1.0f);
                await ShotAsync($"ES30_ch{s_ch}_sacrifice_draft");
                typeof(UI_RelicPartDraftPopup).GetMethod("CloseGuide", Any)?.Invoke(draft, null);
                typeof(UI_RelicPartDraftPopup).GetField("_selected", Any)?.SetValue(draft, 0);
                typeof(UI_RelicPartDraftPopup).GetMethod("OnConfirmClicked", Any)?.Invoke(draft, null);
                sb.AppendLine("  유물 기억 창이 떠서 첫 칸으로 골랐다");
                break;
            }
        }
        await Wait(1.5f);
        sb.AppendLine($"  누른 뒤 — 금화 {gold0}→{run.PlayerState?.TempGold} · 강화재료 {mat0}→{run.FuelBank?.Get(FuelKind.EnhanceMaterial)} · 물약 {pot0}→{run.PlayerState?.PotionCount}");
    }

    /// <summary>
    /// 보물 창고 바닥 장치(10-31 사용자 「바닥 디버프 이펙트가 제대로 작동하나」 · 「그냥 걸어가서 완료되면 의미가 없다」).
    /// ① 진흙 · 바람 · 가시 위에 세워 <b>실제 지상 최고 속도</b>(DefaultMoveAbility.Move를 그 자리에서 돌려 Rigidbody 속도)와 피해를 잰다 —
    ///    예전 실측은 MoveScale(입력 배율)만 봐서 속도가 그대로인 걸 못 잡았다.
    /// ② 끝나면 바닥 배율이 1로 돌아왔나(영구로 남지 않나).
    /// ③ 시간 모형 — 이 방의 가장 짧은 차례 · 장치 자리로 「곧장 달리기(진흙 그대로)」와 「바람 밟고 진흙 돌아가기」가 제한 시간 안에 몇 개를 여나.
    /// 자동 진행은 틱마다 무적을 다시 켜므로(무적이면 피해 · 비틀거림이 안 걸린다) 재는 동안 그 틱을 잠깐 뗀다.
    /// </summary>
    private static async UniTask DeviceTestAsync(StringBuilder sb, GameRunSession run, WorldInteractionChallenge wc)
    {
        var player = run.Player;
        var list = typeof(TreasureVaultChallenge).GetField("_devices", Any)?.GetValue(wc) as IList;
        var tickDev = typeof(TreasureVaultChallenge).GetMethod("TickDevices", Any);
        if (player == null || list == null || tickDev == null) return;
        var tickInfo = typeof(RunStructureAutoPlayEditor).GetMethod("Tick", AnyStatic);
        var tick = tickInfo != null ? (EditorApplication.CallbackFunction)System.Delegate.CreateDelegate(typeof(EditorApplication.CallbackFunction), tickInfo) : null;
        var inv = typeof(PlayerController).GetField("debugInvincible", Any);
        var susp = typeof(PlayerController).GetField("_controlSuspended", Any);
        Vector3 home = player.transform.position;
        if (tick != null) EditorApplication.update -= tick;
        inv?.SetValue(player, false);
        float plain = TopSpeed(player);
        sb.AppendLine($"  장치 재기(세워진 직후 · 장치 판정 TickDevices를 직접 부름) — 평지 최고 속도 {plain:0.0} m/s · 조작 멈춤 {susp?.GetValue(player)}");
        try
        {
            var seen = new HashSet<string>();
            foreach (var d in list)
            {
                string kind = d.GetType().GetField("Kind")?.GetValue(d)?.ToString();
                if (kind == null || !seen.Add(kind)) continue;
                var pos = (Vector3)d.GetType().GetField("Pos").GetValue(d);
                pos.y = player.transform.position.y;
                Place(player, pos);
                if (kind == "Mud")
                {
                    tickDev.Invoke(wc, new object[] { 0.02f });
                    float s = TopSpeed(player);
                    sb.AppendLine($"  장치 진흙 — 최고 속도 {s:0.0} m/s = 평지의 ×{s / Mathf.Max(0.01f, plain):0.00}(기대 ×0.45) · 바닥 배율 {player.FloorSpeedScale:0.00}");
                    await ShotAsync($"ES30_ch{s_ch}_vault_mud");
                    Place(player, home); await Wait(0.6f);
                    sb.AppendLine($"  진흙에서 나와 0.6초 — 바닥 배율 {player.FloorSpeedScale:0.00}(기대 1.00 · 조작 멈춤 중엔 타이머도 멈춘다: {susp?.GetValue(player)})");
                }
                else if (kind == "Wind")
                {
                    tickDev.Invoke(wc, new object[] { 0.02f });
                    float s = TopSpeed(player);
                    sb.AppendLine($"  장치 바람 — 최고 속도 {s:0.0} m/s = 평지의 ×{s / Mathf.Max(0.01f, plain):0.00}(기대 ×2.00)");
                    Place(player, home); await Wait(1.8f);
                    sb.AppendLine($"  바람 뒤 1.8초 — 바닥 배율 {player.FloorSpeedScale:0.00}(기대 1.00 · 1.5초에 풀림 · 조작 멈춤 {susp?.GetValue(player)})");
                }
                else
                {
                    int hp0 = player.RuntimeStats.Hp;
                    float minScale = 1f;
                    for (int k = 0; k < 30; k++) { Place(player, pos); tickDev.Invoke(wc, new object[] { 0.1f }); minScale = Mathf.Min(minScale, player.FloorSpeedScale); }
                    sb.AppendLine($"  장치 가시(3초어치) — 체력 {hp0} → {player.RuntimeStats.Hp}(기대 한 번 최대 체력 4%) · 비틀거림 최저 바닥 배율 {minScale:0.00}(기대 0.35)");
                    float invEnd = (float)(typeof(PlayerController).GetField("_invincibleEnd", Any)?.GetValue(player) ?? 0f);
                    sb.AppendLine($"    무적 진단 — IsInvincible {player.IsInvincible} · 디버그 {inv?.GetValue(player)} · 남은 무적 {Mathf.Max(0f, invEnd - Time.time):0.0}초 · 저스트 회피 무적 {PerfectDodgeInvuln(player)} · 유물 스킬 무적 {player.RelicSkillInvulnerable}");
                }
            }
            Place(player, home);
            player.ApplyFloorSlow(0.45f, 9f);
            player.ApplyHaste(2f, 9f);
            float before = player.FloorSpeedScale;
            player.ClearFloorSpeed();
            sb.AppendLine($"  끝/방 소멸 정리(ClearFloorSpeed) — 9초짜리 진흙 · 바람을 건 배율 {before:0.00} → {player.FloorSpeedScale:0.00}(기대 1.00) · 최고 속도 {TopSpeed(player):0.0} m/s(기대 {plain:0.0})");
            VaultTimingModel(sb, wc, list, plain);
        }
        finally
        {
            player.ClearFloorSpeed();
            inv?.SetValue(player, true);
            Place(player, home);
            if (tick != null) EditorApplication.update += tick;
        }
    }

    /// <summary>그 자리에서 낼 수 있는 지상 최고 속도 — 멈춘 채로 Move를 여러 번 돌려(가속 다 채움) Rigidbody 수평 속도를 읽고 다시 멈춘다.</summary>
    private static float TopSpeed(PlayerController player)
    {
        if (!player.TryGetComponent<Rigidbody>(out var rb) || player.MoveAbility == null) return 0f;
        bool kin = rb.isKinematic;
        rb.isKinematic = false;   // 입장 연출이 쥔 동안에도 잰다(재고 나서 되돌린다)
        rb.linearVelocity = Vector3.zero;
        player.RunBlend01 = 1f;
        Vector3 fwd = player.transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        for (int k = 0; k < 60; k++) player.MoveAbility.Move(player, fwd.normalized);
        var v = rb.linearVelocity;
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = kin;
        return new Vector2(v.x, v.z).magnitude;
    }

    /// <summary>
    /// 시간 모형(10-31) — 이 방의 여는 차례(_route)를 걸어서 가는 길(NavMesh)로 0.1 m씩 걷는다. 상자는 [F] 거리 2.8 m에서 연다 · 출발 0.4초 · 상자마다 0.15초.
    /// 곧장 = 곧은 길 · 진흙을 그대로 지난다(×0.45) · 길 위 바람은 밟는다. 바람 타기 = 구간 들머리 바람에 들렀다 가고 진흙은 돌아간다(그 구간 1.57배 길이).
    /// 가시는 둘 다 셈하지 않는다(때를 맞춰 지난다고 본다).
    /// </summary>
    private static void VaultTimingModel(StringBuilder sb, WorldInteractionChallenge wc, IList list, float speed)
    {
        var route = typeof(TreasureVaultChallenge).GetField("_route", Any)?.GetValue(wc) as List<Vector3>;
        float limit = (float)(typeof(TreasureVaultChallenge).GetField("_timeLimit", Any)?.GetValue(wc) ?? 0f);
        float meters = (float)(typeof(TreasureVaultChallenge).GetField("_routeMeters", Any)?.GetValue(wc) ?? 0f);
        var entry = (Vector3)(typeof(TreasureVaultChallenge).GetField("_entry", Any)?.GetValue(wc) ?? Vector3.zero);
        if (route == null || route.Count == 0 || speed < 0.5f) { sb.AppendLine("  시간 모형: 길 · 속도 없음"); return; }
        var reachField = typeof(TreasureVaultChallenge).GetField("ChestReach", BindingFlags.Static | BindingFlags.NonPublic);
        float reach = reachField != null ? (float)reachField.GetRawConstantValue() : 2.8f;
        var devs = new List<(string kind, Vector3 pos, float r)>();
        foreach (var d in list)
            devs.Add((d.GetType().GetField("Kind").GetValue(d).ToString(), (Vector3)d.GetType().GetField("Pos").GetValue(d), (float)d.GetType().GetField("Radius").GetValue(d)));
        int winds = devs.Count(x => x.kind == "Wind"), muds = devs.Count(x => x.kind == "Mud"), spikes = devs.Count(x => x.kind == "Spike");
        sb.AppendLine($"  시간 모형 — 제한 {limit:0.0}초 · 차례대로 실제로 걷는 길 {meters:0.0} m · 여는 거리 {reach:0.0} m · 장치 바람 {winds} · 진흙 {muds} · 가시 {spikes}");
        foreach (bool useWind in new[] { false, true })
        {
            var times = SimRun(entry, route, devs, speed, useWind, reach);
            int opened = times.Count(x => x <= limit);
            sb.AppendLine($"    {(useWind ? "바람 타기" : "곧장 달리기")}: 상자 {opened}/{times.Length} · 여는 때 {string.Join(" / ", times.Select(x => x.ToString("0.0")))}초");
        }
    }

    private static float[] SimRun(Vector3 entry, List<Vector3> route, List<(string kind, Vector3 pos, float r)> devs, float v, bool useWind, float reach)
    {
        const float step = 0.1f, haste = 2f, hasteSec = 1.5f, mud = 0.45f, around = 1.57f;
        var times = new float[route.Count];
        float t = 0.4f, hasteLeft = 0f;
        var used = new HashSet<int>();
        var path = new NavMeshPath();
        Vector3 at = entry;
        for (int g = 0; g < route.Count; g++)
        {
            Vector3 chest = route[g];
            var goals = new List<(Vector3 p, float stop)>();
            if (useWind)
            {
                int best = -1; float bestD = 7.5f;
                for (int i = 0; i < devs.Count; i++)
                {
                    if (devs[i].kind != "Wind" || used.Contains(i)) continue;
                    float dd = Flat(devs[i].pos, at);
                    Vector3 toW = devs[i].pos - at, toC = chest - at; toW.y = toC.y = 0f;
                    if (dd < bestD && Vector3.Dot(toW, toC) > 0f) { bestD = dd; best = i; }
                }
                if (best >= 0) { used.Add(best); goals.Add((devs[best].pos, devs[best].r * 0.6f)); }
            }
            goals.Add((chest, reach));
            foreach (var (p, stop) in goals)
            {
                // 걸어서 가는 길(NavMesh 꺾인 점)을 따라 — 벽 · 기둥을 돌아간다(곧은 줄로 재면 짧게 나왔다)
                var corners = NavMesh.CalculatePath(at, p, MinigameArena.Filter, path) && path.status == NavMeshPathStatus.PathComplete
                    ? path.corners : new[] { at, p };
                for (int ci = 1; ci < corners.Length && Flat(p, at) > stop; ci++)
                {
                    for (int guard = 0; guard < 2000 && Flat(corners[ci], at) > 0.05f && Flat(p, at) > stop; guard++)
                    {
                        Vector3 d = corners[ci] - at; d.y = 0f;
                        float len = Mathf.Min(step, d.magnitude);
                        at += d.normalized * len;
                        float mul = hasteLeft > 0f ? haste : 1f;
                        float cost = 1f;
                        foreach (var dv in devs)
                        {
                            if (Flat(dv.pos, at) > dv.r) continue;
                            if (dv.kind == "Mud") cost = useWind ? around : 1f / mud;
                            else if (dv.kind == "Wind") hasteLeft = hasteSec;
                        }
                        float dt = len * cost / (v * mul);
                        t += dt; hasteLeft -= dt;
                    }
                }
            }
            t += 0.15f;
            times[g] = t;
        }
        return times;
    }

    /// <summary>자동 진행 틱(무적을 다시 켠다)을 떼고 무적을 끈다 — 되돌리는 동작을 돌려준다.</summary>
    private static System.Action Vulnerable(PlayerController player)
    {
        var tickInfo = typeof(RunStructureAutoPlayEditor).GetMethod("Tick", AnyStatic);
        var tick = tickInfo != null ? (EditorApplication.CallbackFunction)System.Delegate.CreateDelegate(typeof(EditorApplication.CallbackFunction), tickInfo) : null;
        var inv = typeof(PlayerController).GetField("debugInvincible", Any);
        if (tick != null) EditorApplication.update -= tick;
        inv?.SetValue(player, false);
        return () =>
        {
            if (player != null) inv?.SetValue(player, true);
            if (tick != null) EditorApplication.update += tick;
        };
    }

    private static object PerfectDodgeInvuln(PlayerController player)
    {
        var pd = typeof(PlayerController).GetProperty("PerfectDodge", Any)?.GetValue(player);
        return pd?.GetType().GetProperty("IsInvulnerable")?.GetValue(pd) ?? "?";
    }

    private static void Place(PlayerController player, Vector3 at)
    {
        if (player == null) return;
        player.transform.position = at;
        if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = at; rb.linearVelocity = Vector3.zero; }
    }

    /// <summary>이벤트가 세운 자리 — 필드(Vector3 · Transform · GameObject · 목록) · 상호작용 표식 · 소품. 자리마다 걸을 면 · 바닥 · 닿음.</summary>
    private static void ReportSpots(StringBuilder sb, Component ev, Vector3 player)
    {
        if (ev == null) { sb.AppendLine("  자리: (이벤트 없음)"); return; }
        var spots = new List<(string name, Vector3 p)>();
        for (var t = ev.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
        {
            foreach (var f in t.GetFields(Any | BindingFlags.DeclaredOnly))
            {
                if (f.Name == "Center" || f.Name.Contains("cam") || f.Name.Contains("Cam")) continue;
                object v = f.GetValue(ev);
                if (v == null) continue;
                switch (v)
                {
                    case Vector3 p when p != Vector3.zero: spots.Add((f.Name, p)); break;
                    case Transform tr when tr != null && tr != ev.transform && tr.GetComponentInParent<PlayerController>() == null: spots.Add((f.Name, tr.position)); break;
                    case GameObject go when go != null && go != ev.gameObject && go.GetComponentInParent<PlayerController>() == null: spots.Add((f.Name, go.transform.position)); break;
                    case IList list when !(v is string):
                        int i = 0;
                        foreach (var o in list)
                        {
                            if (i >= 12) break;
                            Vector3? q = o switch
                            {
                                Vector3 pv => pv,
                                Transform tv when tv != null => tv.position,
                                GameObject gv when gv != null => gv.transform.position,
                                Component cv when cv != null => cv.transform.position,
                                _ => o?.GetType().GetField("Pos")?.GetValue(o) as Vector3?
                                     ?? (o?.GetType().GetField("Root")?.GetValue(o) as Transform)?.position
                                     ?? (o?.GetType().GetField("Go")?.GetValue(o) as GameObject)?.transform.position,
                            };
                            if (q.HasValue && q.Value != Vector3.zero) spots.Add(($"{f.Name}[{i}]", q.Value));
                            i++;
                        }
                        break;
                }
            }
        }
        // 상호작용 이벤트가 세운 소품(_props 아래 렌더러 묶음)
        if (ev is WorldInteractionChallenge && typeof(WorldInteractionChallenge).GetField("_props", Any)?.GetValue(ev) is Transform props && props != null)
            for (int i = 0; i < props.childCount && i < 12; i++) spots.Add(($"소품 {props.GetChild(i).name}", props.GetChild(i).position));

        int outN = 0;
        foreach (var (name, p) in spots.GroupBy(s => s.name).Select(g => g.First()))
        {
            bool onNav = NavMesh.SamplePosition(p, out var hit, 0.8f, MinigameArena.Filter);
            bool ground = Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var gh, 6f, ~0, QueryTriggerInteraction.Ignore);
            bool linked = onNav && MinigameArena.IsLinked(player, hit.position);
            bool bad = !onNav || !linked;
            if (bad) outN++;
            sb.AppendLine($"    {(bad ? "✗" : "·")} {name} {Fmt(p)} · 걸을 면 {(onNav ? $"{Vector3.Distance(p, hit.position):0.0}m" : "없음")} · 바닥 {(ground ? $"{p.y - gh.point.y:+0.0;-0.0}m {gh.collider.name}" : "없음")} · 걸어서 닿음 {linked} · 플레이어까지 {Flat(p, player):0.0} m");
        }
        sb.AppendLine($"  자리 {spots.Count}곳 · 걸을 면 밖/못 닿음 {outN}곳");
    }

    private static string State(Component ev)
    {
        switch (ev)
        {
            case EventMinigame mg:
            {
                int hz = -1;
                if (typeof(BarrageCorridorGame).IsInstanceOfType(mg) && typeof(BarrageCorridorGame).GetField("_hazards", Any)?.GetValue(mg) is ICollection c) hz = c.Count;
                return $"{(mg.IsPlaying ? "놀이" : mg.IsResolved ? "끝" : "대기")}{(mg.IsPlaying ? $"{mg.Remaining:0}" : "")}{(hz >= 0 ? $"/탄{hz}" : "")}";
            }
            case WorldInteractionChallenge wc: return WicResolved(wc) ? "끝" : "대기";
            case null: return "-";
            default: return ev.GetType().Name;
        }
    }

    private static bool Resolved(Component ev) => ev switch
    {
        EventMinigame mg => mg.IsResolved,
        WorldInteractionChallenge wc => WicResolved(wc),
        _ => false,
    };

    private static bool WicResolved(WorldInteractionChallenge wc)
        => typeof(WorldInteractionChallenge).GetProperty("IsResolved", Any)?.GetValue(wc) is bool b && b;

    private static void Finish(string why)
    {
        EditorApplication.update -= Watch;
        Application.logMessageReceived -= OnLog;
        EventMinigameDebugMenu.ForceDoor("");
        EventMinigame.DebugDemo = MinigameDemo.None;
        PatternGuideHelper.DebugNoUnlitColor = false;
        ResetUnlitCache();
        if (s_sb == null) return;
        s_sb.AppendLine($"\n끝 — {why} · 본 방 {s_done.Count}/{Types.Length}: {string.Join(", ", s_done)}");
        File.WriteAllText($"Temp/event_sweep_ch{s_ch}.txt", s_sb.ToString());
        if (EditorApplication.isPlaying) EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
        Debug.Log($"{Tag} 끝 ch{s_ch} — {why}");
        s_sb = null;
    }

    private static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
    private static string Fmt(Vector3 v) => $"({v.x:0.0},{v.y:0.0},{v.z:0.0})";

    private static async UniTask ShotAsync(string name)
    {
        Directory.CreateDirectory("Temp/ui_shots");
        string path = $"Temp/ui_shots/{name}.png";
        if (File.Exists(path)) File.Delete(path);
        ScreenCapture.CaptureScreenshot(path);
        for (int i = 0; i < 40 && !File.Exists(path); i++) await UniTask.Delay(50, ignoreTimeScale: true);
    }

    private static UniTask Wait(float s) => UniTask.Delay(System.TimeSpan.FromSeconds(s), DelayType.Realtime);
}
#endif
