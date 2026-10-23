#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-23 E4 사용자 「Ch4 보스 대기방 입구(출구)가 기둥에 가려 안 보인다」 — 시작 자리 화면 촬영 · 가리는 것 찾기.
/// 플레이어가 막 선 자리 그대로 한 장(시작 카메라 방향) → 카메라를 출구(가장 먼 ProcRoomGate) 쪽으로 돌려 한 장.
/// 카메라 → 출구 가운데(바닥 + 2 m) 선분을 1 m마다 짚어 그 자리를 덮는 렌더러(보이는 것 기준)와 콜라이더를 적는다.
/// 결과: Logs/boss_fight_sim/preboss_start_*/view.txt · 사진 · 로그 「[PreBossStart] 끝」.
/// </summary>
public static class PreBossStartViewEditor
{
    private const float SampleStep  = 1f;
    private const float GateEyeLift = 2f;
    private const float NavSnap = 10f;
    private static readonly float[] AlongPath  = { 18f, 24f, 30f, 36f };
    private static readonly float[] BeforeGate = { 16f, 9f };

    [MenuItem("RelicFairy/Debug/10-23 E4 PreBoss Start View (Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[PreBossStart] 플레이 모드(보스 대기방)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim", $"preboss_start_{DateTime.Now:MMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("보스 대기방 시작 화면 — 출구가 보이는가\n");
        try
        {
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            var cam    = UnityEngine.Object.FindFirstObjectByType<GameCameraController>();
            var view   = Camera.main;
            if (player == null || view == null) { sb.AppendLine("플레이어 · 카메라 없음"); return; }
            var gate = UnityEngine.Object.FindObjectsByType<ProcRoomGate>(FindObjectsSortMode.None)
                .OrderByDescending(g => (g.transform.position - player.transform.position).sqrMagnitude).FirstOrDefault();
            if (gate == null) { sb.AppendLine("출구 문 없음"); return; }

            Vector3 pp = player.transform.position;
            Vector3 gp = gate.transform.position;
            Vector3 toGate = gp - pp; toGate.y = 0f;
            float gateYaw = Quaternion.LookRotation(toGate).eulerAngles.y;
            float camYaw  = view.transform.eulerAngles.y;
            sb.AppendLine($"플레이어 {pp} · 문 {gate.name} {gp} · 거리 {toGate.magnitude:0.0} m · 문 방위 {gateYaw:0} · 카메라 방위 {camYaw:0} · 차 {Mathf.DeltaAngle(camYaw, gateYaw):0}°");

            await UniTask.Delay(500, DelayType.Realtime);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "start_0_as_is.png"));
            await UniTask.Delay(400, DelayType.Realtime);
            Occluders(sb, "시작 카메라", view.transform.position, gp + Vector3.up * GateEyeLift);
            Occluders(sb, "플레이어 눈", pp + Vector3.up * 1.6f, gp + Vector3.up * GateEyeLift);

            if (cam != null)
            {
                cam.SetHeadingImmediate(gateYaw);
                await UniTask.Delay(1500, DelayType.Realtime);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "start_1_face_exit.png"));
                await UniTask.Delay(400, DelayType.Realtime);
                Occluders(sb, "출구 쪽 카메라", view.transform.position, gp + Vector3.up * GateEyeLift);
            }

            foreach (var g in UnityEngine.Object.FindObjectsByType<ProcRoomGate>(FindObjectsSortMode.None))
                sb.AppendLine($"  문 {PathOf(g.transform)} {g.transform.position} · 켜짐 {g.gameObject.activeInHierarchy}");

            // 걸어가는 길 — 시작에서 문 쪽으로 18 · 24 · 30 · 36 m(다리 끝 · 계단 아래 · 계단 중간) · 문 앞 16 · 9 m. 내비 위 가장 가까운 자리에 문을 보게 세운다
            Vector3 fwd = toGate.normalized;
            int n = 2;
            var spots = AlongPath.Select(d => (Vector3.Lerp(pp, gp, d / toGate.magnitude), $"path_{d:0}m"))
                .Concat(BeforeGate.Select(b => (gp - fwd * b, $"gate_{b:0}m")));
            foreach (var (spot, label) in spots)
            {
                Vector3 at = spot;
                if (UnityEngine.AI.NavMesh.SamplePosition(at, out var nh, NavSnap, UnityEngine.AI.NavMesh.AllAreas)) at = nh.position;
                player.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, gateYaw, 0f));
                if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = at; rb.linearVelocity = Vector3.zero; }
                if (cam != null) cam.SetHeadingImmediate(gateYaw);
                await UniTask.Delay(1500, DelayType.Realtime);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"start_{n++}_{label}.png"));
                await UniTask.Delay(400, DelayType.Realtime);
                Occluders(sb, $"{label} 카메라(발 {at})", view.transform.position, gp + Vector3.up * GateEyeLift);
            }
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText(Path.Combine(dir, "view.txt"), sb.ToString(), Encoding.UTF8);
            Debug.Log($"[PreBossStart] 끝 — {dir}");
        }
    }

    /// <summary><paramref name="from"/> → <paramref name="to"/> 선분을 덮는 렌더러(경계 상자) · 콜라이더 — 처음 덮는 거리 순.</summary>
    private static void Occluders(StringBuilder sb, string tag, Vector3 from, Vector3 to)
    {
        sb.AppendLine($"[{tag}] {from} → {to}");
        var first = new Dictionary<string, float>();
        var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
            .Where(r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer) && !(r is TrailRenderer) && !(r is LineRenderer))
            .Where(r => !r.name.Contains("_LOD") || r.name.EndsWith("_LOD0"))
            .ToArray();
        float len = Vector3.Distance(from, to);
        for (float d = SampleStep; d < len - SampleStep; d += SampleStep)
        {
            Vector3 p = Vector3.Lerp(from, to, d / len);
            foreach (var r in renderers)
            {
                if (!r.bounds.Contains(p)) continue;
                string key = PathOf(r.transform);
                if (!first.ContainsKey(key)) first[key] = d;
            }
        }
        foreach (var kv in first.OrderBy(k => k.Value).Take(20)) sb.AppendLine($"  렌더러 {kv.Value,5:0.0} m: {kv.Key}");
        foreach (var h in Physics.RaycastAll(from, (to - from).normalized, len, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).Take(8))
            sb.AppendLine($"  콜라이더 {h.distance,5:0.0} m: {PathOf(h.collider.transform)}({LayerMask.LayerToName(h.collider.gameObject.layer)})");
    }

    private static string PathOf(Transform t)
    {
        var parts = new List<string>();
        for (int i = 0; t != null && i < 4; i++, t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }
}
#endif
