using UnityEngine;

[CreateAssetMenu(menuName = "Lee/Board/Placement Rules SO")]
public class LeePlacementRulesSO : ScriptableObject
{
    [Tooltip("Allowed distance from cell center = cellSize * multiplier. 0.5 matches current logic.")]
    [Range(0.1f, 2f)]
    public float maxAllowedDistMultiplier = 0.5f;

    [Tooltip("If true, snap shape root position to the first occupied square after placement.")]
    public bool snapShapeToFirstSquare = true;
}
