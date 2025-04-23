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
        currentSlotIndex = 0;
        currentWeapon = null;
        isWeaponEquipped = false;
    }

    /// <summary>
    /// 특정 슬롯에 무기 장착 //TODO장착될때 풀러 패키지를 등록해야함
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
            currentSlotIndex = slotIndex;
            currentWeapon = weaponSlots[slotIndex];
            isWeaponEquipped = true;
        }

        SpawnWeaponObject(); // 무기 전환 후 자동 생성
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
    /// 현재 무기를 오브젝트로 스폰하여 손에 붙임
    /// </summary>
     public void SpawnWeaponObject()
    {
        if (currentWeapon != null)
        {
            if (currentWeaponObject != null)
            {
                Destroy(currentWeaponObject);
            }

            string weaponObjName = currentWeapon.weaponKey.ToString();  // weaponKey를 사용
          

            AddressablesManager.Instance.InstantiateAsync(weaponObjName, instance =>
            {
                currentWeaponObject = instance;
                isWeaponEquipped = true;        //무기 장착 확인
                if (currentWeaponObject != null)
                {
                    currentWeaponObject.transform.SetParent(weaponHandTransform);
                    currentWeaponObject.transform.localPosition = Vector3.zero;
                    currentWeaponObject.transform.localRotation = Quaternion.identity;

                    Debug.Log($"무기 {weaponObjName}가 성공적으로 생성되었습니다.");
                }
                else
                {
                    Debug.LogError($"무기 {weaponObjName} 생성에 실패했습니다.");
                }
            });
        }
        else
        {
            Debug.LogError("현재 무기가 null 입니다.");
        }
    }


}
