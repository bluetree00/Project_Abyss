using System;
using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
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
    private static readonly int BaseColorId     = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId         = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    // 검정 계열 VFX 틴트 — 순수 black(0,0,0)은 파티클에 곱해져 소멸하므로 어두운 보라로 대체
    public static readonly Color DarkTint = new Color(0.05f, 0f, 0.12f, 1f);

    // 가산 파트가 주된 이펙트용 검정 틴트 — DarkTint는 가산 파트를 지워 영혼 창 빔이 안 보였다(10-03). DarkTint는 다른 패턴이 그대로 쓴다
    public static readonly Color DarkReadableTint = new Color(0.55f, 0.15f, 0.85f, 1f);

    /// <summary>판정 이만큼 전부터 위험 칸(검 색 칸)을 붉게 — 예고 시간 · 판정은 그대로(10-11 사용자 「어떤 공격이 위험한지 더 알려줘야」).</summary>
    public const float ArmLead = 0.25f;
    // 베기 이펙트 최대 수명 — 반복 Sword Slash 15가 2.75초 남아 판정 뒤에도 위험한 줄로 읽혔다. 검 소환은 베기 1.2초 뒤 판정이라 그보다 길게(10-11 이펙트 톤 2차)
    private const float SlashVfxMaxSeconds = 1.6f;
    private static readonly Color ArmColor = new Color(1f, 0.12f, 0.1f, 0.8f);
    private const float ArmEmission = 1.5f;
    private static readonly int   BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int   MainTexId = Shader.PropertyToID("_MainTex");
    // 칠한 칸 → (렌더러, 원래 재질). 검은 칸은 바탕 그림이 어두워 색을 곱해도 붉어지지 않았다(10-11 실측) → 바탕 그림 없는 붉은 재질로 잠깐 바꾼다
    private static readonly Dictionary<GameObject, (Renderer renderer, Material original)> s_armed = new();
    private static readonly Dictionary<Material, Material> s_armMaterials = new();   // 원래 재질마다 한 번 만든다

    private const float ReachSampleRadius   = 0.75f;   // 칸(2 m) 중심에서 — 칸 안쪽 바닥만 잡는다
    private const float ReachFloorTolerance = 0.6f;

    // ── 타일 스폰/제거 ──────────────────────────────────────

    /// <summary>
    /// <paramref name="from"/>에서 걸어서 갈 수 있는 칸인가 — 칸 중심 가까이 같은 바닥 높이에 NavMesh가 있고 길이 끝까지 이어진다
    /// (에이전트 2종을 따로 굽는다 → 어느 쪽이든). NavMesh만 보면 벽 너머 · 끊긴 바닥까지 잡혔다(10-13 실측).
    /// </summary>
    public static bool IsReachable(Vector3 from, Vector3 cellCenter)
    {
        var path = new UnityEngine.AI.NavMeshPath();
        int n = UnityEngine.AI.NavMesh.GetSettingsCount();
        for (int i = 0; i < n; i++)
        {
            var filter = new UnityEngine.AI.NavMeshQueryFilter
            {
                agentTypeID = UnityEngine.AI.NavMesh.GetSettingsByIndex(i).agentTypeID,
                areaMask    = UnityEngine.AI.NavMesh.AllAreas,
            };
            if (!UnityEngine.AI.NavMesh.SamplePosition(cellCenter, out var to, ReachSampleRadius, filter)
                || Mathf.Abs(to.position.y - cellCenter.y) > ReachFloorTolerance) continue;
            if (!UnityEngine.AI.NavMesh.SamplePosition(from, out var start, 2f, filter)) continue;
            if (UnityEngine.AI.NavMesh.CalculatePath(start.position, to.position, filter, path)
                && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete)
                return true;
        }
        return false;
    }

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
            go.transform.localScale = Vector3.one * DKBossRoomContext.CellSize;

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
            {
                Disarm(tile.GO);   // 칠한 색을 지우고 풀로 — 다음에 꺼낼 때 붉게 남지 않게
                BossEffectPool.Release(tile.GO);
            }
        tiles.Clear();
    }

    // ── 판정 직전 위험 칸(10-11) ────────────────────────────

    /// <summary><paramref name="hitAt"/> − <see cref="ArmLead"/>가 지나면 검 색 칸을 붉게 칠한다 — 같은 칸은 다시 칠하지 않아 매 프레임 불러도 된다(새로 깔린 칸만 더 칠한다).</summary>
    public static void ArmBefore(float timer, float hitAt, List<DKTileInfo> tiles, DKSwordColor swordColor)
    {
        if (tiles == null || tiles.Count == 0 || timer < hitAt - ArmLead) return;
        foreach (var t in tiles)
            if (t.Color == swordColor) ArmTile(t.GO);
    }

    /// <summary>이 칸을 붉게(한 번만) — 바탕 그림 없는 붉은 재질로 바꾼다. 풀로 돌려보내기 전에 <see cref="Disarm"/>.</summary>
    public static void ArmTile(GameObject go)
    {
        if (go == null || s_armed.ContainsKey(go)) return;
        var r = go.TryGetComponent<Renderer>(out var own) ? own : go.GetComponentInChildren<Renderer>();
        if (r == null || r.sharedMaterial == null) return;
        Material original = r.sharedMaterial;
        if (!s_armMaterials.TryGetValue(original, out var arm))
        {
            arm = new Material(original) { name = original.name + " (판정 직전)" };
            if (arm.HasProperty(BaseMapId)) arm.SetTexture(BaseMapId, null);
            if (arm.HasProperty(MainTexId)) arm.SetTexture(MainTexId, null);
            if (arm.HasProperty(BaseColorId)) arm.SetColor(BaseColorId, ArmColor);
            if (arm.HasProperty(ColorId))     arm.SetColor(ColorId, ArmColor);
            // 칸 재질은 조명을 받는다(URP Lit) — 기사 아레나의 푸른 조명 아래 붉은 바탕은 검게 보였다(10-11 실측) → 발광으로 조명과 무관하게 붉게
            if (arm.HasProperty(EmissionColorId))
            {
                arm.EnableKeyword("_EMISSION");
                arm.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                arm.SetColor(EmissionColorId, new Color(ArmColor.r, ArmColor.g, ArmColor.b) * ArmEmission);
            }
            s_armMaterials[original] = arm;
        }
        r.sharedMaterial = arm;
        s_armed[go] = (r, original);
    }

    /// <summary>원래 재질로 되돌린다(칠하지 않은 칸은 그대로).</summary>
    public static void Disarm(GameObject go)
    {
        if (go == null || !s_armed.TryGetValue(go, out var e)) return;
        s_armed.Remove(go);
        if (e.renderer != null) e.renderer.sharedMaterial = e.original;
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
        Color tint  = swordColor == DKSwordColor.White ? Color.white : DarkTint;
        float colLen = (DKBossRoomContext.Height - 2) * DKBossRoomContext.CellSize;
        float rowLen = (DKBossRoomContext.Width  - 2) * DKBossRoomContext.CellSize;

        // 세로줄: 플레이어 열 x, 방 중앙 z
        Vector3 colPos = DKBossRoomContext.CellToWorld(playerCell.x, DKBossRoomContext.Height / 2, 0.1f);
        SpawnStretchedVfx(prefab, colPos, Quaternion.identity, ColumnLineLength(), tint);

        // 가로줄: 방 중앙 x, 플레이어 행 z, Y90 회전
        Vector3 rowPos = DKBossRoomContext.CellToWorld(DKBossRoomContext.Width / 2, playerCell.y, 0.1f);
        SpawnStretchedVfx(prefab, rowPos, Quaternion.Euler(0f, 90f, 0f), RowLineLength(), tint);
    }

    /// <summary>
    /// NormalSlash 전용: 단일 행(rowZ) 에만 VFX 1개 스폰.
    /// </summary>
    public static void SpawnSingleRowVfx(GameObject prefab, int rowZ, DKSwordColor swordColor)
    {
        if (prefab == null) return;
        Color tint = swordColor == DKSwordColor.White ? Color.white : DarkTint;
        Vector3 pos = DKBossRoomContext.CellToWorld(DKBossRoomContext.Width / 2, rowZ, 0.1f);
        SpawnStretchedVfx(prefab, pos, Quaternion.Euler(0f, 90f, 0f), RowLineLength(), tint);
    }

    /// <summary>
    /// NormalSlash 전용: 플레이어가 단일 행(rowZ) 에 있으면 피격.
    /// </summary>
    /// <param name="weight">피격 등급 — 2페이즈 수동 영혼 창은 약(포이즈를 쌓지 않음, 10-08 점검). 기본 = 피해량으로 판정.</param>
    public static void TriggerSingleRowDamage(MonsterContext ctx, int rowZ,
                                              float damageMult, float knockbackMult,
                                              HitWeight weight = HitWeight.Auto)
    {
        if (ctx?.Runtime?.PlayerTarget == null) return;
        Vector2Int playerCell = DKBossRoomContext.WorldToCell(ctx.Runtime.PlayerTarget.position);
        if (!DKBossRoomContext.IsInterior(playerCell.x, playerCell.y)) return;   // 테두리 칸엔 줄 타일 · 빔을 안 그린다(10-08 점검)
        if (playerCell.y == rowZ)
            ApplyDamageToPlayer(ctx, damageMult, knockbackMult, weight);
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
        Color tint  = swordColor == DKSwordColor.White ? Color.white : DarkTint;
        int   bx    = bossCell.x, bz = bossCell.y;
        float scale = (2 * ring + 1) * DKBossRoomContext.CellSize * 0.1f; // 변 길이(칸) × CellSize × 0.1

        float cs = DKBossRoomContext.CellSize;

        if (ring == 0)
        {
            if (DKBossRoomContext.IsInterior(bx, bz))
                SpawnStretchedVfx(prefab,
                    DKBossRoomContext.CellToWorld(bx, bz, 0.1f),
                    Quaternion.identity, cs * 0.1f, tint);
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
        if (!DKBossRoomContext.IsInterior(pc.x, pc.y)) return;   // 테두리 칸엔 링 타일을 안 그린다(10-08 점검)
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

    /// <summary>
    /// Effect_09 계열 셰이더(_TintColor HDR)를 사용하는 방패 VFX 색상을 일괄 변경한다.
    /// TintVfx의 _BaseColor/_Color 경로가 무효인 셰이더에 사용.
    /// <paramref name="readableDark"/>면 검정을 읽히는 보라로 — 가산 블렌드의 남색은 거의 안 보인다(흑백 전환 순간처럼 꼭 보여야 할 때, 10-03).
    /// </summary>
    public static void TintShieldVfx(GameObject go, DKSwordColor swordColor, bool readableDark = false)
    {
        if (go == null) return;

        Color particleTint = swordColor == DKSwordColor.White ? Color.white : readableDark ? DarkReadableTint : DarkTint;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(particleTint);
        }

        // _TintColor HDR: White → 밝은 흰색, Black → 어두운 남색(additive blend에서 거의 검정)
        Color hdrTint = swordColor == DKSwordColor.White
            ? new Color(5f, 5.5f, 6f, 1f)
            : readableDark ? new Color(1.6f, 0.4f, 2.6f, 1f) : new Color(0.1f, 0.0f, 0.3f, 1f);
        foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
            foreach (var mat in rend.materials)
                if (mat.HasProperty("_TintColor"))
                    mat.SetColor("_TintColor", hdrTint);
    }

    // ── lineLength 헬퍼 (Floor 기준 동적 계산) ──────────────

    /// <summary>세로줄(열, Z 방향) VFX를 방 높이에 맞게 늘리는 lineLength.</summary>
    public static float ColumnLineLength()
        => (DKBossRoomContext.Height - 2) * DKBossRoomContext.CellSize * 0.1f;

    /// <summary>가로줄(행, X 방향) VFX를 방 너비에 맞게 늘리는 lineLength.</summary>
    public static float RowLineLength()
        => (DKBossRoomContext.Width - 2) * DKBossRoomContext.CellSize * 0.1f;

    // ── 내부 헬퍼 ──────────────────────────────────────────

    private static void SpawnGroupedVfx(
        GameObject prefab,
        Func<int, int, DKSwordColor> colorRule,
        DKSwordColor swordColor,
        DKVfxGroupMode mode)
    {
        Color tint = swordColor == DKSwordColor.White ? Color.white : DarkTint;

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

        // 매칭하는 모든 열/행마다 VFX 1개씩, Floor 크기에 맞게 늘림
        float colLen = ColumnLineLength();
        float rowLen = RowLineLength();
        foreach (int cx in matchedCols)
        {
            Vector3 pos = DKBossRoomContext.CellToWorld(cx, centerZ, 0.1f);
            SpawnStretchedVfx(prefab, pos, Quaternion.identity, colLen, tint);
        }
        foreach (int rz in matchedRows)
        {
            Vector3 pos = DKBossRoomContext.CellToWorld(centerX, rz, 0.1f);
            SpawnStretchedVfx(prefab, pos, Quaternion.Euler(0f, 90f, 0f), rowLen, tint);
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
        var go = SpawnSlashVfx(prefab, worldPos, rot);
        if (go == null) return;
        go.transform.localScale = new Vector3(1f, 1f, lineLength);
        TintVfx(go, tint);
    }

    private static void SpawnVfxAt(GameObject prefab, int x, int z, Color tint, Quaternion rot)
    {
        Vector3 pos = DKBossRoomContext.CellToWorld(x, z, 0.1f);
        var go = SpawnSlashVfx(prefab, pos, rot);
        if (go != null) TintVfx(go, tint);
    }

    private static GameObject SpawnSlashVfx(GameObject prefab, Vector3 pos, Quaternion rot)
    {
        var go = BossEffectPool.Spawn(prefab, pos, rot);
        if (go != null)
        {
            BossPageTransitionState.ToneRoarVfx(go, Color.clear);   // 굴절층 끔 — 줄마다 흰 일그러짐이 겹쳤다(10-13 검증)
            BossEffectPool.ScheduleRelease(go, Mathf.Min(BossEffectPool.CalculateLifetime(prefab, 2.5f), SlashVfxMaxSeconds));
        }
        return go;
    }

    // ── 경계 테두리 엣지 스폰/제거 ────────────────────────────

    /// <summary>
    /// 타일 목록의 색상 경계를 탐지하여 얇은 테두리 엣지를 스폰한다.
    /// 엣지는 타일과 같은 흑/백 계열을 유지하되 밝기 차이를 줘서 장판 위에서도 읽히게 만든다.
    /// 두 색상이 공유하는 내부 경계는 먼저 처리된 타일 기준 엣지 1개만 스폰한다.
    /// </summary>
    public static List<GameObject> SpawnBoundaryEdges(
        List<DKTileInfo> tiles,
        GameObject edgePrefab,
        float thickness = 0.13333334f,
        float yOffset   = 0.12f,
        Color? colorOverride = null)   // 줄 예고처럼 바닥색과 상관없이 밝게 읽혀야 할 때(10-03)
    {
        var result = new List<GameObject>();
        if (edgePrefab == null || tiles == null || tiles.Count == 0) return result;

        float cs = DKBossRoomContext.CellSize;

        var colorMap = new Dictionary<Vector2Int, DKSwordColor>(tiles.Count);
        foreach (var t in tiles) colorMap[t.Cell] = t.Color;

        // 중복 방지: (canonX, canonZ, axis) — axis 0=N/S 경계, 1=E/W 경계
        var processed = new HashSet<(int, int, int)>();

        // (dx, dz, isNS): isNS=true → 이웃이 Z 방향 → 엣지는 X축으로 놓임
        (int dx, int dz, bool isNS)[] dirs =
        {
            ( 0,  1, true ),  // North
            ( 0, -1, true ),  // South
            ( 1,  0, false),  // East
            (-1,  0, false),  // West
        };

        foreach (var tile in tiles)
        {
            int     tx        = tile.Cell.x;
            int     tz        = tile.Cell.y;
            Vector3 tileWorld = DKBossRoomContext.CellToWorld(tx, tz, yOffset);

            foreach (var (dx, dz, isNS) in dirs)
            {
                var nb = new Vector2Int(tx + dx, tz + dz);

                bool neighborSameColor =
                    colorMap.TryGetValue(nb, out var nbColor) && nbColor == tile.Color;
                if (neighborSameColor) continue;

                // 정식 엣지 키: 항상 좌표가 작은 쪽 기준
                int canonX = isNS ? tx                   : Mathf.Min(tx, nb.x);
                int canonZ = isNS ? Mathf.Min(tz, nb.y) : tz;
                int axis   = isNS ? 0 : 1;
                if (!processed.Add((canonX, canonZ, axis))) continue;

                // 엣지 위치: 타일 중심과 이웃 중심의 중간
                Vector3 edgePos = tileWorld
                    + new Vector3(dx * cs * 0.5f, 0f, dz * cs * 0.5f);

                // N/S 경계 → 엣지가 X축 방향 Euler(-90,0,0)
                // E/W 경계 → 엣지가 Z축 방향 Euler(-90,90,0)
                Quaternion rot = isNS
                    ? Quaternion.Euler(-90f,  0f, 0f)
                    : Quaternion.Euler(-90f, 90f, 0f);

                var go = BossEffectPool.Spawn(edgePrefab, edgePos, rot);
                if (go == null) continue;

                go.transform.localScale = new Vector3(cs, thickness, cs);
                ApplyEdgeStyle(go, tile.Color, colorOverride);

                result.Add(go);
            }
        }

        return result;
    }

    /// <summary>경계 엣지 오브젝트 목록을 풀에 반환하고 비운다.</summary>
    public static void DestroyEdges(List<GameObject> edges)
    {
        if (edges == null) return;
        foreach (var go in edges)
            if (go != null) BossEffectPool.Release(go);
        edges.Clear();
    }

    // ───────────────────────────────────────────────────────

    private static void ApplyDamageToPlayer(MonsterContext ctx, float damageMult, float knockbackMult,
                                            HitWeight weight = HitWeight.Auto)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        // 악몽 「지휘」 — 환영 기수가 서 있는 동안 +15%
        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * damageMult * DKStandardBearer.DamageScale(ctx.Monster)));
        // 가해자 = 기사 — null이면 피격 방향 연출·같은 공격자 약 피격 억제·전투 로그가 가해자를 모른다(09-26)
        player.TakeDamage(dmg, ctx.Monster != null ? ctx.Monster.gameObject : null, false, weight);

        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        toPlayer.y = 0.2f;
        Vector3 knockDir = toPlayer.sqrMagnitude > 0.001f ? toPlayer.normalized : ctx.Transform.forward;
        player.ApplyKnockback(knockDir * ctx.Config.stat.knockbackForce * knockbackMult);
    }

    private static void ApplyEdgeStyle(GameObject go, DKSwordColor tileColor, Color? colorOverride = null)
    {
        if (go == null) return;

        Color baseColor = colorOverride ?? (tileColor == DKSwordColor.White
            ? new Color(0.95f, 0.96f, 0.98f, 0.92f)
            : new Color(0.14f, 0.15f, 0.18f, 0.92f));
        Color emission = colorOverride ?? (tileColor == DKSwordColor.White
            ? new Color(0.08f, 0.08f, 0.10f, 1f)
            : new Color(0.02f, 0.02f, 0.03f, 1f));

        var block = new MaterialPropertyBlock();
        foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
        {
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, baseColor);
            block.SetColor(ColorId, baseColor);
            block.SetColor(EmissionColorId, emission);
            renderer.SetPropertyBlock(block);
        }
    }
}
}
