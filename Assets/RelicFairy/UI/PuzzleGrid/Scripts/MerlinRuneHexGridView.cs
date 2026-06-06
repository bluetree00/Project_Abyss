using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 멀린의 룬 헥사곤 그리드 뷰.
///
/// RuneDataManager.GetZoneMapRows()로 존맵 데이터를 읽어 다이아몬드 형태 그리드를 렌더링한다.
/// 각 셀은 존 코드에 따라 색상이 지정된 Image 컴포넌트로 구성된다.
/// 아이템 배치 미리보기 하이라이트를 지원한다.
///
/// BuildGrid() 호출 시 시각 셀과 동일 위치에 투명 GridSquare 오브젝트를 생성하여
/// GridManager 위치 기반 드래그-앤-드롭 배치 시스템과 연동한다.
/// HexGrid 프로퍼티로 생성된 Grid 컴포넌트를 반환한다.
/// </summary>
public sealed class MerlinRuneHexGridView : MonoBehaviour
{
    // ── Constants ──
    private const float CELL_SIZE = 50f;
    private const float CELL_GAP  = 4f;
    private const float CELL_STEP = CELL_SIZE + CELL_GAP;   // 54f — GridManager.GetGap() 기준값

    // 한 면만 붙어도 배치 허용: 점유 셀로부터 이 거리 내의 빈 셀을 isPlaceable=true로 표시
    // 값 = 최대 블록 선형 길이(5셀) → 5셀짜리 블록도 한 끝만 닿으면 배치 가능
    private const int ADJACENCY_REACH = 5;

    // 존별 색상은 ElementDef에서 조회. 미정의 코드 폴백만 보유.
    private static readonly Color COLOR_EMPTY  = new(0.15f, 0.15f, 0.20f, 0.30f);

    // 하이라이트 시 alpha 증가 배수
    private const float HIGHLIGHT_ALPHA = 1.0f;
    private const float NORMAL_ALPHA    = 0.75f;

    // ── Private fields ──
    private readonly List<GameObject>              _cellObjects       = new();
    private readonly Dictionary<Vector2Int, Image> _cellImages        = new();
    private readonly Dictionary<Vector2Int, Image> _cellFlashImages   = new();
    private readonly Dictionary<Vector2Int, Color>  _cellBaseColors    = new();
    private readonly HashSet<Vector2Int>             _occupiedPositions = new();
    private readonly Dictionary<Vector2Int, char>    _cellZones         = new();

    // 드래그 배치용 GridSquare 레이어
    private GameObject    _gridSquaresRoot;
    private GridAssetSO   _runtimeGridAsset;
    private GridVisualSO  _runtimeVisualSO;
    private bool          _isBuilt;

    // ── Public API ──

    /// <summary>BuildGrid() 후 GridManager에 등록할 Grid 컴포넌트.</summary>
    public Grid HexGrid => _gridSquaresRoot != null
        ? _gridSquaresRoot.GetComponent<Grid>()
        : null;

    /// <summary>
    /// RuneDataManager에서 존맵 데이터를 읽어 그리드를 생성한다.
    /// 이미 빌드됐으면 GridSquare를 재사용하고 시각 점유 상태만 갱신한다.
    /// </summary>
    public void BuildGrid()
    {
        if (_isBuilt)
        {
            RefreshOccupiedCells();
            return;
        }

        ClearGrid();

        var runeData = Managers.RuneData;
        if (runeData == null)
        {
            Debug.LogWarning("[MerlinRuneHexGridView] RuneDataManager가 없습니다.");
            return;
        }

        var rows = runeData.GetZoneMapRows();
        if (rows == null || rows.Count == 0)
        {
            Debug.LogWarning("[MerlinRuneHexGridView] 존맵 데이터가 없습니다.");
            return;
        }

        // 전체 그리드 중심 정렬을 위해 최대 패턴 길이 파악
        int maxPatternLen = 0;
        foreach (var row in rows)
        {
            if (row.pattern != null && row.pattern.Length > maxPatternLen)
                maxPatternLen = row.pattern.Length;
        }

        float totalHeight = rows.Count * CELL_STEP;

        // 중심 기준 시작 오프셋
        float startY = totalHeight * 0.5f - CELL_STEP * 0.5f;

        // ── GridSquare 레이어 ──────────────────────────────────────────
        _gridSquaresRoot = new GameObject("HexGridSquares", typeof(RectTransform));
        _gridSquaresRoot.transform.SetParent(transform, false);
        var gsRootRT = _gridSquaresRoot.GetComponent<RectTransform>();
        gsRootRT.anchorMin = Vector2.zero;
        gsRootRT.anchorMax = Vector2.one;
        gsRootRT.sizeDelta = Vector2.zero;

        var hexGrid = _gridSquaresRoot.AddComponent<Grid>();

        // 런타임 GridVisualSO — GridManager.GetGap()이 44f를 반환하도록 squareGap 설정
        _runtimeVisualSO = ScriptableObject.CreateInstance<GridVisualSO>();
        _runtimeVisualSO.squareGap = CELL_STEP;
        _runtimeVisualSO.autoCenter = false;

        _runtimeGridAsset = ScriptableObject.CreateInstance<GridAssetSO>();
        _runtimeGridAsset.visual = _runtimeVisualSO;

        var squares = new List<GridSquare>();

        // 전체 그리드 X 기준 고정: 동일 절대 열(absCol) = 동일 X 위치
        // 행마다 독립 중앙 정렬하면 같은 col이 행마다 다른 X에 위치해 모양 배치 시 어긋남
        float globalStartX = -(maxPatternLen * CELL_STEP - CELL_GAP) * 0.5f + CELL_SIZE * 0.5f;

        // ── 시각 셀 + GridSquare 동시 생성 ────────────────────────────
        foreach (var row in rows)
        {
            if (row.pattern == null) continue;

            int   len       = row.pattern.Length;
            int   colOffset = (maxPatternLen - len) / 2;  // 다이아몬드 중심 정렬
            float posY      = startY - row.hex_row * CELL_STEP;

            for (int c = 0; c < len; c++)
            {
                char code   = row.pattern[c];
                int  absCol = c + colOffset;
                var  pos    = new Vector2Int(absCol, row.hex_row);
                float posX  = globalStartX + absCol * CELL_STEP;

                CreateCell(pos, posX, posY, code);
                CreateGridSquare(pos, posX, posY, row.hex_row, absCol, squares);
            }
        }

        hexGrid.InitFromPrebuiltSquares(_runtimeGridAsset, squares);
        _isBuilt = true;
    }

    /// <summary>
    /// 지정된 셀 위치들을 하이라이트(밝게)하거나 원래 색상으로 복귀시킨다.
    /// 아이템 미리보기 시 호출.
    /// </summary>
    public void HighlightCells(Vector2Int[] positions, bool highlight)
    {
        if (positions == null) return;

        foreach (var pos in positions)
        {
            if (!_cellImages.TryGetValue(pos, out var img)) continue;
            if (!_cellBaseColors.TryGetValue(pos, out var bc)) continue;
            if (highlight)
                img.color = NormalColor(bc);
            else
            {
                bool occupied = _occupiedPositions.Contains(pos);
                img.color = occupied ? OccupiedColor(bc) : EmptyColor(bc);
            }
        }
    }

    /// <summary>배치된 셀들을 표시 상태로 갱신하고 Bridge에 존별 카운트를 알린다.</summary>
    public void RefreshPlacedCells(HashSet<Vector2Int> placedPositions)
    {
        _occupiedPositions.Clear();
        if (placedPositions != null)
            foreach (var p in placedPositions) _occupiedPositions.Add(p);

        foreach (var kvp in _cellImages)
        {
            if (!_cellBaseColors.TryGetValue(kvp.Key, out var bc)) continue;
            bool placed = _occupiedPositions.Contains(kvp.Key);
            kvp.Value.color = placed ? OccupiedColor(bc) : EmptyColor(bc);
        }

        var zoneCounts   = GetZoneOccupiedCounts();
        var clusterSizes = ZoneClusterCalculator.Compute(_occupiedPositions, _cellZones);
        MerlinRuneBridge.Instance?.OnZoneCellsUpdated(zoneCounts, clusterSizes);
    }

    /// <summary>
    /// 점유 셀로부터 BFS로 ADJACENCY_REACH 거리 내의 빈 셀을 isPlaceable=true로 표시한다.
    /// 멀티셀 블록의 한 면만 기존 블록에 닿아도 배치를 허용하는 구조.
    /// 그리드가 비어 있으면 전체 셀 개방. 배치/제거 후 호출.
    /// </summary>
    public void UpdateAdjacencyConstraints()
    {
        var grid = HexGrid;
        if (grid == null) return;
        var squares = grid.GetGridSquares();
        if (squares == null) return;

        bool hasOccupied = _occupiedPositions.Count > 0;

        if (!hasOccupied)
        {
            foreach (var sq in squares)
                if (!sq.isOccupied) sq.isPlaceable = true;
            return;
        }

        // BFS: 점유 셀 경계에서 ADJACENCY_REACH 거리까지 확장
        var reachable = new HashSet<Vector2Int>();
        var frontier  = new Queue<(Vector2Int pos, int depth)>();

        foreach (var occ in _occupiedPositions)
            frontier.Enqueue((occ, 0));

        while (frontier.Count > 0)
        {
            var (pos, depth) = frontier.Dequeue();
            if (depth >= ADJACENCY_REACH) continue;

            TryExpandBFS(pos + new Vector2Int(-1, 0), reachable, frontier, depth);
            TryExpandBFS(pos + new Vector2Int( 1, 0), reachable, frontier, depth);
            TryExpandBFS(pos + new Vector2Int( 0,-1), reachable, frontier, depth);
            TryExpandBFS(pos + new Vector2Int( 0, 1), reachable, frontier, depth);
        }

        foreach (var sq in squares)
        {
            if (sq.isOccupied) continue;
            sq.isPlaceable = reachable.Contains(new Vector2Int(sq.col, sq.row));
        }
    }

    private void TryExpandBFS(Vector2Int n,
        HashSet<Vector2Int> reachable,
        Queue<(Vector2Int, int)> frontier,
        int depth)
    {
        if (_occupiedPositions.Contains(n)) return;
        if (!_cellZones.ContainsKey(n)) return;
        if (reachable.Add(n))
            frontier.Enqueue((n, depth + 1));
    }

    /// <summary>존별 점유 셀 수를 반환한다. key = zone_id 문자열.</summary>
    public Dictionary<string, int> GetZoneOccupiedCounts()
    {
        var counts = new Dictionary<string, int>();
        foreach (var pos in _occupiedPositions)
        {
            if (!_cellZones.TryGetValue(pos, out var code)) continue;
            string zoneId = ZoneCharToId(code);
            if (zoneId == null) continue;
            counts.TryGetValue(zoneId, out int cur);
            counts[zoneId] = cur + 1;
        }
        return counts;
    }

    /// <summary>zone_id 기준으로 점유된 셀 수를 반환한다.</summary>
    public int CountOccupiedByZone(string zoneId)
    {
        if (string.IsNullOrEmpty(zoneId)) return 0;
        char code = ZoneIdToChar(zoneId);
        int count = 0;
        foreach (var pos in _occupiedPositions)
            if (_cellZones.TryGetValue(pos, out var c) && c == code) count++;
        return count;
    }

    /// <summary>전체 존별 셀 총 개수 (그리드 데이터 기준).</summary>
    public Dictionary<string, int> GetZoneTotalCounts()
    {
        var totals = new Dictionary<string, int>();
        foreach (var kvp in _cellZones)
        {
            string zoneId = ZoneCharToId(kvp.Value);
            if (zoneId == null) continue;
            totals.TryGetValue(zoneId, out int cur);
            totals[zoneId] = cur + 1;
        }
        return totals;
    }

    /// <summary>
    /// 배치된 아이템의 GridSquare 목록을 받아 해당 셀에 흰색 플래시 후 점유 알파로 정착시킨다.
    /// </summary>
    public void TriggerPlacementEffect(IList<GridSquare> squares)
    {
        if (squares == null || squares.Count == 0) return;
        TriggerPlacementEffectAsync(squares, this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>GridSquare.isOccupied 를 읽어 셀 표시 상태를 갱신한다.
    /// HexGrid 자체 squares를 우선 참조 (BoardManager 전환 후 GridManager.grid가 다를 수 있음).</summary>
    public void RefreshOccupiedCells()
    {
        // HexGrid 직접 참조 우선 — GridManager.Instance?.grid가 다른 그리드를 가리킬 수 있어 fallback
        var gridSquares = HexGrid?.GetGridSquares()
                         ?? GridManager.Instance?.grid?.GetGridSquares();
        if (gridSquares == null) { RefreshPlacedCells(null); return; }

        var occupied = new HashSet<Vector2Int>();
        foreach (var sq in gridSquares)
            if (sq.isOccupied)
                occupied.Add(new Vector2Int(sq.col, sq.row));

        RefreshPlacedCells(occupied);
    }

    /// <summary>
    /// 지정 위치를 _occupiedPositions에서 직접 제거하고 시각·Bridge를 갱신한다.
    /// HandleItemRemoved 에서 캐시된 위치로 호출해 GridManager 의존 없이 즉시 반영.
    /// </summary>
    public void RemovePlacedCells(Vector2Int[] positions)
    {
        if (positions == null) return;
        foreach (var pos in positions)
        {
            _occupiedPositions.Remove(pos);
            if (_cellImages.TryGetValue(pos, out var img) && _cellBaseColors.TryGetValue(pos, out var bc))
                img.color = EmptyColor(bc);
        }
        var zoneCounts   = GetZoneOccupiedCounts();
        var clusterSizes = ZoneClusterCalculator.Compute(_occupiedPositions, _cellZones);
        MerlinRuneBridge.Instance?.OnZoneCellsUpdated(zoneCounts, clusterSizes);
    }

    /// <summary>현재 점유된 셀 좌표(col,row) 스냅샷. 세이브 캡처용.</summary>
    public List<Vector2Int> GetOccupiedCells() => new List<Vector2Int>(_occupiedPositions);

    /// <summary>
    /// 이어하기: 저장된 점유 셀을 그리드에 재주입한다.
    /// 그리드가 미빌드면 먼저 빌드하고, GridSquare 점유(재배치 차단)와 시각을 맞춘 뒤
    /// RefreshPlacedCells로 존 클러스터를 재계산해 Bridge(시너지)에 통보한다.
    /// 점유 셀이 시너지의 단일 진실원본이므로 이 호출만으로 빌드 효과가 복원된다.
    /// </summary>
    public void RestoreOccupiedCells(IReadOnlyList<Vector2Int> cells)
    {
        if (!_isBuilt) BuildGrid();

        var set = new HashSet<Vector2Int>();
        if (cells != null)
            foreach (var c in cells)
                if (_cellZones.ContainsKey(c)) set.Add(c);

        // GridSquare 점유 반영 — 이후 신규 룬 배치가 점유 칸을 침범하지 않게.
        var grid = HexGrid;
        var squares = grid != null ? grid.GetGridSquares() : null;
        if (squares != null)
            foreach (var sq in squares)
                sq.SetOccupied(set.Contains(new Vector2Int(sq.col, sq.row)));

        RefreshPlacedCells(set);        // 시각 + OnZoneCellsUpdated → 시너지 재계산
        UpdateAdjacencyConstraints();
    }

    /// <summary>모든 배치 셀을 초기화한다. DoReset 에서 호출.</summary>
    public void ClearAllPlacedCells()
    {
        foreach (var pos in _occupiedPositions)
            if (_cellImages.TryGetValue(pos, out var img) && _cellBaseColors.TryGetValue(pos, out var bc))
                img.color = EmptyColor(bc);
        _occupiedPositions.Clear();
        MerlinRuneBridge.Instance?.OnZoneCellsUpdated(
            new Dictionary<string, int>(),
            new Dictionary<string, int>());
    }

    // ── Private methods ──

    private void CreateCell(Vector2Int gridPos, float posX, float posY, char zoneCode)
    {
        var cellGO = new GameObject($"Cell_{gridPos.x}_{gridPos.y}", typeof(RectTransform));
        cellGO.transform.SetParent(transform, false);

        var rt = cellGO.GetComponent<RectTransform>();
        rt.sizeDelta        = new Vector2(CELL_SIZE, CELL_SIZE);
        rt.anchoredPosition = new Vector2(posX, posY);

        var img = cellGO.AddComponent<Image>();
        var zoneColor     = GetZoneColor(zoneCode);
        img.color         = EmptyColor(zoneColor);
        img.raycastTarget = false;

        // 약간의 둥글기를 위해 배경 위에 내부 하이라이트 추가
        var innerGO = new GameObject("Inner", typeof(RectTransform));
        innerGO.transform.SetParent(cellGO.transform, false);
        var innerRT = innerGO.GetComponent<RectTransform>();
        innerRT.anchorMin = new Vector2(0.05f, 0.05f);
        innerRT.anchorMax = new Vector2(0.95f, 0.95f);
        innerRT.sizeDelta = Vector2.zero;
        var innerImg = innerGO.AddComponent<Image>();
        innerImg.color         = new Color(1f, 1f, 1f, 0.08f);
        innerImg.raycastTarget = false;

        // 배치 플래시 오버레이 (투명 → 흰색 → 투명 애니메이션용)
        var flashGO = new GameObject("Flash", typeof(RectTransform));
        flashGO.transform.SetParent(cellGO.transform, false);
        var flashRT = flashGO.GetComponent<RectTransform>();
        flashRT.anchorMin = Vector2.zero;
        flashRT.anchorMax = Vector2.one;
        flashRT.sizeDelta = Vector2.zero;
        var flashImg = flashGO.AddComponent<Image>();
        flashImg.color         = new Color(1f, 1f, 1f, 0f);
        flashImg.raycastTarget = false;

        _cellObjects.Add(cellGO);
        _cellImages[gridPos]      = img;
        _cellFlashImages[gridPos] = flashImg;
        _cellBaseColors[gridPos]  = zoneColor;
        _cellZones[gridPos]       = zoneCode;
    }

    private void CreateGridSquare(Vector2Int gridPos, float posX, float posY,
                                   int hexRow, int col, List<GridSquare> squares)
    {
        var sqGO = new GameObject($"Sq_{col}_{hexRow}", typeof(RectTransform));
        sqGO.transform.SetParent(_gridSquaresRoot.transform, false);

        var sqRT = sqGO.GetComponent<RectTransform>();
        sqRT.sizeDelta        = new Vector2(CELL_SIZE, CELL_SIZE);
        sqRT.anchoredPosition = new Vector2(posX, posY);

        // 드래그 프리뷰 오버레이 이미지 (SetPreviewHighlight 가 색상을 입혀줌)
        var hoverImg = sqGO.AddComponent<Image>();
        hoverImg.color         = new Color(0f, 1f, 0f, 0f);
        hoverImg.enabled       = false;
        hoverImg.raycastTarget = false;

        var sq = sqGO.AddComponent<GridSquare>();
        sq.hoverImage = hoverImg;
        sq.Init(hexRow, col, true);   // 존맵의 모든 셀은 배치 가능

        // BoxCollider2D: ShapeBlock 트리거 충돌 감지용 (물리 호버 하이라이트)
        var col2d = sqGO.AddComponent<BoxCollider2D>();
        col2d.isTrigger = true;
        col2d.size      = new Vector2(CELL_SIZE * 0.8f, CELL_SIZE * 0.8f);

        squares.Add(sq);
    }

    private void ClearGrid()
    {
        _isBuilt = false;
        foreach (var go in _cellObjects)
            if (go != null) Destroy(go);
        _cellObjects.Clear();
        _cellImages.Clear();
        _cellFlashImages.Clear();
        _cellBaseColors.Clear();
        _occupiedPositions.Clear();
        _cellZones.Clear();

        if (_gridSquaresRoot != null)
        {
            Destroy(_gridSquaresRoot);
            _gridSquaresRoot = null;
        }

        if (_runtimeGridAsset != null)
        {
            Destroy(_runtimeGridAsset);
            _runtimeGridAsset = null;
        }

        if (_runtimeVisualSO != null)
        {
            Destroy(_runtimeVisualSO);
            _runtimeVisualSO = null;
        }
    }

    private static char ZoneIdToChar(string zoneId) => ElementDef.IdToCode(zoneId);

    private static string ZoneCharToId(char code) => ElementDef.CodeToId(code);

    private static Color GetZoneColor(char code) =>
        ElementDef.TryGetCodeColor(code, out var c) ? c : COLOR_EMPTY;

    // 점유: 존 색상을 1.45배 밝게 + 완전 불투명
    private static Color OccupiedColor(Color c) =>
        new(Mathf.Min(1f, c.r * 1.45f), Mathf.Min(1f, c.g * 1.45f), Mathf.Min(1f, c.b * 1.45f), HIGHLIGHT_ALPHA);

    // 비어 있음: 어둡고 반투명
    private static Color EmptyColor(Color c) =>
        new(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, 0.55f);

    // 드래그 호버: 존 색상 그대로, 기본 알파
    private static Color NormalColor(Color c) =>
        new(c.r, c.g, c.b, NORMAL_ALPHA);

    private async UniTaskVoid TriggerPlacementEffectAsync(IList<GridSquare> squares, CancellationToken ct)
    {
        var positions = new List<Vector2Int>(squares.Count);
        foreach (var sq in squares)
            if (sq != null) positions.Add(new Vector2Int(sq.col, sq.row));

        // 점유 업데이트 전 — 기존 점유 셀 중 새 블록과 인접한 셀 수집 (연결 연출 대상)
        var posSet       = new HashSet<Vector2Int>(positions);
        var connSet      = new HashSet<Vector2Int>();
        foreach (var pos in positions)
        {
            CollectConnectedNeighbor(pos + new Vector2Int(-1, 0), posSet, connSet);
            CollectConnectedNeighbor(pos + new Vector2Int( 1, 0), posSet, connSet);
            CollectConnectedNeighbor(pos + new Vector2Int( 0,-1), posSet, connSet);
            CollectConnectedNeighbor(pos + new Vector2Int( 0, 1), posSet, connSet);
        }
        var connNeighbors = new List<Vector2Int>(connSet);
        bool hasConn      = connNeighbors.Count > 0;

        // 점유 색상 즉시 적용 + _occupiedPositions 업데이트
        foreach (var pos in positions)
        {
            _occupiedPositions.Add(pos);
            if (_cellImages.TryGetValue(pos, out var img) && _cellBaseColors.TryGetValue(pos, out var bc))
                img.color = OccupiedColor(bc);
        }

        // Bridge에 클러스터 업데이트 알림 (시너지 패널 즉시 갱신)
        var zoneCounts   = GetZoneOccupiedCounts();
        var clusterSizes = ZoneClusterCalculator.Compute(_occupiedPositions, _cellZones);
        MerlinRuneBridge.Instance?.OnZoneCellsUpdated(zoneCounts, clusterSizes);

        // ── 애니메이션 ───────────────────────────────────────────────────
        // 신규 셀: 흰색 플래시  0→0.6(30%)→0 (420ms)
        // 연결 이웃: 시안 펄스  15% 지연 시작, 더 부드럽게 소멸
        const int   STEPS    = 20;
        const float TOTAL_MS = 480f;
        const float RAMP_T   = 0.28f;
        const float PEAK_A   = 0.65f;
        const float CONN_A   = 0.50f;
        const float CONN_DELAY = 0.12f; // 연결 펄스 시작 지연 비율

        for (int i = 0; i <= STEPS; i++)
        {
            float t = i / (float)STEPS;

            // 신규 셀: 흰색 플래시
            float a = t < RAMP_T
                ? (t / RAMP_T) * PEAK_A
                : (1f - (t - RAMP_T) / (1f - RAMP_T)) * PEAK_A;
            foreach (var pos in positions)
                if (_cellFlashImages.TryGetValue(pos, out var flash))
                    flash.color = new Color(1f, 1f, 1f, a);

            // 연결 이웃: 시안 펄스 (약간 지연)
            if (hasConn)
            {
                float ct2 = Mathf.Max(0f, (t - CONN_DELAY) / (1f - CONN_DELAY));
                float ca  = ct2 < RAMP_T
                    ? (ct2 / RAMP_T) * CONN_A
                    : (1f - (ct2 - RAMP_T) / (1f - RAMP_T)) * CONN_A;
                var connColor = new Color(0.35f, 0.90f, 1.00f, ca);
                foreach (var pos in connNeighbors)
                    if (_cellFlashImages.TryGetValue(pos, out var flash))
                        flash.color = connColor;
            }

            try { await UniTask.Delay((int)(TOTAL_MS / STEPS), ignoreTimeScale: true, cancellationToken: ct); }
            catch (System.OperationCanceledException) { return; }
        }

        foreach (var pos in positions)
            if (_cellFlashImages.TryGetValue(pos, out var flash))
                flash.color = new Color(1f, 1f, 1f, 0f);
        if (hasConn)
            foreach (var pos in connNeighbors)
                if (_cellFlashImages.TryGetValue(pos, out var flash))
                    flash.color = new Color(0.35f, 0.90f, 1.00f, 0f);
    }

    private void CollectConnectedNeighbor(Vector2Int n,
        HashSet<Vector2Int> newPosSet, HashSet<Vector2Int> result)
    {
        if (!newPosSet.Contains(n) && _occupiedPositions.Contains(n))
            result.Add(n);
    }

    private void OnDestroy() => ClearGrid();
}
