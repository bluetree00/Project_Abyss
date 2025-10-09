// PlayerWeaponManager_WithPool_Addressables.cs
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// 풀러(ObjectPoolerManager)를 사용하도록 개선된 PlayerWeaponManager
/// - 슬롯 2개 관리
/// - 풀러에서 무기 인스턴스 Spawn/Return 사용
/// - 풀러 미설정 시 Addressables.InstantiateAsync 폴백
/// - 즉시 교체 정책 (Immediate equip)
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

    [Header("Slots")]
    public WeaponSlot[] slots = new WeaponSlot[2];

    // 플레이어가 가진 무기 목록(인벤토리). 런타임 복사본을 저장.
    private List<WeaponData> _owned = new List<WeaponData>();

    // 외부 풀러 레퍼런스 (null이면 폴백으로 Addressables 사용)
    private ObjectPoolerManager _objectPoolerManager = null;

    // 이벤트
    public event Action<int, WeaponData> OnEquip;
    public event Action<int> OnUnequip;
    public event Action<int, int> OnSwap;

    private void Awake()
    {
        for (int i = 0; i < SlotCount; i++)
            slots[i] = new WeaponSlot();
    }

    #region Pooler 설정 API

    public void SetObjectPooler(ObjectPoolerManager pooler)
    {
        _objectPoolerManager = pooler;
    }

    #endregion

    #region Public API (획득/장착/언장착 등)

    public async UniTask AcquireWeaponAsync(string weaponSOKey, bool autoEquip = true)
    {
        if (string.IsNullOrEmpty(weaponSOKey))
        {
            Debug.LogError("AcquireWeaponAsync: weaponSOKey가 비어있음");
            return;
        }

        // Addressables에서 SO 로드
        var handle = Addressables.LoadAssetAsync<WeaponSO>(weaponSOKey);
        await handle.Task;

        if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
        {
            Debug.LogError($"AcquireWeaponAsync: WeaponSO 로드 실패 - {weaponSOKey}");
            return;
        }

        var runtime = new WeaponData(handle.Result);
        _owned.Add(runtime);

        if (autoEquip)
        {
            int empty = GetFirstEmptySlotIndex();
            if (empty >= 0)
                await EquipToSlotAsync(empty, runtime);
        }
    }

    public async UniTask EquipToSlotAsync(int slotIndex, WeaponData runtimeData)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount) throw new ArgumentOutOfRangeException(nameof(slotIndex));
        if (runtimeData == null) throw new ArgumentNullException(nameof(runtimeData));

        // 1) 기존 장비 언장착
        await UnequipSlotAsync(slotIndex);

        // 2) Spawn 위치/회전
        Transform attach = GetWeaponAttachTransform(slotIndex);
        Vector3 spawnPos = attach != null ? attach.position : this.transform.position;
        Quaternion spawnRot = attach != null ? attach.rotation : Quaternion.identity;

        GameObject go = null;

        // 3) 풀러에서 Spawn
        if (_objectPoolerManager != null)
        {
            try
            {
                go = _objectPoolerManager.SpawnWeaponFromPool(runtimeData.weaponKey, spawnPos, spawnRot);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Pool spawn 실패({runtimeData.weaponKey}), Addressables.InstantiateAsync 사용. Exception: {e.Message}");
            }
        }

        // 4) 풀러 없거나 실패하면 Addressables.InstantiateAsync 사용
        if (go == null)
        {
            if (string.IsNullOrEmpty(runtimeData.prefabKey))
            {
                Debug.LogWarning($"EquipToSlotAsync: 무기 {runtimeData.weaponKey}에 prefabKey 없음. 데이터만 슬롯에 등록.");
                slots[slotIndex].runtimeData = runtimeData;
                slots[slotIndex].instance = null;
                OnEquip?.Invoke(slotIndex, runtimeData);
                return;
            }

            try
            {
                var handle = Addressables.InstantiateAsync(runtimeData.prefabKey, spawnPos, spawnRot);
                await handle.Task;

                if (handle.Status == AsyncOperationStatus.Succeeded)
                    go = handle.Result;
                else
                    Debug.LogError($"EquipToSlotAsync: Addressables.InstantiateAsync 실패 - {runtimeData.prefabKey}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"EquipToSlotAsync: Addressables.InstantiateAsync Exception - {runtimeData.prefabKey}, {ex.Message}");
            }
        }

        if (go == null)
        {
            slots[slotIndex].runtimeData = runtimeData;
            slots[slotIndex].instance = null;
            OnEquip?.Invoke(slotIndex, runtimeData);
            return;
        }

        // 5) 부모 설정 및 초기화
        if (attach != null)
        {
            go.transform.SetParent(attach, worldPositionStays: true);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
        }
        else
            go.transform.SetParent(this.transform, worldPositionStays: true);

        var inst = go.GetComponent<WeaponInstance>() ?? go.AddComponent<WeaponInstance>();
        inst.Initialize(runtimeData);
        inst.OnEquip();

        slots[slotIndex].runtimeData = runtimeData;
        slots[slotIndex].instance = go;
        OnEquip?.Invoke(slotIndex, runtimeData);
    }

    public async UniTask UnequipSlotAsync(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount) throw new ArgumentOutOfRangeException(nameof(slotIndex));

        var slot = slots[slotIndex];
        if (slot.instance != null)
        {
            bool returnedToPool = false;

            if (_objectPoolerManager != null)
            {
                try
                {
                    _objectPoolerManager.ReturnToPool(slot.instance);
                    returnedToPool = true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"UnequipSlotAsync: ReturnToPool 실패, Destroy 폴백. Exception: {e.Message}");
                }
            }

            if (!returnedToPool)
                Destroy(slot.instance);

            slot.instance = null;
        }

        slot.runtimeData = null;
        OnUnequip?.Invoke(slotIndex);
        await UniTask.CompletedTask;
    }

    public async UniTask SwapSlotsAsync(int a, int b)
    {
        if (a == b) return;
        if (a < 0 || a >= SlotCount || b < 0 || b >= SlotCount) return;

        var tmpData = slots[a].runtimeData;
        var tmpInst = slots[a].instance;

        slots[a].runtimeData = slots[b].runtimeData;
        slots[a].instance = slots[b].instance;
        if (slots[a].instance != null)
            slots[a].instance.transform.SetParent(GetWeaponAttachTransform(a), false);

        slots[b].runtimeData = tmpData;
        slots[b].instance = tmpInst;
        if (slots[b].instance != null)
            slots[b].instance.transform.SetParent(GetWeaponAttachTransform(b), false);

        OnSwap?.Invoke(a, b);
        await UniTask.CompletedTask;
    }

    #endregion

    #region Pickup / Replace

    public async UniTask HandlePickupAsync(string weaponSOKey, bool autoEquip = true)
    {
        if (string.IsNullOrEmpty(weaponSOKey)) return;

        var handle = Addressables.LoadAssetAsync<WeaponSO>(weaponSOKey);
        await handle.Task;

        if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
        {
            Debug.LogError($"HandlePickupAsync: WeaponSO 로드 실패 - {weaponSOKey}");
            return;
        }

        var newRuntime = new WeaponData(handle.Result);
        int empty = GetFirstEmptySlotIndex();

        if (empty >= 0)
        {
            _owned.Add(newRuntime);
            if (autoEquip) await EquipToSlotAsync(empty, newRuntime);
            else AddToInventoryOnly(newRuntime);
            return;
        }

        int? chosen = await ShowReplacePromptAsync(newRuntime);
        if (chosen == null)
        {
            Debug.Log("HandlePickupAsync: 플레이어가 획득을 취소함");
            return;
        }

        await ReplaceSlotAsync(chosen.Value, newRuntime);
    }

    public async UniTask ReplaceSlotAsync(int slotIndex, WeaponData newRuntime)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount) throw new ArgumentOutOfRangeException(nameof(slotIndex));
        var slot = slots[slotIndex];

        if (slot.runtimeData != null)
            SpawnDroppedWeaponAtPlayer(slot.runtimeData);

        if (slot.instance != null)
        {
            try { _objectPoolerManager?.ReturnToPool(slot.instance); }
            catch { Destroy(slot.instance); }
            slot.instance = null;
        }

        _owned.Add(newRuntime);
        await EquipToSlotAsync(slotIndex, newRuntime);
    }

    #endregion

    #region Helpers / UI Stubs

    private Transform GetWeaponAttachTransform(int slotIndex) => this.transform;

    private void SpawnDroppedWeaponAtPlayer(WeaponData oldData)
    {
        Debug.Log($"SpawnDroppedWeaponAtPlayer: '{oldData.displayName}' 드랍(실제 구현 필요).");
    }

    private async UniTask<int?> ShowReplacePromptAsync(WeaponData newWeapon)
    {
        Debug.Log($"ShowReplacePromptAsync: 인벤토리가 가득 찼습니다. '{newWeapon.displayName}' 교체 시뮬레이션.");
        await UniTask.Delay(TimeSpan.FromSeconds(1));
        return 0;
    }

    private void AddToInventoryOnly(WeaponData runtime)
    {
        _owned.Add(runtime);
    }

    #endregion

    #region Utility

    public WeaponData GetCurrentWeapon(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount) return null;
        return slots[slotIndex].runtimeData;
    }

    public IReadOnlyList<WeaponData> GetOwnedWeapons() => _owned.AsReadOnly();

    public int GetFirstEmptySlotIndex()
    {
        for (int i = 0; i < SlotCount; i++)
            if (slots[i].IsEmpty) return i;
        return -1;
    }

    #endregion
}
