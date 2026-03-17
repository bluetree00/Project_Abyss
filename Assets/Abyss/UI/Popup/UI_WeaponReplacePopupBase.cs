using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 무기 교체 팝업 공통 베이스.
/// 새 무기 패널 / 현재 무기 패널 / 교체·취소 버튼으로 구성.
/// 하위 클래스에서 타입별 추가 정보(ATK / 스킬 설명)를 바인딩합니다.
/// </summary>
public abstract class UI_WeaponReplacePopupBase : UI_Popup
{
    [Header("새 장비 패널")]
    [SerializeField] protected Image    newWeaponIcon;
    [SerializeField] protected TMP_Text newWeaponName;

    [Header("현재 장비 패널")]
    [SerializeField] protected Image    curWeaponIcon;
    [SerializeField] protected TMP_Text curWeaponName;

    [Header("버튼")]
    [SerializeField] protected Button confirmButton;
    [SerializeField] protected Button cancelButton;

    protected UniTaskCompletionSource<bool> _tcs;
    protected int _targetSlot;

    public virtual void Setup(WeaponData newWeapon, WeaponData currentWeapon, int targetSlot)
    {
        _targetSlot = targetSlot;
        _tcs = new UniTaskCompletionSource<bool>();

        ApplyIcon(newWeaponIcon, newWeapon?.icon);
        SetText(newWeaponName,   newWeapon?.displayName ?? "");

        ApplyIcon(curWeaponIcon, currentWeapon?.icon);
        SetText(curWeaponName,   currentWeapon?.displayName ?? "빈 슬롯");

        confirmButton?.onClick.RemoveAllListeners();
        confirmButton?.onClick.AddListener(() => Complete(true));

        cancelButton?.onClick.RemoveAllListeners();
        cancelButton?.onClick.AddListener(() => Complete(false));
    }

    /// <summary>교체 확정 여부 대기. true = 교체, false = 취소</summary>
    public UniTask<bool> WaitForChoiceAsync() => _tcs.Task;

    protected void Complete(bool confirmed)
    {
        _tcs?.TrySetResult(confirmed);
        ClosePopupUI();
    }

    protected void ApplyIcon(Image img, Sprite sprite)
    {
        if (img == null) return;
        img.sprite = sprite;
        img.gameObject.SetActive(sprite != null);
    }

    protected void SetText(TMP_Text label, string value)
    {
        if (label != null) label.text = value;
    }
}
