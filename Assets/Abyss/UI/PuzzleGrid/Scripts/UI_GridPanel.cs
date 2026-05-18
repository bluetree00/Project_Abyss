using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

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

    // ── SerializeField ──
    [Header("Sub-Views")]
    [SerializeField] private GridGalleryView galleryView;
    [SerializeField] private GridEditView    editView;
    [SerializeField] private StagingAreaView stagingArea;
    [SerializeField] private ItemInfoPanel   itemInfoPanel;

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

    // ── Lifecycle ──
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        gameObject.SetActive(false);
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

        if (confirmButton != null)
            confirmButton.onClick.RemoveListener(OnConfirmClicked);
        if (confirmDialogKeepBtn != null)
            confirmDialogKeepBtn.onClick.RemoveListener(OnDialogKeep);
        if (confirmDialogDiscardBtn != null)
            confirmDialogDiscardBtn.onClick.RemoveListener(OnDialogDiscardAll);

        HideConfirmDialog();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
        {
            if (_isOpen) ClosePanel();
            else         OpenPanel();
        }
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
        gameObject.SetActive(true);

        BindInventory();
        EnterGalleryMode();

        // 우측 패널: 새 아이템 우선 → 보관함 첫 번째 아이템 → 빈 상태
        if (_pendingNewItem != null)
        {
            itemInfoPanel?.ShowItem(_pendingNewItem, isNew: true);
            _pendingNewItem = null;
        }
        else
        {
            RefreshInfoPanelDefault();
        }

        stagingArea?.Refresh(_inventory);
    }

    private void ClosePanel()
    {
        _isOpen = false;
        HideConfirmDialog();

        editView?.Deactivate();
        BlockSynergyBridge.Instance?.DeactivatePanelGrid();

        gameObject.SetActive(false);
    }

    // ── Mode Switching ──

    public void EnterGalleryMode()
    {
        BlockSynergyBridge.Instance?.DeactivatePanelGrid();
        galleryView?.gameObject.SetActive(true);
        editView?.gameObject.SetActive(false);
        galleryView?.Refresh();
    }

    public void EnterEditMode(string gridId)
    {
        BlockSynergyBridge.Instance?.ActivatePanelGrid();
        galleryView?.gameObject.SetActive(false);
        editView?.gameObject.SetActive(true);
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

    // ── Info Panel Default ──

    private void RefreshInfoPanelDefault()
    {
        if (itemInfoPanel == null) return;

        // 보관함에 아이템이 있으면 첫 번째 표시 (슬라이드 진입)
        if (_inventory != null && _inventory.StagingCount > 0)
        {
            itemInfoPanel.ShowItem(_inventory.StagingItems[0], isNew: false, slideIn: true);
        }
        else
        {
            itemInfoPanel.ShowEmpty();
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
}
