using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 캐릭터 선택 패널.
/// 1) 그리드에서 캐릭터 카드를 클릭하면
/// 2) 캐릭터 스탯 팝업(CharInfoPopup)이 중앙에 표시됩니다.
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
    [SerializeField] private Image      previewPortrait;
    [SerializeField] private TMP_Text   previewName;
    [SerializeField] private TMP_Text   previewHp;
    [SerializeField] private TMP_Text   previewAttack;

    [Header("버튼")]
    [SerializeField] private Button confirmButton;    // 팝업 안 - 이 캐릭터로 시작
    [SerializeField] private Button closePopupButton; // 팝업 닫기 (그리드로 복귀)
    [SerializeField] private Button cancelButton;     // PrepPanel 전체 닫기 (로비 복귀)

    private UI_CharacterSelectItem _selectedItem;
    private bool _built;

    // ─────────────────────────────────────────────────────────
    public override void Open()
    {
        gameObject.SetActive(true);

        // 팝업은 항상 닫힌 상태로 시작
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

        // ContentSizeFitter는 다음 레이아웃 패스까지 계산을 미루므로
        // Mask 클리핑 전에 Content 크기를 강제 갱신합니다.
        LayoutRebuilder.ForceRebuildLayoutImmediate(characterListRoot as RectTransform);
    }

    // ─────────────────────────────────────────────────────────
    // 카드 클릭 → 선택 + 팝업 오픈
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

        if (previewPortrait != null) previewPortrait.sprite = entry.portrait;
        if (previewName     != null) previewName.text       = entry.data.characterName;
        if (previewHp       != null) previewHp.text         = $"HP  {entry.data.maxHealth}";
        if (previewAttack   != null) previewAttack.text     = $"ATK {entry.data.attackPower}";
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
