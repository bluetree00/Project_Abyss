#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 10-19 사용자 「챕터3 보스 맵에서도 인트로와 같은 바닥 돌출」 실측(테스트 허브 · 플레이 중 — 런이 없으면 챕터3 보스 직행으로 시작해 보스방까지 들어간다).
/// 높은 제단(SM_ArchFloorCurve_01b)이 켜졌나 · 제단 자리 바닥(콜라이더) · 내비메시 높이 · 플레이어 발밑 틈 · 보스 발밑 틈 · 게임 카메라 사진.
/// 지금 상태를 잰 뒤 높은 제단을 잠깐 켜 같은 자리를 다시 찍는다(고치기 전 모습 · 끝나면 되돌림).
/// 결과 Temp/ch3_altar.txt · Temp/ui_shots/CH3_altar_*.png · 로그 「[CH3altar] 끝」. 세이브는 건드리지 않는다.
/// </summary>
public static class Ch3AltarProbeEditor
{
    private const string Tag       = "[CH3altar]";
    private const string OutTxt    = "Temp/ch3_altar.txt";
    private const string ShotDir   = "Temp/ui_shots";
    private const string EnterExit = "RelicFairy/Test Run/Enter Open Exit (Play)";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-19 챕터3 보스 제단 바닥 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중(테스트 허브)에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-19 챕터3 보스 제단 바닥 실측\n");
        Directory.CreateDirectory(ShotDir);
        GameObject high = null;
        try
        {
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher != null && GameRunBootstrapper.Instance?.Run?.Player == null)
            {
                launcher.SelectChapter(3);
                typeof(TestHubLauncher).GetField("_bossApproach", Any)?.SetValue(launcher, true);
                launcher.TryLaunch();
            }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 60f && GameRunBootstrapper.Instance?.Run?.Player == null)
                await UniTask.Delay(500, DelayType.Realtime);
            await Wait(6f);

            // 보스 대기방 → 열린 출구로(보스방이 지어질 때까지 몇 번)
            GameObject zone = null;
            for (int i = 0; i < 6 && (zone = GameObject.Find("BossZoneCenter")) == null; i++)
            {
                EditorApplication.ExecuteMenuItem(EnterExit);
                for (float w = 0f; w < 8f && (zone = GameObject.Find("BossZoneCenter")) == null; w += 0.5f) await Wait(0.5f);
            }
            var player = GameRunBootstrapper.Instance?.Run?.Player;
            if (zone == null || zone.transform.parent == null || player == null) { sb.AppendLine($"준비 안 됨 — zone {zone != null} · player {player != null}"); return; }
            await Wait(5f);
            player.SetInvincible(300f);

            var arena = zone.transform.parent;
            sb.AppendLine($"아레나 {arena.name}");
            foreach (var t in arena.GetComponentsInChildren<Transform>(true))
                if (t.name == "SM_ArchFloorCurve_01b") high = t.gameObject;

            await MeasureAsync(sb, arena, player, "지금", "");
            if (high != null && !high.activeSelf)
            {
                high.SetActive(true);   // 고치기 전 모습(높은 제단 켬) — 끝나면 되돌린다
                await Wait(0.5f);
                await MeasureAsync(sb, arena, player, "높은 제단 켬(고치기 전)", "_before");
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (high != null) high.SetActive(false);
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    private static async UniTask MeasureAsync(StringBuilder sb, Transform arena, PlayerController player, string label, string suffix)
    {
        int mask = ~LayerMask.GetMask("Player", "Monster");
        sb.AppendLine($"── {label}");
        foreach (var t in arena.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != "SM_ArchFloorCurve_01b" && t.name != "SM_ArchFloorCurve_01b0") continue;
            float top = float.NegativeInfinity;
            foreach (var r in t.GetComponentsInChildren<Renderer>(false)) top = Mathf.Max(top, r.bounds.max.y);
            sb.AppendLine($"{t.name} 켜짐 {t.gameObject.activeInHierarchy} · 로컬 {arena.InverseTransformPoint(t.position)} · 그림 윗면 {(float.IsInfinity(top) ? "-" : top.ToString("F2"))}");
        }
        foreach (var (name, x, z) in new[] { ("제단 가운데", 0f, 14f), ("제단 앞", 0f, 6f), ("가운데", 0f, 0f), ("입구 쪽", 0f, -8f) })
        {
            Vector3 p = arena.TransformPoint(new Vector3(x, 0f, z));
            float fy = float.NaN; string col = "-";
            if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var hit, 80f, mask, QueryTriggerInteraction.Ignore)) { fy = hit.point.y; col = hit.collider.name; }
            float ny = !float.IsNaN(fy) && NavMesh.SamplePosition(new Vector3(p.x, fy, p.z), out var nh, 3f, NavMesh.AllAreas) ? nh.position.y : float.NaN;
            sb.AppendLine($"{name}: 바닥 {fy:F2}({col}) · 내비 {ny:F2} · 차 {ny - fy:F2}");
        }
        foreach (var (shot, z) in new[] { ("CH3_altar_1_front", -4f), ("CH3_altar_2_on", 10f) })
        {
            Vector3 p = arena.TransformPoint(new Vector3(0f, 0f, z));
            if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var h2, 80f, mask, QueryTriggerInteraction.Ignore)) p.y = h2.point.y + 0.3f;
            MovePlayer(player, p);
            var camCtrl = GameCameraController.Instance;
            if (camCtrl != null) camCtrl.SetHeadingImmediate(camCtrl.CurrentHeading);
            await Wait(1.8f);
            string under = "-";
            float gap = float.NaN;
            if (Physics.Raycast(player.transform.position + Vector3.up * 0.5f, Vector3.down, out var g, 10f, mask, QueryTriggerInteraction.Ignore))
            { gap = player.transform.position.y - g.point.y; under = g.collider.name; }
            sb.AppendLine($"{shot}{suffix}: 플레이어 발밑 틈 {gap:F2} m({under})");
            await ShotAsync(shot + suffix);
        }
        var spawner = Object.FindFirstObjectByType<BossSpawner>();
        var boss = spawner != null ? spawner.SpawnedBoss : null;
        if (boss != null)
        {
            float bg = float.NaN; string bu = "-";
            if (Physics.Raycast(boss.transform.position + Vector3.up * 0.5f, Vector3.down, out var b, 10f, mask, QueryTriggerInteraction.Ignore))
            { bg = boss.transform.position.y - b.point.y; bu = b.collider.name; }
            sb.AppendLine($"보스 {boss.name} 로컬 {arena.InverseTransformPoint(boss.transform.position)} · 발밑 틈 {bg:F2} m({bu})");
        }
        else sb.AppendLine("보스 아직 없음");
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

    private static UniTask Wait(float seconds) => UniTask.Delay(System.TimeSpan.FromSeconds(seconds), DelayType.Realtime);

    private static async UniTask ShotAsync(string name)
    {
        var runner = Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        await UniTask.WaitForEndOfFrame(runner);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (tex == null) return;
        File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
        Object.Destroy(tex);
    }
}
#endif
