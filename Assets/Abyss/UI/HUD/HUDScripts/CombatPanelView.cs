//============================================================
// CombatPanelView.cs
// - HP 슬라이더/텍스트
// - 공격력 텍스트
// - 장비 슬롯 × 2 (아이콘, 이름, 공격력)
//============================================================
using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class CombatPanelView : MonoBehaviour
{
    [Header("Status — HP")]
    [SerializeField] private Slider   hpSlider;
    [SerializeField] private TMP_Text hpText;

    [Header("Status — Attack")]
    [SerializeField] private TMP_Text attackText;

    [Header("Weapon Slots")]
    [SerializeField] private WeaponSlotUI slot0;
    [SerializeField] private WeaponSlotUI slot1;

    // ─────────────────────────────────────────────────────────
    // HP
    // ─────────────────────────────────────────────────────────
    public void SetHp(int hp, int maxHp)
    {
        if (hpSlider != null)
        {
            hpSlider.maxValue = Mathf.Max(1, maxHp);
            hpSlider.value    = Mathf.Clamp(hp, 0, maxHp);
        }
        if (hpText != null)
            hpText.text = $"{hp} / {maxHp}";
    }

    // ─────────────────────────────────────────────────────────
    // 공격력
    // ─────────────────────────────────────────────────────────
    public void SetAttack(int attack)
    {
        if (attackText != null)
            attackText.text = $"ATK {attack}";
    }

    // ─────────────────────────────────────────────────────────
    // 장비 슬롯
    // ─────────────────────────────────────────────────────────
    public void SetWeaponSlot(int index, WeaponSlotInfo info)
    {
        var ui = index == 0 ? slot0 : index == 1 ? slot1 : null;
        ui?.Apply(info);
    }

    // ─────────────────────────────────────────────────────────
    // 슬롯 UI 단위 (Inspector 바인딩)
    // ─────────────────────────────────────────────────────────
    [Serializable]
    public sealed class WeaponSlotUI
    {
        [SerializeField] private GameObject emptyRoot;    // 장비 없을 때 표시할 빈 슬롯 이미지
        [SerializeField] private Image      iconImage;
        [SerializeField] private TMP_Text   nameText;
        [SerializeField] private TMP_Text   attackText;

        public void Apply(WeaponSlotInfo info)
        {
            // emptyRoot: 장비 없을 때 표시하는 빈 슬롯 이미지
            SetActive(emptyRoot, !info.HasWeapon);

            // 개별 요소는 장비가 있을 때만 표시
            if (iconImage != null)
            {
                iconImage.gameObject.SetActive(info.HasWeapon && info.Icon != null);
                if (info.HasWeapon) iconImage.sprite = info.Icon;
            }
            if (nameText   != null) nameText.gameObject.SetActive(info.HasWeapon);
            if (attackText != null) attackText.gameObject.SetActive(info.HasWeapon);

            if (!info.HasWeapon) return;

            if (nameText   != null) nameText.text  = info.Name;
            if (attackText != null) attackText.text = $"ATK {info.Attack:F0}";
        }

        private static void SetActive(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }
    }
}
