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
    // 판은 13열 × 12행이고 CenterPanel(화면 20%~75% = 1052×1010)에 그려진다.
    // 셀 50일 때 판이 698×648에 그쳐 중앙의 절반 이상이 빈 채로 남았다 —
    // 화면에서 가장 중요한 요소가 가장 작게 보이는 상태였다.
    // 폭 (13-1)*STEP+CELL ≤ 1028, 높이 12*STEP ≤ 986을 만족하는 최대치가 72다(→ 1008×936).
    // Shape는 GridManager.GetGap()=CELL_STEP, 시각 크기는 squareVisualSize=CELL_SIZE를
    // 따라오므로 이 두 상수만 바꾸면 드래그 조각까지 함께 커진다.
    private const float CELL_SIZE = 72f;
    private const float CELL_GAP  = 6f;
    private const float CELL_STEP = CELL_SIZE + CELL_GAP;   // 78f — GridManager.GetGap() 기준값

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
    private readonly Dictionary<Vector2Int, Image> _cellBorderImages  = new();   // 점유 셀 상시 테두리
    private readonly Dictionary<Vector2Int, Color>  _cellBaseColors    = new();
    private readonly HashSet<Vector2Int>             _occupiedPositions = new();
    private readonly Dictionary<Vector2Int, char>    _cellZones         = new();

    // 배치할 룬의 속성 힌트(매칭 존 강조용). '\0' = 힌트 없음.
    private char _hintElementCode = '\0';

    /// <summary>한 존에 존핵이 겹쳤을 때 허용하는 최대 합산 증폭(%). 폭주 방지 상한.</summary>
    private const float MaxZoneAmpBonus = 60f;

    // 드래그 배치용 GridSquare 레이어
    private GameObject    _gridSquaresRoot;
    private GridAssetSO   _runtimeGridAsset;
    private GridVisualSO  _runtimeVisualSO;
    private bool          _isBuilt;

    // 존별 그리드 타일(디자이너 제공, 6속성 + 선택 중앙). 있으면 셀에 타일을 찍고 색 대신 명암만 틴트.
    private Dictionary<char, Sprite> _tileByCode;

    // ── Public API ──

    /// <summary>존별 셀 타일 스프라이트 주입(디자이너 6속성 순서 F·I·T·P·L·D + 선택 중앙). BuildGrid 전에 호출.</summary>
    public void SetZoneTiles(IReadOnlyList<Sprite> byElementOrder, Sprite centerTile)
    {
        _tileByCode = new Dictionary<char, Sprite>();
        var order = ElementDef.Order;   // F, I, T, P, L, D 순
        if (byElementOrder != null)
            for (int i = 0; i < order.Count && i < byElementOrder.Count; i++)
            {
                if (byElementOrder[i] == null) continue;
                _tileByCode[ElementDef.IdToCode(order[i])] = byElementOrder[i];
            }
        if (centerTile != null) _tileByCode[ElementDef.CenterCode] = centerTile;
    }

    private Sprite TileForCode(char code)
        => _tileByCode != null && _tileByCode.TryGetValue(code, out var s) ? s : null;

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
        // 배치 블록도 시각 셀(50)과 같은 크기로 그려지도록 알려준다 — 스텝(54)으로 그리면
        // 블록이 존 타일 경계를 양옆 2px씩 잠식해 존 구분선이 끊겨 보였다.
        _runtimeVisualSO.squareVisualSize = CELL_SIZE;
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
                CreateGridSquare(pos, posX, posY, row.hex_row, absCol, code, squares);
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

        // 칸 색은 RefreshPlaceableTint 한 곳에서만 칠한다.
        // 예전엔 여기서 빈 칸을 전부 EmptyColor로 덮었는데, 이 함수가 모든 흐름의 <b>맨 끝</b>에서
        // (RefreshSynergyStatus → RefreshOccupiedCells 경유) 불리는 바람에 방금 세운 배치 힌트가
        // 매번 지워졌다 — 룬을 하나 놓고 나면 "다음 룬을 어디 놓을 수 있는지"가 화면에서 사라지던 원인.
        RefreshPlaceableTint(BuildPlaceableSet());

        NotifyBridge();
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

        // 판정 규칙은 BuildPlaceableSet 하나로 통일(빈 판=전체 개방 / 점유 있으면 인접 도달 범위).
        var placeable = BuildPlaceableSet();

        foreach (var sq in squares)
        {
            if (sq.isOccupied) continue;
            sq.isPlaceable = placeable.Contains(new Vector2Int(sq.col, sq.row));
        }

        // 시각 갱신 — "여기 놓을 수 있다"를 직접 표기(빈 어두움 vs 배치 가능 밝음). 배치/제거/패널 오픈 시 반영.
        RefreshPlaceableTint(placeable);
    }

    /// <summary>
    /// 배치 가능 셀을 밝게 틴트해 배치 위치를 안내한다. 점유=밝음, 배치가능=중간, 그 외=어두움.
    /// 배치할 룬의 속성 힌트(_hintElementCode)가 있으면 <b>그 룬이 실제로 놓일 수 있는 칸</b>
    /// (매칭 속성 존 + 중앙)만 밝게 남긴다. 속성 제약(<see cref="RuneZoneRule"/>)이 배치를 거부하는 칸을
    /// "놓을 수 있어 보이게" 칠하면 안 되므로, 비매칭 칸은 빈 칸과 같은 명암으로 내린다.
    /// </summary>
    private void RefreshPlaceableTint(HashSet<Vector2Int> placeable)
    {
        bool   hintOn    = _hintElementCode != '\0';
        string hintZone  = hintOn ? ElementDef.CodeToId(_hintElementCode) : null;

        foreach (var kvp in _cellImages)
        {
            if (!_cellBaseColors.TryGetValue(kvp.Key, out var bc)) continue;

            if (_occupiedPositions.Contains(kvp.Key)) { kvp.Value.color = OccupiedColor(bc); continue; }

            _cellZones.TryGetValue(kvp.Key, out var zc);
            bool canPlace = placeable.Contains(kvp.Key) && RuneZoneRule.Accepts(zc, hintZone);
            bool match    = hintOn && zc == _hintElementCode;

            if (canPlace && match)      kvp.Value.color = MatchHighlightColor(bc);   // 매칭 속성칸 — 강조
            else if (canPlace)          kvp.Value.color = hintOn ? DimPlaceable(bc)   // 중앙(중립 허브) — 한 단계 낮춤
                                                                 : PlaceableColor(bc);
            else                        kvp.Value.color = EmptyColor(bc);
        }

        RefreshOccupiedBorders();
    }

    /// <summary>점유 셀에 상시 금테를 켜고, 빈 셀은 끈다. 색 갱신과 항상 같이 돈다.</summary>
    private void RefreshOccupiedBorders()
    {
        foreach (var kvp in _cellBorderImages)
        {
            bool occupied = _occupiedPositions.Contains(kvp.Key);
            var frame = kvp.Value;
            if (frame == null) continue;

            frame.color = occupied ? new Color(1f, 0.86f, 0.42f, 0.95f) : Color.clear;
            var hole = frame.transform.childCount > 0 ? frame.transform.GetChild(0).gameObject : null;
            if (hole != null && hole.activeSelf != occupied) hole.SetActive(occupied);
        }
    }

    /// <summary>배치할 룬의 속성 힌트를 설정하고 판을 갱신한다. null/빈값이면 힌트 해제.</summary>
    public void SetPlacementElementHint(string elementId)
    {
        _hintElementCode = string.IsNullOrEmpty(elementId) ? '\0' : ElementDef.IdToCode(elementId);
        UpdateAdjacencyConstraints();
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

    /// <summary>
    /// 주어진 모양(셀 오프셋)을 판 어딘가에 놓을 수 있는지 조회한다. <b>읽기 전용 · 부작용 없음.</b>
    /// <para>
    /// GridSquare가 아니라 <see cref="_occupiedPositions"/>·<see cref="_cellZones"/>만 보므로
    /// 패널이 SetActive(false)여도 동작한다(선택 팝업이 배치 화면 밖에서 판정해야 하기 때문).
    /// 판정 규칙은 <see cref="UpdateAdjacencyConstraints"/> + GridManager.TryPlaceShape과 동일하다:
    /// 모든 셀이 판 위에 있고, 비어 있고, (판에 뭔가 있다면) 전부 인접 도달 범위 안이어야 한다.
    /// </para>
    /// 회전은 미지원이므로 주어진 방향 그대로만 검사한다.
    /// <paramref name="elementId"/>를 주면 속성 배치 제약(<see cref="RuneZoneRule"/>)까지 함께 본다 —
    /// 이게 없으면 "놓을 자리 있음"으로 표시된 룬이 막상 판에서는 어디에도 안 들어간다.
    /// </summary>
    public bool CanPlaceAnywhere(IReadOnlyList<Vector2Int> offsets, string elementId = null, bool isLegendary = false)
    {
        if (offsets == null || offsets.Count == 0) return false;
        // 판이 아직 빌드된 적 없으면(첫 룬 획득 등) 판정 불가 → 막지 않는다(permissive).
        // 배치 화면을 한 번도 연 적 없을 때 모든 카드가 '놓을 자리 없음'으로 뜨던 첫 사용 버그 방지.
        if (_cellZones.Count == 0) return true;

        var placeable = BuildPlaceableSet();
        if (placeable.Count == 0) return false;

        foreach (var anchor in _cellZones.Keys)
        {
            bool fits = true;
            for (int i = 0; i < offsets.Count; i++)
            {
                var cell = anchor + offsets[i];
                if (!placeable.Contains(cell)) { fits = false; break; }
                if (!_cellZones.TryGetValue(cell, out var zc) || !RuneZoneRule.Accepts(zc, elementId, isLegendary))
                { fits = false; break; }
            }
            if (fits) return true;
        }
        return false;
    }

    /// <summary>배치 가능한(판 위 · 비어 있음 · 인접 조건 충족) 셀 집합. UpdateAdjacencyConstraints의 순수 함수 버전.</summary>
    private HashSet<Vector2Int> BuildPlaceableSet()
    {
        var result = new HashSet<Vector2Int>();

        // 인접 제약은 <b>존 안에서만</b> 본다.
        //
        // 예전엔 판 전체를 하나로 보고 "이미 놓인 칸에서 5칸 이내"만 열었다. 그런데 룬은 자기 속성
        // 존에만 놓이므로(RuneZoneRule), 첫 룬을 얼음 존에 놓으면 5칸 밖에 있는 불 존은 영원히
        // 닿지 못한다 — 두 번째 룬부터 놓을 자리가 아예 사라지고 판도 아무 데도 빛나지 않았다.
        // 존을 건너뛰는 인접은 애초에 의미가 없으므로, 각 존을 독립된 판처럼 다룬다:
        // 비어 있는 존은 전체 개방, 이미 룬이 있는 존은 그 룬에서 뻗어 나가게.
        var zones = new HashSet<char>();
        foreach (var z in _cellZones.Values) zones.Add(z);

        var frontier = new Queue<(Vector2Int pos, int depth)>();

        foreach (var zone in zones)
        {
            frontier.Clear();
            bool zoneHasRune = false;

            foreach (var occ in _occupiedPositions)
            {
                if (!_cellZones.TryGetValue(occ, out var zc) || zc != zone) continue;
                zoneHasRune = true;
                frontier.Enqueue((occ, 0));
            }

            if (!zoneHasRune)
            {
                // 빈 존 — 어디서든 시작할 수 있다.
                foreach (var kv in _cellZones)
                    if (kv.Value == zone && !_occupiedPositions.Contains(kv.Key)) result.Add(kv.Key);
                continue;
            }

            while (frontier.Count > 0)
            {
                var (pos, depth) = frontier.Dequeue();
                if (depth >= ADJACENCY_REACH) continue;

                TryExpandInZone(pos + new Vector2Int(-1, 0), zone, result, frontier, depth);
                TryExpandInZone(pos + new Vector2Int( 1, 0), zone, result, frontier, depth);
                TryExpandInZone(pos + new Vector2Int( 0,-1), zone, result, frontier, depth);
                TryExpandInZone(pos + new Vector2Int( 0, 1), zone, result, frontier, depth);
            }
        }

        return result;   // TryExpandInZone이 점유 셀·판 밖·타존을 이미 배제한다
    }

    /// <summary>같은 존 안의 빈 칸으로만 인접 확장한다.</summary>
    private void TryExpandInZone(Vector2Int n, char zone,
        HashSet<Vector2Int> reachable,
        Queue<(Vector2Int, int)> frontier,
        int depth)
    {
        if (_occupiedPositions.Contains(n)) return;
        if (!_cellZones.TryGetValue(n, out var zc) || zc != zone) return;
        if (reachable.Add(n))
            frontier.Enqueue((n, depth + 1));
    }

    /// <summary>
    /// 판 상태를 브릿지에 통보한다 — 존별 점유 수(시너지 단계) + 존핵 배수(정제소 증폭)를 함께 보낸다.
    /// 배치/제거/복원 등 점유가 바뀌는 모든 지점에서 이 하나만 호출하면 둘이 어긋나지 않는다.
    /// </summary>
    private void NotifyBridge()
    {
        var bridge = MerlinRuneBridge.Instance;
        if (bridge == null) return;
        bridge.OnZoneCellsUpdated(GetZoneOccupiedCounts());
        bridge.OnZoneAmplifiersUpdated(GetZoneAmplifiers());
    }

    /// <summary>
    /// [정제소 존핵] 판에 놓인 존핵이 만드는 <b>존별 배수</b>를 계산한다(key=zone_id, 1.3 = +30%).
    /// 존핵은 <b>자기 속성과 같은 존</b>에 놓였을 때만 발동한다(안 맞는 존에 놓으면 무효).
    /// 같은 존에 여러 개면 합연산하되 상한(<see cref="MaxZoneAmpBonus"/>)으로 폭주를 막는다.
    /// </summary>
    public Dictionary<string, float> GetZoneAmplifiers()
    {
        var result = new Dictionary<string, float>();
        var squares = HexGrid?.GetGridSquares();
        if (squares == null) return result;

        // 한 룬이 여러 칸을 점유하므로 instanceId로 1회만 집계
        var counted = new HashSet<string>();

        foreach (var sq in squares)
        {
            if (sq == null || !sq.isOccupied) continue;
            var item = sq.occupyingItem;
            if (item == null || string.IsNullOrEmpty(item.element)) continue;
            if (!string.IsNullOrEmpty(item.instanceId) && !counted.Add(item.instanceId)) continue;

            float amp = AmplifyPercentOf(item);
            if (amp <= 0f) continue;

            // 이 룬이 놓인 칸의 존이 자기 속성과 같아야 발동
            var pos = new Vector2Int(sq.col, sq.row);
            if (!_cellZones.TryGetValue(pos, out var code)) continue;
            string zoneId = ZoneCharToId(code);
            if (zoneId == null || zoneId != item.element) continue;

            result.TryGetValue(zoneId, out float cur);
            result[zoneId] = cur + amp;
        }

        // 퍼센트 합 → 배수로 변환(상한 적용)
        var keys = new List<string>(result.Keys);
        foreach (var k in keys)
            result[k] = 1f + Mathf.Min(MaxZoneAmpBonus, result[k]) / 100f;

        return result;
    }

    /// <summary>존핵 효과(AmplifyZone)의 퍼센트. 존핵이 아니면 0.</summary>
    private static float AmplifyPercentOf(RuntimeItemData item)
    {
        if (item?.effects == null) return 0f;
        float sum = 0f;
        foreach (var slot in item.effects)
            if (slot != null && slot.effectType == "AmplifyZone") sum += slot.value;
        return sum;
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

        var removed = new List<Vector2Int>(positions.Length);
        foreach (var pos in positions)
        {
            _occupiedPositions.Remove(pos);
            if (_cellImages.TryGetValue(pos, out var img) && _cellBaseColors.TryGetValue(pos, out var bc))
                img.color = EmptyColor(bc);
            // 금테는 점유 해제와 동시에 즉시 끈다(RefreshOccupiedBorders를 기다리지 않게).
            if (_cellBorderImages.TryGetValue(pos, out var frame) && frame != null)
            {
                frame.color = Color.clear;
                if (frame.transform.childCount > 0) frame.transform.GetChild(0).gameObject.SetActive(false);
            }
            removed.Add(pos);
        }

        NotifyBridge();
        TriggerRemovalEffectAsync(removed, this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>
    /// 룬을 뺀 칸에 <b>회수 연출</b>을 준다 — 배치(흰색 번쩍임)의 반대로, 붉은 기운이
    /// 짧게 번졌다가 사그라든다. 배치엔 있고 제거엔 없어 "그냥 사라진다"로 보이던 것을 보완.
    /// </summary>
    private async UniTaskVoid TriggerRemovalEffectAsync(List<Vector2Int> positions, CancellationToken ct)
    {
        const int   STEPS    = 14;
        const float TOTAL_MS = 300f;
        const float PEAK_A   = 0.55f;
        var tone = new Color(1f, 0.42f, 0.34f, 0f);   // 빠져나가는 붉은 기운

        for (int i = 0; i <= STEPS; i++)
        {
            float t = i / (float)STEPS;
            float a = (1f - t) * PEAK_A;               // 처음이 가장 진하고 서서히 사라진다
            tone.a = a;
            foreach (var pos in positions)
                if (_cellFlashImages.TryGetValue(pos, out var flash))
                    flash.color = tone;

            try { await UniTask.Delay((int)(TOTAL_MS / STEPS), ignoreTimeScale: true, cancellationToken: ct); }
            catch (System.OperationCanceledException) { return; }
        }

        foreach (var pos in positions)
            if (_cellFlashImages.TryGetValue(pos, out var flash))
                flash.color = new Color(1f, 1f, 1f, 0f);
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

    /// <summary>
    /// 런 하드리셋 — 판의 점유를 <b>칸까지</b> 전부 비운다(런 종료 시 MerlinRuneBridge.ClearBoard에서 호출).
    ///
    /// 이 뷰는 Puzzle 인스턴스가 아니라 <b>UI_GridPanel(DDOL)</b> 소속이라, 판째로 버리는
    /// ClearBoard의 사정권 밖이었다. 그래서 지난 런의 점유(칸·금테·존 카운트)가 남아
    /// 다음 런에서 룬 없이 시너지가 붙고 그 칸에 새 룬을 놓을 수 없었다.
    /// </summary>
    public void ResetBoardOccupancy()
    {
        var squares = HexGrid?.GetGridSquares();
        if (squares != null)
            foreach (var sq in squares)
                if (sq != null) sq.SetOccupied(false);

        RefreshPlacedCells(null);        // _occupiedPositions 비움 + 색/금테 + 브릿지 통보(빈 카운트)
        UpdateAdjacencyConstraints();
    }

    /// <summary>모든 배치 셀을 초기화한다. DoReset 에서 호출.</summary>
    public void ClearAllPlacedCells()
    {
        foreach (var pos in _occupiedPositions)
            if (_cellImages.TryGetValue(pos, out var img) && _cellBaseColors.TryGetValue(pos, out var bc))
                img.color = EmptyColor(bc);
        _occupiedPositions.Clear();
        MerlinRuneBridge.Instance?.OnZoneCellsUpdated(new Dictionary<string, int>());
        MerlinRuneBridge.Instance?.OnZoneAmplifiersUpdated(null);
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
        var tile          = TileForCode(zoneCode);
        // 타일이 있으면 색은 타일이 소유 → 베이스를 흰색으로 두고 명암(empty/occupied)만 틴트.
        var baseColor     = tile != null ? Color.white : zoneColor;
        if (tile != null) img.sprite = tile;
        img.color         = EmptyColor(baseColor);
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

        // 점유 셀 상시 테두리 — 룬이 놓인 칸을 계속 밝은 윤곽으로 감싼다.
        // "어느 칸이 채워졌나"가 색 명암 차이만으로는 안 읽혀서, 금테로 명시한다.
        // 안쪽을 비워 <b>프레임(외곽 링)</b>만 보이게 만든다: 바깥 테두리 Image 위에
        // 칸 색과 같은 안쪽 마스크를 덮어 가운데를 뚫는다.
        var borderGO = new GameObject("Border", typeof(RectTransform));
        borderGO.transform.SetParent(cellGO.transform, false);
        var borderRT = borderGO.GetComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero;
        borderRT.anchorMax = Vector2.one;
        borderRT.sizeDelta = Vector2.zero;
        var borderImg = borderGO.AddComponent<Image>();
        borderImg.color         = Color.clear;   // 평소 숨김 → 점유 시 금색으로
        borderImg.raycastTarget = false;

        var borderHoleGO = new GameObject("Hole", typeof(RectTransform));
        borderHoleGO.transform.SetParent(borderGO.transform, false);
        var holeRT = borderHoleGO.GetComponent<RectTransform>();
        holeRT.anchorMin = Vector2.zero;
        holeRT.anchorMax = Vector2.one;
        holeRT.offsetMin = new Vector2(2f, 2f);      // 2px 링만 남기고 안쪽을 판 색으로 덮는다
        holeRT.offsetMax = new Vector2(-2f, -2f);
        var holeImg = borderHoleGO.AddComponent<Image>();
        holeImg.sprite        = tile;                // 칸과 같은 타일/색 → 프레임만 노출
        holeImg.color         = OccupiedColor(baseColor);
        holeImg.raycastTarget = false;
        borderHoleGO.SetActive(false);               // 테두리와 함께 켜고 끈다

        _cellBorderImages[gridPos] = borderImg;

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
        _cellBaseColors[gridPos]  = baseColor;
        _cellZones[gridPos]       = zoneCode;
    }

    private void CreateGridSquare(Vector2Int gridPos, float posX, float posY,
                                   int hexRow, int col, char zoneCode, List<GridSquare> squares)
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

        // 클릭 판 — 칸을 눌러 배치하려면 <b>레이캐스트를 받는 그래픽</b>이 필요하다.
        // hoverImg는 평소 enabled=false + raycastTarget=false라 포인터가 아예 닿지 않았다.
        // Graphic은 한 오브젝트에 하나만 붙으므로(DisallowMultipleComponent) 자식으로 깐다.
        // 완전 투명이어도 유니티 UI는 레이캐스트를 받는다(알파 임계값 미설정 시).
        var clickGO = new GameObject("ClickArea", typeof(RectTransform));
        clickGO.transform.SetParent(sqGO.transform, false);
        var clickRT = clickGO.GetComponent<RectTransform>();
        clickRT.anchorMin = Vector2.zero;
        clickRT.anchorMax = Vector2.one;
        clickRT.offsetMin = clickRT.offsetMax = Vector2.zero;
        var clickImg = clickGO.AddComponent<Image>();
        clickImg.color         = Color.clear;
        clickImg.raycastTarget = true;

        var sq = sqGO.AddComponent<GridSquare>();
        sq.hoverImage = hoverImg;
        sq.zoneCode   = zoneCode;     // 속성 배치 제약(RuneZoneRule) 판정 근거
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
        _cellBorderImages.Clear();
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

    // 배치 가능(빈 셀): 어두움(Empty)과 점유(Occupied) 사이 — "여기 놓을 수 있다"를 밝기로 안내
    private static Color PlaceableColor(Color c) =>
        new(Mathf.Min(1f, c.r * 1.05f), Mathf.Min(1f, c.g * 1.05f), Mathf.Min(1f, c.b * 1.05f), 0.85f);

    // 매칭 속성칸 강조: 존 색을 강하게 + 높은 알파(배치 유도 하이라이트)
    private static Color MatchHighlightColor(Color c) =>
        new(Mathf.Min(1f, c.r * 1.6f), Mathf.Min(1f, c.g * 1.6f), Mathf.Min(1f, c.b * 1.6f), 1f);

    // 힌트 중 비매칭 배치가능 셀: 매칭칸이 도드라지도록 한 단계 낮춘 밝기
    private static Color DimPlaceable(Color c) =>
        new(c.r * 0.7f, c.g * 0.7f, c.b * 0.7f, 0.6f);

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
        NotifyBridge();

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

    // ── 시너지 완성 연출 ──────────────────────────────────────────────

    /// <summary>시너지 단계 달성 시 UI_GridPanel이 브릿지 이벤트를 받아 호출. 해당 속성 존을 터뜨린다.</summary>
    public void PlayZoneSynergyBurst(string zoneId)
    {
        char code = ElementDef.IdToCode(zoneId);
        if (code == '\0') return;
        PlaySynergyBurstAsync(code, this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>
    /// 시너지 단계 달성 시 해당 속성 존 전체를 속성색으로 <b>파도치듯 터뜨린다</b>.
    /// 존 중심에서 바깥으로 퍼지는 웨이브(셀별 거리 지연) + 밝은 섬광 후 정착. 배치 플래시와 겹쳐도 안전(플래시 오버레이 공유).
    /// </summary>
    private async UniTaskVoid PlaySynergyBurstAsync(char zoneCode, CancellationToken ct)
    {
        // 대상 셀 수집 + 존 중심 계산(웨이브 기준점)
        var cells = new List<Vector2Int>();
        Vector2 centroid = Vector2.zero;
        foreach (var kvp in _cellZones)
            if (kvp.Value == zoneCode) { cells.Add(kvp.Key); centroid += kvp.Key; }
        if (cells.Count == 0) return;
        centroid /= cells.Count;

        Color elem = ElementDef.TryGetCodeColor(zoneCode, out var ec) ? ec : Color.white;
        Color burst = new Color(Mathf.Min(1f, elem.r * 1.4f + 0.2f),
                                Mathf.Min(1f, elem.g * 1.4f + 0.2f),
                                Mathf.Min(1f, elem.b * 1.4f + 0.2f), 1f);

        // 셀별 웨이브 시작 지연(중심에서의 거리 비례)
        float maxDist = 0.01f;
        var delay = new Dictionary<Vector2Int, float>(cells.Count);
        foreach (var p in cells)
        {
            float d = Vector2.Distance(p, centroid);
            delay[p] = d;
            if (d > maxDist) maxDist = d;
        }

        const int   STEPS     = 26;
        const float TOTAL_MS  = 620f;
        const float WAVE_SPAN = 0.45f;  // 웨이브가 판을 훑는 데 쓰는 전체 진행 비율
        const float RISE      = 0.25f;  // 각 셀 섬광 상승 구간
        const float PEAK      = 0.95f;

        try
        {
            for (int i = 0; i <= STEPS; i++)
            {
                float t = i / (float)STEPS;
                foreach (var p in cells)
                {
                    if (!_cellFlashImages.TryGetValue(p, out var flash)) continue;
                    float start = (delay[p] / maxDist) * WAVE_SPAN;   // 이 셀의 섬광 시작 시점
                    float lt = (t - start) / (1f - WAVE_SPAN);
                    float a = lt <= 0f || lt >= 1f ? 0f
                            : (lt < RISE ? lt / RISE : 1f - (lt - RISE) / (1f - RISE)) * PEAK;
                    flash.color = new Color(burst.r, burst.g, burst.b, a);
                }
                await UniTask.Delay((int)(TOTAL_MS / STEPS), ignoreTimeScale: true, cancellationToken: ct);
            }
        }
        catch (System.OperationCanceledException) { }

        foreach (var p in cells)
            if (_cellFlashImages.TryGetValue(p, out var flash))
                flash.color = new Color(1f, 1f, 1f, 0f);
    }
}
