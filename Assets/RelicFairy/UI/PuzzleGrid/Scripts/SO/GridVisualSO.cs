using UnityEngine;

[CreateAssetMenu(menuName = "PuzzleGrid/Grid/Grid Visual SO")]
public class GridVisualSO : ScriptableObject
{
    [Header("Prefabs")]
    public GameObject gridSquarePrefab;

    [Header("Layout")]
    [Tooltip("칸 중심 간 거리(스텝). 배치 블록의 간격 기준이기도 하다.")]
    public float squareGap = 90f;

    [Tooltip("칸 하나의 시각 크기. 0이면 squareGap과 같게 본다(칸 사이 여백 없음).\n" +
             "squareGap보다 작게 두면 그 차이가 칸 사이 여백이 되고, 배치 블록도 같은 크기로 그려져 " +
             "타일 경계선을 덮지 않는다.")]
    public float squareVisualSize = 0f;
    [Tooltip("If true, startPosition is ignored and the grid auto-centers around (0,0) of the Grid root RectTransform.")]
    public bool autoCenter = true;

    [Tooltip("Used only when autoCenter is false. Anchored start position for (row=0,col=0).")]
    public Vector2 startPosition = Vector2.zero;
    public float squareScale = 0.9f;

    [Tooltip("Optional extra offset applied after auto-centering.")]
    public Vector2 centerOffset = Vector2.zero;

    [Header("Square Colors")]
    public Color placeableColor = Color.white;
    public Color blockedColor = Color.gray;
}
