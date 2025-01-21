using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ShapeStorage : MonoBehaviour
{
    public List<ShapeData> shapeData; // 모양 데이터 리스트
    public List<Shape> shapeList;     // 생성된 모양 리스트

    
    private void OnEnable()
    {
        GameEvents.RequestNewShapes += RequestNewShapes;
    }

    private void OnDisable()
    {
        GameEvents.RequestNewShapes -= RequestNewShapes;
    }


    // Start is called before the first frame update
    void Start()
    {
        // shapeData가 비어있는지 확인
        if (shapeData.Count == 0)
        {
            Debug.LogError("No ShapeData available!");
            return;
        }

        // 각 Shape에 대해 랜덤한 ShapeData를 할당
        foreach (var shape in shapeList)
        {
            int shapeIndex = UnityEngine.Random.Range(0, shapeData.Count);
            ShapeData selectedShapeData = shapeData[shapeIndex];

            // Shape 생성 후 로그로 출력
            shape.CreateShape(selectedShapeData);

            // 생성된 모양에 대한 정보 로그
            Debug.Log($"Shape created with ShapeData: {selectedShapeData.name}");
        }
    }
    // 현재 선택된 Shape 반환
    public Shape GetCurrentSelectedShape()
    {
        foreach (var shape in shapeList)
        {
            // Shape이 시작 위치에 있지 않고, 적어도 하나의 정사각형이 활성화된 경우
            if (!shape.IsOnStartPosition() && shape.IsAnyOfShapeSquareActive())
            {
                return shape;
            }
        }

        Debug.LogError("No shape selected!");
        return null;
    }

    private void RequestNewShapes()
    {
        foreach(var shape in shapeList)
        {
            var shapeIndex = UnityEngine.Random.Range(0, shapeData.Count);
            shape.RequestNewShape(shapeData[shapeIndex]);
        }
    }
}
