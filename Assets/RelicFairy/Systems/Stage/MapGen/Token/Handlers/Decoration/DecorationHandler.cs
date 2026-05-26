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

        float rotY = entry.randomYRotation ? Random.Range(0f, 360f) : 0f;

        // 멀티셀 오브젝트: anchor 셀(좌하단)에서 크기 중심으로 오프셋
        var centerOffset = new Vector3(
            (entry.sizeX - 1) * 0.5f * ctx.CellSize,
            entry.yOffset,
            (entry.sizeZ - 1) * 0.5f * ctx.CellSize);
        var pos = ctx.WorldPos + centerOffset;

        var go     = Object.Instantiate(entry.prefab, pos, Quaternion.Euler(0f, rotY, 0f), ctx.Parent);
        go.name    = $"Deco_{ctx.Cell.x}_{ctx.Cell.y}_{code}";

        if (entry.scale != 1f)
            go.transform.localScale *= entry.scale;

        AttachNavMeshIgnore(go);
        DissolveEffect.PlayAppearAsync(go, 0.6f, ctx.Ct).Forget();
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
