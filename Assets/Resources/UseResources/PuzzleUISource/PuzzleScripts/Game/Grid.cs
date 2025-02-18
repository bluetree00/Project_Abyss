using System.Collections.Generic;
using UnityEngine;

public class Grid : MonoBehaviour
{
    // 그리드 설정 관련 변수들
    public ShapeStorage shapeStorage;
    public int columns = 0; // 그리드의 열 개수
    public int rows = 0; // 그리드의 행 개수
    public float squaresGap = 0.1f; // 각 칸 사이의 간격
    public GameObject gridSquare; // 생성할 그리드 칸의 프리팹
    public Vector2 startPosition = new Vector2(0.0f, 0.0f); // 그리드의 시작 위치
    public float squareScale = 0.5f; // 각 그리드 칸의 스케일(크기 비율)
    public float everySquareOffset = 0.0f; // 칸 크기에 추가적으로 더해질 오프셋

    // 퍼블릭으로 모양 선택 변수 추가
    public Define.ShapeType selectedShapeType = Define.ShapeType.Apple;  // 디폴트는 Apple 모양

    // 내부에서 사용하는 변수들
    private Vector2 _offset = new Vector2(0.0f, 0.0f); // 각 칸 간의 거리 계산을 위한 오프셋
    private List<GameObject> _gridSquares = new List<GameObject>(); // 생성된 칸(GameObject) 리스트
    
    private LineIndicator _lineIndicator;

    private void OnEnable() 
    {
        GameEvents.CheckIfShapeCanBePlaced += CheckIfShapeCanBePlaced;
        GameEvents.RequestBlockRemoval += RemoveBlocksByID; // 🔹 블록 삭제 이벤트 구독
    }

    private void OnDisable() 
    {
        GameEvents.CheckIfShapeCanBePlaced -= CheckIfShapeCanBePlaced;
        GameEvents.RequestBlockRemoval -= RemoveBlocksByID; // 🔹 구독 해제
    }

    // 게임 시작 시 호출 (Unity 생명 주기 함수)
    void Start()
    {
        _lineIndicator = GetComponent<LineIndicator>();
        CreateGrid(); // 그리드 생성
    }

    // 그리드를 생성하는 함수
    private void CreateGrid()
    {
        // 1. 칸(GameObject)을 생성하여 리스트에 추가
        SpawnGridSquares();

        // 2. 생성된 칸들의 위치를 설정
        SetGridSquaresPositions();
    }

    private void SpawnGridSquares()
    {
        int square_index = 0; // 각 칸의 고유 인덱스 초기화

        for (var row = 0; row < rows; ++row)
        {
            for (var column = 0; column < columns; ++column)
            {
                // 그리드 칸 생성
                _gridSquares.Add(Instantiate(gridSquare) as GameObject);

                // 생성된 그리드 칸의 정보 설정
                var gridSquareComponent = _gridSquares[_gridSquares.Count - 1].GetComponent<GridSquare>();
                gridSquareComponent.SquareIndex = square_index;
                gridSquareComponent.transform.SetParent(this.transform);
                gridSquareComponent.transform.localScale = new Vector3(squareScale, squareScale, squareScale);

                // 모양 정보를 받아와서 적용
                int[,] shapeGrid = Define.GetShapeGrid(selectedShapeType);  // 선택된 모양 가져오기
                bool isShape = shapeGrid[row, column] == 1;  // 해당 칸에 모양이 있으면 true

                gridSquareComponent.SetImage(isShape);  // 해당 칸에 모양 적용

                square_index++;
            }
        }
    }

    private void SetGridSquaresPositions()
    {
        int column_number = 0;
        int row_number = 0;
        Vector2 square_gap_number = new Vector2(0.0f, 0.0f);
        bool row_moved = false;

        var square_rect = _gridSquares[0].GetComponent<RectTransform>();

        _offset.x = square_rect.rect.width * square_rect.transform.localScale.x + everySquareOffset;
        _offset.y = square_rect.rect.height * square_rect.transform.localScale.y + everySquareOffset;

        foreach(GameObject square in _gridSquares)
        {
            if(column_number + 1 > columns)
            {
                square_gap_number.x = 0;
                column_number = 0;
                row_number++;
                row_moved = false;
            }

            var pos_x_offset = _offset.x * column_number + (square_gap_number.x * squaresGap);
            var pos_y_offset = _offset.y * row_number + (square_gap_number.y * squaresGap);

            if(column_number > 0 && column_number % 3 ==0)
            {
                square_gap_number.x++;
                pos_x_offset += squaresGap;
            }

            if(row_number > 0 && row_number % 3 ==0 && row_moved == false)
            {
                row_moved = true;
                square_gap_number.y++;
                pos_y_offset += squaresGap;
            }

            square.GetComponent<RectTransform>().anchoredPosition = new Vector2(startPosition.x + pos_x_offset,
                startPosition.y - pos_y_offset);
            square.GetComponent<RectTransform>().localPosition = new Vector3(startPosition.x + pos_x_offset,
                startPosition.y - pos_y_offset, 0.0f);

            column_number++;
        }
    }

   private void CheckIfShapeCanBePlaced()
{
    var squareIndexes = new List<int>(); // 선택된 칸들의 인덱스를 저장할 리스트

    // 선택된 칸들을 찾아 리스트에 추가
    foreach (var square in _gridSquares)
    {
        var gridSquare = square.GetComponent<GridSquare>();

        // 칸이 선택되었고, 이미 채워지지 않았다면
        if (gridSquare.Selected && !gridSquare.SquareOccupied)
        {
            squareIndexes.Add(gridSquare.SquareIndex);
            gridSquare.Selected = false; // 선택된 칸을 비선택 상태로 변경
        }
    }

    var currentSelectedShape = shapeStorage.GetCurrentSelectedShape();

    if (currentSelectedShape == null) return;

    // 선택된 칸의 수와 모양의 요구하는 칸 수가 일치하는지 확인
    if (currentSelectedShape.TotalSquareNumber == squareIndexes.Count)
    {
        int[,] shapeGrid = Define.GetShapeGrid(selectedShapeType); // 선택된 모양 가져오기
        bool canPlaceShape = true; // 배치 가능 여부 확인 변수

        // 0인 칸과 겹치는지 체크
        foreach (var squareIndex in squareIndexes)
        {
            var gridSquare = _gridSquares[squareIndex].GetComponent<GridSquare>();

            // 현재 칸의 row와 column을 계산
            int row = squareIndex / columns;
            int column = squareIndex % columns;

            // 해당 칸이 1인 부분이어야만 배치 가능
            if (shapeGrid[row, column] == 0)
            {
                canPlaceShape = false; // 0인 부분과 겹치면 배치 불가
             
                break;
            }
        }

        // 0인 부분과 겹치지 않으면 배치
        if (canPlaceShape)
        {
            foreach (var squareIndex in squareIndexes)
            {
                var gridSquare = _gridSquares[squareIndex].GetComponent<GridSquare>();

                int row = squareIndex / columns;
                int column = squareIndex % columns;

                // 모양의 1인 부분에만 배치
                if (shapeGrid[row, column] == 1 && !gridSquare.SquareOccupied)
                {
                    gridSquare.PlaceShapeOnBoard(currentSelectedShape.GetShapeID()); // 🔹 블록 ID 저장
                    
                }
            }

            // 남은 모양 체크 후 이벤트 처리
            var shapeLeft = 0;

            foreach (var shape in shapeStorage.shapeList)
            {
                if(shape.IsOnStartPosition() && shape.IsAnyOfShapeSquareActive())
                {
                    shapeLeft++;
                }
            }

            if(shapeLeft == 0)
            {
                GameEvents.RequestNewShapes();
            }
            else
            {
                GameEvents.SetShapeInactive();
            }
        }
        else
        {
           
            Debug.Log("0인 부분과 겹쳐서 모양을 시작 위치로.");
            
            // 모양을 시작 위치로 되돌림
            GameEvents.MoveShapeToStartPosition();
        }
    }
    else
    {
        
        Debug.Log("칸 수가 일치하지 않아 모양을 시작 위치로.");
     
        GameEvents.MoveShapeToStartPosition();
    }
}

public void RemoveBlocksByID(int shapeID)
{
    HashSet<int> visited = new HashSet<int>(); // 방문한 블록 저장
    Stack<int> stack = new Stack<int>(); // DFS를 위한 스택

    int[,] shapeGrid = Define.GetShapeGrid(selectedShapeType); // 🔹 Grid의 selectedShapeType 사용

    foreach (var square in _gridSquares)
    {
        var gridSquare = square.GetComponent<GridSquare>();
        if (gridSquare.SquareOccupied && gridSquare.GetShapeID() == shapeID)
        {
            stack.Push(gridSquare.SquareIndex);
        }
    }

    while (stack.Count > 0)
    {
        int currentIndex = stack.Pop();

        if (!visited.Contains(currentIndex))
        {
            visited.Add(currentIndex);
            var currentSquare = _gridSquares[currentIndex].GetComponent<GridSquare>();

            if (currentSquare.SquareOccupied && currentSquare.GetShapeID() == shapeID)
            {
                currentSquare.ClearSquare(); // 블록 삭제
            }

            int row = currentIndex / columns;
            int column = currentIndex % columns;

            // 🔹 추가적인 연결 체크 로직 (shapeGrid를 참조)
            if (row > 0 && shapeGrid[row - 1, column] == 1) stack.Push(currentIndex - columns);
            if (row < rows - 1 && shapeGrid[row + 1, column] == 1) stack.Push(currentIndex + columns);
            if (column > 0 && shapeGrid[row, column - 1] == 1) stack.Push(currentIndex - 1);
            if (column < columns - 1 && shapeGrid[row, column + 1] == 1) stack.Push(currentIndex + 1);
        }
    }
     GameEvents.CheckIfShapeCanBePlaced?.Invoke();

}

}
