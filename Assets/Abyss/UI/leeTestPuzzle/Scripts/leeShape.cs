using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class leeShape : MonoBehaviour,IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    // 한 칸짜리 블록(ShapeBlock) 프리팹
    public GameObject shapeBlockPrefab;
    // 이 Shape를 구성하는 블록의 상대 좌표 목록 (예: (0,0), (1,0) ...)
    public List<Vector2Int> cellOffsets;
     // 셀 간격
    public float cellSize = 80f; 
    
    [Header("Drag Settings")]
    // 드래그 중에 살짝 키워서 피드백 주는 스케일
    public Vector3 selectedScale = new Vector3(1.1f, 1.1f, 1f);
    // 손가락/마우스보다 조금 위에 보이게 하는 오프셋
    public Vector2 pointerOffset = new Vector2(0f, 50f);

    private Canvas canvas;
    private RectTransform rectTransform;
    private Vector3 startPosition;
    private Vector3 startScale;
    private bool isDraggable = true;

    // 이 Shape가 현재 점유하고 있는 GridSquare 목록
    private List<leeGridSquare> occupiedSquares = new List<leeGridSquare>();
    public void SetOccupiedSquares(List<leeGridSquare> squares)
    {
        occupiedSquares = squares;
    }
    public List<leeGridSquare> GetOccupiedSquares()
    {
        return occupiedSquares;
    }

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();

        // 초기 위치/스케일 저장 (실패 시 복귀용)
        startPosition = rectTransform.position;
        startScale = rectTransform.localScale;
        
        // cellOffsets를 기준으로 자식 블록들을 생성
        BuildShapeBlocks();
    }
    
    // 현재 cellOffsets 정보를 기준으로 자식 ShapeBlock들을 만든다
    void BuildShapeBlocks()
    {
        // 기존 자식 있으면 정리
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }

        // 오프셋마다 블록 하나씩 생성
        foreach (var offset in cellOffsets)
        {
            var blockObj = Instantiate(shapeBlockPrefab, transform);
            var rt = blockObj.GetComponent<RectTransform>();
            // (0,0)을 기준으로 오른쪽/아래 방향으로 배치
            rt.anchoredPosition = new Vector2(offset.x * cellSize, offset.y * cellSize);
        }
    }
    public void OnPointerDown(PointerEventData eventData)
    {
        // 필요하면 클릭 시 효과 추가 가능
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!isDraggable) return;

        // 드래그를 시작할 때, 이전에 점유하고 있던 칸 비우기
        leeGridManager.Instance.ReleaseShape(this);

        // 드래그 중에는 약간 키워서 피드백
        rectTransform.localScale = selectedScale;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDraggable) return;

        // 스크린 좌표를 Canvas 로컬 좌표로 변환
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.transform as RectTransform,
            eventData.position,
            canvas.worldCamera,
            out localPoint
        );

        // Canvas 기준 좌표를 월드 좌표로 변환하고, 오프셋 적용
        Vector3 worldPos = canvas.transform.TransformPoint(localPoint);
        worldPos += (Vector3)pointerOffset;
        rectTransform.position = worldPos;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        
        if (!isDraggable) return;
        // 스케일 원래대로
        rectTransform.localScale = startScale;

        // Grid에 배치 시도
        if (!leeGridManager.Instance.TryPlaceShape(this))
        {
             // 실패하면 시작 위치로 복귀 (보드에서 제거된 상태)
            ReturnToStart();
        }
        else
        {
            // 성공 시에도 isDraggable은 그대로 둬서 재배치 가능
        }
    }

    public void ReturnToStart()
    {
        rectTransform.position = startPosition;
    }
}