using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Cysharp.Threading.Tasks;

[RequireComponent(typeof(Collider))]
public class WorldWeaponDisplay : MonoBehaviour
{
    [Header("Weapon Data")]
    public WeaponSO weaponSO; // 에디터 배치용

    private WeaponData _runtimeData;  // 코드로 드랍된 무기 데이터
    private GameObject _weaponInstance;
    private bool _pickedUp = false;

    private async void Start()
    {
        if (_runtimeData == null && weaponSO != null)
            await SpawnDisplayAsync(weaponSO.weaponDisplayKey);
    }

    /// <summary>
    /// 런타임 WeaponData로 초기화 (교체 드랍 시 사용)
    /// </summary>
    public void InitFromData(WeaponData data)
    {
        _runtimeData = data;
        SpawnDisplayAsync(data.weaponDisplayKey).Forget();
    }

    /// <summary>
    /// 버린 무기를 월드에 스폰
    /// </summary>
    public static WorldWeaponDisplay SpawnFromData(WeaponData data, Vector3 position)
    {
        var go = new GameObject($"DroppedWeapon_{data.displayName}");
        go.transform.position = position;

        var col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 1f;

        var display = go.AddComponent<WorldWeaponDisplay>();
        display.InitFromData(data);
        return display;
    }

    private async UniTask SpawnDisplayAsync(string displayKey)
    {
        if (string.IsNullOrEmpty(displayKey)) return;

        if (_weaponInstance != null)
            Destroy(_weaponInstance);

        var handle = Addressables.InstantiateAsync(displayKey, transform.position, transform.rotation);
        await handle.Task;

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            _weaponInstance = handle.Result;
            _weaponInstance.transform.SetParent(transform, true);
        }
        else
        {
            Debug.LogWarning($"[WorldWeaponDisplay] 프리팹 로드 실패: {displayKey}");
        }
    }

    private async void OnTriggerEnter(Collider other)
    {
        if (_pickedUp) return;

        var player = other.GetComponent<PlayerController>();
        if (player == null) return;

        _pickedUp = true;

        var data = _runtimeData ?? (weaponSO != null ? new WeaponData(weaponSO) : null);
        if (data == null) { _pickedUp = false; return; }

        bool acquired = false;
        if (player.WeaponManager != null)
            acquired = await player.WeaponManager.HandlePickupAsync(data);

        if (acquired)
        {
            // 교체 완료 → 픽업 오브젝트 제거
            Destroy(gameObject);
        }
        else
        {
            // 버리기 선택 → 콜라이더 일시 비활성화 후 바닥에 유지
            // 플레이어가 겹쳐 있는 상태이므로 2초 후 다시 줍기 가능
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
            await UniTask.Delay(2000);
            if (this != null && gameObject != null)
            {
                if (col != null) col.enabled = true;
                _pickedUp = false;

                // 파티클 재시작
                if (_weaponInstance != null)
                    foreach (var ps in _weaponInstance.GetComponentsInChildren<ParticleSystem>(true))
                        ps.Play();
            }
        }
    }
}
