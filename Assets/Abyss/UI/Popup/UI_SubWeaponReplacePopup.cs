using TMPro;
using UnityEngine;

/// <summary>
/// 서브 장비 교체 팝업.
/// 추가 표시 정보: Q스킬 이름 + 스킬 설명
/// </summary>
public class UI_SubWeaponReplacePopup : UI_WeaponReplacePopupBase
{
    [Header("서브 장비 추가 정보")]
    [SerializeField] private TMP_Text newSkillName;
    [SerializeField] private TMP_Text newSkillDesc;
    [SerializeField] private TMP_Text curSkillName;
    [SerializeField] private TMP_Text curSkillDesc;

    public override void Setup(WeaponData newWeapon, WeaponData currentWeapon, int targetSlot)
    {
        base.Setup(newWeapon, currentWeapon, targetSlot);

        SetText(newSkillName, newWeapon?.skillName        ?? "");
        SetText(newSkillDesc, newWeapon?.skillDescription ?? "");
        SetText(curSkillName, currentWeapon?.skillName        ?? "");
        SetText(curSkillDesc, currentWeapon?.skillDescription ?? "");
    }
}
