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
            : (entry.randomYRotation ? Random.Range(0f, 360f) : 0f);

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

        // 바닥 스냅 — 렌더러 바운즈 최저점을 배치 지면(pos.y)에 맞춘다.
        // 피벗이 메시 중심인 소품(잔해·제단 등)이 공중에 뜨는 문제를 프리팹 수정 없이 해결.
        // (스케일 적용 뒤 계산해야 정확)
        if (entry.snapToGround)
            SnapBottomToY(go, pos.y);

        AttachNavMeshIgnore(go);
        DissolveEffect.PlayAppearAsync(go, 0.6f, ctx.Ct).Forget();
    }

    /// <summary>
    /// 오브젝트의 렌더러 바운즈 최저점을 지면 y에 정렬한다(월드 공간).
    /// 렌더러가 없으면 아무것도 하지 않는다. 파티클 전용 등은 카탈로그에서 snapToGround를 끄면 된다.
    /// </summary>
    private static void SnapBottomToY(GameObject go, float groundY)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
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
