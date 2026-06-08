using UnityEngine;

/// <summary>
/// 닿으면 줍는 서약 픽업(트리거, F 아님). GameRunSession이 없는 씬(베이스캠프)에 배치한다.
/// 픽업 1개 = 서약 1개 고정. 줍힌 서약은 PlayerLoadout에 "예약"되고,
/// 던전 진입(StartRoomGate.ExitStartRoomAsync, 핸들러 Initialize 후) 시 CovenantHandler.TryAdd로 적용된다.
/// (선택형/이벤트방 보상은 별도 WorldCovenantPickup — F+팝업 — 유지)
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class CovenantPickup : MonoBehaviour
{
    [SerializeField, Tooltip("부여할 서약 id (CovenantFactory 상수: galahad/morrigan/arthur 등)")]
    private string covenantId;

    [SerializeField, Tooltip("획득 시 일회성 VFX(선택, Addressable 키)")]
    private string acquireVfxKey;

    private bool _taken;

    private void Reset()
    {
        if (TryGetComponent<Collider>(out var col)) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_taken || string.IsNullOrEmpty(covenantId)) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;

        _taken = true;
        AppBootstrapper.Instance?.Loadout?.AddCovenant(covenantId);
        Debug.Log($"[CovenantPickup] 서약 예약: {covenantId}");

        if (!string.IsNullOrEmpty(acquireVfxKey))
            ItemEffectVfxHelper.SpawnOneShotAt(acquireVfxKey, transform.position);

        Destroy(gameObject);
    }
}
