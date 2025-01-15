using System.Collections.Generic;
using UnityEngine;

public class Grid : MonoBehaviour
{
    // 그리드 설정 관련 변수들
    public int columns = 0; // 그리드의 열 개수
    public int rows = 0; // 그리드의 행 개수
    public float squaresGap = 0.1f; // 각 칸 사이의 간격
    public GameObject gridSquarePrefab; // 생성할 그리드 칸의 프리팹
    public Vector2 startPosition = new Vector2(0.0f, 0.0f); // 그리드의 시작 위치
    public float squareScale = 0.5f; // 각 그리드 칸의 스케일(크기 비율)
    public float everySquareOffset = 0.0f; // 칸 크기에 추가적으로 더해질 오프셋

    // 내부에서 사용하는 변수들
    private Vector2 _offset = new Vector2(0.0f, 0.0f); // 각 칸 간의 거리 계산을 위한 오프셋
    private List<GameObject> _gridSquares = new List<GameObject>(); // 생성된 칸(GameObject) 리스트

    private void OnEnable() 
    {
        GameEvent.CheckIfShapeCanBePlaced += CheckIfShapeCanBePlaceed;
    }


    private void OnDisable() 
    {
        GameEvent.CheckIfShapeCanBePlaced -= CheckIfShapeCanBePlaceed;
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

    // 그리드 칸을 생성하는 함수
    private void SpawnGridSquares()
    {
        // 행(row)과 열(column)을 반복하며 칸을 생성
        for (int row = 0; row < rows; ++row)
        {
            for (int column = 0; column < columns; ++column)
            {
                // 프리팹을 복제(Instantiate)하여 새로운 칸 생성
                var gridSquare = Instantiate(gridSquarePrefab, transform);

                // 생성된 칸의 스케일(크기 비율) 설정
                gridSquare.transform.localScale = new Vector3(squareScale, squareScale, squareScale);

                // 생성된 칸을 리스트에 추가
                _gridSquares.Add(gridSquare);

                // 칸의 GridSquare 컴포넌트를 가져와 초기화
                var gridSquareComponent = gridSquare.GetComponent<GridSquare>();
                if (gridSquareComponent != null)
                {
                    // 칸의 초기 이미지를 짝수/홀수 구분에 따라 설정
                    gridSquareComponent.SetImage((row * columns + column) % 2 == 0);
                }
                else
                {
                    // GridSquare 컴포넌트가 없는 경우 경고 메시지 출력
                    Debug.LogError("GridSquare 컴포넌트가 누락되었습니다.");
                }
            }
        }
    }

    // 생성된 칸들의 위치를 설정하는 함수
    private void SetGridSquaresPositions()
    {
        // 생성된 칸이 없으면 경고 메시지 출력 후 함수 종료
        if (_gridSquares.Count == 0)
        {
            Debug.LogError("그리드가 비어 있습니다. SpawnGridSquares가 호출되지 않았습니다.");
            return;
        }

        // 프리팹의 RectTransform을 통해 칸의 크기를 가져옴
        var squareRect = gridSquarePrefab.GetComponent<RectTransform>();
        _offset.x = squareRect.rect.width * squareRect.transform.localScale.x + everySquareOffset; // 가로 오프셋 계산
        _offset.y = squareRect.rect.height * squareRect.transform.localScale.y + everySquareOffset; // 세로 오프셋 계산

        // 리스트의 각 칸(GameObject)에 대해 위치 설정
        for (int i = 0; i < _gridSquares.Count; ++i)
        {
            // 현재 칸의 행(row)과 열(column) 계산
            int row = i / columns;
            int column = i % columns;

            // 칸의 위치 계산 (X: 가로 방향, Y: 세로 방향)
            float posX = startPosition.x + column * (_offset.x + squaresGap);
            float posY = startPosition.y - row * (_offset.y + squaresGap);

            // 생성된 칸의 RectTransform을 가져와 위치를 설정
            var rectTransform = _gridSquares[i].GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.anchoredPosition = new Vector2(posX, posY); // UI 캔버스 상 위치 설정
                rectTransform.localPosition = new Vector3(posX, posY, 0.0f); // 로컬 좌표 설정
            }
        }
    }

    private void CheckIfShapeCanBePlaceed()
    {
        foreach (var square in _gridSquares)
        {
            var gridSquare = square.GetComponent<GridSquare>();

            if(gridSquare.CanWeUseThisSquare() == true)
            {
                gridSquare.ActivateSquare();
            }
        }
    }
}
