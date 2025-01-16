using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GridSquare : MonoBehaviour
{
    public Image hooverImage;
    public Image activeImage;
    public Image normalImage;
    public List<Sprite> normallmages;

    public bool Selected {get; set;}
    public int SquareIndex{get; set;}
    public bool SquareOccupied {get; set;}
    // Start is called before the first frame update
    void Start()
    {
        Selected = false;
        SquareOccupied = false;
        
    }

    public bool CanWeUseThisSquare()
    {
        return hooverImage.gameObject.activeSelf;
    }

    public void PlaceShapeOnBoard()
    {
        ActivateSquare();
    }

    public void ActivateSquare()
    {
        hooverImage.gameObject.SetActive(false);
        activeImage.gameObject.SetActive(true);
        Selected = true;
        SquareOccupied = true;
    }
    


    public void SetImage(bool setFirstImage)
    {
        normalImage.GetComponent<Image>().sprite = setFirstImage ? normallmages[1] : normallmages[0];
    }

    private void OnTriggerEnter2D(Collider2D other) 
    {
        if(SquareOccupied == false)
        {
            Selected = true;
            hooverImage.gameObject.SetActive(true);
        }
        else if(other.GetComponent<ShapeSquare>() != null)
        {
            other.GetComponent<ShapeSquare>().SetOccupied();
        }
       
       
    }

     private void OnTriggerStay2D(Collider2D other) 
    {
        Selected = true;

        if(SquareOccupied == false)
        {
            hooverImage.gameObject.SetActive(true);
        }
        else if(other.GetComponent<ShapeSquare>() != null)
        {
            other.GetComponent<ShapeSquare>().SetOccupied();
        }
    }

    private void OnTriggerExit2D(Collider2D other) 
    {
        if(SquareOccupied == false)
        {
            Selected = false;
            hooverImage.gameObject.SetActive(false);
        }
        else if(other.GetComponent<ShapeSquare>() != null)
        {
            other.GetComponent<ShapeSquare>().UnSetOccupied();
        }


    }

}
