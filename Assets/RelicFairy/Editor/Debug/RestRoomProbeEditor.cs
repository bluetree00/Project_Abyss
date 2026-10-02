#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-01 쉼터 · 요정의 샘 실측 — ① 대기방 요정의 샘: 체력 30% · 포션 0으로 만든 뒤 마셔 회복량을 잰다
/// ② 챕터 1~4 쉼터 방을 차례로 지어 들어가(로컬 룸 풀) 모닥불 「쉰다」(1 · 3챕터) · 모루 「벼린다」(2 · 4챕터)를 실제로 고르고
/// 체력 · 무기 강화 단계 · 하나만 고를 수 있는지(두 번째 선택이 막히는지)를 기록하고 무대를 찍는다.
/// 결과: Temp/rest_probe/&lt;태그&gt;/ · report.txt. ⚠️ 테스트 허브 런에서만 — 지금 런의 체력 · 무기가 실제로 바뀐다.
/// </summary>
public static class RestRoomProbeEditor
{
    private const BindingFlags NonPub = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-01 쉼터 · 요정의 샘 실측 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            Debug.LogWarning("[RestProbe] 런에 들어간 뒤 실행해야 한다.");
            return;
        }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        string dir = Path.Combine("Temp", "rest_probe", DateTime.Now.ToString("HHmmss"));
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("쉼터 · 요정의 샘 실측\n");
        bool prevLocal = EditorPrefs.GetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, false);
        EditorPrefs.SetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, true);
        Invoke("HideTestHubGui");
        try
        {
            var run = GameRunBootstrapper.Instance.Run;

            // ① 요정의 샘(대기방에 있을 때만)
            var spring = UnityEngine.Object.FindFirstObjectByType<ChapterSpring>();
            if (spring != null)
            {
                var stats = run.Player.RuntimeStats;
                stats.SetHp(Mathf.RoundToInt(stats.MaxHp * 0.3f));
                run.PlayerState.RestorePotions(0, run.PlayerState.PotionCapacity);
                int hp0 = stats.Hp, p0 = run.PlayerState.PotionCount;
                MovePlayer(run.Player, spring.transform.position + new Vector3(0f, 0.2f, -2f));
                await Shot("Rest_Spring_before", 600);
                typeof(ChapterSpring).GetMethod("Drink", NonPub)?.Invoke(spring, null);
                await Shot("Rest_Spring_after", 700);
                sb.AppendLine($"요정의 샘: 체력 {hp0}→{stats.Hp}/{stats.MaxHp} · 포션 {p0}→{run.PlayerState.PotionCount}/{run.PlayerState.PotionCapacity}");
                typeof(ChapterSpring).GetMethod("Drink", NonPub)?.Invoke(spring, null);
                sb.AppendLine($"  두 번째 마시기 → 체력 {stats.Hp} (변화 없어야)");
            }
            else sb.AppendLine("요정의 샘 없음(대기방이 아님)");

            // ② 쉼터 방 — 대기방이면 게이트를 지나 절차 진행부터
            var ensure = typeof(ServiceRoomTourProbeEditor).GetMethod("EnsureProcRunAsync", BindingFlags.Static | BindingFlags.NonPublic);
            if (ensure != null) await (UniTask<bool>)ensure.Invoke(null, null);
            var flow  = RunFlowController.Active;
            var enter = typeof(RunFlowController).GetMethod("EnterRoomAsync", NonPub);
            var cur   = typeof(RunFlowController).GetField("_current", NonPub);
            if (flow == null || enter == null || cur == null) { sb.AppendLine("절차 진행 없음"); return; }

            for (int ch = 1; ch <= 4; ch++)
            {
                var pool = await Managers.ZoneLayout.LoadPoolAsync($"CHAPTER_{ch}_ROOM_POOL");
                var entry = pool?.Find(p => string.Equals(p.category, "Rest", StringComparison.OrdinalIgnoreCase));
                if (entry == null) { sb.AppendLine($"Ch{ch}: 쉼터 방이 풀에 없음"); continue; }

                var plan = new DoorPlan { kind = RoomPlanKind.Rest, entry = entry };
                var task = (UniTask)enter.Invoke(flow, new object[] { plan, DoorEdge.North, CancellationToken.None, 0 });
                await UniTask.WhenAny(task, UniTask.Delay(20000, ignoreTimeScale: true));
                await UniTask.Delay(2200, ignoreTimeScale: true);

                var result = cur.GetValue(flow);
                var room   = result?.GetType().GetField("roomGO")?.GetValue(result) as GameObject;
                var ctrl   = room != null ? room.GetComponent<RestRoomController>() : null;
                if (ctrl == null) { sb.AppendLine($"Ch{ch}: {entry.pool_key} — RestRoomController 없음"); continue; }

                var fire  = room.transform.Find("RestFire");
                var anvil = room.transform.Find("RestAnvil");
                sb.AppendLine($"== Ch{ch} {entry.pool_key} · 모닥불 {(fire != null)} · 모루 {(anvil != null)}");
                Vector3 look = (fire != null ? fire.position : room.transform.position) + Vector3.up * 0.4f;
                Render(Path.Combine(dir, $"{entry.pool_key}_stage.png"), look + new Vector3(0f, 6.5f, -7.5f), look, 48f, 960, 540);

                var stats = run.Player.RuntimeStats;
                var w = run.Player.WeaponManager?.Weapon0Data;
                bool rest = ch % 2 == 1;
                stats.SetHp(Mathf.RoundToInt(stats.MaxHp * 0.4f));
                int hp0 = stats.Hp, lv0 = w != null ? w.enhanceLevel : -1;
                MovePlayer(run.Player, (rest ? fire : anvil)?.position + new Vector3(0f, 0.2f, -2.2f) ?? look);
                await Shot($"Rest_{entry.pool_key}_near", 700);
                typeof(RestRoomController).GetMethod(rest ? "Rest" : "Forge", NonPub)?.Invoke(ctrl, null);
                await Shot($"Rest_{entry.pool_key}_chosen", 800);
                sb.AppendLine($"  {(rest ? "쉰다" : "벼린다")}: 체력 {hp0}→{stats.Hp}/{stats.MaxHp} · 무기 {w?.displayName} 강화 {lv0}→{(w != null ? w.enhanceLevel : -1)}");
                // 하나만 — 나머지 선택은 막혀야 한다
                int hp1 = stats.Hp, lv1 = w != null ? w.enhanceLevel : -1;
                typeof(RestRoomController).GetMethod(rest ? "Forge" : "Rest", NonPub)?.Invoke(ctrl, null);
                sb.AppendLine($"  두 번째 선택 → 체력 {hp1}→{stats.Hp} · 강화 {lv1}→{(w != null ? w.enhanceLevel : -1)} (변화 없어야)");
            }
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            EditorPrefs.SetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, prevLocal);
            Invoke("RestoreTestHubGui");
            File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString());
            Debug.Log($"[RestProbe] 끝 → {dir}/report.txt");
        }
    }

    /// <summary>로컬 방 풀(Docs CSV) 우선 — CDN에 아직 안 올린 방(쉼터 등)을 일반 자동 실측 · 시뮬에서 쓰려고. 다음 런부터.</summary>
    [MenuItem("RelicFairy/Test Run/Local Room Pool - On")]
    private static void LocalPoolOn()  { EditorPrefs.SetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, true);  Debug.Log("[RestProbe] 로컬 방 풀 우선 켬"); }

    [MenuItem("RelicFairy/Test Run/Local Room Pool - Off")]
    private static void LocalPoolOff() { EditorPrefs.SetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, false); Debug.Log("[RestProbe] 로컬 방 풀 우선 끔"); }

    private static void MovePlayer(PlayerController player, Vector3 pos)
    {
        if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out var hit, 8f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore))
            pos.y = hit.point.y + 0.2f;
        player.transform.position = pos;
        if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
    }

    private static void Render(string path, Vector3 pos, Vector3 target, float fov, int w, int h)
        => typeof(ServiceRoomTourProbeEditor).GetMethod("Render", BindingFlags.Static | BindingFlags.NonPublic)?
               .Invoke(null, new object[] { path, pos, target, fov, w, h });

    private static UniTask Shot(string name, int settleMs)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, settleMs }) : UniTask.CompletedTask;
    }

    private static void Invoke(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
}
#endif
