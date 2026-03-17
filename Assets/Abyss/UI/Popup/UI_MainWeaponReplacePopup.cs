using TMPro;
using UnityEngine;

/// <summary>
/// 메인 무기 교체 팝업.
/// 추가 표시 정보: 공격력(ATK)
/// </summary>
public class UI_MainWeaponReplacePopup : UI_WeaponReplacePopupBase
{
    [Header("메인 무기 추가 정보")]
    [SerializeField] private TMP_Text newWeaponAtk;
    [SerializeField] private TMP_Text curWeaponAtk;

    public override void Setup(WeaponData newWeapon, WeaponData currentWeapon, int targetSlot)
    {
        base.Setup(newWeapon, currentWeapon, targetSlot);

        SetText(newWeaponAtk, newWeapon    != null ? $"ATK  {newWeapon.baseAttack:F0}"     : "");
        SetText(curWeaponAtk, currentWeapon != null ? $"ATK  {currentWeapon.baseAttack:F0}" : "");
    }
}
