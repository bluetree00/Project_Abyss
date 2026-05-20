using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 그리드/보관함 오버레이 메인 컨트롤러.
///
/// ■ Canvas_Overlay/@Overlay 하위에 사전 배치 (DDOL UIRoot 소속).
/// ■ TAB 키 또는 외부 호출(ShowWithNewItem)로 열린다.
/// ■ GridManager.OnItemPlaced/Removed ↔ RunItemInventory.PlaceItem/UnplaceItem 브리지.
/// ■ [확인] 버튼: 보관함 잔여 아이템 경고 후 닫힘.
/// </summary>
public sealed class UI_GridPanel : UI_Base
{
    public static UI_GridPanel Instance { get; private set; }

    public RectTransform BoardContainer => boardContainer;
    public bool IsOpen => _isOpen;

    // ── SerializeField ──
    [Header("Sub-Views")]
    [SerializeField] private GridGalleryView galleryView;
    [SerializeField] private GridEditView    editView;
    [SerializeField] private StagingAreaView stagingArea;
    [SerializeField] private ItemInfoPanel   itemInfoPanel;

    [Header("보드 컨테이너 (puzzlePrefab 스폰 위치, editView 밖에 배치)")]
    [SerializeField] private RectTransform boardContainer;

    [Header("Buttons")]
    [SerializeField] private UnityEngine.UI.Button confirmButton;

    [Header("Confirm Dialog")]
    [SerializeField] private GameObject confirmDialog;
    [SerializeField] private UnityEngine.UI.Button confirmDialogKeepBtn;
    [SerializeField] private UnityEngine.UI.Button confirmDialogDiscardBtn;
    [SerializeField] private TMPro.TMP_Text confirmDialogText;

    // ── Private ──
    private RunItemInventory _inventory;
    private RuntimeItemData  _pendingNewItem;
    private bool             _isOpen;

    // ── Gallery Detail Panel (코드로 생성) ──
    private GameObject   _galleryDetailPanel;
    private TMP_Text     _galleryDetailTitle;
    private Transform    _galleryDetailList;
    private Button       _galleryEditBtn;
    private string       _selectedGalleryGridId;
    private readonly List<GameObject> _galleryDetailRows = new();

    // ── Synergy Toast ──
    private GameObject             _synergyToast;
    private TMP_Text               _synergyToastText;
    private CancellationTokenSource _toastCts;

    // ── Lifecycle ──
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildSynergyToast();
        BuildGalleryDetailPanel();
        EnsureConfirmButtonLabel();
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        _toastCts?.Cancel();
        _toastCts?.Dispose();
    }

    private void OnEnable()
    {
        BindInventory();

        if (GridManager.Instance != null)
        {
            GridManager.Instance.OnItemPlaced    += HandleItemPlaced;
            GridManager.Instance.OnItemRemoved   += HandleItemRemoved;
            GridManager.Instance.OnItemSelected  += HandleItemSelected;
        }

        if (BlockSynergyBridge.Instance != null)
            BlockSynergyBridge.Instance.OnSynergyActivated += ShowSynergyActivated;

        if (galleryView != null)
            galleryView.OnGridSelected += RefreshGalleryDetail;

        if (stagingArea != null)
            stagingArea.OnItemSelected += OnStagingItemSelected;

        if (confirmButton != null)
            confirmButton.onClick.AddListener(OnConfirmClicked);
        if (confirmDialogKeepBtn != null)
            confirmDialogKeepBtn.onClick.AddListener(OnDialogKeep);
        if (confirmDialogDiscardBtn != null)
            confirmDialogDiscardBtn.onClick.AddListener(OnDialogDiscardAll);
    }

    private void OnDisable()
    {
        UnbindInventory();

        if (GridManager.Instance != null)
        {
            GridManager.Instance.OnItemPlaced    -= HandleItemPlaced;
            GridManager.Instance.OnItemRemoved   -= HandleItemRemoved;
            GridManager.Instance.OnItemSelected  -= HandleItemSelected;
        }

        if (BlockSynergyBridge.Instance != null)
            BlockSynergyBridge.Instance.OnSynergyActivated -= ShowSynergyActivated;

        if (galleryView != null)
            galleryView.OnGridSelected -= RefreshGalleryDetail;

        if (stagingArea != null)
            stagingArea.OnItemSelected -= OnStagingItemSelected;

        _toastCts?.Cancel();

        if (confirmButton != null)
            confirmButton.onClick.RemoveListener(OnConfirmClicked);
        if (confirmDialogKeepBtn != null)
            confirmDialogKeepBtn.onClick.RemoveListener(OnDialogKeep);
        if (confirmDialogDiscardBtn != null)
            confirmDialogDiscardBtn.onClick.RemoveListener(OnDialogDiscardAll);

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

    // ── Open / Close ──

    private void OpenPanel()
    {
        _isOpen = true;
        Time.timeScale = 0f;
        gameObject.SetActive(true);
        GameRunBootstrapper.Instance?.Run?.EnterGridSynergy();

        BindInventory();
        _pendingNewItem = null; // 갤러리 모드에서는 staging area 슬롯으로 표시되므로 별도 처리 불필요
        stagingArea?.Refresh(_inventory);
        EnterGalleryMode();
    }

    private void ClosePanel()
    {
        _isOpen = false;
        Time.timeScale = 1f;
        HideConfirmDialog();
        GameRunBootstrapper.Instance?.Run?.ExitGridSynergy();

        editView?.Deactivate();

        gameObject.SetActive(false);
    }

    // ── Mode Switching ──

    public void EnterGalleryMode()
    {
        boardContainer?.gameObject.SetActive(false);
        galleryView?.gameObject.SetActive(true);
        editView?.gameObject.SetActive(false);

        // 패널 전환: 갤러리 상세 보이기, ItemInfoPanel 숨기기
        if (_galleryDetailPanel != null) _galleryDetailPanel.SetActive(true);
        itemInfoPanel?.gameObject.SetActive(false);

        galleryView?.Refresh();
        galleryView?.RefreshThumbnails();

        // 첫 번째 그리드 자동 선택
        var firstGridId = galleryView?.GetSelectedGridId();
        if (!string.IsNullOrEmpty(firstGridId))
            RefreshGalleryDetail(firstGridId);
    }

    public void EnterEditMode(string gridId)
    {
        boardContainer?.gameObject.SetActive(true);
        galleryView?.gameObject.SetActive(false);
        editView?.gameObject.SetActive(true);

        // 패널 전환: ItemInfoPanel 보이기, 갤러리 상세 숨기기
        if (_galleryDetailPanel != null) _galleryDetailPanel.SetActive(false);
        itemInfoPanel?.gameObject.SetActive(true);

        editView?.Activate(gridId);
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
        stagingArea?.Refresh(_inventory);
        RefreshInfoPanelDefault();
    }

    private void OnPlacedChanged() { }

    // ── GridManager Event Bridge ──

    private void HandleItemPlaced(RuntimeItemData item)
    {
        _inventory?.PlaceItem(item);
        itemInfoPanel?.ShowItem(item, isNew: false);
        galleryView?.RefreshThumbnails();
        editView?.RefreshSynergyText();
    }

    private void HandleItemRemoved(RuntimeItemData item)
    {
        _inventory?.UnplaceItem(item);
        galleryView?.RefreshThumbnails();
    }

    private void HandleItemSelected(RuntimeItemData item)
    {
        itemInfoPanel?.ShowItem(item, isNew: false);
    }

    private void OnStagingItemSelected(RuntimeItemData item)
    {
        stagingArea?.HighlightItem(item);

        // 갤러리 모드일 때: 갤러리 상세 패널 ↔ ItemInfoPanel 교체
        if (_galleryDetailPanel != null && _galleryDetailPanel.activeSelf)
        {
            _galleryDetailPanel.SetActive(false);
            itemInfoPanel?.gameObject.SetActive(true);
        }

        itemInfoPanel?.ShowItem(item, isNew: false);
    }

    // ── Info Panel Default ──

    private void RefreshInfoPanelDefault()
    {
        if (itemInfoPanel == null) return;

        // 보관함에 아이템이 있으면 첫 번째 표시 (슬라이드 진입) + 카드 하이라이트
        if (_inventory != null && _inventory.StagingCount > 0)
        {
            var firstItem = _inventory.StagingItems[0];
            itemInfoPanel.ShowItem(firstItem, isNew: false, slideIn: true);
            stagingArea?.HighlightItem(firstItem);
        }
        else
        {
            itemInfoPanel.ShowEmpty();
            stagingArea?.HighlightItem(null);
        }
    }

    // ── Confirm Button ──

    private void OnConfirmClicked()
    {
        if (_inventory == null || _inventory.StagingCount == 0)
        {
            ClosePanel();
            return;
        }

        // 보관함 잔여 경고
        int remaining = _inventory.StagingCount;
        if (confirmDialogText != null)
            confirmDialogText.text = $"보관함에 아이템 {remaining}개가 있습니다.\n미배치 아이템은 폐기됩니다.";

        ShowConfirmDialog();
    }

    private void OnDialogKeep()
    {
        HideConfirmDialog();
        // 메모리 레이아웃: [계속 배치] → 편집 뷰 유지 (현재 뷰 상태 그대로)
    }

    private void OnDialogDiscardAll()
    {
        HideConfirmDialog();
        if (_inventory == null) { ClosePanel(); return; }

        // 보관함 아이템 전체 폐기 → 대응 Shape도 제거
        var toDiscard = new System.Collections.Generic.List<RuntimeItemData>(_inventory.StagingItems);
        foreach (var item in toDiscard)
        {
            stagingArea?.RemoveShapeForItem(item);
            _inventory.DiscardFromStaging(item);
        }

        ClosePanel();
    }

    private void ShowConfirmDialog()
    {
        if (confirmDialog != null) confirmDialog.SetActive(true);
    }

    private void HideConfirmDialog()
    {
        if (confirmDialog != null) confirmDialog.SetActive(false);
    }

    // ── Gallery Detail Panel ──

    private void BuildGalleryDetailPanel()
    {
        _galleryDetailPanel = new GameObject("GalleryDetailPanel", typeof(RectTransform));
        _galleryDetailPanel.transform.SetParent(transform, false);

        // itemInfoPanel과 동일 앵커 영역 (우측 36% 폭, 상단 보관함 위까지)
        var rt = _galleryDetailPanel.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.64f, 0.18f);
        rt.anchorMax        = new Vector2(1f,    0.92f);
        rt.offsetMin        = new Vector2(8f,    0f);
        rt.offsetMax        = new Vector2(-8f,   0f);

        // 패널 배경
        var bg = _galleryDetailPanel.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.1f, 0.14f, 0.95f);

        // 타이틀 텍스트
        var titleGO = new GameObject("Title", typeof(RectTransform));
        titleGO.transform.SetParent(_galleryDetailPanel.transform, false);
        var titleRT = titleGO.GetComponent<RectTransform>();
        titleRT.anchorMin        = new Vector2(0f, 0.85f);
        titleRT.anchorMax        = new Vector2(1f, 1f);
        titleRT.sizeDelta        = Vector2.zero;
        titleRT.anchoredPosition = Vector2.zero;
        _galleryDetailTitle = titleGO.AddComponent<TextMeshProUGUI>();
        _galleryDetailTitle.fontSize        = 15f;
        _galleryDetailTitle.alignment       = TextAlignmentOptions.Center;
        _galleryDetailTitle.color           = new Color(0.85f, 0.92f, 1f, 1f);
        _galleryDetailTitle.raycastTarget   = false;
        _galleryDetailTitle.enableWordWrapping = false;

        // 아이템 목록 스크롤 영역
        var listGO = new GameObject("ItemList", typeof(RectTransform));
        listGO.transform.SetParent(_galleryDetailPanel.transform, false);
        var listRT = listGO.GetComponent<RectTransform>();
        listRT.anchorMin        = new Vector2(0f, 0.18f);
        listRT.anchorMax        = new Vector2(1f, 0.84f);
        listRT.sizeDelta        = Vector2.zero;
        listRT.anchoredPosition = Vector2.zero;
        var listBG = listGO.AddComponent<Image>();
        listBG.color = new Color(0f, 0f, 0f, 0.1f);

        // VerticalLayoutGroup for item rows
        var vlg = listGO.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        vlg.childAlignment       = TextAnchor.UpperLeft;
        vlg.spacing              = 4f;
        vlg.padding              = new RectOffset(6, 6, 6, 6);
        vlg.childControlWidth    = true;
        vlg.childControlHeight   = false;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        _galleryDetailList = listGO.transform;

        // [편집하기] 버튼
        var editBtnGO = new GameObject("EditBtn", typeof(RectTransform));
        editBtnGO.transform.SetParent(_galleryDetailPanel.transform, false);
        var editBtnRT = editBtnGO.GetComponent<RectTransform>();
        editBtnRT.anchorMin        = new Vector2(0.1f, 0.02f);
        editBtnRT.anchorMax        = new Vector2(0.9f, 0.16f);
        editBtnRT.sizeDelta        = Vector2.zero;
        editBtnRT.anchoredPosition = Vector2.zero;
        var editBtnBG = editBtnGO.AddComponent<Image>();
        editBtnBG.color = new Color(0.2f, 0.45f, 0.8f, 0.9f);
        _galleryEditBtn = editBtnGO.AddComponent<Button>();
        _galleryEditBtn.targetGraphic = editBtnBG;

        var editBtnLblGO = new GameObject("Label", typeof(RectTransform));
        editBtnLblGO.transform.SetParent(editBtnGO.transform, false);
        var editBtnLblRT = editBtnLblGO.GetComponent<RectTransform>();
        editBtnLblRT.anchorMin = Vector2.zero;
        editBtnLblRT.anchorMax = Vector2.one;
        editBtnLblRT.sizeDelta = Vector2.zero;
        var editBtnTxt = editBtnLblGO.AddComponent<TextMeshProUGUI>();
        editBtnTxt.text          = "이 그리드 편집하기 →";
        editBtnTxt.fontSize      = 14f;
        editBtnTxt.alignment     = TextAlignmentOptions.Center;
        editBtnTxt.color         = Color.white;
        editBtnTxt.raycastTarget = false;

        _galleryDetailPanel.SetActive(false);
    }

    private void RefreshGalleryDetail(string gridId)
    {
        _selectedGalleryGridId = gridId;

        // 갤러리 상세 패널 복귀 (보관함 아이템을 보고 있었다면 원래대로)
        _galleryDetailPanel?.SetActive(true);
        itemInfoPanel?.gameObject.SetActive(false);
        stagingArea?.HighlightItem(null);

        // 기존 행 제거
        foreach (var row in _galleryDetailRows)
            if (row != null) Destroy(row);
        _galleryDetailRows.Clear();

        // 타이틀 업데이트
        if (_galleryDetailTitle != null)
        {
            string gridName = gridId;
            var bridge = BlockSynergyBridge.Instance;
            if (bridge != null)
            {
                var grids = bridge.GetRegisteredGrids();
                if (grids != null && grids.TryGetValue(gridId, out var data))
                    gridName = data.displayName ?? gridId;
            }
            _galleryDetailTitle.text = $"{gridName}  배치된 아이템";
        }

        // 배치된 아이템 목록
        var items = BoardManager.Instance?.GetPlacedItemsForGrid(gridId);
        if (items != null && _galleryDetailList != null)
        {
            foreach (var item in items)
            {
                var row = BuildGalleryDetailRow(item);
                row.transform.SetParent(_galleryDetailList, false);
                _galleryDetailRows.Add(row);
            }
        }

        // [편집하기] 버튼 연결
        if (_galleryEditBtn != null)
        {
            _galleryEditBtn.onClick.RemoveAllListeners();
            var capturedId = gridId;
            _galleryEditBtn.onClick.AddListener(() => EnterEditMode(capturedId));
        }
    }

    private GameObject BuildGalleryDetailRow(RuntimeItemData item)
    {
        var rowGO = new GameObject($"Row_{item.instanceId[..Mathf.Min(8, item.instanceId.Length)]}", typeof(RectTransform));
        var rowRT = rowGO.GetComponent<RectTransform>();
        rowRT.sizeDelta = new Vector2(0f, 50f);

        var rowBG = rowGO.AddComponent<Image>();
        rowBG.color = new Color(0.1f, 0.12f, 0.18f, 0.6f);

        // 레어도 색 라인 (좌측)
        var rarityBarGO = new GameObject("RarityBar", typeof(RectTransform));
        rarityBarGO.transform.SetParent(rowGO.transform, false);
        var rarityBarRT = rarityBarGO.GetComponent<RectTransform>();
        rarityBarRT.anchorMin        = new Vector2(0f, 0f);
        rarityBarRT.anchorMax        = new Vector2(0f, 1f);
        rarityBarRT.sizeDelta        = new Vector2(4f, 0f);
        rarityBarRT.anchoredPosition = Vector2.zero;
        var rarityBarImg = rarityBarGO.AddComponent<Image>();
        rarityBarImg.color = RarityColor(item.rarity);

        // 아이콘
        var iconGO = new GameObject("Icon", typeof(RectTransform));
        iconGO.transform.SetParent(rowGO.transform, false);
        var iconRT = iconGO.GetComponent<RectTransform>();
        iconRT.anchorMin        = new Vector2(0f, 0.1f);
        iconRT.anchorMax        = new Vector2(0f, 0.9f);
        iconRT.pivot            = new Vector2(0f, 0.5f);
        iconRT.sizeDelta        = new Vector2(36f, 0f);
        iconRT.anchoredPosition = new Vector2(8f, 0f);
        var iconImg = iconGO.AddComponent<Image>();
        if (item.icon != null)
        {
            iconImg.sprite         = item.icon;
            iconImg.preserveAspect = true;
        }
        else
        {
            iconImg.color = new Color(0.3f, 0.3f, 0.4f, 0.5f);
        }

        // 이름 텍스트
        var nameTxtGO = new GameObject("Name", typeof(RectTransform));
        nameTxtGO.transform.SetParent(rowGO.transform, false);
        var nameTxtRT = nameTxtGO.GetComponent<RectTransform>();
        nameTxtRT.anchorMin        = new Vector2(0f, 0.2f);
        nameTxtRT.anchorMax        = new Vector2(1f, 0.8f);
        nameTxtRT.sizeDelta        = new Vector2(-56f, 0f);
        nameTxtRT.anchoredPosition = new Vector2(52f, 0f);
        var nameTxt = nameTxtGO.AddComponent<TextMeshProUGUI>();
        nameTxt.text              = item.displayName ?? item.itemId;
        nameTxt.fontSize          = 12f;
        nameTxt.alignment         = TextAlignmentOptions.MidlineLeft;
        nameTxt.color             = new Color(0.85f, 0.9f, 1f, 1f);
        nameTxt.raycastTarget     = false;
        nameTxt.enableWordWrapping = false;

        return rowGO;
    }

    private static Color RarityColor(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Rare      => new Color(0.3f, 0.6f, 1f,   1f),
        ItemRarity.Epic      => new Color(0.7f, 0.3f, 1f,   1f),
        ItemRarity.Legendary => new Color(1f,   0.7f, 0.2f, 1f),
        _                    => new Color(0.7f, 0.7f, 0.7f, 1f),
    };

    // ── Confirm Button Label ──

    private void EnsureConfirmButtonLabel()
    {
        if (confirmButton == null) return;

        // 배경 이미지가 없으면 추가
        var img = confirmButton.GetComponent<UnityEngine.UI.Image>();
        if (img == null) img = confirmButton.gameObject.AddComponent<UnityEngine.UI.Image>();
        img.color = new Color(0.18f, 0.42f, 0.72f, 0.95f);
        confirmButton.targetGraphic = img;

        // 텍스트 자식이 없으면 생성, 있으면 정렬·색상·내용 강제 보정
        var existing = confirmButton.GetComponentInChildren<TMPro.TMP_Text>();
        if (existing != null)
        {
            if (string.IsNullOrEmpty(existing.text)) existing.text = "배치 완료";
            existing.color     = Color.white;
            existing.fontSize  = 16f;
            existing.alignment = TMPro.TextAlignmentOptions.Center;
            existing.raycastTarget = false;
        }
        else
        {
            var txtGO = new GameObject("Label", typeof(RectTransform));
            txtGO.transform.SetParent(confirmButton.transform, false);
            var txtRT = txtGO.GetComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero;
            txtRT.anchorMax = Vector2.one;
            txtRT.sizeDelta = Vector2.zero;
            var txt = txtGO.AddComponent<TMPro.TextMeshProUGUI>();
            txt.text          = "배치 완료";
            txt.fontSize      = 16f;
            txt.alignment     = TMPro.TextAlignmentOptions.Center;
            txt.color         = Color.white;
            txt.raycastTarget = false;
        }
    }

    // ── Synergy Toast ──

    private void BuildSynergyToast()
    {
        _synergyToast = new GameObject("SynergyToast", typeof(RectTransform));
        _synergyToast.transform.SetParent(transform, false);

        var rt = _synergyToast.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.01f, 0.52f);
        rt.anchorMax        = new Vector2(0.64f, 0.62f);
        rt.offsetMin        = Vector2.zero;
        rt.offsetMax        = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;

        var bg = _synergyToast.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.14f, 0.08f, 0.93f);

        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(_synergyToast.transform, false);
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.sizeDelta = new Vector2(-12f, -8f);

        _synergyToastText = textGO.AddComponent<TextMeshProUGUI>();
        _synergyToastText.fontSize       = 15f;
        _synergyToastText.alignment      = TextAlignmentOptions.Center;
        _synergyToastText.color          = new Color(0.3f, 1f, 0.55f, 1f);
        _synergyToastText.raycastTarget  = false;
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
}
