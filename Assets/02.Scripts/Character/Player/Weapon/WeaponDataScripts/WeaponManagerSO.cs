using UnityEngine;
using System;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewWeaponManager", menuName = "Managers/WeaponManagerSO", order = 1)]
public class WeaponManagerSO : ScriptableObject
{
    [SerializeField] private WeaponData[] weaponSlots = new WeaponData[2];
    public int SlotCount => weaponSlots.Length;

    [SerializeField] private int currentSlotIndex = 0;
    [SerializeField] private WeaponData currentWeapon;
    public WeaponData CurrentWeapon => currentWeapon;

    [SerializeField] private GameObject currentWeaponObject;
    public GameObject CurrentWeaponObject => currentWeaponObject;

    [SerializeField] private bool isWeaponEquipped = false;
    public bool IsWeaponEquipped => isWeaponEquipped;

    public Transform weaponHandTransform;

    private GameObject[] weaponObjects;
    private Dictionary<string, AnimatorOverrideController> cachedAnimators = new();

    private RuntimeAnimatorController defaultController;

    public event Action OnWeaponEquippedEvent;

    public void Initialize(int slotSize, Animator animator)
    {
        weaponSlots = new WeaponData[slotSize];
        weaponObjects = new GameObject[slotSize];
        currentSlotIndex = 0;
        currentWeapon = null;
        currentWeaponObject = null;
        isWeaponEquipped = false;

        defaultController = animator.runtimeAnimatorController;
    }

    public void EquipWeapon(WeaponData newWeapon, int slotIndex, Animator animator)
    {
        if (!IsValidSlot(slotIndex) || newWeapon == null) return;

          // 🔸 무기 능력 초기화
        var controller = weaponHandTransform.GetComponentInParent<CharacterController>();
        controller?.ClearWeaponAbilities();

        // 기존 장착 무기 비활성화
        if (slotIndex == currentSlotIndex)
        {
            if (currentWeaponObject != null)
                currentWeaponObject.SetActive(false);

            ClearWeaponAnimations(animator);
            currentWeaponObject = null; // 명확하게 null 처리
        }

        weaponSlots[slotIndex] = newWeapon;

        if (slotIndex == currentSlotIndex)
        {
            currentWeapon = newWeapon;
            isWeaponEquipped = true;
            ActivateWeaponInSlot(slotIndex, animator);
            OnWeaponEquippedEvent?.Invoke(); // 이벤트 호출
        }
        else
        {
            SpawnWeaponObject(slotIndex, animator);
        }
    }

    public void SwitchWeapon(int slotIndex, Animator animator)
    {
        if (!IsValidSlot(slotIndex) || weaponSlots[slotIndex] == null) return;

            // 🔸 무기 능력 초기화
        var controller = weaponHandTransform.GetComponentInParent<CharacterController>();
        controller?.ClearWeaponAbilities();

        // 기존 무기 비활성화
        if (currentWeaponObject != null)
        {
            currentWeaponObject.SetActive(false);
        }

        ClearWeaponAnimations(animator);

        currentSlotIndex = slotIndex;
        currentWeapon = weaponSlots[slotIndex];
        isWeaponEquipped = true;
        OnWeaponEquippedEvent?.Invoke(); // 이벤트 호출
        // 새로운 슬롯에 무기 오브젝트가 존재하면 활성화하고 애니메이션 적용
        if (weaponObjects[slotIndex] != null)
        {
            weaponObjects[slotIndex].SetActive(true);
            currentWeaponObject = weaponObjects[slotIndex];
            ApplyWeaponAnimations(currentWeapon, animator);
        }
        else
        {
            SpawnWeaponObject(slotIndex, animator);
        }
    }

    public void UnequipCurrentWeapon(Animator animator)
    {
        if (currentWeaponObject != null)
        {
            currentWeaponObject.SetActive(false);
        }

        ClearWeaponAnimations(animator);
        currentWeapon = null;
        currentWeaponObject = null;
        isWeaponEquipped = false;
    }

    public WeaponData GetWeaponAtSlot(int slotIndex)
    {
        return IsValidSlot(slotIndex) ? weaponSlots[slotIndex] : null;
    }

    public int GetCurrentSlotIndex() => currentSlotIndex;

    public bool HasEmptySlot()
    {
        foreach (var w in weaponSlots)
            if (w == null) return true;
        return false;
    }

    public int GetFirstEmptySlotIndex()
    {
        for (int i = 0; i < weaponSlots.Length; i++)
            if (weaponSlots[i] == null) return i;
        return -1;
    }

    private void SpawnWeaponObject(int slotIndex, Animator animator = null)
{
    var weapon = weaponSlots[slotIndex];
    if (weapon == null) return;

    string key = weapon.weaponKey.ToString();

    // 이미 생성된 무기 오브젝트가 있으면 생성하지 않음
    if (weaponObjects[slotIndex] != null) return;

    Managers.AddressableManager.InstantiateAsync(key, instance =>
    {
        // 슬롯 무기 데이터가 바뀌었을 수 있으므로 다시 확인
        if (weaponSlots[slotIndex]?.weaponKey.ToString() != key) return;

        // 무기 객체가 중복 생성되는 것을 방지
        if (weaponObjects[slotIndex] != null) return;

        weaponObjects[slotIndex] = instance;

        instance.transform.SetParent(weaponHandTransform);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;

        if (slotIndex == currentSlotIndex)
        {
            currentWeaponObject = instance;
            currentWeaponObject.SetActive(true);

            if (animator != null)
                ApplyWeaponAnimations(weaponSlots[slotIndex], animator);
        }
        else
        {
            instance.SetActive(false);
        }
    });
}

    private void ActivateWeaponInSlot(int slotIndex, Animator animator)
    {
        // 이미 생성된 무기 오브젝트가 있으면 활성화, 없으면 생성
        if (weaponObjects[slotIndex] != null)
        {
            weaponObjects[slotIndex].SetActive(true);
            currentWeaponObject = weaponObjects[slotIndex];
        }
        else
        {
            SpawnWeaponObject(slotIndex, animator);
        }

        ApplyWeaponAnimations(weaponSlots[slotIndex], animator);
    }

    public void ApplyWeaponAnimations(WeaponData weapon, Animator animator)
    {
        ApplyLightAttackAnimations(weapon, animator);
        ApplyHeavyAttackAnimations(weapon, animator);
    }

    private void ApplyLightAttackAnimations(WeaponData weapon, Animator animator)
    {
        if (weapon == null || animator == null || weapon.lightAttackAnimationSetSO == null) return;

        string key = weapon.weaponKey.ToString();

        // 기존 오버라이드 컨트롤러 재사용 또는 생성
        if (!cachedAnimators.TryGetValue(key, out var overrideController))
        {
            overrideController = new AnimatorOverrideController(defaultController);
            cachedAnimators[key] = overrideController;
        }

        string[] attackKeys = GetSortedAttackKeys(defaultController, "NormalAttack_");

        for (int i = 0; i < attackKeys.Length; i++)
        {
            if (i < weapon.lightAttackAnimationSetSO.attackAnimations.Count &&
                weapon.lightAttackAnimationSetSO.attackAnimations[i] != null)
            {
                overrideController[attackKeys[i]] = weapon.lightAttackAnimationSetSO.attackAnimations[i];
            }
        }

        // 애니메이터에 적용
        animator.runtimeAnimatorController = overrideController;
    }


    private void ApplyHeavyAttackAnimations(WeaponData weapon, Animator animator)
    {
        if (weapon == null || animator == null || weapon.heavyAttackSet == null) return;

        string key = weapon.weaponKey.ToString();

        // 이미 light에서 오버라이드한 컨트롤러가 있는 경우 재사용
        if (!cachedAnimators.TryGetValue(key, out var overrideController))
        {
            overrideController = new AnimatorOverrideController(defaultController);
            cachedAnimators[key] = overrideController;
        }

        var heavySet = weapon.heavyAttackSet;

        if (heavySet.HeavyAttackAnimations.Length >= 3)
        {
            overrideController[heavySet.HeavyAttackAnimations[0]] = heavySet.chargeClip;
            overrideController[heavySet.HeavyAttackAnimations[1]] = heavySet.attackClip;
            overrideController[heavySet.HeavyAttackAnimations[2]] = heavySet.endClip;
        }

        // 현재 애니메이터 컨트롤러를 다시 설정
        animator.runtimeAnimatorController = overrideController;
    }





    private void ClearWeaponAnimations(Animator animator)
    {
        if (animator == null || defaultController == null) return;
        animator.runtimeAnimatorController = defaultController;
    }

    private string[] GetSortedAttackKeys(RuntimeAnimatorController controller, string prefix)
    {
        var clips = controller.animationClips;
        var keys = new List<string>();

        foreach (var clip in clips)
            if (clip.name.StartsWith(prefix))
                keys.Add(clip.name);

        keys.Sort((a, b) =>
        {
            int.TryParse(a.Substring(prefix.Length), out int aNum);
            int.TryParse(b.Substring(prefix.Length), out int bNum);
            return aNum.CompareTo(bNum);
        });

        return keys.ToArray();
    }

    private bool IsValidSlot(int index) =>
        index >= 0 && index < weaponSlots.Length;
}
