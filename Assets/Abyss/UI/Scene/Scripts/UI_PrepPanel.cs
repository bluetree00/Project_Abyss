using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 캐릭터 선택 패널.
/// 1) 그리드에서 캐릭터 카드를 클릭하면
/// 2) Bamao CharacterStatusFrame 기반의 캐릭터 스탯 팝업(CharInfoPopup)이 표시됩니다.
/// </summary>
public class UI_PrepPanel : UI_Base
{
    [Header("데이터")]
    [SerializeField] private CharacterRoster roster;

    [Header("캐릭터 목록 (그리드)")]
    [SerializeField] private Transform characterListRoot;
    [SerializeField] private UI_CharacterSelectItem itemTemplate;

    [Header("캐릭터 정보 팝업")]
    [SerializeField] private GameObject charInfoPopup;
    [SerializeField] private GameObject bgDim;

    [Header("캐릭터 시트 (CharacterSheetFrame)")]
    [SerializeField] private Image    charImage;       // Charactor_AMeow Image
    [SerializeField] private TMP_Text charNameText;    // InfoText (TMP)

    [Header("스탯 슬라이더 (CharacterStatusFrame)")]
    [SerializeField] private Slider   sliderHp;
    [SerializeField] private TMP_Text sliderHpText;    // SliderHeart/SliderValueText

    [SerializeField] private Slider   sliderAtk;
    [SerializeField] private TMP_Text sliderAtkText;   // SliderAtk/SliderValueText

    [SerializeField] private Slider   sliderSpd;
    [SerializeField] private TMP_Text sliderSpdText;   // SliderStamina/SliderValueText

    [Header("버튼")]
    [SerializeField] private Button confirmButton;    // 팝업 안 - 이 캐릭터로 시작
    [SerializeField] private Button closePopupButton; // 팝업 닫기 (그리드로 복귀)
    [SerializeField] private Button cancelButton;     // PrepPanel 전체 닫기 (로비 복귀)

    private UI_CharacterSelectItem _selectedItem;
    private bool _built;

    // 슬라이더 max 기준값
    private const float MaxHp  = 200f;
    private const float MaxAtk = 100f;
    private const float MaxSpd = 15f;

    // ─────────────────────────────────────────────────────────
    public override void Open()
    {
        gameObject.SetActive(true);

        if (charInfoPopup != null) charInfoPopup.SetActive(false);
        if (bgDim        != null) bgDim.SetActive(false);

        if (!_built)
        {
            BuildCharacterList();
            _built = true;
        }

        RefreshConfirmButton();
    }

    public override void Close()
    {
        if (charInfoPopup != null) charInfoPopup.SetActive(false);
        gameObject.SetActive(false);
    }

    public override void Init()
    {
        base.Init();
        if (confirmButton    != null) confirmButton.onClick.AddListener(OnClickConfirm);
        if (closePopupButton != null) closePopupButton.onClick.AddListener(CloseInfoPopup);
        if (cancelButton     != null) cancelButton.onClick.AddListener(Close);
    }

    // ─────────────────────────────────────────────────────────
    private void BuildCharacterList()
    {
        if (roster == null)
        {
            Debug.LogError("[UI_PrepPanel] CharacterRoster가 연결되지 않았습니다.");
            return;
        }
        if (itemTemplate == null)
        {
            Debug.LogError("[UI_PrepPanel] itemTemplate이 연결되지 않았습니다.");
            return;
        }

        itemTemplate.gameObject.SetActive(false);

        foreach (var entry in roster.characters)
        {
            if (entry == null || entry.data == null) continue;

            var item = Instantiate(itemTemplate, characterListRoot);
            item.gameObject.SetActive(true);
            item.Init();
            item.Setup(entry, SelectItem);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(characterListRoot as RectTransform);
    }

    // ─────────────────────────────────────────────────────────
    private void SelectItem(UI_CharacterSelectItem item)
    {
        if (_selectedItem != null) _selectedItem.SetSelected(false);

        _selectedItem = item;
        _selectedItem.SetSelected(true);

        RefreshPreview(item.Entry);
        RefreshConfirmButton();

        if (bgDim        != null) bgDim.SetActive(true);
        if (charInfoPopup != null) charInfoPopup.SetActive(true);
    }

    private void CloseInfoPopup()
    {
        if (charInfoPopup != null) charInfoPopup.SetActive(false);
        if (bgDim        != null) bgDim.SetActive(false);
    }

    private void RefreshPreview(CharacterRoster.CharacterEntry entry)
    {
        if (entry == null || entry.data == null) return;
        var d = entry.data;

        if (charImage    != null)
        {
            charImage.sprite = entry.portrait;
            charImage.color  = entry.portrait != null ? Color.white : Color.gray;
        }
        if (charNameText != null) charNameText.text = d.characterName;

        SetSlider(sliderHp,  sliderHpText,  d.maxHealth,      MaxHp,  "HP");
        SetSlider(sliderAtk, sliderAtkText, d.attackPower,    MaxAtk, "ATK");
        SetSlider(sliderSpd, sliderSpdText, d.baseMoveSpeed,  MaxSpd, "SPD");
    }

    private static void SetSlider(Slider slider, TMP_Text label, float value, float maxVal, string prefix)
    {
        if (slider != null)
        {
            var anim = slider.GetComponent<BamaoUIPack.Scripts.SliderAnimator>();
            if (anim != null) anim.enabled = false;

            slider.minValue = 0f;
            slider.maxValue = maxVal;
            slider.value    = Mathf.Clamp(value, 0f, maxVal);
        }
        if (label != null) label.text = $"{prefix} {value:0}";
    }

    private void RefreshConfirmButton()
    {
        if (confirmButton != null)
            confirmButton.interactable = (_selectedItem != null && _selectedItem.Entry?.data != null);
    }

    // ─────────────────────────────────────────────────────────
    private void OnClickConfirm()
    {
        if (_selectedItem == null || _selectedItem.Entry?.data == null)
        {
            Debug.LogWarning("[UI_PrepPanel] 캐릭터가 선택되지 않았습니다.");
            return;
        }

        var entry = _selectedItem.Entry;
        Managers.CharacterData.SetCharacterData(entry.data, entry.prefabKey);

        var app = AppBootstrapper.Instance;
        if (app == null) return;

        Close();
        app.RequestStartRun();
    }
}
