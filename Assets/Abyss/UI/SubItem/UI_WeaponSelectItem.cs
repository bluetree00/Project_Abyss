using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 무기 선택 PrepPanel의 개별 무기 카드 아이템.
/// UI_PrepPanel이 직접 Setup()을 호출합니다.
/// </summary>
public class UI_WeaponSelectItem : UI_Base
{
    [SerializeField] private Image    iconImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text atkText;
    [SerializeField] private GameObject selectedMark;
    [SerializeField] private Button   button;

    private WeaponRoster.WeaponEntry _entry;
    private Action<UI_WeaponSelectItem> _onSelected;

    public WeaponRoster.WeaponEntry Entry => _entry;

    public override void Init()
    {
        base.Init();
        if (button != null)
            button.onClick.AddListener(OnClick);
    }

    public void Setup(WeaponRoster.WeaponEntry entry, Action<UI_WeaponSelectItem> onSelected)
    {
        _entry      = entry;
        _onSelected = onSelected;

        var so   = entry?.data;
        var icon = (entry != null && entry.icon != null) ? entry.icon : so?.icon;

        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.color  = icon != null ? Color.white : Color.gray;
        }

        if (nameText != null) nameText.text = so != null ? so.displayName : "???";
        if (atkText  != null) atkText.text  = so != null ? $"ATK {so.baseAttack:0}" : "";

        SetSelected(false);
    }

    public void SetSelected(bool selected)
    {
        if (selectedMark != null)
            selectedMark.SetActive(selected);
    }

    private void OnClick() => _onSelected?.Invoke(this);
}
