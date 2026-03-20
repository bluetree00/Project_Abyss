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

    private void OnTriggerEnter(Collider other)
    {
        if (_pickedUp) return;

        var player = other.GetComponent<PlayerController>();
        if (player == null) return;

        _pickedUp = true;

        var data = _runtimeData ?? (weaponSO != null ? new WeaponData(weaponSO) : null);
        if (data == null)
        {
            _pickedUp = false;
            return;
        }

        player.RequestPickup(data, this);
        // Destroy는 팝업 결과 후 ConfirmPickup()에서 처리
    }

    /// <summary>플레이어가 트리거 밖으로 나가면 다시 픽업 가능하도록 리셋</summary>
    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponent<PlayerController>() == null) return;
        _pickedUp = false;
    }

    /// <summary>픽업 확정 — 월드 오브젝트 제거</summary>
    public void ConfirmPickup()
    {
        Destroy(gameObject);
    }

    /// <summary>픽업 취소 — _pickedUp은 플레이어가 나갈 때(OnTriggerExit)까지 유지</summary>
    public void CancelPickup()
    {
        // 콜라이더 조작 없이 _pickedUp이 true인 채로 유지
        // → 팝업 종료 직후 재발동 방지, 플레이어가 나갔다 오면 OnTriggerEnter 재발동 가능
    }
}
