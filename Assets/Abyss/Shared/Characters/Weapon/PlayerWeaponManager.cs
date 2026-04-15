using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

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
    public const int Slot0 = 0;
    public const int Slot1 = 1;

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

    /// <summary>슬롯 0 무기 데이터</summary>
    public WeaponData Weapon0Data => slots[Slot0]?.runtimeData;

    /// <summary>슬롯 1 무기 데이터</summary>
    public WeaponData Weapon1Data  => slots[Slot1]?.runtimeData;

    // 기본 풀 사이즈 (필요시 변경)
    private const int defaultPoolSizeForEffects = 6;

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
    /// <summary>무기 타입에 따라 장착할 손 결정</summary>
    private Transform ResolveHandTransform(WeaponData data)
    {
        if (_owner == null) return null;
        bool useLeft = data != null
            && (data.weaponType == WeaponType.Bow || data.weaponType == WeaponType.Crossbow)
            && _owner.handTransformLeft != null;
        return useLeft ? _owner.handTransformLeft : _owner.handTransform;
    }

    private void Awake()
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] == null) slots[i] = new WeaponSlot();
    }

    public void Initialize(PlayerController owner)
    {
        _owner = owner;
    }

    // ----------------------
    // 무기 획득 (Addressables key)
    // ----------------------
    public async UniTask AcquireWeaponAsync(string weaponSOKey, bool autoEquip = true)
    {
        if (string.IsNullOrEmpty(weaponSOKey)) return;

        var handle = Addressables.LoadAssetAsync<WeaponSO>(weaponSOKey);
        await handle.Task;
        if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null) return;

        var runtime = WeaponData.FromSO(handle.Result);
        ApplyServerOverride(runtime);
        await AcquireWeaponAsync(runtime, autoEquip);
    }

    // ----------------------
    // 무기 획득 (이미 로드된 WeaponData)
    // - 이곳에서 effectPackage가 있으면 Managers에게 풀 초기화 요청(대기)
    // ----------------------
    public async UniTask AcquireWeaponAsync(WeaponData runtimeData, bool autoEquip = true)
    {
        if (runtimeData == null) return;

        _owned.Add(runtimeData);

        if (autoEquip)
        {
            int targetSlot = GetFirstEmptySlotIndex();
            if (targetSlot < 0) targetSlot = Slot0;
            await EquipToSlotAsync(targetSlot, runtimeData, setActive: true);
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
                // 활/석궁은 왼손, 나머지는 오른손
                bool useLeftHand = runtimeData.weaponType == WeaponType.Bow
                                || runtimeData.weaponType == WeaponType.Crossbow;
                Transform mountPoint = _owner != null
                    ? (useLeftHand && _owner.handTransformLeft != null
                        ? _owner.handTransformLeft
                        : _owner.handTransform)
                    : null;
                Debug.Log($"[WeaponManager] Mount: type={runtimeData.weaponType}, useLeft={useLeftHand}, leftHand={_owner?.handTransformLeft?.name ?? "null"}, mount={mountPoint?.name ?? "null"}");
                var instHandle = Addressables.InstantiateAsync(runtimeData.weaponPrefabKey, mountPoint);
                await instHandle.Task;
                if (instHandle.Status == AsyncOperationStatus.Succeeded && instHandle.Result != null)
                {
                    slot.instance = instHandle.Result;
                    slot.isAddressablesInstance = true;

                    var wi = slot.instance.GetComponent<WeaponInstance>();
                    if (wi != null) wi.Initialize(runtimeData);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"EquipToSlotAsync failed: {ex.Message}");
            }
        }

        if (slot.instance != null)
        {
            var parent = ResolveHandTransform(runtimeData);
            slot.instance.transform.SetParent(parent, false);
            slot.instance.SetActive(setActive);

            // 등장 연출
            if (setActive)
                DissolveEffect.PlayAppear(slot.instance, 0.4f);
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
                        ResolveHandTransform(target.runtimeData));
                    await instHandle.Task;
                    if (instHandle.Status == AsyncOperationStatus.Succeeded && instHandle.Result != null)
                    {
                        target.instance = instHandle.Result;
                        target.isAddressablesInstance = true;

                        var wi = target.instance.GetComponent<WeaponInstance>();
                        if (wi != null) wi.Initialize(target.runtimeData);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"SwitchToSlotAsync failed: {ex.Message}");
                }
            }

            if (target.instance != null)
            {
                var parent = ResolveHandTransform(target.runtimeData);
                target.instance.transform.SetParent(parent, false);
                target.instance.SetActive(true);

                // 무기 전환 시 디졸브 등장 연출
                DissolveEffect.PlayAppear(target.instance, 0.4f);
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
    // 장비 획득 처리 (픽업 등 외부 호출)
    // - HandlePickupAsync에서도 풀 초기화를 수행하도록 추가
    // ----------------------
    public async UniTask HandlePickupAsync(WeaponData runtimeData, WorldWeaponDisplay source = null)
    {
        if (runtimeData == null)
        {
            source?.CancelPickup();
            return;
        }

        ApplyServerOverride(runtimeData);

        int emptySlot = GetFirstEmptySlotIndex();

        if (emptySlot >= 0)
        {
            // 빈 슬롯 있음: 바로 장착
            _owned.Add(runtimeData);
            source?.ConfirmPickup();
            await EquipToSlotAsync(emptySlot, runtimeData, setActive: true);
            return;
        }

        // 슬롯 2개 모두 차 있음 → 양쪽 비교 팝업
        int? chosenSlot = await ShowReplacePromptAsync(runtimeData);
        if (chosenSlot.HasValue)
        {
            _owned.Add(runtimeData);
            source?.ConfirmPickup();
            var replaced = await ReplaceSlotAsync(chosenSlot.Value, runtimeData);
            if (replaced != null) Debug.Log($"Replaced {replaced.displayName}");
        }
        else
        {
            // 버리기: 월드 아이템 복원
            source?.CancelPickup();
            Debug.Log($"Pickup cancelled: {runtimeData.displayName}");
        }
    }

    private async UniTask<int?> ShowReplacePromptAsync(WeaponData newWeapon)
    {
        var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_WeaponReplacePopup>();
        if (popup == null)
        {
            Debug.LogWarning("[PlayerWeaponManager] UI_WeaponReplacePopup 로드 실패, 자동 교체");
            return Slot0;
        }

        popup.Setup(slots[Slot0].runtimeData, slots[Slot1].runtimeData, newWeapon);
        return await popup.WaitForChoiceAsync();
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
            if (slotIndex < 0 || slotIndex >= SlotCount) return null;
            var slot = slots[slotIndex];
            var old = slot.runtimeData;

            // ---------- 1) 버린 무기를 월드에 드랍 ----------
            if (old != null && _owner != null)
            {
                var dropPos = _owner.transform.position + _owner.transform.right * 1.5f;
                WorldWeaponDisplay.SpawnFromData(old, dropPos);
            }
            _owned.Remove(old);

            // ---------- 2) 기존 인스턴스 정리 (Addressables 인스턴스는 ReleaseInstance 호출) ----------
            if (slot.instance != null)
            {
                try
                {
                    if (slot.isAddressablesInstance)
                    {
                        Addressables.ReleaseInstance(slot.instance);
                    }
                    else
                    {
                        // 런타임 생성된 일반 인스턴스이면 파괴
                        UnityEngine.Object.Destroy(slot.instance);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PlayerWeaponManager] Failed to release/ destroy old weapon instance: {ex.Message}");
                }
                finally
                {
                    slot.instance = null;
                    slot.isAddressablesInstance = false;
                }
            }

            // ---------- 3) 슬롯 데이터 교체 ----------
            slot.runtimeData = newRuntime;

            // ---------- 4) 새 장비를 즉시 장착(활성화)하고 애니메이션/이벤트 트리거 발생시키기 ----------
            // EquipToSlotAsync 내부에서 instance 생성 및 SetCurrentSlotInternalAsync 호출됨
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
    // 서버 수치 오버라이드
    // ----------------------
    /// <summary>
    /// WeaponData의 수치를 서버 EquipmentEntry로 오버라이드.
    /// SO의 에셋 참조(animation, ability, skill)는 유지하고 수치만 교체.
    /// </summary>
    private static void ApplyServerOverride(WeaponData data)
    {
        if (data == null) return;

        var mgr = Managers.ServerEquipment;
        if (mgr == null || !mgr.IsInitialized) return;

        // weaponPrefabKey 또는 displayName으로 서버 데이터 조회
        EquipmentEntry entry = null;

        // 모든 서버 장비를 순회하여 prefabKey 또는 이름 매칭
        foreach (var kv in mgr.GetAll())
        {
            var e = kv.Value;
            if (e.weapon_prefab_key == data.weaponPrefabKey
                || e.weapon_display_key == data.weaponDisplayKey
                || e.weapon_id == data.weaponPrefabKey)
            {
                entry = e;
                break;
            }
        }

        if (entry == null) return;

        // 수치만 오버라이드 (SO 에셋 참조는 유지)
        data.baseAttack    = entry.base_attack;
        data.baseDefense   = entry.base_defense;
        data.attackSpeed   = entry.attack_speed;
        data.attackRange   = entry.attack_range;
        data.areaOfEffect  = entry.area_of_effect;
        data.holdThreshold = entry.hold_threshold;
        data.chargeStages  = entry.charge_stages;
        data.groundEndCount = entry.ground_combo_count;
        data.airEndCount   = entry.air_combo_count;

        Debug.Log($"[WeaponManager] 서버 수치 적용: {entry.weapon_name} (ATK:{entry.base_attack}, SPD:{entry.attack_speed})");
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
