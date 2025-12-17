using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class GridSquare : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public Image hooverImage;
    public Image activeImage;
    public Image normalImage;
    public List<Sprite> normallmages;

    public bool Selected { get; set; }
    public int SquareIndex { get; set; }
    public bool SquareOccupied { get; set; }

    private int shapeID = -1; // 배치된 블록의 ID (없으면 -1)
    private Grid gridReference; // 🔹 Grid 참조 추가

    // 드래그를 위한 변수 추가
    private Shape restoredShape = null;
    private bool isDragging = false;

    // Start is called before the first frame update
    void Start()
    {
        Selected = false;
        SquareOccupied = false;
    }

    public int GetShapeID()
    {
        return shapeID; // shapeID 값을 반환
    }

    public bool CanWeUseThisSquare()
    {
        return hooverImage.gameObject.activeSelf;
    }

    public void PlaceShapeOnBoard(int id)
    {
        ActivateSquare();
        AssignShapeID(id); // 배치된 블록의 ID 저장
    }

    public void ActivateSquare()
    {
        hooverImage.gameObject.SetActive(false);
        activeImage.gameObject.SetActive(true);
        Selected = true;
        SquareOccupied = true;
    }

    public void AssignShapeID(int id)
    {
        shapeID = id;
    }

    public void ClearSquare()
    {
        shapeID = -1;
        SquareOccupied = false;
        activeImage.gameObject.SetActive(false);
        hooverImage.gameObject.SetActive(false);
    }

    public void SetImage(bool setFirstImage)
    {
        normalImage.GetComponent<Image>().sprite = setFirstImage ? normallmages[1] : normallmages[0];
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (SquareOccupied == false)
        {
            Selected = true;
            hooverImage.gameObject.SetActive(true);
        }
        else if (other.GetComponent<ShapeSquare>() != null)
        {
            other.GetComponent<ShapeSquare>().SetOccupied();
        }
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        Selected = true;
        if (SquareOccupied == false)
        {
            hooverImage.gameObject.SetActive(true);
        }
        else if (other.GetComponent<ShapeSquare>() != null)
        {
            other.GetComponent<ShapeSquare>().SetOccupied();
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (SquareOccupied == false)
        {
            Selected = false;
            hooverImage.gameObject.SetActive(false);
        }
        else if (other.GetComponent<ShapeSquare>() != null)
        {
            other.GetComponent<ShapeSquare>().UnSetOccupied();
        }
    }

    // 마우스 버튼을 누르는 순간 실행 (클릭과 동시에 삭제/복구 처리)
    public void OnPointerDown(PointerEventData eventData)
    {
        Debug.Log($"GridSquare: Pointer Down on shapeID {shapeID}");
        if (shapeID != -1)
        {
            // 기존 블록 복구와 삭제 이벤트 호출
            GameEvents.InvokeShapeRestoration(shapeID);
            GameEvents.InvokeBlockRemoval(shapeID);

            // ShapeStorage.LastRestoredShape는 복구된 블록을 참조하도록 수정되어야 함
            restoredShape = ShapeStorage.LastRestoredShape;
            if (restoredShape != null)
            {
                // 복구된 블록에 마우스 누름과 드래그 시작 이벤트 전달
                ExecuteEvents.Execute(restoredShape.gameObject, eventData, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(restoredShape.gameObject, eventData, ExecuteEvents.beginDragHandler);
                isDragging = true;
            }
        }
    }

    // 마우스 이동 시, 복구된 블록을 따라 드래그 처리
    public void OnDrag(PointerEventData eventData)
    {
        if (isDragging && restoredShape != null)
        {
            ExecuteEvents.Execute(restoredShape.gameObject, eventData, ExecuteEvents.dragHandler);
        }
    }

    // 마우스 버튼을 놓을 때, 드래그 종료 이벤트 전달
    public void OnPointerUp(PointerEventData eventData)
    {
        if (isDragging && restoredShape != null)
        {
            ExecuteEvents.Execute(restoredShape.gameObject, eventData, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(restoredShape.gameObject, eventData, ExecuteEvents.endDragHandler);
            isDragging = false;
            restoredShape = null;
        }
    }
}
