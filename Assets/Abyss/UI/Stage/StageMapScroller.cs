using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 스테이지 맵 드래그 스크롤.
/// Awake에서 자동으로 MapContent 컨테이너를 생성하고
/// 맵 요소(MapPanel, 노드, 라인)를 하위로 이동시킨다.
/// DebugRunPanel 등 고정 UI는 제외.
///
/// 드래그로 맵을 자유롭게 이동하며, 관성(inertia)을 지원한다.
/// </summary>
public class StageMapScroller : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("스크롤 설정")]
    [Tooltip("맵 콘텐츠 높이 (화면보다 큰 값, 너비는 화면에 맞춤)")]
    [SerializeField] private float contentHeight = 5400f;

    [Header("관성")]
    [SerializeField] private bool useInertia = true;
    [SerializeField] private float decelerationRate = 0.135f;

    [Header("바운드")]
    [Tooltip("맵 가장자리에서 튕기는 정도 (0=없음)")]
    [SerializeField] private float elasticity = 0.1f;

    [Header("제외 대상")]
    [Tooltip("스크롤에 포함하지 않을 자식 이름 (DebugRunPanel 등)")]
    [SerializeField] private string[] excludeNames = { "DebugRunPanel" };

    // ── Private ──
    private RectTransform _contentRT;
    private RectTransform _viewportRT;
    private Vector2 _velocity;
    private bool _isDragging;
    private Vector2 _prevDragPos;

    private void Awake()
    {
        _viewportRT = GetComponent<RectTransform>();

        BuildContentContainer();

        // 노드 랜덤 배치 → 라인 생성 → 시작 노드 포커스
        var layout = GetComponent<StageNodeLayout>();
        if (layout != null)
            layout.ApplyLayout();

        var lineConnector = GetComponent<StageLineConnector>();
        if (lineConnector != null)
            lineConnector.Rebuild();

        FocusOnStartNode();
    }

    /// <summary>Start 카테고리 노드를 찾아서 초기 포커스.</summary>
    private void FocusOnStartNode()
    {
        if (_contentRT == null) return;

        var points = _contentRT.GetComponentsInChildren<StagePointUI>(true);
        foreach (var p in points)
        {
            // StagePointUI에 stageCategory 접근이 필요 → public 프로퍼티 추가 필요
            // 대안: AppBootstrapper.CurrentRun에서 StagePointManager.CurrentPointId로 찾기
        }

        // 현재 노드(보통 Start) 기준으로 포커스
        var run = AppBootstrapper.Instance?.CurrentRun;
        int currentId = run?.StagePointManager?.CurrentPointId ?? -1;

        if (currentId >= 0)
        {
            foreach (var p in points)
            {
                if (p.PointId == currentId)
                {
                    FocusOn(p.GetComponent<RectTransform>());
                    return;
                }
            }
        }

        // 런이 없으면 첫 번째 노드 기준
        if (points.Length > 0)
            FocusOn(points[0].GetComponent<RectTransform>());
    }

    private void Update()
    {
        if (_contentRT == null) return;

        // 관성
        if (!_isDragging && useInertia && _velocity.sqrMagnitude > 0.01f)
        {
            _velocity *= Mathf.Pow(decelerationRate, Time.unscaledDeltaTime);
            ApplyMovement(_velocity * Time.unscaledDeltaTime);
        }

        // 바운드 복귀
        if (!_isDragging && elasticity > 0f)
        {
            Vector2 clamped = ClampPosition(_contentRT.anchoredPosition);
            Vector2 diff = clamped - _contentRT.anchoredPosition;
            if (diff.sqrMagnitude > 0.01f)
                _contentRT.anchoredPosition = Vector2.Lerp(_contentRT.anchoredPosition, clamped, elasticity);
        }
    }

    // ── Drag Handlers ──

    public void OnBeginDrag(PointerEventData eventData)
    {
        _isDragging = true;
        _velocity = Vector2.zero;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _viewportRT, eventData.position, eventData.pressEventCamera, out _prevDragPos);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_contentRT == null) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _viewportRT, eventData.position, eventData.pressEventCamera, out Vector2 localPos);

        Vector2 delta = localPos - _prevDragPos;
        delta.x = 0f; // 수직 스크롤만 허용
        _prevDragPos = localPos;

        if (useInertia)
            _velocity = delta / Mathf.Max(Time.unscaledDeltaTime, 0.001f);

        ApplyMovement(delta);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _isDragging = false;
    }

    // ── Public ──

    /// <summary>특정 노드 위치로 맵을 이동 (포커스).</summary>
    public void FocusOn(RectTransform target, bool animate = false)
    {
        if (_contentRT == null || target == null) return;

        // 타겟의 Y위치를 뷰포트 중앙으로 이동 (X 고정)
        // 노드 anchoredPosition.y가 콘텐츠 중심 기준이므로 부호 반전
        Vector2 newPos = new Vector2(0f, -target.anchoredPosition.y);
        newPos = ClampPosition(newPos);

        if (animate)
        {
            // TODO: DOTween이나 UniTask로 부드러운 이동
            _contentRT.anchoredPosition = newPos;
        }
        else
        {
            _contentRT.anchoredPosition = newPos;
        }

        _velocity = Vector2.zero;
    }

    /// <summary>MapContent RectTransform 반환 (StageLineConnector 등에서 사용).</summary>
    public RectTransform ContentTransform => _contentRT;

    // ── Private Methods ──

    private void ApplyMovement(Vector2 delta)
    {
        Vector2 newPos = _contentRT.anchoredPosition + delta;

        // 소프트 바운드: 경계 넘어가면 저항
        Vector2 clamped = ClampPosition(newPos);
        Vector2 overflow = newPos - clamped;

        if (overflow.sqrMagnitude > 0.01f)
            newPos = clamped + overflow * 0.3f; // 30%만 허용 (탄성 느낌)

        _contentRT.anchoredPosition = newPos;
    }

    private Vector2 ClampPosition(Vector2 pos)
    {
        float viewH = _viewportRT.rect.height;

        // 콘텐츠 중심 기준 스크롤 범위: ±(contentHeight - viewH) / 2
        float scrollRange = Mathf.Max(0f, (contentHeight - viewH) * 0.5f);

        return new Vector2(0f, Mathf.Clamp(pos.y, -scrollRange, scrollRange));
    }

    private void BuildContentContainer()
    {
        // MapContent 컨테이너 생성
        var contentGO = new GameObject("MapContent", typeof(RectTransform));
        contentGO.transform.SetParent(transform, false);

        _contentRT = contentGO.GetComponent<RectTransform>();

        // 노드가 anchor (0.5, 0.5) 기준이므로 MapContent도 중앙 기준
        // → 노드의 anchoredPosition이 그대로 유지됨
        _contentRT.anchorMin = new Vector2(0.5f, 0.5f);
        _contentRT.anchorMax = new Vector2(0.5f, 0.5f);
        _contentRT.pivot = new Vector2(0.5f, 0.5f);

        float viewWidth = _viewportRT.rect.width > 0 ? _viewportRT.rect.width : 1920f;
        float viewHeight = _viewportRT.rect.height > 0 ? _viewportRT.rect.height : 1080f;
        _contentRT.sizeDelta = new Vector2(viewWidth, contentHeight);

        // 초기 위치: 맵 상단부터 시작
        float scrollRange = Mathf.Max(0f, (contentHeight - viewHeight) * 0.5f);
        _contentRT.anchoredPosition = new Vector2(0f, -scrollRange);

        // 기존 자식들을 MapContent 아래로 이동 (제외 대상 제외)
        var excludeSet = new System.Collections.Generic.HashSet<string>();
        if (excludeNames != null)
        {
            foreach (var n in excludeNames)
            {
                if (!string.IsNullOrEmpty(n))
                    excludeSet.Add(n.Trim());
            }
        }

        // 이동할 자식 목록 수집 (순회 중 SetParent 방지)
        var toMove = new System.Collections.Generic.List<Transform>();
        for (int i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            if (child == _contentRT.transform) continue;

            bool exclude = false;
            foreach (var exName in excludeSet)
            {
                if (child.name.Trim().StartsWith(exName))
                {
                    exclude = true;
                    break;
                }
            }

            if (!exclude)
                toMove.Add(child);
        }

        foreach (var child in toMove)
            child.SetParent(_contentRT, false);

        // StageLineConnector가 있으면 MapContent로 이동
        var lineConnector = GetComponent<StageLineConnector>();
        if (lineConnector != null)
        {
            // StageLineConnector의 transform 참조를 MapContent로 변경하기 위해
            // 컴포넌트를 MapContent로 이동 (Unity에서 컴포넌트 이동 불가 → 대신 알려줌)
            // StageLineConnector는 GetComponentsInChildren으로 노드를 찾으므로
            // MapContent의 자식인 노드들도 찾을 수 있음 (부모가 StageUI이므로)
            // 단, 선 생성 시 부모가 StageUI → MapContent 아래 노드와 앵커가 다를 수 있음
            // → StageLineConnector가 MapContent를 인식하도록 해야 함
        }

        // MapContent를 첫 번째 자식으로 (고정 UI보다 뒤에 렌더링)
        contentGO.transform.SetAsFirstSibling();

        // MapPanel 배경에서 드래그 이벤트를 받도록 연결
        var mapPanel = _contentRT.Find("MapPanel");
        if (mapPanel != null)
        {
            var fwd = mapPanel.gameObject.AddComponent<DragForwarder>();
            fwd.Target = this;
        }
    }
}

/// <summary>
/// 드래그 이벤트를 StageMapScroller로 전달하는 헬퍼.
/// MapPanel 배경에 붙여서 배경 드래그 시 스크롤이 동작하도록 한다.
/// </summary>
public class DragForwarder : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public StageMapScroller Target { get; set; }

    public void OnBeginDrag(PointerEventData eventData) => Target?.OnBeginDrag(eventData);
    public void OnDrag(PointerEventData eventData) => Target?.OnDrag(eventData);
    public void OnEndDrag(PointerEventData eventData) => Target?.OnEndDrag(eventData);
}
