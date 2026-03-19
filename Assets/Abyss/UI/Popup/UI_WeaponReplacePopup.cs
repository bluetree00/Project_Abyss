using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 슬롯이 꽉 찼을 때 어느 장비를 버릴지 선택하는 팝업
/// PlayerWeaponManager.ShowReplacePromptAsync에서 호출
/// </summary>
public class UI_WeaponReplacePopup : UI_Popup
{
    [Header("새 무기")]
    [SerializeField] private Image    newWeaponIcon;
    [SerializeField] private TMP_Text newWeaponName;
    [SerializeField] private TMP_Text newWeaponAtk;

    [Header("슬롯 0")]
    [SerializeField] private Image    slot0Icon;
    [SerializeField] private TMP_Text slot0Name;
    [SerializeField] private TMP_Text slot0Atk;
    [SerializeField] private Button   slot0Button;

    [Header("슬롯 1")]
    [SerializeField] private Image    slot1Icon;
    [SerializeField] private TMP_Text slot1Name;
    [SerializeField] private TMP_Text slot1Atk;
    [SerializeField] private Button   slot1Button;

    [Header("취소")]
    [SerializeField] private Button cancelButton;

    private UniTaskCompletionSource<int?> _tcs;

    /// <summary>
    /// 팝업 데이터 세팅 후 WaitForChoiceAsync로 결과 대기
    /// </summary>
    public void Setup(WeaponData newWeapon, PlayerWeaponManager.WeaponSlot[] slots)
    {
        // 새 무기
        ApplyWeaponDisplay(newWeaponIcon, newWeaponName, newWeaponAtk,
            newWeapon.icon, newWeapon.displayName, newWeapon.baseAttack);

        // 슬롯 0 / 1
        ApplySlotDisplay(slot0Icon, slot0Name, slot0Atk, slots.Length > 0 ? slots[0] : null);
        ApplySlotDisplay(slot1Icon, slot1Name, slot1Atk, slots.Length > 1 ? slots[1] : null);

        _tcs = new UniTaskCompletionSource<int?>();

        slot0Button?.onClick.RemoveAllListeners();
        slot0Button?.onClick.AddListener(() => Complete(0));

        slot1Button?.onClick.RemoveAllListeners();
        slot1Button?.onClick.AddListener(() => Complete(1));

        cancelButton?.onClick.RemoveAllListeners();
        cancelButton?.onClick.AddListener(() => Complete(null));
    }

    /// <summary>
    /// 선택 결과를 기다린다. 반환값: 버릴 슬롯 인덱스(0/1) 또는 null(취소)
    /// </summary>
    public UniTask<int?> WaitForChoiceAsync() => _tcs.Task;

    private void Complete(int? result)
    {
        _tcs?.TrySetResult(result);
        ClosePopupUI();
    }

    private void ApplyWeaponDisplay(Image icon, TMP_Text nameText, TMP_Text atkText,
        Sprite sprite, string displayName, float atk)
    {
        if (icon != null)
        {
            icon.sprite = sprite;
            icon.gameObject.SetActive(sprite != null);
        }
        if (nameText != null) nameText.text = displayName;
        if (atkText  != null) atkText.text  = $"ATK {atk:F0}";
    }

    private void ApplySlotDisplay(Image icon, TMP_Text nameText, TMP_Text atkText,
        PlayerWeaponManager.WeaponSlot slot)
    {
        bool has = slot != null && !slot.IsEmpty && slot.runtimeData != null;
        ApplyWeaponDisplay(
            icon, nameText, atkText,
            has ? slot.runtimeData.icon        : null,
            has ? slot.runtimeData.displayName : "빈 슬롯",
            has ? slot.runtimeData.baseAttack  : 0f);
    }
}
