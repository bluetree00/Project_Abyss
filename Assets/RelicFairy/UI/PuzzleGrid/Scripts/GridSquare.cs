using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class GridSquare : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Grid Info")]
    // 이 칸의 행/열 인덱스
    public int row;
    public int col;
    // Shape를 놓을 수 있는 칸인지 여부 (false면 막힌 칸)
    public bool isPlaceable = true;
    // 이 칸이 속한 속성 존 코드(F/I/T/P/L/D, '+'=CENTER). '\0'=속성 없는 판(레거시 그리드).
    // 멀린 룬판이 셀을 만들 때 심는다 — 속성 배치 제약(RuneZoneRule)의 판정 근거.
    [HideInInspector] public char zoneCode;

    [Header("Background")]
    // 칸 기본 배경 이미지 (색으로 placeable/blocked 구분)
    public Image baseImage;
    public Color placeableColor = Color.white;
    public Color blockedColor = Color.gray;

    [Header("Visuals")]
    // 실제 블록이 놓였을 때 보여주는 이미지
    public Image activeImage;
    // 드래그 중, 이 칸 위에 놓을 수 있음을 보여주는 하이라이트 이미지
    public Image hoverImage;

    [Header("State")]
    // 이 칸에 현재 Shape가 점유 중인지
    public bool isOccupied = false;
    // 이 칸을 점유한 아이템 (썸네일 색상 구분용)
    [HideInInspector] public RuntimeItemData occupyingItem;
    // 하이라이트 켜짐 여부
    public bool isHighlighted = false;

    // Grid에서 초기화 시 호출
    public void Init(int r, int c, bool placeable)
    {
        row = r;
        col = c;
        isPlaceable = placeable;

        // 배경 색으로 놓을 수 있는 칸/없는 칸을 구분
        if (baseImage != null)
        {
            baseImage.color = isPlaceable ? placeableColor : blockedColor;
        }

        // 막힌 칸은 항상 비어 있고, 하이라이트도 꺼둔다
        if (!isPlaceable)
        {
            SetOccupied(false);
            SetHighlight(false);
        }
    }

    void Start()
    {
        // 점유 표시(activeImage)를 현재 상태에 맞춘다. 예전엔 무조건 false로 밀었는데,
        // 칸이 <b>비활성 계층</b>(닫힌 배치 화면)에서 만들어지면 Start가 패널을 처음 열 때까지
        // 늦춰져, 이어하기로 복원해 둔 점유가 그 시점에 통째로 지워졌다(룬은 판에 있는데 칸은 빈 것으로).
        SetOccupied(isOccupied);
        SetHighlight(false);
    }

    // 이 칸에 블록이 실제로 놓였는지 상태 및 activeImage 표시
    public void SetOccupied(bool occupied)
    {
        isOccupied = occupied;
        if (!occupied) occupyingItem = null;
        if (activeImage != null)
            activeImage.enabled = isOccupied;
    }

    // 드래그 중 하이라이트 표시 (hoverImage on/off)
    public void SetHighlight(bool on)
    {
        isHighlighted = on;
        if (hoverImage != null)
            hoverImage.enabled = isHighlighted;
    }

    // 셰이프 전체 단위 프리뷰 하이라이트 (색상 지정)
    public void SetPreviewHighlight(bool on, Color color)
    {
        isHighlighted = on;
        if (hoverImage != null)
        {
            hoverImage.enabled = on;
            if (on) hoverImage.color = color;
        }
    }

    // ── 클릭 배치 (드래그 없이 칸을 눌러 놓기) ────────────────
    // 드래그는 손이 큰 조작이라 다중 칸 룬을 정확히 얹기 어려웠다. 칸을 직접 누르는 쪽이
    // 판정이 명확해서, 보관함에서 룬을 고른 뒤 칸을 누르면 그 칸을 기준으로 놓인다.

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.dragging) return;   // 드래그 종료 클릭은 무시(기존 드래그 배치와 충돌 방지)
        GridManager.Instance?.NotifySquareClicked(this);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        GridManager.Instance?.NotifySquareHovered(this);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        GridManager.Instance?.NotifySquareHovered(null);
    }

    // 이 칸 위에 겹쳐져 있는 ShapeBlock 개수 (여러 조각이 동시에 겹칠 수 있으므로)
    int overlapCount = 0;

    // ShapeBlock이 이 칸의 Trigger 영역에 들어왔을 때
    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("ShapeBlock")) return;
        overlapCount++;
        // 셰이프 단위 프리뷰가 활성 중이면 물리 트리거 하이라이트를 건너뜀
        if (GridManager.Instance != null && GridManager.Instance.IsPreviewingShape) return;
        if (isPlaceable && !isOccupied)
            SetHighlight(true);
    }

    // ShapeBlock이 이 칸 위에 머무르는 동안 (안전하게 계속 켜두기)
    void OnTriggerStay2D(Collider2D other)
    {
        if (!other.CompareTag("ShapeBlock")) return;
        if (GridManager.Instance != null && GridManager.Instance.IsPreviewingShape) return;
        if (isPlaceable && !isOccupied)
            SetHighlight(true);
    }


    // ShapeBlock이 이 칸에서 완전히 나갔을 때
    void OnTriggerExit2D(Collider2D other)
    {
        overlapCount--;
        if (other.CompareTag("ShapeBlock"))
        {
            if (overlapCount <= 0)
            {
                overlapCount = 0;
                SetHighlight(false);
            }
        }
    }
}
