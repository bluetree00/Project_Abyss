using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class Shape : MonoBehaviour, IPointerClickHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler
{
    public GameObject squareShapeImage; // 정사각형 모양의 프리팹
    public Vector3 shapeSelectedScale; // 드래그 중 선택된 상태의 크기
    public Vector2 offset = new Vector2(0f, 700f); // 드래그 시 위치 보정을 위한 오프셋

    [HideInInspector]
    public ShapeData CurrentShapeData; // ScriptableObject로 관리되는 ShapeData

    private List<GameObject> _currentShape = new List<GameObject>(); // 활성화된 정사각형 오브젝트를 저장할 리스트
    private Vector3 _shapeStartScale; // Shape의 초기 크기
    private RectTransform _transform; // 현재 오브젝트의 RectTransform
    private bool _shapeDraggable = true; // 드래그 가능 여부를 나타내는 플래그
    private Canvas _canvas; // 상위 Canvas를 참조
    private Vector3 _startPosition;
    private bool _shapeActive = true;

    public void Awake()
    {
        // 초기 크기와 RectTransform 설정
        _shapeStartScale = this.GetComponent<RectTransform>().localScale;
        _transform = this.GetComponent<RectTransform>();
        _canvas = GetComponentInParent<Canvas>();
        _shapeDraggable = true;
        _startPosition = _transform.localPosition;
        _shapeActive = true;
    }

    public bool IsOnStartPosition()
    {
        return _transform.localPosition == _startPosition;
    }

    public bool IsAnyOfShapeSquareActive()
    {
        foreach (var square in _currentShape)
        {
            if(square.gameObject.activeSelf)
            {
                return true;
            }
        }

        return false;
    }

    public void DeactivateShape()
    {
        if(_shapeActive)
        {
            foreach (var square in _currentShape)
            {
                square?.GetComponent<ShapeSquare>().DeactivateShape();
            }
        }

        _shapeActive = false;
    }

    public void ActiveShape()
    {
        if(!_shapeActive)
        {
            foreach( var square in _currentShape)
            {
                square?.GetComponent<ShapeSquare>().ActiavateShape();
            }
        }

        _shapeActive = true;
    }

    void Start()
    {
        // 새로운 Shape 데이터를 기반으로 초기화
        RequestNewShape(CurrentShapeData);
    }

    // 새로운 Shape 요청
    public void RequestNewShape(ShapeData shapeData)
    {
        _transform.localPosition = _startPosition;
        CreateShape(shapeData);
    }

    // Shape를 생성
    public void CreateShape(ShapeData shapeData)
    {
        CurrentShapeData = shapeData;

        // 활성화할 정사각형 개수 계산
        var totalSquareNumber = GetNumberOfSquares(shapeData);

        // 리스트에 정사각형 오브젝트를 추가 (부족하면 생성)
        while (_currentShape.Count <= totalSquareNumber)
        {
            _currentShape.Add(Instantiate(squareShapeImage, transform));
        }

        // 모든 정사각형 오브젝트 초기화
        foreach (var square in _currentShape)
        {
            square.gameObject.transform.position = Vector3.zero;
            square.gameObject.SetActive(false);
        }

        // 정사각형 오브젝트의 크기 계산
        var squareRect = squareShapeImage.GetComponent<RectTransform>();
        var moveDistance = new Vector2(squareRect.rect.width * squareRect.localScale.x,
                                       squareRect.rect.height * squareRect.localScale.y);

        int currentIndexInList = 0;

        // 행과 열을 기반으로 정사각형 배치
        for (var row = 0; row < shapeData.rows; row++)
        {
            for (var column = 0; column < shapeData.columns; column++)
            {
                if (shapeData.board[row].column[column])
                {
                    _currentShape[currentIndexInList].SetActive(true); // 활성화
                    _currentShape[currentIndexInList].GetComponent<RectTransform>().localPosition =
                        new Vector2(GetXpositionForShapeSquare(shapeData, column, moveDistance),
                                    GetYPositionForShapeSquare(shapeData, row, moveDistance)); // 위치 설정

                    currentIndexInList++;
                }
            }
        }
    }

    // 정사각형의 Y 좌표 계산
    private float GetYPositionForShapeSquare(ShapeData shapeData, int row, Vector2 moveDistance)
    {
        float shiftOnY = 0f;

        if (shapeData.rows > 1)
        {
            if (shapeData.rows % 2 != 0) // 행 개수가 홀수인 경우
            {
                var middleSquareIndex = (shapeData.rows - 1) / 2;

                shiftOnY = (row - middleSquareIndex) * moveDistance.y;
            }
            else // 행 개수가 짝수인 경우
            {
                var middleSquareIndex1 = (shapeData.rows / 2) - 1;
                var middleSquareIndex2 = shapeData.rows / 2;

                shiftOnY = (row <= middleSquareIndex1 ? (row - middleSquareIndex1) : (row - middleSquareIndex2)) * moveDistance.y;
            }
        }

        return shiftOnY;
    }

    // 정사각형의 X 좌표 계산
    private float GetXpositionForShapeSquare(ShapeData shapeData, int column, Vector2 moveDistance)
    {
        float shiftOnX = 0f;

        if (shapeData.columns > 1)
        {
            if (shapeData.columns % 2 != 0) // 열 개수가 홀수인 경우
            {
                var middleSquareIndex = (shapeData.columns - 1) / 2;

                shiftOnX = (column - middleSquareIndex) * moveDistance.x;
            }
            else // 열 개수가 짝수인 경우
            {
                var middleSquareIndex1 = (shapeData.columns / 2) - 1;
                var middleSquareIndex2 = shapeData.columns / 2;

                shiftOnX = (column <= middleSquareIndex1 ? (column - middleSquareIndex1) : (column - middleSquareIndex2)) * moveDistance.x;
            }
        }

        return shiftOnX;
    }

    // 활성화된 정사각형 개수 계산
    private int GetNumberOfSquares(ShapeData shapeData)
    {
        int number = 0;

        foreach (var rowData in shapeData.board)
        {
            foreach (var active in rowData.column)
            {
                if (active) number++;
            }
        }

        return number;
    }

    // 마우스 클릭 이벤트 처리
    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log("Pointer Clicked");
    }

    // 마우스 버튼 해제 이벤트 처리
    public void OnPointerUp(PointerEventData eventData)
    {
        Debug.Log("Pointer Up");
    }

    // 드래그 시작 이벤트 처리
    public void OnBeginDrag(PointerEventData eventData)
    {
        this.GetComponent<RectTransform>().localScale = shapeSelectedScale; // 크기를 선택 상태로 변경
    }

    // 드래그 중 이벤트 처리
    public void OnDrag(PointerEventData eventData)
    {
        Vector2 pos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvas.transform as RectTransform,
            eventData.position,
            _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Camera.main, // Render Mode에 따른 카메라 설정
            out pos);

        _transform.localPosition = pos + offset; // 위치 업데이트
    }

    // 드래그 종료 이벤트 처리
    public void OnEndDrag(PointerEventData eventData)
    {
        this.GetComponent<RectTransform>().localScale = _shapeStartScale; // 크기를 원래대로 복구
        GameEvent.CheckIfShapeCanBePlaced(); // 드래그된 Shape가 배치 가능한지 체크
    }

    // 마우스 버튼 클릭 이벤트 처리
    public void OnPointerDown(PointerEventData eventData)
    {
        Debug.Log("Pointer Down");
    }
}
