using UnityEngine;

/// <summary>
/// 서브 무기 템플릿 (Slot 1)
/// Q스킬 전용
/// </summary>
[CreateAssetMenu(menuName = "Game/Weapon/SubWeaponSO")]
public class SubWeaponSO : WeaponSO
{
    [Header("서브 스킬 쿨다운 (초)")]
    public float skillQCooldown = 8f;

    private void OnValidate()
    {
        slotType = WeaponSlotType.Sub;
    }
}
