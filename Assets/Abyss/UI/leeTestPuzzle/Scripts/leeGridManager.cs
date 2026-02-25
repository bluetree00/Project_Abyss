using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class leeGridManager : MonoBehaviour
{
    public static leeGridManager Instance { get; private set; }

    public leeGrid grid;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public bool TryPlaceShape(leeShape shape)
    {
        var candidateSquares = new List<leeGridSquare>();

        var blocks = shape.GetComponentsInChildren<RectTransform>();
        int blockCount = 0;

        foreach (var block in blocks)
        {
            if (block == shape.transform) continue;
            blockCount++;

            leeGridSquare square = FindGridSquareUnderPosition(block.position);

            if (square == null)
            {
                return false;
            }

            if (square.isOccupied)
            {
                return false;
            }

            if (!candidateSquares.Contains(square))
                candidateSquares.Add(square);
        }

        foreach (var sq in candidateSquares)
        {
            sq.SetOccupied(true);
            sq.SetHighlight(false);
        }

        if (candidateSquares.Count > 0)
        {
            var first = candidateSquares[0];
            shape.transform.position = first.transform.position;
        }

        return true;
    }

    leeGridSquare FindGridSquareUnderPosition(Vector3 worldPos)
    {
        float minDist = float.MaxValue;
        leeGridSquare result = null;

        foreach (var sq in grid.GetGridSquares())
        {
            float d = Vector3.Distance(worldPos, sq.transform.position);
            if (d < minDist)
            {
                minDist = d;
                result = sq;
            }
        }
        if (result == null){return result;}
        var rt = result.GetComponent<RectTransform>();
        float cellSize = rt.rect.size.x * result.transform.lossyScale.x;


        float maxAllowedDist = cellSize * 0.5f;
        if (minDist > maxAllowedDist){return null;}
        
        return result;
    }
}