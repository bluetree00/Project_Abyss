using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 게임 준비 패널 — 3-스테이트 구조
/// </summary>
public class UI_PrepPanel : UI_Base
{
    // ── 서브 패널 ─────────────────────────────────────────
    [Header("서브 패널 (3-State)")]
    [SerializeField] private GameObject panelMain;
    [SerializeField] private GameObject panelCharSelect;
    [SerializeField] private GameObject panelWeaponSelect;

    // ── 메인 패널 ─────────────────────────────────────────
    [Header("메인 — 캐릭터 슬롯")]
    [SerializeField] private Button   charSlotButton;
    [SerializeField] private Image    charSlotPortrait;
    [SerializeField] private TMP_Text charSlotName;

    [Header("메인 — 무기 슬롯")]
    [SerializeField] private Button   weaponSlotButton;
    [SerializeField] private Image    weaponSlotIcon;
    [SerializeField] private TMP_Text weaponSlotName;

    [Header("메인 — 캐릭터 스탯")]
    [SerializeField] private TMP_Text mainHpLabel;
    [SerializeField] private TMP_Text mainDefLabel;
    [SerializeField] private Image    mainAbilityIcon0;
    [SerializeField] private Image    mainAbilityIcon1;
    [SerializeField] private Image    mainAbilityIcon2;

    [Header("메인 — 무기 스킬")]
    [SerializeField] private TMP_Text mainWeaponParamsTitle;
    [SerializeField] private TMP_Text mainWeaponAtk;
    [SerializeField] private TMP_Text mainWeaponSpd;
    [SerializeField] private TMP_Text mainWeaponRng;

    [Header("메인 — 툴팁")]
    [SerializeField] private GameObject tooltipPanel;
    [SerializeField] private TMP_Text   tooltipName;
    [SerializeField] private TMP_Text   tooltipDesc;
    [SerializeField] private TMP_Text   tooltipCooldown;

    [Header("메인 — 버튼")]
    [SerializeField] private Button   gameStartButton;
    [SerializeField] private Button   cancelButton;

    // ── 캐릭터 선택 패널 ──────────────────────────────────
    [Header("캐릭터 선택 — 데이터")]
    [SerializeField] private CharacterRoster roster;

    [Header("캐릭터 선택 — 그리드")]
    [SerializeField] private Transform characterListRoot;
    [SerializeField] private UI_CharacterSelectItem itemTemplate;

    [Header("캐릭터 선택 — 오른쪽 프리뷰")]
    [SerializeField] private Image    charPreviewImage;
    [SerializeField] private TMP_Text charPreviewName;
    [SerializeField] private Slider   sliderHp;
    [SerializeField] private TMP_Text sliderHpText;
    [SerializeField] private Slider   sliderAtk;
    [SerializeField] private TMP_Text sliderAtkText;
    [SerializeField] private Slider   sliderSpd;
    [SerializeField] private TMP_Text sliderSpdText;

    [Header("캐릭터 선택 — 능력 아이콘")]
    [SerializeField] private Image abilityIcon0;
    [SerializeField] private Image abilityIcon1;
    [SerializeField] private Image abilityIcon2;

    [Header("캐릭터 선택 — 버튼")]
    [SerializeField] private Button   charConfirmButton;

    // ── 무기 선택 패널 ────────────────────────────────────
    [Header("무기 선택 — 데이터")]
    [SerializeField] private WeaponRoster weaponRoster;

    [Header("무기 선택 — 그리드")]
    [SerializeField] private Transform weaponListRoot;
    [SerializeField] private UI_WeaponSelectItem weaponItemTemplate;

    [Header("무기 선택 — 오른쪽 프리뷰")]
    [SerializeField] private Image    weaponPreviewImage;
    [SerializeField] private TMP_Text weaponPreviewName;
    [SerializeField] private TMP_Text weaponAtkText;
    [SerializeField] private TMP_Text weaponAtkSpeedText;
    [SerializeField] private TMP_Text weaponRangeText;
    [SerializeField] private Image    weaponQSkillIcon;
    [SerializeField] private Image    weaponESkillIcon;

    [Header("무기 선택 — 버튼")]
    [SerializeField] private Button   weaponConfirmButton;

    // ── 플레이스홀더 ───────────────────────────────────────
    [Header("플레이스홀더")]
    [SerializeField] private Sprite defaultCharSprite;
    [SerializeField] private Sprite defaultWeaponSprite;

    // ── 내부 상태 ─────────────────────────────────────────
    private CharacterRoster.CharacterEntry _selectedCharEntry;
    private WeaponRoster.WeaponEntry       _selectedWeaponEntry;
    private UI_CharacterSelectItem         _selectedCharItem;
    private UI_WeaponSelectItem            _selectedWeaponItem;
    private bool _built;

    private const float MaxHp  = 500f;
    private const float MaxAtk = 100f;
    private const float MaxSpd = 15f;

    // ── UI_Base 생명주기 ──────────────────────────────────
    public override void Init()
    {
        base.Init();

        if (cancelButton        != null) cancelButton.onClick.AddListener(Close);
        if (charSlotButton      != null) charSlotButton.onClick.AddListener(OpenCharSelect);
        if (weaponSlotButton    != null) weaponSlotButton.onClick.AddListener(OpenWeaponSelect);
        if (gameStartButton     != null) gameStartButton.onClick.AddListener(OnClickGameStart);
        if (charConfirmButton   != null) charConfirmButton.onClick.AddListener(OnClickCharConfirm);
        if (weaponConfirmButton != null) weaponConfirmButton.onClick.AddListener(OnClickWeaponConfirm);
    }

    public override void Open()
    {
        gameObject.SetActive(true);

        if (!_built)
        {
            BuildCharacterList();
            BuildWeaponList();
            _built = true;
        }

        ShowMain();
    }

    public override void Close()
    {
        gameObject.SetActive(false);
    }

    // ── 패널 전환 ─────────────────────────────────────────
    private void ShowMain()
    {
        SetPanel(panelMain, true);
        SetPanel(panelCharSelect, false);
        SetPanel(panelWeaponSelect, false);
        RefreshMainSlots();
        RefreshGameStartButton();
    }

    private void OpenCharSelect()
    {
        SetPanel(panelMain, false);
        SetPanel(panelCharSelect, true);
        SetPanel(panelWeaponSelect, false);
    }

    private void OpenWeaponSelect()
    {
        SetPanel(panelMain, false);
        SetPanel(panelCharSelect, false);
        SetPanel(panelWeaponSelect, true);
    }

    private static void SetPanel(GameObject panel, bool active)
    {
        if (panel != null) panel.SetActive(active);
    }

    // ── 메인 패널 갱신 ────────────────────────────────────
    private void RefreshMainSlots()
    {
        // ── 캐릭터 슬롯 ──
        bool hasChar = _selectedCharEntry?.data != null;

        if (charSlotPortrait != null)
        {
            if (hasChar && _selectedCharEntry.portrait != null)
            {
                charSlotPortrait.sprite = _selectedCharEntry.portrait;
                charSlotPortrait.color  = Color.white;
            }
            else
            {
                charSlotPortrait.sprite = defaultCharSprite;
                charSlotPortrait.color  = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            }
        }

        if (charSlotName != null)
            charSlotName.text = hasChar ? _selectedCharEntry.data.characterName : "클릭하여 캐릭터 선택";

        // 캐릭터 스탯 — 미선택 시 개별 숨김
        SetGOActive(mainHpLabel, hasChar);
        SetGOActive(mainDefLabel, hasChar);
        SetGOActive(mainAbilityIcon0, hasChar);
        SetGOActive(mainAbilityIcon1, hasChar);
        SetGOActive(mainAbilityIcon2, hasChar);
        if (hasChar)
        {
            var d = _selectedCharEntry.data;
            if (mainHpLabel != null)  mainHpLabel.text  = $"체력  {d.maxHealth}";
            if (mainDefLabel != null) mainDefLabel.text = $"방어력  {d.baseDefense}";
            SetAbilityIcon(mainAbilityIcon0, _selectedCharEntry.abilityIcon0);
            SetAbilityIcon(mainAbilityIcon1, _selectedCharEntry.abilityIcon1);
            SetAbilityIcon(mainAbilityIcon2, _selectedCharEntry.abilityIcon2);

            if (d.passive != null)
            {
                SetupTooltip(mainAbilityIcon0, d.passive.passiveName, d.passive.description, 0);
                SetupTooltip(mainAbilityIcon1, d.passive.passiveName, d.passive.description, 0);
                SetupTooltip(mainAbilityIcon2, d.passive.passiveName, d.passive.description, 0);
            }
        }

        // ── 무기 슬롯 ──
        var weaponSO = _selectedWeaponEntry?.data;
        bool hasWeapon = weaponSO != null;
        var weaponIcon = (_selectedWeaponEntry != null && _selectedWeaponEntry.icon != null)
                          ? _selectedWeaponEntry.icon : weaponSO?.icon;

        if (weaponSlotIcon != null)
        {
            if (hasWeapon && weaponIcon != null)
            {
                weaponSlotIcon.sprite = weaponIcon;
                weaponSlotIcon.color  = Color.white;
            }
            else
            {
                weaponSlotIcon.sprite = defaultWeaponSprite;
                weaponSlotIcon.color  = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            }
        }

        if (weaponSlotName != null)
            weaponSlotName.text = hasWeapon ? weaponSO.displayName : "클릭하여 무기 선택";

        // 무기 스탯 — 미선택 시 개별 숨김
        SetGOActive(mainWeaponParamsTitle, hasWeapon);
        SetGOActive(mainWeaponAtk, hasWeapon);
        SetGOActive(mainWeaponSpd, hasWeapon);
        SetGOActive(mainWeaponRng, hasWeapon);
        if (hasWeapon)
        {
            if (mainWeaponAtk != null) mainWeaponAtk.text = $"ATK: {weaponSO.baseAttack:0}";
            if (mainWeaponSpd != null) mainWeaponSpd.text = $"SPD: {weaponSO.attackSpeed:0.0}/s";
            if (mainWeaponRng != null) mainWeaponRng.text = $"RNG: {weaponSO.attackRange:0}m";

            if (weaponSlotIcon != null)
            {
                var slotWeapon = weaponSlotIcon.transform.parent;
                var qIcon = slotWeapon?.Find("QSkillIcon")?.GetComponent<Image>();
                var eIcon = slotWeapon?.Find("ESkillIcon")?.GetComponent<Image>();
                SetAbilityIcon(qIcon, weaponSO.skillQ?.icon);
                SetAbilityIcon(eIcon, weaponSO.skillE?.icon);
                SetGOActive(qIcon, true);
                SetGOActive(eIcon, true);

                if (weaponSO.skillQ != null)
                    SetupTooltip(qIcon, weaponSO.skillQ.skillName, weaponSO.skillQ.description, weaponSO.skillQ.cooldown);
                if (weaponSO.skillE != null)
                    SetupTooltip(eIcon, weaponSO.skillE.skillName, weaponSO.skillE.description, weaponSO.skillE.cooldown);
            }
        }
        else if (weaponSlotIcon != null)
        {
            // 미선택 시 Q/E 스킬 아이콘 숨김
            var slotWeapon = weaponSlotIcon.transform.parent;
            SetGOActive(slotWeapon?.Find("QSkillIcon")?.GetComponent<Image>(), false);
            SetGOActive(slotWeapon?.Find("ESkillIcon")?.GetComponent<Image>(), false);
        }
    }

    private void RefreshGameStartButton()
    {
        bool canStart = _selectedCharEntry != null && _selectedWeaponEntry != null;
        if (gameStartButton != null)
        {
            gameStartButton.interactable = canStart;

            // 시작 버튼 텍스트 변경
            var btnText = gameStartButton.GetComponentInChildren<TMP_Text>();
            if (btnText != null)
                btnText.text = canStart ? "게임 시작" : "캐릭터와 무기를 선택하세요";
        }
    }

    // ── 캐릭터 선택 ───────────────────────────────────────
    private void BuildCharacterList()
    {
        if (roster == null || characterListRoot == null || itemTemplate == null)
        {
            Debug.LogWarning("[UI_PrepPanel] 캐릭터 리스트 참조 누락");
            return;
        }

        itemTemplate.gameObject.SetActive(false);

        foreach (var entry in roster.characters)
        {
            if (entry == null || entry.data == null) continue;
            var item = Instantiate(itemTemplate, characterListRoot);
            item.gameObject.SetActive(true);
            item.Init();
            item.Setup(entry, OnCharItemClicked);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(characterListRoot as RectTransform);
    }

    private void OnCharItemClicked(UI_CharacterSelectItem item)
    {
        if (_selectedCharItem != null) _selectedCharItem.SetSelected(false);
        _selectedCharItem = item;
        _selectedCharItem.SetSelected(true);
        RefreshCharPreview(item.Entry);
        if (charConfirmButton != null) charConfirmButton.interactable = true;
    }

    private void RefreshCharPreview(CharacterRoster.CharacterEntry entry)
    {
        if (entry == null || entry.data == null) return;
        var d = entry.data;

        if (charPreviewImage != null)
        {
            var portrait = entry.portrait != null ? entry.portrait : defaultCharSprite;
            charPreviewImage.sprite = portrait;
            charPreviewImage.color  = portrait != null ? Color.white : Color.gray;
        }
        if (charPreviewName != null) charPreviewName.text = d.characterName;

        SetSlider(sliderHp,  sliderHpText,  d.maxHealth,     MaxHp,  "체력");
        SetSlider(sliderAtk, sliderAtkText, d.GetTotalAttackPower(), MaxAtk, "공격력");
        SetSlider(sliderSpd, sliderSpdText, d.baseMoveSpeed, MaxSpd, "이동속도");

        SetAbilityIcon(abilityIcon0, entry.abilityIcon0);
        SetAbilityIcon(abilityIcon1, entry.abilityIcon1);
        SetAbilityIcon(abilityIcon2, entry.abilityIcon2);

        if (d.passive != null)
        {
            SetupTooltip(abilityIcon0, d.passive.passiveName, d.passive.description, 0);
            SetupTooltip(abilityIcon1, d.passive.passiveName, d.passive.description, 0);
            SetupTooltip(abilityIcon2, d.passive.passiveName, d.passive.description, 0);
        }
    }

    private void SetupTooltip(Image icon, string name, string desc, float cooldown)
    {
        if (icon == null) return;
        var trigger = icon.GetComponent<SkillTooltipTrigger>();
        if (trigger == null) trigger = icon.gameObject.AddComponent<SkillTooltipTrigger>();
        trigger.SetData(name, desc, cooldown);
        trigger.SetTooltipPanel(tooltipPanel, tooltipName, tooltipDesc, tooltipCooldown);
    }

    private void OnClickCharConfirm()
    {
        if (_selectedCharItem == null) return;
        _selectedCharEntry = _selectedCharItem.Entry;
        ShowMain();
    }

    // ── 무기 선택 ─────────────────────────────────────────
    private void BuildWeaponList()
    {
        if (weaponRoster == null || weaponListRoot == null || weaponItemTemplate == null)
        {
            Debug.LogWarning("[UI_PrepPanel] 무기 리스트 참조 누락");
            return;
        }

        weaponItemTemplate.gameObject.SetActive(false);

        foreach (var entry in weaponRoster.weapons)
        {
            if (entry == null || entry.data == null) continue;
            var item = Instantiate(weaponItemTemplate, weaponListRoot);
            item.gameObject.SetActive(true);
            item.Init();
            item.Setup(entry, OnWeaponItemClicked);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(weaponListRoot as RectTransform);
    }

    private void OnWeaponItemClicked(UI_WeaponSelectItem item)
    {
        if (_selectedWeaponItem != null) _selectedWeaponItem.SetSelected(false);
        _selectedWeaponItem = item;
        _selectedWeaponItem.SetSelected(true);
        RefreshWeaponPreview(item.Entry);
        if (weaponConfirmButton != null) weaponConfirmButton.interactable = true;
    }

    private void RefreshWeaponPreview(WeaponRoster.WeaponEntry entry)
    {
        if (entry == null || entry.data == null) return;
        var so   = entry.data;
        var icon = entry.icon != null ? entry.icon : so.icon;

        if (weaponPreviewImage != null)
        {
            var preview = icon != null ? icon : defaultWeaponSprite;
            weaponPreviewImage.sprite = preview;
            weaponPreviewImage.color  = preview != null ? Color.white : Color.gray;
        }
        if (weaponPreviewName  != null) weaponPreviewName.text  = so.displayName;
        if (weaponAtkText      != null) weaponAtkText.text      = $"공격력  {so.baseAttack:0}";
        if (weaponAtkSpeedText != null) weaponAtkSpeedText.text = $"공격속도  {so.attackSpeed:0.0}/s";
        if (weaponRangeText    != null) weaponRangeText.text    = $"사거리  {so.attackRange:0}m";

        SetAbilityIcon(weaponQSkillIcon, so.skillQ?.icon);
        SetAbilityIcon(weaponESkillIcon, so.skillE?.icon);

        if (so.skillQ != null)
            SetupTooltip(weaponQSkillIcon, so.skillQ.skillName, so.skillQ.description, so.skillQ.cooldown);
        if (so.skillE != null)
            SetupTooltip(weaponESkillIcon, so.skillE.skillName, so.skillE.description, so.skillE.cooldown);
    }

    private void OnClickWeaponConfirm()
    {
        if (_selectedWeaponItem == null) return;
        _selectedWeaponEntry = _selectedWeaponItem.Entry;
        ShowMain();
    }

    // ── 게임 시작 ─────────────────────────────────────────
    private void OnClickGameStart()
    {
        if (_selectedCharEntry == null || _selectedWeaponEntry == null)
        {
            Debug.LogWarning("[UI_PrepPanel] 캐릭터 또는 무기가 선택되지 않았습니다.");
            return;
        }

        Managers.CharacterData.SetCharacterData(_selectedCharEntry.data, _selectedCharEntry.prefabKey);

        var app = AppBootstrapper.Instance;
        if (app == null) return;

        app.Loadout.SetCharacter(_selectedCharEntry.data, _selectedCharEntry.prefabKey);
        app.Loadout.SetWeaponSlot0(_selectedWeaponEntry.data);

        Close();
        app.RequestStartRun();
    }

    // ── 유틸 ──────────────────────────────────────────────
    private static void SetGOActive(Component comp, bool active)
    {
        if (comp != null) comp.gameObject.SetActive(active);
    }

    private static void SetAbilityIcon(Image img, Sprite sprite)
    {
        if (img == null) return;
        img.sprite = sprite;
        img.color  = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0.15f);
    }

    private static void ClearAbilityIcon(Image img)
    {
        if (img == null) return;
        img.sprite = null;
        img.color  = new Color(1f, 1f, 1f, 0.08f);
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
        if (label != null) label.text = $"{prefix}  {value:0}";
    }
}
