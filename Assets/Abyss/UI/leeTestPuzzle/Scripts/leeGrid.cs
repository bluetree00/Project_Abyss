using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class leeGrid : MonoBehaviour
{
    [Header("Grid Settings")]
    public GameObject gridSquarePrefab;
    public int columns = 9;
    public int rows = 9;
    public float squareGap = 3f;
    public Vector2 startPosition = new Vector2(-435f, 442f);
    public float squareScale = 0.9f;

    private List<leeGridSquare> gridSquares = new List<leeGridSquare>();

    void Start()
    {
        CreateGrid();
    }

    void CreateGrid()
    {
        SpawnGridSquares();
        SetGridSquarePositions();
    }

    void SpawnGridSquares()
    {
        gridSquares.Clear();

        int total = rows * columns;
        for (int i = 0; i < total; i++)
        {
            GameObject squareObj = Instantiate(gridSquarePrefab, transform);
            squareObj.transform.localScale = Vector3.one * squareScale;

            leeGridSquare square = squareObj.GetComponent<leeGridSquare>();
            gridSquares.Add(square);
        }
    }

    void SetGridSquarePositions()
    {
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                int index = row * columns + col;
                var square = gridSquares[index];

                float x = startPosition.x + col * (squareGap);
                float y = startPosition.y - row * (squareGap);

                var rt = square.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(x, y);
            }
        }
    }

    public List<leeGridSquare> GetGridSquares()
    {
        return gridSquares;
    }
}