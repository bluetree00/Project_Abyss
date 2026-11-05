#if UNITY_EDITOR
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
/// 11-05 #14 「문 뒤 스폰」 전 방 전수(사용자 19차 빌드 뒤 「모든 챕터에서 검증」) — 테스트 허브에서 그 챕터 런을 연 뒤
/// 방 풀(CDN)의 전투방(일반 · 정예 · 정예 보스)을 하나씩 실제로 지어(RunFlowController.EnterRoomAsync) 세 가지를 잰다.
///   ① 스폰 자리 — 스포너마다 TryGetSpawnPosition을 <see cref="SampleTries"/>번 뽑아 문 칸 너머 · 방 경계(FieldBounds) 밖이 나오나.
///      모든 시기 · 모든 파도(두 번째 파도 · 정예 보스 포함)가 이 한 길로 자리를 잡는다.
///   ② 실제 소환 — 파도를 끝까지(쓰러뜨려 다음 파도로) 소환된 자리가 문 너머인가 · 방에 있는 동안 출구 문 너머에 선 적 있나.
///   ③ 길 — 봉인 중(입구 잠금 · 출구 게이트 판의 NavMeshObstacle) 방 안에서 문 3 m 너머까지 길찾기(에이전트 종류마다)가 이어지나.
/// 인수 Temp/req1105_doors_args.txt = 「챕터 시기」(시기 0 봉인기 · 1 해방기 · 2 악몽).
/// 결과 Temp/req1105_level/doors_sweep_ch{n}_e{시기}.txt · 로그 「[Req1105D] 끝」.
/// </summary>
public static class Req1105DoorSweepProbeEditor
{
    private const string Tag = "[Req1105D]";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private const string ArgsPath = "Temp/req1105_doors_args.txt";
    private const int    SampleTries = 40;
    private const float  BeyondDoor = 3f;        // 길 검사 — 문 칸에서 안팎으로 이만큼
    private const float  RoomSeconds = 40f;      // 방 하나 상한
    private static readonly string[] EraName = { "봉인기", "해방기", "악몽" };
    private static string Dir => Path.Combine(Directory.GetCurrentDirectory(), "Temp", "req1105_level");

    [MenuItem("RelicFairy/Debug/11-05 #14 문 뒤 스폰 실측 — 전 방 전수(인수 파일 · 테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || UnityEngine.Object.FindFirstObjectByType<TestHubLauncher>() == null)
        {
            Debug.LogWarning($"{Tag} 테스트 허브 플레이 중에");
            return;
        }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        int ch = 1, era = 1;
        if (File.Exists(ArgsPath))
        {
            var a = File.ReadAllText(ArgsPath).Split(new[] { ' ', ',', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (a.Length > 0) int.TryParse(a[0], out ch);
            if (a.Length > 1) int.TryParse(a[1], out era);
        }
        ch = Mathf.Clamp(ch, 1, 4); era = Mathf.Clamp(era, 0, 2);
        var sb = new StringBuilder($"11-05 #14 문 뒤 스폰 — 전 방 전수 · Ch{ch} · {EraName[era]}{Environment.NewLine}");
        int prevEra = EditorPrefs.GetInt(StoryProgress.DebugOverridePrefsKey, -1);
        bool prevLocal = EditorPrefs.GetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, false);
        PlayerController player = null;
        FieldInfo invField = typeof(PlayerController).GetField("debugInvincible", Any);
        bool invWas = false;
        int rooms = 0, totSamples = 0, totBadSample = 0, totOob = 0, totSpawned = 0, totSpawnBehind = 0, totEverExit = 0, totLeak = 0, totPathChecks = 0, totWave2 = 0, totControl = 0;
        var failRooms = new List<string>();
        try
        {
            EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, era);
            StoryProgress.RefreshDebugOverride();
            EditorPrefs.SetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, false);   // 방 풀은 CDN(지금 게임과 같은 표)
            Debug.Log($"{Tag} 시작 Ch{ch} · {EraName[era]}(시기 {StoryProgress.Era})");

            // 1) 그 챕터 런을 연다 — 자동 진행이 대기방 · 첫 방까지 데려간 뒤 멈춘다
            if (!await LaunchRunAsync(ch)) { sb.AppendLine("  ‼ 런을 못 열었다(절차 방 진행이 시작되지 않음)"); return; }
            var run = GameRunBootstrapper.Instance.Run;
            player = run.Player;
            invWas = invField?.GetValue(player) is bool iw && iw;
            invField?.SetValue(player, true);
            sb.AppendLine($"  런 챕터 {run.CurrentChapter} · 시기 {StoryProgress.Era}");

            var flow  = RunFlowController.Active;
            var enter = typeof(RunFlowController).GetMethod("EnterRoomAsync", Any);
            var curF  = typeof(RunFlowController).GetField("_current", Any);
            var pool  = await Managers.ZoneLayout.LoadPoolAsync($"CHAPTER_{ch}_ROOM_POOL");
            var list  = pool.Where(e => e.category is "Normal" or "Elite" or "MidBoss").ToList();
            sb.AppendLine($"  방 풀 CHAPTER_{ch}_ROOM_POOL {pool.Count}개 중 전투방 {list.Count}(일반 {list.Count(e => e.category == "Normal")} · 정예 {list.Count(e => e.category == "Elite")} · 정예 보스 {list.Count(e => e.category == "MidBoss")})");

            var agentIds = new List<int>();
            for (int i = 0; i < NavMesh.GetSettingsCount(); i++) agentIds.Add(NavMesh.GetSettingsByIndex(i).agentTypeID);

            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                await KillAllAsync(player);
                var kind = e.category == "Elite" ? RoomPlanKind.Elite : e.category == "MidBoss" ? RoomPlanKind.MidBoss : RoomPlanKind.Normal;
                // 위험한 문(두 번째 파도 정예 둘 확정)은 해방기 · 악몽 정예 문에만 — 정예 방 절반을 위험한 문으로
                var plan = new DoorPlan { kind = kind, entry = e, danger = era > 0 && kind == RoomPlanKind.Elite && i % 2 == 0 };
                var task = (UniTask)enter.Invoke(flow, new object[] { plan, (DoorEdge)(i % 4), CancellationToken.None, i % 2 });
                await UniTask.WhenAny(task, UniTask.Delay(25000, ignoreTimeScale: true));
                var room = curF.GetValue(flow) as ProcRoomResult;
                if (room?.roomGO == null) { sb.AppendLine($"  ‼ {e.pool_key} 방 없음"); failRooms.Add(e.pool_key); continue; }
                rooms++;
                var r = await MeasureRoomAsync(room, e.pool_key, kind, plan.danger, player, agentIds);
                sb.AppendLine(r.line);
                foreach (var d in r.details.Take(6)) sb.AppendLine("     ‼ " + d);
                totSamples += r.samples; totBadSample += r.badSample; totOob += r.oob; totSpawned += r.spawned; totSpawnBehind += r.spawnBehind;
                totEverExit += r.everExit; totLeak += r.leaks; totPathChecks += r.pathChecks; totWave2 += r.wave2 ? 1 : 0; totControl += r.controlOpen;
                if (r.badSample + r.oob + r.spawnBehind + r.everExit + r.leaks > 0) failRooms.Add(e.pool_key);
            }
        }
        catch (Exception ex) { sb.AppendLine($"  ‼ 예외 {ex}"); failRooms.Add("예외"); }
        finally
        {
            try { await KillAllAsync(player); } catch { }
            if (player != null) invField?.SetValue(player, invWas);
            EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, prevEra);
            StoryProgress.RefreshDebugOverride();
            EditorPrefs.SetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, prevLocal);
            sb.AppendLine($"  합계 — 방 {rooms} · 스폰 자리 뽑기 {totSamples}번 중 문 너머 {totBadSample} · 방 경계 밖 {totOob} · 실제 소환 {totSpawned}마리(두 번째 파도 온 방 {totWave2}) 중 " +
                          $"소환 자리 문 너머 {totSpawnBehind} · 출구 문 너머에 선 적 {totEverExit} · 봉인 중 문 밖 길 {totLeak}/{totPathChecks}(대조: 문 판 길 깎기를 끄면 {totControl}/{totPathChecks} 이어짐)");
            sb.AppendLine(failRooms.Count == 0 ? "  PASS — 문 뒤 스폰 · 문 밖 길 0" : $"  FAIL — {string.Join(", ", failRooms)}");
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, $"doors_sweep_ch{ch}_e{era}.txt"), sb.ToString(), new UTF8Encoding(true));
            Debug.Log($"{Tag} 끝 Ch{ch} {EraName[era]} — {(failRooms.Count == 0 ? "PASS" : "FAIL " + failRooms.Count)}");
        }
    }

    private struct RoomResult
    {
        public string line;
        public List<string> details;
        public int samples, badSample, oob, spawned, spawnBehind, everExit, leaks, pathChecks, controlOpen;
        public bool wave2;
    }

    private static async UniTask<RoomResult> MeasureRoomAsync(ProcRoomResult room, string key, RoomPlanKind kind, bool danger, PlayerController player, List<int> agentIds)
    {
        var r = new RoomResult { details = new List<string>() };
        var slots = new List<ProcExitSlot>();
        if (room.hasEntrance) slots.Add(room.entrance);
        if (room.exits != null) slots.AddRange(room.exits);
        int exitFrom = room.hasEntrance ? 1 : 0;
        Vector3 center = RoomCenter(slots, room.roomGO);

        // ② 실제 소환 — 이 방 스포너의 소환 알림을 받는다(방은 막 지어졌고 첫 파도는 비동기라 대개 아직 안 나왔다)
        var spawners = room.roomGO.GetComponentsInChildren<MonsterSpawner>(true);
        var mine = new Dictionary<int, MonsterBase>();
        var spawnPos = new Dictionary<int, Vector3>();
        void OnSpawned(MonsterBase m)
        {
            if (m == null) return;
            int id = m.GetInstanceID();
            mine[id] = m;
            if (!spawnPos.ContainsKey(id)) spawnPos[id] = m.transform.position;
        }
        foreach (var s in spawners) s.OnMonsterSpawned += OnSpawned;
        var wave = room.roomGO.GetComponent<RoomWaveController>();
        try
        {
            // 알림 전에 이미 나온 몬스터(이 방 둘레 · 살아 있음) — 처음 본 자리로
            foreach (var m in AliveNear(center, 45f)) OnSpawned(m);

            // ① 스폰 자리 뽑기 — 물리 쿼리(IsSpawnSpotClear)가 갓 지은 장식 콜라이더를 보게 한 번 맞춘다
            Physics.SyncTransforms();
            var tgsp = typeof(MonsterSpawner).GetMethod("TryGetSpawnPosition", Any);
            foreach (var s in spawners)
            {
                bool hasB = GetField(s, "_hasFieldBounds") is bool hb && hb;
                var fb = hasB ? (Bounds)GetField(s, "_fieldBounds") : default;
                for (int k = 0; k < SampleTries; k++)
                {
                    var args = new object[] { null };
                    if (!(bool)tgsp.Invoke(s, args)) continue;
                    var p = (Vector3)args[0];
                    r.samples++;
                    if (BehindDoor(p, center, slots, 0, out string w)) { r.badSample++; r.details.Add($"뽑기 {s.name} {Fmt(p)} {w}"); }
                    else if (hasB && !ContainsXZ(fb, p)) { r.oob++; r.details.Add($"뽑기 {s.name} {Fmt(p)} 방 경계 밖"); }
                }
            }

            // ③ 길 — 문 판의 길찾기 깎기(carving)는 다음 갱신에 반영된다 — 잠깐 기다렸다가
            await UniTask.Delay(1000, ignoreTimeScale: true);
            var path = new NavMeshPath();
            var checkedPairs = new List<(Vector3 a, Vector3 b, NavMeshQueryFilter f)>();
            for (int si = 0; si < slots.Count; si++)
            {
                var s = slots[si];
                Vector3 axis = Outward(s, center);
                Vector3 inside = s.worldPos - axis * BeyondDoor, outside = s.worldPos + axis * BeyondDoor;
                foreach (int id in agentIds)
                {
                    var filter = new NavMeshQueryFilter { agentTypeID = id, areaMask = NavMesh.AllAreas };
                    if (!NavMesh.SamplePosition(inside, out var hi, 2f, filter)) continue;
                    if (!NavMesh.SamplePosition(outside, out var ho, 2f, filter)) continue;            // 문 밖에 판이 없다 — 샐 길도 없다
                    if (Vector3.Dot(ho.position - s.worldPos, axis) < 0.5f) continue;                   // 바깥 점이 문 안쪽으로 붙었다
                    r.pathChecks++;
                    checkedPairs.Add((hi.position, ho.position, filter));
                    if (NavMesh.CalculatePath(hi.position, ho.position, filter, path) && path.status == NavMeshPathStatus.PathComplete)
                    {
                        r.leaks++;
                        r.details.Add($"길 {(si < exitFrom ? "입구" : "출구")} {s.edge} 에이전트 {NavMesh.GetSettingsNameFromID(id)} — 문 안 {Fmt(hi.position)} → 밖 {Fmt(ho.position)} 이어짐");
                    }
                }
            }

            // ③′ 대조 — 문 판의 길 깎기를 잠깐 끄면 같은 두 점이 이어지나(이어져야 위 「막힘」이 판 덕분이라는 뜻 · 11-05 #14 고침 전 상태)
            var flowGo = RunFlowController.Active;
            var obstacles = flowGo != null ? flowGo.GetComponentsInChildren<NavMeshObstacle>(true).Where(o => o.enabled).ToList() : new List<NavMeshObstacle>();
            if (checkedPairs.Count > 0 && obstacles.Count > 0)
            {
                foreach (var o in obstacles) o.enabled = false;
                await UniTask.Delay(600, ignoreTimeScale: true);
                foreach (var (a, b, f) in checkedPairs)
                    if (NavMesh.CalculatePath(a, b, f, path) && path.status == NavMeshPathStatus.PathComplete) r.controlOpen++;
                foreach (var o in obstacles) if (o != null) o.enabled = true;
                await UniTask.Delay(600, ignoreTimeScale: true);
            }

            // ② 파도를 끝까지 — 새로 나온 게 1.2초 없으면 쓰러뜨려 다음 파도로
            float t0 = Time.realtimeSinceStartup, lastNew = t0;
            int seenCount = 0;
            var everExit = new HashSet<int>();
            while (Time.realtimeSinceStartup - t0 < RoomSeconds && wave != null && !(GetField(wave, "_cleared") is bool c && c))
            {
                UnblockPopups();
                foreach (var m in AliveNear(center, 45f)) if (!mine.ContainsKey(m.GetInstanceID())) OnSpawned(m);
                if (mine.Count != seenCount) { seenCount = mine.Count; lastNew = Time.realtimeSinceStartup; }
                int alive = 0;
                foreach (var m in mine.Values)
                {
                    if (m == null || !m.isActiveAndEnabled || m.IsDead) continue;
                    alive++;
                    if (BehindDoor(m.transform.position, center, slots, exitFrom, out string w) && everExit.Add(m.GetInstanceID()))
                        r.details.Add($"출구 너머 {m.name} {Fmt(m.transform.position)} {w}");
                }
                if (alive > 0 && Time.realtimeSinceStartup - lastNew > 1.2f) { KillList(mine.Values, player); lastNew = Time.realtimeSinceStartup; }
                await UniTask.Delay(150, ignoreTimeScale: true);
            }
            r.wave2 = GetField(wave, "_totalWaves") is int tw && GetField(wave, "_baseWaves") is int bw && tw > bw;
            r.everExit = everExit.Count;
            foreach (var kv in spawnPos)
            {
                r.spawned++;
                if (BehindDoor(kv.Value, center, slots, 0, out string w))
                {
                    r.spawnBehind++;
                    r.details.Add($"소환 자리 {(mine.TryGetValue(kv.Key, out var m) && m != null ? m.name : kv.Key.ToString())} {Fmt(kv.Value)} {w}");
                }
            }
            bool cleared = wave != null && GetField(wave, "_cleared") is bool cl && cl;
            r.line = $"  {(r.badSample + r.oob + r.spawnBehind + r.everExit + r.leaks == 0 ? "PASS" : "FAIL")} {key}({kind}{(danger ? " · 위험한 문" : "")}) 문 {slots.Count} · 스포너 {spawners.Length} · " +
                     $"뽑기 {r.samples} 문 너머 {r.badSample} 경계 밖 {r.oob} · 소환 {r.spawned}{(r.wave2 ? "(두 번째 파도)" : "")} 소환 자리 문 너머 {r.spawnBehind} · 출구 너머 선 적 {r.everExit} · " +
                     $"문 밖 길 {r.leaks}/{r.pathChecks}(판 끄면 {r.controlOpen}){(cleared ? "" : " · ⚠ 클리어 전에 시간 끝")}";
        }
        finally
        {
            foreach (var s in spawners) if (s != null) s.OnMonsterSpawned -= OnSpawned;
        }
        return r;
    }

    /// <summary>테스트 허브에서 그 챕터 런을 열고(자동 진행이 대기방 게이트를 지나 첫 방에 들어갈 때까지) 자동 진행을 멈춘다.</summary>
    private static async UniTask<bool> LaunchRunAsync(int ch)
    {
        var begin = typeof(RunStructureAutoPlayEditor).GetMethod("BeginWith", Any);
        var running = typeof(RunStructureAutoPlayEditor).GetField("s_running", Any);
        for (int tries = 0; tries < 20 && !(running?.GetValue(null) is bool r0 && r0); tries++)
        {
            begin.Invoke(null, new object[] { false, ch, false, false, false });
            if (running?.GetValue(null) is bool r1 && r1) break;
            await UniTask.Delay(2000, ignoreTimeScale: true);
        }
        float end = Time.realtimeSinceStartup + 120f;
        var curF = typeof(RunFlowController).GetField("_current", Any);
        while (Time.realtimeSinceStartup < end)
        {
            var flow = RunFlowController.Active;
            if (flow != null && curF.GetValue(flow) != null && GameRunBootstrapper.Instance?.Run?.Player != null) break;
            await UniTask.Delay(250, ignoreTimeScale: true);
        }
        if (running?.GetValue(null) is bool r2 && r2)
            typeof(RunStructureAutoPlayEditor).GetMethod("Finish", Any, null, new[] { typeof(string) }, null)?.Invoke(null, new object[] { "req1105-doors" });
        await UniTask.Delay(1500, ignoreTimeScale: true);
        UnblockPopups();
        return RunFlowController.Active != null && GameRunBootstrapper.Instance?.Run?.Player != null;
    }

    /// <summary>시간을 멈추는 창(보상 · 시작 선물 · 대사)을 걷는다 — 방을 갈아 끼우는 동안 파도가 멈추지 않게.</summary>
    private static void UnblockPopups()
    {
        if (StartGift.IsOpen) { var gift = GameObject.Find("@StartGift"); if (gift != null) UnityEngine.Object.Destroy(gift); }
        if (UnityEngine.Object.FindFirstObjectByType<UI_DialoguePopup>() != null) TestHubDebugMenu.AdvanceDialogue();
        var ui = Managers.UI;
        if (ui != null && ui.IsGameplayBlocked && !ui.TryCloseTopPopupOnEscape()) ui.CloseAllPopupUI();
    }

    private static async UniTask KillAllAsync(PlayerController player)
    {
        KillList(UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None), player);
        await UniTask.Delay(300, ignoreTimeScale: true);
    }

    private static void KillList(IEnumerable<MonsterBase> list, PlayerController player)
    {
        foreach (var m in list.ToList())
            if (m != null && m.isActiveAndEnabled && !m.IsDead && m.Grade != MonsterGrade.Boss)
                m.TakeDamage(m.CurrentHp * 10f + 100000f, player != null ? player.gameObject : null, 0f);
    }

    private static IEnumerable<MonsterBase> AliveNear(Vector3 center, float radius)
    {
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
        {
            if (m == null || !m.isActiveAndEnabled || m.IsDead) continue;
            Vector3 d = m.transform.position - center; d.y = 0f;
            if (d.sqrMagnitude <= radius * radius) yield return m;
        }
    }

    /// <summary>문 칸 너머(복도 쪽 · 문 폭 + 1.5 m 안)에 섰나 — slots[from..]만 본다(from 1 = 출구만).</summary>
    private static bool BehindDoor(Vector3 p, Vector3 center, List<ProcExitSlot> slots, int from, out string which)
    {
        which = null;
        for (int i = from; i < slots.Count; i++)
        {
            var s = slots[i];
            Vector3 axis = Outward(s, center);
            Vector3 d = p - s.worldPos; d.y = 0f;
            float along = Vector3.Dot(d, axis);
            float side = (d - axis * along).magnitude;
            if (along > 0.5f && side < s.openingWidth * 0.5f + 1.5f) { which = $"{s.edge} 문 너머 {along:0.0} m"; return true; }
        }
        return false;
    }

    /// <summary>
    /// 문 바깥 방향 = 문 칸의 변(월드 · 북 +z · 동 +x) — 막힌 경계 표시(GameRunBootstrapper Opening)와 같은 규칙.
    /// 예전엔 「방 렌더러 묶음 가운데 → 문」으로 셌는데, 묶음이 경계 띠 · 복도까지 품어 가운데가 치우치면 축이 뒤집혀 방 안이 「문 너머」로 잡혔다(11-05 첫 전수).
    /// </summary>
    private static Vector3 Outward(ProcExitSlot s, Vector3 center) => s.edge switch
    {
        DoorEdge.North => Vector3.forward,
        DoorEdge.South => Vector3.back,
        DoorEdge.East  => Vector3.right,
        _              => Vector3.left,
    };

    /// <summary>방 가운데(몬스터를 고를 둘레 기준) — 문 칸들의 평균.</summary>
    private static Vector3 RoomCenter(List<ProcExitSlot> slots, GameObject roomGO)
    {
        if (slots.Count == 0) return roomGO.transform.position;
        Vector3 sum = Vector3.zero;
        foreach (var s in slots) sum += s.worldPos;
        return sum / slots.Count;
    }

    private static bool ContainsXZ(Bounds b, Vector3 p) => p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z;
    private static object GetField(object o, string name) => o?.GetType().GetField(name, Any)?.GetValue(o);
    private static string Fmt(Vector3 v) => $"({v.x:0.0}, {v.z:0.0})";
}
#endif
