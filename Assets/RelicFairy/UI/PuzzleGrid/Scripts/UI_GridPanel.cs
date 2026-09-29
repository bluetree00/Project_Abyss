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

    /// <summary>드래그 놓기가 거절된 이유를 토스트로 알린다(Shape.OnEndDrag). 룬판이 없으면 아무 일도 없다.</summary>
    public static void NotifyDropRejected(string reason)
    {
        if (Instance != null && !string.IsNullOrEmpty(reason)) Instance.ShowToast(reason, warn: true);
    }

    // ── Properties ──
    public RectTransform BoardContainer => _boardContainer;
    public bool IsOpen => _isOpen;

    // ── Private: Sub-views ──
    private CharacterInfoPanelView      _charInfoView;
    private MerlinRuneHexGridView       _hexGridView;
    private MerlinRuneSynergyStatusView _synergyStatusView;
    private StagingAreaView             _stagingArea;
    private ItemInfoPanel               _itemInfoPanel;

    // ── 정제소 배치 팝업 스킨 (@UIRoot에서 배선; 미배선 시 기존 외형 유지) ──
    [Header("정제소 스킨")]
    [SerializeField] private Sprite _bgSprite;             // 자연바탕
    // 속성판(헥사 그리드 배경) 아트는 더 이상 깔지 않는다 — 존 타일 가독성을 먹었다.
    // 슬롯은 프리팹 호환을 위해 남겨 두되 사용하지 않는다(BuildMainArea 주석 참고).
    [SerializeField, HideInInspector] private Sprite _boardSprite;
    [SerializeField] private Sprite _stagingBorderSprite;  // 룬 배치 테두리
    [SerializeField] private Sprite _stagingBgSprite;      // 룬 배치 테두리 바탕
    [SerializeField] private Sprite _synergyBorderSprite;  // 시너지 테두리
    [SerializeField] private Sprite _synergyBgSprite;      // 시너지 바탕
    [Header("그리드 타일 (디자이너 6속성 — F·I·T·P·L·D 순)")]
    [SerializeField] private Sprite[] _zoneTiles;          // 존별 셀 타일 6장
    [SerializeField] private Sprite   _centerTile;         // 중앙 타일(선택)

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
    private RectTransform _itemInfoRoot;

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

    // ── Private: 룬 선택 규격 크롬(09-25) ──
    private const float HeaderH      = 80f;    // 제목 띠(룬 선택 팝업과 같은 아트)
    private const float ActionAreaH  = 140f;   // 오른쪽 아래 안내 두 줄 + [초기화][완료]
    private const float OpenSlideDur = 0.28f;
    private Image    _headerBandImg;
    private TMP_Text _headerStatusText;
    private Image    _cardBgImg;
    private Image    _resetBtnImg;
    private Image    _confirmBtnImg;
    private TMP_Text _stagingCountText;
    private bool     _chromeSkinned;
    private Vector2  _headerRest, _leftRest, _rightRest;
    private CancellationTokenSource _openFxCts;
    private CancellationTokenSource _closeFadeCts;   // 닫기 페이드 — 도는 중 다시 열면 취소

    // ── Private: Footer Center Bonus ──
    private TMP_Text _footerCenterText;

    // ── Private: Fade ──
    private CanvasGroup _canvasGroup;

    // ── Private: State ──
    private RunItemInventory _inventory;
    private RuntimeItemData  _pendingNewItem;
    private RuntimeItemData  _pendingAddItem;   // 보관함이 가득 차 아직 못 넣은 획득 아이템(자리 나면 자동 추가)
    private bool             _isOpen;
    private bool             _layoutBuilt;
    private bool             _raisedAbovePopups;   // 팝업(상점) 위로 올려 연 상태 — 닫을 때 원래 순서로
    private int              _orderBeforeRaise;
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
        // 방어적 Release — 패널이 열린(Acquire) 채 파괴되면 timeScale 0 고착(ClosePanel만 Release).
        TimeScaleArbiter.Release(this);
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        _openFxCts?.Cancel();
        _openFxCts?.Dispose();
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
            GridManager.Instance.OnSquareClicked += HandleSquareClicked;
            GridManager.Instance.OnSquareHovered += HandleSquareHovered;
        }

        if (MerlinRuneBridge.Instance != null)
        {
            MerlinRuneBridge.Instance.OnSynergyActivated    += ShowSynergyActivated;
            MerlinRuneBridge.Instance.OnZoneSynergyBurst    += HandleZoneSynergyBurst;
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
            GridManager.Instance.OnSquareClicked -= HandleSquareClicked;
            GridManager.Instance.OnSquareHovered -= HandleSquareHovered;
        }

        if (MerlinRuneBridge.Instance != null)
        {
            MerlinRuneBridge.Instance.OnSynergyActivated    -= ShowSynergyActivated;
            MerlinRuneBridge.Instance.OnZoneSynergyBurst    -= HandleZoneSynergyBurst;
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

    /// <summary>새로 획득한 아이템을 강조하며 패널을 연다. 룬 선택 팝업이 고른 직후 부른다.</summary>
    public void ShowWithNewItem(RuntimeItemData newItem)
    {
        _pendingNewItem = newItem;
        OpenPanel();
    }

    /// <summary>
    /// 보관함이 가득 차 <b>추가에 실패한</b> 아이템을 들고 패널을 연다.
    /// 플레이어가 배치/폐기로 자리를 비우면 <see cref="TryFlushPendingAdd"/>가 자동으로 넣어준다.
    /// </summary>
    public void ShowWithPendingItem(RuntimeItemData item)
    {
        _pendingAddItem = item;
        OpenPanel();
        TryFlushPendingAdd();   // 여는 사이에 자리가 났을 수도 있다
    }

    /// <summary>
    /// 열린 팝업(상점 등) <b>위로</b> 올린다. 이 패널 캔버스는 팝업 스택(410~) 아래인 400이라,
    /// 상점에서 룬을 사면 룬판이 상점 뒤에 열려 보이지도 눌리지도 않았다(09-19 실측).
    /// 상시로 올리지 않는 이유: 평소엔 팝업이 이 판 위에 떠야 한다. 닫을 때 원래 순서로 돌린다.
    /// </summary>
    public void RaiseAbovePopups()
    {
        if (!TryGetComponent<Canvas>(out var canvas)) return;

        int top = int.MinValue;
        foreach (var p in FindObjectsByType<UI_Popup>(FindObjectsSortMode.None))
            if (p.isActiveAndEnabled && p.TryGetComponent<Canvas>(out var pc)) top = Mathf.Max(top, pc.sortingOrder);
        if (top == int.MinValue || top < canvas.sortingOrder) return;

        if (!_raisedAbovePopups) { _orderBeforeRaise = canvas.sortingOrder; _raisedAbovePopups = true; }
        canvas.overrideSorting = true;
        canvas.sortingOrder    = top + 1;
    }

    // 갤러리 모드(EnterGalleryMode)·편집 모드(EnterEditMode)는 단일 편집 화면으로 개편되며 폐기됐다.
    // 두 함수 모두 빈 껍데기였고 호출처도 없어 제거함. 갤러리 뷰(GridGalleryView.cs)도 함께 삭제.

    // ── Open / Close ──

    private void OpenPanel()
    {
        _isOpen = true;
        _closeFadeCts?.Cancel();   // 닫히는 페이드 중에 다시 열면 끄지 않는다
        TimeScaleArbiter.Acquire(this, 0f, TimeScaleArbiter.Priority.Pause);
        gameObject.SetActive(true);
        BringToFront();
        EnsureChromeSkin();
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
                {
                    BoardManager.Instance.EnterExternalGrid(hexGrid, hexGrid.gridAsset);
                    BoardManager.Instance.SetSpawnAreaVisible(false);   // 배치는 칸 클릭으로 한다
                }
                else if (GridManager.Instance != null)
                    GridManager.Instance.SetActiveGrid(hexGrid);
            }

            // 초기 점유 상태 반영 (재오픈 시 이전 배치 복원)
            _hexGridView.RefreshOccupiedCells();
            _hexGridView.SetPieceGroups(_placedItemPositions);
            _hexGridView.UpdateAdjacencyConstraints();

        }

        run?.EnterGridSynergy();

        BindInventory();
        _stagingArea?.Refresh(_inventory);

        RefreshSynergyStatus();
        RefreshFooter();
        RefreshHeaderStatus();
        RefreshInfoPanelDefault();

        // 배치할 룬의 속성 존을 판에서 강조(어디 놓으면 시너지인지 안내).
        // RefreshSynergyStatus가 칸 색을 다시 칠하므로 반드시 그 뒤에 세운다.
        RefreshPlacementHint();

        if (_pendingNewItem != null)
        {
            _itemInfoPanel?.ShowItem(_pendingNewItem, isNew: true);
            _pendingNewItem = null;
        }

        PlayOpenFxAsync().Forget();   // 보관함이 새로 그려진 뒤에 — 올라올 카드가 정해져 있어야 한다
    }

    /// <summary>
    /// 이 패널이 속한 캔버스 안에서 맨 앞으로 올린다.
    /// 퀘스트 추적·알림 위젯이 같은 캔버스의 <b>뒤쪽 형제</b>라, 전체화면인 이 패널 위에
    /// "무장 / 모루에서 장비 선택" 같은 글자가 그대로 겹쳐 보였다. 위젯 이름에 의존하지 않도록
    /// 캔버스 직속 조상까지 올라가 그 조상을 맨 뒤 형제(=최상단 렌더)로 옮긴다.
    /// </summary>
    private void BringToFront()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        var t = transform;
        while (t.parent != null && t.parent != canvas.transform) t = t.parent;
        if (t.parent == canvas.transform) t.SetAsLastSibling();
    }

    private void ClosePanel()
    {
        _isOpen = false;
        _openFxCts?.Cancel();
        if (_raisedAbovePopups && TryGetComponent<Canvas>(out var canvas)) canvas.sortingOrder = _orderBeforeRaise;
        _raisedAbovePopups = false;
        TimeScaleArbiter.Release(this);
        HideConfirmDialog();
        GameRunBootstrapper.Instance?.Run?.ExitGridSynergy();
        FadeOutAndHideAsync().Forget();   // 열 때는 0.18초 페이드인데 닫을 때만 순간 소멸했다(09-28 UI 톤 통일)
    }

    private async UniTaskVoid FadeOutAndHideAsync()
    {
        _closeFadeCts?.Cancel();
        _closeFadeCts?.Dispose();
        _closeFadeCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        var ct = _closeFadeCts.Token;
        if (_canvasGroup != null) _canvasGroup.blocksRaycasts = false;
        try
        {
            while (_canvasGroup != null && _canvasGroup.alpha > 0f)
            {
                _canvasGroup.alpha = Mathf.MoveTowards(_canvasGroup.alpha, 0f, Time.unscaledDeltaTime / UIFader.CloseSec);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            gameObject.SetActive(false);
        }
        catch (System.OperationCanceledException) { }
        finally
        {
            if (_canvasGroup != null) _canvasGroup.blocksRaycasts = true;
        }
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

        // 전체 배경 (자연바탕 스킨 — 미배선 시 단색)
        var bg = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
        bg.color = new Color(0.10f, 0.12f, 0.18f, 0.97f);
        SkinImage(bg, _bgSprite, fill: true);

        BuildHeader();
        BuildMainArea();
        BuildFooter();
    }

    /// <summary>이미지에 스프라이트 주입(비파괴 — sprite null이면 기존 유지). fill=true면 늘려 채움, false면 종횡비 보존.</summary>
    private static void SkinImage(Image img, Sprite sprite, bool fill)
    {
        if (img == null || sprite == null) return;
        img.sprite = sprite;
        img.type = Image.Type.Simple;
        img.color = Color.white;
        img.preserveAspect = !fill;
    }

    // Header (80px, 상단) — 룬 선택 팝업의 제목 띠 아트(EnsureChromeSkin). 제목 32 · 상태 22.
    // [초기화][완료]는 오른쪽 상세 아래로 옮겼다(09-25) — 머리줄 오른쪽 끝의 90px 빨강·초록 칸은 주 행동으로 읽히지 않았다.
    private void BuildHeader()
    {
        var headerGO = Go("Header");
        headerGO.transform.SetParent(transform, false);
        _headerRT = headerGO.GetComponent<RectTransform>();
        _headerRT.anchorMin        = new Vector2(0f, 1f);
        _headerRT.anchorMax        = new Vector2(1f, 1f);
        _headerRT.pivot            = new Vector2(0.5f, 1f);
        _headerRT.sizeDelta        = new Vector2(-48f, HeaderH - 8f);
        _headerRT.anchoredPosition = new Vector2(0f, -6f);
        _headerRest = _headerRT.anchoredPosition;

        _headerBandImg = headerGO.AddComponent<Image>();
        _headerBandImg.color = new Color(0.10f, 0.12f, 0.18f, 0.96f);   // 아트 미로드 폴백

        // ← 뒤로가기 — 둥근 단추(테두리 원 + 안쪽 원)
        _backButton = MakeButton(headerGO.transform, "BackBtn",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(50f, 50f), new Vector2(44f, 0f),
            new Color(0.45f, 0.52f, 0.70f, 1f), "←");
        var backImg = _backButton.GetComponent<Image>();
        backImg.sprite = RuneCardKit.Disc;
        ShopUIStyle.ApplyButtonColors(_backButton, backImg);
        var backFill = ShopUIStyle.MakeImage(_backButton.transform, "Fill", new Color(0.05f, 0.07f, 0.12f, 1f));
        backFill.sprite = RuneCardKit.Disc;
        ShopUIStyle.Stretch(backFill.rectTransform, 2.5f);
        backFill.transform.SetAsFirstSibling();
        var backLbl = _backButton.GetComponentInChildren<TMP_Text>();
        if (backLbl != null) { backLbl.fontSize = 26f; backLbl.color = new Color(0.88f, 0.92f, 1f, 1f); }

        // 제목 — 이 화면의 이름과 할 일을 한 줄로
        var titleGO = MakeTxt(headerGO.transform, "Title",
            "<b>룬판</b>   <size=22><color=#C9D0E4>보관함의 룬을 판에 놓으세요</color></size>", 32f,
            new Color(0.97f, 0.94f, 0.85f, 1f));
        var titleRT = titleGO.GetComponent<RectTransform>();
        titleRT.anchorMin        = new Vector2(0f, 0f);
        titleRT.anchorMax        = new Vector2(0.6f, 1f);
        titleRT.offsetMin        = new Vector2(84f, 0f);
        titleRT.offsetMax        = Vector2.zero;
        titleGO.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;

        // 상태 — 보관함 N/5 · 배치 M (RefreshHeaderStatus)
        var statusGO = MakeTxt(headerGO.transform, "Status", "", 22f, new Color(0.72f, 0.76f, 0.86f, 1f));
        var statusRT = statusGO.GetComponent<RectTransform>();
        statusRT.anchorMin        = new Vector2(0.6f, 0f);
        statusRT.anchorMax        = new Vector2(1f, 1f);
        statusRT.offsetMin        = Vector2.zero;
        statusRT.offsetMax        = new Vector2(-36f, 0f);
        _headerStatusText = statusGO.GetComponent<TMP_Text>();
        _headerStatusText.alignment = TextAlignmentOptions.MidlineRight;
    }

    // MainArea — Header 하단 ~ Footer 상단
    private void BuildMainArea()
    {
        var mainGO = Go("MainArea");
        mainGO.transform.SetParent(transform, false);
        _mainAreaRT = mainGO.GetComponent<RectTransform>();
        _mainAreaRT.anchorMin        = new Vector2(0f, 0f);
        _mainAreaRT.anchorMax        = new Vector2(1f, 1f);
        _mainAreaRT.offsetMin        = new Vector2(0f, 184f);  // 하단 보관함 바 180px(의뢰서 F4) + 여백
        _mainAreaRT.offsetMax        = new Vector2(0f, -(HeaderH + 4f));  // 제목 띠 80 + 여백

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
        _leftPanelRT.anchorMax = new Vector2(0.22f, 1f);
        _leftPanelRT.offsetMin = _leftPanelRT.offsetMax = Vector2.zero;

        // 의뢰서 F2: 좌 = 캐릭터 정보(초상·HP·2×3 스탯·활성 효과) 위, 속성 시너지 아래.
        _charInfoView = CharacterInfoPanelView.Create(go.transform);
        // 「활성 효과」 머리띠 오른쪽 — 이번 런 발동 계열 각인(09-29 빌드 컨셉).
        var effHdr = ShopUIStyle.FindDeep(_charInfoView.transform, "EffectsHdr");
        if (effHdr != null && effHdr.GetComponent<BuildImprintStrip>() == null) effHdr.gameObject.AddComponent<BuildImprintStrip>();
        var charRT = (RectTransform)_charInfoView.transform;
        charRT.anchorMin = new Vector2(0f, 0.46f);
        charRT.anchorMax = new Vector2(1f, 1f);
        charRT.offsetMin = charRT.offsetMax = Vector2.zero;
        var synGO = Go("SynergyStatusRoot");
        synGO.transform.SetParent(go.transform, false);
        _synergyStatusRoot = synGO.GetComponent<RectTransform>();
        _synergyStatusRoot.anchorMin = Vector2.zero;
        _synergyStatusRoot.anchorMax = new Vector2(1f, 0.46f);
        _synergyStatusRoot.offsetMin = _synergyStatusRoot.offsetMax = Vector2.zero;
        _synergyStatusView = synGO.AddComponent<MerlinRuneSynergyStatusView>();
        _synergyStatusView.SetSkin(_synergyBgSprite, _synergyBorderSprite);   // 시너지 바탕/테두리
        _leftRest = _leftPanelRT.anchoredPosition;
    }

    // CenterPanel (20% ~ 75%)
    private void BuildCenterPanel(Transform parent)
    {
        var go = Go("CenterPanel");
        go.transform.SetParent(parent, false);
        _centerPanelRT = go.GetComponent<RectTransform>();
        _centerPanelRT.anchorMin = new Vector2(0.22f, 0f);
        _centerPanelRT.anchorMax = new Vector2(0.75f, 1f);
        _centerPanelRT.offsetMin = new Vector2(2f, 0f);
        _centerPanelRT.offsetMax = new Vector2(-2f, 0f);

        // HexGrid 전체 높이 (0.0 ~ 1.0) — 하단 시너지 영역 제거로 그리드가 중앙 전체 사용
        var hexRootGO = Go("HexGridRoot");
        hexRootGO.transform.SetParent(go.transform, false);
        _hexGridRoot = hexRootGO.GetComponent<RectTransform>();
        _hexGridRoot.anchorMin = new Vector2(0f, 0.0f);
        _hexGridRoot.anchorMax = new Vector2(1f, 1.0f);
        _hexGridRoot.offsetMin = _hexGridRoot.offsetMax = Vector2.zero;

        // 판 뒤는 비운다 — 창 바탕(자연 그림) 위에 판이 놓이고, 경계는 판 외곽선이 준다(09-29 사용자
        // 「판 뒤 배경은 정리하고 자연 배경에서 룬판을 쓰는 게 좋아 보인다 · 얇고 깔끔한 테두리」).
        // 이미지는 남긴다(알파 0) — 끌어 놓기 레이캐스트가 이 칸 전체를 받아야 한다.
        var hexBG = hexRootGO.AddComponent<Image>();
        hexBG.color = new Color(0f, 0f, 0f, 0f);

        _hexGridView = hexRootGO.AddComponent<MerlinRuneHexGridView>();
        _hexGridView.SetZoneTiles(_zoneTiles, _centerTile);   // 타일 미배선 시 기존 색상 방식 유지

        BuildBoardFrame(_hexGridRoot);

        // (판 가운데 「보관함의 룬을 끌어 판에 놓으세요」 판은 09-27에 뺐다 — 중앙 존을 340×86으로 덮었다.
        //  끄는 곳 안내는 보관함 머리글 아래 상시 문구가 맡는다: BuildFooter)

        // BoardContainer: GridManager/MerlinRuneBridge와 연동하는 영역
        var boardGO = Go("BoardContainer");
        boardGO.transform.SetParent(hexRootGO.transform, false);
        _boardContainer = boardGO.GetComponent<RectTransform>();
        _boardContainer.anchorMin = Vector2.zero;
        _boardContainer.anchorMax = Vector2.one;
        _boardContainer.offsetMin = _boardContainer.offsetMax = Vector2.zero;
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
        _rightRest = _rightPanelRT.anchoredPosition;

        // 바탕은 옅게 — 이 칸의 주인공은 보석 테두리 카드다(판 바탕이 비쳐야 카드가 떠 보인다).
        var rightBG = go.AddComponent<Image>();
        rightBG.color = new Color(0.05f, 0.06f, 0.10f, 0.45f);
        // 의뢰서 F3: 우 = 아이템 정보만. 보관함은 하단 바(F4)로 갔다.
        BuildItemInfoArea(go.transform);
    }

    /// <summary>
    /// 오른쪽 상세 — 룬 선택 카드와 같은 얼굴(09-25). ItemInfoPanel의 필드를 코드로 만들어 주입한다.
    /// 카드 = 위 「고른 룬」 줄 아래부터 아래 안내·버튼 줄 위까지. 등급 보석 테두리·뒤집기는 ItemInfoPanel이 카드에 건다.
    /// </summary>
    private void BuildAndInjectItemInfoPanelFields(GameObject root)
    {
        if (_itemInfoPanel == null) return;

        var rf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var type = typeof(ItemInfoPanel);
        void Inject(string field, object value) => type.GetField(field, rf)?.SetValue(_itemInfoPanel, value);

        var cg = root.AddComponent<CanvasGroup>();
        Inject("canvasGroup", cg);

        // 카드
        var cardGO = Go("RuneCard");
        cardGO.transform.SetParent(root.transform, false);
        var cardRT = cardGO.GetComponent<RectTransform>();
        cardRT.anchorMin = Vector2.zero;
        cardRT.anchorMax = Vector2.one;
        cardRT.offsetMin = new Vector2(0f, ActionAreaH);
        cardRT.offsetMax = new Vector2(0f, -34f);
        _cardBgImg = cardGO.AddComponent<Image>();
        _cardBgImg.color         = new Color(0.06f, 0.07f, 0.11f, 0.96f);   // 아트 미로드 폴백(EnsureChromeSkin이 카드 바탕 아트로)
        _cardBgImg.raycastTarget = false;
        Inject("itemRoot", cardGO);
        Inject("cardRoot", cardRT);

        var top = new Vector2(0.5f, 1f);
        var mid = new Vector2(0.5f, 0.5f);

        // 등급빛 + 문양 — 카드 위쪽 칸의 주인공
        var glow = ShopUIStyle.MakeImage(cardRT, "IconGlow", Color.clear);
        ShopUIStyle.Anchor(glow.rectTransform, top, top, mid, new Vector2(0f, -122f), new Vector2(280f, 280f));
        Inject("iconGlow", glow);

        var icon = ShopUIStyle.MakeImage(cardRT, "ItemIcon", Color.white);
        icon.preserveAspect = true;
        ShopUIStyle.Anchor(icon.rectTransform, top, top, mid, new Vector2(0f, -122f), new Vector2(168f, 168f));
        Inject("itemIcon", icon);

        // 이름 30
        var nameGO = MakeTxt(cardRT, "ItemName", "", 30f, new Color(0.97f, 0.95f, 0.88f, 1f), bold: true);
        var nameRT = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin        = new Vector2(0f, 1f);
        nameRT.anchorMax        = new Vector2(1f, 1f);
        nameRT.pivot            = new Vector2(0.5f, 1f);
        nameRT.sizeDelta        = new Vector2(-40f, 42f);
        nameRT.anchoredPosition = new Vector2(0f, -224f);
        var nameTxt = nameGO.GetComponent<TMP_Text>();
        nameTxt.alignment    = TextAlignmentOptions.Center;
        nameTxt.overflowMode = TextOverflowModes.Ellipsis;
        Inject("itemName", nameTxt);

        // 등급·칸 수 / 속성 칩 줄
        var chipGO = Go("ChipRow");
        chipGO.transform.SetParent(cardRT, false);
        var chipRT = chipGO.GetComponent<RectTransform>();
        ShopUIStyle.Anchor(chipRT, new Vector2(0f, 1f), new Vector2(1f, 1f), mid, new Vector2(0f, -290f), new Vector2(0f, 30f));
        Inject("chipRow", chipRT);

        // 효과 — 전부, 줄바꿈 허용(행이 자기 높이로 선다)
        var fxGO = Go("EffectList");
        fxGO.transform.SetParent(cardRT, false);
        var fxRT = fxGO.GetComponent<RectTransform>();
        fxRT.anchorMin = Vector2.zero;
        fxRT.anchorMax = Vector2.one;
        fxRT.offsetMin = new Vector2(28f, 146f);
        fxRT.offsetMax = new Vector2(-24f, -318f);
        var vlg = fxGO.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment         = TextAnchor.UpperLeft;
        vlg.spacing                = 6f;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        Inject("effectListRoot", fxRT);

        // 모양(왼쪽 아래) + 놓을 자리(오른쪽 아래) — 모양이 "놓을 수 있는가"의 근거라 같은 줄에 둔다
        var shapeGO = Go("ShapePreview");
        shapeGO.transform.SetParent(cardRT, false);
        var shapeRT = shapeGO.GetComponent<RectTransform>();
        ShopUIStyle.Anchor(shapeRT, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(24f, 24f), new Vector2(170f, 110f));
        Inject("shapePreviewRoot", shapeRT);

        var fitFill = ShopUIStyle.MakeFrame(cardRT, "FitBadge", Color.clear, Color.clear, 1.5f);
        var fitRT = (RectTransform)fitFill.transform.parent;
        ShopUIStyle.Anchor(fitRT, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 40f), new Vector2(190f, 34f));
        var fitLbl = ShopUIStyle.MakeText(fitFill.transform, "Label", 17f, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
        fitLbl.enableAutoSizing = false;
        ShopUIStyle.Stretch(fitLbl.rectTransform);
        Inject("fitBorder", fitRT.GetComponent<Image>());
        Inject("fitFill", fitFill);
        Inject("fitLabel", fitLbl);

        // NEW — 카드 위 끝에 걸터앉는 금빛 칩(16px). 예전 10px 빨강 글자는 읽히지 않았다.
        var newGO = Go("NewBadge");
        newGO.transform.SetParent(cardRT, false);
        var newRT = newGO.GetComponent<RectTransform>();
        ShopUIStyle.Anchor(newRT, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(22f, -2f), new Vector2(60f, 26f));
        var newBG = newGO.AddComponent<Image>();
        newBG.color         = new Color(0.88f, 0.71f, 0.25f, 1f);
        newBG.raycastTarget = false;
        var newLblGO = MakeTxt(newRT, "Label", "NEW", 16f, new Color(0.10f, 0.07f, 0.02f, 1f), bold: true);
        var newLblRT = newLblGO.GetComponent<RectTransform>();
        newLblRT.anchorMin = Vector2.zero;
        newLblRT.anchorMax = Vector2.one;
        newLblRT.offsetMin = newLblRT.offsetMax = Vector2.zero;
        newLblGO.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.Center;
        Inject("newBadge", newGO);

        // 빈 상태
        var emptyRootGO = Go("EmptyRoot");
        emptyRootGO.transform.SetParent(root.transform, false);
        var emptyRootRT = emptyRootGO.GetComponent<RectTransform>();
        emptyRootRT.anchorMin = Vector2.zero;
        emptyRootRT.anchorMax = Vector2.one;
        emptyRootRT.offsetMin = new Vector2(0f, ActionAreaH);
        emptyRootRT.offsetMax = new Vector2(0f, -34f);
        Inject("emptyRoot", emptyRootGO);

        var emptyTxtGO = MakeTxt(emptyRootGO.transform, "EmptyText", "보관함에서 룬을 고르면\n여기에 자세히 나옵니다", 18f,
            new Color(0.64f, 0.69f, 0.82f, 0.9f));
        var emptyTxtRT = emptyTxtGO.GetComponent<RectTransform>();
        emptyTxtRT.anchorMin = Vector2.zero;
        emptyTxtRT.anchorMax = Vector2.one;
        emptyTxtRT.offsetMin = emptyTxtRT.offsetMax = Vector2.zero;
        var emptyTxt = emptyTxtGO.GetComponent<TMP_Text>();
        emptyTxt.alignment        = TextAlignmentOptions.Center;
        emptyTxt.textWrappingMode = TextWrappingModes.Normal;
        Inject("emptyText", emptyTxt);

        // AddComponent 직후 Awake의 ShowEmpty는 필드 주입 전이라 아무것도 못 숨긴다 — 주입이 끝난 지금 빈 상태로 맞춘다.
        // 안 그러면 OpenPanel을 거치지 않고 켜졌을 때 스프라이트 없는 아이콘이 흰 사각형으로, NEW 배지가 켜진 채 남는다.
        _itemInfoPanel.ShowEmpty();
    }

    private void BuildItemInfoArea(Transform parent)
    {
        var infoRootGO = Go("ItemInfoRoot");
        infoRootGO.transform.SetParent(parent, false);
        _itemInfoRoot = infoRootGO.GetComponent<RectTransform>();
        _itemInfoRoot.anchorMin = Vector2.zero;
        _itemInfoRoot.anchorMax = Vector2.one;
        _itemInfoRoot.offsetMin = new Vector2(18f, 10f);
        _itemInfoRoot.offsetMax = new Vector2(-18f, -8f);

        var selLbl = MakeTxt(infoRootGO.transform, "SelectLabel", "고른 룬", 17f,
            new Color(0.72f, 0.77f, 0.88f, 1f), bold: true);
        var selRT = selLbl.GetComponent<RectTransform>();
        selRT.anchorMin        = new Vector2(0f, 1f);
        selRT.anchorMax        = new Vector2(1f, 1f);
        selRT.pivot            = new Vector2(0f, 1f);
        selRT.sizeDelta        = new Vector2(0f, 24f);
        selRT.anchoredPosition = new Vector2(4f, 0f);

        // ItemInfoPanel 컴포넌트 추가 (필수 SerializeField를 코드로 초기화)
        _itemInfoPanel = infoRootGO.AddComponent<ItemInfoPanel>();
        BuildAndInjectItemInfoPanelFields(infoRootGO);

        // 안내 두 줄 — 예전 「가짜 버튼」 자리. 배치는 드래그·칸 누르기로만 일어나므로 버튼이 아니라 문장이다.
        var hintGO = MakeTxt(infoRootGO.transform, "PlaceHint",
            "카드를 끌어 놓거나, 골라서 칸을 누른다\n놓인 룬은 판 밖으로 끌면 보관함으로", 17f,
            new Color(0.74f, 0.79f, 0.90f, 0.95f));
        var hintRT = hintGO.GetComponent<RectTransform>();
        hintRT.anchorMin        = new Vector2(0f, 0f);
        hintRT.anchorMax        = new Vector2(1f, 0f);
        hintRT.pivot            = new Vector2(0.5f, 0f);
        hintRT.sizeDelta        = new Vector2(0f, 48f);
        hintRT.anchoredPosition = new Vector2(0f, 80f);
        var hintTxt = hintGO.GetComponent<TMP_Text>();
        hintTxt.alignment        = TextAlignmentOptions.Center;
        hintTxt.textWrappingMode = TextWrappingModes.Normal;

        // [초기화](보조 — 넘기기 아트) · [완료](주 — 선택 아트). 아트는 열 때 입힌다(EnsureChromeSkin).
        _resetButton   = MakeActionButton(infoRootGO.transform, "ResetBtn",   "초기화", 0f,    0.36f, primary: false, out _resetBtnImg);
        _confirmButton = MakeActionButton(infoRootGO.transform, "ConfirmBtn", "완료",   0.39f, 1f,    primary: true,  out _confirmBtnImg);
    }

    // Footer (180px, 하단) — 보관함 바. 카드 272폭 5칸(09-25: 220 → 272, 바 오른쪽 빈 공간을 글자에 준다).
    private void BuildFooter()
    {
        var footerGO = Go("Footer");
        footerGO.transform.SetParent(transform, false);
        _footerRT = footerGO.GetComponent<RectTransform>();
        _footerRT.anchorMin        = new Vector2(0f, 0f);
        _footerRT.anchorMax        = new Vector2(1f, 0f);
        _footerRT.sizeDelta        = new Vector2(0f, 180f);
        _footerRT.anchoredPosition = new Vector2(0f, 90f);
        var footerBG = footerGO.AddComponent<Image>();
        footerBG.color = new Color(0.06f, 0.08f, 0.13f, 0.98f);

        // 위 끝 금빛 머리선 — 판과 보관함의 경계
        var line = ShopUIStyle.MakeImage(footerGO.transform, "TopLine", new Color(0.91f, 0.73f, 0.33f, 0.45f));
        ShopUIStyle.Anchor(line.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 1.5f));

        var lblGO = MakeTxt(footerGO.transform, "StagingLabel", "보관함", 24f,
            new Color(0.95f, 0.92f, 0.84f, 1f), bold: true);
        var lblRT = lblGO.GetComponent<RectTransform>();
        lblRT.anchorMin = new Vector2(0f, 1f);
        lblRT.anchorMax = new Vector2(0f, 1f);
        lblRT.pivot     = new Vector2(0f, 1f);
        lblRT.anchoredPosition = new Vector2(34f, -20f);
        lblRT.sizeDelta = new Vector2(200f, 32f);

        var cntGO = MakeTxt(footerGO.transform, "StagingCount", "", 20f, new Color(0.80f, 0.84f, 0.94f, 1f));
        var cntRT = cntGO.GetComponent<RectTransform>();
        cntRT.anchorMin = new Vector2(0f, 1f);
        cntRT.anchorMax = new Vector2(0f, 1f);
        cntRT.pivot     = new Vector2(0f, 1f);
        cntRT.anchoredPosition = new Vector2(34f, -56f);
        cntRT.sizeDelta = new Vector2(200f, 28f);
        _stagingCountText = cntGO.GetComponent<TMP_Text>();

        // 끄는 곳 — 보관함 카드가 손잡이다(09-27 사용자 「룬을 드래그하는 곳이 어디인지 표기」). 늘 떠 있다.
        var dragGO = MakeTxt(footerGO.transform, "DragHint", "▲ 카드를 끌어\n판에 놓기", 18f,
            new Color(0.93f, 0.80f, 0.48f, 1f));
        var dragRT = dragGO.GetComponent<RectTransform>();
        dragRT.anchorMin = dragRT.anchorMax = dragRT.pivot = new Vector2(0f, 1f);
        dragRT.anchoredPosition = new Vector2(34f, -96f);
        dragRT.sizeDelta        = new Vector2(230f, 56f);
        TMPOutlineHelper.ApplySoftShadow(dragGO.GetComponent<TMP_Text>());

        // StagingAreaView는 콘텐츠 좌상단 기준으로 슬롯을 놓는다 — 바 가운데보다 60 오른쪽(왼쪽 라벨 자리)에 세운다.
        // 바 폭은 5칸 기준으로 고정 — 칸이 6 · 7이 되면(제단 해금) 카드 폭을 줄여 같은 폭에 넣는다(ApplyStagingCapacity).
        float barW = StagingBarWidth;
        var contentGO = Go("StagingBar");
        contentGO.transform.SetParent(footerGO.transform, false);
        var contentRT = contentGO.GetComponent<RectTransform>();
        contentRT.anchorMin        = new Vector2(0.5f, 1f);
        contentRT.anchorMax        = new Vector2(0.5f, 1f);
        contentRT.pivot            = new Vector2(0f, 1f);
        contentRT.anchoredPosition = new Vector2(-barW * 0.5f + 60f, -8f);
        _stagingArea = footerGO.AddComponent<StagingAreaView>();
        _stagingArea.SetSlotSkin(_stagingBgSprite, _stagingBorderSprite);   // 슬롯 빌드 전에 주입
        _stagingArea.Init(contentRT);
        ApplyStagingCapacity();                                               // 칸 수만큼 1행

        // 예전 푸터 텍스트(활성 시너지·중앙 보너스·셀 카운트)는 시너지 패널이 대신한다 — 만들되 숨긴다(RefreshFooter 참조 유지).
        var actGO = MakeTxt(footerGO.transform, "ActiveSyn", "", 12f, Color.white);
        _footerActiveSynText = actGO.GetComponent<TMP_Text>();
        actGO.SetActive(false);
        var centerGO = MakeTxt(footerGO.transform, "CenterBonus", "", 11f, Color.white);
        _footerCenterText = centerGO.GetComponent<TMP_Text>();
        centerGO.SetActive(false);
        var cntHiddenGO = MakeTxt(footerGO.transform, "CellCount", "", 12f, Color.white);
        _footerCellCountText = cntHiddenGO.GetComponent<TMP_Text>();
        cntHiddenGO.SetActive(false);
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
        panelRT.sizeDelta        = new Vector2(460f, 210f);   // 18px 네 줄 + 버튼(13px로 끼워 넣던 340×130)
        panelRT.anchoredPosition = Vector2.zero;
        panelGO.AddComponent<Image>().color = new Color(0.10f, 0.11f, 0.17f, 0.98f);

        var textGO = MakeTxt(panelGO.transform, "Text",
            "보관함 아이템이 남아 있습니다.", 18f, new Color(0.9f, 0.92f, 1f, 1f));
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0f, 0.36f);
        textRT.anchorMax = new Vector2(1f, 1f);
        textRT.offsetMin = new Vector2(18f, 0f);
        textRT.offsetMax = new Vector2(-18f, -10f);
        _confirmDialogText = textGO.GetComponent<TMP_Text>();
        _confirmDialogText.alignment     = TextAlignmentOptions.Center;
        _confirmDialogText.textWrappingMode = TextWrappingModes.Normal;

        // [계속 배치] 버튼
        _confirmDialogKeepBtn = MakeButton(panelGO.transform, "KeepBtn",
            new Vector2(0.08f, 0.08f), new Vector2(0.45f, 0.30f),
            Vector2.zero, Vector2.zero,
            new Color(0.25f, 0.27f, 0.35f, 1f), "계속 배치");

        // [폐기 후 종료] 버튼
        _confirmDialogDiscardBtn = MakeButton(panelGO.transform, "DiscardBtn",
            new Vector2(0.55f, 0.08f), new Vector2(0.92f, 0.30f),
            Vector2.zero, Vector2.zero,
            new Color(0.72f, 0.18f, 0.18f, 1f), "폐기 후 종료");

        _confirmDialog.SetActive(false);
    }

    private void ShowConfirmDialog()
    {
        if (_confirmDialog == null) return;
        _confirmDialog.transform.SetAsLastSibling();   // 판에 놓인 룬(Puzzle, 루트 직속)보다 위 — 어두운 막 위로 룬이 비쳤다
        _confirmDialog.SetActive(true);
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
        rt.anchorMin        = new Vector2(0.24f, 0.80f);   // 제목 띠(위 80px)를 덮지 않게 그 아래 판 위쪽에
        rt.anchorMax        = new Vector2(0.73f, 0.86f);
        rt.offsetMin        = Vector2.zero;
        rt.offsetMax        = Vector2.zero;

        _synergyToast.AddComponent<Image>().color = new Color(0.04f, 0.14f, 0.08f, 0.93f);

        var textGO = MakeTxt(_synergyToast.transform, "Text",
            "", 18f, new Color(0.3f, 1f, 0.55f, 1f));
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.sizeDelta = new Vector2(-12f, -8f);
        _synergyToastText = textGO.GetComponent<TMP_Text>();
        _synergyToastText.alignment = TextAlignmentOptions.Center;
        _synergyToastText.textWrappingMode = TextWrappingModes.NoWrap;

        _synergyToast.SetActive(false);
    }

    private void ShowSynergyActivated(string description)
    {
        ShowSynergyToastAsync(description).Forget();
    }

    /// <summary>시너지 단계 달성 → 판(뷰)이 해당 속성 존을 터뜨리는 연출. 브릿지 이벤트 포워딩(뷰 생명주기 무관하게 안전).</summary>
    private void HandleZoneSynergyBurst(string zoneId)
    {
        _hexGridView?.PlayZoneSynergyBurst(zoneId);
    }

    /// <summary>짧은 안내 문구(배치 실패 사유 등). 시너지 토스트와 같은 자리를 쓴다. <paramref name="warn"/>면 붉은 띠.</summary>
    private void ShowToast(string message, bool warn = false) => ShowSynergyToastAsync(message, prefix: false, warn).Forget();

    private UniTaskVoid ShowSynergyToastAsync(string description) => ShowSynergyToastAsync(description, prefix: true);

    // 알림 띠 색 — 시너지·완료는 초록, 거절·실패는 붉은색(09-27: 거절도 초록이라 성공처럼 읽혔다).
    private static readonly Color ToastOkPlate   = new(0.04f, 0.14f, 0.08f, 0.93f);
    private static readonly Color ToastOkInk     = new(0.30f, 1.00f, 0.55f, 1f);
    private static readonly Color ToastWarnPlate = new(0.20f, 0.05f, 0.05f, 0.93f);
    private static readonly Color ToastWarnInk   = new(1.00f, 0.66f, 0.58f, 1f);

    private async UniTaskVoid ShowSynergyToastAsync(string description, bool prefix, bool warn = false)
    {
        if (_synergyToast != null && _synergyToast.TryGetComponent<Image>(out var plate))
            plate.color = warn ? ToastWarnPlate : ToastOkPlate;
        if (_synergyToastText != null) _synergyToastText.color = warn ? ToastWarnInk : ToastOkInk;
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        _toastCts = new CancellationTokenSource();
        var ct = _toastCts.Token;

        if (_synergyToastText != null)
            _synergyToastText.text = prefix ? $"◆ 시너지 활성화!  {description}" : description;
        if (_synergyToast != null)
        {
            _synergyToast.transform.SetAsLastSibling();   // 판에 놓인 룬 밑에 깔리지 않게
            _synergyToast.SetActive(true);
        }

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
        ApplyStagingCapacity();   // 새 런 — 제단에서 보관함을 넓혔으면 칸 수가 다르다
    }

    /// <summary>보관함 바 폭(5칸 기준 고정).</summary>
    private static float StagingBarWidth =>
        StagingAreaView.SLOT_SPACING + 5 * (StagingAreaView.SLOT_WIDTH + StagingAreaView.SLOT_SPACING);

    /// <summary>
    /// 보관함 칸 수를 이번 런 값(<see cref="RunItemInventory.StagingCapacity"/>)으로 — 같은 바 폭에 칸 수만큼, 카드 폭을 나눠 넣는다.
    /// </summary>
    private void ApplyStagingCapacity()
    {
        if (_stagingArea == null) return;
        int cap = RunItemInventory.StagingCapacity;
        float w = Mathf.Min(StagingAreaView.SLOT_WIDTH,
                            (StagingBarWidth - StagingAreaView.SLOT_SPACING) / cap - StagingAreaView.SLOT_SPACING);
        _stagingArea.SetColumns(cap);
        _stagingArea.ApplyCapacity(cap, w);
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
        TryFlushPendingAdd();   // 보관함에 자리가 나면 대기 중이던 획득 아이템을 넣는다

        _stagingArea?.Refresh(_inventory);
        RefreshInfoPanelDefault();
        RefreshFooter();
        RefreshHeaderStatus();
    }

    /// <summary>
    /// 보관함이 가득 찬 상태에서 획득한 아이템을 보류했다가, 자리가 나면 자동으로 추가한다.
    /// (과거엔 AddToStaging 실패를 호출부가 무시해 <b>아이템이 조용히 사라졌다</b>.)
    /// </summary>
    private void TryFlushPendingAdd()
    {
        if (_pendingAddItem == null || _inventory == null) return;
        if (_inventory.StagingCount >= RunItemInventory.MaxStagingCapacity) return;

        var item = _pendingAddItem;
        _pendingAddItem = null;                 // 재진입 방지 — AddToStaging이 OnStagingChanged를 다시 부른다
        if (!_inventory.AddToStaging(item))
        {
            _pendingAddItem = item;             // 실패하면 다시 보류
            return;
        }

        ItemEffectVfxHelper.ShowNotice($"<color=#7FE7FF>보관함에 추가</color> {item.displayName}");
        _itemInfoPanel?.ShowItem(item, isNew: true);
    }

    private void OnPlacedChanged()
    {
        RefreshSynergyStatus();
        RefreshFooter();
        RefreshHeaderStatus();
    }

    // ── GridManager Event Bridge ──

    private void HandleItemPlaced(RuntimeItemData item)
    {
        Debug.Log($"[GridChk] 배치 수신: {item?.instanceId} (frame {Time.frameCount})");
        _inventory?.PlaceItem(item);
        _itemInfoPanel?.ShowItem(item, isNew: false);
        _totalPlacedCells += GetItemCellCount(item);

        // 룬을 놓았으면 속성 매칭 강조는 역할을 다했으므로 해제한다.
        _hexGridView?.SetPlacementElementHint(null);

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

            // 어느 칸이 어느 룬의 것인지 알려야 <b>조각 단위 외곽선</b>이 그려진다.
            // 안 넘기면 칸마다 테두리가 따로 떠서 같은 모양 조각이 붙었을 때 구분이 안 된다.
            _hexGridView.SetPieceGroups(_placedItemPositions);
            _hexGridView.UpdateAdjacencyConstraints();
        }

        // hexgrid가 _occupiedPositions와 Bridge를 갱신한 뒤 시너지 뷰 갱신
        RefreshSynergyStatus();
        RefreshFooter();
        RefreshPlacementHint();   // 방금 놓았으니 다음 룬의 놓을 자리를 보여준다
    }

    private void HandleItemRemoved(RuntimeItemData item)
    {
        Debug.Log($"[GridChk] 해제 수신: {item?.instanceId} (frame {Time.frameCount})");
        _inventory?.UnplaceItem(item);
        _totalPlacedCells = Mathf.Max(0, _totalPlacedCells - GetItemCellCount(item));

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
            _hexGridView.SetPieceGroups(_placedItemPositions);
            _hexGridView.UpdateAdjacencyConstraints();
        }

        RefreshSynergyStatus();
        RefreshFooter();
        RefreshPlacementHint();
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

    // ── 클릭 배치 ──
    // 드래그로 다중 칸 룬을 얹는 건 손이 커서 어려웠다. 보관함에서 룬을 고른 뒤 판의 칸을 누르면
    // 그 칸을 기준으로 놓인다. 판정·점유·통보는 드래그와 같은 경로(GridManager.TryPlaceShape)를 탄다.

    /// <summary>지금 놓으려는 룬. 보관함에서 고른 것 → 없으면 보관함 첫 룬.</summary>
    private RuntimeItemData PlacementTarget
        => _stagingArea?.HighlightedItem
           ?? (_inventory != null && _inventory.StagingCount > 0 ? _inventory.StagingItems[0] : null);

    private void HandleSquareClicked(GridSquare square)
    {
        if (square == null) return;

        // 이미 놓인 칸을 누르면 회수 — 클릭 조작만으로 배치/취소가 모두 되게 한다.
        if (square.isOccupied)
        {
            if (square.occupyingItem != null) RemovePlacedItem(square.occupyingItem);
            return;
        }

        var item = PlacementTarget;
        if (item == null) { ShowToast("보관함에서 룬을 먼저 고르세요", warn: true); return; }

        var shape = _stagingArea?.GetShapeForItem(item);
        if (shape == null) { ShowToast("이 룬의 모양 정보를 찾지 못했습니다", warn: true); return; }

        // 판 좌표계로 먼저 옮긴다. 주차 구역(shapeHost)은 스케일이 다르고 마스크에 잘려 있어,
        // 거기 둔 채로 위치를 계산하면 블록이 칸과 어긋난다. 블록은 그리드 gap 크기로 만들어져
        // 있으므로 판 위에서는 스케일 1이 정답이다.
        var gridHost = BoardManager.Instance?.gridHost;
        if (gridHost != null && shape.transform.parent != gridHost)
        {
            shape.transform.SetParent(gridHost, false);
            shape.transform.localScale = Vector3.one;
        }

        if (!GridManager.Instance.TryPlaceShapeAt(shape, square))
        {
            // 점유만 치우면 놓을 수 있는가 — 레전더리는 자기 존의 대부분을 먹으므로
            // 기존 룬을 손으로 하나씩 빼게 두면 보상이 노동이 된다. 한 번에 묻는다.
            if (TryOfferSalvagePlacement(shape)) return;

            BoardManager.Instance?.ReSlotAndReturn(shape);   // 주차 구역으로 되돌린다
            ShowToast("여기엔 놓을 수 없습니다", warn: true);
            return;
        }

        BoardManager.Instance?.OnShapePlaced(shape);
        GridManager.Instance.ClearPreview();
    }

    // ── 자리 비우고 배치 ─────────────────────────────────────

    private readonly List<RuntimeItemData> _salvageBlockers = new();
    private Shape _salvagePendingShape;

    /// <summary>
    /// 놓으려는 자리를 <b>기존 룬만</b>이 막고 있으면 "폐기하고 배치할까"를 묻는다.
    /// <para>속성 존 불일치나 판 밖처럼 <b>치워도 못 놓는</b> 경우는 묻지 않는다 —
    /// 헛되이 폐기시키면 그게 진짜 손실이다.</para>
    /// </summary>
    /// <returns>확인 창을 띄웠으면 true(호출부는 되돌리기를 하지 않는다).</returns>
    private bool TryOfferSalvagePlacement(Shape shape)
    {
        if (GridManager.Instance == null) return false;
        if (!GridManager.Instance.TryGetBlockingItems(shape, _salvageBlockers)) return false;
        if (_salvageBlockers.Count == 0) return false;

        int ore = 0;
        foreach (var it in _salvageBlockers) ore += RuneSalvage.OreValueOf(it);

        _salvagePendingShape = shape;
        if (_confirmDialogText != null)
            _confirmDialogText.text =
                $"이 자리의 룬 {_salvageBlockers.Count}개를 폐기하고 배치합니다.\n\n" +
                $"<color=#63D9C0>원석 +{ore}</color>  <size=80%>정제소에서 다시 뽑을 수 있다</size>";

        // 확인창을 <b>보관함 전량 폐기</b>와 공유하므로 버튼 동작을 이번 용도로 갈아끼운다.
        // 안 갈아끼우면 [폐기]가 보관함을 통째로 비운다.
        RebindConfirmDialog(OnSalvagePlacementConfirm, OnSalvagePlacementCancel);
        ShowConfirmDialog();
        return true;
    }

    /// <summary>확인창 버튼의 동작을 교체한다. 다이얼로그가 두 용도(전량 폐기 / 자리 비우고 배치)를 공유한다.</summary>
    private void RebindConfirmDialog(UnityEngine.Events.UnityAction onConfirm,
                                     UnityEngine.Events.UnityAction onCancel)
    {
        if (_confirmDialogDiscardBtn != null)
        {
            _confirmDialogDiscardBtn.onClick.RemoveAllListeners();
            _confirmDialogDiscardBtn.onClick.AddListener(onConfirm);
        }
        if (_confirmDialogKeepBtn != null)
        {
            _confirmDialogKeepBtn.onClick.RemoveAllListeners();
            _confirmDialogKeepBtn.onClick.AddListener(onCancel);
        }
    }

    /// <summary>확인창을 원래 용도(보관함 전량 폐기)로 되돌린다.</summary>
    private void RestoreConfirmDialogDefault()
        => RebindConfirmDialog(OnDialogDiscardAll, OnDialogKeep);

    /// <summary>확인 — 막고 있던 룬을 전부 폐기(원석 환원)하고 그 자리에 배치한다.</summary>
    private void OnSalvagePlacementConfirm()
    {
        HideConfirmDialog();
        RestoreConfirmDialogDefault();   // 다음 「전량 폐기」가 이 동작을 물려받지 않도록

        var shape = _salvagePendingShape;
        _salvagePendingShape = null;
        if (shape == null) return;

        int ore = 0;
        foreach (var item in _salvageBlockers)
        {
            RemovePlacedItem(item);                    // 판에서 내리고
            _inventory?.DiscardFromStaging(item);      // 보관함에서도 없앤다(진짜 폐기)
            _stagingArea?.RemoveShapeForItem(item);
            ore += RuneSalvage.Refund(item);
        }
        _salvageBlockers.Clear();

        if (GridManager.Instance != null && GridManager.Instance.TryPlaceShape(shape))
        {
            BoardManager.Instance?.OnShapePlaced(shape);
            GridManager.Instance.ClearPreview();
            ShowToast($"배치 완료 — 원석 +{ore}");
        }
        else
        {
            // 비웠는데도 못 놓는 경우(예상 밖) — 룬만 잃지 않도록 되돌린다.
            BoardManager.Instance?.ReSlotAndReturn(shape);
            ShowToast("배치에 실패했습니다", warn: true);
        }
    }

    private void OnSalvagePlacementCancel()
    {
        HideConfirmDialog();
        RestoreConfirmDialogDefault();
        var shape = _salvagePendingShape;
        _salvagePendingShape = null;
        _salvageBlockers.Clear();

        if (shape != null) BoardManager.Instance?.ReSlotAndReturn(shape);
    }

    /// <summary>판에서 룬을 회수해 보관함으로 되돌린다. 드래그로 빼낼 때와 같은 경로.</summary>
    private void RemovePlacedItem(RuntimeItemData item)
    {
        var shape = _stagingArea?.GetShapeForItem(item);
        if (shape == null) return;

        BoardManager.Instance?.OnShapePickedUp(shape);
        GridManager.Instance?.ReleaseShape(shape);   // 점유 해제 + OnItemRemoved 통보
        BoardManager.Instance?.ReSlotAndReturn(shape);
        GridManager.Instance?.ClearPreview();
    }

    private void HandleSquareHovered(GridSquare square)
    {
        if (GridManager.Instance == null) return;

        if (square == null || square.isOccupied) { GridManager.Instance.ClearPreview(); return; }

        var item  = PlacementTarget;
        var shape = item != null ? _stagingArea?.GetShapeForItem(item) : null;
        if (shape == null) { GridManager.Instance.ClearPreview(); return; }

        GridManager.Instance.PreviewShapeAt(shape, square);
    }

    private void OnStagingItemSelected(RuntimeItemData item)
    {
        _stagingArea?.HighlightItem(item);
        _itemInfoPanel?.ShowItem(item, isNew: false);

        // 고른 룬의 속성 존을 판에서 강조한다. 이게 없으면 판이 전 칸 균일하게 밝아
        // "어디에 놓을 수 있는지"가 화면에 전혀 안 나온다(빈 판은 모든 칸이 배치 가능이라 대비가 0).
        _hexGridView?.SetPlacementElementHint(item?.element, RuneZoneRule.NoCenter(item));
    }

    private void OnStagingItemHovered(RuntimeItemData item)
    {
        _itemInfoPanel?.ShowItem(item, isNew: false);
        if (item != null) _hexGridView?.SetPlacementElementHint(item.element, RuneZoneRule.NoCenter(item));
    }

    private void OnStagingItemUnhovered()
    {
        // 지금 <b>고른 룬</b>으로 되돌린다. 예전엔 보관함 첫 룬으로 되돌려(RefreshInfoPanelDefault) 3번 카드를 고르고
        // 판으로 손을 옮기면 판 강조·클릭 배치 대상이 1번으로 바뀌었다(09-27).
        var sel = _stagingArea?.HighlightedItem;
        bool inStorage = false;
        if (sel != null && _inventory != null)
            for (int i = 0; i < _inventory.StagingCount; i++)
                if (_inventory.StagingItems[i] == sel) { inStorage = true; break; }

        if (inStorage) _itemInfoPanel?.ShowItem(sel, isNew: false);
        else           RefreshInfoPanelDefault();
        RefreshPlacementHint();   // 호버 해제 → 지금 고른 룬 기준으로 되돌린다
    }

    /// <summary>
    /// 판의 배치 힌트를 <b>"플레이어가 다음에 놓을 룬"</b> 하나로 다시 맞춘다.
    ///
    /// 힌트를 세우는 곳이 여러 군데(패널 오픈·보관함 선택·호버)라 배치·제거 뒤에는
    /// 아무도 갱신하지 않아, 룬을 하나 놓고 나면 다음 룬의 놓을 자리가 표시되지 않았다.
    /// 기준을 한 줄로 못 박아 어느 경로로 들어오든 같은 결과가 나오게 한다.
    /// </summary>
    private void RefreshPlacementHint()
    {
        if (_hexGridView == null) return;

        var target = _pendingNewItem
                  ?? _pendingAddItem
                  ?? _stagingArea?.HighlightedItem
                  ?? (_inventory != null && _inventory.StagingCount > 0 ? _inventory.StagingItems[0] : null);

        _hexGridView.SetPlacementElementHint(target?.element, RuneZoneRule.NoCenter(target));
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
    }

    // ── Synergy Status Refresh ──

    private void RefreshSynergyStatus()
    {
        // 항상 최신 점유 상태에서 cluster를 계산한 뒤 갱신 (패널 재오픈 시 이전 결과 보존)
        _hexGridView?.RefreshOccupiedCells();
        _synergyStatusView?.SetZoneAmplifiers(_hexGridView?.GetZoneAmplifiers());   // 정제소 핵이 키우는 존 표시
        _synergyStatusView?.Refresh(MerlinRuneBridge.Instance?.GetZoneOccupiedCounts());
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

        var zoneCounts = MerlinRuneBridge.Instance?.GetZoneOccupiedCounts();

        var sb = new System.Text.StringBuilder("존 시너지  ");

        foreach (var zoneId in runeData.GetZoneIds())
        {
            var synergies = runeData.GetZoneSynergies(zoneId);
            if (synergies == null) continue;

            int count = (zoneCounts != null && zoneCounts.TryGetValue(zoneId, out var c)) ? c : 0;
            bool anyMet = false;
            foreach (var s in synergies)
                if (s.threshold > 0 && count >= s.threshold) { anyMet = true; break; }

            string hex = ElementDef.IdHex(zoneId);

            if (anyMet)
                sb.Append($"<color={hex}><b>●{zoneId}</b></color>  ");
            else
                sb.Append($"<color=#445566>◇{zoneId}</color>  ");
        }

        _footerActiveSynText.SetText(sb.ToString().TrimEnd());

        if (_footerCenterText != null)
        {
            bool centerActive = MerlinRuneBridge.Instance?.IsCenterBonusActive ?? false;
            _footerCenterText.text  = centerActive ? "◆ 중앙 보너스" : "◇ 중앙";
            _footerCenterText.color = centerActive
                ? new Color(1.00f, 0.88f, 0.40f, 0.95f)
                : new Color(0.50f, 0.55f, 0.70f, 0.55f);
        }
    }

    // ── Button Handlers ──

    private void OnBackClicked()
    {
        Managers.Sound?.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();
        ClosePanel();
    }

    /// <summary>
    /// 정제소 열기 — 판을 보다가 바로 특수 룬(존핵)을 벼린다. 만든 룬은 보관함으로 들어가 이 판에 배치된다.
    /// UI에 직접 만든 버튼의 onClick을 이 메서드로 지정하면 그리드를 거치지 않고 정제소를 연다(미리보기용).
    /// </summary>
    public void OpenRefinery() => OpenRefineryAsync().Forget();

    private void OnRefineryClicked() => OpenRefinery();

    private async UniTaskVoid OpenRefineryAsync()
    {
        try
        {
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_RefineryPanel>();
            if (panel == null) Debug.LogWarning("[UI_GridPanel] UI_RefineryPanel 로드 실패");
        }
        catch (System.OperationCanceledException) { }
    }

    private void OnResetClicked()
    {
        // 배치가 없으면 확인 대화상자를 띄울 것도 없다 — 다만 조용히 삼키면 버튼이 죽은 것으로 읽힌다.
        if (_totalPlacedCells == 0)
        {
            ShopUIStyle.PlaySfx("shop_reject");
            ShowToast("배치된 룬이 없습니다", warn: true);
            return;
        }
        Managers.Sound?.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();
        ShowResetConfirmDialog();
    }

    private void DoReset()
    {
        if (_inventory != null)
        {
            // ⚠️ 배치된 아이템은 PlacedItems에 있다(UnplaceItem이 placed → staging으로 되돌리는 구조).
            //    과거엔 StagingItems를 순회해서 배치된 건 하나도 안 잡혔고,
            //    셀만 지워져 '시너지는 사라졌는데 아이템 효과와 Shape는 남는' 상태가 됐다.
            var placed = new List<RuntimeItemData>(_inventory.PlacedItems);
            foreach (var item in placed)
            {
                _stagingArea?.RemoveShapeForItem(item);
                _inventory.UnplaceItem(item);   // PlacedItems → StagingItems 복귀
            }
        }
        _placedItemPositions.Clear();
        _totalPlacedCells = 0;
        _hexGridView?.ClearAllPlacedCells();
        _hexGridView?.UpdateAdjacencyConstraints();
        RefreshSynergyStatus();
        RefreshFooter();
        _stagingArea?.Refresh(_inventory);
    }

    private void OnConfirmClicked()
    {
        Managers.Sound?.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();

        if (_inventory == null || _inventory.StagingCount == 0)
        {
            ClosePanel();
            return;
        }

        int remaining = _inventory.StagingCount;
        if (_confirmDialogText != null)
        {
            // 합계 환원량을 먼저 보여준다 — "버려진다"만 있으면 손실로만 읽힌다.
            int ore = 0;
            foreach (var it in _inventory.StagingItems) ore += RuneSalvage.OreValueOf(it);

            _confirmDialogText.text =
                $"보관함에 아이템 {remaining}개가 있습니다.\n미배치 아이템은 폐기됩니다.\n\n" +
                $"<color=#63D9C0>원석 +{ore}</color>  <size=80%>정제소에서 다시 뽑을 수 있다</size>";
        }

        RestoreConfirmDialogDefault();   // 직전이 「자리 비우고 배치」였을 수 있다
        ShowConfirmDialog();
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
        int ore = 0;
        foreach (var item in toDiscard)
        {
            _stagingArea?.RemoveShapeForItem(item);
            _inventory.DiscardFromStaging(item);
            ore += RuneSalvage.Refund(item);   // 전량 폐기도 원석으로 환원된다
        }
        if (ore > 0) Debug.Log($"[GridPanel] 보관함 전량 폐기 → 원석 +{ore}");

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
        panelRT.sizeDelta        = new Vector2(420f, 170f);
        panelRT.anchoredPosition = Vector2.zero;
        panelGO.AddComponent<Image>().color = new Color(0.10f, 0.11f, 0.17f, 0.98f);

        var textGO = MakeTxt(panelGO.transform, "Text",
            "배치된 룬을 모두 보관함으로 되돌립니다.", 18f, new Color(0.9f, 0.92f, 1f, 1f));
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0f, 0.44f);
        textRT.anchorMax = new Vector2(1f, 1f);
        textRT.sizeDelta = Vector2.zero;
        textGO.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.Center;

        _resetDialogNo = MakeButton(panelGO.transform, "CancelBtn",
            new Vector2(0.08f, 0.10f), new Vector2(0.45f, 0.40f),
            Vector2.zero, Vector2.zero,
            new Color(0.25f, 0.27f, 0.35f, 1f), "취소");

        _resetDialogYes = MakeButton(panelGO.transform, "ResetBtn",
            new Vector2(0.55f, 0.10f), new Vector2(0.92f, 0.40f),
            Vector2.zero, Vector2.zero,
            new Color(0.65f, 0.20f, 0.18f, 1f), "초기화");

        _resetDialog.SetActive(false);
    }

    private void ShowResetConfirmDialog()
    {
        if (_resetDialog == null) return;
        _resetDialog.transform.SetAsLastSibling();     // 판에 놓인 룬보다 위
        _resetDialog.SetActive(true);
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

    // ── 룬 선택 규격 크롬 (09-25) ──

    /// <summary>
    /// 룬 선택 팝업의 아트(제목 띠·카드 바탕)와 공통 버튼을 입힌다. 스킨은 앱 부트에서 비동기로 들어오고
    /// 이 패널은 그보다 먼저(@UIRoot Awake) 지어지므로, 열 때 한 번 입힌다. 스킨이 없으면 색 폴백 그대로.
    /// </summary>
    private void EnsureChromeSkin()
    {
        if (_chromeSkinned) return;
        var skin = UISkin.RuneSelect;
        if (skin == null) return;
        _chromeSkinned = true;

        ShopUIStyle.Skin(_headerBandImg, skin.titleBar,      sliced: true);
        // 행동 버튼 = 전 화면 공통 베벨(금 = 완료 · 먹빛 = 초기화). 예전엔 룬 획득의 선택/넘기기 아트를 빌렸는데
        // 거기 글자가 구워져 있어 「완료」「초기화」 밑에 「선택」「넘기기」가 비쳤다(09-28 UI 톤 진단).
        UITheme.StyleButton(_confirmBtnImg, UITheme.CtaTint);
        UITheme.StyleButton(_resetBtnImg,   UITheme.SecondaryTint);
        ShopUIStyle.Skin(_cardBgImg,     skin.cardFill,      sliced: true);
    }

    /// <summary>머리줄 상태(보관함 N/5 · 배치 M)와 보관함 바 개수. 인벤토리가 바뀔 때마다.</summary>
    private void RefreshHeaderStatus()
    {
        int cap    = RunItemInventory.StagingCapacity;
        int staged = _inventory?.StagingCount ?? 0;
        int placed = _inventory?.PlacedItems?.Count ?? 0;
        _headerStatusText?.SetText($"보관함 <color=#FFFFFF>{staged}/{cap}</color>    ·    배치 <color=#FFFFFF>{placed}</color>");
        _stagingCountText?.SetText($"{staged} / {cap}");
    }

    /// <summary>
    /// 열기 연출 — 제목 띠가 위에서, 왼쪽·오른쪽 칸이 옆에서 살짝 미끄러져 들어오고 보관함 카드가 차례로 올라온다.
    /// 룬판은 자주 여는 화면이라 전체 0.45초 안(룬 선택 팝업처럼 멈추지 않는다). 판 자체는 움직이지 않는다 —
    /// 놓인 룬 조각이 판과 따로 그려져 판만 움직이면 어긋난다.
    /// </summary>
    private async UniTaskVoid PlayOpenFxAsync()
    {
        _openFxCts?.Cancel();
        _openFxCts?.Dispose();
        _openFxCts = new CancellationTokenSource();
        var ct = _openFxCts.Token;

        _stagingArea?.PlayDealIn(0.06f);
        try
        {
            for (float t = 0f; t < 1f; )
            {
                t += Time.unscaledDeltaTime / OpenSlideDur;
                float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
                float r = 1f - k;
                if (_headerRT     != null) _headerRT.anchoredPosition     = _headerRest + new Vector2(0f, 14f * r);
                if (_leftPanelRT  != null) _leftPanelRT.anchoredPosition  = _leftRest   + new Vector2(-20f * r, 0f);
                if (_rightPanelRT != null) _rightPanelRT.anchoredPosition = _rightRest  + new Vector2(20f * r, 0f);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (System.OperationCanceledException) { }
        finally
        {
            if (_headerRT     != null) _headerRT.anchoredPosition     = _headerRest;
            if (_leftPanelRT  != null) _leftPanelRT.anchoredPosition  = _leftRest;
            if (_rightPanelRT != null) _rightPanelRT.anchoredPosition = _rightRest;
        }
    }

    /// <summary>오른쪽 아래 행동 버튼. 아트가 오기 전엔 색 판(주 = 청색, 보조 = 어두운 판).</summary>
    private static Button MakeActionButton(Transform parent, string name, string label,
                                           float xMin, float xMax, bool primary, out Image img)
    {
        var go = Go(name);
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(xMin, 0f);
        rt.anchorMax        = new Vector2(xMax, 0f);
        rt.pivot            = new Vector2(0.5f, 0f);
        rt.sizeDelta        = new Vector2(0f, 64f);
        rt.anchoredPosition = new Vector2(0f, 8f);

        img = go.AddComponent<Image>();
        img.color = primary ? new Color(0.20f, 0.36f, 0.55f, 1f) : new Color(0.18f, 0.20f, 0.28f, 1f);
        var btn = go.AddComponent<Button>();
        ShopUIStyle.ApplyButtonColors(btn, img);

        var lblGO = MakeTxt(go.transform, "Label", label, primary ? 24f : 22f,
            primary ? new Color(0.98f, 0.95f, 0.86f, 1f) : new Color(0.82f, 0.86f, 0.96f, 1f), bold: true);
        var lblRT = lblGO.GetComponent<RectTransform>();
        lblRT.anchorMin = Vector2.zero;
        lblRT.anchorMax = Vector2.one;
        lblRT.offsetMin = lblRT.offsetMax = Vector2.zero;
        lblGO.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.Center;
        return btn;
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
        t.textWrappingMode = TextWrappingModes.NoWrap;
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
        txt.fontSize      = 18f;
        txt.color         = Color.white;
        txt.alignment     = TextAlignmentOptions.Center;
        txt.raycastTarget = false;
        txt.textWrappingMode = TextWrappingModes.NoWrap;

        return btn;
    }

    /// <summary>
    /// 판 <b>바깥 테두리</b>만 액자로 두른다(조각 8개: 가로 변 2 · 세로 변 2 · 코너 4).
    ///
    /// 칸 자체에는 아트를 깔지 않는다 — 이 판은 존 색으로 어느 칸이 무슨 속성인지 읽는 UI라,
    /// 예전에 판 배경 그림을 깔았다가 존 타일이 텍스처에 묻혀 되돌린 적이 있다(위 주석 참고).
    /// 액자는 판 밖으로만 나가므로 그 가독성을 건드리지 않는다.
    /// </summary>
    private void BuildBoardFrame(RectTransform board)
    {
        var lib = RuneArt.Library;
        if (lib == null || !lib.HasBoardFrame || board == null) return;

        var root = new GameObject("BoardFrame", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(board, false);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        // 코너는 원본 비율 그대로, 변은 코너 사이를 늘려 채운다.
        const float CW = 46f, CH = 44f;   // 코너 조각 76×74 → 판 크기에 맞춘 표시치
        const float EW = 24f;             // 변 두께

        Corner(root, "TL", lib.FrameTL, new Vector2(0f, 1f), CW, CH);
        Corner(root, "TR", lib.FrameTR, new Vector2(1f, 1f), CW, CH);
        Corner(root, "BL", lib.FrameBL, new Vector2(0f, 0f), CW, CH);
        Corner(root, "BR", lib.FrameBR, new Vector2(1f, 0f), CW, CH);

        Edge(root, "Top",    lib.FrameTop,    true,  1f, CW, EW);
        Edge(root, "Bottom", lib.FrameBottom, true,  0f, CW, EW);
        Edge(root, "Left",   lib.FrameLeft,   false, 0f, CH, EW);
        Edge(root, "Right",  lib.FrameRight,  false, 1f, CH, EW);
    }

    private static void Corner(RectTransform parent, string name, Sprite art, Vector2 anchor, float w, float h)
    {
        var go = new GameObject("Frame_" + name, typeof(RectTransform), typeof(CanvasRenderer));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.sprite = art;
        img.raycastTarget = false;   // 액자가 칸 클릭·드롭을 먹으면 배치가 막힌다
    }

    /// <summary>변 조각 — 코너 사이를 늘린다. horizontal=true면 가로 변(상/하), false면 세로 변(좌/우).</summary>
    private static void Edge(RectTransform parent, string name, Sprite art,
                             bool horizontal, float side, float cornerInset, float thickness)
    {
        var go = new GameObject("Frame_" + name, typeof(RectTransform), typeof(CanvasRenderer));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);

        if (horizontal)
        {
            rt.anchorMin = new Vector2(0f, side);
            rt.anchorMax = new Vector2(1f, side);
            rt.pivot     = new Vector2(0.5f, side);
            rt.offsetMin = new Vector2(cornerInset, 0f);
            rt.offsetMax = new Vector2(-cornerInset, 0f);
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, thickness);
        }
        else
        {
            rt.anchorMin = new Vector2(side, 0f);
            rt.anchorMax = new Vector2(side, 1f);
            rt.pivot     = new Vector2(side, 0.5f);
            rt.offsetMin = new Vector2(0f, cornerInset);
            rt.offsetMax = new Vector2(0f, -cornerInset);
            rt.sizeDelta = new Vector2(thickness, rt.sizeDelta.y);
        }
        rt.anchoredPosition = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.sprite = art;
        img.type   = Image.Type.Sliced;   // 변은 늘어난다 — 보더 없으면 통짜로 늘어난다
        img.raycastTarget = false;
    }


}
