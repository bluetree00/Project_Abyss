using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
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
    private float _zoneSizeX;
    private float _zoneSizeZ;

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
        _zoneSizeX        = zoneSizeX;
        _zoneSizeZ        = zoneSizeZ;

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
        var player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;

        _activated = true;

        if (_waveController != null)
        {
            var ct = gameObject.GetCancellationTokenOnDestroy();
            ActivateCombatZoneAsync(player.transform, ct).Forget();
        }
        else
        {
            ActivateNonCombatZone();
        }

        Destroy(this);
    }

    // ── Private ───────────────────────────────────────────────────────────

    // 플레이어가 존 경계에서 EntryMargin만큼 안쪽에 들어온 뒤 배리어를 생성한다.
    // 경계선 위에서 트리거가 발화해도 플레이어가 실제로 방 안에 들어올 때까지 대기.
    private const float EntryMargin = 2f;

    private async UniTaskVoid ActivateCombatZoneAsync(Transform playerTransform, CancellationToken ct)
    {
        EnableSpawners();
        _waveController.Activate();
        Debug.Log($"[ZoneEntryTrigger] 전투 존 진입 — 웨이브 시작: {gameObject.name}");

        float innerHX     = _zoneSizeX * 0.5f - EntryMargin;
        float innerHZ     = _zoneSizeZ * 0.5f - EntryMargin;
        var   origin      = transform.position;
        // await 이후에는 Destroy(this)로 컴포넌트가 제거되어 transform 접근이 불가능하므로 미리 캡처
        var   zoneParent  = transform;

        try
        {
            await UniTask.WaitUntil(() =>
            {
                if (playerTransform == null) return true;
                var rel = playerTransform.position - origin;
                return Mathf.Abs(rel.x) < innerHX && Mathf.Abs(rel.z) < innerHZ;
            }, cancellationToken: ct);
        }
        catch (OperationCanceledException) { return; }

        var zoneProgression = GameRunBootstrapper.Instance?.Run?.ZoneProgression;
        if (zoneProgression != null)
        {
            var barrierGO = new GameObject("CombatBarrier");
            barrierGO.transform.SetParent(zoneParent, false);
            var barrier = barrierGO.AddComponent<CombatBarrier>();
            barrier.Initialize(_zoneSizeX, _zoneSizeZ);
            zoneProgression.RegisterBarrier(zoneProgression.CurrentZoneIndex, barrier);
        }
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
