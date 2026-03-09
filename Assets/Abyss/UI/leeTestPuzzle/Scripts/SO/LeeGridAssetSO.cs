using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// SO 변경 종류.
/// Shapes  → Shape 추가/삭제: 기존 배치 상태를 유지한 채 증분 동기화.
/// Layout  → Pattern/Visual 변경: 전체 세션 재빌드.
/// </summary>
public enum LeeGridChangeType { Shapes, Layout }

[CreateAssetMenu(menuName = "Lee/Grid/Grid Asset SO")]
public class LeeGridAssetSO : ScriptableObject
{
    [Header("Pattern & Visual")]
    public LeeGridPatternSO pattern;
    public LeeGridVisualSO visual;

    [Header("Button")]
    [Tooltip("Grid 선택 버튼에 표시할 이미지. null 이면 버튼 이미지를 변경하지 않음.")]
    public Sprite buttonSprite;

    [Header("Shapes for this grid (spawn when selected)")]
    public LeeShapeAssetSO[] spawnableShapes;

    [Header("Events")]
    public UnityEvent onAllPlaceableFilled;

     public event Action<LeeGridChangeType> OnDataChanged;

#if UNITY_EDITOR
    [NonSerialized] private LeeGridPatternSO _prevPattern;
    [NonSerialized] private LeeGridVisualSO _prevVisual;
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
            OnDataChanged?.Invoke(LeeGridChangeType.Shapes);
        else if (patternChanged || visualChanged)
            OnDataChanged?.Invoke(LeeGridChangeType.Layout);

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
    /// 뮤테이터 호출 시 발동. LeeBoardManager가 구독해 자동 갱신한다.
    /// Shapes → 증분 동기화 / Layout → 전체 재빌드.
    /// </summary>

    // ── 뮤테이터 (변경 후 OnDataChanged 자동 발동) ─────────────────

    public void SetPattern(LeeGridPatternSO newPattern)
    {
        pattern = newPattern;
        OnDataChanged?.Invoke(LeeGridChangeType.Layout);
    }

    public void SetVisual(LeeGridVisualSO newVisual)
    {
        visual = newVisual;
        OnDataChanged?.Invoke(LeeGridChangeType.Layout);
    }

    public void AddShape(LeeShapeAssetSO shape)
    {
        if (shape == null) return;
        var list = spawnableShapes != null
            ? new List<LeeShapeAssetSO>(spawnableShapes)
            : new List<LeeShapeAssetSO>();
        list.Add(shape);
        spawnableShapes = list.ToArray();
        OnDataChanged?.Invoke(LeeGridChangeType.Shapes);
    }

    public void RemoveShape(LeeShapeAssetSO shape)
    {
        if (shape == null || spawnableShapes == null) return;
        var list = new List<LeeShapeAssetSO>(spawnableShapes);
        if (!list.Remove(shape)) return;
        spawnableShapes = list.ToArray();
        OnDataChanged?.Invoke(LeeGridChangeType.Shapes);
    }

    public void SetShapes(LeeShapeAssetSO[] shapes)
    {
        spawnableShapes = shapes;
        OnDataChanged?.Invoke(LeeGridChangeType.Shapes);
    }

    /// <summary>필드를 직접 수정한 뒤 수동으로 알릴 때 사용.</summary>
    public void NotifyChanged(LeeGridChangeType changeType = LeeGridChangeType.Layout)
        => OnDataChanged?.Invoke(changeType);
}
