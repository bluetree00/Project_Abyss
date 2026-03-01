using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class leeGridManager : MonoBehaviour
{

    // 싱글톤 인스턴스
    public static leeGridManager Instance { get; private set; }

    // 이 매니저가 제어하는 그리드
    public leeGrid grid;

    void Awake()
    {
         // 싱글톤 보장
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // Shape를 현재 위치에서 Grid 위에 배치할 수 있는지 시도
    public bool TryPlaceShape(leeShape shape)
    {
        var candidateSquares = new List<leeGridSquare>();

        // Shape의 자식 RectTransform들 (각 칸 블록들)
        var blocks = shape.GetComponentsInChildren<RectTransform>();
        int blockCount = 0;

        foreach (var block in blocks)
        {
            // 루트(Shape 자기 자신)는 제외
            if (block == shape.transform) continue;
            blockCount++;

            // 이 블록이 가장 가까운 GridSquare를 찾는다
            leeGridSquare square = FindGridSquareUnderPosition(block.position);

            // 보드 밖이거나 허용 거리 밖이면 배치 실패
            if (square == null)
            {
                return false;
            }

            // 막힌 칸이면 배치 불가
            if (!square.isPlaceable)
            {
                return false;
            }

            // 이미 다른 Shape가 점유한 칸이면 배치 불가
            if (square.isOccupied)
            {
                return false;
            }
            
            if (!candidateSquares.Contains(square))
                candidateSquares.Add(square);
        }

        // 여기까지 왔으면 이 Shape의 모든 블록을 배치해도 됨
        foreach (var sq in candidateSquares)
        {
            sq.SetOccupied(true);
            sq.SetHighlight(false);
        }

        // Shape 전체를 첫 번째 칸 위치로 스냅
        if (candidateSquares.Count > 0)
        {
            var first = candidateSquares[0];
            shape.transform.position = first.transform.position;
        }
        
        // 이 Shape가 점유하고 있는 칸 목록을 저장 (재배치/해제용)
        shape.SetOccupiedSquares(candidateSquares);

        // 모든 placeable 칸이 채워졌는지 검사 (퍼즐 완료 체크)
        CheckAllPlaceableFilled();

        return true;
    }

    // 월드 위치 기준으로 가장 가까운 GridSquare를 찾고,
    // 너무 멀면 null 반환 (보드에 안 올라온 것으로 처리)
    leeGridSquare FindGridSquareUnderPosition(Vector3 worldPos)
    {
        float minDist = float.MaxValue;
        leeGridSquare result = null;

        foreach (var sq in grid.GetGridSquares())
        {
            float d = Vector3.Distance(worldPos, sq.transform.position);
            if (d < minDist)
            {
                minDist = d;
                result = sq;
            }
        }
        if (result == null){return result;}
        var rt = result.GetComponent<RectTransform>();
        float cellSize = rt.rect.size.x * result.transform.lossyScale.x;

        // 셀 중심에서 일정 거리 이상 떨어져 있으면 "해당 칸 없음" 처리
        float maxAllowedDist = cellSize * 0.5f;
        if (minDist > maxAllowedDist){return null;}
        
        return result;
    }

    // 모든 placeable 칸이 채워졌는지 확인하고, 채워졌다면 로그 출력
    void CheckAllPlaceableFilled()
    {
        foreach (var sq in grid.GetGridSquares())
        {
            if (sq.isPlaceable && !sq.isOccupied)
            {
                // 하나라도 비어 있으면 아직 미완성
                return;
            }
        }
        // 여기까지 왔으면 모든 placeable 칸이 채워진 상태
        Debug.Log("모든 배치 가능한 칸이 채워졌습니다!");
        // 나중에 여기서 클리어 연출/다음 스테이지 등 이벤트 호출 가능
    }

    // Shape를 다시 드래그하기 시작할 때, 기존에 점유하고 있던 칸을 비워준다
    public void ReleaseShape(leeShape shape)
    {
        var squares = shape.GetOccupiedSquares();
        if (squares == null) return;

        foreach (var sq in squares)
        {
            if (sq != null)
            {
                sq.SetOccupied(false);
                sq.SetHighlight(false);
            }
        }
        // 이제 Shape는 어떤 칸도 점유하지 않는 상태
        shape.SetOccupiedSquares(new List<leeGridSquare>());
    }
}