using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 선택된 존에 플레이어가 처음 진입하면 RoomWaveController를 활성화하는 트리거.
/// GameRunBootstrapper.SpawnZoneByIndexAsync 에서 각 존 GO에 부착된다.
///
/// 방 등장 연출(DissolveEntrance)은 SpawnZoneByIndexAsync에서 선행 완료됨.
/// 이 트리거는 스포너 활성화 + 웨이브 시작만 담당한다.
///
/// 전투 존 (WaveController 있음): 스포너 + 웨이브 활성화 → 클리어 시 ClearRewardTrigger가 ZoneExitGate를 활성화.
/// 비전투 존 (WaveController 없음, 코리도·이벤트 등): 진입 즉시 ZoneExitGate를 활성화.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class ZoneEntryTrigger : MonoBehaviour
{
    private RoomWaveController _waveController;
    private List<MonoBehaviour> _deferredSpawners;
    private bool _activated;

    /// <summary>
    /// 이 트리거를 초기화한다. zoneSizeX/Z 는 grid_width/height × blockCellSize.
    /// BoxCollider 크기를 존 전체 영역에 맞게 설정한다.
    /// </summary>
    public void Initialize(
        RoomWaveController   waveController,
        List<MonoBehaviour>  deferredSpawners,
        float                zoneSizeX,
        float                zoneSizeZ)
    {
        _waveController   = waveController;
        _deferredSpawners = deferredSpawners;

        var col = GetComponent<BoxCollider>();
        col.isTrigger = true;
        col.size      = new Vector3(zoneSizeX, 12f, zoneSizeZ);
        col.center    = new Vector3(0f, 6f, 0f);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other) => TryActivate(other);

    // 존 스폰 시 플레이어가 이미 BoxCollider 내부에 있는 경우 OnTriggerEnter가 발화하지 않을 수 있어 Stay도 처리
    private void OnTriggerStay(Collider other) => TryActivate(other);

    private void TryActivate(Collider other)
    {
        if (_activated) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;

        _activated = true;
        Destroy(this);

        if (_waveController != null)
            ActivateCombatZone();
        else
            ActivateNonCombatZone();
    }

    // ── Private ───────────────────────────────────────────────────────────

    private void ActivateCombatZone()
    {
        EnableSpawners();
        _waveController.Activate();
        Debug.Log($"[ZoneEntryTrigger] 전투 존 진입 — 웨이브 시작: {gameObject.name}");
    }

    private void ActivateNonCombatZone()
    {
        EnableSpawners();
        Debug.Log($"[ZoneEntryTrigger] 비전투 존 진입 — 클리어 게이트 활성화: {gameObject.name}");

        var zoneProgression = GameRunBootstrapper.Instance?.Run?.ZoneProgression;
        zoneProgression?.EnableExitGateForZone(zoneProgression.CurrentZoneIndex);
    }

    private void EnableSpawners()
    {
        if (_deferredSpawners == null) return;
        for (int i = 0; i < _deferredSpawners.Count; i++)
        {
            if (_deferredSpawners[i] != null)
                _deferredSpawners[i].enabled = true;
        }
    }
}
