using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameEvents : MonoBehaviour
{
     public static event Action<int> RequestBlockRemoval;
    public static Action CheckIfShapeCanBePlaced;

    public static Action MoveShapeToStartPosition;

    public static Action RequestNewShapes;

    public static Action SetShapeInactive;

    public static void InvokeBlockRemoval(int shapeID)
    {
        RequestBlockRemoval?.Invoke(shapeID);
    }

    public static event Action<int> RequestShapeByID;

    public static void InvokeShapeRestoration(int shapeID)
    {
        RequestShapeByID?.Invoke(shapeID);
    }

   // public static Action<ShapeType> OnShapeMatched;

}
