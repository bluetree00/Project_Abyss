using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.InputSystem;

/// <summary>
/// 플레이어별 무기 매니저 (싱글톤 아님)
/// - 슬롯 수: 2
/// - Addressables.InstantiateAsync / ReleaseInstance 사용
/// - ReplaceSlotAsync, SwapSlotsAsync 포함
/// - 기존 장비는 비활성화, 새 장비는 활성화
/// - 프리팹 Transform 원본 유지
/// 
/// 추가:
/// - IWeaponProvider 인터페이스 구현
/// - CurrentWeaponData/Instance 접근자, 유틸 메서드 제공
/// </summary>
public interface IWeaponProvider
{
    WeaponData CurrentWeaponData { get; }
    GameObject CurrentWeaponInstance { get; }
    bool HasWeapon { get; }
    int CurrentSlotIndex { get; }
    event Action<WeaponData, GameObject> OnWeaponChanged;
    bool TryGetCurrentWeapon(out WeaponData data, out GameObject instance);
    T GetCurrentWeaponComponent<T>() where T : Component;
    Component GetCurrentWeaponComponent(Type type);
}

public class PlayerWeaponManager : MonoBehaviour, IWeaponProvider
{
    public int SlotCount => 2;

    [Serializable]
    public class WeaponSlot
    {
        public WeaponData runtimeData;
        public GameObject instance;
        public bool isAddressablesInstance;
        public bool IsEmpty => runtimeData == null;
    }

    public WeaponSlot[] slots = new WeaponSlot[2];
    private List<WeaponData> _owned = new List<WeaponData>();

    private PlayerController _owner;

    private int currentSlotIndex = -1;
    private bool _isSwitching = false;

    // 기존 이벤트 유지 (외부에서 구독)
    public event Action<WeaponData, GameObject> OnWeaponChanged;

    // ----------------------
    // 편의 접근자 / IWeaponProvider 구현
    // ----------------------
    public WeaponData CurrentWeaponData
    {
        get
        {
            if (currentSlotIndex >= 0 && currentSlotIndex < SlotCount)
                return slots[currentSlotIndex].runtimeData;
            return null;
        }
    }

    public GameObject CurrentWeaponInstance
    {
        get
        {
            if (currentSlotIndex >= 0 && currentSlotIndex < SlotCount)
                return slots[currentSlotIndex].instance;
            return null;
        }
    }

    public bool HasWeapon => CurrentWeaponData != null;
    public int CurrentSlotIndex => currentSlotIndex;

    public bool TryGetCurrentWeapon(out WeaponData data, out GameObject instance)
    {
        data = CurrentWeaponData;
        instance = CurrentWeaponInstance;
        return data != null;
    }

    /// <summary>
    /// 장비 인스턴스에서 특정 컴포넌트를 안전하게 가져옵니다(캐싱은 호출자에게 맡김).
    /// </summary>
    public T GetCurrentWeaponComponent<T>() where T : Component
    {
        var inst = CurrentWeaponInstance;
        if (inst == null) return null;
        return inst.GetComponent<T>();
    }

    public Component GetCurrentWeaponComponent(Type type)
    {
        var inst = CurrentWeaponInstance;
        if (inst == null) return null;
        return inst.GetComponent(type);
    }

    // ----------------------
    // 내부
    // ----------------------
    private void Awake()
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] == null) slots[i] = new WeaponSlot();
    }

    public void Initialize(PlayerController owner)
    {
        _owner = owner;
    }

    private void Update()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.digit1Key.wasPressedThisFrame) _ = SwitchToSlotAsync(0);
        if (Keyboard.current.digit2Key.wasPressedThisFrame) _ = SwitchToSlotAsync(1);
    }

    // ----------------------
    // 무기 획득
    // ----------------------
    public async UniTask AcquireWeaponAsync(string weaponSOKey, bool autoEquip = true)
    {
        if (string.IsNullOrEmpty(weaponSOKey)) return;

        var handle = Addressables.LoadAssetAsync<WeaponSO>(weaponSOKey);
        await handle.Task;
        if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null) return;

        var runtime = new WeaponData(handle.Result);
        await AcquireWeaponAsync(runtime, autoEquip);
    }

    public async UniTask AcquireWeaponAsync(WeaponData runtimeData, bool autoEquip = true)
    {
        if (runtimeData == null) return;

        _owned.Add(runtimeData);

        int empty = GetFirstEmptySlotIndex();
        if (autoEquip && empty >= 0)
        {
            await EquipToSlotAsync(empty, runtimeData, true);
        }
    }

    // ----------------------
    // 슬롯 장착 (기존 장비는 비활성화)
    // ----------------------
    private async UniTask EquipToSlotAsync(int slotIndex, WeaponData runtimeData, bool setActive = false)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount || runtimeData == null) return;

        var slot = slots[slotIndex];

        // 현재 장착 중인 슬롯 비활성화
        if (currentSlotIndex >= 0 && currentSlotIndex != slotIndex)
        {
            var cur = slots[currentSlotIndex];
            if (cur.instance != null) cur.instance.SetActive(false);
        }

        // 슬롯에 데이터 등록
        slot.runtimeData = runtimeData;

        // 인스턴스 생성
        if (slot.instance == null && !string.IsNullOrEmpty(runtimeData.weaponPrefabKey))
        {
            try
            {
                var instHandle = Addressables.InstantiateAsync(runtimeData.weaponPrefabKey,
                    _owner != null ? _owner.handTransform : null);
                await instHandle.Task;
                if (instHandle.Status == AsyncOperationStatus.Succeeded && instHandle.Result != null)
                {
                    slot.instance = instHandle.Result;
                    slot.isAddressablesInstance = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"EquipToSlotAsync failed: {ex.Message}");
            }
        }

        if (slot.instance != null)
        {
            // 프리팹 Transform 그대로 handTransform에 붙이되 위치/회전 유지
            slot.instance.transform.SetParent(_owner != null ? _owner.handTransform : null, true);
            slot.instance.SetActive(setActive);
        }

        if (setActive)
            await SetCurrentSlotInternalAsync(slotIndex);

        Debug.Log($"Equipped {runtimeData.displayName} to slot {slotIndex} (active={setActive})");
    }

    // ----------------------
    // 슬롯 전환
    // ----------------------
    public async UniTask SwitchToSlotAsync(int slotIndex)
    {
        if (_isSwitching) return;
        _isSwitching = true;

        try
        {
            if (slotIndex < 0 || slotIndex >= SlotCount) return;

            var target = slots[slotIndex];
            if (target.IsEmpty)
            {
                Debug.Log($"Slot {slotIndex} is empty.");
                return;
            }
            if (currentSlotIndex == slotIndex) return;

            // 이전 슬롯 비활성화
            if (currentSlotIndex >= 0)
            {
                var cur = slots[currentSlotIndex];
                if (cur.instance != null) cur.instance.SetActive(false);
            }

            // 타겟 인스턴스 없으면 생성
            if (target.instance == null && !string.IsNullOrEmpty(target.runtimeData.weaponPrefabKey))
            {
                try
                {
                    var instHandle = Addressables.InstantiateAsync(target.runtimeData.weaponPrefabKey,
                        _owner != null ? _owner.handTransform : null);
                    await instHandle.Task;
                    if (instHandle.Status == AsyncOperationStatus.Succeeded && instHandle.Result != null)
                    {
                        target.instance = instHandle.Result;
                        target.isAddressablesInstance = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"SwitchToSlotAsync failed: {ex.Message}");
                }
            }

            if (target.instance != null)
            {
                target.instance.transform.SetParent(_owner != null ? _owner.handTransform : null, true);
                target.instance.SetActive(true);
            }

            await SetCurrentSlotInternalAsync(slotIndex);
            Debug.Log($"Switched to slot {slotIndex} => {target.runtimeData.displayName}");
        }
        finally
        {
            _isSwitching = false;
        }
    }

    private async UniTask SetCurrentSlotInternalAsync(int slotIndex)
    {
        currentSlotIndex = slotIndex;
        // 이벤트 호출 (IWeaponProvider 구현체의 이벤트)
        OnWeaponChanged?.Invoke(slots[slotIndex].runtimeData, slots[slotIndex].instance);
        await UniTask.Yield();
    }

    public int GetFirstEmptySlotIndex()
    {
        for (int i = 0; i < SlotCount; i++)
            if (slots[i] == null || slots[i].IsEmpty) return i;
        return -1;
    }

    public int GetCurrentSlotIndex() => currentSlotIndex;

    // ----------------------
    // 장비 획득 처리
    // ----------------------
    public async UniTask HandlePickupAsync(WeaponData runtimeData, bool autoEquip = true)
    {
        if (runtimeData == null) return;

        _owned.Add(runtimeData);
        int empty = GetFirstEmptySlotIndex();
        if (autoEquip && empty >= 0)
        {
            await EquipToSlotAsync(empty, runtimeData, true);
            return;
        }

        int? chosenSlot = await ShowReplacePromptAsync(runtimeData);
        if (chosenSlot.HasValue)
        {
            var replaced = await ReplaceSlotAsync(chosenSlot.Value, runtimeData);
            if (replaced != null) Debug.Log($"Replaced {replaced.displayName}");
        }
        else
        {
            Debug.Log($"Pickup cancelled: {runtimeData.displayName}");
        }
    }

    private async UniTask<int?> ShowReplacePromptAsync(WeaponData newWeapon)
    {
        Debug.Log($"Inventory full! Replace weapon with: {newWeapon.displayName}? Simulated choice: slot 0");
        await UniTask.Delay(TimeSpan.FromSeconds(1f));
        return 0;
    }

    // ----------------------
    // 슬롯 교체
    // ----------------------
    public async UniTask<WeaponData> ReplaceSlotAsync(int slotIndex, WeaponData newRuntime)
    {
        if (_isSwitching) return null;
        _isSwitching = true;

        try
        {
            var slot = slots[slotIndex];
            var old = slot.runtimeData;

            // 기존 장비 비활성화
            if (slot.instance != null) slot.instance.SetActive(false);

            slot.runtimeData = newRuntime;
            slot.instance = null;
            slot.isAddressablesInstance = false;

            await EquipToSlotAsync(slotIndex, newRuntime, true);
            return old;
        }
        finally
        {
            _isSwitching = false;
        }
    }

    // ----------------------
    // 슬롯 교환
    // ----------------------
    public async UniTask SwapSlotsAsync(int slotA, int slotB)
    {
        if (_isSwitching) return;
        _isSwitching = true;

        try
        {
            if (slotA < 0 || slotA >= SlotCount || slotB < 0 || slotB >= SlotCount || slotA == slotB) return;

            var tempRuntime = slots[slotA].runtimeData;
            var tempInstance = slots[slotA].instance;
            var tempIsAddr = slots[slotA].isAddressablesInstance;

            slots[slotA].runtimeData = slots[slotB].runtimeData;
            slots[slotA].instance = slots[slotB].instance;
            slots[slotA].isAddressablesInstance = slots[slotB].isAddressablesInstance;

            slots[slotB].runtimeData = tempRuntime;
            slots[slotB].instance = tempInstance;
            slots[slotB].isAddressablesInstance = tempIsAddr;

            // 활성화 상태 유지
            if (currentSlotIndex == slotA)
            {
                if (slots[slotA].instance != null) slots[slotA].instance.SetActive(true);
                if (slots[slotB].instance != null) slots[slotB].instance.SetActive(false);
            }
            else if (currentSlotIndex == slotB)
            {
                if (slots[slotB].instance != null) slots[slotB].instance.SetActive(true);
                if (slots[slotA].instance != null) slots[slotA].instance.SetActive(false);
            }

            Debug.Log($"Swapped slot {slotA} and {slotB}");
            await UniTask.Yield();
        }
        finally
        {
            _isSwitching = false;
        }
    }

    // ----------------------
    // 클린업
    // ----------------------
    private void OnDestroy()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            var s = slots[i];
            if (s != null && s.instance != null)
            {
                if (s.isAddressablesInstance) Addressables.ReleaseInstance(s.instance);
                else Destroy(s.instance);

                s.instance = null;
                s.isAddressablesInstance = false;
            }
        }
    }
}
