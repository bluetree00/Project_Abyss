//============================================================
// CombatPanelView.cs
// - HP 슬라이더/텍스트
// - 장비 슬롯 × 2 (아이콘)
// - Q/E 스킬 슬롯 (아이콘, 쿨다운)
// - Active 슬롯 × 3
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
    [SerializeField] private Image    hpFillImage;

    [Header("Weapon Slots")]
    [SerializeField] private WeaponSlotUI slot0;
    [SerializeField] private WeaponSlotUI slot1;

    [Header("Skill Slots — Q / E")]
    [SerializeField] private SkillSlotUI skillQ;
    [SerializeField] private SkillSlotUI skillE;

    [Header("Active Slots")]
    [SerializeField] private ActiveSlotUI[] activeSlots = new ActiveSlotUI[3];

    // ─────────────────────────────────────────────────────────
    // HP
    // ─────────────────────────────────────────────────────────
    public void SetHp(int hp, int maxHp)
    {
        int clampedMax = Mathf.Max(1, maxHp);
        int clampedHp  = Mathf.Clamp(hp, 0, clampedMax);

        if (hpSlider != null)
        {
            hpSlider.minValue = 0f;
            hpSlider.maxValue = clampedMax;
            hpSlider.value = clampedHp;
            hpSlider.normalizedValue = clampedHp / (float)clampedMax;
        }

        var fill = hpFillImage;
        if (fill == null && hpSlider != null && hpSlider.fillRect != null)
            fill = hpSlider.fillRect.GetComponent<Image>();
        if (fill != null)
            fill.fillAmount = clampedHp / (float)clampedMax;

        if (hpText != null)
            hpText.text = $"{clampedHp} / {clampedMax}";
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
    // 스킬 슬롯
    // ─────────────────────────────────────────────────────────
    public void SetSkillIcon(SkillType skill, Sprite icon)
    {
        GetSkillSlot(skill)?.SetIcon(icon);
    }

    public void SetSkillCooldown(SkillType skill, float remaining, float total)
    {
        GetSkillSlot(skill)?.SetCooldown(remaining, total);
    }

    private SkillSlotUI GetSkillSlot(SkillType skill) => skill switch
    {
        SkillType.Q => skillQ,
        SkillType.E => skillE,
        _           => null,
    };

    // ─────────────────────────────────────────────────────────
    // Active 슬롯
    // ─────────────────────────────────────────────────────────
    public void SetActiveSlot(int index, Sprite icon)
    {
        if (index >= 0 && index < activeSlots.Length)
            activeSlots[index]?.SetIcon(icon);
    }

    // ─────────────────────────────────────────────────────────
    // 무기 슬롯 UI (Inspector 바인딩)
    // ─────────────────────────────────────────────────────────
    [Serializable]
    public sealed class WeaponSlotUI
    {
        [SerializeField] private GameObject emptyRoot;
        [SerializeField] private Image      iconImage;

        public void Apply(WeaponSlotInfo info)
        {
            SetActive(emptyRoot, !info.HasWeapon);

            if (iconImage != null)
            {
                iconImage.gameObject.SetActive(info.HasWeapon && info.Icon != null);
                if (info.HasWeapon && info.Icon != null) iconImage.sprite = info.Icon;
            }
        }

        private static void SetActive(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }
    }

    // ─────────────────────────────────────────────────────────
    // 스킬 슬롯 UI (Inspector 바인딩)
    // ─────────────────────────────────────────────────────────
    [Serializable]
    public sealed class SkillSlotUI
    {
        [SerializeField] private Image      iconImage;
        [SerializeField] private GameObject cooldownBg;
        [SerializeField] private TMP_Text   cooldownText;
        /// <summary>선택: Radial360 FillMethod 설정된 Image — 쿨다운 진행 오버레이.</summary>
        [SerializeField] private Image      cooldownOverlay;

        public void SetIcon(Sprite icon)
        {
            if (iconImage == null) return;
            iconImage.sprite = icon;
            iconImage.gameObject.SetActive(icon != null);
        }

        public void SetCooldown(float remaining, float total)
        {
            bool onCooldown = remaining > 0.05f;

            if (cooldownBg != null && cooldownBg.activeSelf != onCooldown)
                cooldownBg.SetActive(onCooldown);

            if (cooldownText != null)
                cooldownText.text = onCooldown ? Mathf.CeilToInt(remaining).ToString() : string.Empty;

            if (cooldownOverlay != null)
            {
                cooldownOverlay.gameObject.SetActive(onCooldown);
                cooldownOverlay.fillAmount = (onCooldown && total > 0f) ? remaining / total : 0f;
            }
        }
    }

    // ─────────────────────────────────────────────────────────
    // Active 슬롯 UI (Inspector 바인딩)
    // ─────────────────────────────────────────────────────────
    [Serializable]
    public sealed class ActiveSlotUI
    {
        [SerializeField] private Image iconImage;

        public void SetIcon(Sprite icon)
        {
            if (iconImage == null) return;
            iconImage.sprite = icon;
            iconImage.gameObject.SetActive(icon != null);
        }
    }
}
