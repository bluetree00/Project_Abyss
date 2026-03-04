using UnityEngine;

[CreateAssetMenu(menuName = "Lee/Board/Board Config SO")]
public class LeeBoardConfigSO : ScriptableObject
{
    [Header("Mode")]
    [Tooltip("If true, start in the selection UI (recommended).")]
    public bool startInSelectionMode = true;

    [Header("Gameplay Scale")]
    [Tooltip("Uniform scale applied to BOTH the active grid and spawned shapes. Keep this at 1 unless you want to zoom the whole gameplay UI.")]
    public float gameplayUniformScale = 1f;

    [Header("Legacy Zoom (not used in new flow)")]
    public Vector3 normalScale = Vector3.one;
    public Vector3 selectedScale = new Vector3(1.25f, 1.25f, 1f);

    [Header("Selection")]
    public bool bringToFrontOnSelect = true;

    [Tooltip("If true, automatically selects the first grid in LeeBoardManager.grids at Start().")]
    public bool autoSelectFirstGrid = false;

    [Tooltip("If false, selecting a grid will NOT spawn a shape automatically.")]
    public bool spawnShapeOnSelect = true;

    [Header("Shape Spawn")]
    [Tooltip("If true, uses anchoredPosition under shapeSpawnParent (UI). If false, uses world position.")]
    public bool useAnchoredSpawn = true;

    public Vector2 shapeSpawnAnchoredPos = Vector2.zero;
    public Vector3 shapeSpawnWorldPos = Vector3.zero;

    [Tooltip("If true: pick random from grid asset's spawnableShapes. If false: always pick index 0.")]
    public bool randomShapeOnSelect = true;

    [Header("Spawn ALL shapes")]
    [Tooltip("If true, spawns ALL shapes assigned to the selected grid (non-overlapping). If false, spawns one (legacy).")]
    public bool spawnAllShapes = true;

    [Tooltip("Spacing between spawned shapes when doing manual layout (pixels in anchored space).")]
    public float shapeSpacing = 60f;
}
