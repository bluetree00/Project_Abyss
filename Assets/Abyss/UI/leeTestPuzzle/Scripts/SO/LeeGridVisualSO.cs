using UnityEngine;

[CreateAssetMenu(menuName = "Lee/Grid/Grid Visual SO")]
public class LeeGridVisualSO : ScriptableObject
{
    [Header("Prefabs")]
    public GameObject gridSquarePrefab;

    [Header("Layout")]
    public float squareGap = 90f;
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
