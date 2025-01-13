using System.Collections.Generic;
using UnityEngine;

public class Grid : MonoBehaviour
{
    public int columns = 0;
    public int rows = 0;
    public float squaresGap = 0.1f;
    public GameObject gridSquarePrefab; // 이름 변경: gridSquare → gridSquarePrefab
    public Vector2 startPosition = new Vector2(0.0f, 0.0f);
    public float squareScale = 0.5f;
    public float everySquareOffset = 0.0f;

    private Vector2 _offset = new Vector2(0.0f, 0.0f);
    private List<GameObject> _gridSquares = new List<GameObject>();

    void Start()
    {
        CreateGrid();
    }

    private void CreateGrid()
    {
        // 1. 그리드 오브젝트 생성
        SpawnGridSquares();

        // 2. 그리드 위치 설정
        SetGridSquaresPositions();
    }

    private void SpawnGridSquares()
    {
        for (int row = 0; row < rows; ++row)
        {
            for (int column = 0; column < columns; ++column)
            {
                // 오브젝트 생성
                var gridSquare = Instantiate(gridSquarePrefab, transform);
                gridSquare.transform.localScale = new Vector3(squareScale, squareScale, squareScale);

                // 리스트에 추가
                _gridSquares.Add(gridSquare);

                // 기본 이미지 설정
                var gridSquareComponent = gridSquare.GetComponent<GridSquare>();
                if (gridSquareComponent != null)
                {
                    gridSquareComponent.SetImage((row * columns + column) % 2 == 0);
                }
                else
                {
                    Debug.LogError("GridSquare 컴포넌트가 누락되었습니다.");
                }
            }
        }
    }

    private void SetGridSquaresPositions()
    {
        if (_gridSquares.Count == 0)
        {
            Debug.LogError("그리드가 비어 있습니다. SpawnGridSquares가 호출되지 않았습니다.");
            return;
        }

        var squareRect = gridSquarePrefab.GetComponent<RectTransform>();
        _offset.x = squareRect.rect.width * squareRect.transform.localScale.x + everySquareOffset;
        _offset.y = squareRect.rect.height * squareRect.transform.localScale.y + everySquareOffset;

        for (int i = 0; i < _gridSquares.Count; ++i)
        {
            int row = i / columns;
            int column = i % columns;

            float posX = startPosition.x + column * (_offset.x + squaresGap);
            float posY = startPosition.y - row * (_offset.y + squaresGap);

            var rectTransform = _gridSquares[i].GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.anchoredPosition = new Vector2(posX, posY);
                rectTransform.localPosition = new Vector3(posX, posY, 0.0f);
            }
        }
    }
}
