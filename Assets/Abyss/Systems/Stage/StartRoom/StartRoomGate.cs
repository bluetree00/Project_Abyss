using UnityEngine;

/// <summary>
/// 스타트 방 출구 게이트.
/// WispController가 진입하고 캐릭터가 선택된 상태이면 StageMap으로 전환한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class StartRoomGate : MonoBehaviour
{
    [SerializeField, Tooltip("캐릭터 미선택 시 노출할 안내 오브젝트 (선택)")]
    private GameObject notReadyIndicator;

    private bool _triggered;

    private void Awake() => GetComponent<Collider>().isTrigger = true;

    private void OnTriggerEnter(Collider other)
    {
        if (_triggered) return;
        if (other.GetComponentInParent<WispController>() == null &&
            other.GetComponentInParent<PlayerController>() == null) return;

        var loadout = AppBootstrapper.Instance?.Loadout;
        if (loadout == null || !loadout.IsReady)
        {
            if (notReadyIndicator != null) notReadyIndicator.SetActive(true);
            Debug.LogWarning("[StartRoom] 캐릭터 미선택 — 게이트 통과 불가");
            return;
        }

        _triggered = true;
        Debug.Log("[StartRoom] 캐릭터 선택 완료 — StageMap 전환");
        AppBootstrapper.Instance.RequestLoad(Define.Scene.StageMap);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<WispController>() == null &&
            other.GetComponentInParent<PlayerController>() == null) return;
        if (notReadyIndicator != null) notReadyIndicator.SetActive(false);
    }
}
