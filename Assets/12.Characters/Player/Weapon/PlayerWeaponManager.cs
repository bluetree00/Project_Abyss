using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// 플레이어별 무기 매니저 (싱글톤 아님)
/// - Player가 소유하고, Player에서 접근하도록 함
/// - AcquireWeaponAsync(WeaponData) 오버로드 제공
/// </summary>
public class PlayerWeaponManager : MonoBehaviour
{
    public int SlotCount => 2;

    [Serializable]
    public class WeaponSlot
    {
        public WeaponData runtimeData;
        public GameObject instance;
        public bool IsEmpty => runtimeData == null;
    }

    public WeaponSlot[] slots = new WeaponSlot[2];
    private System.Collections.Generic.List<WeaponData> _owned = new System.Collections.Generic.List<WeaponData>();

    // 소유자(선택): Player 참조 보관하면 편함
    private PlayerController _owner;

    public void Initialize(PlayerController owner)
    {
        _owner = owner;
        for (int i = 0; i < slots.Length; i++) if (slots[i] == null) slots[i] = new WeaponSlot();
    }

    // WeaponSO 키로 로드 후 획득 (기존 기능 유지)
    public async UniTask AcquireWeaponAsync(string weaponSOKey, bool autoEquip = true)
    {
        if (string.IsNullOrEmpty(weaponSOKey)) return;
        var handle = Addressables.LoadAssetAsync<WeaponSO>(weaponSOKey);
        await handle.Task;
        if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null) return;

        var runtime = new WeaponData(handle.Result);
        await AcquireWeaponAsync(runtime, autoEquip);
    }

    // WeaponData 직접 전달받아 획득 처리 (월드오브젝트가 호출할 때 사용)
    public async UniTask AcquireWeaponAsync(WeaponData runtimeData, bool autoEquip = true)
    {
        if (runtimeData == null) return;

        _owned.Add(runtimeData);

        // 빈 슬롯이 있으면 자동 장착
        int empty = GetFirstEmptySlotIndex();
        if (autoEquip && empty >= 0)
        {
            await EquipToSlotAsync(empty, runtimeData);
            return;
        }

        // 빈 슬롯 없으면 단순히 인벤토리에만 넣기 (또는 교체 흐름 호출)
        // TODO: 교체 UI/로직 처리
    }

    private async UniTask EquipToSlotAsync(int slotIndex, WeaponData runtimeData)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount) return;

        // 기존 장비 제거(있으면)
        if (!slots[slotIndex].IsEmpty)
        {
            Destroy(slots[slotIndex].instance);
        }

        // 인스턴스 생성
        GameObject instance = null;
        if (runtimeData.prefabKey != null)
        {
            var prefabHandle = Addressables.LoadAssetAsync<GameObject>(runtimeData.prefabKey);
            await prefabHandle.Task;
            if (prefabHandle.Status == AsyncOperationStatus.Succeeded)
            {
                instance = Instantiate(prefabHandle.Result, _owner.handTransform);
            }
        }

        // 슬롯 등록
        slots[slotIndex].runtimeData = runtimeData;
        slots[slotIndex].instance = instance;

        Debug.Log($"[{name}] Equipped {runtimeData.displayName} to slot {slotIndex}");
    }


    public int GetFirstEmptySlotIndex()
    {
        for (int i = 0; i < SlotCount; i++)
            if (slots[i] == null || slots[i].IsEmpty) return i;
        return -1;
    }

        /// <summary>
    /// 월드에서 플레이어가 WeaponData를 획득했을 때 처리
    /// - 빈 슬롯이 있으면 바로 장착
    /// - 슬롯이 가득 찼으면 교체 UI 호출
    /// </summary>
    public async UniTask HandlePickupAsync(WeaponData runtimeData, bool autoEquip = true)
    {
        if (runtimeData == null) return;

        // 1) 소유 목록에 추가
        _owned.Add(runtimeData);

        // 2) 빈 슬롯 확인
        int emptySlot = GetFirstEmptySlotIndex();
        if (autoEquip && emptySlot >= 0)
        {
            await EquipToSlotAsync(emptySlot, runtimeData);
            return;
        }

        // 3) 슬롯이 가득 찼으면 교체 처리
        // TODO: 실제 UI나 프롬프트를 여는 부분
        int? chosenSlot = await ShowReplacePromptAsync(runtimeData);
        if (chosenSlot.HasValue)
        {
            await ReplaceSlotAsync(chosenSlot.Value, runtimeData);
        }
        else
        {
            // 플레이어가 취소하면 인벤토리만 등록
            Debug.Log($"Pickup cancelled: {runtimeData.displayName}");
        }
    }

    private async UniTask<int?> ShowReplacePromptAsync(WeaponData newWeapon)
    {
        // 실제 UI 구현에 맞게 수정 필요
        Debug.Log($"Inventory full! Replace weapon with: {newWeapon.displayName}? Simulated choice: slot 0");
        await UniTask.Delay(TimeSpan.FromSeconds(1f));
        return 0; // 테스트용으로 항상 0번 슬롯 교체
    }

    private async UniTask ReplaceSlotAsync(int slotIndex, WeaponData newRuntime)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount) throw new ArgumentOutOfRangeException(nameof(slotIndex));

        var slot = slots[slotIndex];

        // 기존 무기 제거
        if (slot.instance != null)
        {
            Destroy(slot.instance);
            slot.instance = null;
        }

        slot.runtimeData = null;

        // 새 무기 장착
        await EquipToSlotAsync(slotIndex, newRuntime);
    }

}
