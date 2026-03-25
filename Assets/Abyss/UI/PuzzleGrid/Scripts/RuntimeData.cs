using System;
using UnityEngine;

/// <summary>
/// 런타임에 SO 없이 퍼즐 데이터를 전달하기 위한 순수 C# 데이터 클래스들.
/// BoardManager.SetRuntimeData(data) 또는 EnterGrid(data)에 넘겨 사용한다.
/// </summary>

[Serializable]
public class GridPatternData
{
    public int rows = 8;
    public int columns = 8;
    /// <summary>각 행을 '1'(배치 가능) / '0'(불가)으로 표현. 예: "11001100"</summary>
    public string[] rows01;
}

[Serializable]
public class GridVisualData
{
    public float   squareGap      = 90f;
    public bool    autoCenter     = true;
    public Vector2 startPosition  = Vector2.zero;
    public float   squareScale    = 0.9f;
    public Vector2 centerOffset   = Vector2.zero;
    public Color   placeableColor = Color.white;
    public Color   blockedColor   = Color.gray;
}

[Serializable]
public class ShapeData
{
    /// <summary>Shape의 표시 이름. 여러 Shape가 같은 이름을 가질 수 있다.</summary>
    public string       shapeName;
    /// <summary>각 블록의 그리드 셀 기준 상대 오프셋. (0,0)이 피벗.</summary>
    public Vector2Int[] cellOffsets;
    public float        cellSize = 90f;
}

[Serializable]
public class GridAssetData
{
    /// <summary>
    /// 세션 캐싱에 사용되는 고유 식별자. 반드시 비어 있지 않아야 한다.
    /// 같은 id로 EnterGrid를 재호출하면 캐시된 세션을 재사용한다.
    /// </summary>
    public string id;

    public GridPatternData pattern = new GridPatternData();
    public GridVisualData  visual  = new GridVisualData();
    public ShapeData[]     spawnableShapes;

    /// <summary>모든 배치 가능한 칸이 채워졌을 때 호출된다.</summary>
    public Action onAllPlaceableFilled;
}

[Serializable]
public class BoardConfigData
{
    public bool  startInSelectionMode = true;
    public float gameplayUniformScale = 1f;
}

[Serializable]
public class PlacementRulesData
{
    public float maxAllowedDistMultiplier = 0.5f;
    public bool  snapShapeToFirstSquare   = true;
}
