using UnityEngine;

[CreateAssetMenu(menuName = "Lee/Shape/Shape Asset SO")]
public class LeeShapeAssetSO : ScriptableObject
{
    [Tooltip("Shape의 표시 이름. 여러 Shape가 같은 이름을 가질 수 있다.")]
    public string shapeName;

    public GameObject shapeBlockPrefab;

    [Tooltip("Offsets in grid cells. (0,0) is the pivot of the shape.")]
    public Vector2Int[] cellOffsets;

    public float cellSize = 90f;
}
