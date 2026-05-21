using System;
using System.Collections.Generic;
using UnityEngine;

namespace Abyss.Monster
{
/// <summary>그리드 타일 정보. 셀 좌표, 색상, 생성된 GameObject를 보관한다.</summary>
public struct DKTileInfo
{
    public Vector2Int Cell;
    public DKSwordColor Color;
    public GameObject GO;
}

/// <summary>이펙트 스폰 단위. PerColumn/PerRow를 사용하면 줄 당 1개만 스폰된다.</summary>
public enum DKVfxGroupMode { PerCell, PerColumn, PerRow, PerCrossLine }

/// <summary>
/// DeathKnight 그리드 패턴 공통 헬퍼.
/// 타일 스폰/제거 및 플레이어 피격 판정을 담당한다.
/// </summary>
public static class DKGridPatternHelper
{
    // ── 타일 스폰/제거 ──────────────────────────────────────

    /// <summary>colorRule(x, z) 결과에 따라 내부 셀 전체에 타일 프리팹을 스폰한다.</summary>
    public static List<DKTileInfo> SpawnTiles(
        Func<int, int, DKSwordColor> colorRule,
        GameObject whitePrefab,
        GameObject blackPrefab,
        float yOffset = 0.05f)
    {
        var result = new List<DKTileInfo>();
        if (whitePrefab == null && blackPrefab == null) return result;

        foreach (var cell in DKBossRoomContext.GetInteriorCells())
        {
            DKSwordColor color = colorRule(cell.x, cell.y);
            GameObject prefab = color == DKSwordColor.White ? whitePrefab : blackPrefab;
            if (prefab == null) continue;

            Vector3 worldPos = DKBossRoomContext.CellToWorld(cell.x, cell.y, yOffset);
            // Quad 노말이 +Z → -90° X 회전으로 +Y(위)를 향하게
            GameObject go = BossEffectPool.Spawn(prefab, worldPos, Quaternion.Euler(-90f, 0f, 0f));
            if (go == null) continue;

            result.Add(new DKTileInfo { Cell = cell, Color = color, GO = go });
        }
        return result;
    }

    /// <summary>타일 목록의 모든 GameObject를 파괴하고 목록을 비운다.</summary>
    public static void DestroyTiles(List<DKTileInfo> tiles)
    {
        if (tiles == null) return;
        foreach (var tile in tiles)
            if (tile.GO != null)
                BossEffectPool.Release(tile.GO);
        tiles.Clear();
    }

    // ── VFX 스폰 (피격과 분리) ──────────────────────────────

    /// <summary>
    /// 타일이 사라진 직후 호출: VFX만 스폰하고 피격은 적용하지 않는다.
    /// VFX 재생이 완료되면 별도로 TriggerDamage를 호출한다.
    /// </summary>
    public static void SpawnHitVfx(
        Func<int, int, DKSwordColor> colorRule,
        DKSwordColor swordColor,
        GameObject impactVfxPrefab,
        DKVfxGroupMode vfxMode = DKVfxGroupMode.PerCell)
    {
        if (impactVfxPrefab == null) return;
        SpawnGroupedVfx(impactVfxPrefab, colorRule, swordColor, vfxMode);
    }

    /// <summary>
    /// QuickStrike 전용: 플레이어 열(세로) + 플레이어 행(가로) 각 1개씩 scale=3 stretched VFX.
    /// ChangeSlash/NormalSlash와 동일 방식으로 십자 형태를 만든다.
    /// </summary>
    public static void SpawnCrossVfx(GameObject prefab, Vector2Int playerCell, DKSwordColor swordColor)
    {
        if (prefab == null) return;
        Color tint  = swordColor == DKSwordColor.White ? Color.white : Color.black;
        float colLen = (DKBossRoomContext.Height - 2) * DKBossRoomContext.CellSize;
        float rowLen = (DKBossRoomContext.Width  - 2) * DKBossRoomContext.CellSize;

        // 세로줄: 플레이어 열 x, 방 중앙 z, scale z=3(줄 길이)
        Vector3 colPos = DKBossRoomContext.CellToWorld(playerCell.x, DKBossRoomContext.Height / 2, 0.1f);
        SpawnStretchedVfx(prefab, colPos, Quaternion.identity, 3f, tint);

        // 가로줄: 방 중앙 x, 플레이어 행 z, Y90 회전, scale z=3
        Vector3 rowPos = DKBossRoomContext.CellToWorld(DKBossRoomContext.Width / 2, playerCell.y, 0.1f);
        SpawnStretchedVfx(prefab, rowPos, Quaternion.Euler(0f, 90f, 0f), 3f, tint);
    }

    /// <summary>
    /// NormalSlash 전용: 단일 행(rowZ) 에만 VFX 1개 스폰.
    /// </summary>
    public static void SpawnSingleRowVfx(GameObject prefab, int rowZ, DKSwordColor swordColor)
    {
        if (prefab == null) return;
        Color tint = swordColor == DKSwordColor.White ? Color.white : Color.black;
        Vector3 pos = DKBossRoomContext.CellToWorld(DKBossRoomContext.Width / 2, rowZ, 0.1f);
        SpawnStretchedVfx(prefab, pos, Quaternion.Euler(0f, 90f, 0f), 3f, tint);
    }

    /// <summary>
    /// NormalSlash 전용: 플레이어가 단일 행(rowZ) 에 있으면 피격.
    /// </summary>
    public static void TriggerSingleRowDamage(MonsterContext ctx, int rowZ,
                                              float damageMult, float knockbackMult)
    {
        if (ctx?.Runtime?.PlayerTarget == null) return;
        Vector2Int playerCell = DKBossRoomContext.WorldToCell(ctx.Runtime.PlayerTarget.position);
        if (playerCell.y == rowZ)
            ApplyDamageToPlayer(ctx, damageMult, knockbackMult);
    }

    /// <summary>
    /// VFX 재생 완료 후 호출: 플레이어 피격만 적용한다 (VFX 스폰 없음).
    /// </summary>
    public static void TriggerDamage(
        MonsterContext ctx,
        Func<int, int, DKSwordColor> colorRule,
        DKSwordColor swordColor,
        float damageMult,
        float knockbackMult)
    {
        if (ctx?.Runtime?.PlayerTarget == null) return;

        Vector2Int playerCell = DKBossRoomContext.WorldToCell(ctx.Runtime.PlayerTarget.position);
        foreach (var cell in DKBossRoomContext.GetInteriorCells())
        {
            if (colorRule(cell.x, cell.y) != swordColor) continue;
            if (cell.x == playerCell.x && cell.y == playerCell.y)
            {
                ApplyDamageToPlayer(ctx, damageMult, knockbackMult);
                break;
            }
        }
    }

    /// <summary>
    /// Strike 전용: 링(동심 정사각형) 테두리를 변 당 1개 stretched VFX로 스폰.
    /// 1칸 = scale 0.1 기준 → 변 길이 (2r+1)칸 = scale (2r+1)*0.1
    /// 상/하 = Y90(가로), 좌/우 = identity(세로), 중심은 각 변의 중앙 셀.
    /// </summary>
    public static void SpawnRingPerimeterVfx(GameObject prefab, Vector2Int bossCell,
                                             int ring, DKSwordColor swordColor)
    {
        if (prefab == null) return;
        Color tint  = swordColor == DKSwordColor.White ? Color.white : Color.black;
        int   bx    = bossCell.x, bz = bossCell.y;
        float scale = (2 * ring + 1) * 0.1f; // 변 길이(칸) × 0.1

        if (ring == 0)
        {
            if (DKBossRoomContext.IsInterior(bx, bz))
                SpawnStretchedVfx(prefab,
                    DKBossRoomContext.CellToWorld(bx, bz, 0.1f),
                    Quaternion.identity, 0.1f, tint);
            return;
        }

        // 상단 변 중앙 (x=bx, z=bz+ring): 가로
        if (DKBossRoomContext.IsInterior(bx, bz + ring))
            SpawnStretchedVfx(prefab,
                DKBossRoomContext.CellToWorld(bx, bz + ring, 0.1f),
                Quaternion.Euler(0f, 90f, 0f), scale, tint);

        // 하단 변 중앙 (x=bx, z=bz-ring): 가로
        if (DKBossRoomContext.IsInterior(bx, bz - ring))
            SpawnStretchedVfx(prefab,
                DKBossRoomContext.CellToWorld(bx, bz - ring, 0.1f),
                Quaternion.Euler(0f, 90f, 0f), scale, tint);

        // 좌측 변 중앙 (x=bx-ring, z=bz): 세로
        if (DKBossRoomContext.IsInterior(bx - ring, bz))
            SpawnStretchedVfx(prefab,
                DKBossRoomContext.CellToWorld(bx - ring, bz, 0.1f),
                Quaternion.identity, scale, tint);

        // 우측 변 중앙 (x=bx+ring, z=bz): 세로
        if (DKBossRoomContext.IsInterior(bx + ring, bz))
            SpawnStretchedVfx(prefab,
                DKBossRoomContext.CellToWorld(bx + ring, bz, 0.1f),
                Quaternion.identity, scale, tint);
    }

    /// <summary>
    /// Strike 전용: 플레이어가 지정 링 위에 있으면 피격.
    /// </summary>
    public static void TriggerRingDamage(MonsterContext ctx, Vector2Int bossCell, int ring,
                                         float damageMult, float knockbackMult)
    {
        if (ctx?.Runtime?.PlayerTarget == null) return;
        Vector2Int pc = DKBossRoomContext.WorldToCell(ctx.Runtime.PlayerTarget.position);
        int playerRing = Mathf.Max(
            Mathf.Abs(pc.x - bossCell.x),
            Mathf.Abs(pc.y - bossCell.y));
        if (playerRing == ring)
            ApplyDamageToPlayer(ctx, damageMult, knockbackMult);
    }

    // ── VFX 틴트 ───────────────────────────────────────────

    /// <summary>GameObject 하위의 ParticleSystem 및 Renderer 색상을 일괄 변경한다.</summary>
    public static void TintVfx(GameObject go, Color color)
    {
        if (go == null) return;

        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color);
        }

        foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var mat in rend.materials)
            {
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", color);
                else if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", color);
            }
        }
    }

    // ── 내부 헬퍼 ──────────────────────────────────────────

    private static void SpawnGroupedVfx(
        GameObject prefab,
        Func<int, int, DKSwordColor> colorRule,
        DKSwordColor swordColor,
        DKVfxGroupMode mode)
    {
        Color tint = swordColor == DKSwordColor.White ? Color.white : Color.black;

        if (mode == DKVfxGroupMode.PerCell)
        {
            foreach (var cell in DKBossRoomContext.GetInteriorCells())
            {
                if (colorRule(cell.x, cell.y) != swordColor) continue;
                SpawnVfxAt(prefab, cell.x, cell.y, tint, Quaternion.identity);
            }
            return;
        }

        var matchedCols = new HashSet<int>();
        var matchedRows = new HashSet<int>();

        foreach (var cell in DKBossRoomContext.GetInteriorCells())
        {
            if (colorRule(cell.x, cell.y) != swordColor) continue;
            if (mode == DKVfxGroupMode.PerColumn || mode == DKVfxGroupMode.PerCrossLine)
                matchedCols.Add(cell.x);
            if (mode == DKVfxGroupMode.PerRow || mode == DKVfxGroupMode.PerCrossLine)
                matchedRows.Add(cell.y);
        }

        int centerX = DKBossRoomContext.Width  / 2;
        int centerZ = DKBossRoomContext.Height / 2;

        // 매칭하는 모든 열/행마다 VFX 1개씩, Z scale=3 으로 한 줄 길이에 맞게 늘림
        foreach (int cx in matchedCols)
        {
            Vector3 pos = DKBossRoomContext.CellToWorld(cx, centerZ, 0.1f);
            SpawnStretchedVfx(prefab, pos, Quaternion.identity, 3f, tint);
        }
        foreach (int rz in matchedRows)
        {
            Vector3 pos = DKBossRoomContext.CellToWorld(centerX, rz, 0.1f);
            SpawnStretchedVfx(prefab, pos, Quaternion.Euler(0f, 90f, 0f), 3f, tint);
        }
    }

    /// <summary>
    /// 줄 전체를 덮도록 VFX를 스폰한다.
    /// localScale.z = lineLength 로 Z 축 방향으로 늘린다.
    /// </summary>
    public static void SpawnStretchedVfx(GameObject prefab, Vector3 worldPos, Quaternion rot,
                                         float lineLength, Color tint)
    {
        if (prefab == null) return;
        var go = BossEffectPool.SpawnOneShot(prefab, worldPos, rot, fallbackLifetime: 2.5f);
        if (go == null) return;
        go.transform.localScale = new Vector3(1f, 1f, lineLength);
        TintVfx(go, tint);
    }

    private static void SpawnVfxAt(GameObject prefab, int x, int z, Color tint, Quaternion rot)
    {
        Vector3 pos = DKBossRoomContext.CellToWorld(x, z, 0.1f);
        var go = BossEffectPool.SpawnOneShot(prefab, pos, rot, fallbackLifetime: 2.5f);
        if (go != null) TintVfx(go, tint);
    }

    private static void ApplyDamageToPlayer(MonsterContext ctx, float damageMult, float knockbackMult)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * damageMult));
        player.TakeDamage(dmg);

        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        toPlayer.y = 0.2f;
        Vector3 knockDir = toPlayer.sqrMagnitude > 0.001f ? toPlayer.normalized : ctx.Transform.forward;
        player.ApplyKnockback(knockDir * ctx.Config.stat.knockbackForce * knockbackMult);
    }
}
}
