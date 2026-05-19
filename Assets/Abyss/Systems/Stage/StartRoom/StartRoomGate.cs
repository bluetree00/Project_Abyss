using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 스타트 방 출구 게이트.
/// 캐릭터 + 무기가 모두 선택된 상태이면 StageMap으로 전환한다.
/// 게이트 통과 직전에 isInStartRoom=false를 서버에 저장해 이어하기 시 StartRoom 재진입을 방지한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class StartRoomGate : MonoBehaviour
{
    [SerializeField, Tooltip("준비 완료 시 활성화할 포탈 비주얼 오브젝트")]
    private GameObject portalActive;
    [SerializeField, Tooltip("준비 미완료 시 노출할 안내 오브젝트 (선택)")]
    private GameObject notReadyIndicator;

    private bool _triggered;
    private bool _gateOpen;

    // ── Lifecycle ────────────────────────────────────────────────

    private void Awake() => GetComponent<Collider>().isTrigger = true;

    private void Update()
    {
        bool ready = IsLoadoutReady();
        if (ready == _gateOpen) return;

        _gateOpen = ready;
        if (portalActive != null) portalActive.SetActive(ready);
    }

    // ── Trigger ──────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (_triggered) return;
        if (other.GetComponentInParent<WispController>() == null &&
            other.GetComponentInParent<PlayerController>() == null) return;

        if (!IsLoadoutReady())
        {
            if (notReadyIndicator != null) notReadyIndicator.SetActive(true);
            Debug.LogWarning("[StartRoomGate] 캐릭터·무기 미선택 — 게이트 통과 불가");
            return;
        }

        _triggered = true;
        Debug.Log("[StartRoomGate] 준비 완료 — StageMap 전환");
        ExitStartRoomAsync().Forget();
    }

    private async UniTaskVoid ExitStartRoomAsync()
    {
        var bootstrapper = GameRunBootstrapper.Instance;

        // zone-layout 모드: Zone 0 클리어 후 첫 번째 인접 존 선택·스폰 → 인게임 전환
        if (bootstrapper != null && bootstrapper.IsZoneLayoutMode)
        {
            var runSession = bootstrapper.Run;
            var zoneProgression = runSession?.ZoneProgression;
            if (zoneProgression != null)
            {
                var ct = bootstrapper.GetCancellationTokenOnDestroy();
                await zoneProgression.ShowZoneSelectionAsync(0, ct);
            }
            else
            {
                // ZoneProgressionService 미초기화 시 기존 전체 스폰 폴백
                await bootstrapper.SpawnRemainingWorldZonesAsync();
            }
            UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(false);
            Debug.Log("[StartRoomGate] zone-layout 모드 — 존 선택 완료, 탐험 시작");
            return;
        }

        // 기존 StageMap 전환 흐름
        var rp      = RunProgressManager.Instance;
        var session = bootstrapper?.Run;

        if (rp != null && session != null && session.IsRunning)
        {
            // Start 노드 클리어 처리 — Visited 상태에서 Cleared로 올려야 맵 UI에서 정상 표시됨
            var spm = session.StagePointManager;
            if (spm != null && spm.CurrentPointId >= 0)
                spm.MarkCleared(spm.CurrentPointId);

            // 게이트 통과 = 스타트룸 퇴장 → isInStartRoom=false로 저장
            await rp.SaveAsync(session, rp.ActiveSlotIndex, isInStartRoom: false);
        }

        AppBootstrapper.Instance?.RequestLoad(Define.Scene.StageMap);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<WispController>() == null &&
            other.GetComponentInParent<PlayerController>() == null) return;
        if (notReadyIndicator != null) notReadyIndicator.SetActive(false);
    }

    // ── Private ──────────────────────────────────────────────────

    private static bool IsLoadoutReady()
    {
        var loadout = AppBootstrapper.Instance?.Loadout;
        return loadout != null && loadout.IsReady && loadout.WeaponSlot0 != null;
    }
}
