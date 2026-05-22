using UnityEngine;

[CreateAssetMenu(menuName = "RelicFairy/Map/Corridor Style", fileName = "CorridorStyle_New")]
public class CorridorStyleSO : ScriptableObject
{
    [Header("Floor")]
    [SerializeField] public GameObject floorTilePrefab;

    [Header("Edge Decorations")]
    [SerializeField] public GameObject[] leftEdgePrefabs;
    [SerializeField] public GameObject[] rightEdgePrefabs;
    [SerializeField] public float edgeObjectSpacing = 4f;

    [Header("Void")]
    [SerializeField] public bool hasVoidBelow;
    [SerializeField] public GameObject voidFogPrefab;
}
