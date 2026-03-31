using UnityEngine;

/// <summary>
/// 메인 무기 템플릿 (Slot 0 / Slot 1 공용)
/// 일반 공격 + Q스킬 + E스킬 담당
/// 쿨다운은 SkillSO에서 관리
/// </summary>
[CreateAssetMenu(menuName = "Game/Weapon/MainWeaponSO")]
public class MainWeaponSO : WeaponSO
{
    private void OnValidate()
    {
        slotType = WeaponSlotType.Main;
    }
}
