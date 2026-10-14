#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// [실측 · 플레이 중 · 기사 아레나] 10-13 「예고 = 판정」 — 기사 Strike(1페이지 전역 베기) · Tomb(2페이지 원탁의 무덤)은
/// 격자 맨 바깥 줄(x = 0 · W−1, z = 0 · H−1)엔 칸을 깔지 않는데 피해는 방패 · 안전 자리 밖 전역이다.
/// 칸이 안 깔리는 자리 중 플레이어가 설 수 있는 곳(어느 에이전트 NavMesh든 칸 중심 0.75 m 안 · 바닥 높이 ±0.6 m · 유리 앞쪽)을 센다.
/// 결과: Logs/boss_fight_sim/dk_border_*.txt · 로그 「[DKBorder] 끝」.
/// </summary>
internal static class DKGridBorderProbeEditor
{
    private const float SampleRadius = 0.75f;
    private const float FloorTolerance = 0.6f;

    [MenuItem("RelicFairy/Boss/DK Grid Border Probe (Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[DKBorder] 플레이 중에만"); return; }
        var sb = new StringBuilder("기사 격자 — 칸이 안 깔리는데 설 수 있는 자리(10-13 84)\n");
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"dk_border_{DateTime.Now:MMdd_HHmmss}.txt");
        try
        {
            var dk = UnityEngine.Object.FindFirstObjectByType<DeathKnightBossMonster>();
            if (dk == null) { sb.AppendLine("기사 없음 — 기사 아레나에서 돌린다"); return; }
            var zone = dk.Page2Zone;
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            s_from = player != null ? player.transform.position : zone.CenterWorld;
            int w = DKBossRoomContext.Width, h = DKBossRoomContext.Height;
            sb.AppendLine($"격자 {w}×{h} · 칸 {DKBossRoomContext.CellSize} m · 중심 {DKBossRoomContext.WorldCenter} · 유리 앞 z {zone.FrontLimitZ:0.0}(기사 쪽 {zone.TowardKnight:+0;-0}) · 바닥 y {zone.FloorY:0.00}");

            // 색 칸 패턴(빠른 베기 · 바꿔 베기 · 일반 베기 · 영혼 창 · 피라미드 등) — 기본 격자의 안쪽 칸만 깔고 판정
            Count(sb, "색 칸 패턴(기본 격자 · 안쪽만)", zone, (x, z) => DKBossRoomContext.IsInterior(x, z));

            // Strike — 격자 중심을 피라미드 앵커로 덮어써서 안쪽(1..W−2 × 1..H−2)에만 깐다
            Transform anchor = dk.PyramidStrikeAnchor;
            if (anchor != null) DKBossRoomContext.SetWorldCenterOverride(anchor.position);
            Count(sb, "Strike(앵커 중심 · 안쪽만)", zone, (x, z) => DKBossRoomContext.IsInterior(x, z));
            DKBossRoomContext.ClearWorldCenterOverride();

            // Tomb — 2페이지 구역(MinX..MaxX × MinZ..MaxZ) · 무너진 가운데 3×3은 무대 위험 구역(바닥 표시 · 틱)이라 뺀다
            Count(sb, "Tomb(2페이지 구역)", zone, (x, z) => zone.Contains(x, z) || zone.IsCollapsed(x, z));
            sb.AppendLine($"  참고: 가운데 3×3(무너짐) 중 설 수 있는 칸 {CountWhere(zone, (x, z) => zone.IsCollapsed(x, z))}");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[DKBorder] 끝 — {path}");
        }
    }

    // 격자 전체(+ 바깥 한 칸)를 훑어 「칸 없음 · 설 수 있음 · 유리 앞」을 센다
    private static void Count(StringBuilder sb, string label, DKPage2Zone zone, Func<int, int, bool> tiled)
    {
        int w = DKBossRoomContext.Width, h = DKBossRoomContext.Height;
        int standable = 0, gaps = 0, beyondMax = 0;
        var list = new StringBuilder();
        for (int z = -ScanMargin; z <= h - 1 + ScanMargin; z++)   // 격자 밖 넉넉히 — 바닥이 어디까지 이어지는지
        for (int x = -ScanMargin; x <= w - 1 + ScanMargin; x++)
        {
            Vector3 c = CellCenterUnclamped(x, z, zone.FloorY);
            if (!PlayerSide(zone, c) || !Standable(c, zone.FloorY) || !Reachable(c)) continue;
            standable++;
            int beyond = Mathf.Max(Mathf.Max(-x, x - (w - 1)), Mathf.Max(-z, z - (h - 1)));
            beyondMax = Mathf.Max(beyondMax, beyond);
            if (tiled(x, z)) continue;
            gaps++;
            if (gaps <= 40) list.Append($" ({x},{z}){(beyond > 0 ? $"밖{beyond}" : "")}");
        }
        sb.AppendLine($"  {label}: 갈 수 있는 칸 {standable}(격자 밖 최대 {beyondMax}칸) · 그중 칸 없음 {gaps}{(gaps > 0 ? " ⚠" : "")}");
        if (gaps > 0) sb.AppendLine("   칸 없음 목록:" + list);
    }

    private const int ScanMargin = 8;
    private static Vector3 s_from;   // 플레이어 자리 — 길이 이어지는 칸만 센다(막힌 입구 통로 · 벽 너머 바닥 제외)

    private static bool Reachable(Vector3 to)
    {
        var path = new NavMeshPath();
        int n = NavMesh.GetSettingsCount();
        for (int i = 0; i < n; i++)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = NavMesh.GetSettingsByIndex(i).agentTypeID, areaMask = NavMesh.AllAreas };
            if (!NavMesh.SamplePosition(s_from, out var a, 2f, filter) || !NavMesh.SamplePosition(to, out var b, SampleRadius, filter)) continue;
            if (NavMesh.CalculatePath(a.position, b.position, filter, path) && path.status == NavMeshPathStatus.PathComplete) return true;
        }
        return false;
    }

    private static int CountWhere(DKPage2Zone zone, Func<int, int, bool> pick)
    {
        int n = 0;
        for (int z = 0; z < DKBossRoomContext.Height; z++)
        for (int x = 0; x < DKBossRoomContext.Width; x++)
            if (pick(x, z) && Standable(CellCenterUnclamped(x, z, zone.FloorY), zone.FloorY)) n++;
        return n;
    }

    private static Vector3 CellCenterUnclamped(int x, int z, float floorY)
    {
        Vector3 p = DKBossRoomContext.CellToWorld(x, z, 0f);
        p.y = floorY;
        return p;
    }

    // 유리벽 앞(플레이어 쪽)인가 — 유리 너머 기사 쪽은 플레이어가 못 간다
    private static bool PlayerSide(DKPage2Zone zone, Vector3 p) => (zone.FrontLimitZ - p.z) * zone.TowardKnight >= -0.5f;

    // 어느 에이전트 NavMesh든 칸 중심 가까이 바닥 높이에 있으면 설 수 있다(에이전트 2종 따로 구움)
    private static bool Standable(Vector3 p, float floorY)
    {
        int n = NavMesh.GetSettingsCount();
        for (int i = 0; i < n; i++)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = NavMesh.GetSettingsByIndex(i).agentTypeID, areaMask = NavMesh.AllAreas };
            if (NavMesh.SamplePosition(p, out var hit, SampleRadius, filter) && Mathf.Abs(hit.position.y - floorY) <= FloorTolerance)
                return true;
        }
        return false;
    }
}
#endif
