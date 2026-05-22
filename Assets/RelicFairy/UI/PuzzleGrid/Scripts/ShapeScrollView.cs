using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shape 슬롯 패널을 스크롤 가능하게 만드는 컴포넌트.
///
/// [씬 설정 방법]
///   ShapeScrollView  ← 이 컴포넌트 부착 (ScrollRect, RectMask2D 자동 추가됨)
///   └── ShapeHost    ← BoardManager.shapeHost 와 동일한 오브젝트
///
/// [Inspector 설정]
///   - content          : BoardManager.shapeHost 와 동일한 RectTransform 할당
///   - verticalScrollbar: 직접 할당하거나 null 이면 ShapeScrollView 오른쪽에 자동 생성
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ScrollRect))]
[RequireComponent(typeof(RectMask2D))]
public class ShapeScrollView : MonoBehaviour
{
    [Tooltip("BoardManager.shapeHost 와 동일한 RectTransform.")]
    public RectTransform content;

    [Header("스크롤 설정")]
    [Tooltip("드래그 감도.")]
    public float scrollSensitivity = 15f;
    [Tooltip("관성 감속 계수 (0 = 관성 없음, 1 = 감속 없음).")]
    [Range(0f, 1f)]
    public float decelerationRate = 0.135f;

    [Header("뷰포트 위치/크기")]
    [Tooltip("true 이면 아래 값으로 이 오브젝트의 RectTransform을 덮어씀.\n" +
             "false 이면 씬에서 직접 설정한 RectTransform을 그대로 사용.")]
    public bool overrideRect = false;
    [Tooltip("anchorMin (overrideRect = true 일 때만 적용).")]
    public Vector2 viewportAnchorMin = new Vector2(0f, 0f);
    [Tooltip("anchorMax (overrideRect = true 일 때만 적용).")]
    public Vector2 viewportAnchorMax = new Vector2(0f, 1f);
    [Tooltip("pivot (overrideRect = true 일 때만 적용).")]
    public Vector2 viewportPivot = new Vector2(0f, 0.5f);
    [Tooltip("anchoredPosition — Shape 생성 영역의 화면 위치 (overrideRect = true 일 때만 적용).")]
    public Vector2 viewportPosition = new Vector2(0f, 0f);
    [Tooltip("sizeDelta — 가로폭, 세로폭. 세로 stretch 시 Y = 0 (overrideRect = true 일 때만 적용).")]
    public Vector2 viewportSizeDelta = new Vector2(200f, 0f);

    [Header("스크롤바")]
    [Tooltip("할당 시 ScrollRect에 연결. null 이면 아래 설정으로 자동 생성.")]
    public Scrollbar verticalScrollbar;
    [Tooltip("자동 생성 시 스크롤바 너비 (px).")]
    public float scrollbarWidth = 16f;
    [Tooltip("자동 생성 스크롤바의 anchoredPosition.")]
    public Vector2 scrollbarPosition = new Vector2(900f, 0f);
    [Tooltip("자동 생성 스크롤바의 localScale.")]
    public Vector3 scrollbarScale = new Vector3(1f, 5f, 1f);

    void Awake()
    {
        if (content == null)
        {
            Debug.LogError("[ShapeScrollView] content가 null입니다. Inspector에서 shapeHost를 할당하세요.");
            return;
        }

        // 뷰포트 RectTransform 덮어쓰기 (옵션)
        if (overrideRect)
        {
            var myRT              = (RectTransform)transform;
            myRT.anchorMin        = viewportAnchorMin;
            myRT.anchorMax        = viewportAnchorMax;
            myRT.pivot            = viewportPivot;
            myRT.anchoredPosition = viewportPosition;
            myRT.sizeDelta        = viewportSizeDelta;
        }

        var scrollRect = GetComponent<ScrollRect>();
        scrollRect.content           = content;
        scrollRect.viewport          = (RectTransform)transform;
        scrollRect.horizontal        = false;
        scrollRect.vertical          = true;
        scrollRect.movementType      = ScrollRect.MovementType.Elastic;
        scrollRect.elasticity        = 0.1f;
        scrollRect.inertia           = true;
        scrollRect.decelerationRate  = decelerationRate;
        scrollRect.scrollSensitivity = scrollSensitivity;

        // content 앵커를 상단으로 고정 (ScrollRect Content 표준 설정)
        content.anchorMin        = new Vector2(0f, 1f);
        content.anchorMax        = new Vector2(1f, 1f);
        content.pivot            = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
    }

    void Start()
    {
        if (content == null) return;

        // Start()에서 생성 — Canvas 레이아웃이 완료된 후 GetWorldCorners 로 위치 계산
        if (verticalScrollbar == null)
            verticalScrollbar = BuildScrollbar();

        if (verticalScrollbar != null)
        {
            var scrollRect = GetComponent<ScrollRect>();
            scrollRect.verticalScrollbar           = verticalScrollbar;
            // AutoHide: viewport 크기 고정 → 콘텐츠 크기 변화 시 normalizedPosition 드리프트 방지
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }
    }

    /// <summary>
    /// 스크롤바 UI 계층을 생성한다.
    /// scrollbarPosition / scrollbarScale Inspector 값으로 위치와 크기를 설정한다.
    /// </summary>
    private Scrollbar BuildScrollbar()
    {
        var sbRoot = new GameObject("Scrollbar Vertical",
            typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        sbRoot.transform.SetParent(transform.parent, false);
        var sbRT = (RectTransform)sbRoot.transform;

        // 앵커 기반 배치: GameplayRoot 기준으로 shapes 패널 우측 끝에 붙임
        // ShapeScrollView가 (0.63~0.94), 스크롤바는 그 바로 오른쪽 (0.937~0.955)
        sbRT.anchorMin        = new Vector2(0.937f, 0.05f);
        sbRT.anchorMax        = new Vector2(0.955f, 0.95f);
        sbRT.offsetMin        = Vector2.zero;
        sbRT.offsetMax        = Vector2.zero;
        sbRT.pivot            = new Vector2(0.5f, 0.5f);
        sbRoot.transform.localScale = Vector3.one;

        sbRoot.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.3f);

        // Sliding Area (핸들이 움직이는 영역)
        var slidingGo              = new GameObject("Sliding Area", typeof(RectTransform));
        slidingGo.transform.SetParent(sbRoot.transform, false);
        var slidingRT              = (RectTransform)slidingGo.transform;
        slidingRT.anchorMin        = Vector2.zero;
        slidingRT.anchorMax        = Vector2.one;
        slidingRT.sizeDelta        = new Vector2(-scrollbarWidth, -scrollbarWidth);
        slidingRT.anchoredPosition = Vector2.zero;

        // Handle
        var handleGo              = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleGo.transform.SetParent(slidingGo.transform, false);
        var handleRT              = (RectTransform)handleGo.transform;
        handleRT.anchorMin        = Vector2.zero;
        handleRT.anchorMax        = Vector2.one;
        handleRT.sizeDelta        = new Vector2(scrollbarWidth, scrollbarWidth);
        handleRT.anchoredPosition = Vector2.zero;

        handleGo.GetComponent<Image>().color = new Color(0.8f, 0.8f, 0.8f, 0.8f);

        var sb           = sbRoot.GetComponent<Scrollbar>();
        sb.targetGraphic = handleGo.GetComponent<Image>();
        sb.handleRect    = handleRT;
        sb.direction     = Scrollbar.Direction.BottomToTop;

        return sb;
    }
}
