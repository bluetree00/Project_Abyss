using UnityEngine;

[CreateAssetMenu(menuName = "Lee/Shape/Shape Drag SO")]
public class LeeShapeDragSO : ScriptableObject
{
    public Vector3 selectedScale = new Vector3(1.1f, 1.1f, 1f);
    public Vector2 pointerOffset = new Vector2(0f, 50f);
}
