using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(menuName = "Lee/Grid/Grid Asset SO")]
public class LeeGridAssetSO : ScriptableObject
{
    [Header("Pattern & Visual")]
    public LeeGridPatternSO pattern;
    public LeeGridVisualSO visual;

    [Header("Shapes for this grid (spawn when selected)")]
    public LeeShapeAssetSO[] spawnableShapes;

    [Header("Events")]
    public UnityEvent onAllPlaceableFilled;
}
