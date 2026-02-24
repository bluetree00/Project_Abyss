using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class TetrominoUI : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public List<Vector2Int> cellOffsets;
    public BoardManager boardManager;
    public RectTransform rectTransform;
    public Canvas canvas;

    public Vector2Int? placedOrigin;

    private Transform originalParent;
    private Vector3 originalPosition;
    private CanvasGroup canvasGroup;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        originalParent = rectTransform.parent;
        originalPosition = rectTransform.anchoredPosition;

        rectTransform.SetParent(canvas.transform, true);
        canvasGroup.blocksRaycasts = false;

        if (placedOrigin.HasValue)
        {
            boardManager.ClearBlockCells(this);
            placedOrigin = null;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        rectTransform.anchoredPosition += eventData.delta / canvas.scaleFactor;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        canvasGroup.blocksRaycasts = true;

        if (!boardManager.TryPlaceBlockUI(this, eventData))
        {
            rectTransform.SetParent(originalParent, true);
            rectTransform.anchoredPosition = originalPosition;
        }
    }
}