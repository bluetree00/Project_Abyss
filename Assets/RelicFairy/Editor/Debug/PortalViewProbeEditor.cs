#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-19 ⑨ 사용자 「베이스캠프 던전 입구 포탈 이펙트 — 위치도 이상하고 리소스도 좋지 않다」 — 지금 모습 · 범위 재기.
/// 거점(BaseCamp) 플레이 중: 플레이어를 포탈 발판(PortalCenter) 앞 9 m · 14 m에 세워 포탈을 보게 하고 게임 카메라로 찍는다.
/// 돌 고리(PortalFrame) · 소용돌이(PortalVortex) · 번개 막(Arcane_Portal) 렌더러 범위(가운데 · 반폭)를 적는다 — 소용돌이를 고리 안에 맞추는 근거.
/// 결과: Logs/boss_fight_sim/portal_view_*/view.txt · 사진 · 로그 「[PortalView] 끝」.
/// </summary>
public static class PortalViewProbeEditor
{
    private static readonly string[] Watch = { "PortalFrame", "PortalVortex", "Arcane_Portal", "PortalDais", "PortalApron", "PortalLight" };
    // (발판 앞 거리 m, 축에서 돌린 각도°) — 정면 + 사용자 사진(09_포탈)처럼 옆에서
    private static readonly (float back, float yaw)[] Shots = { (14f, 0f), (9f, -40f), (9f, 40f) };

    [MenuItem("RelicFairy/Debug/10-19 Portal View (Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[PortalView] 플레이 모드(거점)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        string outDir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim", $"portal_view_{DateTime.Now:MMdd_HHmmss}");
        Directory.CreateDirectory(outDir);
        var sb = new StringBuilder("거점 던전 입구 포탈 — 지금 모습\n");
        try
        {
            var center = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.name == "PortalCenter");
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            if (center == null || player == null) { sb.AppendLine($"PortalCenter {(center != null)} · 플레이어 {(player != null)}"); return; }
            Vector3 c = center.position;
            sb.AppendLine($"PortalCenter {c}");
            foreach (var name in Watch)
                foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(t => t.name == name))
                {
                    var rs = t.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                    if (rs.Length == 0) { sb.AppendLine($"  {name}: 렌더러 없음 · 자리 {t.position} · 켜짐 {t.gameObject.activeInHierarchy}"); continue; }
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    sb.AppendLine($"  {name}: 자리 {t.position} · 회전 {t.eulerAngles} · 배율 {t.lossyScale.x:0.000} · 범위 가운데 {b.center} · 반폭 {b.extents} · 렌더러 {rs.Length} · 켜짐 {t.gameObject.activeInHierarchy}");
                    foreach (var r in rs.OrderByDescending(r => r.bounds.size.sqrMagnitude).Take(4))
                        sb.AppendLine($"     - {r.name}: 가운데 {r.bounds.center} · 반폭 {r.bounds.extents}");
                }

            // 발판에서 계단(거점 쪽)으로 — 포탈을 보는 방향
            var frame = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).FirstOrDefault(t => t.name == "PortalFrame");
            Vector3 look = frame != null ? frame.position - c : Vector3.right; look.y = 0f;
            if (look.sqrMagnitude < 0.01f) look = Vector3.right;
            look.Normalize();
            var cam = UnityEngine.Object.FindFirstObjectByType<GameCameraController>();
            await UniTask.Delay(2500, DelayType.Realtime);   // 첫 장이 화면 전환 중 검게 찍혔다
            int n = 0;
            foreach (var (back, turn) in Shots)
            {
                Vector3 dir = Quaternion.Euler(0f, turn, 0f) * look;
                float yaw = Quaternion.LookRotation(dir).eulerAngles.y;
                Vector3 at = c - dir * back;
                if (Physics.Raycast(at + Vector3.up * 10f, Vector3.down, out var h, 30f, ~0, QueryTriggerInteraction.Ignore)) at.y = h.point.y + 0.05f;
                player.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
                if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = at; rb.linearVelocity = Vector3.zero; }
                if (cam != null) cam.SetHeadingImmediate(yaw);
                await UniTask.Delay(1500, DelayType.Realtime);
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, $"portal_{n++}_{back:0}m_{turn:0}.png"));
                sb.AppendLine($"  찍음 — 발판 앞 {back} m · 축에서 {turn}° (yaw {yaw:0})");
                await UniTask.Delay(400, DelayType.Realtime);
            }
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText(Path.Combine(outDir, "view.txt"), sb.ToString(), Encoding.UTF8);
            Debug.Log($"[PortalView] 끝 — {outDir}");
        }
    }
}
#endif
