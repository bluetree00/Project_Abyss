using UnityEngine;

[CreateAssetMenu(menuName = "PuzzleGrid/Shape/Shape Drag SO")]
public class ShapeDragSO : ScriptableObject
{
    public Vector3 selectedScale = new Vector3(1.1f, 1.1f, 1f);
    public Vector2 pointerOffset = new Vector2(0f, 50f);
}
