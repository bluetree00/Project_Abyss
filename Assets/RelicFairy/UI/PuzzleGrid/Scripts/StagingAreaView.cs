using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 보관함 하단 패널.
///
/// ■ 5개 고정 슬롯을 항상 표시 (빈 슬롯 포함).
/// ■ RunItemInventory.StagingItems 기준으로 슬롯 0..n 에 아이템 카드 렌더링.
/// ■ 신규 아이템: 빛나는 테두리 + NEW 뱃지.
/// ■ [X] 버튼: 해당 아이템 폐기 (UI_GridPanel.OnDialogDiscardAll과 별개, 개별 폐기).
/// ■ 카드 클릭: ItemInfoPanel에 해당 아이템 정보 표시.
/// ■ Shape 생성: 카드 생성 시 RuneDataManager → ShapeAssetSO → BoardManager.SpawnSharedShape.
/// </summary>
public sealed class StagingAreaView : MonoBehaviour
{
    // ── Constants ──
    // 디자이너 슬롯 아트는 가로형(룬 배치 테두리 326×214@2x, 비율 1.523)이다 —
    // 예전엔 세로형 128×156이라 아트를 얹으면 프레임이 세로로 늘어났다.
    //
    // 한 줄 가로 배치는 5칸 합이 875라 폭 470짜리 뷰포트에 2.7칸만 들어가고,
    // 대신 세로로 200px 넘게 남았다. 2열 그리드로 바꿔 5칸을 한눈에 보이게 한다
    // (열 폭 = (뷰포트 470 - 여백 3×10) / 2 = 220 → 높이 220/1.523 ≈ 144).
    private const int   SLOT_COLUMNS  = 2;
    private int _columns = SLOT_COLUMNS;   // Init 전에 SetColumns로 바꾼다 — 하단 보관함 바는 5열 1행(의뢰서 F4)
    // 09-25 UX: 220 → 272. 보관함 바 오른쪽이 260px 넘게 비어 있었다 — 그 폭을 카드 글자(이름·효과)에 준다.
    // 슬롯 테두리 아트(326×214)는 이 비율보다 좁아 빈 칸에서만 비율을 지켜 가운데에 세운다(BuildFixedSlots).
    public  const float SLOT_WIDTH    = 272f;
    public  const float SLOT_HEIGHT   = 144f;
    public  const float SLOT_SPACING  = 14f;
    private const float GRID_CELL_SIZE = 120f;
    private const float SELECT_LIFT   = 8f;     // 고른 카드가 들리는 높이
    private const float DEAL_RISE     = 40f;    // 열 때 아래에서 올라오는 거리
    private const float DEAL_DUR      = 0.20f;
    private const float DEAL_STAGGER  = 0.045f; // 룬판은 자주 열어 짧게 — 5칸이 다 올라와도 0.45초 안
    private const float HOVER_SCALE   = 1.03f;  // 손을 얹은 카드 — 「잡을 수 있다」
    private const float DRAG_GHOST    = 0.4f;   // 끄는 동안 원래 카드(잔상)

    /// <summary>고정 슬롯을 2열로 깔았을 때 필요한 줄 수.</summary>
    private int SlotRows =>
        (RunItemInventory.MaxStagingCapacity + _columns - 1) / _columns;

    private static readonly Color COLOR_NEW_BORDER      = new(1f, 0.92f, 0.3f, 1f);
    private static readonly Color COLOR_NORMAL_BORDER   = new(0.4f, 0.4f, 0.5f, 0.7f);
    private static readonly Color COLOR_SELECTED_BORDER = new(0.3f, 0.85f, 1f, 1f);
    private static readonly Color HoverGlowColor        = new(1f, 0.84f, 0.45f, 0.85f);
    private static readonly Color COLOR_EMPTY_BG        = new(0.14f, 0.14f, 0.20f, 0.7f);
    private static readonly Color COLOR_EMPTY_BORDER    = new(0.3f, 0.3f, 0.4f, 0.4f);
    private static readonly Color FilledTint            = new(0.34f, 0.35f, 0.42f, 1f);   // 룬이 든 칸의 바탕 아트 곱
    private static readonly Color NameInk               = new(0.98f, 0.97f, 0.93f, 1f);
    private static readonly Color SelectedNameInk       = new(1f, 0.84f, 0.45f, 1f);

    // ── SerializeField ──
    [Header("스크롤 컨텐츠")]
    [SerializeField] private RectTransform scrollContent;

    [Header("카드 폰트 (없으면 기본 폰트)")]
    [SerializeField] private TMP_FontAsset cardFont;

    [Header("참조")]
    [SerializeField] private BoardManager boardManager;

    // ── Events ──
    /// <summary>보관함 카드 클릭 시 발생. UI_GridPanel에서 패널 전환을 처리한다.</summary>
    public event System.Action<RuntimeItemData> OnItemSelected;
    /// <summary>보관함 카드 호버 시 발생.</summary>
    public event System.Action<RuntimeItemData> OnItemHovered;
    /// <summary>보관함 카드 호버 해제 시 발생.</summary>
    public event System.Action                  OnItemUnhovered;

    // ── Private ──
    // 고정 슬롯 구조
    private readonly GameObject[] _slotGOs       = new GameObject[RunItemInventory.MaxStagingCapacity];
    private readonly RuntimeItemData[] _slotItems = new RuntimeItemData[RunItemInventory.MaxStagingCapacity];

    // Shape 관리
    private readonly Dictionary<string, Shape> _shapeByInstanceId = new();
    private readonly HashSet<string>           _newItemIds         = new();

    // 섹션 구분선
    private GameObject _sectionDivider;

    // 선택 하이라이트 (현재 하이라이트된 슬롯 인덱스)
    private RuntimeItemData _highlightedItem;

    /// <summary>지금 고른(강조된) 룬. 판이 "이 룬을 어디 놓을 수 있는지"를 칠하는 기준.</summary>
    public RuntimeItemData HighlightedItem => _highlightedItem;

    // 슬롯 아트(룬 배치 테두리 바탕 / 룬 배치 테두리). UI_GridPanel이 Init 전에 주입한다.
    private Sprite _slotFillSprite;
    private Sprite _slotFrameSprite;

    /// <summary>슬롯 한 칸의 바탕·테두리 아트를 주입한다. 미주입이면 기존 색 박스로 그린다.</summary>
    public void SetSlotSkin(Sprite fill, Sprite frame)
    {
        _slotFillSprite  = fill;
        _slotFrameSprite = frame;
    }

    // 열기 연출
    private System.Threading.CancellationTokenSource _dealCts;

    // 폐기 확인 다이얼로그
    private RuntimeItemData _pendingDiscard;
    private GameObject      _discardDialog;
    private TMP_Text        _discardDialogText;

    // ── Lifecycle ──
    private void Awake()
    {
        if (boardManager == null)
            boardManager = BoardManager.Instance;
        BuildDiscardDialog();
    }

    // ── Public Init ──

    /// <summary>코드로 생성 시 scrollContent를 주입하고 슬롯을 빌드한다. UI_GridPanel에서 AddComponent 직후 호출.</summary>
    /// <summary>슬롯 격자 열 수. <see cref="Init"/> 전에 불러야 한다(슬롯은 Init에서 놓인다).</summary>
    public void SetColumns(int columns) => _columns = Mathf.Max(1, columns);

    public void Init(RectTransform content)
    {
        scrollContent = content;
        BuildFixedSlots();
    }

    private void OnDestroy()
    {
        _dealCts?.Cancel();
        _dealCts?.Dispose();
        HideDiscardDialog();
        _shapeByInstanceId.Clear();
        _newItemIds.Clear();
    }

    // ── Public API ──

    /// <summary>인벤토리 기반으로 슬롯을 동기화한다. UI_GridPanel.OnStagingChanged에서 호출.</summary>
    public void Refresh(RunItemInventory inventory)
    {
        if (inventory == null) return;

        var stagingItems = inventory.StagingItems;

        // NEW 감지 (전체 목록 선행 스캔)
        foreach (var item in stagingItems)
        {
            if (item == null || _newItemIds.Contains(item.instanceId)) continue;
            bool wasKnown = false;
            for (int j = 0; j < RunItemInventory.MaxStagingCapacity; j++)
                if (_slotItems[j] == item) { wasKnown = true; break; }
            if (!wasKnown) _newItemIds.Add(item.instanceId);
        }

        // 섹션 정렬: 미배치(non-new) 먼저, 새로 얻은 아이템 나중
        var nonNew  = new List<RuntimeItemData>();
        var newList = new List<RuntimeItemData>();
        foreach (var item in stagingItems)
        {
            if (item == null) continue;
            if (_newItemIds.Contains(item.instanceId)) newList.Add(item);
            else nonNew.Add(item);
        }
        var sorted = new List<RuntimeItemData>(nonNew);
        sorted.AddRange(newList);

        for (int i = 0; i < RunItemInventory.MaxStagingCapacity; i++)
        {
            RuntimeItemData item = i < sorted.Count ? sorted[i] : null;
            _slotItems[i] = item;
            RefreshSlotDisplay(i, item);
            if (item != null) EnsureShapeExists(item);
        }

        UpdateSectionDivider(nonNew.Count, newList.Count);

        // 더 이상 보관함에 없는 아이템의 추적 정보만 해제
        var activeIds = new HashSet<string>();
        foreach (var it in stagingItems)
            if (it != null) activeIds.Add(it.instanceId);

        var toRemove = new List<string>();
        foreach (var kvp in _shapeByInstanceId)
            if (!activeIds.Contains(kvp.Key)) toRemove.Add(kvp.Key);

        foreach (var id in toRemove)
        {
            _shapeByInstanceId.Remove(id);
            _newItemIds.Remove(id);
        }
    }

    /// <summary>카드 테두리를 하이라이트한다. RefreshInfoPanelDefault에서 선택된 아이템을 표시할 때 호출.</summary>
    public void HighlightItem(RuntimeItemData item)
    {
        // 이전 하이라이트 해제 — 기준을 먼저 바꾼 뒤 칠한다(들림·등급빛이 새 기준으로 계산되게)
        var prev = _highlightedItem;
        _highlightedItem = item;

        if (prev != null && prev != item)
        {
            int prevIdx = FindSlotIndex(prev);
            if (prevIdx >= 0) ApplySlotBorderColor(prevIdx, prev);
        }

        if (item != null)
        {
            int idx = FindSlotIndex(item);
            if (idx >= 0) ApplySlotBorderColor(idx, item);
        }
    }

    /// <summary>
    /// 패널을 열 때 룬이 든 칸을 아래에서 차례로 올린다(룬 선택 카드 등장과 같은 70ms 간격). 빈 칸은 움직이지 않는다.
    /// 룬판은 자주 여는 화면이라 전체가 0.45초 안에 끝나게 짧게 둔다. 표시 전용 — 선택·인벤토리 상태는 그대로다.
    /// </summary>
    public void PlayDealIn(float delay)
    {
        _dealCts?.Cancel();
        _dealCts?.Dispose();
        _dealCts = new System.Threading.CancellationTokenSource();
        int order = 0;
        for (int i = 0; i < RunItemInventory.MaxStagingCapacity; i++)
        {
            if (_slotItems[i] == null || _slotGOs[i] == null) continue;
            DealSlotAsync(i, delay + order * DEAL_STAGGER, _dealCts.Token).Forget();
            order++;
        }
    }

    private async UniTaskVoid DealSlotAsync(int index, float delay, System.Threading.CancellationToken ct)
    {
        var go = _slotGOs[index];
        var rt = go.GetComponent<RectTransform>();
        // ?? 금지 — 에디터의 GetComponent는 없을 때 가짜 null을 돌려줘 ??를 통과한다(MissingComponentException, 09-25 실측)
        if (!go.TryGetComponent<CanvasGroup>(out var cg)) cg = go.AddComponent<CanvasGroup>();
        try
        {
            cg.alpha = 0f;
            rt.anchoredPosition = SlotRestPos(index) + new Vector2(0f, -DEAL_RISE);
            if (delay > 0f) await UniTask.Delay(System.TimeSpan.FromSeconds(delay), ignoreTimeScale: true, cancellationToken: ct);
            for (float t = 0f; t < 1f; )
            {
                t += Time.unscaledDeltaTime / DEAL_DUR;
                float p = Mathf.Clamp01(t);
                float e = 1f - Mathf.Pow(1f - p, 3f);
                cg.alpha = p;
                rt.anchoredPosition = SlotRestPos(index) + new Vector2(0f, -DEAL_RISE * (1f - e));   // 도중에 골라도 들림을 따라간다
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (System.OperationCanceledException) { }
        finally
        {
            if (cg != null) cg.alpha = 1f;
            if (rt != null) rt.anchoredPosition = SlotRestPos(index);
        }
    }

    /// <summary>
    /// 이 룬의 슬롯을 한 번 튕긴다. <b>표시 전용</b> — 선택 상태·인벤토리 어느 것도 건드리지 않는다.
    /// 「끌어다 놓으세요」 안내가 어느 칸을 말하는지 가리키는 용도(P0-3).
    /// </summary>
    public void PulseItem(RuntimeItemData item)
    {
        int idx = FindSlotIndex(item);
        if (idx < 0 || _slotGOs[idx] == null) return;

        PulseSlotAsync(_slotGOs[idx].GetComponent<RectTransform>()).Forget();
    }

    /// <summary>이 아이템에 연결된 Shape. 클릭 배치가 "무엇을 놓을지" 찾는 데 쓴다. 없으면 null.</summary>
    public Shape GetShapeForItem(RuntimeItemData item)
        => item != null && _shapeByInstanceId.TryGetValue(item.instanceId, out var s) ? s : null;

    /// <summary>특정 아이템의 Shape를 제거한다. 폐기 시 UI_GridPanel에서 호출.</summary>
    public void RemoveShapeForItem(RuntimeItemData item)
    {
        if (item == null) return;
        if (_shapeByInstanceId.TryGetValue(item.instanceId, out var shape))
        {
            if (boardManager == null) boardManager = BoardManager.Instance;
            boardManager?.RemoveSharedShape(shape);
            _shapeByInstanceId.Remove(item.instanceId);
        }
    }

    /// <summary>
    /// BuildFixedSlots가 만든 슬롯 골격 자식인가. 아이템 카드 컨텐츠를 갈아끼울 때
    /// 파괴하면 안 되는 이름들 — 슬롯을 다시 빌드하지 않으므로 한 번 잃으면 복구되지 않는다.
    /// </summary>
    private static bool IsSlotChrome(string childName)
        => childName == "Border" || childName == "EmptyLabel" || childName == "Frame" || childName == "HoverGlow";

    /// <summary>슬롯 본체의 테두리선 색을 바꾼다(테두리는 Border가 아니라 슬롯 자신에 붙어 있다).</summary>
    private static void SetSlotOutline(GameObject slotGO, Color color)
    {
        if (slotGO == null) return;
        if (slotGO.TryGetComponent<Outline>(out var ol)) ol.effectColor = color;
    }

    // ── Fixed Slot Build ──

    private void BuildFixedSlots()
    {
        if (scrollContent == null) return;

        for (int i = 0; i < RunItemInventory.MaxStagingCapacity; i++)
        {
            var slotGO = new GameObject($"Slot_{i}", typeof(RectTransform));
            slotGO.transform.SetParent(scrollContent, false);
            var rt = slotGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(SLOT_WIDTH, SLOT_HEIGHT);
            PositionSlot(rt, i);

            // 빈 슬롯 배경 — 테두리선은 여기 붙인다.
            // 예전엔 Border 이미지가 슬롯 전체를 반투명하게 한 번 더 덮어 배경과 두 겹으로 겹쳤고,
            // 그래서 빈 칸이 두꺼운 색 블록처럼 보였다(장식 프레임 위에서 특히 지저분했다).
            var bg = slotGO.AddComponent<Image>();
            if (_slotFillSprite != null)
            {
                // 바탕(287×204)은 슬롯과 비율이 달라 통째로 늘리면 가로로 8% 왜곡된다.
                // 스프라이트에 9슬라이스 경계(24px)를 넣어 모서리는 그대로 두고 가운데만 늘린다.
                bg.sprite = _slotFillSprite;
                bg.type   = Image.Type.Sliced;
                bg.color  = Color.white;
            }
            else
            {
                bg.color = COLOR_EMPTY_BG;
            }

            // 아트가 자체 테두리를 갖고 있으면 코드 외곽선은 붙이지 않는다(이중 테두리 방지).
            if (_slotFrameSprite == null)
            {
                var slotOutline = slotGO.AddComponent<Outline>();
                slotOutline.effectColor    = COLOR_EMPTY_BORDER;
                slotOutline.effectDistance = new Vector2(2f, -2f);
            }

            // Border — 이제 '선택/보유 상태 틴트' 전용 레이어다(평시엔 투명).
            var borderGO = new GameObject("Border", typeof(RectTransform));
            borderGO.transform.SetParent(slotGO.transform, false);
            var borderRT = borderGO.GetComponent<RectTransform>();
            borderRT.anchorMin = Vector2.zero;
            borderRT.anchorMax = Vector2.one;
            borderRT.sizeDelta = Vector2.zero;
            var borderImg = borderGO.AddComponent<Image>();
            borderImg.color = Color.clear;
            borderImg.raycastTarget = false;

            // 장식 테두리 아트 — 틴트 레이어보다 위, 카드 내용보다 아래.
            if (_slotFrameSprite != null)
            {
                var frameGO = new GameObject("Frame", typeof(RectTransform));
                frameGO.transform.SetParent(slotGO.transform, false);
                var frameRT = frameGO.GetComponent<RectTransform>();
                frameRT.anchorMin = Vector2.zero;
                frameRT.anchorMax = Vector2.one;
                frameRT.sizeDelta = Vector2.zero;
                var frameImg = frameGO.AddComponent<Image>();
                frameImg.sprite = _slotFrameSprite;
                // 테두리 아트(326×214)는 9슬라이스 경계가 없어 늘리면 모서리 장식이 찌그러진다.
                // 슬롯이 272로 넓어져(09-25) 비율이 달라졌으므로 비율을 지켜 가운데에 세운다.
                // 이 테두리는 <b>빈 칸</b> 표시다 — 룬이 들면 등급 보석 테두리로 바뀐다(RefreshSlotDisplay).
                frameImg.type           = Image.Type.Simple;
                frameImg.preserveAspect = true;
                frameImg.raycastTarget  = false;
            }

            // 빈 슬롯 텍스트 (기본 표시)
            var emptyTxtGO = new GameObject("EmptyLabel", typeof(RectTransform));
            emptyTxtGO.transform.SetParent(slotGO.transform, false);
            var emptyRT = emptyTxtGO.GetComponent<RectTransform>();
            emptyRT.anchorMin = Vector2.zero;
            emptyRT.anchorMax = Vector2.one;
            emptyRT.sizeDelta = Vector2.zero;
            var emptyTxt = emptyTxtGO.AddComponent<TextMeshProUGUI>();
            if (cardFont != null) emptyTxt.font = cardFont;
            emptyTxt.text      = "빈 칸";
            emptyTxt.fontSize  = 16f;
            emptyTxt.color     = new Color(0.55f, 0.55f, 0.66f, 0.85f);
            emptyTxt.alignment = TextAlignmentOptions.Center;
            emptyTxt.raycastTarget = false;

            var hover = slotGO.AddComponent<SlotHoverHandler>();
            hover.onEnter = item => OnItemHovered?.Invoke(item);
            hover.onExit  = ()   => OnItemUnhovered?.Invoke();
            hover.owner   = this;
            hover.index   = i;

            // 카드에서 바로 끌어 판에 놓는다. 주차 구역(레거시 패널)은 감춰 둔 채,
            // 이 카드가 그 셰이프의 손잡이 역할을 한다 — 드래그 본체는 Shape가 그대로 처리한다.
            var drag = slotGO.AddComponent<SlotDragHandler>();
            drag.owner = this;

            _slotGOs[i] = slotGO;
        }

        BuildSectionDivider();

        // scrollContent 크기 = 2열 그리드 전체 크기. 뷰포트(약 470×630) 안에 들어가므로
        // 실제로는 스크롤이 걸리지 않고 5칸이 모두 보인다.
        scrollContent.sizeDelta = new Vector2(
            SLOT_SPACING + _columns * (SLOT_WIDTH  + SLOT_SPACING),
            SLOT_SPACING + SlotRows     * (SLOT_HEIGHT + SLOT_SPACING));
    }

    private void RefreshSlotDisplay(int index, RuntimeItemData item)
    {
        if (index < 0 || index >= RunItemInventory.MaxStagingCapacity) return;
        var slotGO = _slotGOs[index];
        if (slotGO == null) return;

        // 이전 shimmer 컴포넌트 제거 (ShimmerMask 자식은 아래 루프에서 함께 제거됨)
        var prevShimmer = slotGO.GetComponent<StagingSlotShimmer>();
        if (prevShimmer != null) Destroy(prevShimmer);

        // 기존 아이템 컨텐츠 제거 — BuildFixedSlots가 만든 슬롯 골격은 남긴다.
        // "Frame"(디자이너 룬 배치 테두리)이 예외에 빠져 있어서 첫 Refresh 때 5칸 아트가
        // 통째로 파괴됐다. 골격 이름은 한곳에서만 관리한다(IsSlotChrome).
        for (int i = slotGO.transform.childCount - 1; i >= 0; i--)
        {
            var child = slotGO.transform.GetChild(i);
            if (!IsSlotChrome(child.name))
                Destroy(child.gameObject);
        }

        var bgImg     = slotGO.GetComponent<Image>();
        var borderImg = slotGO.transform.Find("Border")?.GetComponent<Image>();
        var emptyLbl  = slotGO.transform.Find("EmptyLabel")?.gameObject;

        // 빈 칸 테두리(납품 슬롯 아트)는 빈 칸에서만 — 룬이 들면 등급 보석 테두리가 대신한다(두 겹 테두리 방지).
        var frame = slotGO.transform.Find("Frame");
        if (frame != null) frame.gameObject.SetActive(item == null);

        var hover = slotGO.GetComponent<SlotHoverHandler>();
        if (hover != null) hover.item = item;

        var drag = slotGO.GetComponent<SlotDragHandler>();
        if (drag != null) drag.item = item;   // 빈 칸이면 null → 드래그해도 아무 일 없음

        // 디자이너 바탕 아트가 깔린 슬롯은 색을 덮어쓰지 않는다 — 어두운 색을 곱하면
        // 흰 바탕 아트가 그대로 죽어서, 아트를 주입한 의미가 없어진다.
        bool hasArt = _slotFillSprite != null;

        if (item == null)
        {
            // 빈 슬롯 상태
            if (bgImg != null)     bgImg.color     = hasArt ? Color.white : COLOR_EMPTY_BG;
            if (borderImg != null) borderImg.color = Color.clear;   // 빈 칸엔 틴트 없음(테두리는 슬롯 본체)
            SetSlotOutline(slotGO, COLOR_EMPTY_BORDER);
            if (emptyLbl != null)  emptyLbl.SetActive(true);
            ((RectTransform)slotGO.transform).anchoredPosition = SlotRestPos(index);   // 들린 채 비지 않게
        }
        else
        {
            // 아이템 슬롯 상태
            if (emptyLbl != null)  emptyLbl.SetActive(false);
            bool isNew = _newItemIds.Contains(item.instanceId);

            // 룬이 든 칸은 바탕 아트를 어둡게 누른다 — 흰색 그대로면 밝은 회색 판이라 룬 문양·글자가 떴다(09-25 캡처).
            if (bgImg != null)     bgImg.color     = hasArt ? FilledTint : new Color(0.18f, 0.18f, 0.26f, 0.95f);
            if (borderImg != null)
            {
                ApplySlotBorderColor(index, item);   // 상태색은 한 창구에서만 칠한다
            }

            BuildCardContent(slotGO, item, isNew);

            // 배치 전 카드에 shimmer 반짝임 효과 — 전설은 금빛으로 더 자주(획득 카드·판과 같은 광택)
            var shimmer = slotGO.AddComponent<StagingSlotShimmer>();
            if (item.rarity == ItemRarity.Legendary)
                shimmer.Configure(StagingSlotShimmer.LegendaryTint, 0.55f, 1.6f, SLOT_WIDTH);
        }
    }

    /// <summary>배치룬 카드 하단 효과 한 줄 — 첫 효과를 "라벨 +값"으로. 효과 없으면 빈 문자열.</summary>
    private static string EffectLine(RuntimeItemData item)
    {
        if (item?.effects == null || item.effects.Count == 0) return "";
        var slot = item.effects[0];
        if (string.IsNullOrEmpty(slot.effectType)) return "";
        var meta = EffectMetaRegistry.Get(slot.effectType);
        string val = EffectDescriptionFormatter.FormatValue(meta.Unit, slot.value);
        return $"{meta.Label} {val}";
    }

    private void BuildCardContent(GameObject slotGO, RuntimeItemData item, bool isNew)
    {
        // 등급은 카드 둘레의 보석 테두리가 말한다(아래 끝에서 세운다) — 예전 위 끝 5px 색 띠는 뺐다.

        // 모양 프리뷰 — 카드의 <b>주인공</b>. 룬은 "효과가 좋아도 판에 안 들어가면 무의미"하므로
        // 플레이어가 가장 먼저 보는 것이 모양이어야 한다. 카드 상단 대부분을 차지한다.
        //
        // 예전엔 위쪽 2/3를 item.icon(레거시 아이템 아이콘 — 노란 블록 그림)이 차지하고
        // 모양은 그 아래 좁은 띠에 6px 점으로 그려져, 정작 필요한 정보가 안 보였다.
        var shapePreviewGO = new GameObject("ShapePreview", typeof(RectTransform));
        shapePreviewGO.transform.SetParent(slotGO.transform, false);
        var shapePreviewRT = shapePreviewGO.GetComponent<RectTransform>();
        // 위쪽 끝은 NEW 뱃지·[X] 버튼(모두 22px 이하 = 슬롯 높이의 약 15%)이 차지하는 띠 아래로
        // 물린다. 예전엔 0.90까지 올라가 모양 미리보기가 뱃지·버튼과 겹쳤다.
        // 좌: 룬 고유 아트 / 우: 블록 모양. 겹쳐 놓으면 둘 다 안 읽혀서 칸을 나눈다
        // ("이게 어떤 룬인가"와 "판에서 어떤 모양을 먹는가"는 서로 다른 정보다).
        shapePreviewRT.anchorMin        = new Vector2(0.44f, 0.40f);
        shapePreviewRT.anchorMax        = new Vector2(0.93f, 0.88f);
        shapePreviewRT.sizeDelta        = Vector2.zero;
        shapePreviewRT.anchoredPosition = Vector2.zero;
        LayoutRebuilder.ForceRebuildLayoutImmediate(shapePreviewRT);   // rect 확정 후 셀 크기 역산
        BuildCardShapePreview(shapePreviewRT, item);

        BuildRuneArtPane(slotGO.transform, item);

        // 글자 판 — 이름·효과는 룬 아트 <b>위에</b> 얹힌다. 아트가 밝은 룬(노랑·하늘)에서는
        // 흰 글자가 그대로 묻혔다(사용자 지적 09-21). 아래 3분의 1에 잉크 판을 깔아 대비를 고정한다.
        var inkGO = new GameObject("TextInk", typeof(RectTransform));
        inkGO.transform.SetParent(slotGO.transform, false);
        var inkRT = inkGO.GetComponent<RectTransform>();
        inkRT.anchorMin        = new Vector2(0f, 0f);
        inkRT.anchorMax        = new Vector2(1f, 0.37f);
        inkRT.sizeDelta        = Vector2.zero;
        inkRT.anchoredPosition = Vector2.zero;
        var inkImg = inkGO.AddComponent<Image>();
        inkImg.color         = new Color(0.03f, 0.03f, 0.05f, 0.90f);   // 0.74는 노란 룬 아트에서 여전히 떴다(실측 09-21)
        inkImg.raycastTarget = false;

        // 이름 (하단 상단부)
        var nameTxtGO = new GameObject("Name", typeof(RectTransform));
        nameTxtGO.transform.SetParent(slotGO.transform, false);
        var nameRT = nameTxtGO.GetComponent<RectTransform>();
        // 25px — 18px 줄높이(23)가 Ellipsis에 잘리지 않게(칸이 줄높이보다 낮으면 TMP가 줄을 통째로 버린다 — 09-21 실측)
        nameRT.anchorMin        = new Vector2(0f, 0.19f);
        nameRT.anchorMax        = new Vector2(1f, 0.365f);
        nameRT.offsetMin        = new Vector2(8f, 0f);
        nameRT.offsetMax        = new Vector2(-8f, 0f);
        var nameTxt = nameTxtGO.AddComponent<TextMeshProUGUI>();
        if (cardFont != null) nameTxt.font = cardFont;
        nameTxt.text              = item.displayName ?? item.itemId;
        nameTxt.fontSize          = 18f;   // 룬 선택 규격 — 카드 이름은 효과(16)보다 한 단 크게
        nameTxt.fontStyle         = FontStyles.Bold;
        nameTxt.color             = _highlightedItem == item ? SelectedNameInk : NameInk;   // 잉크 판 위 양피지 흰색 · 고르면 금빛
        nameTxt.alignment         = TextAlignmentOptions.Center;
        nameTxt.textWrappingMode = TextWrappingModes.NoWrap;
        nameTxt.overflowMode      = TextOverflowModes.Ellipsis;

        // 효과(기능) — 완성본: 배치룬 카드 아래에 해당 기능을 표기
        var fxTxtGO = new GameObject("Effect", typeof(RectTransform));
        fxTxtGO.transform.SetParent(slotGO.transform, false);
        var fxRT = fxTxtGO.GetComponent<RectTransform>();
        fxRT.anchorMin        = new Vector2(0f, 0.02f);
        fxRT.anchorMax        = new Vector2(1f, 0.185f);
        fxRT.offsetMin        = new Vector2(8f, 0f);
        fxRT.offsetMax        = new Vector2(-8f, 0f);
        var fxTxt = fxTxtGO.AddComponent<TextMeshProUGUI>();
        if (cardFont != null) fxTxt.font = cardFont;
        fxTxt.text              = EffectLine(item);
        fxTxt.fontSize          = 16f;   // 잉크 판을 깔아 자리가 생겼다(줄바꿈 없음 + 말줄임이라 넘치지 않는다)
        fxTxt.fontStyle         = FontStyles.Bold;
        fxTxt.color             = new Color(0.93f, 0.97f, 0.72f, 1f);
        fxTxt.alignment         = TextAlignmentOptions.Center;
        fxTxt.textWrappingMode = TextWrappingModes.NoWrap;
        fxTxt.overflowMode      = TextOverflowModes.Ellipsis;
        fxTxt.raycastTarget     = false;

        // 등급 보석 테두리 — 룬 선택 카드와 같은 조각을 카드 크기(272)에 맞춰 줄여 얹는다. 글자·단추보다 아래.
        RuneCardKit.BuildGemFrame((RectTransform)slotGO.transform, item.rarity, 30f, 118f, bottomBar: false);

        // NEW 뱃지 — 위 끝에 걸터앉은 금빛 칩(16px). 예전 9px 빨강 글자는 읽히지 않았다.
        if (isNew)
        {
            var badgeGO = new GameObject("NewBadge", typeof(RectTransform));
            badgeGO.transform.SetParent(slotGO.transform, false);
            var badgeBG = badgeGO.AddComponent<Image>();
            badgeBG.color         = new Color(0.88f, 0.71f, 0.25f, 1f);
            badgeBG.raycastTarget = false;
            var badgeRT = badgeGO.GetComponent<RectTransform>();
            badgeRT.anchorMin        = new Vector2(0f, 1f);
            badgeRT.anchorMax        = new Vector2(0f, 1f);
            badgeRT.pivot            = new Vector2(0f, 0.5f);
            badgeRT.sizeDelta        = new Vector2(52f, 24f);
            badgeRT.anchoredPosition = new Vector2(10f, -2f);

            var badgeTxtGO = new GameObject("NewBadgeText", typeof(RectTransform));
            badgeTxtGO.transform.SetParent(badgeGO.transform, false);
            var badgeTxtRT = badgeTxtGO.GetComponent<RectTransform>();
            badgeTxtRT.anchorMin = Vector2.zero;
            badgeTxtRT.anchorMax = Vector2.one;
            badgeTxtRT.sizeDelta = Vector2.zero;
            var badgeTxt = badgeTxtGO.AddComponent<TextMeshProUGUI>();
            if (cardFont != null) badgeTxt.font = cardFont;
            badgeTxt.text          = "NEW";
            badgeTxt.fontSize      = 16f;
            badgeTxt.fontStyle     = FontStyles.Bold;
            badgeTxt.alignment     = TextAlignmentOptions.Center;
            badgeTxt.color         = new Color(0.10f, 0.07f, 0.02f, 1f);
            badgeTxt.raycastTarget = false;
        }

        // [×] 폐기 버튼 — 모서리에 걸친 둥근 단추(30px). 예전 22px 빨강 네모 + 14px X는 카드 위 경고처럼 튀었다.
        var xBtnGO = new GameObject("DiscardBtn", typeof(RectTransform));
        xBtnGO.transform.SetParent(slotGO.transform, false);
        var xRT = xBtnGO.GetComponent<RectTransform>();
        xRT.anchorMin        = new Vector2(1f, 1f);
        xRT.anchorMax        = new Vector2(1f, 1f);
        xRT.pivot            = new Vector2(0.5f, 0.5f);
        xRT.sizeDelta        = new Vector2(30f, 30f);
        xRT.anchoredPosition = new Vector2(-6f, -4f);
        var xRing = xBtnGO.AddComponent<Image>();
        xRing.sprite = RuneCardKit.Disc;
        xRing.color  = new Color(0.42f, 0.45f, 0.56f, 1f);
        var xBtn = xBtnGO.AddComponent<Button>();
        ShopUIStyle.ApplyButtonColors(xBtn, xRing);

        var xFill = ShopUIStyle.MakeImage(xBtnGO.transform, "Fill", new Color(0.07f, 0.08f, 0.12f, 1f));
        xFill.sprite = RuneCardKit.Disc;
        ShopUIStyle.Stretch(xFill.rectTransform, 2f);

        var xTxtGO = new GameObject("X", typeof(RectTransform));
        xTxtGO.transform.SetParent(xBtnGO.transform, false);
        var xTxtRT = xTxtGO.GetComponent<RectTransform>();
        xTxtRT.anchorMin = Vector2.zero;
        xTxtRT.anchorMax = Vector2.one;
        xTxtRT.sizeDelta = Vector2.zero;
        var xTxt = xTxtGO.AddComponent<TextMeshProUGUI>();
        if (cardFont != null) xTxt.font = cardFont;
        xTxt.text          = "×";
        xTxt.fontSize      = 22f;
        xTxt.fontStyle     = FontStyles.Bold;
        xTxt.alignment     = TextAlignmentOptions.Center;
        xTxt.color         = new Color(0.92f, 0.94f, 1f, 1f);
        xTxt.raycastTarget = false;

        var capturedItem = item;
        xBtn.onClick.AddListener(() => OnDiscardClicked(capturedItem));

        // 슬롯 클릭 버튼 (배경 이미지에 Button 추가)
        var clickBtn = slotGO.GetComponent<Button>() ?? slotGO.AddComponent<Button>();
        var clickBtnImg = slotGO.GetComponent<Image>();
        clickBtn.targetGraphic = clickBtnImg;
        clickBtn.onClick.RemoveAllListeners();
        clickBtn.onClick.AddListener(() => OnCardClicked(capturedItem));
    }

    // ── Shape Management ──

    private void EnsureShapeExists(RuntimeItemData item)
    {
        if (item == null || item.shapeId == 0)
        {
            Debug.LogWarning($"[StagingAreaView] EnsureShapeExists 조기반환: shapeId={item?.shapeId} (item={item?.itemId})");
            return;
        }
        if (_shapeByInstanceId.ContainsKey(item.instanceId)) return;

        if (boardManager == null) boardManager = BoardManager.Instance;

        // 이미 풀에 있는 Shape(배치됐다가 다시 보관함으로 돌아온 경우)를 재사용해 복사 방지
        if (boardManager != null)
        {
            var existing = boardManager.GetSharedShapeByItem(item.instanceId);
            if (existing != null)
            {
                _shapeByInstanceId[item.instanceId] = existing;
                return;
            }
        }

        if (boardManager == null)
        {
            Debug.LogWarning($"[StagingAreaView] BoardManager null — shape 생성 불가 (item={item.itemId})");
            return;
        }

        var blockData = Managers.RuneData;
        if (blockData == null)
        {
            Debug.LogWarning($"[StagingAreaView] RuneDataManager null — shape 생성 불가 (item={item.itemId})");
            return;
        }

        var shapeEntry = blockData.GetShape(item.shapeId);
        if (shapeEntry == null)
        {
            Debug.LogWarning($"[StagingAreaView] shapeId={item.shapeId} 데이터 없음 (item={item.itemId})");
            return;
        }

        var offsets = RuneDataManager.ParseCellOffsets(shapeEntry);

        var shapeSO = ScriptableObject.CreateInstance<ShapeAssetSO>();
        shapeSO.shapeName        = shapeEntry.shape_name;
        shapeSO.shapeBlockPrefab = boardManager.defaultShapeBlockPrefab;
        shapeSO.cellOffsets      = offsets;
        shapeSO.cellSize         = shapeEntry.cell_size > 0 ? shapeEntry.cell_size : GRID_CELL_SIZE;

        var shape = boardManager.SpawnSharedShape(shapeSO);
        if (shape != null)
        {
            shape.BindItem(item);
            _shapeByInstanceId[item.instanceId] = shape;
        }
    }

    // ── Section Divider ──

    private void BuildSectionDivider()
    {
        if (scrollContent == null) return;

        _sectionDivider = new GameObject("SectionDivider", typeof(RectTransform));
        _sectionDivider.transform.SetParent(scrollContent, false);

        // 2열 그리드에선 구분이 줄과 줄 <b>사이</b>에 생기므로 가로선이다.
        var rt = _sectionDivider.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 1f);
        rt.anchorMax        = new Vector2(0f, 1f);
        rt.pivot            = new Vector2(0f, 0.5f);
        rt.sizeDelta        = new Vector2(
            SLOT_SPACING + _columns * (SLOT_WIDTH + SLOT_SPACING), SLOT_SPACING);
        rt.anchoredPosition = Vector2.zero;

        var lineGO = new GameObject("Line", typeof(RectTransform));
        lineGO.transform.SetParent(_sectionDivider.transform, false);
        var lineRT = lineGO.GetComponent<RectTransform>();
        lineRT.anchorMin = new Vector2(0.06f, 0.5f);
        lineRT.anchorMax = new Vector2(0.94f, 0.5f);
        lineRT.sizeDelta = new Vector2(0f, 2f);
        var lineImg = lineGO.AddComponent<Image>();
        lineImg.color         = new Color(0.95f, 0.82f, 0.3f, 0.55f);
        lineImg.raycastTarget = false;

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(_sectionDivider.transform, false);
        var labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin        = new Vector2(0.5f, 0.5f);
        labelRT.anchorMax        = new Vector2(0.5f, 0.5f);
        labelRT.sizeDelta        = new Vector2(20f, SLOT_SPACING);
        labelRT.anchoredPosition = Vector2.zero;
        var labelTxt = labelGO.AddComponent<TextMeshProUGUI>();
        if (cardFont != null) labelTxt.font = cardFont;
        labelTxt.text          = "◆";
        labelTxt.fontSize      = 10f;
        labelTxt.alignment     = TextAlignmentOptions.Center;
        labelTxt.color         = new Color(0.95f, 0.82f, 0.3f, 0.85f);
        labelTxt.raycastTarget = false;

        _sectionDivider.SetActive(false);
    }

    private void UpdateSectionDivider(int nonNewCount, int newCount)
    {
        if (_sectionDivider == null) return;

        // 경계가 줄 중간에 떨어지면(예: 미배치 3개) 가로선을 그을 자리가 없다 — 그땐 감춘다.
        // NEW 뱃지가 이미 새 아이템을 표시하므로 구분선이 없어도 읽힌다.
        bool show = nonNewCount > 0 && newCount > 0 && nonNewCount % _columns == 0;
        _sectionDivider.SetActive(show);
        if (!show) return;

        int   row = nonNewCount / SLOT_COLUMNS;
        float y   = -SLOT_SPACING * 0.5f - row * (SLOT_HEIGHT + SLOT_SPACING);
        _sectionDivider.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, y);
    }

    // ── Layout Helpers ──

    /// <summary>index번 슬롯을 2열 그리드의 좌상단 기준 위치에 놓는다.</summary>
    private void PositionSlot(RectTransform rt, int index)
    {
        rt.anchorMin        = new Vector2(0f, 1f);
        rt.anchorMax        = new Vector2(0f, 1f);
        rt.pivot            = new Vector2(0f, 1f);
        rt.anchoredPosition = SlotBasePos(index);
    }

    private Vector2 SlotBasePos(int index)
    {
        int col = index % _columns;
        int row = index / _columns;
        return new Vector2(
             SLOT_SPACING + col * (SLOT_WIDTH  + SLOT_SPACING),
            -SLOT_SPACING - row * (SLOT_HEIGHT + SLOT_SPACING));
    }

    /// <summary>쉬는 자리 — 고른 카드는 <see cref="SELECT_LIFT"/>만큼 들려 있다(룬 선택 카드의 「고르면 들림」과 같은 말).</summary>
    private Vector2 SlotRestPos(int index)
    {
        bool selected = _slotItems[index] != null && _slotItems[index] == _highlightedItem;
        return SlotBasePos(index) + (selected ? new Vector2(0f, SELECT_LIFT) : Vector2.zero);
    }

    /// <summary>슬롯 한 칸 스케일 펀치. 팝업이 아니라 오버레이지만 룬판은 시간정지 중일 수 있어 unscaled(UIJuice)로 돈다.</summary>
    private async UniTaskVoid PulseSlotAsync(RectTransform rt)
    {
        if (rt == null) return;

        try { await UIJuice.PunchAsync(rt, 0.08f, 0.22f, destroyCancellationToken); }
        catch (System.OperationCanceledException) { }
    }

    /// <summary>
    /// 카드에 손을 얹으면 살짝 커지고 금빛 테두리가 선다 — 「잡을 수 있는 것」으로 읽히게(의뢰서 「호버=글로우」, 09-27).
    /// 빈 칸은 반응하지 않는다. 테두리는 처음 필요할 때 만든다(골격 이름 HoverGlow — 카드 내용 갈이에서 살아남는다).
    /// </summary>
    private void SetSlotHover(int index, bool on)
    {
        if (index < 0 || index >= _slotGOs.Length || _slotGOs[index] == null) return;
        on &= _slotItems[index] != null;
        var slot = _slotGOs[index].transform;
        slot.localScale = on ? Vector3.one * HOVER_SCALE : Vector3.one;

        var glow = slot.Find("HoverGlow");
        if (glow == null && on)
        {
            var img = ShopUIStyle.MakeImage(slot, "HoverGlow", HoverGlowColor);
            img.sprite        = UIProceduralSprites.RoundedOutline(radius: 12f, stroke: 2.5f);
            img.type          = Image.Type.Sliced;
            img.raycastTarget = false;
            ShopUIStyle.Stretch(img.rectTransform, -3f);
            glow = img.transform;
            // 골격(Border·EmptyLabel·Frame) 바로 뒤 = 카드 내용 아래 — 맨 위에 두면 NEW 배지·× 위로 선이 그어졌다(09-27 실측).
            int chrome = 0;
            for (int c = 0; c < slot.childCount; c++)
            {
                var n = slot.GetChild(c).name;
                if (n != "HoverGlow" && IsSlotChrome(n)) chrome++;
            }
            glow.SetSiblingIndex(chrome);
        }
        if (glow == null) return;
        glow.gameObject.SetActive(on);
    }

    /// <summary>카드를 잡으면 그 룬을 고른 것으로 친다 — 판의 속성 존 강조가 손에 쥔 룬을 따라온다. 카드는 잔상으로 흐려진다.</summary>
    private void BeginCardDrag(RuntimeItemData item, GameObject slotGO)
    {
        if (item != null && item != _highlightedItem) OnItemSelected?.Invoke(item);
        SetSlotHover(System.Array.IndexOf(_slotGOs, slotGO), false);
        if (slotGO == null) return;
        if (!slotGO.TryGetComponent<CanvasGroup>(out var cg)) cg = slotGO.AddComponent<CanvasGroup>();
        cg.alpha = DRAG_GHOST;
    }

    private void EndCardDrag(GameObject slotGO)
    {
        if (slotGO != null && slotGO.TryGetComponent<CanvasGroup>(out var cg)) cg.alpha = 1f;
    }

    private int FindSlotIndex(RuntimeItemData item)
    {
        if (item == null) return -1;
        for (int i = 0; i < RunItemInventory.MaxStagingCapacity; i++)
            if (_slotItems[i] == item) return i;
        return -1;
    }

    /// <summary>
    /// 슬롯의 상태색을 칠하는 <b>단일 창구</b>. 테두리선은 슬롯 본체(Outline)에,
    /// 색 틴트는 Border 레이어에 옅게 얹는다 — 진하게 덮으면 아이콘이 색에 묻힌다.
    /// </summary>
    private void ApplySlotBorderColor(int index, RuntimeItemData item)
    {
        if (index < 0 || index >= RunItemInventory.MaxStagingCapacity) return;
        var slotGO = _slotGOs[index];
        if (slotGO == null) return;

        Color color = item == null
            ? COLOR_EMPTY_BORDER
            : (_highlightedItem == item ? COLOR_SELECTED_BORDER
               : _newItemIds.Contains(item.instanceId) ? COLOR_NEW_BORDER : COLOR_NORMAL_BORDER);

        SetSlotOutline(slotGO, color);

        // 상태는 <b>들림 + 등급빛</b>으로 말한다. 예전엔 상태색(청록·노랑)을 알파 0.35로 카드 전체에 덮어
        // 어두운 판이 밝은 색판으로 보였다(09-25 캡처) — 룬 아트·글자가 그 색에 묻혔다.
        bool selected = item != null && _highlightedItem == item;
        ((RectTransform)slotGO.transform).anchoredPosition = SlotRestPos(index);

        // 판 전체를 덮는 틴트는 쓰지 않는다 — 고른 카드가 통째로 회색 판처럼 떴다(09-25 2차 캡처).
        // 고른 표시는 들림 + 이름 금색(룬 선택 카드의 「고르면 들림·금빛」과 같은 말).
        var borderImg = slotGO.transform.Find("Border")?.GetComponent<Image>();
        if (borderImg != null) borderImg.color = Color.clear;
        var nameTxt = slotGO.transform.Find("Name")?.GetComponent<TMP_Text>();
        if (nameTxt != null) nameTxt.color = selected ? SelectedNameInk : NameInk;
    }

    /// <summary>
    /// 카드 왼쪽 칸에 <b>룬 고유 아트</b>를 세운다(오른쪽 모양 칸과 분리).
    /// 아트는 등급 기준이라 속성이 안 드러나서, 아래에 얇은 속성색 띠를 둬 오른쪽 속성 타일과 잇는다.
    /// </summary>
    private static void BuildRuneArtPane(Transform slot, RuntimeItemData item)
    {
        // 문양 결정은 RuneArt.ResolveRuneIcon 한곳에서 — 기능 → 등급 → 속성.
        // 예전엔 여기만 기능 문양을 건너뛰고 등급 아트에서 시작해, 같은 룬이
        // 획득 팝업에선 기능 칼날이고 배치 화면에선 돌 각인석으로 보였다.
        var art = RuneArt.ResolveRuneIcon(item);
        if (art == null) return;

        // 문양 뒤 등급빛 — 룬 선택 카드의 문양 칸과 같은 빛(평범은 옅게).
        var rc = ShopUIStyle.Rarity(item.rarity);
        var glow = ShopUIStyle.MakeImage(slot, "RuneArtGlow",
            new Color(rc.r, rc.g, rc.b, item.rarity == ItemRarity.Common ? 0.16f : 0.40f));
        glow.sprite = UI_RuneSelectPopup.SoftDot;
        glow.rectTransform.anchorMin = new Vector2(0.00f, 0.30f);
        glow.rectTransform.anchorMax = new Vector2(0.42f, 1.02f);
        glow.rectTransform.offsetMin = glow.rectTransform.offsetMax = Vector2.zero;

        var go = new GameObject("RuneArtPane", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(slot, false);
        rt.anchorMin        = new Vector2(0.06f, 0.42f);
        rt.anchorMax        = new Vector2(0.36f, 0.90f);
        rt.sizeDelta        = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;

        var img = go.GetComponent<Image>();
        img.sprite         = art;
        img.preserveAspect = true;
        img.raycastTarget  = false;

        var strip = new GameObject("ElemStrip", typeof(RectTransform), typeof(Image));
        var srt = (RectTransform)strip.transform;
        srt.SetParent(slot, false);
        srt.anchorMin        = new Vector2(0.08f, 0.385f);
        srt.anchorMax        = new Vector2(0.34f, 0.405f);
        srt.sizeDelta        = Vector2.zero;
        srt.anchoredPosition = Vector2.zero;
        var simg = strip.GetComponent<Image>();
        simg.color         = ElementDef.IdColor(item.element, new Color(0.6f, 0.6f, 0.7f));
        simg.raycastTarget = false;
    }

    // ── Card Shape Preview ──

    private void BuildCardShapePreview(RectTransform root, RuntimeItemData item)
    {
        if (item == null || item.shapeId == 0) return;

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
            if (o.x < minX) minX = o.x; if (o.x > maxX) maxX = o.x;
            if (o.y < minY) minY = o.y; if (o.y > maxY) maxY = o.y;
        }
        int cols = maxX - minX + 1;
        int rows = maxY - minY + 1;

        // 셀은 고정 크기가 아니라 <b>미리보기 칸에 맞춰 확대</b>한다.
        // 6px 고정이던 시절엔 1칸 룬이 점 하나로 보여 무슨 모양인지 분간이 안 됐다.
        const float GAP     = 2f;
        const float CELL_MAX = 22f;
        const float CELL_MIN = 5f;
        float boxW = Mathf.Max(1f, root.rect.width  - 6f);
        float boxH = Mathf.Max(1f, root.rect.height - 4f);
        float fitW = (boxW - (cols - 1) * GAP) / Mathf.Max(1, cols);
        float fitH = (boxH - (rows - 1) * GAP) / Mathf.Max(1, rows);
        float CELL = Mathf.Clamp(Mathf.Min(fitW, fitH), CELL_MIN, CELL_MAX);

        float totalW = cols * CELL + (cols - 1) * GAP;
        float totalH = rows * CELL + (rows - 1) * GAP;
        float startX = -totalW * 0.5f + CELL * 0.5f;
        float startY =  totalH * 0.5f - CELL * 0.5f;

        // 스프라이트/틴트 규칙은 RuneArt.ResolveRuneCell 한곳에서 정한다 —
        // 드래그 블록·선택 팝업·아이템 정보와 같은 룬이 같게 보이도록.
        RuneArt.ResolveRuneCell(item.element, item.rarity,
            GridThumbnail.GetItemColor(item.instanceId), out var art, out var tint);

        // 판 위 블록과 같은 속성 타일로 통일 — 대기열에서 본 모양이 판에서 그대로 나와야 한다.
        var blockTile = RuneArt.GetBlockTile(item.element);
        if (blockTile != null) { art = blockTile; tint = Color.white; }

        foreach (var o in offsets)
        {
            int col = o.x - minX;
            int row = maxY - o.y;

            var cellGO = new GameObject($"P{o.x}_{o.y}", typeof(RectTransform), typeof(Image));
            cellGO.transform.SetParent(root, false);
            var rt  = cellGO.GetComponent<RectTransform>();
            rt.sizeDelta        = Vector2.one * CELL;
            rt.anchoredPosition = new Vector2(
                startX + col * (CELL + GAP),
                startY - row * (CELL + GAP));
            var img = cellGO.GetComponent<Image>();
            if (art != null) img.sprite = art;
            img.color          = tint;
            img.preserveAspect = art != null;
            img.raycastTarget  = false;
        }

    }

    // ── Event Handlers ──

    private void OnCardClicked(RuntimeItemData item)
    {
        OnItemSelected?.Invoke(item);
    }

    private void OnDiscardClicked(RuntimeItemData item)
    {
        if (item == null) return;
        ShowDiscardDialog(item);
    }

    // ── Discard Confirm Dialog ──

    private void BuildDiscardDialog()
    {
        _discardDialog = new GameObject("DiscardConfirmDialog", typeof(RectTransform));
        _discardDialog.transform.SetParent(transform, false);

        var rt = _discardDialog.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;

        var overlay = _discardDialog.AddComponent<Image>();
        overlay.color = new Color(0f, 0f, 0f, 0.55f);
        overlay.raycastTarget = true;

        var panelGO = new GameObject("Panel", typeof(RectTransform));
        panelGO.transform.SetParent(_discardDialog.transform, false);
        var panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRT.pivot            = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta        = new Vector2(400f, 180f);   // 17px 세 줄 + 버튼 — 280×110에 13px로 끼워 넣었었다
        panelRT.anchoredPosition = Vector2.zero;
        var panelBG = panelGO.AddComponent<Image>();
        panelBG.color = new Color(0.1f, 0.1f, 0.16f, 0.98f);

        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(panelGO.transform, false);
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0f, 0.36f);
        textRT.anchorMax = Vector2.one;
        textRT.sizeDelta = new Vector2(-24f, -12f);
        _discardDialogText = textGO.AddComponent<TextMeshProUGUI>();
        if (cardFont != null) _discardDialogText.font = cardFont;
        _discardDialogText.fontSize  = 17f;
        _discardDialogText.alignment = TextAlignmentOptions.Center;
        _discardDialogText.color     = new Color(0.9f, 0.9f, 0.95f, 1f);

        BuildDialogButton(panelGO, "YesBtn",
            new Vector2(0.08f, 0.08f), new Vector2(0.45f, 0.34f),
            new Color(0.75f, 0.18f, 0.18f, 1f), "폐기", OnDiscardConfirm);

        BuildDialogButton(panelGO, "NoBtn",
            new Vector2(0.55f, 0.08f), new Vector2(0.92f, 0.34f),
            new Color(0.25f, 0.25f, 0.32f, 1f), "취소", HideDiscardDialog);

        _discardDialog.SetActive(false);
    }

    private void BuildDialogButton(GameObject parent, string goName,
        Vector2 anchorMin, Vector2 anchorMax, Color bgColor, string label,
        UnityEngine.Events.UnityAction callback)
    {
        var btnGO = new GameObject(goName, typeof(RectTransform));
        btnGO.transform.SetParent(parent.transform, false);
        var btnRT = btnGO.GetComponent<RectTransform>();
        btnRT.anchorMin        = anchorMin;
        btnRT.anchorMax        = anchorMax;
        btnRT.sizeDelta        = Vector2.zero;
        btnRT.anchoredPosition = Vector2.zero;
        var btnBG = btnGO.AddComponent<Image>();
        btnBG.color = bgColor;
        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnBG;
        btn.onClick.AddListener(callback);

        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(btnGO.transform, false);
        var lblRT = lblGO.GetComponent<RectTransform>();
        lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one; lblRT.sizeDelta = Vector2.zero;
        var lblTxt = lblGO.AddComponent<TextMeshProUGUI>();
        if (cardFont != null) lblTxt.font = cardFont;
        lblTxt.text      = label;
        lblTxt.fontSize  = 18f;
        lblTxt.alignment = TextAlignmentOptions.Center;
        lblTxt.color     = Color.white;
        lblTxt.raycastTarget = false;
    }

    private void ShowDiscardDialog(RuntimeItemData item)
    {
        _pendingDiscard = item;
        if (_discardDialogText != null)
        {
            // 폐기 전에 <b>얼마가 돌아오는지</b> 보여준다. 안 보이면 그냥 버리는 것으로 읽혀
            // 「폐기 → 원석 → 정제소」 순환을 플레이어가 인지하지 못한다.
            int ore = RuneSalvage.OreValueOf(item);
            _discardDialogText.text =
                $"\"{item.displayName ?? item.itemId}\"\n폐기하시겠습니까?\n\n" +
                $"<color=#63D9C0>원석 +{ore}</color>  <size=80%>정제소에서 다시 뽑을 수 있다</size>";
        }
        if (_discardDialog != null)
        {
            // 판에 놓인 룬(Puzzle — 룬판 루트 직속·맨 위)이 어두운 막 위로 비쳤다 — 룬판 루트의 맨 위로 옮긴다(09-27).
            var panel = GetComponentInParent<UI_GridPanel>();
            if (panel != null && _discardDialog.transform.parent != panel.transform)
                _discardDialog.transform.SetParent(panel.transform, false);
            _discardDialog.transform.SetAsLastSibling();
            _discardDialog.SetActive(true);
        }
    }

    private void HideDiscardDialog()
    {
        _pendingDiscard = null;
        if (_discardDialog != null)
            _discardDialog.SetActive(false);
    }

    private void OnDiscardConfirm()
    {
        var item = _pendingDiscard;
        HideDiscardDialog();
        if (item == null) return;

        RemoveShapeForItem(item);
        var run = GameRunBootstrapper.Instance?.Run;
        run?.ItemInventory?.DiscardFromStaging(item);

        // 폐기는 손실이 아니라 <b>다음 룬을 뽑을 기회</b>다 — 원석으로 돌려주고 정제소가 그걸 먹는다.
        RuneSalvage.Refund(item);
    }


    // ── Nested: Drag Handler ──

    /// <summary>
    /// 보관함 카드를 잡으면 그 룬의 Shape를 손에 쥐어 준다.
    ///
    /// 셰이프는 감춰진 주차 구역(shapeHost)에 있어 직접 잡을 수 없다. 카드가 손잡이가 되어
    /// 드래그 이벤트를 Shape에게 그대로 넘기면, 판정·스냅·배치는 기존 드래그 경로가 전부 처리한다
    /// (조작 진입점만 카드로 옮긴 것이라 규칙이 갈라지지 않는다).
    /// </summary>
    private sealed class SlotDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public RuntimeItemData item;
        public StagingAreaView owner;

        private Shape _shape;

        public void OnBeginDrag(PointerEventData eventData)
        {
            _shape = owner != null ? owner.GetShapeForItem(item) : null;
            if (_shape == null) return;

            owner.BeginCardDrag(item, gameObject);   // 고른 룬 = 끄는 룬 · 카드는 잔상
            _shape.OnBeginDrag(eventData);   // 부모가 gameplayRoot로 바뀌며 감춤(알파0)에서 벗어난다
            MoveToPointer(eventData);
        }

        public void OnDrag(PointerEventData eventData) => _shape?.OnDrag(eventData);

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_shape == null) return;
            owner?.EndCardDrag(gameObject);
            _shape.OnEndDrag(eventData);
            _shape = null;
        }

        /// <summary>
        /// 주차돼 있던 셰이프를 <b>지금 잡은 지점</b>으로 옮긴다 — 커서 아래에서 바로 끌리게.
        ///
        /// 월드 좌표로 맞추는 이유: anchoredPosition은 <b>앵커 기준</b> 오프셋인데 주차용 셰이프는
        /// 앵커가 상단(0.5, 1)이라, 중심 기준 좌표를 그대로 넣으면 부모 높이의 절반만큼 위로 튄다.
        /// 이후 이동은 Shape.OnDrag가 델타로 처리하므로 커서를 계속 따라온다.
        /// </summary>
        private void MoveToPointer(PointerEventData eventData)
        {
            var rt = (RectTransform)_shape.transform;
            if (rt.parent is not RectTransform parent) return;

            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    parent, eventData.position, eventData.pressEventCamera, out var world))
                rt.position = world;
        }
    }

    // ── Nested: Hover Handler ──

    private sealed class SlotHoverHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public RuntimeItemData                item;
        public System.Action<RuntimeItemData> onEnter;
        public System.Action                  onExit;
        public StagingAreaView                owner;
        public int                            index;

        public void OnPointerEnter(PointerEventData eventData)
        {
            owner?.SetSlotHover(index, true);
            if (item != null) onEnter?.Invoke(item);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            owner?.SetSlotHover(index, false);
            onExit?.Invoke();
        }
    }
}
