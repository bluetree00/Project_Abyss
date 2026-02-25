using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class leeShape : MonoBehaviour,IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Drag Settings")]
    public Vector3 selectedScale = new Vector3(1.1f, 1.1f, 1f);
    public Vector2 pointerOffset = new Vector2(0f, 50f);

    private Canvas canvas;
    private RectTransform rectTransform;
    private Vector3 startPosition;
    private Vector3 startScale;
    private bool isDraggable = true;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();

        startPosition = rectTransform.position;
        startScale = rectTransform.localScale;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!isDraggable) return;

        rectTransform.localScale = selectedScale;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDraggable) return;

        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.transform as RectTransform,
            eventData.position,
            canvas.worldCamera,
            out localPoint
        );

        Vector3 worldPos = canvas.transform.TransformPoint(localPoint);
        worldPos += (Vector3)pointerOffset;
        rectTransform.position = worldPos;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!isDraggable) return;

        rectTransform.localScale = startScale;

        if (!leeGridManager.Instance.TryPlaceShape(this))
        {
            ReturnToStart();
        }
        else
        {
            isDraggable = false;
        }
    }

    public void ReturnToStart()
    {
        rectTransform.position = startPosition;
    }
}