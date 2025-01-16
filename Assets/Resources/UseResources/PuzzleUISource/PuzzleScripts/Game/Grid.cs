using System.Collections.Generic;
using UnityEngine;

using System.Collections.Generic;
using UnityEngine;

public class Grid : MonoBehaviour
{
    public ShapeStorage shapeStorage; // 블록 데이터 저장소
    public int columns = 0; // 그리드의 열 개수
    public int rows = 0; // 그리드의 행 개수
    public float squaresGap = 0.1f; // 각 칸 사이의 간격
    public GameObject gridSquarePrefab; // 생성할 그리드 칸의 프리팹
    public Vector2 startPosition = new Vector2(0.0f, 0.0f); // 그리드의 시작 위치
    public float squareScale = 0.5f; // 각 그리드 칸의 스케일(크기 비율)
    public float everySquareOffset = 0.0f; // 칸 크기에 추가적으로 더해질 오프셋

    private Vector2 _offset = new Vector2(0.0f, 0.0f); // 각 칸 간의 거리 계산을 위한 오프셋
    private List<GameObject> _gridSquares = new List<GameObject>(); // 생성된 칸(GameObject) 리스트

    private void OnEnable() 
    {
        GameEvent.CheckIfShapeCanBePlaced += CheckIfShapeCanBePlaced;
    }

    private void OnDisable() 
    {
        GameEvent.CheckIfShapeCanBePlaced -= CheckIfShapeCanBePlaced;
    }

    void Start()
    {
        CreateGrid(); // 그리드 생성
    }

    private void CreateGrid()
    {
        SpawnGridSquares(); // 칸 생성
        SetGridSquaresPositions(); // 칸 위치 설정
    }

    private void SpawnGridSquares()
    {
        int square_index = 0; // 각 칸의 고유 인덱스 초기화

        for (int row = 0; row < rows; ++row)
        {
            for (int column = 0; column < columns; ++column)
            {
                var gridSquare = Instantiate(gridSquarePrefab, transform);

                gridSquare.transform.localScale = new Vector3(squareScale, squareScale, squareScale);
                _gridSquares.Add(gridSquare);

                var gridSquareComponent = gridSquare.GetComponent<GridSquare>();
                if (gridSquareComponent != null)
                {
                    gridSquareComponent.SetImage((row * columns + column) % 2 == 0);
                    gridSquareComponent.SquareIndex = square_index; // 고유 인덱스 설정
                }
                else
                {
                    Debug.LogError("GridSquare 컴포넌트가 누락되었습니다.");
                }

                square_index++;
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

    /// <summary>
    /// 현재 선택된 블록이 그리드에 배치 가능한지 확인하고 배치
    /// </summary>
    private void CheckIfShapeCanBePlaced()
    {
        var squareIndexes = new List<int>();

        foreach (var square in _gridSquares)
        {
            var gridSquare = square.GetComponent<GridSquare>();

            if (gridSquare.Selected && !gridSquare.SquareOccupied)
            {
                squareIndexes.Add(gridSquare.SquareIndex);
                gridSquare.Selected = false;
            }
        }

        Debug.Log($"선택된 칸 개수: {squareIndexes.Count}");

        var GetCurrentSelectedShape = shapeStorage.GetCurrentSelectedShape();
        if (GetCurrentSelectedShape == null) 
        {
            Debug.LogWarning("현재 선택된 블록이 없습니다.");
            return;
        }

        Debug.Log($"블록이 차지해야 할 칸 개수: {GetCurrentSelectedShape.TotalSquareNumber}");

        if (GetCurrentSelectedShape.TotalSquareNumber == squareIndexes.Count)
        {
            foreach (var squareIndex in squareIndexes)
            {
                _gridSquares[squareIndex].GetComponent<GridSquare>().PlaceShapeOnBoard();
            }

            GetCurrentSelectedShape.DeactivateShape();
        }
        else
        {
            Debug.LogWarning("칸 개수가 맞지 않아 블록을 배치할 수 없습니다.");
            GameEvent.MoveShapeToStartPosition();
        }
    }

    // 추가 필요사항 구현
    // 필요에 따라 추가적인 메서드(블록 초기화, UI 업데이트 등)를 여기에 작성
}
