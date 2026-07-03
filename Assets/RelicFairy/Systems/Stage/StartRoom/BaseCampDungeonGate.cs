using UnityEngine;

/// <summary>
/// 베이스캠프 던전 입장 포탈. 유물(Loadout.Relic)+무기(Loadout.WeaponSlot0)+서약(예약)이 모두 준비되면
/// 활성화되고, 활성 상태에서 플레이어가 트리거에 진입하면 던전 씬으로 전환한다(BaseCampBootstrapper.EnterDungeon).
/// 미준비 시 포탈 비주얼 비활성 + 통과 차단. (StartRoomGate 스타트 방 모드 게이팅 패턴 기반)
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class BaseCampDungeonGate : MonoBehaviour
{
    [SerializeField, Tooltip("준비 완료 시 활성화할 포탈 비주얼 오브젝트(선택)")]
    private GameObject portalActive;

    private bool _entered;
    private bool _ready;

    private void Reset()
    {
        if (TryGetComponent<Collider>(out var col)) col.isTrigger = true;
    }

    private void Start()
    {
        if (portalActive != null) portalActive.SetActive(false);
    }

    private void Update()
    {
        bool ready = IsLoadoutReady();
        if (ready == _ready) return;
        _ready = ready;
        if (portalActive != null) portalActive.SetActive(ready);
    }

    private void OnTriggerEnter(Collider other) => TryEnter(other);
    private void OnTriggerStay(Collider other)  => TryEnter(other);

    private void TryEnter(Collider other)
    {
        if (_entered || !_ready) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;

        _entered = true;
        BaseCampBootstrapper.Instance?.EnterDungeon();
    }

    private static bool IsLoadoutReady()
    {
        var loadout = AppBootstrapper.Instance?.Loadout;
        return loadout != null
            && loadout.Relic != null
            && loadout.WeaponSlot0 != null
            && loadout.ReservedCovenants != null && loadout.ReservedCovenants.Count > 0;   // 서약까지 필수
    }
}
