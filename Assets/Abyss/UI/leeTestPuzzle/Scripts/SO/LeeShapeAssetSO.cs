using UnityEngine;

[CreateAssetMenu(menuName = "Lee/Shape/Shape Asset SO")]
public class LeeShapeAssetSO : ScriptableObject
{
    public GameObject shapeBlockPrefab;

    [Tooltip("Offsets in grid cells. (0,0) is the pivot of the shape.")]
    public Vector2Int[] cellOffsets;

    public float cellSize = 90f;
}
