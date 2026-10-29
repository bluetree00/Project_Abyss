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

    // 가산 파트가 주된 이펙트용 검정 틴트 — DarkTint는 가산 파트를 지워 영혼 창 빔이 안 보였다(10-03). 베기 이펙트도 10-24부터 이것(검은 검 베기가 거의 안 보였다)
    public static readonly Color DarkReadableTint = new Color(0.55f, 0.15f, 0.85f, 1f);

    /// <summary>판정 이만큼 전부터 맞는 칸(검 색 칸)을 신호 빨강으로 — 예고 시간 · 판정은 그대로(10-11 사용자 「어떤 공격이 위험한지 더 알려줘야」).</summary>
    public const float ArmLead = 0.25f;
    // 베기 이펙트 최대 수명 — 반복 Sword Slash 15가 2.75초 남아 판정 뒤에도 위험한 줄로 읽혔다. 검 소환은 베기 1.2초 뒤 판정이라 그보다 길게(10-11 이펙트 톤 2차)
    private const float SlashVfxMaxSeconds = 1.6f;
    // Sword Slash 15의 충격(베임 · 바닥 갈라짐 · 섬광 버스트)은 생성 0.51초 뒤에 나온다 — 판정 순간에 만들면 갈라짐이 피해보다 0.5초 늦고,
    // 순차 베기(피라미드 · 일반 베기)는 실제 맞는 줄보다 2~3줄 뒤처져 보였다(10-24 사용자 「공격 범위랑 가이드라인이랑 다른 것도」).
    private const float SlashImpactAt   = 0.51f;
    private const float SlashImpactLead = 0.05f;   // 건너뛴 뒤 충격까지 — 판정 프레임과 거의 같이
    // ── 칸 예고 색(10-23 가이드 색 통일 — 「실제로 맞는 칸 = 빨강」 한 가지 규칙) ──────────
    // 맞는 칸: 깔리는 순간 공용 공격 색(PatternGuideHelper.AttackFill)으로 차오름 → 판정 ArmLead초 전 신호 빨강(Active) · 더 밝게.
    // 안 맞는 칸: 흑/백 그림 대신 옅은 무채색 하나(검 색은 검 · 갑옷 · 베기 이펙트가 알린다). 어두운 바닥 · 유리 결계 너머에서도 읽히게 발광을 준다.
    private static Color ChargeColor => new Color(PatternGuideHelper.AttackFill.r, PatternGuideHelper.AttackFill.g, PatternGuideHelper.AttackFill.b, 0.8f);   // 0.72 → 0.8(10-24 「너무 안 보여」)
    private static Color ArmColor    => new Color(PatternGuideHelper.Active.r, PatternGuideHelper.Active.g, PatternGuideHelper.Active.b, 0.9f);
    private static readonly Color SafeColor = new Color(0.80f, 0.82f, 0.86f, 0.32f);   // 옅은 무채색 — 안전 흰색보다 어둡고 옅게
    private const float ChargeEmission = 1.6f;    // 1.3 → 1.6(10-24 사용자 「챕터3 보스 패턴이 너무 안 보여」)
    private const float ArmEmission    = 2.8f;    // 1.5 → 2.2 — 유리 결계 너머에서도 신호가 튀게(10-23) → 2.8(10-24)
    private const float SafeEmission   = 0.12f;
    private const float EdgeGlow       = 1.5f;    // 맞는 칸 둘레 테두리 — 칸보다 이만큼 더 밝게(또렷한 가장자리 · 10-24 1.35 → 1.5)
    private static readonly Color EdgeNeutral         = new Color(0.86f, 0.88f, 0.92f, 0.9f);   // 테두리 기본 — 밝은 무채색(흑/백 어느 칸이든 같은 선)
    private static readonly Color EdgeNeutralEmission = new Color(0.30f, 0.31f, 0.33f, 1f);
    private static readonly int   BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int   MainTexId = Shader.PropertyToID("_MainTex");

    private enum TileLook { Safe, Charge, Signal }   // 순서 = 세기(테두리는 양옆 칸 중 센 쪽을 따른다 · 칸은 약해지지 않는다)

    // 칠한 칸 → (렌더러, 원래 재질, 지금 모습). 검은 칸은 바탕 그림이 어두워 색을 곱해도 붉어지지 않았다(10-11 실측) → 바탕 그림 없는 단색 재질로 잠깐 바꾼다
    private static readonly Dictionary<GameObject, (Renderer renderer, Material original, TileLook look)> s_painted = new();
    private static readonly Dictionary<(Material, TileLook), Material> s_lookMaterials = new();   // 원래 재질 · 모습마다 한 번 만든다
    // 경계 테두리 → 양옆 칸(바깥 경계면 한쪽만) · 지금 모습 — 맞는 칸 둘레를 공격 색으로 다시 칠할 때 읽는다(10-23)
    private static readonly Dictionary<GameObject, (GameObject a, GameObject b)> s_edgeSides = new();
    private static readonly Dictionary<GameObject, TileLook> s_edgeLook = new();
    private static readonly List<GameObject> s_staleEdges = new();   // 씬과 함께 사라진 테두리 — 다시 칠할 때 걷는다
    // 테두리 재질에 발광을 켠 사본(원래 재질마다 한 번) — 원래 테두리 재질은 발광이 꺼져 블록 발광 색이 먹지 않았다(10-23)
    private static readonly Dictionary<Material, Material> s_edgeMaterials = new();
    private static readonly HashSet<Material> s_edgeCopies = new();

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

    // ── 칸 예고(10-11 · 10-23 가이드 색 통일) ────────────────────

    /// <summary>
    /// 칸 예고 한 프레임 — 검 색 칸(= 실제로 맞는 칸)은 공용 공격 색으로, <paramref name="hitAt"/> − <see cref="ArmLead"/>부터 신호 빨강으로,
    /// 나머지 칸은 옅은 무채색으로 칠한다(10-23 — 예전엔 판정 0.25초 전까지 흑/백만 보여 어느 칸이 맞는지 규칙으로 읽어야 했다).
    /// 같은 모습은 다시 칠하지 않아 매 프레임 불러도 된다(새로 깔린 칸만 더 칠한다). 맞는 칸 둘레 테두리도 같은 색으로 또렷이.
    /// </summary>
    public static void ArmBefore(float timer, float hitAt, List<DKTileInfo> tiles, DKSwordColor swordColor)
    {
        if (tiles == null || tiles.Count == 0) return;
        TileLook hit = timer >= hitAt - ArmLead ? TileLook.Signal : TileLook.Charge;
        bool changed = false;
        foreach (var t in tiles)
            changed |= Paint(t.GO, t.Color == swordColor ? hit : TileLook.Safe);
        if (changed) RestyleEdges();
    }

    /// <summary>
    /// 안전 원(방패 · 부서진 기둥) 안 칸을 「안 맞는 칸」으로 — 판정은 원 안을 면제하는데 칸이 붉게 남아 예고와 판정이 달랐다(10-24 사용자 「공격 범위랑 가이드라인이 다르다」).
    /// 칸 색을 검 색의 반대로 바꿔 <see cref="ArmBefore"/>가 다시 붉게 칠하지 않게 하고, 칠한 칸은 원래 재질로 되돌린 뒤 안 맞는 칸 모습으로(칠하기는 약해지지 않으므로).
    /// </summary>
    public static void MarkSafeWithin(List<DKTileInfo> tiles, Vector3 center, float radius, DKSwordColor swordColor)
    {
        if (tiles == null) return;
        float r2 = radius * radius;
        DKSwordColor other = swordColor == DKSwordColor.White ? DKSwordColor.Black : DKSwordColor.White;
        bool changed = false;
        for (int i = 0; i < tiles.Count; i++)
        {
            var t = tiles[i];
            if (t.GO == null || t.Color != swordColor) continue;
            Vector3 d = t.GO.transform.position - center;
            if (d.x * d.x + d.z * d.z > r2) continue;
            t.Color  = other;
            tiles[i] = t;
            Disarm(t.GO);
            changed |= Paint(t.GO, TileLook.Safe);
        }
        if (changed) RestyleEdges();
    }

    /// <summary>이 칸을 신호 빨강으로 — 바탕 그림 없는 단색 재질로 바꾼다. 풀로 돌려보내기 전에 <see cref="Disarm"/>.</summary>
    public static void ArmTile(GameObject go) => Paint(go, TileLook.Signal);

    /// <summary>맞을 칸을 깐 순간 — 공용 공격 색으로(10-23 가이드 색 통일). 판정 직전엔 <see cref="ArmTile"/>. 풀로 돌려보내기 전에 <see cref="Disarm"/>.</summary>
    public static void ChargeTile(GameObject go) => Paint(go, TileLook.Charge);

    /// <summary>원래 재질로 되돌린다(칠하지 않은 칸은 그대로).</summary>
    public static void Disarm(GameObject go)
    {
        if (go == null || !s_painted.TryGetValue(go, out var e)) return;
        s_painted.Remove(go);
        if (e.renderer != null) e.renderer.sharedMaterial = e.original;
    }

    /// <summary>칸 모습을 바꾼다 — 바뀌었으면 true. 약해지지는 않는다(신호 빨강이 다시 흐려지지 않게).</summary>
    private static bool Paint(GameObject go, TileLook look)
    {
        if (go == null) return false;
        Renderer r;
        Material original;
        if (s_painted.TryGetValue(go, out var e))
        {
            if (e.look >= look || e.renderer == null) return false;
            r        = e.renderer;
            original = e.original;
        }
        else
        {
            r = go.TryGetComponent<Renderer>(out var own) ? own : go.GetComponentInChildren<Renderer>();
            if (r == null || r.sharedMaterial == null) return false;
            original = r.sharedMaterial;
        }
        r.sharedMaterial = LookMaterial(original, look);
        s_painted[go]    = (r, original, look);
        return true;
    }

    /// <summary>
    /// 원래 재질 · 모습마다 한 번 만든 단색 재질. 칸 재질은 조명을 받는다(URP Lit) — 기사 아레나의 푸른 조명 아래 붉은 바탕은 검게 보였다(10-11 실측)
    /// → 발광으로 조명과 무관하게.
    /// </summary>
    private static Material LookMaterial(Material original, TileLook look)
    {
        if (s_lookMaterials.TryGetValue((original, look), out var m) && m != null) return m;
        Color c    = look == TileLook.Signal ? ArmColor    : look == TileLook.Charge ? ChargeColor    : SafeColor;
        float glow = look == TileLook.Signal ? ArmEmission : look == TileLook.Charge ? ChargeEmission : SafeEmission;
        m = new Material(original) { name = original.name + (look == TileLook.Signal ? " (판정 직전)" : look == TileLook.Charge ? " (맞는 칸)" : " (안 맞는 칸)") };
        if (m.HasProperty(BaseMapId)) m.SetTexture(BaseMapId, null);
        if (m.HasProperty(MainTexId)) m.SetTexture(MainTexId, null);
        if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
        if (m.HasProperty(ColorId))     m.SetColor(ColorId, c);
        if (m.HasProperty(EmissionColorId))
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetColor(EmissionColorId, new Color(c.r, c.g, c.b) * glow);
        }
        s_lookMaterials[(original, look)] = m;
        return m;
    }

    /// <summary>테두리를 양옆 칸 중 센 모습으로 — 맞는 칸 둘레는 차오름 · 신호 색으로 칸보다 밝게(10-23). 칸 모습이 바뀐 프레임에만 부른다.</summary>
    private static void RestyleEdges()
    {
        foreach (var kv in s_edgeSides)
        {
            if (kv.Key == null) { s_staleEdges.Add(kv.Key); continue; }   // 씬과 함께 사라진 테두리 — 아래에서 걷는다
            TileLook look  = LookOf(kv.Value.a);
            TileLook other = LookOf(kv.Value.b);
            if (other > look) look = other;
            if (look == TileLook.Safe) continue;   // 안 맞는 칸 사이 · 바깥 테두리는 밝은 무채색 그대로
            if (s_edgeLook.TryGetValue(kv.Key, out var cur) && cur >= look) continue;
            s_edgeLook[kv.Key] = look;
            Color c    = look == TileLook.Signal ? ArmColor : ChargeColor;
            float glow = (look == TileLook.Signal ? ArmEmission : ChargeEmission) * EdgeGlow;
            SetEdgeColor(kv.Key, new Color(c.r, c.g, c.b, 0.95f), new Color(c.r, c.g, c.b) * glow);
        }
        if (s_staleEdges.Count == 0) return;
        foreach (var go in s_staleEdges) { s_edgeSides.Remove(go); s_edgeLook.Remove(go); }
        s_staleEdges.Clear();
    }

    private static TileLook LookOf(GameObject tile)
        => tile != null && s_painted.TryGetValue(tile, out var e) ? e.look : TileLook.Safe;

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
        Color tint  = swordColor == DKSwordColor.White ? Color.white : DarkReadableTint;
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
        Color tint = swordColor == DKSwordColor.White ? Color.white : DarkReadableTint;
        Vector3 pos = DKBossRoomContext.CellToWorld(DKBossRoomContext.Width / 2, rowZ, 0.1f);
        SpawnStretchedVfx(prefab, pos, Quaternion.Euler(0f, 90f, 0f), RowLineLength(), tint, atImpact: true);   // 줄마다 판정 순간에 만든다
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
        Color tint  = swordColor == DKSwordColor.White ? Color.white : DarkReadableTint;
        int   bx    = bossCell.x, bz = bossCell.y;
        float scale = (2 * ring + 1) * DKBossRoomContext.CellSize * 0.1f; // 변 길이(칸) × CellSize × 0.1

        float cs = DKBossRoomContext.CellSize;

        if (ring == 0)
        {
            if (DKBossRoomContext.IsInterior(bx, bz))
                SpawnStretchedVfx(prefab,
                    DKBossRoomContext.CellToWorld(bx, bz, 0.1f),
                    Quaternion.identity, cs * 0.1f, tint, atImpact: true);
            return;
        }

        // 상단 변 중앙 (x=bx, z=bz+ring): 가로
        if (DKBossRoomContext.IsInterior(bx, bz + ring))
            SpawnStretchedVfx(prefab,
                DKBossRoomContext.CellToWorld(bx, bz + ring, 0.1f),
                Quaternion.Euler(0f, 90f, 0f), scale, tint, atImpact: true);

        // 하단 변 중앙 (x=bx, z=bz-ring): 가로
        if (DKBossRoomContext.IsInterior(bx, bz - ring))
            SpawnStretchedVfx(prefab,
                DKBossRoomContext.CellToWorld(bx, bz - ring, 0.1f),
                Quaternion.Euler(0f, 90f, 0f), scale, tint, atImpact: true);

        // 좌측 변 중앙 (x=bx-ring, z=bz): 세로
        if (DKBossRoomContext.IsInterior(bx - ring, bz))
            SpawnStretchedVfx(prefab,
                DKBossRoomContext.CellToWorld(bx - ring, bz, 0.1f),
                Quaternion.identity, scale, tint, atImpact: true);

        // 우측 변 중앙 (x=bx+ring, z=bz): 세로
        if (DKBossRoomContext.IsInterior(bx + ring, bz))
            SpawnStretchedVfx(prefab,
                DKBossRoomContext.CellToWorld(bx + ring, bz, 0.1f),
                Quaternion.identity, scale, tint, atImpact: true);
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
        Color tint = swordColor == DKSwordColor.White ? Color.white : DarkReadableTint;

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
    /// <param name="atImpact">판정 순간에 만든다 — 충격 직전까지 앞부분을 건너뛴다(<see cref="SlashImpactAt"/>).</param>
    public static void SpawnStretchedVfx(GameObject prefab, Vector3 worldPos, Quaternion rot,
                                         float lineLength, Color tint, bool atImpact = false)
    {
        if (prefab == null) return;
        var go = SpawnSlashVfx(prefab, worldPos, rot);
        if (go == null) return;
        go.transform.localScale = new Vector3(1f, 1f, lineLength);
        TintVfx(go, tint);
        if (atImpact) SkipToImpact(go);   // 물들인 뒤 — 건너뛰는 동안 나온 입자도 같은 색
    }

    /// <summary>입자계를 충격 직전까지 돌려 둔다(뿌리 입자계만 — 자식은 함께 돈다).</summary>
    private static void SkipToImpact(GameObject go)
    {
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
        {
            var parent = ps.transform.parent;
            if (ps.transform != go.transform && parent != null && parent.GetComponentInParent<ParticleSystem>() != null) continue;
            ps.Simulate(SlashImpactAt - SlashImpactLead, true, false, true);
            ps.Play(true);
        }
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
    /// 엣지는 밝은 무채색 하나 — 맞는 칸 둘레는 <see cref="ArmBefore"/>가 공격 색으로 다시 칠한다(10-23 가이드 색 통일).
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
        var goMap    = new Dictionary<Vector2Int, GameObject>(tiles.Count);   // 테두리 양옆 칸(10-23)
        foreach (var t in tiles) { colorMap[t.Cell] = t.Color; goMap[t.Cell] = t.GO; }

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
                ApplyEdgeStyle(go, colorOverride);
                s_edgeSides[go] = (tile.GO, goMap.TryGetValue(nb, out var nbGo) ? nbGo : null);

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
        {
            if (go == null) continue;
            s_edgeSides.Remove(go);
            s_edgeLook.Remove(go);
            BossEffectPool.Release(go);
        }
        edges.Clear();
    }

    // ───────────────────────────────────────────────────────

    private static void ApplyDamageToPlayer(MonsterContext ctx, float damageMult, float knockbackMult,
                                            HitWeight weight = HitWeight.Auto)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        // 악몽 「지휘」 — 환영 기수가 서 있는 동안 +15% · 사슬 둘째 칸 ×0.8(10-22 E5 페이지2)
        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * damageMult * DKStandardBearer.DamageScale(ctx.Monster)
                                     * DeathKnightBossMonster.ChainScale(ctx.Monster)));
        // 가해자 = 기사 — null이면 피격 방향 연출·같은 공격자 약 피격 억제·전투 로그가 가해자를 모른다(09-26)
        player.TakeDamage(dmg, ctx.Monster != null ? ctx.Monster.gameObject : null, false, weight);

        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        toPlayer.y = 0.2f;
        Vector3 knockDir = toPlayer.sqrMagnitude > 0.001f ? toPlayer.normalized : ctx.Transform.forward;
        player.ApplyKnockback(knockDir * ctx.Config.stat.knockbackForce * knockbackMult);
    }

    /// <summary>
    /// 테두리 기본 모습 — (10-23 가이드 색 통일) 흑/백 칸마다 흰 · 어두운 회색이던 것(검은 칸 테두리는 어두운 바닥에서 안 보였다)을
    /// 밝은 무채색 하나로. 맞는 칸 둘레는 <see cref="RestyleEdges"/>가 공격 색으로 다시 칠한다.
    /// </summary>
    private static void ApplyEdgeStyle(GameObject go, Color? colorOverride = null)
    {
        if (go == null) return;
        SetEdgeColor(go, colorOverride ?? EdgeNeutral, colorOverride ?? EdgeNeutralEmission);
    }

    /// <summary>테두리 색 · 발광 — 발광을 켠 재질 사본으로 바꿔 끼운 뒤 블록으로 칠한다(원래 재질은 발광이 꺼져 있었다).</summary>
    private static void SetEdgeColor(GameObject go, Color baseColor, Color emission)
    {
        var block = new MaterialPropertyBlock();
        foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
        {
            Material src = renderer.sharedMaterial;
            if (src != null && !s_edgeCopies.Contains(src))
            {
                if (!s_edgeMaterials.TryGetValue(src, out var lit) || lit == null)
                {
                    lit = new Material(src) { name = src.name + " (발광)" };
                    lit.EnableKeyword("_EMISSION");
                    lit.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    s_edgeMaterials[src] = lit;
                    s_edgeCopies.Add(lit);
                }
                renderer.sharedMaterial = lit;
            }
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, baseColor);
            block.SetColor(ColorId, baseColor);
            block.SetColor(EmissionColorId, emission);
            renderer.SetPropertyBlock(block);
        }
    }
}
}
