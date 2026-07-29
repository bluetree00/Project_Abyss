using UnityEngine;

/// <summary>
/// 런타임 데이터 클래스(GridAssetData 등)로부터
/// ScriptableObject.CreateInstance를 통해 SO 인스턴스를 생성한다.
///
/// 생성된 SO는 에셋이 아니므로 씬/세션 종료 시 GC 대상.
/// BoardManager.OnDestroy에서 Destroy를 호출해 명시적으로 해제한다.
/// </summary>
public static class SORuntimeFactory
{
    // ── GridAssetData → SO 세트 생성 ─────────────────────────────

    /// <summary>
    /// GridAssetData 전체로부터 런타임 SO 세트를 생성한다.
    /// 프리팹은 외부에서 주입받는다 (BoardManager의 defaultSquarePrefab 등).
    /// out 파라미터로 하위 SO들을 반환하므로 호출자가 Destroy 책임을 진다.
    /// </summary>
    public static GridAssetSO CreateGridAssetSO(
        GridAssetData data,
        GameObject squarePrefab,
        GameObject shapeBlockPrefab,
        out GridPatternSO patternSO,
        out GridVisualSO visualSO,
        out ShapeAssetSO[] shapeSOs)
    {
        // Pattern SO
        patternSO = ScriptableObject.CreateInstance<GridPatternSO>();
        patternSO.name = $"Runtime_Pattern_{data.id}";
        if (data.pattern != null)
        {
            patternSO.rows    = data.pattern.rows;
            patternSO.columns = data.pattern.columns;
            patternSO.rows01  = data.pattern.rows01 != null
                ? (string[])data.pattern.rows01.Clone()
                : null;
        }

        // Visual SO
        visualSO = ScriptableObject.CreateInstance<GridVisualSO>();
        visualSO.name             = $"Runtime_Visual_{data.id}";
        visualSO.gridSquarePrefab = squarePrefab;
        if (data.visual != null)
        {
            visualSO.squareGap      = data.visual.squareGap;
            visualSO.autoCenter     = data.visual.autoCenter;
            visualSO.startPosition  = data.visual.startPosition;
            visualSO.squareScale    = data.visual.squareScale;
            visualSO.centerOffset   = data.visual.centerOffset;
            visualSO.placeableColor = data.visual.placeableColor;
            visualSO.blockedColor   = data.visual.blockedColor;
        }

        // Shape SOs
        if (data.spawnableShapes != null && data.spawnableShapes.Length > 0)
        {
            shapeSOs = new ShapeAssetSO[data.spawnableShapes.Length];
            for (int i = 0; i < data.spawnableShapes.Length; i++)
            {
                var sd = data.spawnableShapes[i];
                if (sd == null) continue;
                var s = ScriptableObject.CreateInstance<ShapeAssetSO>();
                s.name             = $"Runtime_Shape_{data.id}_{i}";
                s.shapeName        = sd.shapeName;
                s.shapeBlockPrefab = shapeBlockPrefab;
                s.cellOffsets      = sd.cellOffsets != null
                    ? (Vector2Int[])sd.cellOffsets.Clone()
                    : null;
                s.cellSize = sd.cellSize;
                shapeSOs[i] = s;
            }
        }
        else
        {
            shapeSOs = null;
        }

        // Grid Asset SO
        var so = ScriptableObject.CreateInstance<GridAssetSO>();
        so.name            = data.id;
        so.pattern         = patternSO;
        so.visual          = visualSO;
        so.spawnableShapes = shapeSOs;

        // Action → UnityEvent 연결
        if (data.onAllPlaceableFilled != null)
            so.onAllPlaceableFilled.AddListener(() => data.onAllPlaceableFilled.Invoke());

        return so;
    }

    // ── Config / Rules SO 생성 ───────────────────────────────────────

    /// <summary>BoardConfigData로부터 런타임 BoardConfigSO를 생성한다.</summary>
    public static BoardConfigSO CreateBoardConfigSO(BoardConfigData data)
    {
        var so = ScriptableObject.CreateInstance<BoardConfigSO>();
        so.startInSelectionMode = data.startInSelectionMode;
        so.gameplayUniformScale = data.gameplayUniformScale;
        return so;
    }

    /// <summary>PlacementRulesData로부터 런타임 PlacementRulesSO를 생성한다.</summary>
    public static PlacementRulesSO CreatePlacementRulesSO(PlacementRulesData data)
    {
        var so = ScriptableObject.CreateInstance<PlacementRulesSO>();
        so.maxAllowedDistMultiplier = data.maxAllowedDistMultiplier;
        so.snapShapeToFirstSquare   = data.snapShapeToFirstSquare;
        return so;
    }

    // ── SO 일괄 Destroy ──────────────────────────────────────────────

    /// <summary>
    /// CreateGridAssetSO로 생성된 SO 세트를 전부 Destroy한다.
    /// gridSO가 null이면 하위 SO만 Destroy한다.
    /// </summary>
    public static void DestroyRuntimeSOs(
        GridAssetSO gridSO,
        GridPatternSO patternSO,
        GridVisualSO visualSO,
        ShapeAssetSO[] shapeSOs)
    {
        if (gridSO    != null) Object.Destroy(gridSO);
        if (patternSO != null) Object.Destroy(patternSO);
        if (visualSO  != null) Object.Destroy(visualSO);
        if (shapeSOs  != null)
            foreach (var s in shapeSOs)
                if (s != null) Object.Destroy(s);
    }
}
