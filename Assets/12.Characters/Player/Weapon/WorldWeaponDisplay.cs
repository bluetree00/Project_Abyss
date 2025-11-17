using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Cysharp.Threading.Tasks;

[RequireComponent(typeof(Collider))]
public class WorldWeaponDisplay : MonoBehaviour
{
    [Header("Weapon Data")]
    public WeaponSO weaponSO; // 테스트용

    private GameObject _weaponInstance;
    private bool _pickedUp = false; // 중복 픽업 방지

    private async void Start()
    {
        if (weaponSO != null)
        {
            await SpawnWeaponPrefabAsync(weaponSO);
        }
    }

    private async UniTask SpawnWeaponPrefabAsync(WeaponSO so)
    {
        if (string.IsNullOrEmpty(so.weaponDisplayKey)) return;

        if (_weaponInstance != null)
            Destroy(_weaponInstance);

        var handle = Addressables.InstantiateAsync(so.weaponDisplayKey, transform.position, transform.rotation);
        await handle.Task;

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            _weaponInstance = handle.Result;
            _weaponInstance.transform.SetParent(transform, true);

            var wi = _weaponInstance.GetComponent<WeaponInstance>() ?? _weaponInstance.AddComponent<WeaponInstance>();
            wi.Initialize(new WeaponData(so));
        }
        else
        {
            Debug.LogError($"Weapon prefab 생성 실패: {so.weaponDisplayKey}");
        }
    }

    private async void OnTriggerEnter(Collider other)
    {
        if (_pickedUp) return;

        var player = other.GetComponent<PlayerController>();
        if (player != null)
        {
            _pickedUp = true;

            // WeaponData 복사본 생성
            var runtimeData = new WeaponData(weaponSO);

            // PlayerWeaponManager에 전달
            if (player.WeaponManager != null)
            {
                await player.WeaponManager.HandlePickupAsync(runtimeData, autoEquip: true);
            }

            // 월드 오브젝트 제거
            Destroy(gameObject);
        }
    }
}
