using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight 보스룸 그리드 컨텍스트.
/// 맵 바닥을 Width×Height 셀로 나누며, 셀↔월드 좌표 변환을 제공한다.
/// 기본값: 30×30, cellSize=1f, 중심=Vector3.zero
/// </summary>
public static class DKBossRoomContext
{
    // ── 기본 설정 ──────────────────────────────────────────
    public static int     Width       = 30;
    public static int     Height      = 30;
    public static float   CellSize    = 1f;
    public static Vector3 WorldCenter = Vector3.zero;

    // ── 초기화 ─────────────────────────────────────────────

    /// <summary>보스룸 그리드 파라미터를 설정한다.</summary>
    public static void Initialize(int width, int height, float cellSize, Vector3 worldCenter)
    {
        Width       = width;
        Height      = height;
        CellSize    = cellSize;
        WorldCenter = worldCenter;
    }

    // ── 좌표 변환 ──────────────────────────────────────────

    /// <summary>
    /// 그리드 셀 (x, z) → 월드 좌표 변환.
    /// yOffset: 바닥에서 살짝 띄울 높이 (장판 Z-파이팅 방지).
    /// </summary>
    public static Vector3 CellToWorld(int x, int z, float yOffset = 0.05f)
    {
        float wx = WorldCenter.x + (x * CellSize) - (Width  - 1) * 0.5f * CellSize;
        float wz = WorldCenter.z + (z * CellSize) - (Height - 1) * 0.5f * CellSize;
        return new Vector3(wx, WorldCenter.y + yOffset, wz);
    }

    /// <summary>월드 좌표 → 그리드 셀 변환.</summary>
    public static Vector2Int WorldToCell(Vector3 world)
    {
        float localX = world.x - WorldCenter.x + (Width  - 1) * 0.5f * CellSize;
        float localZ = world.z - WorldCenter.z + (Height - 1) * 0.5f * CellSize;
        int cellX = Mathf.RoundToInt(localX / CellSize);
        int cellZ = Mathf.RoundToInt(localZ / CellSize);
        cellX = Mathf.Clamp(cellX, 0, Width  - 1);
        cellZ = Mathf.Clamp(cellZ, 0, Height - 1);
        return new Vector2Int(cellX, cellZ);
    }

    /// <summary>
    /// 벽(외곽)을 제외한 내부 셀 목록 반환.
    /// x=1..Width-2, z=1..Height-2
    /// </summary>
    public static IEnumerable<Vector2Int> GetInteriorCells()
    {
        for (int z = 1; z <= Height - 2; z++)
        for (int x = 1; x <= Width  - 2; x++)
            yield return new Vector2Int(x, z);
    }

    /// <summary>해당 좌표가 내부 셀인지 판정한다.</summary>
    public static bool IsInterior(int x, int z)
        => x >= 1 && x <= Width - 2 && z >= 1 && z <= Height - 2;
}
}
