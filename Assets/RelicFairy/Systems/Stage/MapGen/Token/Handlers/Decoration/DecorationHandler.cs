using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// d&lt;code&gt; 토큰 처리 — DecorationCatalog에서 프리팹을 찾아 배치한다.
/// 예) "dTR" → code="TR", catalog.Get("TR") 조회 → prefab 인스턴스화.
/// NavMesh 빌드에서 제외(ignoreFromBuild=true)하고 디졸브 등장 연출을 실행한다.
/// </summary>
[TokenHandler("d", TokenCategory.Decoration, "장식 오브젝트 — DecorationCatalogSO에서 코드로 프리팹 조회 후 배치", isPrefix: true,
    csvExample: "dTR / dWL / dTL / dBR ...\n(d 뒤의 코드가 DecorationCatalog 키, NavMesh 제외 자동 적용)")]
public sealed class DecorationHandler : ITokenHandler
{
    public void Execute(TokenContext ctx)
    {
        if (ctx.DecorationCatalogs == null || ctx.DecorationCatalogs.Length == 0) return;

        // "dTR" → code = "TR"
        string code = ctx.RawToken.Length > 1 ? ctx.RawToken.Substring(1) : string.Empty;
        if (string.IsNullOrEmpty(code)) return;

        var catalog = PickCatalog(ctx.Theme, ctx.DecorationCatalogs);
        if (catalog == null)
        {
            Debug.LogWarning($"[DecorationHandler] 카탈로그 없음 (theme='{ctx.Theme}')");
            return;
        }

        var entry = catalog.Get(code);
        if (entry == null || entry.prefab == null)
        {
            Debug.LogWarning($"[DecorationHandler] 코드 '{code}' 미등록 (theme={ctx.Theme}, catalog={catalog.name})");
            return;
        }

        // 벽 방향 감지 (faceNearestWall)
        Vector3 inwardDir = Vector3.zero;
        if (entry.faceNearestWall && ctx.Grid != null)
            inwardDir = FindInwardDirection(ctx.Grid, ctx.Cell);

        float rotY = (entry.faceNearestWall && inwardDir != Vector3.zero)
            ? Mathf.Atan2(inwardDir.x, inwardDir.z) * Mathf.Rad2Deg
            : (entry.randomYRotation
                ? (ctx.Rng != null ? (float)ctx.Rng.NextDouble() * 360f : Random.Range(0f, 360f))
                : 0f);

        // 멀티셀 오브젝트: anchor 셀(좌하단)에서 크기 중심으로 오프셋
        var centerOffset = new Vector3(
            (entry.sizeX - 1) * 0.5f * ctx.CellSize,
            entry.yOffset,
            (entry.sizeZ - 1) * 0.5f * ctx.CellSize);

        // 벽 인셋: 방 안쪽으로 수평 오프셋
        if (entry.faceNearestWall && inwardDir != Vector3.zero)
            centerOffset += inwardDir * entry.wallInset;

        var pos = ctx.WorldPos + centerOffset;

        var go     = Object.Instantiate(entry.prefab, pos, Quaternion.Euler(0f, rotY, 0f), ctx.Parent);
        go.name    = $"Deco_{ctx.Cell.x}_{ctx.Cell.y}_{code}";

        if (entry.scale != 1f)
            go.transform.localScale *= entry.scale;

        // 바닥 스냅 — 메시 바운즈 최저점을 '실제 바닥 블록 윗면'에 맞춘다.
        // ⚠️토큰 기준 Y(baseY + 0.5)는 1m 큐브 바닥을 가정한 역사적 상수인데, 실제 바닥 블록은
        //   Cube × scale.y(0.2)라 진짜 윗면은 baseY + 0.1 — pos.y로 스냅하면 전부 0.4m 떠버린다.
        //   그래서 팔레트에서 실제 윗면을 계산해 목표로 삼는다(ResolveFloorTopY).
        // 벽부착(횃불 등)·명시 오프셋(yOffset≠0)만 제외(의도 배치). 그 외 바닥 소품은 항상 스냅.
        if (!entry.faceNearestWall && Mathf.Approximately(entry.yOffset, 0f))
            SnapBottomToY(go, ResolveFloorTopY(ctx));

        AttachNavMeshIgnore(go);
        DissolveEffect.PlayAppearAsync(go, 0.6f, ctx.Ct).Forget();
    }

    /// <summary>
    /// 팔레트의 Floor 블록에서 '실제 바닥 윗면'의 월드 Y를 계산한다.
    /// 토큰 기준 Y(baseY + TokenParser.TokenLocalYOffset)는 1m 큐브를 가정한 값이라
    /// 실제 바닥(Cube × scale.y 0.2)보다 높다 — 그대로 스냅하면 장식이 공중에 뜬다.
    /// 팔레트/프리팹 정보를 못 얻으면 기존 동작(pos.y)으로 안전 폴백.
    /// </summary>
    private static float ResolveFloorTopY(TokenContext ctx)
    {
        var def = ctx.ActivePalette != null ? ctx.ActivePalette.Pick(TileType.Floor, ctx.Rng) : null;
        if (def == null || def.prefab == null) return ctx.WorldPos.y;

        float meshTop = 0.5f;                                   // 기본 큐브 상단(로컬)
        var mf = def.prefab.GetComponentInChildren<MeshFilter>();
        if (mf != null && mf.sharedMesh != null) meshTop = mf.sharedMesh.bounds.max.y;

        float scaleY     = def.prefab.transform.localScale.y * def.localScale.y;
        float localTop   = ctx.BaseY + def.localOffset.y + meshTop * scaleY;
        float localToken = ctx.BaseY + TokenParser.TokenLocalYOffset;

        // WorldPos.y = localToken을 부모 변환한 값 → 로컬 차이만 부모 스케일로 환산해 보정.
        float parentScaleY = ctx.Parent != null ? ctx.Parent.lossyScale.y : 1f;
        return ctx.WorldPos.y + (localTop - localToken) * parentScaleY;
    }

    /// <summary>
    /// 오브젝트의 렌더러 바운즈 최저점을 지면 y에 정렬한다(월드 공간).
    /// 렌더러가 없으면 아무것도 하지 않는다.
    /// </summary>
    private static void SnapBottomToY(GameObject go, float groundY)
    {
        // MeshRenderer만 — ParticleSystemRenderer 바운즈(원점/거대)가 최저점을 오염시키는 것 방지.
        var rends = go.GetComponentsInChildren<MeshRenderer>();
        if (rends.Length == 0) return;

        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

        float delta = groundY - b.min.y;                 // 바닥 최저점을 groundY로
        go.transform.position += new Vector3(0f, delta, 0f);
    }

    // 인접 4방향 중 Wall이 있는 방향의 반대(방 안쪽)를 반환. Wall 없으면 zero.
    private static Vector3 FindInwardDirection(TileType[,] grid, Vector2Int cell)
    {
        int w = grid.GetLength(0);
        int h = grid.GetLength(1);
        int[] dx = { -1, 1, 0, 0 };
        int[] dz = {  0, 0,-1, 1 };

        for (int d = 0; d < 4; d++)
        {
            int nx = cell.x + dx[d], nz = cell.y + dz[d];
            if (nx < 0 || nx >= w || nz < 0 || nz >= h) continue;
            if (grid[nx, nz] == TileType.Wall)
                return new Vector3(-dx[d], 0f, -dz[d]); // 벽 반대 = 방 안쪽
        }
        return Vector3.zero;
    }

    private static DecorationCatalogSO PickCatalog(string theme, DecorationCatalogSO[] catalogs)
    {
        DecorationCatalogSO fallback = null;
        foreach (var cat in catalogs)
        {
            if (cat == null) continue;
            if (cat.MatchesTheme(theme)) return cat;
            if (fallback == null) fallback = cat;
        }
        return fallback;
    }

    private static void AttachNavMeshIgnore(GameObject root)
    {
        if (root == null) return;
        EnsureNavMeshIgnore(root);

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null)
                EnsureNavMeshIgnore(renderers[i].gameObject);
    }

    private static void EnsureNavMeshIgnore(GameObject go)
    {
        var mod = go.GetComponent<Unity.AI.Navigation.NavMeshModifier>();
        if (mod == null)
            mod = go.AddComponent<Unity.AI.Navigation.NavMeshModifier>();
        mod.ignoreFromBuild = true;
    }
}
