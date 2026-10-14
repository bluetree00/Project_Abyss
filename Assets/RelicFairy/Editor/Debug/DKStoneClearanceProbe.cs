#if UNITY_EDITOR
using System.IO;
using System.Text;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 도구 · 플레이 중] 기사 공명 성흔석(DKResonanceStone)이 기둥 · 벽과 겹치는지 잰다(10-07 사용자: 왼쪽 석상이 기둥과 겹침).
/// 석상마다 자리 · 기사 기준 각도 · 반경 안 충돌체(지형)와 키 큰 렌더러(기둥 후보)까지 수평 거리를 적고 화면을 찍는다.
/// 결과: Logs/boss_fight_sim/stone_clearance_시각.txt + .png
/// </summary>
internal static class DKStoneClearanceProbe
{
    private const float Reach = 3f;   // 석상 몸 가운데서 이 거리 안을 본다

    [MenuItem("RelicFairy/Boss/DK/Resonance Stone Clearance (Play)")]
    private static void Probe()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[StoneProbe] 플레이 중에만"); return; }
        var sb     = new StringBuilder();
        var stones = Object.FindObjectsByType<DKResonanceStone>(FindObjectsSortMode.None);
        var dk     = Object.FindFirstObjectByType<DeathKnightBossMonster>();
        sb.AppendLine($"석상 {stones.Length}개 · 기사 {(dk != null ? dk.transform.position.ToString("F1") : "없음")}");
        int mask  = ~LayerMask.GetMask("Player", "Monster", "MonsterHit", "UI", "Ignore Raycast", "Ground");
        var rends = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        foreach (var s in stones)
        {
            Vector3 p   = s.transform.position;
            Vector3 mid = p + Vector3.up * 1.3f;
            float ang = dk != null ? Vector3.SignedAngle(dk.transform.forward, Flat(p - dk.transform.position), Vector3.up) : 0f;
            sb.AppendLine($"석상 {p:F2} · 기사 정면 기준 {ang:0}°");
            foreach (var c in Physics.OverlapSphere(mid, Reach, mask, QueryTriggerInteraction.Ignore))
            {
                if (c.transform.IsChildOf(s.transform)) continue;
                Vector3 cp = c.ClosestPoint(mid);
                sb.AppendLine($"  충돌체 {Flat(cp - p).magnitude:0.00} m · {c.GetType().Name} · 레이어 {LayerMask.LayerToName(c.gameObject.layer)} · {PathOf(c.transform)}");
            }
            foreach (var r in rends)
            {
                if (r == null || !r.enabled || r.transform.IsChildOf(s.transform)) continue;
                var b = r.bounds;
                if (b.size.y < 2f || b.min.y > p.y + 1f || b.max.y < p.y + 1.5f) continue;   // 바닥에 선 키 큰 것만
                float fd = Flat(b.ClosestPoint(mid) - p).magnitude;
                if (fd > 1.6f) continue;
                sb.AppendLine($"  렌더러 {fd:0.00} m · 크기 {b.size:F1} · {PathOf(r.transform)}");
            }
        }
        string dir  = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim");
        Directory.CreateDirectory(dir);
        string stem = Path.Combine(dir, $"stone_clearance_{System.DateTime.Now:MMdd_HHmmss}");
        File.WriteAllText(stem + ".txt", sb.ToString(), Encoding.UTF8);
        ScreenCapture.CaptureScreenshot(stem + ".png");
        Debug.Log($"[StoneProbe] {stem}.txt\n{sb}");
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    private static string PathOf(Transform t)
    {
        string path = t.name;
        for (var p = t.parent; p != null && path.Length < 140; p = p.parent) path = p.name + "/" + path;
        return path;
    }
}
#endif
