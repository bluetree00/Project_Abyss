using UnityEngine;

public enum StartRoomPickupType { Character, Weapon }

/// <summary>
/// 스타트 방에 배치하는 캐릭터/무기 픽업 오브젝트.
/// 플레이어가 트리거에 진입하면 AppBootstrapper.Loadout에 선택 결과를 저장한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class StartRoomPickup : MonoBehaviour
{
    [SerializeField] private StartRoomPickupType pickupType;

    [Header("Character")]
    [SerializeField] private CharacterData characterData;
    [SerializeField] private string characterPrefabKey = "Knight";

    [Header("Weapon (Slot 0)")]
    [SerializeField] private WeaponSO weaponSO;

    private void Awake() => GetComponent<Collider>().isTrigger = true;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.TryGetComponent<PlayerController>(out _)) return;
        Apply();
    }

    private void Apply()
    {
        var loadout = AppBootstrapper.Instance?.Loadout;
        if (loadout == null) return;

        if (pickupType == StartRoomPickupType.Character)
        {
            loadout.SetCharacter(characterData, characterPrefabKey);
            Managers.CharacterData?.SetCharacterData(characterData, characterPrefabKey);
            Debug.Log($"[StartRoom] 캐릭터 선택: {characterData?.characterName} ({characterPrefabKey})");
        }
        else
        {
            loadout.SetWeaponSlot0(weaponSO);
            Debug.Log($"[StartRoom] 무기 선택: {weaponSO?.displayName}");
            // 게이트 통과 시점에 저장하므로 픽업 즉시 저장하지 않음
        }
    }
}
