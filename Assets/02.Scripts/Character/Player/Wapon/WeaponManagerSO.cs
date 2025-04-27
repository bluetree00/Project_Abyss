using UnityEngine;

[CreateAssetMenu(fileName = "NewWeaponManager", menuName = "Managers/WeaponManagerSO", order = 1)]
public class WeaponManagerSO : ScriptableObject
{
    // 무기 슬롯들 (예: 0번: 주무기, 1번: 보조무기 등)
    [SerializeField] private WeaponData[] weaponSlots = new WeaponData[2];

    // 현재 선택된 슬롯 인덱스
    [SerializeField] private int currentSlotIndex = 0;

    // 현재 장착 중인 무기
    [SerializeField] private WeaponData currentWeapon;
    public WeaponData CurrentWeapon => currentWeapon;

    [SerializeField] private bool isWeaponEquipped = false;
    public bool IsWeaponEquipped => isWeaponEquipped;

    // 무기 슬롯 수
    public int SlotCount => weaponSlots.Length;

    // 각 슬롯에 대한 무기 오브젝트
    private GameObject[] weaponObjects;

    // 손의 트랜스폼을 저장할 변수
    public Transform weaponHandTransform;

    [SerializeField]
    private GameObject currentWeaponObject;
    public GameObject CurrentWeaponObject { get { return currentWeaponObject; } }

    /// <summary>
    /// 초기화용 (런타임에서 ScriptableObject 복제 후 초기화에 사용)
    /// </summary>
     public void Initialize(int slotSize)
    {
        weaponSlots = new WeaponData[slotSize];
        weaponObjects = new GameObject[slotSize]; // 각 슬롯에 대응되는 무기 오브젝트 배열 초기화
        currentSlotIndex = 0;
        currentWeapon = null;
        isWeaponEquipped = false;

        // 각 슬롯에 대응되는 무기 객체 초기화
        for (int i = 0; i < weaponSlots.Length; i++)
        {
            if (weaponSlots[i] != null)
            {
                SpawnWeaponObject(i);  // 각 슬롯에 대해 초기화
            }
        }
    }

    /// <summary>
    /// 특정 슬롯에 무기 장착
    /// </summary>
    public void EquipWeapon(WeaponData newWeapon, int slotIndex)
    {
        if (IsValidSlot(slotIndex))
        {
            weaponSlots[slotIndex] = newWeapon;

            // 현재 슬롯이라면 즉시 장착
            if (slotIndex == currentSlotIndex)
            {
                currentWeapon = newWeapon;
                isWeaponEquipped = true;
            }
        }
    }

    /// <summary>
    /// 무기 해제
    /// </summary>
    public void UnequipWeapon(int slotIndex)
    {
        if (IsValidSlot(slotIndex))
        {
            weaponSlots[slotIndex] = null;

            if (slotIndex == currentSlotIndex)
            {
                currentWeapon = null;
                isWeaponEquipped = false;
            }
        }
    }

    /// <summary>
    /// 무기 슬롯 전환
    /// </summary>
    public void SwitchWeapon(int slotIndex)
    {
        if (IsValidSlot(slotIndex) && weaponSlots[slotIndex] != null)
        {
            // 현재 활성화된 무기를 비활성화
            if (weaponObjects[currentSlotIndex] != null)
            {
                weaponObjects[currentSlotIndex].SetActive(false);
            }

            // 새로 선택한 슬롯의 무기를 활성화
            currentSlotIndex = slotIndex;
            currentWeapon = weaponSlots[slotIndex];
            isWeaponEquipped = true;
            ActivateWeaponInSlot(slotIndex);  // 해당 슬롯의 무기 활성화
        }
    }

    /// <summary>
    /// 슬롯 내 무기 데이터 조회
    /// </summary>
    public WeaponData GetWeaponAtSlot(int slotIndex)
    {
        if (IsValidSlot(slotIndex))
            return weaponSlots[slotIndex];

        return null;
    }

    /// <summary>
    /// 현재 슬롯 인덱스 반환
    /// </summary>
    public int GetCurrentSlotIndex()
    {
        return currentSlotIndex;
    }

    /// <summary>
    /// 슬롯 인덱스 유효성 검사
    /// </summary>
    private bool IsValidSlot(int index)
    {
        return index >= 0 && index < weaponSlots.Length;
    }

    public bool HasEmptySlot()
    {
        foreach (var slot in weaponSlots)
        {
            if (slot == null)
                return true;
        }
        return false;
    }

    public int GetFirstEmptySlotIndex()
    {
        for (int i = 0; i < weaponSlots.Length; i++)
        {
            if (weaponSlots[i] == null)
                return i;
        }
        return -1; // 없으면 -1
    }

        /// <summary>
        /// 슬롯에 해당하는 무기 오브젝트를 생성하여 활성화
        /// </summary>
        private void SpawnWeaponObject(int slotIndex)
        {
            if (weaponSlots[slotIndex] != null)
            {
                // 이미 해당 슬롯에 무기 오브젝트가 있으면 비활성화 후 재활성화
                if (weaponObjects[slotIndex] != null)
                {
                    weaponObjects[slotIndex].SetActive(true);
                }
                else
                {
                    string weaponObjName = weaponSlots[slotIndex].weaponKey.ToString();  // weaponKey 사용
                    AddressablesManager.Instance.InstantiateAsync(weaponObjName, instance =>
                    {
                        weaponObjects[slotIndex] = instance;
                        if (weaponObjects[slotIndex] != null)
                        {
                            weaponObjects[slotIndex].transform.SetParent(weaponHandTransform);
                            weaponObjects[slotIndex].transform.localPosition = Vector3.zero;
                            weaponObjects[slotIndex].transform.localRotation = Quaternion.identity;

                            Debug.Log($"무기 {weaponObjName}가 성공적으로 생성되었습니다.");
                        }
                        else
                        {
                            Debug.LogError($"무기 {weaponObjName} 생성에 실패했습니다.");
                        }
                    });
                }
            }
            else
            {
                Debug.LogError("현재 무기가 null 입니다.");
            }
        }

         /// <summary>
    /// 특정 슬롯의 무기를 활성화
    /// </summary>
    private void ActivateWeaponInSlot(int slotIndex)
    {
        if (weaponObjects[slotIndex] != null)
        {
            weaponObjects[slotIndex].SetActive(true);
        }
        else
        {
            // 객체가 없다면 무기 초기화
            SpawnWeaponObject(slotIndex);
        }
    }


}
