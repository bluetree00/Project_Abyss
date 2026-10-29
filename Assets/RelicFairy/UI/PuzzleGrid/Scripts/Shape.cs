using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Runtime Shape root.
/// - Built from ShapeAssetSO (block prefab + offsets).
/// - Drag behavior from ShapeDragSO.
/// IMPORTANT: Put this script on the ROOT of the shape prefab.
/// Do NOT put drag scripts on child blocks.
/// </summary>
public class Shape : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler,
                     IPointerEnterHandler, IPointerExitHandler
{
    private const float DragAlpha = 0.72f;   // 끄는 동안 조각 알파 — 미리보기가 비치게

    private static int _idCounter = 0;

    /// <summary>지금 끌고 있는 룬(없으면 null) — 룬판이 E로 돌릴 대상을 찾는다(10-25).</summary>
    public static Shape Dragging { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Dragging = null;

    [Header("Identity")]
    [Tooltip("Shape SO에서 복사된 표시 이름. 여러 Shape가 같은 이름을 가질 수 있다.")]
    public string shapeName;
    [Tooltip("인스턴스 생성 시 자동 부여되는 고유 ID. ex) Shape_01")]
    public string shapeId;

    [Header("Assets")]
    public ShapeAssetSO shapeAsset;
    public ShapeDragSO dragAsset;

    [Header("Fallback (used if SO is null)")]
    public GameObject shapeBlockPrefab;
    public List<Vector2Int> cellOffsets = new();
    public float cellSize = 90f;

    [SerializeField] private Vector2 homeAnchoredPos;
    [SerializeField] private RectTransform homeParent;

    /// <summary>이 Shape에 연결된 아이템 데이터. 배치/제거 시 RunItemInventory와 동기화.</summary>
    public RuntimeItemData ItemData { get; private set; }

    /// <summary>회전 칸(0 ~ 3, 90°씩 시계 방향) — 묶인 아이템의 <see cref="RuntimeItemData.rotation"/>과 같다.</summary>
    public int Rotation { get; private set; }

    private Canvas canvas;
    private RectTransform rt;
    private Vector3 cachedStartLocalScale;
    private Vector2 cachedStartAnchoredPos;

    private List<GridSquare> occupiedSquares = new();
    private LayoutElement _layout;
    private RunePieceOutline _outline;   // 조각 바깥 윤곽(10-18) — 루트에 붙는다(RebuildOutline)
    private List<Vector2Int> _baseOffsets;   // 돌리기 전 모양(에셋 그대로) — 회전은 늘 여기서 다시 계산한다
    private CancellationTokenSource _spinCts;   // 돌릴 때 칸마다 제자리에서 도는 짧은 연출(10-26)

    void Awake()
    {
        shapeId = $"Shape_{++_idCounter:D2}";

        rt = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>();
        _layout = GetComponent<LayoutElement>();
        CacheStartTransform();

        // If asset already assigned in inspector, build now.
        if (shapeAsset != null)
            ApplyAsset(shapeAsset);
        else
            BuildShapeBlocks();
    }

    // 판이 닫히는 등 커서가 빠져나간 알림 없이 꺼지면 다음에 열 때까지 밝은 채로 남는다.
    private void OnDisable()
    {
        if (_outline != null) _outline.SetHovered(false);
        StopSpin();
    }

    private void OnDestroy() => _spinCts?.Cancel();

    public void ApplyAsset(ShapeAssetSO asset)
    {
        shapeAsset = asset;
        if (shapeAsset == null) return;

        shapeName        = shapeAsset.shapeName;
        shapeBlockPrefab = shapeAsset.shapeBlockPrefab;
        cellOffsets      = new List<Vector2Int>(shapeAsset.cellOffsets ?? new Vector2Int[0]);
        _baseOffsets     = new List<Vector2Int>(cellOffsets);
        Rotation         = 0;
        cellSize         = (GridManager.Instance != null && GridManager.Instance.HasGrid)
                           ? GridManager.Instance.GetGap()
                           : shapeAsset.cellSize;

        BuildShapeBlocks();
    }

    public void CacheStartTransform()
    {
        if (rt == null) rt = GetComponent<RectTransform>();
        cachedStartAnchoredPos = rt.anchoredPosition;
        cachedStartLocalScale = rt.localScale;
    }

    public void ReturnToStart()
    {
        rt.anchoredPosition = cachedStartAnchoredPos;
        rt.localScale = cachedStartLocalScale;
    }

    private void BuildShapeBlocks()
    {
        // Clear
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);

        if (shapeBlockPrefab == null) return;

        // 간격은 스텝(cellSize)으로, 크기는 칸의 시각 크기로 그린다.
        // 둘을 같은 값으로 쓰면 블록이 칸 사이 여백까지 덮어 존 타일 경계선이 잠식된다.
        float visualSize = (GridManager.Instance != null && GridManager.Instance.HasGrid)
                           ? GridManager.Instance.GetSquareVisualSize()
                           : cellSize;

        foreach (var offset in cellOffsets)
        {
            var blockObj = Instantiate(shapeBlockPrefab, transform);
            var brt = blockObj.GetComponent<RectTransform>();
            if (brt != null)
            {
                brt.sizeDelta = new Vector2(visualSize, visualSize);
                brt.anchoredPosition = new Vector2(offset.x * cellSize, offset.y * cellSize);
            }

            // BoxCollider2D 크기도 시각 크기 기준으로 동기화 (약 80% 인셋)
            if (blockObj.TryGetComponent<BoxCollider2D>(out var col))
                col.size = new Vector2(visualSize * 0.8f, visualSize * 0.8f);
        }

        ApplyRuneArt();   // 재생성(칸 크기 변경 등) 후에도 룬 외형이 유지되도록
        RebuildOutline(visualSize);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        Managers.Sound?.PlayUiEvent(SoundEvent.RuneLift);   // 판 · 보관함에서 집기(10-12 G1)

        // 전역 배치 상태에서 제거 (슬롯으로 돌아올 수 있도록)
        BoardManager.Instance?.OnShapePickedUp(this);

        GridManager.Instance?.ReleaseShape(this);

        // ShapeScrollView 의 RectMask2D 클리핑 방지:
        // 드래그 중 GameplayRoot 로 리패런트 → 격자 방향으로 스냅해도 잘리지 않음
        var gameplayRoot = BoardManager.Instance?.gameplayRoot?.transform;
        if (gameplayRoot != null && transform.parent != gameplayRoot)
            transform.SetParent(gameplayRoot, true); // worldPositionStays=true

        // Visual feedback: scale multiplier — 10-22 1.1 → 1(ShapeDrag.asset): 끄는 동안에도 판 칸과 1:1이어야 맞춰 둔 자리에 놓인다
        // (1.1배면 칸 68이 74.8로 커져 큰 룬 끝이 0.7칸까지 어긋났고, 놓는 순간 1배로 줄며 자리가 바뀌었다 — 사용자 「전설 룬 크기가 판과 안 맞아 보인다」)
        float mul = (dragAsset != null) ? dragAsset.selectedScale.x : 1f;
        rt.localScale = cachedStartLocalScale * mul;
        SetDragAlpha(DragAlpha);
        if (_outline != null) _outline.SetDragging(true);   // 끄는 조각 = 고른 조각 — 외곽선을 밝게(10-18)

        Dragging = this;

        // Apply pointer offset ONCE at drag start (so it doesn't accumulate)
        if (dragAsset != null && dragAsset.pointerOffset != Vector2.zero)
        {
            float scaleFactor = (canvas != null) ? canvas.scaleFactor : 1f;
            rt.anchoredPosition += dragAsset.pointerOffset / Mathf.Max(0.0001f, scaleFactor);
        }

        if (_layout != null) _layout.ignoreLayout = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        float scaleFactor = canvas != null ? canvas.scaleFactor : 1f;

        Vector2 delta = eventData.delta / Mathf.Max(0.0001f, scaleFactor);
        rt.anchoredPosition += delta;

        // 격자 근처이면 프리뷰만 표시 — 스냅은 손을 놓을 때만 (TryPlaceShape)
        GridManager.Instance?.TryPreviewAndSnap(this, out _);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        // Restore base scale (placement manager will snap position)
        if (Dragging == this) Dragging = null;
        rt.localScale = cachedStartLocalScale;
        SetDragAlpha(1f);
        if (_outline != null) _outline.SetDragging(false);

        GridManager.Instance?.ClearPreview();

        if (GridManager.Instance == null) { ReturnToStart(); return; }

        bool placed = GridManager.Instance.TryPlaceShape(this);

        if (placed)
        {
            // gridHost로 이동 후 raycastTarget 활성화 → 배치된 셀에서 재드래그 가능
            if (BoardManager.Instance?.gridHost != null)
                transform.SetParent(BoardManager.Instance.gridHost, true);
            BoardManager.Instance?.OnShapePlaced(this);
            SetBlocksRaycastTarget(true);
            Managers.Sound?.PlayUiEvent(SoundEvent.RunePlace);
        }
        else
        {
            // 왜 안 들어갔는지 말한다 — 예전엔 말없이 보관함으로 사라졌다(09-27).
            string why = GridManager.Instance.DescribeDropFailure(this);
            if (why != null) UI_GridPanel.NotifyDropRejected(why);
            BoardManager.Instance.ReSlotAndReturn(this);
            Managers.Sound?.PlayUiEvent(SoundEvent.RuneReturn);   // 보관함으로 돌아간다는 정보(10-12 G3)
        }

        if (_layout != null) _layout.ignoreLayout = false;
    }

    /// <summary>끄는 동안 조각을 반투명하게 — 아래 판의 초록/빨강 미리보기가 비쳐야 어디에 놓이는지 보인다(09-27).</summary>
    private void SetDragAlpha(float a)
    {
        if (!TryGetComponent<CanvasGroup>(out var cg)) cg = gameObject.AddComponent<CanvasGroup>();
        cg.alpha = a;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // 드래그 중이 아닌 순수 클릭 → 우측 아이템 정보 패널 갱신 (시나리오 5)
        if (eventData.dragging) return;
        if (ItemData != null)
            GridManager.Instance?.NotifyItemSelected(ItemData);
    }

    // 판에 놓인 조각에 커서를 올리면 외곽선이 밝아진다 — 붙어 있는 같은 그림 중 「이 룬은 여기까지」(10-18).
    // 블록에서 블록으로 옮겨 다닐 땐 루트가 공통 부모라 나갔다 들어오지 않는다.
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_outline != null) _outline.SetHovered(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_outline != null) _outline.SetHovered(false);
    }

    /// <summary>오른쪽 정보판에 뜬 놓인 룬 — 외곽선을 밝게(UI_GridPanel이 정보판이 바뀔 때마다 부른다, 10-18).</summary>
    public void SetOutlineSelected(bool on)
    {
        if (_outline != null) _outline.SetSelected(on);
    }

    /// <summary>아이템 데이터를 이 Shape에 연결한다. StagingAreaView에서 보관함 아이템 드래그 시 호출.</summary>
    public void BindItem(RuntimeItemData item)
    {
        ItemData = item;
        if (item != null && item.rotation != Rotation) SetRotation(item.rotation, keepCenter: false);   // 판 · 보관함 · 이어하기가 같은 방향
        ApplyRuneArt();
    }

    /// <summary>90° 시계 방향으로 한 번 돌린다(10-25 사용자 「룬 블록을 회전시키는 기능」) — 칸마다 제자리에서 짧게 돌아 돈 것이 보인다(10-26).</summary>
    public void RotateClockwise()
    {
        SetRotation(Rotation + 1);
        StopSpin();
        _spinCts = new CancellationTokenSource();
        SpinBlocksAsync(_spinCts.Token).Forget();
    }

    /// <summary>
    /// 회전 칸을 정한다 — 칸 모양을 돌리고 <b>블록을 다시 만들지 않고 자리만 옮긴다</b>(끄는 도중 다시 만들면 Destroy가 프레임 끝이라
    /// 그 프레임 판정 · 미리보기가 옛 블록을 센다). keepCenter면 블록 묶음 가운데가 그 자리에 머문다(커서 아래에서 돈다).
    /// </summary>
    public void SetRotation(int rotation, bool keepCenter = true)
    {
        rotation = ((rotation % 4) + 4) % 4;
        _baseOffsets ??= new List<Vector2Int>(cellOffsets);
        if (rotation == Rotation) return;

        if (rt == null) rt = (RectTransform)transform;
        Vector2 before = BlockBoundsCenterLocal();
        Rotation = rotation;
        cellOffsets.Clear();
        foreach (var o in _baseOffsets) cellOffsets.Add(RotateCW(o, rotation));

        if (transform.childCount != cellOffsets.Count) BuildShapeBlocks();
        else
        {
            for (int i = 0; i < cellOffsets.Count; i++)
                if (transform.GetChild(i) is RectTransform brt)
                    brt.anchoredPosition = new Vector2(cellOffsets[i].x * cellSize, cellOffsets[i].y * cellSize);
            float visualSize = (GridManager.Instance != null && GridManager.Instance.HasGrid)
                               ? GridManager.Instance.GetSquareVisualSize()
                               : cellSize;
            ApplyLegendarySheen();   // 광택이 왼쪽 → 오른쪽으로 흐르게 x 순서를 다시 잡는다
            RebuildOutline(visualSize);
        }

        if (keepCenter) rt.anchoredPosition += (before - BlockBoundsCenterLocal()) * rt.localScale.x;
        if (ItemData != null) ItemData.rotation = Rotation;
    }

    /// <summary>
    /// 돌린 직후 칸마다 제자리에서 90° → 0°(0.14초 · 룬판은 시간을 멈추므로 실시간). 칸 <b>자리</b>는 이미 새 모양이라
    /// 놓일 칸 판정(칸 위치만 읽는다)은 그대로다 — 돌아가는 것은 칸 그림의 기울기뿐.
    /// </summary>
    private async UniTaskVoid SpinBlocksAsync(CancellationToken ct)
    {
        const float Dur = 0.14f;
        try
        {
            for (float t = 0f; t < Dur; t += Time.unscaledDeltaTime)
            {
                var tilt = Quaternion.Euler(0f, 0f, 90f * (1f - t / Dur));   // +90(반시계 쪽)에서 0으로 풀리며 시계 방향으로 돈다
                foreach (Transform child in transform) child.localRotation = tilt;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { return; }
        foreach (Transform child in transform) child.localRotation = Quaternion.identity;
    }

    private void StopSpin()
    {
        _spinCts?.Cancel();
        _spinCts = null;
        foreach (Transform child in transform) child.localRotation = Quaternion.identity;
    }

    /// <summary>칸 하나를 90°씩 시계 방향으로 <paramref name="times"/>번 돌린다(아래가 −y인 판 좌표).</summary>
    public static Vector2Int RotateCW(Vector2Int o, int times)
    {
        times = ((times % 4) + 4) % 4;
        for (int i = 0; i < times; i++) o = new Vector2Int(o.y, -o.x);
        return o;
    }

    /// <summary>아이템 연결 해제. 보관함 반환 또는 폐기 시 호출.</summary>
    public void UnbindItem() => ItemData = null;

    /// <summary>블록 묶음 가운데(셰이프 로컬 · 칸 간격 반영) — 보관함에서 집을 때 이 점을 커서 아래에 둔다(10-22).</summary>
    public Vector2 BlockBoundsCenterLocal()
    {
        if (cellOffsets == null || cellOffsets.Count == 0) return Vector2.zero;
        Vector2Int min = cellOffsets[0], max = cellOffsets[0];
        foreach (var o in cellOffsets) { min = Vector2Int.Min(min, o); max = Vector2Int.Max(max, o); }
        return (Vector2)(min + max) * 0.5f * cellSize;
    }

    /// <summary>
    /// <b>첫 블록을 (0,0)으로 본</b> 각 블록의 칸 오프셋. 클릭 배치가 "이 칸을 누르면 어디까지 덮이나"를
    /// 계산하는 데 쓴다. cellOffsets 원본은 기준점이 모양마다 달라(음수 포함) 그대로 못 쓴다.
    /// </summary>
    public List<Vector2Int> CellOffsetsFromFirstBlock()
    {
        if (cellOffsets == null || cellOffsets.Count == 0) return null;

        var origin = cellOffsets[0];
        var result = new List<Vector2Int>(cellOffsets.Count);
        foreach (var o in cellOffsets) result.Add(o - origin);
        return result;
    }

    /// <summary>
    /// 드래그하는 블록의 겉모습을 <b>그 아이템의 속성 룬</b>으로 바꾼다.
    /// 블록 프리팹은 속성을 모르는 공용 사각 타일이라, 아이템이 붙는 이 시점에만 알 수 있다.
    /// 스프라이트/틴트 규칙은 RuneArt.ResolveRuneCell에 맡긴다 — 예전엔 여기서만 틴트를
    /// 흰색으로 강제해서, 전용 아트가 없는 빛 룬이 손에 쥔 순간 흰 돌로 바뀌었다.
    /// 아트가 아예 없으면 프리팹 기본 외형을 그대로 둔다.
    /// </summary>
    private void ApplyRuneArt()
    {
        if (ItemData == null) return;

        // 1순위: 속성 블록 타일 — 룬이 차지한 칸은 그 룬의 속성으로 칠한다(룬 아이콘과 별개 슬롯).
        //        이미 속성색으로 그려진 타일이라 틴트를 곱하지 않는다.
        // 2순위: 타일이 없으면 기존 규칙(속성 룬 아트 → 없으면 등급 아트 + 속성색 틴트).
        var art  = RuneArt.GetBlockTile(ItemData.element);
        var tint = Color.white;

        if (art == null)
            RuneArt.ResolveRuneCell(ItemData.element, ItemData.rarity, Color.white, out art, out tint);

        if (art == null) return;

        foreach (Transform child in transform)
        {
            if (!child.TryGetComponent<Image>(out var img)) continue;
            img.sprite         = art;
            img.type           = Image.Type.Simple;
            img.color          = tint;
            img.preserveAspect = true;
        }

        ApplyLegendarySheen();
    }

    /// <summary>
    /// 전설 룬은 판 위에서도 금빛 광택이 흐른다(보관함·획득 카드와 같은 표시).
    /// 칸마다 광택을 붙이되 x 위치만큼 늦춰, 빛 한 줄기가 모양 전체를 왼쪽에서 오른쪽으로 가로지르게 한다.
    /// 같은 Shape에 다른 룬이 다시 묶이면 광택을 걷는다.
    /// </summary>
    private void ApplyLegendarySheen()
    {
        bool legendary = ItemData != null && ItemData.rarity == ItemRarity.Legendary;
        float minX = float.MaxValue;
        foreach (Transform child in transform)
            if (child is RectTransform r && r.anchoredPosition.x < minX) minX = r.anchoredPosition.x;

        foreach (Transform child in transform)
        {
            if (child is not RectTransform brt) continue;
            child.TryGetComponent<StagingSlotShimmer>(out var sheen);
            if (!legendary)
            {
                if (sheen == null) continue;
                Destroy(sheen);
                var mask = child.Find("ShimmerMask");
                if (mask != null) Destroy(mask.gameObject);
                continue;
            }

            if (sheen == null) sheen = child.gameObject.AddComponent<StagingSlotShimmer>();
            float w = Mathf.Max(1f, brt.rect.width);
            float speed = (w + 90f) / 0.5f;   // 광택 한 번이 칸을 지나는 속도(StagingSlotShimmer 스윕 0.5초)
            sheen.Configure(StagingSlotShimmer.LegendaryTint, 0.5f, 1.6f, w,
                            phase: -(brt.anchoredPosition.x - minX) / speed);
        }
    }

    // Placement occupancy
    public void SetOccupiedSquares(List<GridSquare> squares) => occupiedSquares = squares;
    public List<GridSquare> GetOccupiedSquares() => occupiedSquares;

    public void RebuildWithCellSize(float newCellSize)
    {
        cellSize = newCellSize;
        BuildShapeBlocks();
    }

    public void SetHome(RectTransform parent, Vector2 anchoredPos)
    {
        homeParent = parent;
        homeAnchoredPos = anchoredPos;
    }

    public void ReturnHome()
    {
        if (rt == null) rt = (RectTransform)transform;

        if (homeParent != null)
            rt.SetParent(homeParent, false);

        rt.anchoredPosition = homeAnchoredPos;
    }
    public void SetIgnoreLayout(bool ignore)
    {
        var le = GetComponent<LayoutElement>();
        if (le != null)
            le.ignoreLayout = ignore;
    }

    /// <summary>
    /// 조각 외곽선(10-18) — 블록을 새로 만들 때마다 칸 · 간격 · 블록 크기를 다시 넘긴다.
    /// 자식이 아니라 <b>루트</b>에 붙인다: GridManager · BoardManager는 Shape의 자식을 전부 블록으로 센다.
    /// </summary>
    private void RebuildOutline(float visualSize)
    {
        if (_outline == null && !TryGetComponent(out _outline))
        {
            _outline = gameObject.AddComponent<RunePieceOutline>();
            _outline.raycastTarget = false;   // 루트가 포인터를 먹으면 블록 집기 · 칸 누르기가 막힌다
        }
        _outline.SetCells(cellOffsets, cellSize, visualSize);
    }

    private void SetBlocksRaycastTarget(bool enable)
    {
        foreach (Transform child in transform)
        {
            var img = child.GetComponent<Image>();
            if (img != null) img.raycastTarget = enable;
        }
    }
}
