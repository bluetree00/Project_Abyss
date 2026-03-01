using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class leeGrid : MonoBehaviour
{
    [Header("Grid Settings")]
    // GridSquare 프리팹
    public GameObject gridSquarePrefab;
    // 그리드 크기(행/열)
    public int columns = 8;
    public int rows = 8;
    // 칸 간 간격 (Canvas 좌표 기준)
    public float squareGap = 3f;
    // 첫 번째 칸의 시작 위치 (왼쪽 위 기준)
    public Vector2 startPosition = new Vector2(-435f, 442f);
    // GridSquare 프리팹 스케일
    public float squareScale = 0.9f;

    // 0/1 패턴: 0 = 막힌 칸, 1 = 놓을 수 있는 칸
    public int[,] cellTypes;

    // 생성된 GridSquare들을 담는 리스트
    private List<leeGridSquare> gridSquares = new List<leeGridSquare>();

    void Start()
    {
        InitCellTypes();
        CreateGrid();
    }

    // cellTypes에 0/1 패턴을 정의 (여기서 모양 커스터마이즈)
    void InitCellTypes()
    {   
        cellTypes = new int[,]
        {
            {0,0,0,0,0,0,0,0},
            {0,0,0,0,0,0,0,0},
            {0,0,0,0,0,0,0,0},
            {0,0,1,1,0,0,0,0},
            {0,1,1,1,0,0,0,0},
            {0,0,0,1,0,1,1,0},
            {0,0,0,1,0,1,1,0},
            {0,0,0,0,0,0,0,0},
        };

    }

    // 전체 그리드 생성 흐름
    void CreateGrid()
    {
        SpawnGridSquares();
        SetGridSquarePositions();
    }
    
    // GridSquare 프리팹을 rows*columns 개수만큼 생성
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

    // 각 GridSquare의 위치 설정 + cellTypes 기반 placeable 설정
    void SetGridSquarePositions()
    {
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                int index = row * columns + col;
                var square = (leeGridSquare)gridSquares[index];

                float x = startPosition.x + col * (squareGap);
                float y = startPosition.y - row * (squareGap);

                var rt = square.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(x, y);

                // 0/1 패턴으로 placeable 여부 결정
                bool placeable = (cellTypes[row, col] == 1);
                square.Init(row, col, placeable);
            }
        }
    }
    
    // GridManager가 전체 칸 리스트를 얻을 때 사용
    public List<leeGridSquare> GetGridSquares()
    {
        return gridSquares;
    }
}