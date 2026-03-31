using UnityEngine;

/// <summary>
/// 메인 무기 템플릿 (Slot 0 / Slot 1 공용)
/// 일반 공격 + Q스킬 + E스킬 담당
/// </summary>
[CreateAssetMenu(menuName = "Game/Weapon/MainWeaponSO")]
public class MainWeaponSO : WeaponSO
{
    [Header("스킬 쿨다운 (초)")]
    public float skillQCooldown = 8f;
    public float skillECooldown = 5f;

    private void OnValidate()
    {
        slotType = WeaponSlotType.Main;
    }
}
