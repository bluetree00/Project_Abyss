using UnityEngine;

/// <summary>
/// 스타트 방 출구 게이트.
/// 캐릭터 + 무기가 모두 선택된 상태이면 StageMap으로 전환한다.
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
        AppBootstrapper.Instance.RequestLoad(Define.Scene.StageMap);
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
