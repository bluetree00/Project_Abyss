using UnityEngine;
using UnityEngine.UI;

public class CellUI : MonoBehaviour
{
    public Vector2Int gridPos;
    public bool isPlaceable = true;
    public TetrominoUI occupiedBlock;

    [SerializeField] Image image;

    public void Init(Vector2Int pos, bool placeable)
    {
        gridPos = pos;
        isPlaceable = placeable;
        UpdateColor();
    }

    public void UpdateColor()
    {
        if (!image) image = GetComponent<Image>();

        if (!isPlaceable)
            image.color = Color.gray;
        else if (occupiedBlock != null)
            image.color = Color.white;
        else
            image.color = Color.white;
    }
}