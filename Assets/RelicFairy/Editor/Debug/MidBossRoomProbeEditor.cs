#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-12 정예 보스 방 실측(배치 설계 §8-5-2) — 로컬 방 풀(Docs CSV)의 MidBoss 방에 들어가
/// Ch1: 황소 등장 → 처치 → 보상(받기 전 출구 닫힘) → 룬 창 → 출구 공개, Ch3 · Ch4: 입장 한 장(천장 · 기둥이 카메라를 가리는지).
/// 사진 6장(Temp/ui_shots/MB_*.png) · 기록 Temp/midboss_room_probe.txt · 로그 「[MidBossRoom] 끝」.
/// ⚠️ 테스트 허브 런에서만 — 지금 런의 방이 바뀌고 연료 · 룬이 실제로 들어온다.
/// </summary>
public static class MidBossRoomProbeEditor
{
    private const BindingFlags NonPub = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-12 정예 보스 방 실측 — 입장 · 처치 · 보상 · 출구 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[MidBossRoom] 플레이 모드(테스트 허브)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("정예 보스 방 실측\n");
        bool prevLocal = EditorPrefs.GetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, false);
        EditorPrefs.SetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, true);
        try
        {
            var launcher = UnityEngine.Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher != null && GameRunBootstrapper.Instance?.Run?.Player == null)
            {
                typeof(TestHubLauncher).GetField("_chapter", NonPub)?.SetValue(launcher, 1);
                typeof(TestHubLauncher).GetField("_bossApproach", NonPub)?.SetValue(launcher, false);
                launcher.TryLaunch();
            }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 60f && GameRunBootstrapper.Instance?.Run?.Player == null)
                await UniTask.Delay(500, DelayType.Realtime);
            var ensure = typeof(ServiceRoomTourProbeEditor).GetMethod("EnsureProcRunAsync", BindingFlags.Static | BindingFlags.NonPublic);
            if (ensure != null) await (UniTask<bool>)ensure.Invoke(null, null);
            var run   = GameRunBootstrapper.Instance?.Run;
            var flow  = RunFlowController.Active;
            var enter = typeof(RunFlowController).GetMethod("EnterRoomAsync", NonPub);
            if (run?.Player == null || flow == null || enter == null) { sb.AppendLine("절차 진행 없음"); return; }
            Invoke("HideTestHubGui");

            foreach (int ch in new[] { 1, 3, 4 })
            {
                var pool  = await Managers.ZoneLayout.LoadPoolAsync($"CHAPTER_{ch}_ROOM_POOL");
                var entry = pool?.Find(p => string.Equals(p.category, "MidBoss", StringComparison.OrdinalIgnoreCase));
                if (entry == null) { sb.AppendLine($"Ch{ch}: 풀에 MidBoss 방 없음(로컬 CSV에 행이 있는가)"); continue; }

                var plan = new DoorPlan { kind = RoomPlanKind.MidBoss, entry = entry };
                var task = (UniTask)enter.Invoke(flow, new object[] { plan, DoorEdge.North, CancellationToken.None, 0 });
                // 방 입장이 끝날 때까지 — 에디터가 느리면 「방 모양 보여 주기」 입장이 28초 걸려 20초 제한을 넘고, 처치 · 출구 검사가 입장과 겹쳤다(10-13 ②h)
                await UniTask.WhenAny(task, UniTask.Delay(60000, ignoreTimeScale: true));
                sb.AppendLine($"== Ch{ch} {entry.pool_key} · 방 종류 {run.CurrentRoomKind}");

                MonsterBase bull = null;
                float ts = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - ts < 15f && (bull = FindEliteBoss()) == null)
                    await UniTask.Delay(250, DelayType.Realtime);
                if (bull == null) { sb.AppendLine("  황소가 15초 안에 안 나왔다"); continue; }
                float tInit = Time.realtimeSinceStartup;   // 풀에서 막 꺼낸 몸은 설정 전(등급 · 체력 0)일 수 있다 — 잡힐 때까지
                while (Time.realtimeSinceStartup - tInit < 3f && bull != null && bull.EffectiveMaxHp <= 0)
                    await UniTask.Delay(100, DelayType.Realtime);
                float dist = Vector3.Distance(bull.transform.position, run.Player.transform.position);
                sb.AppendLine($"  등장 {Time.realtimeSinceStartup - ts:0.0}초 · {bull.name} · 등급 {bull.Grade} · 체력 {bull.EffectiveMaxHp} · 플레이어까지 {dist:0.0}m");
                await Shot($"MB_Ch{ch}_entry", 2500);   // 디졸브 0.8 + 포효 1.67
                if (ch != 1) continue;

                // 처치 — 받기 전 출구가 닫혀 있어야 한다(보상을 받아야 출구가 열린다)
                var bank = run.FuelBank;
                int ore0 = bank?.Get(FuelKind.RuneOre) ?? -1, mat0 = bank?.Get(FuelKind.EnhanceMaterial) ?? -1;
                for (int i = 0; i < 10 && !bull.IsDead; i++)
                {
                    bull.TakeDamage(bull.EffectiveMaxHp * 10f + 100000f, run.Player.gameObject, 0f);
                    await UniTask.Delay(300, DelayType.Realtime);
                }
                sb.AppendLine($"  처치 {(bull.IsDead ? "됨" : "안 됨")}");
                ClearRewardTrigger reward = null;
                ts = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - ts < 10f && (reward = UnityEngine.Object.FindFirstObjectByType<ClearRewardTrigger>()) == null)
                    await UniTask.Delay(250, DelayType.Realtime);
                sb.AppendLine($"  보상 {(reward != null ? $"등장 {Time.realtimeSinceStartup - ts:0.0}초" : "안 나옴")} · 받기 전 열린 출구 {ArmedGates()}(0이어야)");
                sb.AppendLine($"  연료 원석 {ore0}→{bank?.Get(FuelKind.RuneOre)} · 강화재료 {mat0}→{bank?.Get(FuelKind.EnhanceMaterial)} (봉인기 기준 +12 · +4)");
                var rule = RoomRewardTable.For(RoomPlanKind.MidBoss);
                sb.AppendLine($"  규칙 후보 {rule.ChoiceCount} · 하한 {rule.RarityFloor} · 막대 머리 「{RoomRewardTable.DescribeBoost(RoomPlanKind.MidBoss)}」");
                if (reward != null) MovePlayer(run.Player, reward.transform.position + new Vector3(0f, 0.2f, -1.5f));
                await Shot("MB_Ch1_reward", 900);

                if (reward != null)
                {
                    typeof(ClearRewardTrigger).GetMethod("OpenRewardFlowAsync", NonPub)?.Invoke(reward, null);
                    await Shot("MB_Ch1_runes", 1500);
                    var popup = UnityEngine.Object.FindFirstObjectByType<UI_RuneSelectPopup>();
                    if (popup != null) typeof(UI_RuneSelectPopup).GetMethod("OnSkipClicked", NonPub)?.Invoke(popup, null);
                    sb.AppendLine($"  룬 창 {(popup != null ? "열림 → 넘김" : "못 찾음")}");
                }
                ts = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - ts < 8f && ArmedGates() == 0)
                    await UniTask.Delay(250, DelayType.Realtime);
                sb.AppendLine($"  보상 뒤 열린 출구 {ArmedGates()}(1 이상이어야)");
                await Shot("MB_Ch1_exit", 600);
            }
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            EditorPrefs.SetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, prevLocal);
            Invoke("RestoreTestHubGui");
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/midboss_room_probe.txt", sb.ToString());
            Debug.Log("[MidBossRoom] 끝\n" + sb);
        }
    }

    private static MonsterBase FindEliteBoss()
    {
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m.IsEliteBoss && !m.IsDead && m.gameObject.activeInHierarchy) return m;
        return null;
    }

    private static int ArmedGates()
    {
        int n = 0;
        foreach (var g in UnityEngine.Object.FindObjectsByType<ProcRoomGate>(FindObjectsSortMode.None))
            if (g.IsArmed) n++;
        return n;
    }

    private static void MovePlayer(PlayerController player, Vector3 pos)
    {
        if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out var hit, 8f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore))
            pos.y = hit.point.y + 0.2f;
        player.transform.position = pos;
        if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
    }

    private static UniTask Shot(string name, int settleMs)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, settleMs }) : UniTask.CompletedTask;
    }

    private static void Invoke(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
}
#endif
