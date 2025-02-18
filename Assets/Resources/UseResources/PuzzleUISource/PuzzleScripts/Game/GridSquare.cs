using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class GridSquare : MonoBehaviour, IPointerClickHandler
{
    public Image hooverImage;
    public Image activeImage;
    public Image normalImage;
    public List<Sprite> normallmages;

    public bool Selected {get; set;}
    public int SquareIndex {get; set;}
    public bool SquareOccupied {get; set;}

    private int shapeID = -1; // 배치된 블록의 ID (없으면 -1)
      private Grid gridReference; // 🔹 Grid 참조 추가

    // Start is called before the first frame update

    public int GetShapeID()
    {
        return shapeID; // shapeID 값을 반환
    }

    void Start()
    {
        Selected = false;
        SquareOccupied = false;
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

    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log($"Shape {shapeID} Clicked");
        if (shapeID != -1) // 유효한 ID인지 확인
        {
            GameEvents.InvokeShapeRestoration(shapeID); // 🔹 클릭한 블록의 ID 전달
            GameEvents.InvokeBlockRemoval(shapeID); 
        }
    }



    


}
