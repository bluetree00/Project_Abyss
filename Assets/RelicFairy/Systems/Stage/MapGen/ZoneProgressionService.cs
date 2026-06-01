using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

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
    private readonly Dictionary<int, CombatBarrier>                _barriers           = new();

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
    /// <summary>해당 존의 출구 게이트가 활성화됐는지 (전투 클리어 또는 비전투 진입 완료).</summary>
    public bool IsExitEnabled(int zoneIndex) => _gateActivatedZones.Contains(zoneIndex);

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

    /// <summary>ZoneEntryTrigger가 전투 존 진입 시 생성한 CombatBarrier를 등록한다.</summary>
    public void RegisterBarrier(int zoneIndex, CombatBarrier barrier)
        => _barriers[zoneIndex] = barrier;

    /// <summary>
    /// 스타트 방 전용. fromZone의 next_zone_indices 중 첫 번째 존으로 UI 없이 직접 진입한다.
    /// Zone 0처럼 문이 하나의 목적지만 가리킬 때 사용한다.
    /// </summary>
    public async UniTask DirectlyEnterFirstNextZoneAsync(int fromZoneIndex, CancellationToken ct)
    {
        var options = GetNextZoneOptions(fromZoneIndex);
        if (options.Count == 0)
        {
            Debug.LogWarning($"[ZoneProgression] Zone {fromZoneIndex} — 연결된 다음 존 없음");
            return;
        }
        await DirectlyEnterZoneAsync(fromZoneIndex, options[0].zone_index, ct);
    }

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

        if (_barriers.TryGetValue(zoneIndex, out var barrier) && barrier != null)
            barrier.Open();

        // 현재 존의 게이트 활성화
        bool any = false;
        foreach (var kvp in _exitGates)
            if (kvp.Key.from == zoneIndex && kvp.Value != null)
            { kvp.Value.EnableGate(); any = true; }

        if (!any)
            Debug.LogWarning($"[ZoneProgression] Zone {zoneIndex} ExitGate 없음 (레거시 선택 UI 제거됨)");

        // 다른 클리어된 방들에서 이미 스폰된 존으로 가는 게이트도 함께 활성화
        // (다른 경로로 목적 존이 먼저 스폰된 경우를 대응)
        foreach (var kvp in _exitGates)
        {
            if (!_gateActivatedZones.Contains(kvp.Key.from)) continue;
            if (!_spawnedZones.Contains(kvp.Key.to)) continue;
            kvp.Value?.EnableGate();
        }
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
    /// fromZone의 월드 중심에서 toZone 방향을 계산하여 두 존이 맞닿는 월드 중심을 반환한다.
    /// dZ != 0 이면 항상 Z(앞뒤)축 우선 배치하고 X 레인 오프셋을 추가한다.
    /// dZ == 0 이면 순수 X축(좌우) 배치.
    /// 이 방식으로 대각선 CSV 연결도 같은 레이어(Z)에 정렬되어 겹침 없이 배치된다.
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

        // 레인 기반 고정 간격: world_center는 lane×55 블록이므로 직접 스케일
        newCenter = new Vector3(
            fromCenter.x + dX * _blockCellSize,
            0f,
            fromCenter.z + dZ * _blockCellSize
        );

        return true;
    }

    /// CSV 좌표 기반 월드 중심 (폴백용). world_center = lane×55 블록이므로 blockCellSize만 곱한다.
    private Vector3 CalcCsvWorldCenter(int zoneIndex)
    {
        var zones = Managers.ZoneLayout.GetZones(_layoutKey);
        var zone  = zones?.Find(z => z.zone_index == zoneIndex);
        if (zone == null) return Vector3.zero;
        return new Vector3(
            zone.world_center_x * _blockCellSize,
            0f,
            zone.world_center_z * _blockCellSize
        );
    }

}
