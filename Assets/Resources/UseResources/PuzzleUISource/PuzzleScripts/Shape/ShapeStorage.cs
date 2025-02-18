using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ShapeStorage : MonoBehaviour
{
    public List<ShapeData> shapeData; // 전체 블록 데이터 리스트
    public List<Shape> shapeList;     // 생성된 블록 리스트

    private int currentID = 0; // 고유 블록 ID
    private Dictionary<int, ShapeData> shapeMap = new Dictionary<int, ShapeData>(); // ID와 블록 저장용 Map
    private Dictionary<int, ShapeData> shapeDataByID = new Dictionary<int, ShapeData>(); // 🔹 ID -> ShapeData 저장
    private Queue<int> removedShapeIDs = new Queue<int>(); // 🔹 삭제된 블록의 ID 저장

    private void OnEnable()
    {
        GameEvents.RequestNewShapes += RequestNewShapes;
        GameEvents.RequestShapeByID += RestoreShapeByID; // 🔹 특정 ID 기반 블록 복구 기능 추가
    }

    private void OnDisable()
    {
        GameEvents.RequestNewShapes -= RequestNewShapes;
        GameEvents.RequestShapeByID -= RestoreShapeByID;
    }

    void Start()
    {
        if (shapeData.Count == 0)
        {
            Debug.LogError("No ShapeData available!");
            return;
        }

        foreach (var shape in shapeList)
        {
            CreateAndAssignShape(shape);
        }
    }

    // 🔹 블록을 생성하고 ID에 맞는 데이터를 저장
    private void CreateAndAssignShape(Shape shape)
    {
        int shapeID;
        ShapeData assignedShapeData;

        if (removedShapeIDs.Count > 0)
        {
            // 🔹 삭제된 블록이 있으면 기존 ID를 재사용
            shapeID = removedShapeIDs.Dequeue();
            assignedShapeData = shapeDataByID[shapeID]; // 기존 데이터 가져오기
            Debug.Log($"Reusing shape ID {shapeID} with shape {assignedShapeData.name}");
        }
        else
        {
            // 🔹 새로운 ID 할당
            shapeID = currentID;
            int shapeIndex = UnityEngine.Random.Range(0, shapeData.Count);
            assignedShapeData = shapeData[shapeIndex];

            shapeDataByID[shapeID] = assignedShapeData; // 🔹 정적 저장
            currentID++; // ID 증가
        }

        shape.CreateShape(assignedShapeData);
        AssignUniqueID(shape, shapeID, assignedShapeData);
    }

    // 🔹 블록에 고유 ID 부여 및 저장
    private void AssignUniqueID(Shape shape, int shapeID, ShapeData shapeData)
    {
        shape.SetShapeID(shapeID); // Shape 클래스에 ID 설정
        shapeMap[shapeID] = shapeData; // Map에 저장
        Debug.Log($"Shape ID {shapeID} assigned to {shapeData.name}");
    }

    public Shape GetCurrentSelectedShape()
    {
        foreach (var shape in shapeList)
        {
            if (!shape.IsOnStartPosition() && shape.IsAnyOfShapeSquareActive())
            {
                return shape;
            }
        }

//        Debug.LogError("No shape selected!");
        return null;
    }

    // 🔹 삭제된 블록을 복구하거나 새로운 블록을 생성하는 기능 추가
    private void RequestNewShapes()
    {
        foreach (var shape in shapeList)
        {
            CreateAndAssignShape(shape);
        }
    }

    // 🔹 특정 ID 기반으로 블록을 복구하는 기능 추가
    public void RestoreShapeByID(int shapeID)
    {
        if (!shapeDataByID.ContainsKey(shapeID))
        {
            Debug.LogError($"Shape ID {shapeID} not found!");
            return;
        }

        ShapeData restoredShapeData = shapeDataByID[shapeID];

        foreach (var shape in shapeList)
        {
            if (!shape.IsAnyOfShapeSquareActive()) // 🔹 비활성화된 블록을 찾아 복구
            {
                shape.RequestNewShape(restoredShapeData);
                AssignUniqueID(shape, shapeID, restoredShapeData);
                Debug.Log($"Restored shape with ID {shapeID}: {restoredShapeData.name}");
                return;
            }
        }
    }

    // 🔹 삭제된 블록을 저장하도록 변경
    public void RemoveShapeData(int shapeID)
    {
        if (shapeMap.ContainsKey(shapeID))
        {
            removedShapeIDs.Enqueue(shapeID); // 🔹 삭제된 블록의 ID 저장
            shapeMap.Remove(shapeID);
            Debug.Log($"Shape ID {shapeID} removed and stored for reuse.");
        }
    }

    // 🔹 삭제된 블록이 존재하는지 확인하는 함수 추가
    public bool HasShapeData(int shapeID)
    {
        return shapeDataByID.ContainsKey(shapeID);
    }
}
