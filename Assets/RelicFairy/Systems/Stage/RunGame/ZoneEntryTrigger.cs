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

    // OnTriggerEnter 시점에 기록한 진입 벽 방향 (중심→벽).
    // inward = -_entryWall. 진입 축만 margin/속도를 체크하기 위해 사용.
    private Vector3 _entryWall;

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

    private void OnTriggerEnter(Collider other)
    {
        // 진입 벽 방향 기록: 플레이어가 어느 쪽 경계에서 들어왔는지 최초 1회 캡처
        if (_entryWall == Vector3.zero)
        {
            var player = other.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                var rel = player.transform.position - transform.position;
                // 정규화된 경계 거리로 지배 축 결정 (더 경계에 가까운 축 = 진입 축)
                float normX = Mathf.Abs(rel.x) / (_zoneSizeX * 0.5f);
                float normZ = Mathf.Abs(rel.z) / (_zoneSizeZ * 0.5f);
                _entryWall  = normX >= normZ
                    ? new Vector3(Mathf.Sign(rel.x), 0f, 0f)
                    : new Vector3(0f, 0f, Mathf.Sign(rel.z));
            }
        }
        TryActivate(other);
    }

    // 존 스폰 시 플레이어가 이미 BoxCollider 내부에 있는 경우 OnTriggerEnter가 발화하지 않을 수 있어 Stay도 처리
    private void OnTriggerStay(Collider other) => TryActivate(other);

    private void TryActivate(Collider other)
    {
        if (_activated) return;
        var player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;

        var rel      = player.transform.position - transform.position;
        var inward   = -_entryWall; // 진입 벽 안쪽 방향 (벽→중심)

        if (_entryWall != Vector3.zero)
        {
            // ── 진입 축 margin 체크 ──────────────────────────────────
            // 진입한 쪽 경계에서 EntryCheckMargin 이상 들어와야 활성화.
            if (_entryWall.x != 0f && Mathf.Abs(rel.x) > _zoneSizeX * 0.5f - EntryCheckMargin) return;
            if (_entryWall.z != 0f && Mathf.Abs(rel.z) > _zoneSizeZ * 0.5f - EntryCheckMargin) return;

            // ── 속도 방향 체크 ───────────────────────────────────────
            // 진입 방향(inward)과 속도의 내적이 음수면 되돌아가는 중 → 발동 안 함.
            if (player.TryGetComponent<Rigidbody>(out var rb))
            {
                var velFlat = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
                if (velFlat.sqrMagnitude > 0.1f &&
                    Vector3.Dot(velFlat.normalized, inward) < -0.3f) return;
            }
        }
        else
        {
            // OnTriggerStay에서만 호출된 경우(_entryWall 미기록): 전방향 margin 폴백
            if (Mathf.Abs(rel.x) > _zoneSizeX * 0.5f - EntryCheckMargin) return;
            if (Mathf.Abs(rel.z) > _zoneSizeZ * 0.5f - EntryCheckMargin) return;
        }

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
    // EntryCheckMargin만큼 안쪽에 있을 때만 TryActivate가 실제로 발동된다.
    // (게이트 근처에서 BoxCollider에 살짝 걸치는 경우 조기 발동 방지)
    private const float EntryMargin      = 2f;
    private const float EntryCheckMargin = 2f;

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
