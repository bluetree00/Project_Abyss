using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// SO 변경 종류.
/// Shapes  → Shape 추가/삭제: 기존 배치 상태를 유지한 채 증분 동기화.
/// Layout  → Pattern/Visual 변경: 전체 세션 재빌드.
/// </summary>
public enum GridChangeType { Shapes, Layout }

[CreateAssetMenu(menuName = "PuzzleGrid/Grid/Grid Asset SO")]
public class GridAssetSO : ScriptableObject
{
    [Header("Pattern & Visual")]
    public GridPatternSO pattern;
    public GridVisualSO visual;

    [Header("Button")]
    [Tooltip("Grid 선택 버튼에 표시할 이미지. null 이면 버튼 이미지를 변경하지 않음.")]
    public Sprite buttonSprite;

    [Header("Shapes for this grid (spawn when selected)")]
    public ShapeAssetSO[] spawnableShapes;

    [Header("Events")]
    public UnityEvent onAllPlaceableFilled;

     public event Action<GridChangeType> OnDataChanged;

#if UNITY_EDITOR
    [NonSerialized] private GridPatternSO _prevPattern;
    [NonSerialized] private GridVisualSO _prevVisual;
    [NonSerialized] private int _prevShapesHash;

    private void OnEnable()
    {
        CachePrev();
    }

    private void OnValidate()
    {
        // 에디터에서 Inspector 수정 반영용
        if (!Application.isPlaying) { CachePrev(); return; }

        bool patternChanged = _prevPattern != pattern;
        bool visualChanged  = _prevVisual  != visual;

        int shapesHash = ComputeShapesHash();
        bool shapesChanged = _prevShapesHash != shapesHash;

        if (shapesChanged)
            OnDataChanged?.Invoke(GridChangeType.Shapes);
        else if (patternChanged || visualChanged)
            OnDataChanged?.Invoke(GridChangeType.Layout);

        CachePrev();
    }

    private void CachePrev()
    {
        _prevPattern = pattern;
        _prevVisual  = visual;
        _prevShapesHash = ComputeShapesHash();
    }

    private int ComputeShapesHash()
    {
        unchecked
        {
            int h = 17;
            if (spawnableShapes != null)
            {
                for (int i = 0; i < spawnableShapes.Length; i++)
                    h = h * 31 + (spawnableShapes[i] ? spawnableShapes[i].GetInstanceID() : 0);
            }
            return h;
        }
    }
#endif

    // ── 런타임 데이터 변경 이벤트 ──────────────────────────────────
    /// <summary>
    /// 뮤테이터 호출 시 발동. BoardManager가 구독해 자동 갱신한다.
    /// Shapes → 증분 동기화 / Layout → 전체 재빌드.
    /// </summary>

    // ── 뮤테이터 (변경 후 OnDataChanged 자동 발동) ─────────────────

    public void SetPattern(GridPatternSO newPattern)
    {
        pattern = newPattern;
        OnDataChanged?.Invoke(GridChangeType.Layout);
    }

    public void SetVisual(GridVisualSO newVisual)
    {
        visual = newVisual;
        OnDataChanged?.Invoke(GridChangeType.Layout);
    }

    public void AddShape(ShapeAssetSO shape)
    {
        if (shape == null) return;
        var list = spawnableShapes != null
            ? new List<ShapeAssetSO>(spawnableShapes)
            : new List<ShapeAssetSO>();
        list.Add(shape);
        spawnableShapes = list.ToArray();
        OnDataChanged?.Invoke(GridChangeType.Shapes);
    }

    public void RemoveShape(ShapeAssetSO shape)
    {
        if (shape == null || spawnableShapes == null) return;
        var list = new List<ShapeAssetSO>(spawnableShapes);
        if (!list.Remove(shape)) return;
        spawnableShapes = list.ToArray();
        OnDataChanged?.Invoke(GridChangeType.Shapes);
    }

    public void SetShapes(ShapeAssetSO[] shapes)
    {
        spawnableShapes = shapes;
        OnDataChanged?.Invoke(GridChangeType.Shapes);
    }

    /// <summary>필드를 직접 수정한 뒤 수동으로 알릴 때 사용.</summary>
    public void NotifyChanged(GridChangeType changeType = GridChangeType.Layout)
        => OnDataChanged?.Invoke(changeType);
}
