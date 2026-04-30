using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using TMPro;

/// <summary>
/// 서버 데이터 ↔ BoardManager ↔ 시너지 효과를 연결하는 브릿지.
/// GameScene에 배치하여 사용.
///
/// 역할:
/// 1. Panel_Grid에 Puzzle.prefab을 동적 로드하여 BoardManager 확보
/// 2. BlockDataManager에서 Grid 데이터 → GridAssetData → BoardManager에 등록
/// 3. SelectionRoot에 6개 썸네일 생성 → 클릭 시 확대 뷰 전환
/// 4. 아이템 획득 시 shape_id → ShapeData → BoardManager에 Shape 등록
/// 5. BoardManager.OnGridFilled 구독 → 시너지 효과 PlayerRuntimeStats에 적용
/// </summary>
public class BlockSynergyBridge : MonoBehaviour
{
    // ── Constants ──
    // Sheet2 원본 비율 551x709 (~0.78:1 세로형) 유지
    private const float THUMBNAIL_WIDTH = 230f;
    private const float THUMBNAIL_HEIGHT = 295f;
    private const float THUMBNAIL_SPACING = 14f;
    private const int THUMBNAIL_COLUMNS = 5;

    // ── Static ──
    public static BlockSynergyBridge Instance { get; private set; }

    // ── SerializeField ──
    [Header("Puzzle 프리팹 (직접 참조)")]
    [SerializeField] private GameObject puzzlePrefab;

    [Header("수동 참조 (없으면 Panel_Grid에서 동적 생성)")]
    [SerializeField] private BoardManager boardManager;

    [Header("썸네일 폰트")]
    [SerializeField] private TMP_FontAsset thumbnailFont;

    [Header("Bamao 디자인 — Inspector에서 할당")]
    [Tooltip("SelectionRoot 전체 배경 (예: BoardPaperFrame sprite, 9-slice 권장)")]
    [SerializeField] private Sprite boardBackgroundSprite;
    [Tooltip("그리드 썸네일 카드 배경 (예: Note1~4 sprite). 여러 개면 순서대로 번갈아 사용")]
    [SerializeField] private Sprite[] thumbnailCardSprites;
    [Tooltip("보드 장식 prefab (Bird, Feather, Nail 등). BoardBackground 위 / 카드 뒤에 깔린다.")]
    [SerializeField] private GameObject[] decorationPrefabs;

    // ── Private ──
    private readonly Dictionary<string, string> _gridIdBySOName = new();
    private readonly Dictionary<string, GridAssetData> _registeredGrids = new();
    private readonly List<GridThumbnail> _thumbnails = new();
    private readonly HashSet<string> _appliedGridIds = new();
    private GameObject _puzzleInstance;
    private bool _initialized;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (boardManager != null)
        {
            boardManager.OnGridFilled -= HandleGridFilled;
            boardManager.OnBackToSelection -= RefreshAllThumbnails;
            boardManager.OnGridSessionActivated -= HandleGridSessionActivated;
        }

        if (_puzzleInstance != null)
            Destroy(_puzzleInstance);

        _thumbnails.Clear();
        _registeredGrids.Clear();

        if (Instance == this) Instance = null;
    }

    // ── 초기화: Panel_Grid에 Puzzle UI 생성 + 서버 Grid 등록 ──

    /// <summary>
    /// BlockDataManager에서 모든 Grid 데이터를 읽어 BoardManager에 등록한다.
    /// GameRunBootstrapper 초기화 이후에 호출.
    /// </summary>
    public void InitializeGridsFromServer()
    {
        if (_initialized) return;

        var blockData = Managers.BlockData;
        if (blockData == null || !blockData.IsInitialized)
        {
            Debug.LogWarning("[BlockSynergyBridge] BlockData 미초기화");
            return;
        }

        // BoardManager 자동 탐색 (이미 내장된 Puzzle 프리팹에서)
        if (boardManager == null)
        {
            boardManager = GetComponentInChildren<BoardManager>(true);
        }

        // 그래도 없으면 Panel_Grid에 Puzzle.prefab 동적 로드
        if (boardManager == null)
        {
            SpawnPuzzleAndInitAsync().Forget();
            return;
        }

        // Puzzle 루트 RectTransform 보정 (Canvas 제거 후 scale 0 방지)
        EnsurePuzzleRectTransform();

        // BoardManager.Awake 강제 실행 (Panel_Grid 비활성 시 Awake 미실행 방지)
        EnsureBoardManagerAwake();

        boardManager.OnGridFilled -= HandleGridFilled;
        boardManager.OnGridFilled += HandleGridFilled;
        boardManager.OnBackToSelection -= RefreshAllThumbnails;
        boardManager.OnBackToSelection += RefreshAllThumbnails;
        boardManager.OnGridSessionActivated -= HandleGridSessionActivated;
        boardManager.OnGridSessionActivated += HandleGridSessionActivated;

        RegisterAllGrids(blockData);
    }

    private async UniTaskVoid SpawnPuzzleAndInitAsync()
    {
        // Panel_Grid 찾기 (HudView 하위, 비활성 포함)
        var panelGrid = FindPanelGrid();
        if (panelGrid == null)
        {
            Debug.LogWarning("[BlockSynergyBridge] Panel_Grid를 찾을 수 없음");
            return;
        }

        if (puzzlePrefab == null)
        {
            Debug.LogWarning("[BlockSynergyBridge] puzzlePrefab이 할당되지 않음");
            return;
        }

        // Panel_Grid를 일시 활성화 (Instantiate + Start() 실행 보장)
        bool wasActive = panelGrid.gameObject.activeSelf;
        if (!wasActive) panelGrid.gameObject.SetActive(true);

        _puzzleInstance = Instantiate(puzzlePrefab, panelGrid);

        // RectTransform 풀 스트레치
        var rt = _puzzleInstance.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        // BoardManager 참조 확보
        boardManager = _puzzleInstance.GetComponentInChildren<BoardManager>(true);
        if (boardManager == null)
        {
            Debug.LogError("[BlockSynergyBridge] Puzzle 프리팹에 BoardManager 없음");
            if (!wasActive) panelGrid.gameObject.SetActive(false);
            return;
        }

        // 1프레임 대기 (BoardManager.Awake/Start 실행 보장)
        await UniTask.Yield();

        boardManager.OnGridFilled += HandleGridFilled;

        var blockData = Managers.BlockData;
        if (blockData != null && blockData.IsInitialized)
            RegisterAllGrids(blockData);

        // 원래 비활성이었으면 다시 비활성 (Tab키로 열 때 활성화)
        if (!wasActive) panelGrid.gameObject.SetActive(false);

        Debug.Log("[BlockSynergyBridge] Puzzle UI 생성 + 서버 Grid 등록 완료");
    }

    private void RegisterAllGrids(BlockDataManager blockData)
    {
        _initialized = true;

        // 이벤트 구독 (중복 방지)
        boardManager.OnGridFilled -= HandleGridFilled;
        boardManager.OnGridFilled += HandleGridFilled;

        // order 순서로 Grid 데이터 수집
        var sortedIds = blockData.GetGridIdsSortedByOrder();
        _registeredGrids.Clear();

        foreach (var gridId in sortedIds)
        {
            var meta = blockData.GetGridMeta(gridId);
            if (meta == null) continue;

            var gridAssetData = ConvertToGridAssetData(gridId, meta);
            _registeredGrids[gridId] = gridAssetData;

            Debug.Log($"[BlockSynergyBridge] Grid 등록 (order={meta.order}): {gridId} ({meta.grid_name}) {meta.rows}x{meta.cols}");
        }

        // 썸네일 UI 생성 → 선택 모드로 시작
        BuildThumbnails(sortedIds, blockData);

        // GameplayRoot(편집 화면) 레이아웃 구성
        if (boardManager.gameplayRoot != null)
        {
            // GameplayRoot: 부모 stretch fill (Puzzle.prefab 기본값 100×100 → 전체 화면)
            var gameplayRT = boardManager.gameplayRoot.GetComponent<RectTransform>();
            if (gameplayRT != null)
            {
                gameplayRT.anchorMin = Vector2.zero;
                gameplayRT.anchorMax = Vector2.one;
                gameplayRT.offsetMin = Vector2.zero;
                gameplayRT.offsetMax = Vector2.zero;
                gameplayRT.pivot = new Vector2(0.5f, 0.5f);
            }

            EnsureBoardBackground(boardManager.gameplayRoot);
            EnsureDecorations(boardManager.gameplayRoot);

            // GridHost: 좌측 68% × 상하 80% — 그리드 편집 영역
            if (boardManager.gridHost != null)
            {
                boardManager.gridHost.anchorMin = new Vector2(0.02f, 0.1f);
                boardManager.gridHost.anchorMax = new Vector2(0.68f, 0.9f);
                boardManager.gridHost.offsetMin = Vector2.zero;
                boardManager.gridHost.offsetMax = Vector2.zero;
                boardManager.gridHost.pivot = new Vector2(0.5f, 0.5f);
            }

            // ShapeScrollView: 우측 패널(72%~98%) — 스크롤 뷰포트 영역
            var shapeSSV = boardManager.gameplayRoot.GetComponentInChildren<ShapeScrollView>(true);
            if (shapeSSV != null)
            {
                var ssvRT = shapeSSV.transform as RectTransform;
                if (ssvRT != null)
                {
                    ssvRT.anchorMin = new Vector2(0.70f, 0.1f);
                    ssvRT.anchorMax = new Vector2(0.98f, 0.9f);
                    ssvRT.offsetMin = Vector2.zero;
                    ssvRT.offsetMax = Vector2.zero;
                    ssvRT.pivot = new Vector2(0.5f, 0.5f);
                }

                // ShapeHost를 ShapeScrollView 내부로 리패런트 (RectMask2D 클리핑 적용)
                if (boardManager.shapeHost != null &&
                    boardManager.shapeHost.parent != shapeSSV.transform)
                {
                    boardManager.shapeHost.SetParent(shapeSSV.transform, false);
                    // ScrollRect Content 표준 앵커: 너비=부모 전체, 상단 고정
                    boardManager.shapeHost.anchorMin = new Vector2(0f, 1f);
                    boardManager.shapeHost.anchorMax = new Vector2(1f, 1f);
                    boardManager.shapeHost.pivot     = new Vector2(0.5f, 1f);
                    boardManager.shapeHost.offsetMin = Vector2.zero;
                    boardManager.shapeHost.offsetMax = Vector2.zero;
                }
            }
            else if (boardManager.shapeHost != null)
            {
                // ShapeScrollView 없을 때 폴백
                boardManager.shapeHost.anchorMin = new Vector2(0.70f, 0.1f);
                boardManager.shapeHost.anchorMax = new Vector2(0.98f, 0.9f);
                boardManager.shapeHost.offsetMin = Vector2.zero;
                boardManager.shapeHost.offsetMax = Vector2.zero;
                boardManager.shapeHost.pivot = new Vector2(0.5f, 1f);
            }

            // spawnOrigin 수정: X=0(중앙), Y=80(상단 80px 아래서 시작)
            boardManager.spawnOrigin = new Vector2(0f, 80f);
        }

        boardManager.BackToSelection();
    }

    // ── 썸네일 UI 생성 ──

    private void BuildThumbnails(List<string> sortedIds, BlockDataManager blockData)
    {
        // 기존 썸네일 제거
        foreach (var thumb in _thumbnails)
            if (thumb != null) Destroy(thumb.gameObject);
        _thumbnails.Clear();

        // SelectionRoot 찾기
        var selectionRoot = boardManager.selectionRoot;
        if (selectionRoot == null)
        {
            Debug.LogWarning("[BlockSynergyBridge] SelectionRoot를 찾을 수 없음");
            return;
        }

        var selectionRT = selectionRoot.GetComponent<RectTransform>();

        // CharacterBG_Board (BamaoScene)와 동일: 부모 stretch fill, offset 0
        selectionRT.anchorMin = Vector2.zero;
        selectionRT.anchorMax = Vector2.one;
        selectionRT.offsetMin = Vector2.zero;
        selectionRT.offsetMax = Vector2.zero;
        selectionRT.pivot = new Vector2(0.5f, 0.5f);

        // 기존 레거시 자식 제거 (GridLoader 등)
        for (int i = selectionRT.childCount - 1; i >= 0; i--)
            Destroy(selectionRT.GetChild(i).gameObject);

        // GridLayoutGroup 설정
        SetupGridLayout(selectionRoot);

        // 썸네일 생성
        foreach (var gridId in sortedIds)
        {
            if (!_registeredGrids.TryGetValue(gridId, out var gridData)) continue;

            var meta = blockData.GetGridMeta(gridId);
            string displayName = meta?.grid_name ?? gridId;

            var thumbnail = CreateThumbnail(selectionRT, gridId, gridData, displayName);
            _thumbnails.Add(thumbnail);
        }

        Debug.Log($"[BlockSynergyBridge] 썸네일 {_thumbnails.Count}개 생성 완료");
    }

    private void SetupGridLayout(GameObject root)
    {
        // 기존 LayoutGroup 제거
        var oldVertical = root.GetComponent<VerticalLayoutGroup>();
        if (oldVertical != null) Destroy(oldVertical);
        var oldHorizontal = root.GetComponent<HorizontalLayoutGroup>();
        if (oldHorizontal != null) Destroy(oldHorizontal);
        var oldGrid = root.GetComponent<GridLayoutGroup>();
        if (oldGrid != null) Destroy(oldGrid);

        // 보드 배경 (선택적 — Inspector 할당 시)
        EnsureBoardBackground(root);

        // 장식 prefab들 (Bird, Feather 등) — 카드보다 뒤에 깔림
        EnsureDecorations(root);

        // GridLayoutGroup 추가 (3열, 행 가변 — 그리드 추가 시 자동 확장)
        var gridLayout = root.AddComponent<GridLayoutGroup>();
        gridLayout.cellSize = new Vector2(THUMBNAIL_WIDTH, THUMBNAIL_HEIGHT);
        gridLayout.spacing = Vector2.one * THUMBNAIL_SPACING;
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = THUMBNAIL_COLUMNS;
        gridLayout.childAlignment = TextAnchor.UpperCenter;
        gridLayout.padding = new RectOffset(120, 120, 100, 100);

        // ContentSizeFitter 확보
        if (!root.TryGetComponent<ContentSizeFitter>(out var fitter))
            fitter = root.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
    }

    /// <summary>
    /// SelectionRoot 자체에 Bamao 보드 sprite를 깐다 (Inspector 할당 시).
    /// SelectionRoot 자식 0번으로 들어가 카드보다 뒤에 그려짐.
    /// </summary>
    private void EnsureBoardBackground(GameObject root)
    {
        if (boardBackgroundSprite == null) return;

        // 이미 추가된 배경이 있으면 스킵
        var existing = root.transform.Find("BoardBackground");
        if (existing != null) return;

        var bgGo = new GameObject("BoardBackground", typeof(RectTransform), typeof(Image));
        bgGo.transform.SetParent(root.transform, false);
        bgGo.transform.SetAsFirstSibling();

        var bgRT = bgGo.GetComponent<RectTransform>();
        // SelectionRoot보다 사방 150px 더 크게 — 적당한 확장
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = new Vector2(-150f, -150f);
        bgRT.offsetMax = new Vector2(150f, 150f);

        var bgImg = bgGo.GetComponent<Image>();
        bgImg.sprite = boardBackgroundSprite;
        bgImg.type = Image.Type.Simple;
        bgImg.preserveAspect = true; // 비율 유지
        bgImg.raycastTarget = false;

        // GridLayoutGroup이 배경을 카드 아이템으로 취급하지 않도록
        var le = bgGo.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
    }

    /// <summary>
    /// 데모 씬의 장식 prefab(Bird/Feather/Nail 등)을 SelectionRoot에 인스턴스화한다.
    /// 카드보다 뒤(sibling 1)에 배치하여 가리지 않도록 한다.
    /// </summary>
    private void EnsureDecorations(GameObject root)
    {
        if (decorationPrefabs == null || decorationPrefabs.Length == 0) return;

        var existing = root.transform.Find("BoardDecorations");
        if (existing != null) return;

        var decRoot = new GameObject("BoardDecorations", typeof(RectTransform));
        decRoot.transform.SetParent(root.transform, false);
        // BoardBackground(0) 다음, 카드(GridLayoutGroup이 채우는 부분)보다 앞에 배치 → 카드 뒤로 그려짐
        decRoot.transform.SetSiblingIndex(1);

        var decRT = decRoot.GetComponent<RectTransform>();
        decRT.anchorMin = Vector2.zero;
        decRT.anchorMax = Vector2.one;
        decRT.offsetMin = Vector2.zero;
        decRT.offsetMax = Vector2.zero;

        var decLE = decRoot.AddComponent<LayoutElement>();
        decLE.ignoreLayout = true;

        foreach (var prefab in decorationPrefabs)
        {
            if (prefab == null) continue;
            var instObj = Instantiate(prefab);
            if (instObj is GameObject inst)
            {
                inst.transform.SetParent(decRoot.transform, false);
            }
            else
            {
                Debug.LogWarning($"[BlockSynergyBridge] Decoration prefab is not a GameObject: {prefab.name}");
                if (instObj != null) Destroy(instObj);
            }
        }
    }

    private GridThumbnail CreateThumbnail(RectTransform parent, string gridId,
                                           GridAssetData data, string displayName)
    {
        // 루트 GO
        var go = new GameObject($"Thumb_{gridId}", typeof(RectTransform), typeof(Image), typeof(GridThumbnail));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(THUMBNAIL_WIDTH, THUMBNAIL_HEIGHT);

        // 배경 — Bamao Note sprite 순환 사용 (Inspector 할당 시), 미할당 시 단색 폴백
        var bg = go.GetComponent<Image>();
        if (thumbnailCardSprites != null && thumbnailCardSprites.Length > 0)
        {
            var spr = thumbnailCardSprites[_thumbnails.Count % thumbnailCardSprites.Length];
            if (spr != null)
            {
                bg.sprite = spr;
                bg.type = Image.Type.Simple;
                bg.preserveAspect = true;
                bg.color = Color.white;
            }
            else
            {
                bg.color = new Color(0.12f, 0.12f, 0.18f, 0.9f);
            }
        }
        else
        {
            bg.color = new Color(0.12f, 0.12f, 0.18f, 0.9f);
        }

        // 이름 텍스트 (상단)
        var nameGO = new GameObject("NameText", typeof(RectTransform), typeof(TextMeshProUGUI));
        nameGO.transform.SetParent(go.transform, false);
        var nameRT = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0f, 1f);
        nameRT.anchorMax = new Vector2(1f, 1f);
        nameRT.pivot = new Vector2(0.5f, 1f);
        nameRT.sizeDelta = new Vector2(0f, 30f);
        nameRT.anchoredPosition = new Vector2(0f, -4f);

        var nameText = nameGO.GetComponent<TMP_Text>();
        nameText.text = displayName;
        nameText.fontSize = 14f;
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.color = Color.white;
        nameText.enableAutoSizing = false;
        nameText.overflowMode = TextOverflowModes.Ellipsis;
        nameText.raycastTarget = false;
        if (thumbnailFont != null)
            nameText.font = thumbnailFont;

        // 그리드 프리뷰 컨테이너 — NameText(상단 15%) 제외한 나머지에 좌우 대칭으로 중앙 정렬
        var containerGO = new GameObject("GridPreview", typeof(RectTransform));
        containerGO.transform.SetParent(go.transform, false);
        var containerRT = containerGO.GetComponent<RectTransform>();
        containerRT.anchorMin = new Vector2(0.15f, 0.1f);
        containerRT.anchorMax = new Vector2(0.85f, 0.85f);
        containerRT.offsetMin = Vector2.zero;
        containerRT.offsetMax = Vector2.zero;
        containerRT.pivot = new Vector2(0.5f, 0.5f);

        // 보더 이미지 (선택 피드백용)
        var borderGO = new GameObject("Border", typeof(RectTransform), typeof(Image));
        borderGO.transform.SetParent(go.transform, false);
        borderGO.transform.SetAsFirstSibling();
        var borderRT = borderGO.GetComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero;
        borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = Vector2.zero;
        borderRT.offsetMax = Vector2.zero;
        var borderImg = borderGO.GetComponent<Image>();
        // Sheet2 sprite를 가리지 않도록 평소엔 투명. GridThumbnail의 RefreshOccupied 등에서 시각 피드백 시점에 색을 켠다.
        borderImg.color = new Color(1f, 1f, 1f, 0f);
        borderImg.raycastTarget = false;

        // GridThumbnail 컴포넌트 설정
        var thumbnail = go.GetComponent<GridThumbnail>();

        // SerializeField에 접근 불가하므로 리플렉션 대신 public Setup 사용
        // nameText, gridContainer, borderImage는 Setup 내부에서 참조가 필요
        // → GridThumbnail에 Init 메서드 추가 필요

        thumbnail.Init(nameText, containerRT, borderImg);
        thumbnail.Setup(gridId, data, displayName, boardManager);

        return thumbnail;
    }

    private Transform FindPanelGrid()
    {
        // 자신(@HUD)의 하위에서 Panel_Grid 검색
        return FindChildRecursive(transform, "Panel_Grid");
    }

    // ── Grid 자동 스케일 ──

    /// <summary>그리드 세션 활성화 시 호출 — 1프레임 후 GridHost에 맞춰 스케일.</summary>
    private void HandleGridSessionActivated(Grid gridInstance)
    {
        FitGridToHostAsync(gridInstance, this.GetCancellationTokenOnDestroy()).Forget();
    }

    private async UniTaskVoid FitGridToHostAsync(Grid grid, System.Threading.CancellationToken ct)
    {
        try
        {
            // Canvas 레이아웃 계산 완료까지 1프레임 대기
            await UniTask.Yield(ct);

            if (grid == null || boardManager?.gridHost == null) return;
            var gridRT = grid.transform as RectTransform;
            if (gridRT == null) return;
            if (!boardManager.gridHost.gameObject.activeInHierarchy) return;

            float hostW = boardManager.gridHost.rect.width;
            float hostH = boardManager.gridHost.rect.height;

            // rect가 아직 0이면 강제 리빌드 후 한 번 더 대기
            if (hostW <= 1f || hostH <= 1f)
            {
                var parent = boardManager.gridHost.parent as RectTransform;
                if (parent != null) LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
                await UniTask.Yield(ct);
                hostW = boardManager.gridHost.rect.width;
                hostH = boardManager.gridHost.rect.height;
            }
            if (hostW <= 1f || hostH <= 1f) return;

            // Grid를 scale=1로 리셋하여 자연 바운딩 박스 계산
            gridRT.localScale = Vector3.one;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            bool hasChild = false;

            foreach (Transform child in grid.transform)
            {
                var childRT = child as RectTransform;
                if (childRT == null) continue;

                // localScale 반영한 실제 반치수 (부모 좌표계 기준)
                float scl = child.localScale.x;
                float hw  = childRT.sizeDelta.x * scl * 0.5f;
                float hh  = childRT.sizeDelta.y * scl * 0.5f;
                var   pos = childRT.anchoredPosition;

                if (pos.x - hw < minX) minX = pos.x - hw;
                if (pos.x + hw > maxX) maxX = pos.x + hw;
                if (pos.y - hh < minY) minY = pos.y - hh;
                if (pos.y + hh > maxY) maxY = pos.y + hh;
                hasChild = true;
            }

            if (!hasChild) return;

            float naturalW = maxX - minX;
            float naturalH = maxY - minY;
            if (naturalW <= 0f || naturalH <= 0f) return;

            // 85% 채움 (여백 확보)
            float scale = Mathf.Min(hostW * 0.85f / naturalW, hostH * 0.85f / naturalH);
            gridRT.localScale       = Vector3.one * scale;
            gridRT.anchoredPosition = Vector2.zero;

            Debug.Log($"[BlockSynergyBridge] Grid 스케일: {scale:F3} (host {hostW:F0}×{hostH:F0}, natural {naturalW:F0}×{naturalH:F0})");
        }
        catch (System.OperationCanceledException) { }
    }

    // ── 치트 ──

    /// <summary>디버그용: 특정 그리드의 시너지를 강제 발동한다.</summary>
    public void CheatTriggerSynergy(string gridId)
    {
        ApplySynergyEffects(gridId);
        RefreshAllThumbnails();
    }

    // ── 썸네일 점유 상태 갱신 ──

    /// <summary>모든 썸네일의 점유 상태를 현재 세션 데이터로 갱신한다.</summary>
    public void RefreshAllThumbnails()
    {
        if (boardManager == null) return;

        foreach (var thumb in _thumbnails)
        {
            if (thumb == null) continue;
            var squares = boardManager.GetGridSquares(thumb.GridId);
            thumb.RefreshOccupied(squares);
        }
    }

    // ── 아이템 획득 시 블록(Shape) 등록 ──

    /// <summary>
    /// 아이템의 shape_id로 블록을 생성하여 BoardManager에 Shape 등록.
    /// </summary>
    public void RegisterShapeFromItem(int shapeId)
    {
        var blockData = Managers.BlockData;
        if (blockData == null || boardManager == null) return;

        var shapeEntry = blockData.GetShape(shapeId);
        if (shapeEntry == null)
        {
            Debug.LogWarning($"[BlockSynergyBridge] Shape 없음: {shapeId}");
            return;
        }

        var offsets = BlockDataManager.ParseCellOffsets(shapeEntry);

        // ShapeAssetSO를 런타임 생성
        var shapeSO = ScriptableObject.CreateInstance<ShapeAssetSO>();
        shapeSO.shapeName = shapeEntry.shape_name;
        shapeSO.shapeBlockPrefab = boardManager.defaultShapeBlockPrefab;
        shapeSO.cellOffsets = offsets;
        shapeSO.cellSize = shapeEntry.cell_size > 0 ? shapeEntry.cell_size : 90f;

        // 공용 풀에 직접 추가 (활성 그리드 없어도 누적됨)
        boardManager.SpawnSharedShape(shapeSO);
        Debug.Log($"[BlockSynergyBridge] Shape 추가(공용풀): {shapeEntry.shape_name} (id={shapeId})");
    }

    // ── Grid 완성 시 시너지 효과 적용 ──

    private void HandleGridFilled(GridAssetSO filledAsset)
    {
        if (filledAsset == null) return;

        string gridId = null;
        foreach (var kvp in _gridIdBySOName)
        {
            if (kvp.Key == filledAsset.name)
            {
                gridId = kvp.Value;
                break;
            }
        }

        if (string.IsNullOrEmpty(gridId))
        {
            Debug.LogWarning($"[BlockSynergyBridge] grid_id 매핑 실패: {filledAsset.name}");
            return;
        }

        ApplySynergyEffects(gridId);
    }

    private void ApplySynergyEffects(string gridId)
    {
        // 이미 적용된 그리드는 중복 적용하지 않음
        if (_appliedGridIds.Contains(gridId)) return;

        var blockData = Managers.BlockData;
        if (blockData == null) return;

        var entries = blockData.GetGrid(gridId);
        if (entries == null) return;

        var run = AppBootstrapper.Instance?.CurrentRun;
        var player = run?.Player;
        if (player == null) return;

        var stats = player.RuntimeStats;

        _appliedGridIds.Add(gridId);

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.effect_type)) continue;

            switch (entry.trigger)
            {
                case "Always":
                    stats.ApplySynergyEffect(entry.effect_type, entry.value);
                    break;

                case "OnHit":
                    stats.RegisterConditionalSynergy(new ConditionalSynergy
                    {
                        gridId     = gridId,
                        effectType = entry.effect_type,
                        trigger    = "OnHit",
                        value      = entry.value,
                        maxStack   = entry.max_stack > 0 ? entry.max_stack : 1,
                        duration   = entry.duration,
                    });
                    break;

                case "OnLowHp":
                    stats.RegisterConditionalSynergy(new ConditionalSynergy
                    {
                        gridId     = gridId,
                        effectType = entry.effect_type,
                        trigger    = "OnLowHp",
                        value      = entry.value,
                        threshold  = entry.value2 > 0f ? entry.value2 : 0.3f,
                    });
                    break;
            }

            // GameRunSession에 이력 기록 (씬 전환 시 복원용)
            run?.RecordSynergy(new SynergyRecord
            {
                gridId     = gridId,
                effectType = entry.effect_type,
                trigger    = entry.trigger,
                value      = entry.value,
                value2     = entry.value2,
                maxStack   = entry.max_stack,
                duration   = entry.duration,
            });

            Debug.Log($"[BlockSynergyBridge] 시너지 발동: {gridId} → {entry.effect_type} ({entry.trigger}) +{entry.value}");
        }
    }

    /// <summary>런 종료 시 적용 이력 초기화. 외부에서 호출.</summary>
    public void ClearAppliedGrids()
    {
        _appliedGridIds.Clear();
    }

    // ── 변환 유틸 ──

    private GridAssetData ConvertToGridAssetData(string gridId, BlockGridEntry meta)
    {
        var rows01 = BlockDataManager.ParseGridRows(meta);
        int rowCount = meta.rows > 0 ? meta.rows : rows01.Length;
        int colCount = meta.cols > 0 ? meta.cols : (rows01.Length > 0 ? rows01[0].Length : 0);

        // SO 이름 → grid_id 매핑 저장
        _gridIdBySOName[gridId] = gridId;

        return new GridAssetData
        {
            id = gridId,
            displayName = meta.grid_name,
            pattern = new GridPatternData
            {
                rows = rowCount,
                columns = colCount,
                rows01 = rows01,
            },
            visual = new GridVisualData(),
            spawnableShapes = System.Array.Empty<ShapeData>(),
        };
    }

    private void EnsureBoardManagerAwake()
    {
        if (boardManager == null) return;

        // Panel_Grid이 비활성이면 BoardManager.Awake가 안 돌았을 수 있음
        // 임시 활성화 → Awake 트리거 → 복원
        if (!boardManager.gameObject.activeInHierarchy)
        {
            var panelGrid = FindPanelGrid();
            if (panelGrid != null)
            {
                bool wasActive = panelGrid.gameObject.activeSelf;
                panelGrid.gameObject.SetActive(true);
                // Awake/Start가 즉시 실행됨
                if (!wasActive)
                    panelGrid.gameObject.SetActive(false);
                // Awake 트리거 완료
            }
        }
    }

    private void EnsurePuzzleRectTransform()
    {
        var gameplayRoot = boardManager.transform.parent;
        var puzzleRoot = gameplayRoot?.parent;
        var panelGrid = puzzleRoot?.parent;

        // Panel_Grid, Puzzle, SelectionRoot만 stretch-fill (컨테이너 역할)
        StretchFill(panelGrid);
        StretchFill(puzzleRoot);

        // SelectionRoot만 stretch (GameplayRoot와 자식은 원래 레이아웃 유지)
        if (puzzleRoot != null)
        {
            var selectionRoot = boardManager.selectionRoot?.GetComponent<RectTransform>();
            StretchFill(selectionRoot);
        }
    }

    private static void StretchFill(Transform t)
    {
        if (t == null) return;
        StretchFill(t.GetComponent<RectTransform>());
    }

    private static void StretchFill(RectTransform rt)
    {
        if (rt == null) return;
        if (rt.localScale.sqrMagnitude < 0.01f)
            rt.localScale = Vector3.one;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindChildRecursive(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
