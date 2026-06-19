using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 무기 선택(모루) 팝업 — 근거리(대검/카타나) 택1 + 원거리(보우/석궁) 택1 → 확정.
/// 모루 오브젝트가 옵션 WeaponSO를 주입하고 <see cref="WaitForChoiceAsync"/>로 결과를 받는다.
/// </summary>
public class UI_WeaponForgePopup : UI_Popup
{
    /// <summary>확정 결과. 취소 시 null.</summary>
    public struct ForgeChoice
    {
        public WeaponSO Melee;
        public WeaponSO Ranged;
    }

    [Header("근거리 옵션 0 (대검)")]
    [SerializeField] private Button melee0Button;
    [SerializeField] private Image melee0Icon;
    [SerializeField] private TMP_Text melee0Name;
    [SerializeField] private GameObject melee0Frame;

    [Header("근거리 옵션 1 (카타나)")]
    [SerializeField] private Button melee1Button;
    [SerializeField] private Image melee1Icon;
    [SerializeField] private TMP_Text melee1Name;
    [SerializeField] private GameObject melee1Frame;

    [Header("원거리 옵션 0 (보우)")]
    [SerializeField] private Button ranged0Button;
    [SerializeField] private Image ranged0Icon;
    [SerializeField] private TMP_Text ranged0Name;
    [SerializeField] private GameObject ranged0Frame;

    [Header("원거리 옵션 1 (석궁)")]
    [SerializeField] private Button ranged1Button;
    [SerializeField] private Image ranged1Icon;
    [SerializeField] private TMP_Text ranged1Name;
    [SerializeField] private GameObject ranged1Frame;

    [Header("확정 / 취소")]
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;

    private static readonly Color CardNormal   = new Color(0.20f, 0.22f, 0.30f, 1f);
    private static readonly Color CardSelected = new Color(0.50f, 0.42f, 0.16f, 1f);

    /// <summary>옵션 1개의 런타임 UI 묶음 (직렬화 평면 필드를 묶어 다룬다).</summary>
    private sealed class Option
    {
        public Button Button;
        public Image Icon;
        public TMP_Text Name;
        public GameObject Frame;
        public WeaponSO Weapon;

        public void Bind(WeaponSO weapon)
        {
            Weapon = weapon;
            bool has = weapon != null;

            if (Button != null) Button.gameObject.SetActive(has);
            if (!has) return;

            if (Icon != null)
            {
                Icon.sprite = weapon.icon;
                Icon.color = weapon.icon != null ? Color.white : new Color(1f, 1f, 1f, 0.15f);
            }
            if (Name != null) Name.text = weapon.displayName;
            SetSelected(false);
        }

        /// <summary>선택 시 카드 색 강조(흰 카드 방지 위해 색을 코드에서 제어). 프레임은 미사용.</summary>
        public void SetSelected(bool on)
        {
            if (Button != null && Button.image != null)
                Button.image.color = on ? CardSelected : CardNormal;
        }
    }

    private Option[] _melee;
    private Option[] _ranged;

    private UniTaskCompletionSource<ForgeChoice?> _tcs;
    private WeaponSO _selectedMelee;
    private WeaponSO _selectedRanged;

    // ── Public API ────────────────────────────────────────────

    public void Setup(IReadOnlyList<WeaponSO> melee, IReadOnlyList<WeaponSO> ranged)
    {
        EnsureOptions();
        ApplyStaticTheme();

        _selectedMelee = null;
        _selectedRanged = null;
        _tcs = new UniTaskCompletionSource<ForgeChoice?>();

        BindCategory(_melee, melee, isMelee: true);
        BindCategory(_ranged, ranged, isMelee: false);

        cancelButton?.onClick.RemoveAllListeners();
        cancelButton?.onClick.AddListener(() => Complete(null));

        confirmButton?.onClick.RemoveAllListeners();
        confirmButton?.onClick.AddListener(OnConfirm);

        RefreshConfirmInteractable();
    }

    public UniTask<ForgeChoice?> WaitForChoiceAsync() => _tcs.Task;

    // ── Private ───────────────────────────────────────────────

    private bool _themed;

    /// <summary>프리팹 색/레이블이 MCP 작성 한계로 누락될 수 있어 코드에서 강제 적용(흰 카드·저대비 방지).</summary>
    private void ApplyStaticTheme()
    {
        if (_themed) return;
        _themed = true;

        if (confirmButton != null && confirmButton.image != null)
            confirmButton.image.color = new Color(0.22f, 0.55f, 0.32f, 1f);
        if (cancelButton != null && cancelButton.image != null)
            cancelButton.image.color = new Color(0.34f, 0.34f, 0.40f, 1f);
        SetButtonTextWhite(confirmButton);
        SetButtonTextWhite(cancelButton);

        SetThemeText("Panel/Title", "무기 제작", new Color(1f, 0.88f, 0.5f, 1f));
        SetThemeText("Panel/MeleeLabel", "근거리 무기 — 1번 슬롯", new Color(0.80f, 0.85f, 1f, 1f));
        SetThemeText("Panel/RangedLabel", "원거리 무기 — 2번 슬롯", new Color(0.80f, 0.85f, 1f, 1f));
    }

    private void SetButtonTextWhite(Button button)
    {
        if (button == null) return;
        var label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.color = Color.white;
    }

    private void SetThemeText(string path, string text, Color color)
    {
        var tr = transform.Find(path);
        if (tr == null) return;
        var label = tr.GetComponent<TMP_Text>();
        if (label == null) return;
        label.text = text;
        label.color = color;
    }

    private void EnsureOptions()
    {
        if (_melee != null) return;

        _melee = new[]
        {
            new Option { Button = melee0Button, Icon = melee0Icon, Name = melee0Name, Frame = melee0Frame },
            new Option { Button = melee1Button, Icon = melee1Icon, Name = melee1Name, Frame = melee1Frame },
        };
        _ranged = new[]
        {
            new Option { Button = ranged0Button, Icon = ranged0Icon, Name = ranged0Name, Frame = ranged0Frame },
            new Option { Button = ranged1Button, Icon = ranged1Icon, Name = ranged1Name, Frame = ranged1Frame },
        };
    }

    private void BindCategory(Option[] options, IReadOnlyList<WeaponSO> weapons, bool isMelee)
    {
        for (int i = 0; i < options.Length; i++)
        {
            var opt = options[i];
            WeaponSO weapon = (weapons != null && i < weapons.Count) ? weapons[i] : null;
            opt.Bind(weapon);

            if (opt.Button == null) continue;
            opt.Button.onClick.RemoveAllListeners();
            if (weapon != null)
            {
                var captured = opt;
                opt.Button.onClick.AddListener(() => OnSelect(options, captured.Weapon, isMelee));
            }
        }
    }

    private void OnSelect(Option[] options, WeaponSO weapon, bool isMelee)
    {
        WeaponSO current = isMelee ? _selectedMelee : _selectedRanged;
        // 이미 선택된 항목을 다시 누르면 선택 취소(토글)
        WeaponSO next = current == weapon ? null : weapon;

        if (isMelee) _selectedMelee = next;
        else _selectedRanged = next;

        for (int i = 0; i < options.Length; i++)
            options[i].SetSelected(next != null && options[i].Weapon == next);

        RefreshConfirmInteractable();
    }

    private void RefreshConfirmInteractable()
    {
        if (confirmButton != null)
            confirmButton.interactable = _selectedMelee != null && _selectedRanged != null;
    }

    private void OnConfirm()
    {
        if (_selectedMelee == null || _selectedRanged == null) return;
        Complete(new ForgeChoice { Melee = _selectedMelee, Ranged = _selectedRanged });
    }

    private void Complete(ForgeChoice? choice)
    {
        _tcs?.TrySetResult(choice);
        ClosePopupUI();
    }
}
