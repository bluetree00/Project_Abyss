using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Shape : MonoBehaviour
{
    public GameObject squareShapeImage; // 사각형 오브젝트 프리팹
    public ShapeData CurrentShapeData;  // 현재 사용할 ShapeData ScriptableObject

    private List<GameObject> _currentShape = new List<GameObject>(); // 생성된 사각형 오브젝트 리스트

    void Start()
    {
        // 초기화 시 새로운 ShapeData를 요청하여 생성
        RequestNewShape(CurrentShapeData);
    }

    // 새로운 ShapeData 기반으로 도형 생성 요청
    public void RequestNewShape(ShapeData shapeData)
    {
        CreateShape(shapeData); // 실제 도형 생성 메서드 호출
    }

    // ShapeData 기반으로 도형 생성
    public void CreateShape(ShapeData shapeData)
    {
        // 현재 사용 중인 ShapeData를 설정
        CurrentShapeData = shapeData;

        // ShapeData에서 활성화된 사각형의 총 개수 계산
        var totalSquareNumber = GetNumberOfSquares(shapeData);

        // _currentShape 리스트 크기를 활성화된 사각형 개수만큼 유지
        while (_currentShape.Count <= totalSquareNumber)
        {
            // 사각형 프리팹 인스턴스 생성 후 리스트에 추가
            _currentShape.Add(Instantiate(squareShapeImage, transform) as GameObject);
        }

        // 모든 사각형의 위치를 초기화하고 비활성화
        foreach (var square in _currentShape)
        {
            square.gameObject.transform.position = Vector3.zero;
            square.gameObject.SetActive(false);
        }

        // 사각형 크기와 이동 거리 계산
        var squareRect = squareShapeImage.GetComponent<RectTransform>();
        var moveDistance = new Vector2(squareRect.rect.width * squareRect.localScale.x,
                                       squareRect.rect.height * squareRect.localScale.y);

        int currentIndexInList = 0;

        // ShapeData의 보드 데이터를 기반으로 활성화된 셀 위치에 사각형 배치
        for (var row = 0; row < shapeData.rows; row++)
        {
            for (var column = 0; column < shapeData.columns; column++)
            {
                if (shapeData.board[row].column[column])
                {
                    _currentShape[currentIndexInList].SetActive(true); // 활성화된 셀의 사각형만 표시
                    _currentShape[currentIndexInList].GetComponent<RectTransform>().localPosition =
                        new Vector2(GetXpositionForShapeSquare(shapeData, column, moveDistance), // X 좌표 계산
                                    GetYPositionForShapeSquare(shapeData, row, moveDistance)); // Y 좌표 계산

                    currentIndexInList++;
                }
            }
        }
    }

    // 특정 행의 Y 좌표 계산
    private float GetYPositionForShapeSquare(ShapeData shapeData, int row, Vector2 moveDistance)
    {
        float shiftOnY = 0f;

        if (shapeData.rows > 1)
        {
            if (shapeData.rows % 2 != 0) // 행의 개수가 홀수인 경우
            {
                var middleSquareIndex = (shapeData.rows - 1) / 2; // 중앙 행 인덱스
                var multiplier = (shapeData.rows - 1) / 2;

                if (row < middleSquareIndex) // 중앙보다 위쪽
                {
                    shiftOnY = moveDistance.y * 1;
                    shiftOnY *= multiplier;
                }
                else if (row > middleSquareIndex) // 중앙보다 아래쪽
                {
                    shiftOnY = moveDistance.y * -1;
                    shiftOnY *= multiplier;
                }
            }
            else // 행의 개수가 짝수인 경우
            {
                var middleSquareIndex2 = (shapeData.rows == 2) ? 1 : (shapeData.rows / 2);
                var middleSquareIndex1 = (shapeData.rows == 2) ? 0 : shapeData.rows - 2;
                var multiplier = shapeData.rows / 2;

                if (row == middleSquareIndex1 || row == middleSquareIndex2) // 중앙 행들
                {
                    if (row == middleSquareIndex2)
                    {
                        shiftOnY = (moveDistance.y / 2) * -1;
                    }

                    if (row == middleSquareIndex1)
                    {
                        shiftOnY = (moveDistance.y / 2);
                    }
                }

                if (row < middleSquareIndex1 && row < middleSquareIndex2) // 위쪽 행
                {
                    shiftOnY = moveDistance.y * 1;
                    shiftOnY *= multiplier;
                }
                else if (row > middleSquareIndex1 && row > middleSquareIndex2) // 아래쪽 행
                {
                    shiftOnY = moveDistance.y * -1;
                    shiftOnY *= multiplier;
                }
            }
        }

        return shiftOnY;
    }

    // 특정 열의 X 좌표 계산
    private float GetXpositionForShapeSquare(ShapeData shapeData, int column, Vector2 moveDistance)
    {
        float shiftOnX = 0f;

        if (shapeData.columns > 1)
        {
            if (shapeData.columns % 2 != 0) // 열의 개수가 홀수인 경우
            {
                var middleSquareIndex = (shapeData.columns - 1) / 2;
                var multiplier = (shapeData.columns - 1) / 2;

                if (column < middleSquareIndex) // 중앙보다 왼쪽
                {
                    shiftOnX = moveDistance.x * -1;
                    shiftOnX *= multiplier;
                }
                else if (column > middleSquareIndex) // 중앙보다 오른쪽
                {
                    shiftOnX = moveDistance.x * 1;
                    shiftOnX *= multiplier;
                }
            }
            else // 열의 개수가 짝수인 경우
            {
                var middleSquareIndex2 = (shapeData.columns == 2) ? 1 : (shapeData.columns / 2);
                var middleSquareIndex1 = (shapeData.columns == 2) ? 0 : shapeData.columns - 1;
                var multiplier = shapeData.columns / 2;

                if (column == middleSquareIndex1 || column == middleSquareIndex2) // 중앙 열들
                {
                    if (column == middleSquareIndex2)
                    {
                        shiftOnX = moveDistance.x / 2;
                    }

                    if (column == middleSquareIndex1)
                    {
                        shiftOnX = (moveDistance.x / 2) * -1;
                    }
                }

                if (column < middleSquareIndex1 && column < middleSquareIndex2) // 왼쪽 열
                {
                    shiftOnX = moveDistance.x * -1;
                    shiftOnX *= multiplier;
                }
                else if (column > middleSquareIndex1 && column > middleSquareIndex2) // 오른쪽 열
                {
                    shiftOnX = moveDistance.x * 1;
                    shiftOnX *= multiplier;
                }
            }
        }

        return shiftOnX;
    }

    // 활성화된 사각형의 총 개수 계산
    private int GetNumberOfSquares(ShapeData shapeData)
    {
        int number = 0;

        foreach (var rowData in shapeData.board)
        {
            foreach (var active in rowData.column)
            {
                if (active) number++; // 활성화된 셀이면 카운트 증가
            }
        }

        return number;
    }
}
