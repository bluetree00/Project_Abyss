using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 존 클리어 → 다음 존 선택 → 지연 스폰 흐름을 조율하는 서비스.
/// GameRunSession이 소유. startWithZoneLayout 모드에서만 사용된다.
/// </summary>
public class ZoneProgressionService
{
    private readonly string _layoutKey;
    private readonly float  _blockCellSize;
    private readonly HashSet<int> _spawnedZones = new();
    private readonly HashSet<int> _clearedZones = new();
    private readonly Dictionary<int, Vector3>     _spawnedWorldCenters = new();
    private readonly Dictionary<(int from, int to), StartRoomGate> _exitGates          = new();
    private readonly HashSet<int>                                  _gateActivatedZones = new();

    /// <summary>플레이어가 현재 위치한 존의 인덱스. 다음 존 선택 완료 시 갱신된다.</summary>
    public int CurrentZoneIndex { get; private set; }

    public IReadOnlyCollection<int> ClearedZones => _clearedZones;

    public ZoneProgressionService(string layoutKey, Vector3 zone0WorldCenter, float blockCellSize)
    {
        _layoutKey     = layoutKey;
        _blockCellSize = blockCellSize;
        _spawnedZones.Add(0);
        _spawnedWorldCenters[0] = zone0WorldCenter;
        CurrentZoneIndex = 0;
    }

    public bool IsSpawned(int zoneIndex) => _spawnedZones.Contains(zoneIndex);
    public bool IsCleared(int zoneIndex) => _clearedZones.Contains(zoneIndex);

    /// <summary>
    /// 이어하기 복원 시 호출. 클리어된 존들과 현재 존 인덱스를 내부 상태에 반영한다.
    /// 클리어된 존은 _spawnedZones·_clearedZones에 추가되고 CurrentZoneIndex가 갱신된다.
    /// </summary>
    public void RestoreState(int currentZoneIndex, System.Collections.Generic.IEnumerable<int> clearedZoneIndices)
    {
        CurrentZoneIndex = currentZoneIndex;
        if (clearedZoneIndices != null)
        {
            foreach (var idx in clearedZoneIndices)
            {
                _spawnedZones.Add(idx);
                _clearedZones.Add(idx);
            }
        }
        _spawnedZones.Add(currentZoneIndex);
    }

    /// <summary>이어하기 또는 외부 스폰 시 해당 존의 월드 중심을 수동 등록한다.</summary>
    public void RegisterSpawnedZone(int zoneIndex, Vector3 worldCenter)
    {
        _spawnedZones.Add(zoneIndex);
        _spawnedWorldCenters[zoneIndex] = worldCenter;
    }

    /// <summary>CreateZoneExitGates에서 생성된 StartRoomGate를 등록한다. (fromZone → toZone 쌍으로 저장)</summary>
    public void RegisterExitGate(int fromZoneIndex, int toZoneIndex, StartRoomGate gate)
        => _exitGates[(fromZoneIndex, toZoneIndex)] = gate;

    /// <summary>
    /// 해당 존의 StartRoomGate(일반 방 모드)를 활성화한다.
    /// 전투 존: ClearRewardTrigger 보상 완료 후 호출.
    /// 비전투 존: ZoneEntryTrigger 진입 시 즉시 호출.
    /// 게이트 없으면 선택 UI를 직접 표시하는 폴백으로 동작한다.
    /// </summary>
    public void EnableExitGateForZone(int zoneIndex)
    {
        // 이미 클리어된 방은 다시 연결 이벤트를 활성화하지 않음
        if (_gateActivatedZones.Contains(zoneIndex)) return;
        _gateActivatedZones.Add(zoneIndex);

        // 현재 존의 게이트 활성화
        bool any = false;
        foreach (var kvp in _exitGates)
            if (kvp.Key.from == zoneIndex && kvp.Value != null)
            { kvp.Value.EnableGate(); any = true; }

        if (!any)
        {
            Debug.LogWarning($"[ZoneProgression] Zone {zoneIndex} ExitGate 없음 — 직접 선택 UI 폴백");
            ShowZoneSelectionFallbackAsync(zoneIndex).Forget();
        }

        // 다른 클리어된 방들에서 이미 스폰된 존으로 가는 게이트도 함께 활성화
        // (다른 경로로 목적 존이 먼저 스폰된 경우를 대응)
        foreach (var kvp in _exitGates)
        {
            if (!_gateActivatedZones.Contains(kvp.Key.from)) continue;
            if (!_spawnedZones.Contains(kvp.Key.to)) continue;
            kvp.Value?.EnableGate();
        }
    }

    private async UniTaskVoid ShowZoneSelectionFallbackAsync(int zoneIndex)
    {
        try { await ShowZoneSelectionAsync(zoneIndex, System.Threading.CancellationToken.None); }
        catch (System.OperationCanceledException) { }
    }

    /// <summary>
    /// 게이트를 통해 직접 선택된 존으로 이동. StartRoomGate(일반 방 모드)가 호출.
    /// 선택 UI 없이 fromZone 클리어 → toZone 스폰 → CurrentZoneIndex 갱신.
    /// </summary>
    public async UniTask DirectlyEnterZoneAsync(int fromZoneIndex, int toZoneIndex, CancellationToken ct)
    {
        _clearedZones.Add(fromZoneIndex);
        await SpawnZoneIfNeededAsync(fromZoneIndex, toZoneIndex, ct);
        CurrentZoneIndex = toZoneIndex;
        Debug.Log($"[ZoneProgression] 게이트 진입 — CurrentZoneIndex → {CurrentZoneIndex}");
    }

    /// <summary>
    /// clearedZoneIndex를 클리어 처리하고, 인접 존 선택 UI를 표시한 뒤 선택된 존을 스폰한다.
    /// 선택지가 없으면 -1을 반환한다 (던전 종료).
    /// </summary>
    public async UniTask<int> ShowZoneSelectionAsync(int clearedZoneIndex, CancellationToken ct)
    {
        _clearedZones.Add(clearedZoneIndex);

        var options = GetNextZoneOptions(clearedZoneIndex);
        if (options.Count == 0)
        {
            Debug.Log($"[ZoneProgression] Zone {clearedZoneIndex} — 다음 존 없음 (던전 종료)");
            return -1;
        }

        int selectedIndex = await ShowSelectionUIAsync(options, ct);
        if (selectedIndex < 0) return -1;

        await SpawnZoneIfNeededAsync(clearedZoneIndex, selectedIndex, ct);
        CurrentZoneIndex = selectedIndex;
        Debug.Log($"[ZoneProgression] CurrentZoneIndex → {CurrentZoneIndex}");
        return selectedIndex;
    }

    // ── Private ──────────────────────────────────────────────────────────

    private List<ZoneLayoutEntry> GetNextZoneOptions(int fromZoneIndex)
    {
        var zones = Managers.ZoneLayout.GetZones(_layoutKey);
        var fromZone = zones?.Find(z => z.zone_index == fromZoneIndex);
        if (fromZone == null || string.IsNullOrEmpty(fromZone.next_zone_indices))
            return new List<ZoneLayoutEntry>();

        var result = new List<ZoneLayoutEntry>();
        foreach (var part in fromZone.next_zone_indices.Split('|'))
        {
            if (!int.TryParse(part.Trim(), out int idx)) continue;
            var zone = zones.Find(z => z.zone_index == idx);
            if (zone != null && !_clearedZones.Contains(idx))
                result.Add(zone);
        }
        return result;
    }

    private async UniTask<int> ShowSelectionUIAsync(List<ZoneLayoutEntry> options, CancellationToken ct)
    {
        var tcs = new UniTaskCompletionSource<int>();
        var canvasGO = BuildSelectionCanvas(options, tcs);

        int result;
        try
        {
            result = await tcs.Task.AttachExternalCancellation(ct);
        }
        catch (System.OperationCanceledException)
        {
            result = -1;
        }
        finally
        {
            if (canvasGO != null)
                Object.Destroy(canvasGO);
        }
        return result;
    }

    private async UniTask SpawnZoneIfNeededAsync(int fromZoneIndex, int toZoneIndex, CancellationToken ct)
    {
        if (_spawnedZones.Contains(toZoneIndex)) return;
        var bootstrapper = GameRunBootstrapper.Instance;
        if (bootstrapper == null) return;

        // 대각선 스폰 방지: CSV 방향을 단일 축으로 고정한 월드 중심 계산
        Vector3? centerOverride = null;
        if (TryCalcEdgeAdjacentCenter(fromZoneIndex, toZoneIndex, out var calcCenter, out _))
            centerOverride = calcCenter;

        await bootstrapper.SpawnZoneByIndexAsync(toZoneIndex, centerOverride, ct);

        // 사용된 월드 중심 기록 (다음 존 계산에 사용)
        _spawnedWorldCenters[toZoneIndex] = centerOverride ?? CalcCsvWorldCenter(toZoneIndex);
        _spawnedZones.Add(toZoneIndex);
    }

    /// <summary>
    /// fromZone의 월드 중심에서 toZone 방향을 CSV 델타의 우세 축으로 결정하고,
    /// 두 존이 면-대-면으로 맞닿는 월드 중심을 반환한다.
    /// </summary>
    private bool TryCalcEdgeAdjacentCenter(int fromIndex, int toIndex, out Vector3 newCenter, out Vector3 fromCenter)
    {
        newCenter  = Vector3.zero;
        fromCenter = Vector3.zero;

        if (!_spawnedWorldCenters.TryGetValue(fromIndex, out fromCenter)) return false;

        var zones    = Managers.ZoneLayout.GetZones(_layoutKey);
        var fromZone = zones?.Find(z => z.zone_index == fromIndex);
        var toZone   = zones?.Find(z => z.zone_index == toIndex);
        if (fromZone == null || toZone == null) return false;

        float dX = toZone.world_center_x - fromZone.world_center_x;
        float dZ = toZone.world_center_z - fromZone.world_center_z;

        if (Mathf.Abs(dX) >= Mathf.Abs(dZ))
        {
            // 동-서 방향 배치
            float sign  = dX >= 0 ? 1f : -1f;
            float halfW = (fromZone.grid_width + toZone.grid_width) * _blockCellSize * 0.5f;
            newCenter = new Vector3(fromCenter.x + sign * halfW, 0f, fromCenter.z);
        }
        else
        {
            // 남-북 방향 배치
            float sign  = dZ >= 0 ? 1f : -1f;
            float halfH = (fromZone.grid_height + toZone.grid_height) * _blockCellSize * 0.5f;
            newCenter = new Vector3(fromCenter.x, 0f, fromCenter.z + sign * halfH);
        }

        return true;
    }

    /// CSV 좌표 기반 월드 중심 (폴백용). GameRunBootstrapper.CalcZoneWorldCenter와 동일 공식.
    private Vector3 CalcCsvWorldCenter(int zoneIndex)
    {
        const float csvGridUnit = 55f;
        var zones = Managers.ZoneLayout.GetZones(_layoutKey);
        var zone  = zones?.Find(z => z.zone_index == zoneIndex);
        if (zone == null) return Vector3.zero;
        float roomW = zone.grid_width  * _blockCellSize;
        float roomH = zone.grid_height * _blockCellSize;
        return new Vector3(
            zone.world_center_x / csvGridUnit * roomW,
            0f,
            zone.world_center_z / csvGridUnit * roomH
        );
    }

    private static GameObject BuildSelectionCanvas(List<ZoneLayoutEntry> options, UniTaskCompletionSource<int> tcs)
    {
        var canvasGO = new GameObject("ZoneSelectionUI");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        // 반투명 전체화면 배경
        var bgGO = new GameObject("Background");
        bgGO.transform.SetParent(canvasGO.transform, false);
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.65f);
        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;

        // 카드 패널
        var panelGO = new GameObject("Panel");
        panelGO.transform.SetParent(canvasGO.transform, false);
        var panelImg = panelGO.AddComponent<Image>();
        panelImg.color = new Color(0.04f, 0.04f, 0.08f, 0.97f);
        var panelRT = panelGO.GetComponent<RectTransform>();
        float panelH = 160f + options.Count * 90f;
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(560f, panelH);
        panelRT.anchoredPosition = Vector2.zero;

        // 제목
        var titleGO = new GameObject("Title");
        titleGO.transform.SetParent(panelGO.transform, false);
        var titleTMP = titleGO.AddComponent<TextMeshProUGUI>();
        titleTMP.text = "다음 구역 선택";
        titleTMP.fontSize = 26f;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.alignment = TextAlignmentOptions.Center;
        titleTMP.color = new Color(0.95f, 0.88f, 0.65f);
        var titleRT = titleGO.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0f, 1f);
        titleRT.anchorMax = new Vector2(1f, 1f);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.offsetMin = new Vector2(20f, -65f);
        titleRT.offsetMax = new Vector2(-20f, -18f);

        // 존 옵션 카드들
        for (int i = 0; i < options.Count; i++)
        {
            var zone = options[i];
            int capturedIndex = zone.zone_index;

            var cardGO = new GameObject($"ZoneCard_{zone.zone_index}");
            cardGO.transform.SetParent(panelGO.transform, false);
            var cardImg = cardGO.AddComponent<Image>();
            cardImg.color = CategoryColor(zone.category);

            var cardRT = cardGO.GetComponent<RectTransform>();
            cardRT.anchorMin = new Vector2(0.08f, 1f);
            cardRT.anchorMax = new Vector2(0.92f, 1f);
            cardRT.pivot = new Vector2(0.5f, 1f);
            cardRT.sizeDelta = new Vector2(0f, 78f);
            cardRT.anchoredPosition = new Vector2(0f, -78f - i * 86f);

            var btn = cardGO.AddComponent<Button>();
            btn.onClick.AddListener(() => tcs.TrySetResult(capturedIndex));

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(cardGO.transform, false);
            var labelTMP = labelGO.AddComponent<TextMeshProUGUI>();
            labelTMP.text = $"<b>{zone.label}</b>   <size=15>{CategoryKor(zone.category)}  ·  난이도 {zone.difficulty_scale:F1}</size>";
            labelTMP.fontSize = 21f;
            labelTMP.alignment = TextAlignmentOptions.Center;
            labelTMP.color = Color.white;
            var labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = new Vector2(12f, 6f);
            labelRT.offsetMax = new Vector2(-12f, -6f);
        }

        return canvasGO;
    }

    private static Color CategoryColor(string cat) => cat?.ToLower() switch
    {
        "boss"    => new Color(0.55f, 0.08f, 0.08f, 0.97f),
        "elite"   => new Color(0.32f, 0.12f, 0.52f, 0.97f),
        "battle"  => new Color(0.08f, 0.18f, 0.38f, 0.97f),
        _         => new Color(0.12f, 0.15f, 0.18f, 0.97f),
    };

    private static string CategoryKor(string cat) => cat?.ToLower() switch
    {
        "battle"   => "전투",
        "elite"    => "정예",
        "boss"     => "보스",
        "corridor" => "통로",
        "start"    => "시작",
        _          => cat ?? "?",
    };
}
