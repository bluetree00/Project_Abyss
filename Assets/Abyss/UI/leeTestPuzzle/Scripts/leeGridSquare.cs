using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class leeGridSquare : MonoBehaviour
{
    [Header("Visuals")]
    public Image activeImage;
    public Image hoverImage; 

    [Header("State")]
    public bool isOccupied = false;
    public bool isHighlighted = false;

    void Start()
    {
        SetOccupied(false);
        SetHighlight(false);
    }

    public void SetOccupied(bool occupied)
    {
        isOccupied = occupied;
        if (activeImage != null)
            activeImage.enabled = isOccupied;
    }

    public void SetHighlight(bool on)
    {
        isHighlighted = on;
        if (hoverImage != null)
            hoverImage.enabled = isHighlighted;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("ShapeBlock"))
        {
            if (!isOccupied)
                SetHighlight(true);
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("ShapeBlock"))
        {
            SetHighlight(false);
        }
    }
}