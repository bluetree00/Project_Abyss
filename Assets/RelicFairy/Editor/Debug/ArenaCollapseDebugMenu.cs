#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 붕괴형 아레나(<see cref="ArenaTileGrid"/>)를 <b>플레이 중</b> 손으로 무너뜨리는 점검 메뉴.
/// 메뉴: RelicFairy/Debug/Arena/…
///
/// 왜 — 지형 붕괴는 리치 2페이즈·N-1 패턴까지 가야 보인다. 붕괴 자체(낙사 복구·해골 정리·게이트 자리 유지·상한)를
/// 보스전과 떼어서 검증하려고 둔다. 대마법사 기획 §6 P1 검증 기준:
/// "타일을 수동 파괴해도 플레이어가 무한 낙사에 빠지지 않는다".
/// </summary>
public static class ArenaCollapseDebugMenu
{
    private const string Root = "RelicFairy/Debug/Arena/";

    [MenuItem(Root + "Teleport Player To Arena Spawn (Play)")]
    public static void TeleportToSpawn()
    {
        if (!TryGetGrid(out var grid)) return;
        var spawn = grid.transform.Find("PlayerSpawn");
        if (spawn == null) { Debug.LogWarning("[ArenaDebug] 아레나에 PlayerSpawn 없음."); return; }
        Teleport(spawn.position);
    }

    // 아래 세 좌표는 아레나 로컬 — 레벨 설계서 §6 마커 배치 기준.
    [MenuItem(Root + "Teleport Player Into Entrance Trigger (Play)")]
    public static void TeleportIntoTrigger() => TeleportLocal(new Vector3(0f, 0.25f, -28f));

    [MenuItem(Root + "Teleport Player Onto Altar z-10 (Play)")]
    public static void TeleportOntoAltar() => TeleportLocal(new Vector3(0f, 0.25f, -10f));

    [MenuItem(Root + "Teleport Player To Outer Ring (Play)")]
    public static void TeleportToOuterRing() => TeleportLocal(new Vector3(27.5f, 0.25f, 2.5f));

    private static void TeleportLocal(Vector3 local)
    {
        if (!TryGetGrid(out var grid)) return;
        Teleport(grid.transform.TransformPoint(local));
    }

    private static void Teleport(Vector3 pos)
    {
        if (!TryGetPlayer(out var player)) return;
        player.transform.position = pos;
        if (player.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.position       = pos;
            rb.linearVelocity = Vector3.zero;
        }
        Debug.Log($"[ArenaDebug] 플레이어 이동 → {pos}");
    }

    [MenuItem(Root + "Collapse Tile Under Player (Play)")]
    public static void CollapseUnderPlayer()
    {
        if (!TryGetGrid(out var grid) || !TryGetPlayer(out var player)) return;
        if (!grid.TryGetCell(player.transform.position, out var cell))
        {
            Debug.LogWarning("[ArenaDebug] 플레이어가 격자 위에 있지 않다.");
            return;
        }
        int n = grid.CollapseCells(new List<Vector2Int> { cell });
        Log(grid, $"발밑 칸 {cell}", n);
    }

    [MenuItem(Root + "Collapse Outer Ring (Play)")]
    public static void CollapseOuterRing()
    {
        if (!TryGetGrid(out var grid)) return;
        Log(grid, $"최외곽 링 {grid.OuterRing}", grid.CollapseRing(grid.OuterRing));
    }

    [MenuItem(Root + "Collapse Quadrant NE (Play)")] public static void CollapseNE() => CollapseQuadrant(0, "북동");
    [MenuItem(Root + "Collapse Quadrant NW (Play)")] public static void CollapseNW() => CollapseQuadrant(1, "북서");
    [MenuItem(Root + "Collapse Quadrant SW (Play)")] public static void CollapseSW() => CollapseQuadrant(2, "남서");
    [MenuItem(Root + "Collapse Quadrant SE (Play)")] public static void CollapseSE() => CollapseQuadrant(3, "남동");

    private static void CollapseQuadrant(int q, string label)
    {
        if (!TryGetGrid(out var grid)) return;
        Log(grid, $"사분면 {label}", grid.CollapseQuadrant(q));
    }

    private static void Log(ArenaTileGrid grid, string what, int count)
    {
        Debug.Log($"[ArenaDebug] {what} 붕괴 {count}장 — 누적 {grid.CollapsedCount}/{grid.TileCount}, 남은 예산 {grid.RemainingBudget}");
    }

    private static bool TryGetGrid(out ArenaTileGrid grid)
    {
        grid = null;
        if (!Application.isPlaying) { Debug.LogWarning("[ArenaDebug] 플레이 모드에서만 동작한다."); return false; }
        grid = ArenaTileGrid.Active;
        if (grid == null) { Debug.LogWarning("[ArenaDebug] 활성 ArenaTileGrid 없음."); return false; }
        return true;
    }

    private static bool TryGetPlayer(out PlayerController player)
    {
        player = GameRunBootstrapper.Instance?.Run?.Player
              ?? Object.FindFirstObjectByType<PlayerController>(FindObjectsInactive.Exclude);
        if (player == null) Debug.LogWarning("[ArenaDebug] 플레이어를 찾지 못했다.");
        return player != null;
    }
}
#endif
