using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 멀린의 룬 그리드 패널 메인 컨트롤러.
///
/// ■ Canvas_Overlay/@Overlay 하위에 사전 배치 (DDOL UIRoot 소속).
/// ■ 단일 편집 화면: Header / LeftPanel / CenterPanel / RightPanel / Footer 레이아웃.
/// ■ 외부 호출(OpenPanel / ShowWithNewItem)로 열린다.
/// ■ GridManager.OnItemPlaced/Removed ↔ RunItemInventory.PlaceItem/UnplaceItem 브리지.
/// ■ [초기화] 버튼: 배치 전체 초기화. [완료] 버튼: 잔여 아이템 경고 후 닫힘.
/// </summary>
public sealed class UI_GridPanel : UI_Base
{
    // ── Static ──
    public static UI_GridPanel Instance { get; private set; }

    // ── Properties ──
    public RectTransform BoardContainer => _boardContainer;
    public bool IsOpen => _isOpen;

    // ── Private: Sub-views ──
    private CharacterInfoPanelView      _charInfoView;
    private MerlinRuneHexGridView       _hexGridView;
    private MerlinRuneSynergyStatusView _synergyStatusView;
    private StagingAreaView             _stagingArea;
    private ItemInfoPanel               _itemInfoPanel;

    // ── Private: Layout roots (코드로 생성) ──
    private RectTransform _headerRT;
    private RectTransform _mainAreaRT;
    private RectTransform _leftPanelRT;
    private RectTransform _centerPanelRT;
    private RectTransform _rightPanelRT;
    private RectTransform _footerRT;
    private RectTransform _boardContainer;

    // 센터: 상단 그리드 / 하단 존 시너지
    private RectTransform _hexGridRoot;
    private RectTransform _synergyStatusRoot;

    // 우측: 상단 스테이징(스크롤) / 하단 아이템정보 + 배치버튼
    private RectTransform _stagingScrollRT;
    private RectTransform _itemInfoRoot;
    private Button        _placeButton;

    // ── Private: Header buttons ──
    private Button  _backButton;
    private Button  _resetButton;
    private Button  _confirmButton;

    // ── Private: Footer ──
    private TMP_Text _footerActiveSynText;
    private TMP_Text _footerCellCountText;

    // ── Private: Confirm Dialog ──
    private GameObject _confirmDialog;
    private Button     _confirmDialogKeepBtn;
    private Button     _confirmDialogDiscardBtn;
    private TMP_Text   _confirmDialogText;

    // ── Private: Synergy Toast ──
    private GameObject             _synergyToast;
    private TMP_Text               _synergyToastText;
    private CancellationTokenSource _toastCts;

    // ── Private: Reset Confirm Dialog ──
    private GameObject _resetDialog;
    private Button     _resetDialogYes;
    private Button     _resetDialogNo;

    // ── Private: Place Button ──
    private Image    _placeBG;
    private TMP_Text _placeLabel;

    // ── Private: Hex Grid Hint ──
    private TMP_Text _hexGridHintText;

    // ── Private: Footer Center Bonus ──
    private TMP_Text _footerCenterText;

    // ── Private: Fade ──
    private CanvasGroup _canvasGroup;

    // ── Private: State ──
    private RunItemInventory _inventory;
    private RuntimeItemData  _pendingNewItem;
    private bool             _isOpen;
    private bool             _layoutBuilt;
    private int              _totalPlacedCells;

    // 아이템 배치 위치 캐시: instanceId → 헥사 그리드 셀 좌표
    // HandleItemRemoved 에서 GridManager 의존 없이 직접 제거에 사용
    private readonly Dictionary<string, Vector2Int[]> _placedItemPositions = new();

    // ── Lifecycle ──

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        BuildLayout();
        BuildConfirmDialog();
        BuildSynergyToast();
        BuildResetConfirmDialog();

        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        BindInventory();

        if (GridManager.Instance != null)
        {
            GridManager.Instance.OnItemPlaced   += HandleItemPlaced;
            GridManager.Instance.OnItemRemoved  += HandleItemRemoved;
            GridManager.Instance.OnItemSelected += HandleItemSelected;
        }

        if (MerlinRuneBridge.Instance != null)
        {
            MerlinRuneBridge.Instance.OnSynergyActivated    += ShowSynergyActivated;
            MerlinRuneBridge.Instance.OnCenterBonusActivated += HandleCenterBonusActivated;
        }

        if (_resetDialogYes != null) _resetDialogYes.onClick.AddListener(OnResetConfirmYes);
        if (_resetDialogNo  != null) _resetDialogNo.onClick.AddListener(OnResetConfirmNo);

        if (_stagingArea != null)
        {
            _stagingArea.OnItemSelected  += OnStagingItemSelected;
            _stagingArea.OnItemHovered   += OnStagingItemHovered;
            _stagingArea.OnItemUnhovered += OnStagingItemUnhovered;
        }

        if (_backButton    != null) _backButton.onClick.AddListener(OnBackClicked);
        if (_resetButton   != null) _resetButton.onClick.AddListener(OnResetClicked);
        if (_confirmButton != null) _confirmButton.onClick.AddListener(OnConfirmClicked);
        if (_placeButton   != null) _placeButton.onClick.AddListener(OnPlaceClicked);

        if (_confirmDialogKeepBtn    != null) _confirmDialogKeepBtn.onClick.AddListener(OnDialogKeep);
        if (_confirmDialogDiscardBtn != null) _confirmDialogDiscardBtn.onClick.AddListener(OnDialogDiscardAll);
    }

    private void OnDisable()
    {
        UnbindInventory();

        if (GridManager.Instance != null)
        {
            GridManager.Instance.OnItemPlaced   -= HandleItemPlaced;
            GridManager.Instance.OnItemRemoved  -= HandleItemRemoved;
            GridManager.Instance.OnItemSelected -= HandleItemSelected;
        }

        if (MerlinRuneBridge.Instance != null)
        {
            MerlinRuneBridge.Instance.OnSynergyActivated    -= ShowSynergyActivated;
            MerlinRuneBridge.Instance.OnCenterBonusActivated -= HandleCenterBonusActivated;
        }

        if (_resetDialogYes != null) _resetDialogYes.onClick.RemoveListener(OnResetConfirmYes);
        if (_resetDialogNo  != null) _resetDialogNo.onClick.RemoveListener(OnResetConfirmNo);

        if (_stagingArea != null)
        {
            _stagingArea.OnItemSelected  -= OnStagingItemSelected;
            _stagingArea.OnItemHovered   -= OnStagingItemHovered;
            _stagingArea.OnItemUnhovered -= OnStagingItemUnhovered;
        }

        _toastCts?.Cancel();

        if (_backButton    != null) _backButton.onClick.RemoveListener(OnBackClicked);
        if (_resetButton   != null) _resetButton.onClick.RemoveListener(OnResetClicked);
        if (_confirmButton != null) _confirmButton.onClick.RemoveListener(OnConfirmClicked);
        if (_placeButton   != null) _placeButton.onClick.RemoveListener(OnPlaceClicked);

        if (_confirmDialogKeepBtn    != null) _confirmDialogKeepBtn.onClick.RemoveListener(OnDialogKeep);
        if (_confirmDialogDiscardBtn != null) _confirmDialogDiscardBtn.onClick.RemoveListener(OnDialogDiscardAll);

        HideConfirmDialog();
    }

    // ── Public API ──

    public override void Open()
    {
        base.Open();
        OpenPanel();
    }

    public override void Close()
    {
        ClosePanel();
    }

    /// <summary>새로 획득한 아이템을 강조하며 패널을 연다. UI_ItemAcquisitionPopup [그리드 열기]에서 호출.</summary>
    public void ShowWithNewItem(RuntimeItemData newItem)
    {
        _pendingNewItem = newItem;
        OpenPanel();
    }

    // ── GridManager 브리지 (MerlinRuneBridge에서 boardManager 없을 때 참조) ──

    public void EnterGalleryMode()
    {
        // 단일 편집 화면으로 개편 후 갤러리 모드 없음 — 편집 화면 바로 진입
    }

    public void EnterEditMode(string gridId)
    {
        // 단일 편집 화면이므로 별도 편집 모드 전환 없음
        // GridManager에 편집 대상 gridId 전달 (기존 연동 유지)
    }

    // ── Open / Close ──

    private void OpenPanel()
    {
        _isOpen = true;
        Time.timeScale = 0f;
        gameObject.SetActive(true);
        FadeInAsync().Forget();

        var run   = GameRunBootstrapper.Instance?.Run;
        var stats = run?.Player?.RuntimeStats;

        _charInfoView?.SetContext(stats, run);

        // 헥사곤 그리드: 데이터 로드 후 빌드 + 드래그-앤-드롭 연동
        if (_hexGridView != null && Managers.RuneData != null && Managers.RuneData.IsInitialized)
        {
            _hexGridView.BuildGrid();

            // Puzzle 인스턴스(UI_GridPanel 루트 직속)가 헥사 셀 위에 렌더링되도록
            MerlinRuneBridge.Instance?.EnsureOnTop();

            var hexGrid = _hexGridView.HexGrid;
            if (hexGrid != null)
            {
                if (BoardManager.Instance != null)
                    BoardManager.Instance.EnterExternalGrid(hexGrid, hexGrid.gridAsset);
                else if (GridManager.Instance != null)
                    GridManager.Instance.SetActiveGrid(hexGrid);
            }

            // 초기 점유 상태 반영 (재오픈 시 이전 배치 복원)
            _hexGridView.RefreshOccupiedCells();
            _hexGridView.UpdateAdjacencyConstraints();
        }

        run?.EnterGridSynergy();

        BindInventory();
        _stagingArea?.Refresh(_inventory);

        RefreshSynergyStatus();
        RefreshFooter();
        RefreshInfoPanelDefault();
        UpdateHexGridHint();

        if (_pendingNewItem != null)
        {
            _itemInfoPanel?.ShowItem(_pendingNewItem, isNew: true);
            _pendingNewItem = null;
        }
    }

    private void ClosePanel()
    {
        _isOpen = false;
        Time.timeScale = 1f;
        HideConfirmDialog();
        GameRunBootstrapper.Instance?.Run?.ExitGridSynergy();
        gameObject.SetActive(false);
    }

    // ── Layout 빌드 ──

    /// <summary>
    /// Awake에서 1회 호출. 전체 패널 레이아웃을 코드로 생성한다.
    /// Canvas: 1920×1080, Scale With Screen Size, Match 0.5 기준.
    /// </summary>
    private void BuildLayout()
    {
        var rootRT = GetComponent<RectTransform>();
        if (rootRT == null) rootRT = gameObject.AddComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = rootRT.offsetMax = Vector2.zero;

        // CanvasGroup — 페이드인에 사용
        _canvasGroup = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

        // 전체 배경
        var bg = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
        bg.color = new Color(0.10f, 0.12f, 0.18f, 0.97f);

        BuildHeader();
        BuildMainArea();
        BuildFooter();
    }

    // Header (60px 고정, 상단)
    private void BuildHeader()
    {
        var headerGO = Go("Header");
        headerGO.transform.SetParent(transform, false);
        _headerRT = headerGO.GetComponent<RectTransform>();
        _headerRT.anchorMin        = new Vector2(0f, 1f);
        _headerRT.anchorMax        = new Vector2(1f, 1f);
        _headerRT.sizeDelta        = new Vector2(0f, 60f);
        _headerRT.anchoredPosition = new Vector2(0f, -30f);

        var hdrBG = headerGO.AddComponent<Image>();
        hdrBG.color = new Color(0.14f, 0.16f, 0.22f, 1f);

        // ← 뒤로가기 버튼 (좌측)
        _backButton = MakeButton(headerGO.transform, "BackBtn",
            new Vector2(0f, 0f), new Vector2(0f, 1f),
            new Vector2(60f, 0f), Vector2.zero,
            new Color(0.22f, 0.26f, 0.36f, 1f), "←");
        var backRT = _backButton.GetComponent<RectTransform>();
        backRT.pivot = new Vector2(0f, 0.5f);
        backRT.anchoredPosition = new Vector2(8f, 0f);
        backRT.sizeDelta = new Vector2(48f, -12f);
        backRT.anchorMin = new Vector2(0f, 0f);
        backRT.anchorMax = new Vector2(0f, 1f);

        // 타이틀 텍스트 (중앙)
        var titleGO = MakeTxt(headerGO.transform, "Title", "그리드 배치", 18f,
            new Color(0.88f, 0.92f, 1f, 1f), bold: true);
        var titleRT = titleGO.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0.3f, 0f);
        titleRT.anchorMax = new Vector2(0.7f, 1f);
        titleRT.sizeDelta = Vector2.zero;
        titleGO.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.Center;

        // [초기화] 버튼 (우측, 완료 버튼 왼쪽)
        _resetButton = MakeButton(headerGO.transform, "ResetBtn",
            new Vector2(1f, 0f), new Vector2(1f, 1f),
            new Vector2(90f, -14f), new Vector2(-106f, 0f),
            new Color(0.45f, 0.22f, 0.18f, 0.9f), "초기화");
        var resetRT = _resetButton.GetComponent<RectTransform>();
        resetRT.pivot           = new Vector2(1f, 0.5f);
        resetRT.anchoredPosition = new Vector2(-110f, 0f);

        // [완료 ✓] 버튼 (우측 끝)
        _confirmButton = MakeButton(headerGO.transform, "ConfirmBtn",
            new Vector2(1f, 0f), new Vector2(1f, 1f),
            new Vector2(96f, -14f), new Vector2(-8f, 0f),
            new Color(0.18f, 0.45f, 0.22f, 0.95f), "완료 ✓");
        var confirmRT = _confirmButton.GetComponent<RectTransform>();
        confirmRT.pivot = new Vector2(1f, 0.5f);
        confirmRT.anchorMin = new Vector2(1f, 0f);
        confirmRT.anchorMax = new Vector2(1f, 1f);
        confirmRT.sizeDelta = new Vector2(96f, -14f);
        confirmRT.anchoredPosition = new Vector2(-8f, 0f);
    }

    // MainArea — Header 하단 ~ Footer 상단
    private void BuildMainArea()
    {
        var mainGO = Go("MainArea");
        mainGO.transform.SetParent(transform, false);
        _mainAreaRT = mainGO.GetComponent<RectTransform>();
        _mainAreaRT.anchorMin        = new Vector2(0f, 0f);
        _mainAreaRT.anchorMax        = new Vector2(1f, 1f);
        _mainAreaRT.offsetMin        = new Vector2(0f, 50f);   // footer 50px
        _mainAreaRT.offsetMax        = new Vector2(0f, -60f);  // header 60px

        BuildLeftPanel(mainGO.transform);
        BuildCenterPanel(mainGO.transform);
        BuildRightPanel(mainGO.transform);
    }

    // LeftPanel (0% ~ 20%)
    private void BuildLeftPanel(Transform parent)
    {
        var go = Go("LeftPanel");
        go.transform.SetParent(parent, false);
        _leftPanelRT = go.GetComponent<RectTransform>();
        _leftPanelRT.anchorMin = new Vector2(0f,    0f);
        _leftPanelRT.anchorMax = new Vector2(0.20f, 1f);
        _leftPanelRT.offsetMin = _leftPanelRT.offsetMax = Vector2.zero;

        _charInfoView = CharacterInfoPanelView.Create(go.transform);
        if (_charInfoView != null)
        {
            var charRT = _charInfoView.GetComponent<RectTransform>();
            charRT.anchorMin = Vector2.zero;
            charRT.anchorMax = Vector2.one;
            charRT.offsetMin = charRT.offsetMax = Vector2.zero;
        }
    }

    // CenterPanel (20% ~ 75%)
    private void BuildCenterPanel(Transform parent)
    {
        var go = Go("CenterPanel");
        go.transform.SetParent(parent, false);
        _centerPanelRT = go.GetComponent<RectTransform>();
        _centerPanelRT.anchorMin = new Vector2(0.20f, 0f);
        _centerPanelRT.anchorMax = new Vector2(0.75f, 1f);
        _centerPanelRT.offsetMin = new Vector2(2f, 0f);
        _centerPanelRT.offsetMax = new Vector2(-2f, 0f);

        // 상단 72%: HexGrid (0.28 ~ 1.00)
        var hexRootGO = Go("HexGridRoot");
        hexRootGO.transform.SetParent(go.transform, false);
        _hexGridRoot = hexRootGO.GetComponent<RectTransform>();
        _hexGridRoot.anchorMin = new Vector2(0f, 0.28f);
        _hexGridRoot.anchorMax = new Vector2(1f, 1.00f);
        _hexGridRoot.offsetMin = _hexGridRoot.offsetMax = Vector2.zero;

        var hexBG = hexRootGO.AddComponent<Image>();
        hexBG.color = new Color(0.10f, 0.12f, 0.16f, 0.95f);

        _hexGridView = hexRootGO.AddComponent<MerlinRuneHexGridView>();

        // 드래그 힌트 (아이템 미배치 시 표시, CenterPanel 직속 → 최후 렌더 보장)
        var hintGO = MakeTxt(go.transform, "DragHint",
            "아이템 카드를 드래그해\n그리드에 배치하세요", 15f,
            new Color(0.62f, 0.68f, 0.85f, 0.45f));
        _hexGridHintText = hintGO.GetComponent<TMP_Text>();
        var hintRT = hintGO.GetComponent<RectTransform>();
        hintRT.anchorMin = new Vector2(0f, 0.28f);
        hintRT.anchorMax = new Vector2(1f, 1.00f);
        hintRT.offsetMin = hintRT.offsetMax = Vector2.zero;
        _hexGridHintText.alignment         = TextAlignmentOptions.Center;
        _hexGridHintText.enableWordWrapping = true;

        // BoardContainer: GridManager/MerlinRuneBridge와 연동하는 영역
        var boardGO = Go("BoardContainer");
        boardGO.transform.SetParent(hexRootGO.transform, false);
        _boardContainer = boardGO.GetComponent<RectTransform>();
        _boardContainer.anchorMin = Vector2.zero;
        _boardContainer.anchorMax = Vector2.one;
        _boardContainer.offsetMin = _boardContainer.offsetMax = Vector2.zero;

        // 하단 27%: 연결 클러스터 시너지 패널 (0.00 ~ 0.27)
        var synRootGO = Go("SynergyStatusRoot");
        synRootGO.transform.SetParent(go.transform, false);
        _synergyStatusRoot = synRootGO.GetComponent<RectTransform>();
        _synergyStatusRoot.anchorMin = new Vector2(0f, 0.00f);
        _synergyStatusRoot.anchorMax = new Vector2(1f, 0.27f);
        _synergyStatusRoot.offsetMin = new Vector2(0f, 2f);
        _synergyStatusRoot.offsetMax = Vector2.zero;

        _synergyStatusView = synRootGO.AddComponent<MerlinRuneSynergyStatusView>();
    }

    // RightPanel (75% ~ 100%)
    private void BuildRightPanel(Transform parent)
    {
        var go = Go("RightPanel");
        go.transform.SetParent(parent, false);
        _rightPanelRT = go.GetComponent<RectTransform>();
        _rightPanelRT.anchorMin = new Vector2(0.75f, 0f);
        _rightPanelRT.anchorMax = new Vector2(1.00f, 1f);
        _rightPanelRT.offsetMin = new Vector2(2f, 0f);
        _rightPanelRT.offsetMax = Vector2.zero;

        var rightBG = go.AddComponent<Image>();
        rightBG.color = new Color(0.12f, 0.14f, 0.20f, 0.95f);

        // 상단 70%: StagingAreaView (스크롤)
        BuildStagingScrollArea(go.transform);

        // 하단 30%: ItemInfoPanel + 배치 버튼
        BuildItemInfoArea(go.transform);
    }

    private void BuildStagingScrollArea(Transform parent)
    {
        var scrollGO = Go("StagingScroll");
        scrollGO.transform.SetParent(parent, false);
        _stagingScrollRT = scrollGO.GetComponent<RectTransform>();
        _stagingScrollRT.anchorMin = new Vector2(0f, 0.27f);
        _stagingScrollRT.anchorMax = new Vector2(1f, 0.62f);
        _stagingScrollRT.offsetMin = new Vector2(4f, 4f);
        _stagingScrollRT.offsetMax = new Vector2(-4f, -4f);

        var scrollBG = scrollGO.AddComponent<Image>();
        scrollBG.color = new Color(0.10f, 0.12f, 0.18f, 0.75f);

        // "보관함" 레이블
        var lblGO = MakeTxt(scrollGO.transform, "StagingLabel", "아이템 목록", 15f,
            new Color(0.82f, 0.87f, 1.00f, 1f));
        var lblRT = lblGO.GetComponent<RectTransform>();
        lblRT.anchorMin = new Vector2(0f, 1f);
        lblRT.anchorMax = new Vector2(1f, 1f);
        lblRT.sizeDelta = new Vector2(0f, 22f);
        lblRT.anchoredPosition = new Vector2(0f, -11f);
        lblGO.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.Center;

        // Scroll viewport (레이블 아래)
        var viewportGO = Go("Viewport");
        viewportGO.transform.SetParent(scrollGO.transform, false);
        var viewportRT = viewportGO.GetComponent<RectTransform>();
        viewportRT.anchorMin = new Vector2(0f, 0f);
        viewportRT.anchorMax = new Vector2(1f, 1f);
        viewportRT.offsetMin = new Vector2(0f, 0f);
        viewportRT.offsetMax = new Vector2(0f, -24f);
        viewportGO.AddComponent<RectMask2D>();

        var scrollRect = scrollGO.AddComponent<ScrollRect>();
        scrollRect.horizontal        = true;
        scrollRect.vertical          = false;
        scrollRect.scrollSensitivity = 30f;
        scrollRect.movementType      = ScrollRect.MovementType.Clamped;
        scrollRect.inertia           = true;

        // ScrollContent — StagingAreaView가 가로 방향으로 슬롯을 배치하므로 좌측 앵커
        var contentGO = Go("ScrollContent");
        contentGO.transform.SetParent(viewportGO.transform, false);
        var contentRT = contentGO.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0f, 0f);
        contentRT.anchorMax = new Vector2(0f, 1f);
        contentRT.pivot     = new Vector2(0f, 0.5f);
        contentRT.offsetMin = contentRT.offsetMax = Vector2.zero;

        scrollRect.content  = contentRT;
        scrollRect.viewport = viewportRT;

        // StagingAreaView 추가 후 Init으로 scrollContent 전달 + 슬롯 빌드
        _stagingArea = scrollGO.AddComponent<StagingAreaView>();
        _stagingArea.Init(contentRT);
    }

    /// <summary>
    /// ItemInfoPanel의 필수 SerializeField를 코드로 생성한 UI 오브젝트로 주입한다.
    /// Inspector 연결 없이 동작하도록 최소 UI 구조를 빌드한다.
    /// </summary>
    private void BuildAndInjectItemInfoPanelFields(GameObject root)
    {
        if (_itemInfoPanel == null) return;

        var rf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var type = typeof(ItemInfoPanel);

        // CanvasGroup
        var cg = root.AddComponent<CanvasGroup>();
        type.GetField("canvasGroup", rf)?.SetValue(_itemInfoPanel, cg);

        // itemRoot: 아이템 표시 영역
        var itemRootGO = new GameObject("ItemRoot", typeof(RectTransform));
        itemRootGO.transform.SetParent(root.transform, false);
        var itemRootRT = itemRootGO.GetComponent<RectTransform>();
        itemRootRT.anchorMin = new Vector2(0f, 0.10f);
        itemRootRT.anchorMax = new Vector2(1f, 0.92f);
        itemRootRT.offsetMin = new Vector2(4f, 0f);
        itemRootRT.offsetMax = new Vector2(-4f, 0f);
        type.GetField("itemRoot", rf)?.SetValue(_itemInfoPanel, itemRootGO);

        // itemIcon
        var iconGO = new GameObject("ItemIcon", typeof(RectTransform));
        iconGO.transform.SetParent(itemRootGO.transform, false);
        var iconRT = iconGO.GetComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0f, 0.62f);
        iconRT.anchorMax = new Vector2(1f, 1f);
        iconRT.sizeDelta = Vector2.zero;
        var iconImg = iconGO.AddComponent<Image>();
        iconImg.raycastTarget = false;
        type.GetField("itemIcon", rf)?.SetValue(_itemInfoPanel, iconImg);

        // rarityBar
        var rarityBarGO = new GameObject("RarityBar", typeof(RectTransform));
        rarityBarGO.transform.SetParent(itemRootGO.transform, false);
        var rarityBarRT = rarityBarGO.GetComponent<RectTransform>();
        rarityBarRT.anchorMin = new Vector2(0f, 1f);
        rarityBarRT.anchorMax = new Vector2(1f, 1f);
        rarityBarRT.sizeDelta = new Vector2(0f, 4f);
        rarityBarRT.anchoredPosition = Vector2.zero;
        var rarityBarImg = rarityBarGO.AddComponent<Image>();
        rarityBarImg.raycastTarget = false;
        type.GetField("rarityBar", rf)?.SetValue(_itemInfoPanel, rarityBarImg);

        // itemName
        var nameTxtGO = MakeTxt(itemRootGO.transform, "ItemName", "", 15f,
            new Color(0.95f, 0.97f, 1f, 1f), bold: true);
        var nameRT = nameTxtGO.GetComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0f, 0.48f);
        nameRT.anchorMax = new Vector2(1f, 0.62f);
        nameRT.offsetMin = nameRT.offsetMax = Vector2.zero;
        var nameTxt = nameTxtGO.GetComponent<TMP_Text>();
        nameTxt.enableWordWrapping = false;
        nameTxt.alignment = TextAlignmentOptions.MidlineLeft;
        type.GetField("itemName", rf)?.SetValue(_itemInfoPanel, nameTxt);

        // rarityText
        var rarityTxtGO = MakeTxt(itemRootGO.transform, "RarityText", "", 12.5f,
            new Color(0.78f, 0.78f, 0.88f, 1f));
        var rarityTxtRT = rarityTxtGO.GetComponent<RectTransform>();
        rarityTxtRT.anchorMin = new Vector2(0f, 0.38f);
        rarityTxtRT.anchorMax = new Vector2(1f, 0.48f);
        rarityTxtRT.offsetMin = rarityTxtRT.offsetMax = Vector2.zero;
        type.GetField("rarityText", rf)?.SetValue(_itemInfoPanel, rarityTxtGO.GetComponent<TMP_Text>());

        // effectListRoot
        var effectListGO = new GameObject("EffectList", typeof(RectTransform));
        effectListGO.transform.SetParent(itemRootGO.transform, false);
        var effectListRT = effectListGO.GetComponent<RectTransform>();
        effectListRT.anchorMin = new Vector2(0f, 0f);
        effectListRT.anchorMax = new Vector2(1f, 0.36f);
        effectListRT.offsetMin = effectListRT.offsetMax = Vector2.zero;
        var vlgEffect = effectListGO.AddComponent<VerticalLayoutGroup>();
        vlgEffect.childAlignment       = TextAnchor.UpperLeft;
        vlgEffect.spacing              = 2f;
        vlgEffect.childControlWidth    = true;
        vlgEffect.childControlHeight   = false;
        vlgEffect.childForceExpandWidth  = true;
        vlgEffect.childForceExpandHeight = false;
        type.GetField("effectListRoot", rf)?.SetValue(_itemInfoPanel, effectListGO.transform);

        // shapePreviewRoot
        var shapePreviewGO = new GameObject("ShapePreview", typeof(RectTransform));
        shapePreviewGO.transform.SetParent(itemRootGO.transform, false);
        var shapePreviewRT = shapePreviewGO.GetComponent<RectTransform>();
        shapePreviewRT.anchorMin = new Vector2(0.6f, 0.60f);
        shapePreviewRT.anchorMax = new Vector2(1.0f, 1.00f);
        shapePreviewRT.offsetMin = shapePreviewRT.offsetMax = Vector2.zero;
        type.GetField("shapePreviewRoot", rf)?.SetValue(_itemInfoPanel, shapePreviewRT);

        // emptyRoot + emptyText
        var emptyRootGO = new GameObject("EmptyRoot", typeof(RectTransform));
        emptyRootGO.transform.SetParent(root.transform, false);
        var emptyRootRT = emptyRootGO.GetComponent<RectTransform>();
        emptyRootRT.anchorMin = Vector2.zero;
        emptyRootRT.anchorMax = Vector2.one;
        emptyRootRT.offsetMin = emptyRootRT.offsetMax = Vector2.zero;
        type.GetField("emptyRoot", rf)?.SetValue(_itemInfoPanel, emptyRootGO);

        var emptyTxtGO = MakeTxt(emptyRootGO.transform, "EmptyText", "아이템을 선택하세요", 12f,
            new Color(0.45f, 0.48f, 0.58f, 0.8f));
        var emptyTxtRT = emptyTxtGO.GetComponent<RectTransform>();
        emptyTxtRT.anchorMin = Vector2.zero;
        emptyTxtRT.anchorMax = Vector2.one;
        emptyTxtRT.offsetMin = emptyTxtRT.offsetMax = Vector2.zero;
        emptyTxtGO.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.Center;
        type.GetField("emptyText", rf)?.SetValue(_itemInfoPanel, emptyTxtGO.GetComponent<TMP_Text>());

        // newBadge
        var newBadgeGO = new GameObject("NewBadge", typeof(RectTransform));
        newBadgeGO.transform.SetParent(itemRootGO.transform, false);
        var newBadgeRT = newBadgeGO.GetComponent<RectTransform>();
        newBadgeRT.anchorMin        = new Vector2(0f, 1f);
        newBadgeRT.anchorMax        = new Vector2(0f, 1f);
        newBadgeRT.pivot            = new Vector2(0f, 1f);
        newBadgeRT.sizeDelta        = new Vector2(36f, 16f);
        newBadgeRT.anchoredPosition = new Vector2(2f, -2f);
        var newBadgeBG = newBadgeGO.AddComponent<Image>();
        newBadgeBG.color = new Color(1f, 0.3f, 0.3f, 0.95f);
        var newBadgeTxtGO = MakeTxt(newBadgeGO.transform, "Label", "NEW", 8f, Color.white, bold: true);
        var newBadgeTxtRT = newBadgeTxtGO.GetComponent<RectTransform>();
        newBadgeTxtRT.anchorMin = Vector2.zero;
        newBadgeTxtRT.anchorMax = Vector2.one;
        newBadgeTxtRT.sizeDelta = Vector2.zero;
        newBadgeTxtGO.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.Center;
        type.GetField("newBadge", rf)?.SetValue(_itemInfoPanel, newBadgeGO);
    }

    private void BuildItemInfoArea(Transform parent)
    {
        var infoRootGO = Go("ItemInfoRoot");
        infoRootGO.transform.SetParent(parent, false);
        _itemInfoRoot = infoRootGO.GetComponent<RectTransform>();
        _itemInfoRoot.anchorMin = new Vector2(0f, 0f);
        _itemInfoRoot.anchorMax = new Vector2(1f, 0.27f);
        _itemInfoRoot.offsetMin = new Vector2(4f, 4f);
        _itemInfoRoot.offsetMax = new Vector2(-4f, -4f);

        var infoBG = infoRootGO.AddComponent<Image>();
        infoBG.color = new Color(0.11f, 0.13f, 0.19f, 0.90f);

        // "선택:" 레이블
        var selLbl = MakeTxt(infoRootGO.transform, "SelectLabel", "선택:", 11f,
            new Color(0.55f, 0.60f, 0.75f, 1f));
        var selRT = selLbl.GetComponent<RectTransform>();
        selRT.anchorMin = new Vector2(0f, 1f);
        selRT.anchorMax = new Vector2(1f, 1f);
        selRT.sizeDelta = new Vector2(0f, 18f);
        selRT.anchoredPosition = new Vector2(0f, -9f);

        // ItemInfoPanel 컴포넌트 추가 (필수 SerializeField를 코드로 초기화)
        _itemInfoPanel = infoRootGO.AddComponent<ItemInfoPanel>();
        BuildAndInjectItemInfoPanelFields(infoRootGO);

        // [배치하기] 버튼 (하단)
        var placeGO = new GameObject("PlaceBtn", typeof(RectTransform));
        placeGO.transform.SetParent(infoRootGO.transform, false);
        var placeRT = placeGO.GetComponent<RectTransform>();
        placeRT.anchorMin        = new Vector2(0.05f, 0f);
        placeRT.anchorMax        = new Vector2(0.95f, 0f);
        placeRT.pivot            = new Vector2(0.5f, 0f);
        placeRT.sizeDelta        = new Vector2(0f, 34f);
        placeRT.anchoredPosition = new Vector2(0f, 4f);

        _placeBG      = placeGO.AddComponent<Image>();
        _placeBG.color = new Color(0.20f, 0.28f, 0.40f, 0.65f);
        _placeButton  = placeGO.AddComponent<Button>();
        _placeButton.targetGraphic = _placeBG;

        var placeTxtGO = MakeTxt(placeGO.transform, "PlaceLabel", "드래그로 배치", 13f,
            new Color(0.7f, 0.78f, 0.90f, 0.8f), bold: true);
        var placeTxtRT = placeTxtGO.GetComponent<RectTransform>();
        placeTxtRT.anchorMin = Vector2.zero;
        placeTxtRT.anchorMax = Vector2.one;
        placeTxtRT.sizeDelta = Vector2.zero;
        _placeLabel = placeTxtGO.GetComponent<TMP_Text>();
        _placeLabel.alignment = TextAlignmentOptions.Center;
    }

    // Footer (50px 고정, 하단)
    private void BuildFooter()
    {
        var footerGO = Go("Footer");
        footerGO.transform.SetParent(transform, false);
        _footerRT = footerGO.GetComponent<RectTransform>();
        _footerRT.anchorMin        = new Vector2(0f, 0f);
        _footerRT.anchorMax        = new Vector2(1f, 0f);
        _footerRT.sizeDelta        = new Vector2(0f, 50f);
        _footerRT.anchoredPosition = new Vector2(0f, 25f);

        var footerBG = footerGO.AddComponent<Image>();
        footerBG.color = new Color(0.10f, 0.12f, 0.16f, 0.98f);

        // 활성 시너지 텍스트 (좌측 69%)
        var actGO = MakeTxt(footerGO.transform, "ActiveSyn",
            "활성: —          미달성: —", 12f, new Color(0.75f, 0.88f, 1f, 1f));
        var actRT = actGO.GetComponent<RectTransform>();
        actRT.anchorMin = new Vector2(0f, 0f);
        actRT.anchorMax = new Vector2(0.69f, 1f);
        actRT.offsetMin = new Vector2(12f, 0f);
        actRT.offsetMax = new Vector2(-4f, 0f);
        _footerActiveSynText = actGO.GetComponent<TMP_Text>();
        _footerActiveSynText.enableWordWrapping = false;
        _footerActiveSynText.alignment = TextAlignmentOptions.MidlineLeft;

        // CENTER 보너스 표시 (중간 11%)
        var centerGO = MakeTxt(footerGO.transform, "CenterBonus", "", 11f,
            new Color(0.50f, 0.55f, 0.70f, 0.6f));
        var centerRT = centerGO.GetComponent<RectTransform>();
        centerRT.anchorMin = new Vector2(0.69f, 0f);
        centerRT.anchorMax = new Vector2(0.82f, 1f);
        centerRT.offsetMin = centerRT.offsetMax = Vector2.zero;
        _footerCenterText = centerGO.GetComponent<TMP_Text>();
        _footerCenterText.alignment         = TextAlignmentOptions.Center;
        _footerCenterText.enableWordWrapping = false;

        // 셀 카운트 (우측 18%)
        var cntGO = MakeTxt(footerGO.transform, "CellCount", "0/20 셀 배치됨", 12f,
            new Color(0.65f, 0.70f, 0.85f, 1f));
        var cntRT = cntGO.GetComponent<RectTransform>();
        cntRT.anchorMin = new Vector2(0.82f, 0f);
        cntRT.anchorMax = new Vector2(1.00f, 1f);
        cntRT.offsetMin = new Vector2(0f, 0f);
        cntRT.offsetMax = new Vector2(-12f, 0f);
        _footerCellCountText = cntGO.GetComponent<TMP_Text>();
        _footerCellCountText.alignment = TextAlignmentOptions.MidlineRight;
    }

    // ── Confirm Dialog ──

    private void BuildConfirmDialog()
    {
        _confirmDialog = new GameObject("ConfirmDialog", typeof(RectTransform));
        _confirmDialog.transform.SetParent(transform, false);

        var rt = _confirmDialog.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;

        var overlay = _confirmDialog.AddComponent<Image>();
        overlay.color         = new Color(0f, 0f, 0f, 0.60f);
        overlay.raycastTarget = true;

        var panelGO = new GameObject("Panel", typeof(RectTransform));
        panelGO.transform.SetParent(_confirmDialog.transform, false);
        var panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRT.pivot            = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta        = new Vector2(340f, 130f);
        panelRT.anchoredPosition = Vector2.zero;
        panelGO.AddComponent<Image>().color = new Color(0.10f, 0.11f, 0.17f, 0.98f);

        var textGO = MakeTxt(panelGO.transform, "Text",
            "보관함 아이템이 남아 있습니다.", 13f, new Color(0.9f, 0.92f, 1f, 1f));
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0f, 0.42f);
        textRT.anchorMax = new Vector2(1f, 1f);
        textRT.sizeDelta = Vector2.zero;
        _confirmDialogText = textGO.GetComponent<TMP_Text>();
        _confirmDialogText.alignment     = TextAlignmentOptions.Center;
        _confirmDialogText.enableWordWrapping = true;

        // [계속 배치] 버튼
        _confirmDialogKeepBtn = MakeButton(panelGO.transform, "KeepBtn",
            new Vector2(0.08f, 0f), new Vector2(0.45f, 0.38f),
            Vector2.zero, new Vector2(0f, 4f),
            new Color(0.25f, 0.27f, 0.35f, 1f), "계속 배치");

        // [폐기 후 종료] 버튼
        _confirmDialogDiscardBtn = MakeButton(panelGO.transform, "DiscardBtn",
            new Vector2(0.55f, 0f), new Vector2(0.92f, 0.38f),
            Vector2.zero, new Vector2(0f, 4f),
            new Color(0.72f, 0.18f, 0.18f, 1f), "폐기 후 종료");

        _confirmDialog.SetActive(false);
    }

    private void ShowConfirmDialog()
    {
        if (_confirmDialog != null) _confirmDialog.SetActive(true);
    }

    private void HideConfirmDialog()
    {
        if (_confirmDialog != null) _confirmDialog.SetActive(false);
    }

    // ── Synergy Toast ──

    private void BuildSynergyToast()
    {
        _synergyToast = new GameObject("SynergyToast", typeof(RectTransform));
        _synergyToast.transform.SetParent(transform, false);

        var rt = _synergyToast.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.21f, 0.87f);
        rt.anchorMax        = new Vector2(0.74f, 0.95f);
        rt.offsetMin        = Vector2.zero;
        rt.offsetMax        = Vector2.zero;

        _synergyToast.AddComponent<Image>().color = new Color(0.04f, 0.14f, 0.08f, 0.93f);

        var textGO = MakeTxt(_synergyToast.transform, "Text",
            "", 14f, new Color(0.3f, 1f, 0.55f, 1f));
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.sizeDelta = new Vector2(-12f, -8f);
        _synergyToastText = textGO.GetComponent<TMP_Text>();
        _synergyToastText.alignment = TextAlignmentOptions.Center;
        _synergyToastText.enableWordWrapping = false;

        _synergyToast.SetActive(false);
    }

    private void ShowSynergyActivated(string description)
    {
        ShowSynergyToastAsync(description).Forget();
    }

    private async UniTaskVoid ShowSynergyToastAsync(string description)
    {
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        _toastCts = new CancellationTokenSource();
        var ct = _toastCts.Token;

        if (_synergyToastText != null)
            _synergyToastText.text = $"★ 시너지 활성화!  {description}";
        if (_synergyToast != null)
            _synergyToast.SetActive(true);

        try
        {
            await UniTask.Delay(2500, ignoreTimeScale: true, cancellationToken: ct);
        }
        catch (System.OperationCanceledException) { }

        if (_synergyToast != null)
            _synergyToast.SetActive(false);
    }

    // ── Inventory Bridge ──

    private void BindInventory()
    {
        var run = GameRunBootstrapper.Instance?.Run;
        if (run?.ItemInventory == null) return;
        if (_inventory == run.ItemInventory) return;

        UnbindInventory();
        _inventory = run.ItemInventory;
        _inventory.OnStagingChanged += OnStagingChanged;
        _inventory.OnPlacedChanged  += OnPlacedChanged;
    }

    private void UnbindInventory()
    {
        if (_inventory == null) return;
        _inventory.OnStagingChanged -= OnStagingChanged;
        _inventory.OnPlacedChanged  -= OnPlacedChanged;
        _inventory = null;
    }

    private void OnStagingChanged()
    {
        _stagingArea?.Refresh(_inventory);
        RefreshInfoPanelDefault();
        RefreshFooter();
        UpdatePlaceButtonState(_inventory?.StagingCount > 0);
    }

    private void OnPlacedChanged()
    {
        RefreshSynergyStatus();
        RefreshFooter();
    }

    // ── GridManager Event Bridge ──

    private void HandleItemPlaced(RuntimeItemData item)
    {
        Debug.Log($"[GridChk] 배치 수신: {item?.instanceId} (frame {Time.frameCount})");
        _inventory?.PlaceItem(item);
        _itemInfoPanel?.ShowItem(item, isNew: false);
        _totalPlacedCells += GetItemCellCount(item);
        UpdateHexGridHint();

        if (_hexGridView != null)
        {
            var shape = item != null
                ? BoardManager.Instance?.GetSharedShapeByItem(item.instanceId)
                : null;
            if (shape != null)
            {
                var occupiedSquares = shape.GetOccupiedSquares();

                // 배치 위치 캐시 (제거 시 GridManager 의존 없이 직접 반영하기 위해)
                if (item != null)
                {
                    var cachedPos = new Vector2Int[occupiedSquares.Count];
                    for (int i = 0; i < occupiedSquares.Count; i++)
                        cachedPos[i] = new Vector2Int(occupiedSquares[i].col, occupiedSquares[i].row);
                    _placedItemPositions[item.instanceId] = cachedPos;
                }

                _hexGridView.TriggerPlacementEffect(occupiedSquares);
            }
            else
            {
                _hexGridView.RefreshOccupiedCells();
            }

            _hexGridView.UpdateAdjacencyConstraints();
        }

        // hexgrid가 _occupiedPositions와 Bridge를 갱신한 뒤 시너지 뷰 갱신
        RefreshSynergyStatus();
        RefreshFooter();
    }

    private void HandleItemRemoved(RuntimeItemData item)
    {
        Debug.Log($"[GridChk] 해제 수신: {item?.instanceId} (frame {Time.frameCount})");
        _inventory?.UnplaceItem(item);
        _totalPlacedCells = Mathf.Max(0, _totalPlacedCells - GetItemCellCount(item));
        UpdateHexGridHint();

        if (_hexGridView != null)
        {
            // 캐시된 위치로 직접 제거 → GridManager 그리드 참조 타이밍 문제 없음
            if (item != null && _placedItemPositions.TryGetValue(item.instanceId, out var cached))
            {
                _hexGridView.RemovePlacedCells(cached);
                _placedItemPositions.Remove(item.instanceId);
            }
            else
            {
                _hexGridView.RefreshOccupiedCells();
            }
            _hexGridView.UpdateAdjacencyConstraints();
        }

        RefreshSynergyStatus();
        RefreshFooter();
    }

    private int GetItemCellCount(RuntimeItemData item)
    {
        if (item == null || item.shapeId == 0) return 1;
        var piece = Managers.RuneData?.GetPiece(item.shapeId);
        if (piece == null) return 1;
        return RuneDataManager.ParseCellOffsets(piece).Length;
    }

    private void HandleItemSelected(RuntimeItemData item)
    {
        _itemInfoPanel?.ShowItem(item, isNew: false);
    }

    private void OnStagingItemSelected(RuntimeItemData item)
    {
        _stagingArea?.HighlightItem(item);
        _itemInfoPanel?.ShowItem(item, isNew: false);
        UpdatePlaceButtonState(item != null);
    }

    private void OnStagingItemHovered(RuntimeItemData item)
    {
        _itemInfoPanel?.ShowItem(item, isNew: false);
    }

    private void OnStagingItemUnhovered()
    {
        RefreshInfoPanelDefault();
    }

    // ── Info Panel Default ──

    private void RefreshInfoPanelDefault()
    {
        if (_itemInfoPanel == null) return;
        bool hasItems = _inventory != null && _inventory.StagingCount > 0;
        if (hasItems)
        {
            var firstItem = _inventory.StagingItems[0];
            _itemInfoPanel.ShowItem(firstItem, isNew: false, slideIn: true);
            _stagingArea?.HighlightItem(firstItem);
        }
        else
        {
            _itemInfoPanel.ShowEmpty();
            _stagingArea?.HighlightItem(null);
        }
        UpdatePlaceButtonState(hasItems);
    }

    // ── Synergy Status Refresh ──

    private void RefreshSynergyStatus()
    {
        // 항상 최신 점유 상태에서 cluster를 계산한 뒤 갱신 (패널 재오픈 시 이전 결과 보존)
        _hexGridView?.RefreshOccupiedCells();
        _synergyStatusView?.Refresh(MerlinRuneBridge.Instance?.GetLastClusterSizes());
    }

    // ── Footer Refresh ──

    private void RefreshFooter()
    {
        if (_footerCellCountText != null)
            _footerCellCountText.SetText($"{_totalPlacedCells}/20 셀");

        if (_footerActiveSynText == null) return;

        var runeData = Managers.RuneData;
        if (runeData == null)
        {
            _footerActiveSynText.SetText("존 시너지: <color=#556677>데이터 로딩 중…</color>");
            return;
        }

        var clusterSizes = MerlinRuneBridge.Instance?.GetLastClusterSizes();

        var sb = new System.Text.StringBuilder("존 시너지  ");

        foreach (var zoneId in runeData.GetZoneIds())
        {
            var synergies = runeData.GetZoneSynergies(zoneId);
            if (synergies == null) continue;

            int count = (clusterSizes != null && clusterSizes.TryGetValue(zoneId, out var c)) ? c : 0;
            bool anyMet = false;
            foreach (var s in synergies)
                if (s.threshold > 0 && count >= s.threshold) { anyMet = true; break; }

            string hex = ElementDef.IdHex(zoneId);

            if (anyMet)
                sb.Append($"<color={hex}><b>●{zoneId}</b></color>  ");
            else
                sb.Append($"<color=#445566>○{zoneId}</color>  ");
        }

        _footerActiveSynText.SetText(sb.ToString().TrimEnd());

        if (_footerCenterText != null)
        {
            bool centerActive = MerlinRuneBridge.Instance?.IsCenterBonusActive ?? false;
            _footerCenterText.text  = centerActive ? "◉ 중앙 보너스" : "◎ 중앙";
            _footerCenterText.color = centerActive
                ? new Color(1.00f, 0.88f, 0.40f, 0.95f)
                : new Color(0.50f, 0.55f, 0.70f, 0.55f);
        }
    }

    // ── Button Handlers ──

    private void OnBackClicked()
    {
        ClosePanel();
    }

    private void OnResetClicked()
    {
        if (_totalPlacedCells == 0) return;
        ShowResetConfirmDialog();
    }

    private void DoReset()
    {
        if (_inventory != null)
        {
            var allPlaced = new List<RuntimeItemData>(_inventory.StagingItems);
            foreach (var item in allPlaced)
            {
                _stagingArea?.RemoveShapeForItem(item);
                _inventory.UnplaceItem(item);
            }
        }
        _placedItemPositions.Clear();
        _totalPlacedCells = 0;
        _hexGridView?.ClearAllPlacedCells();
        _hexGridView?.UpdateAdjacencyConstraints();
        RefreshSynergyStatus();
        RefreshFooter();
        UpdateHexGridHint();
        _stagingArea?.Refresh(_inventory);
    }

    private void OnConfirmClicked()
    {
        if (_inventory == null || _inventory.StagingCount == 0)
        {
            ClosePanel();
            return;
        }

        int remaining = _inventory.StagingCount;
        if (_confirmDialogText != null)
            _confirmDialogText.text = $"보관함에 아이템 {remaining}개가 있습니다.\n미배치 아이템은 폐기됩니다.";

        ShowConfirmDialog();
    }

    private void OnPlaceClicked()
    {
        // 현재 선택된 아이템을 그리드에 배치 시도.
        // GridManager는 드래그 앤 드롭 기반이므로 코드 직접 배치 API 미제공.
        // 보관함 첫 아이템을 InfoPanel에 표시하여 사용자가 드래그하도록 유도.
        if (_inventory == null || _inventory.StagingCount == 0) return;
        var item = _inventory.StagingItems[0];
        _itemInfoPanel?.ShowItem(item, isNew: false);
        _stagingArea?.HighlightItem(item);
    }

    private void OnDialogKeep()
    {
        HideConfirmDialog();
    }

    private void OnDialogDiscardAll()
    {
        HideConfirmDialog();
        if (_inventory == null) { ClosePanel(); return; }

        var toDiscard = new List<RuntimeItemData>(_inventory.StagingItems);
        foreach (var item in toDiscard)
        {
            _stagingArea?.RemoveShapeForItem(item);
            _inventory.DiscardFromStaging(item);
        }

        ClosePanel();
    }

    // ── Reset Confirm Dialog ──

    private void BuildResetConfirmDialog()
    {
        _resetDialog = new GameObject("ResetDialog", typeof(RectTransform));
        _resetDialog.transform.SetParent(transform, false);

        var rt = _resetDialog.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;

        var overlay = _resetDialog.AddComponent<Image>();
        overlay.color         = new Color(0f, 0f, 0f, 0.55f);
        overlay.raycastTarget = true;

        var panelGO = new GameObject("Panel", typeof(RectTransform));
        panelGO.transform.SetParent(_resetDialog.transform, false);
        var panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRT.pivot            = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta        = new Vector2(320f, 110f);
        panelRT.anchoredPosition = Vector2.zero;
        panelGO.AddComponent<Image>().color = new Color(0.10f, 0.11f, 0.17f, 0.98f);

        var textGO = MakeTxt(panelGO.transform, "Text",
            "배치된 아이템을 모두 초기화합니다.", 13f, new Color(0.9f, 0.92f, 1f, 1f));
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0f, 0.44f);
        textRT.anchorMax = new Vector2(1f, 1f);
        textRT.sizeDelta = Vector2.zero;
        textGO.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.Center;

        _resetDialogNo = MakeButton(panelGO.transform, "CancelBtn",
            new Vector2(0.08f, 0f), new Vector2(0.45f, 0.40f),
            Vector2.zero, new Vector2(0f, 4f),
            new Color(0.25f, 0.27f, 0.35f, 1f), "취소");

        _resetDialogYes = MakeButton(panelGO.transform, "ResetBtn",
            new Vector2(0.55f, 0f), new Vector2(0.92f, 0.40f),
            Vector2.zero, new Vector2(0f, 4f),
            new Color(0.65f, 0.20f, 0.18f, 1f), "초기화");

        _resetDialog.SetActive(false);
    }

    private void ShowResetConfirmDialog()
    {
        if (_resetDialog != null) _resetDialog.SetActive(true);
    }

    private void HideResetConfirmDialog()
    {
        if (_resetDialog != null) _resetDialog.SetActive(false);
    }

    private void OnResetConfirmYes()
    {
        HideResetConfirmDialog();
        DoReset();
    }

    private void OnResetConfirmNo()
    {
        HideResetConfirmDialog();
    }

    // ── Place Button State ──

    private void UpdatePlaceButtonState(bool hasItem)
    {
        if (_placeBG == null) return;
        _placeBG.color = hasItem
            ? new Color(0.22f, 0.50f, 0.88f, 0.92f)
            : new Color(0.20f, 0.28f, 0.40f, 0.55f);
        if (_placeLabel != null)
            _placeLabel.color = hasItem
                ? new Color(1f, 1f, 1f, 0.95f)
                : new Color(0.7f, 0.78f, 0.90f, 0.55f);
    }

    // ── Hex Grid Hint ──

    private void UpdateHexGridHint()
    {
        if (_hexGridHintText != null)
            _hexGridHintText.gameObject.SetActive(_totalPlacedCells == 0);
    }

    // ── CENTER Bonus ──

    private void HandleCenterBonusActivated()
    {
        RefreshFooter();
    }

    // ── Fade In ──

    private async UniTaskVoid FadeInAsync()
    {
        if (_canvasGroup == null) return;
        _canvasGroup.alpha = 0f;
        float elapsed = 0f;
        const float DURATION = 0.18f;
        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            while (elapsed < DURATION)
            {
                elapsed += Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Clamp01(elapsed / DURATION);
                await UniTask.NextFrame(cancellationToken: ct);
            }
        }
        catch (System.OperationCanceledException) { }
        if (_canvasGroup != null) _canvasGroup.alpha = 1f;
    }

    // ── Static Helpers ──

    private static GameObject Go(string name) => new(name, typeof(RectTransform));

    private static GameObject MakeTxt(Transform parent, string name,
        string text, float size, Color color, bool bold = false)
    {
        var go = Go(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text          = text;
        t.fontSize      = size;
        t.color         = color;
        t.fontStyle     = bold ? FontStyles.Bold : FontStyles.Normal;
        t.raycastTarget = false;
        t.enableWordWrapping = false;
        return go;
    }

    private static Button MakeButton(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax,
        Vector2 sizeDelta, Vector2 anchoredPos,
        Color bgColor, string label)
    {
        var btnGO = Go(name);
        btnGO.transform.SetParent(parent, false);
        var btnRT = btnGO.GetComponent<RectTransform>();
        btnRT.anchorMin        = anchorMin;
        btnRT.anchorMax        = anchorMax;
        btnRT.sizeDelta        = sizeDelta;
        btnRT.anchoredPosition = anchoredPos;
        btnRT.pivot            = new Vector2(0.5f, 0.5f);

        var img = btnGO.AddComponent<Image>();
        img.color = bgColor;
        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = img;

        var lblGO = Go("Label");
        lblGO.transform.SetParent(btnGO.transform, false);
        var lblRT = lblGO.GetComponent<RectTransform>();
        lblRT.anchorMin = Vector2.zero;
        lblRT.anchorMax = Vector2.one;
        lblRT.sizeDelta = Vector2.zero;
        var txt = lblGO.AddComponent<TextMeshProUGUI>();
        txt.text          = label;
        txt.fontSize      = 13f;
        txt.color         = Color.white;
        txt.alignment     = TextAlignmentOptions.Center;
        txt.raycastTarget = false;
        txt.enableWordWrapping = false;

        return btn;
    }
}
