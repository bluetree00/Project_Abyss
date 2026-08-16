using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cysharp.Threading.Tasks;

/// <summary>
/// 아이템 획득 팝업.
///
/// ■ Canvas_Popup에 Addressable로 로드된다.
///   키: "UI/Popup/UI_ItemAcquisitionPopup"
/// ■ [그리드 열기] → 아이템을 보관함에 추가 + UI_GridPanel 열기.
/// ■ [거부]        → 아이템 폐기, 팝업 닫힘.
/// </summary>
public sealed class UI_ItemAcquisitionPopup : UI_Popup
{
    // ── SerializeField ──
    [Header("아이템 정보")]
    [SerializeField] private Image       itemIcon;
    [SerializeField] private TMP_Text    itemNameText;
    [SerializeField] private TMP_Text    rarityText;
    [SerializeField] private Transform   effectListRoot;
    [SerializeField] private RectTransform shapePreviewRoot;

    [Header("버튼")]
    [SerializeField] private Button openGridButton;
    [SerializeField] private Button rejectButton;

    [Header("폰트")]
    [SerializeField] private TMP_FontAsset popupFont;

    // ── Private ──
    private RuntimeItemData            _item;
    private RunItemInventory           _inventory;
    private UniTaskCompletionSource    _interactionTcs;

    // 등급 연출 대상. 루트는 UI_Popup의 열기 애니가 이미 잡고 있어 창 본체(Panel)를 쓴다.
    private RectTransform _panelRt;
    private Image         _panelImage;
    private TMP_Text      _rejectLabel;

    private static readonly Color COLOR_RISK      = new(1f,    0.35f, 0.35f, 1f);
    private static readonly Color COLOR_NORMAL_FX = new(0.85f, 0.92f, 1f,   1f);
    private static readonly Color COLOR_COMMON    = new(0.75f, 0.75f, 0.75f, 1f);
    private static readonly Color COLOR_RARE      = new(0.3f,  0.6f,  1f,   1f);
    private static readonly Color COLOR_EPIC      = new(0.7f,  0.3f,  1f,   1f);
    private static readonly Color COLOR_LEGENDARY = new(1f,    0.7f,  0.2f, 1f);

    private const float MINI_CELL_SIZE = 26f;
    private const float MINI_CELL_GAP  = 3f;

    /// <summary>
    /// 룬 선택 팝업(<see cref="UI_RuneSelectPopup"/>) 대비 연출 강도. 이 알림은 훨씬 자주 뜨므로
    /// 같은 스펙을 이만큼 눌러 쓴다 — 등급 차등은 남기고 자극만 줄인다.
    /// </summary>
    private const float JUICE_SCALE = 0.7f;

    // ── Lifecycle ──
    public override void Init()
    {
        base.Init();

        if (openGridButton != null)
            openGridButton.onClick.AddListener(OnOpenGridClicked);
        if (rejectButton != null)
        {
            rejectButton.onClick.AddListener(OnRejectClicked);
            _rejectLabel = rejectButton.GetComponentInChildren<TMP_Text>(true);
        }

        // 연출 대상 해석(1회). 못 찾으면 연출만 빠지고 기능은 그대로다.
        if (transform.Find("Panel") is RectTransform panel)
        {
            _panelRt = panel;
            panel.TryGetComponent(out _panelImage);
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();   // 차단 잠금 누수 방지(UI_Popup) — private OnDestroy는 이걸 가린다

        if (openGridButton != null)
            openGridButton.onClick.RemoveListener(OnOpenGridClicked);
        if (rejectButton != null)
            rejectButton.onClick.RemoveListener(OnRejectClicked);
    }

    // ── Public API ──

    /// <summary>버튼 클릭 즉시 resolve — 애니메이션 완료를 기다리지 않는다.</summary>
    public UniTask WaitForInteractionAsync(System.Threading.CancellationToken ct)
    {
        _interactionTcs = new UniTaskCompletionSource();
        return _interactionTcs.Task.AttachExternalCancellation(ct);
    }

    /// <summary>
    /// 팝업 데이터 설정.
    /// UIManager.ShowPopupUIAndGetAsync&lt;UI_ItemAcquisitionPopup&gt;() 후 호출.
    /// </summary>
    public void Setup(RuntimeItemData item, RunItemInventory inventory)
    {
        _item      = item;
        _inventory = inventory;

        if (item == null) { ClosePopupUI(); return; }

        // 아이콘
        if (itemIcon != null)
        {
            itemIcon.sprite  = item.icon;
            itemIcon.enabled = item.icon != null;
        }

        // 이름
        if (itemNameText != null)
            itemNameText.text = item.displayName ?? item.itemId;

        // 레어도
        if (rarityText != null)
        {
            // 등급 라벨은 보상 연출과 같은 표기를 쓴다 — 여기 로컬 표는 Epic·Legendary가 같은 ◆라
            // 최상위가 형태로 구별되지 않았다(RewardPresentation.RarityLabel이 ★로 분리한 그 문제).
            rarityText.text  = RewardPresentation.RarityLabel(item.rarity);
            rarityText.color = RarityColor(item.rarity);
        }

        // 효과 목록
        BuildEffectList(item);

        // Shape 미니 프리뷰
        BuildShapePreview(item);

        // 거부의 대가를 버튼에 병기한다 — 확인 창은 끼우지 않는다(원클릭 유지).
        ApplyRejectWarning();

        // 등급 차등 등장. 결과는 이미 확정이고 여기선 "얼마나 크게 보여줄지"만 정한다.
        PresentAsync(item.rarity).Forget();
    }

    // ── Button Handlers ──

    private void OnOpenGridClicked()
    {
        Managers.Sound?.PlayEffectAsync(SoundKey.Sfx.UiButton).Forget();
        _interactionTcs?.TrySetResult();

        if (_item == null || _inventory == null) { ClosePopupUI(); return; }

        // 보관함에 추가. 가득 차면 실패하는데, 과거엔 이 실패를 무시하고 팝업만 닫아
        // <b>아이템이 조용히 사라졌다</b> → 실패 시 그리드로 넘겨 '보류' 상태로 들고 있게 한다.
        bool added = _inventory.AddToStaging(_item);
        if (!added)
            ItemEffectVfxHelper.ShowNotice(
                $"<color=#FFCC44>보관함 가득 참</color> ({RunItemInventory.MaxStagingCapacity}칸) — 자리를 비우면 자동으로 추가됩니다");

        ClosePopupUI();

        // 프리팹이 비활성 상태여서 Instance가 null인 경우 ShowOverlayUI로 Awake를 트리거
        if (UI_GridPanel.Instance == null)
            Managers.UI?.ShowOverlayUI<UI_GridPanel>();

        if (UI_GridPanel.Instance == null) return;

        if (added) UI_GridPanel.Instance.ShowWithNewItem(_item);      // 강조하며 열기
        else       UI_GridPanel.Instance.ShowWithPendingItem(_item);  // 자리 나면 자동 추가
    }

    private void OnRejectClicked()
    {
        // 획득과 소리로 갈린다 — 낮은 피치가 "버렸다"는 확정감을 준다. 흐름은 그대로 원클릭.
        Managers.Sound?.PlayEffectAsync(SoundKey.Sfx.UiButton, 1f, 0.72f).Forget();
        if (_rejectLabel != null)
            UIJuice.FlashAsync(_rejectLabel, COLOR_RISK, 0.10f, 1, destroyCancellationToken).Forget();

        _interactionTcs?.TrySetResult();
        ClosePopupUI();
    }

    // ── Presentation ──

    /// <summary>
    /// 거부 버튼에 "돌아오는 것이 없다"를 병기한다.
    ///
    /// 보관함 폐기(<see cref="RuneSalvage"/>)와 달리 <b>이 경로엔 원석 환원이 없다</b> —
    /// 아이템이 인벤토리에 들어가기 전에 버려지기 때문이다. 그래서 환원량 대신 소실을 적는다.
    /// 확인 창을 끼우지 않는 이유는 원클릭 거부가 이 팝업의 속도이기 때문 — 정보만 늘린다.
    /// </summary>
    private void ApplyRejectWarning()
    {
        if (_rejectLabel == null) return;

        _rejectLabel.richText = true;
        _rejectLabel.text     = "거부\n<size=68%><color=#FF9A9A>환원 없이 사라짐</color></size>";
    }

    /// <summary>
    /// 등급 차등 등장 연출. <see cref="RewardPresentation"/> 스펙을 그대로 쓰되
    /// <see cref="JUICE_SCALE"/>만큼 눌러 쓴다(§C-2 스펙표는 룬 선택 팝업 기준).
    ///
    /// Common은 스펙상 플래시·펄스가 0이라 조용히 뜨고, Legendary만 회전·2회 플래시·볼륨 펄스까지 붙는다.
    /// <b>어느 버튼도 이 연출을 기다리지 않는다</b> — 획득·거부는 처음부터 눌린다(§C-1 스킵 규칙).
    /// </summary>
    private async UniTaskVoid PresentAsync(ItemRarity rarity)
    {
        var spec = RewardPresentation.For(rarity);
        if (spec.CardPopDuration <= 0f) return;   // 연출 끔

        var ct = destroyCancellationToken;
        try
        {
            Managers.Sound?.PlayEffectAsync(SoundKey.Sfx.UiButton, 0.5f, spec.SfxPitch).Forget();

            if (_panelRt != null)
                await UIJuice.PopInAsync(_panelRt, null, spec.CardPopDuration,
                                         fromScale:   1f - 0.10f * JUICE_SCALE,
                                         fromYOffset: -18f * JUICE_SCALE,
                                         rotZ:        spec.CardPopRotation * JUICE_SCALE, ct);

            // 프레임 플래시 — 창 바탕을 등급색 쪽으로 당겼다 되돌린다. 완전 치환이 아니라
            // 55% 혼합이라 아이콘·글자가 색에 묻히지 않는다.
            if (spec.FlashPulses > 0 && _panelImage != null)
                UIJuice.FlashAsync(_panelImage,
                                   Color.Lerp(_panelImage.color, RewardPresentation.FrameColor(rarity), 0.55f),
                                   0.18f * JUICE_SCALE, spec.FlashPulses, ct).Forget();

            if (spec.PulsePeak > 0f)
                VolumePulseService.Pulse(spec.PulsePeak * JUICE_SCALE, spec.PulseDuration);
        }
        catch (System.OperationCanceledException) { }
    }

    // ── Effect List ──

    private void BuildEffectList(RuntimeItemData item)
    {
        if (effectListRoot == null || item.effects == null) return;

        for (int i = effectListRoot.childCount - 1; i >= 0; i--)
            Destroy(effectListRoot.GetChild(i).gameObject);

        var style = EffectRowStyle.Default;
        style.fontAsset       = popupFont;
        style.fontSize        = 14f;
        style.iconSize        = 18f;
        style.rowHeight       = 22f;
        style.usePrefixArrows = true;
        style.normalColor     = COLOR_NORMAL_FX;
        style.riskColor       = COLOR_RISK;

        foreach (var slot in item.effects)
        {
            if (string.IsNullOrEmpty(slot.effectType)) continue;
            EffectRowWidget.Create(effectListRoot, style, slot);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(effectListRoot as RectTransform);
    }

    // ── Shape Preview ──

    private void BuildShapePreview(RuntimeItemData item)
    {
        if (shapePreviewRoot == null || item.shapeId == 0) return;

        for (int i = shapePreviewRoot.childCount - 1; i >= 0; i--)
            Destroy(shapePreviewRoot.GetChild(i).gameObject);

        var blockData = Managers.RuneData;
        if (blockData == null) return;

        var shapeEntry = blockData.GetShape(item.shapeId);
        if (shapeEntry == null) return;

        var offsets = RuneDataManager.ParseCellOffsets(shapeEntry);
        if (offsets == null || offsets.Length == 0) return;

        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;
        foreach (var o in offsets)
        {
            if (o.x < minX) minX = o.x;  if (o.x > maxX) maxX = o.x;
            if (o.y < minY) minY = o.y;  if (o.y > maxY) maxY = o.y;
        }
        int cols = maxX - minX + 1;
        int rows = maxY - minY + 1;

        float totalW = cols * (MINI_CELL_SIZE + MINI_CELL_GAP) - MINI_CELL_GAP;
        float totalH = rows * (MINI_CELL_SIZE + MINI_CELL_GAP) - MINI_CELL_GAP;
        float startX = -totalW * 0.5f + MINI_CELL_SIZE * 0.5f;
        float startY =  totalH * 0.5f - MINI_CELL_SIZE * 0.5f;

        foreach (var o in offsets)
        {
            int col = o.x - minX;
            int row = maxY - o.y;

            var cellGO = new GameObject($"Cell_{o.x}_{o.y}", typeof(RectTransform), typeof(Image));
            cellGO.transform.SetParent(shapePreviewRoot, false);

            var rt  = cellGO.GetComponent<RectTransform>();
            rt.sizeDelta        = Vector2.one * MINI_CELL_SIZE;
            rt.anchoredPosition = new Vector2(
                startX + col * (MINI_CELL_SIZE + MINI_CELL_GAP),
                startY - row * (MINI_CELL_SIZE + MINI_CELL_GAP));

            var img  = cellGO.GetComponent<Image>();
            img.color         = new Color(0.3f, 0.85f, 0.45f, 0.9f);
            img.raycastTarget = false;
        }
    }

    // ── Helpers ──

    private static Color RarityColor(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Rare      => COLOR_RARE,
        ItemRarity.Epic      => COLOR_EPIC,
        ItemRarity.Legendary => COLOR_LEGENDARY,
        _                    => COLOR_COMMON,
    };
}
