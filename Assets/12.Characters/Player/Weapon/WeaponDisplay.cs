using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WeaponDisplay : MonoBehaviour
{
    private WeaponSO _so;

    public void Initialize(WeaponSO so)
    {
        _so = so;
        // UI 표시
        // 예: icon, 이름, 가격 등
        UpdateUI(so);
    }

    private void UpdateUI(WeaponSO so)
    {
        // 아이콘, 이름 등
    }

    public WeaponSO GetWeaponSO() => _so;
}
