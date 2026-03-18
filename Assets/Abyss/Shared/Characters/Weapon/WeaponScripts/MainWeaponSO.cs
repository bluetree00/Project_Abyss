using UnityEngine;

/// <summary>
/// 메인 무기 템플릿 (Slot 0)
/// 일반 공격 + E스킬 + R스킬 담당
/// </summary>
[CreateAssetMenu(menuName = "Game/Weapon/MainWeaponSO")]
public class MainWeaponSO : WeaponSO
{
    [Header("메인 스킬 쿨다운 (초)")]
    public float skillECooldown = 5f;
    public float skillRCooldown = 10f;

    private void OnValidate()
    {
        slotType = WeaponSlotType.Main;
    }
}
