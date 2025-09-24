using UnityEngine;


//NOTE: weaponSO에서 받은걸로 플레이어 적용 이전 장비에 라이드 헤비 어빌리를 넣는 방식이 아닌 플레이어의 공격에 맞는 어빌리티를 실행하는 방식으로 변경
//NOTE: 예를들어 플레이어가 한 공격이 ground count 1의 공격이라면 해당사항에 맞는 공격을 이벤트로 WeaponSO에서 끌어와서 실행하는 방식
// NOTE: 장비 매니저에서 애니메이션 오버라이드 하는 방식에서 장비 매니저에 함수를 파서 플레이어가 접근 호출하면 장비매니저에서 해당 현재 장비에 대한 내용을 전달
//NOTE: 플레이어는 클래스로 분리한 애니메이션 오버라이드 서비스 클래스를 통해서 자신의 애니메이션을 장비 애니메이션으로 오버라이드
//NOTE: 장비 어빌리티에는 애니메이션 이벤트에서 수치를 받아서 대응 하는 방식으로 변경 예를들어 처음 타이밍에 1 두번쨰 타이밍에 2 이런식으로
//NOTE: 애니메이션 이벤트에서 실제 값을 넘기는것을 수로 받아서 해당 공격에 대한 이펙트와 콜라이더를 생성하는 방식 이렇게 되면 애니메이션 하나에 여러가지 이펙트와 콜라이더 대응가능.
//NOTE: 애니메이션 오버라이드는 기본적으로 캐싱해두고 사용하지만 근거리 원거리 고정이라면 근거리와 원거리용을 따로 만들어 사용한다면 매번 오버라이드하는것 보다 성능적 메모리 절감 가능할듯. 

[CreateAssetMenu(fileName = "NewWeaponManager", menuName = "Managers/WeaponManagerSO", order = 1)]
public class WeaponManagerSO : ScriptableObject
{
    [SerializeField] private WeaponSO[] weaponSlots = new WeaponSO[2];
    public int SlotCount => weaponSlots.Length;

    [SerializeField] private int currentSlotIndex = 0;
    [SerializeField] private WeaponSO currentWeapon;
    public WeaponSO CurrentWeapon => currentWeapon;

    // ... keep rest (weaponObjects can still exist if you spawn prefab by weapon.prefabKey)

    public void EquipWeapon(WeaponSO newWeapon, int slotIndex, Animator animator)
    {
        if (!IsValidSlot(slotIndex) || newWeapon == null) return;

        // 기존 로직 유지 (Clear abilities etc.)
        weaponSlots[slotIndex] = newWeapon;

        if (slotIndex == currentSlotIndex)
        {
            currentWeapon = newWeapon;
            // spawn / activate object etc...
        }
        else
        {
            // lazy instantiate object for that slot if you want...
        }
    }

    public WeaponSO GetWeaponAtSlot(int slotIndex)
    {
        return IsValidSlot(slotIndex) ? weaponSlots[slotIndex] : null;
    }

    // ... other methods unchanged except types
    private bool IsValidSlot(int index) =>
        index >= 0 && index < weaponSlots.Length;
}
