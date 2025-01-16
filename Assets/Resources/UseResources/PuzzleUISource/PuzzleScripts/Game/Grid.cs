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


    // 내부에서 사용하는 변수들
    private Vector2 _offset = new Vector2(0.0f, 0.0f); // 각 칸 간의 거리 계산을 위한 오프셋
    private List<GameObject> _gridSquares = new List<GameObject>(); // 생성된 칸(GameObject) 리스트
    

    private void OnEnable() 
    {
        GameEvents.CheckIfShapeCanBePlaced += CheckIfShapeCanBePlaced;
    }

    private void OnDisable() 
    {
        GameEvents.CheckIfShapeCanBePlaced -= CheckIfShapeCanBePlaced;
    }

    // 게임 시작 시 호출 (Unity 생명 주기 함수)
    void Start()
    {

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

        for( var row = 0; row < rows; ++row)
        {
            for(var column = 0; column < columns; ++column)
            {
                _gridSquares.Add(Instantiate(gridSquare) as GameObject);

                _gridSquares[_gridSquares.Count -1].GetComponent<GridSquare>().SquareIndex = square_index;
                _gridSquares[_gridSquares.Count -1].transform.SetParent(this.transform);
                _gridSquares[_gridSquares.Count -1].transform.localScale = new Vector3(squareScale, squareScale, squareScale);
                _gridSquares[_gridSquares.Count -1].GetComponent<GridSquare>().SetImage(square_index % 2 == 0);
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
                pos_y_offset +=squaresGap;
            }

            square.GetComponent<RectTransform>().anchoredPosition = new Vector2(startPosition.x + pos_x_offset,
                startPosition.y - pos_y_offset);
            square.GetComponent<RectTransform>().localPosition = new Vector3(startPosition.x + pos_x_offset,
                startPosition.y - pos_y_offset, 0.0f);

            column_number ++;
       }
    }

    private void CheckIfShapeCanBePlaced()
{
    var squareIndexes = new List<int>(); // 선택된 칸들의 인덱스를 저장할 리스트

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

    // 디버깅: squareIndexes.Count와 currentSelectedShape.TotalSquareNumber 출력
    Debug.Log($"squareIndexes.Count: {squareIndexes.Count}, currentSelectedShape.TotalSquareNumber: {currentSelectedShape.TotalSquareNumber}");

    // 선택된 칸의 수와 모양의 요구하는 칸 수가 일치하는지 확인
    if (currentSelectedShape.TotalSquareNumber == squareIndexes.Count)
    {
        // 디버깅: 선택된 칸에 모양을 배치할 때
        Debug.Log("칸 수가 일치하므로 모양을 배치합니다.");

        // 선택된 칸에 모양을 배치
        foreach (var squareIndex in squareIndexes)
        {
            var gridSquare = _gridSquares[squareIndex].GetComponent<GridSquare>();

            // 이미 배치된 칸을 제외하고 모양을 배치
            if (!gridSquare.SquareOccupied)
            {
                gridSquare.ActivateSquare(); // 그리드에 모양 배치
                Debug.Log($"모양이 칸 {squareIndex}에 배치되었습니다.");
            }
        }

        // 모양을 비활성화
        currentSelectedShape.DeactivateShape();
        Debug.Log("모양 비활성화 완료.");
    }
    else
    {
        // 디버깅: 칸 수가 일치하지 않음
        Debug.Log("칸 수가 일치하지 않아 모양을 시작 위치로 되돌립니다.");
        
        // 모양을 시작 위치로 되돌림
        GameEvents.MoveShapeToStartPosition();
    }
}


}
