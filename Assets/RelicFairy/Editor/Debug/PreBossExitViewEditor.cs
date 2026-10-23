#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-18 ⑭ 사용자 「Ch2 보스 대기방 입구가 여전히 입구로 안 보인다 — 대기방은 다음 보스방 광장이 보이는 듯한 입구와 연출」 — 지금 모습 촬영.
/// 보스 대기방(PreBoss)에서 출구 문(ProcRoomGate) 앞 9 m · 16 m에 플레이어를 세우고(문을 보게) 게임 카메라 그대로 찍는다.
/// 문 가운데(바닥 + 2 m)에서 바깥으로 60 m 레이캐스트해 닿는 것(이름 · 층 · 거리)과, 문 너머 바닥이 어디까지 이어지는지 적는다.
/// 결과: Logs/boss_fight_sim/preboss_exit_*/view.txt · 사진 · 로그 「[PreBossExit] 끝」.
/// </summary>
public static class PreBossExitViewEditor
{
    private static readonly float[] Back = { 9f, 16f };

    [MenuItem("RelicFairy/Debug/10-18 PreBoss Exit View (Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[PreBossExit] 플레이 모드(보스 대기방)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim", $"preboss_exit_{DateTime.Now:MMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("보스 대기방 출구 — 지금 모습\n");
        try
        {
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            if (player == null) { sb.AppendLine("플레이어 없음"); return; }
            var gate = UnityEngine.Object.FindObjectsByType<ProcRoomGate>(FindObjectsSortMode.None)
                .OrderBy(g => (g.transform.position - player.transform.position).sqrMagnitude).FirstOrDefault();
            if (gate == null) { sb.AppendLine("출구 문 없음"); return; }
            Vector3 gp = gate.transform.position;
            Vector3 outward = gp - player.transform.position; outward.y = 0f; outward.Normalize();
            float ground = Physics.Raycast(gp + Vector3.up * 8f, Vector3.down, out var gh, 30f, ~0, QueryTriggerInteraction.Ignore) ? gh.point.y : gp.y;
            sb.AppendLine($"문 {gate.name} · 자리 {gp} · 바깥 {outward} · 바닥 {ground:0.00} · 문 로컬 크기 {gate.transform.lossyScale}");

            Vector3 eye = new(gp.x, ground + 2f, gp.z);
            var hits = Physics.RaycastAll(eye - outward * 1f, outward, 60f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).Take(6);
            foreach (var h in hits) sb.AppendLine($"  앞 {h.distance - 1f,5:0.0} m: {h.collider.name}({LayerMask.LayerToName(h.collider.gameObject.layer)})");
            for (float d = 2f; d <= 30f; d += 4f)
            {
                bool floor = Physics.Raycast(new Vector3(gp.x, ground + 3f, gp.z) + outward * d, Vector3.down, out var fh, 8f, ~0, QueryTriggerInteraction.Ignore);
                sb.AppendLine($"  문 너머 {d,4:0} m 바닥: {(floor ? $"{fh.collider.name} y {fh.point.y:0.00}" : "없음(허공)")}");
            }

            // 출구 둘레(가로 ±30 m · 문 앞 10 m ~ 너머 60 m) 렌더러 범위 — 너머에 무엇을 지을지 자리 재기(문 기준 로컬: 옆 x · 바깥 z)
            var right = Vector3.Cross(Vector3.up, outward);
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                Vector3 c = r.bounds.center - gp;
                float lx = Vector3.Dot(c, right), lz = Vector3.Dot(c, outward);
                bool floor = r.name.Contains("ArchFloor");   // 방 바닥은 멀어도 — 바닥 조각 크기를 잰다
                if (!floor && (Mathf.Abs(lx) > 30f || lz < -10f || lz > 60f)) continue;
                if (r.name.Contains("_LOD") && !r.name.EndsWith("_LOD0")) continue;
                string key = r.transform.parent != null ? r.transform.parent.name + "/" + r.name : r.name;
                if (!seen.Add(key)) continue;
                Vector3 e = r.bounds.extents;
                sb.AppendLine($"  물체 {key}: 옆 {lx,6:0.0} · 바깥 {lz,6:0.0} · 높이 {r.bounds.min.y:0.0}~{r.bounds.max.y:0.0} · 반폭 {e.x:0.0}×{e.z:0.0}");
            }

            var cam = UnityEngine.Object.FindFirstObjectByType<GameCameraController>();
            float yaw = Quaternion.LookRotation(outward).eulerAngles.y;
            int n = 0;
            foreach (float back in Back)
            {
                Vector3 at = new Vector3(gp.x, ground + 0.1f, gp.z) - outward * back;
                player.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
                if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = at; rb.linearVelocity = Vector3.zero; }
                if (cam != null) cam.SetHeadingImmediate(yaw);
                await UniTask.Delay(1500, DelayType.Realtime);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"exit_{n++}_{back:0}m.png"));
                sb.AppendLine($"  찍음 — 문 앞 {back} m");
                await UniTask.Delay(400, DelayType.Realtime);
            }
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText(Path.Combine(dir, "view.txt"), sb.ToString(), Encoding.UTF8);
            Debug.Log($"[PreBossExit] 끝 — {dir}");
        }
    }
}
#endif
