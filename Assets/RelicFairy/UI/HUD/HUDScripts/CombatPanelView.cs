//============================================================
// CombatPanelView.cs
// - HP 슬라이더/텍스트
// - 장비 슬롯 × 2 (아이콘)
// - Q/E 스킬 슬롯 (아이콘, 쿨다운)
// - Active 슬롯 × 3
// - 버프 목록 + 획득 알림
//============================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class CombatPanelView : MonoBehaviour
{
    [Header("Status — HP")]
    [SerializeField] private Slider   hpSlider;
    [SerializeField] private TMP_Text hpText;
    [SerializeField] private Image    hpFillImage;
    [SerializeField] private Image    hpGhostFillImage;

    [Header("HP 애니메이션")]
    [SerializeField] private float hpLerpSpeed   = 3f;
    [SerializeField] private float ghostDelay    = 0.35f;
    [SerializeField] private float ghostLerpSpeed = 1.2f;

    [Header("Weapon Slots")]
    [SerializeField] private WeaponSlotUI slot0;
    [SerializeField] private WeaponSlotUI slot1;

    [Header("Skill Slots — Q / E")]
    [SerializeField] private SkillSlotUI skillQ;
    [SerializeField] private SkillSlotUI skillE;

    [Header("Active Slots")]
    [SerializeField] private ActiveSlotUI[] activeSlots = new ActiveSlotUI[3];

    [Header("Buff Display")]
    [SerializeField] private Transform buffListRoot;
    [SerializeField] private TMP_Text buffNoticeText;

    [Header("슬롯 레이블 폰트 (NotoSansKR 권장)")]
    [SerializeField] private TMP_FontAsset slotLabelFont;

    // ── HP 애니메이션 런타임 ──
    private float _hpTargetRatio;
    private float _hpDisplayRatio;
    private float _ghostRatio;
    private float _ghostTimer;
    private bool  _ghostActive;
    private bool  _hpInitialized;

    // ── R 스킬 슬롯 런타임 (프리팹에 없어 코드로 생성) ──
    private Image      _rIconImg;
    private GameObject _rCooldownBg;
    private TMP_Text   _rCooldownTxt;

    // ── 스탯 표시 런타임 ──
    private TMP_Text _atkText;
    private TMP_Text _defText;
    private TMP_Text _hpMaxText;
    private GameObject _statRoot;

    // ── 버프 UI 런타임 ──
    private readonly List<GameObject> _buffEntries = new();
    private float _noticeTimer;

    // ─────────────────────────────────────────────────────────
    // HP
    // ─────────────────────────────────────────────────────────
    public void SetHp(int hp, int maxHp)
    {
        int clampedMax = Mathf.Max(1, maxHp);
        int clampedHp  = Mathf.Clamp(hp, 0, clampedMax);
        float newRatio = (float)clampedHp / clampedMax;

        if (hpSlider != null)
        {
            hpSlider.minValue = 0f;
            hpSlider.maxValue = clampedMax;
            // value는 UpdateHpAnimation에서 부드럽게 갱신
        }

        if (!_hpInitialized)
        {
            _hpTargetRatio  = newRatio;
            _hpDisplayRatio = newRatio;
            _ghostRatio     = newRatio;
            _hpInitialized  = true;
            ApplyHpFill(newRatio);
        }
        else
        {
            if (newRatio < _hpTargetRatio - 0.001f)
            {
                _ghostRatio  = _hpDisplayRatio;
                _ghostTimer  = ghostDelay;
                _ghostActive = true;
            }
            _hpTargetRatio = newRatio;
        }

        if (hpText != null)
            hpText.text = $"{clampedHp} / {clampedMax}";
    }

    private void ApplyHpFill(float ratio)
    {
        if (hpSlider != null)
            hpSlider.value = ratio * hpSlider.maxValue;

        if (hpFillImage != null)
            hpFillImage.fillAmount = ratio;

        if (hpGhostFillImage != null)
            hpGhostFillImage.fillAmount = Mathf.Max(_ghostRatio, ratio);
    }

    private void UpdateHpAnimation()
    {
        float dt = Time.deltaTime;
        _hpDisplayRatio = Mathf.MoveTowards(_hpDisplayRatio, _hpTargetRatio, hpLerpSpeed * dt);

        if (_ghostActive)
        {
            if (_ghostTimer > 0f)
            {
                _ghostTimer -= dt;
            }
            else
            {
                _ghostRatio = Mathf.MoveTowards(_ghostRatio, _hpTargetRatio, ghostLerpSpeed * dt);
                if (Mathf.Abs(_ghostRatio - _hpTargetRatio) < 0.002f)
                {
                    _ghostRatio  = _hpTargetRatio;
                    _ghostActive = false;
                }
            }
        }

        ApplyHpFill(_hpDisplayRatio);
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
        if (skill == SkillType.R)
        {
            if (_rIconImg != null)
            {
                _rIconImg.sprite = icon;
                _rIconImg.gameObject.SetActive(icon != null);
            }
            return;
        }
        GetSkillSlot(skill)?.SetIcon(icon);
    }

    public void SetSkillCooldown(SkillType skill, float remaining, float total)
    {
        if (skill == SkillType.R)
        {
            bool onCd = remaining > 0.05f;
            if (_rCooldownBg != null) _rCooldownBg.SetActive(onCd);
            if (_rCooldownTxt != null)
                _rCooldownTxt.text = onCd ? Mathf.CeilToInt(remaining).ToString() : string.Empty;
            return;
        }
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

        [Header("무기 타입 기본 아이콘 (WeaponSO icon이 없을 때 폴백)")]
        [SerializeField] private Sprite iconKatana;
        [SerializeField] private Sprite iconSword;
        [SerializeField] private Sprite iconBow;
        [SerializeField] private Sprite iconCrossbow;

        public void Apply(WeaponSlotInfo info)
        {
            SetActive(emptyRoot, !info.HasWeapon);

            if (iconImage != null)
            {
                if (info.HasWeapon)
                {
                    Sprite resolved = info.Icon != null ? info.Icon : ResolveTypeIcon(info.Type);
                    iconImage.sprite = resolved;
                    iconImage.gameObject.SetActive(resolved != null);
                }
                else
                {
                    iconImage.gameObject.SetActive(false);
                }
            }
        }

        private Sprite ResolveTypeIcon(WeaponType type) => type switch
        {
            WeaponType.Katana     => iconKatana,
            WeaponType.Greatsword => iconSword,
#pragma warning disable CS0618
            WeaponType.Sword      => iconSword,
#pragma warning restore CS0618
            WeaponType.Bow        => iconBow,
            WeaponType.Crossbow   => iconCrossbow,
            _                     => null,
        };

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
        /// <summary>슬롯 우측 하단 키 레이블 (Q / E). Inspector 또는 런타임 생성.</summary>
        [SerializeField] internal TMP_Text  keyLabel;

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
        /// <summary>슬롯 우측 하단 숫자 레이블 (1 / 2 / 3). Inspector 또는 런타임 생성.</summary>
        [SerializeField] internal TMP_Text indexLabel;

        public void SetIcon(Sprite icon)
        {
            if (iconImage == null) return;
            iconImage.sprite = icon;
            iconImage.gameObject.SetActive(icon != null);
        }
    }

    // ─────────────────────────────────────────────────────────
    // 버프 목록 + 획득 알림
    // ─────────────────────────────────────────────────────────

    private const float NoticeDuration = 2f;
    private const float NoticeFadeTime = 0.5f;

    private void Awake()
    {
        EnsureSlotLabels();
        EnsureRSlot();
        EnsureStatPanel();
    }

    // ─────────────────────────────────────────────────────────
    // 슬롯 레이블 초기화 (Q/E/1/2/3)
    // ─────────────────────────────────────────────────────────
    private static readonly string[] ActiveLabelTexts = { "1", "2", "3" };
    private static readonly string[] SkillLabelTexts  = { "Q", "E" };

    private void EnsureSlotLabels()
    {
        EnsureSkillLabel(skillQ, SkillLabelTexts[0]);
        EnsureSkillLabel(skillE, SkillLabelTexts[1]);

        for (int i = 0; i < activeSlots.Length && i < ActiveLabelTexts.Length; i++)
            EnsureActiveLabel(activeSlots[i], ActiveLabelTexts[i]);
    }

    private void EnsureSkillLabel(SkillSlotUI slot, string text)
    {
        if (slot == null) return;
        if (slot.keyLabel != null)
        {
            slot.keyLabel.text = text;
            return;
        }

        // 스킬 슬롯 부모 Transform을 검색
        // SkillSlotUI는 Serializable이므로 Transform 직접 참조가 없어
        // CombatStatusRoot 자식에서 이름으로 검색
        string goName = text == "Q" ? "HUD_QSkile" : "HUD_ESkile";
        var slotGo = FindChildRecursive(transform, goName);
        if (slotGo == null) return;

        slot.keyLabel = CreateCornerLabel(slotGo, text);
    }

    private void EnsureActiveLabel(ActiveSlotUI slot, string text)
    {
        if (slot == null) return;
        if (slot.indexLabel != null)
        {
            slot.indexLabel.text = text;
            return;
        }

        string goName = text switch
        {
            "1" => "HUD_Active_01",
            "2" => "HUD_Active_02",
            "3" => "HUD_Active_03",
            _   => null,
        };
        if (goName == null) return;

        var slotGo = FindChildRecursive(transform, goName);
        if (slotGo == null) return;

        slot.indexLabel = CreateCornerLabel(slotGo, text);
    }

    /// <summary>
    /// 슬롯 오브젝트 우측 하단 외부에 작은 레이블 TMP_Text를 생성한다.
    /// anchoredPosition을 슬롯 우측 하단 바깥쪽으로 배치한다.
    /// </summary>
    private TMP_Text CreateCornerLabel(Transform slotRoot, string text)
    {
        var go = new GameObject($"Label_{text}", typeof(RectTransform));
        go.transform.SetParent(slotRoot, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin  = new Vector2(1f, 0f);
        rect.anchorMax  = new Vector2(1f, 0f);
        rect.pivot      = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(2f, -2f);
        rect.sizeDelta  = new Vector2(20f, 20f);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        AssignSafeFont(tmp);
        tmp.text      = text;
        tmp.fontSize  = 14f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color     = new Color(1f, 0.9f, 0.6f, 1f);
        tmp.alignment = TextAlignmentOptions.TopLeft;

        if (slotLabelFont != null)
            tmp.font = slotLabelFont;

        return tmp;
    }

    private static TMP_FontAsset _safeFontCache;
    private static TMP_FontAsset GetSafeFont()
    {
        if (_safeFontCache != null && _safeFontCache.material != null)
            return _safeFontCache;
        var def = TMP_Settings.defaultFontAsset;
        if (def != null && def.material != null)
            return _safeFontCache = def;
        _safeFontCache = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        return _safeFontCache;
    }
    private static void AssignSafeFont(TMP_Text tmp)
    {
        if (tmp.font != null && tmp.font.material != null) return;
        var safe = GetSafeFont();
        if (safe != null) tmp.font = safe;
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            var found = FindChildRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private void Update()
    {
        if (_hpInitialized)
            UpdateHpAnimation();

        if (_noticeTimer > 0f)
        {
            _noticeTimer -= Time.deltaTime;

            if (buffNoticeText != null)
            {
                if (_noticeTimer <= 0f)
                {
                    buffNoticeText.gameObject.SetActive(false);
                }
                else if (_noticeTimer < NoticeFadeTime)
                {
                    float alpha = _noticeTimer / NoticeFadeTime;
                    var c = buffNoticeText.color;
                    c.a = alpha;
                    buffNoticeText.color = c;
                }
            }
        }

        if (_itemNotices.Count > 0)
            UpdateItemNotices();
    }

    /// <summary>버프 획득/발동 시 화면 알림 (2초 표시 + 0.5초 페이드아웃).</summary>
    public void ShowBuffNotice(string message)
    {
        if (buffNoticeText == null)
        {
            EnsureBuffNoticeText();
            if (buffNoticeText == null) return;
        }

        buffNoticeText.text = message;
        buffNoticeText.gameObject.SetActive(true);

        // 알파 복원
        var c = buffNoticeText.color;
        c.a = 1f;
        buffNoticeText.color = c;

        _noticeTimer = NoticeDuration + NoticeFadeTime;
    }

    /// <summary>현재 활성 버프 목록 전체 갱신.</summary>
    public void RefreshBuffList(IReadOnlyList<ActiveRoomBuff> buffs)
    {
        if (buffListRoot == null)
        {
            EnsureBuffListRoot();
            if (buffListRoot == null) return;
        }

        // 기존 엔트리 정리
        foreach (var go in _buffEntries)
            if (go != null) Destroy(go);
        _buffEntries.Clear();

        if (buffs == null || buffs.Count == 0) return;

        for (int i = 0; i < buffs.Count; i++)
        {
            var buff = buffs[i];
            var entry = CreateBuffEntry(buff, i);
            _buffEntries.Add(entry);
        }
    }

    private GameObject CreateBuffEntry(ActiveRoomBuff buff, int index)
    {
        var go = new GameObject($"BuffEntry_{index}", typeof(RectTransform));
        go.transform.SetParent(buffListRoot, false);

        var rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(200f, 24f);

        // 배경
        var bg = go.AddComponent<Image>();
        bg.color = buff.IsDebuff
            ? new Color(0.6f, 0.15f, 0.15f, 0.7f)
            : new Color(0.15f, 0.35f, 0.6f, 0.7f);

        // 텍스트
        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);

        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(6f, 0f);
        textRect.offsetMax = new Vector2(-4f, 0f);

        var text = textGo.AddComponent<TextMeshProUGUI>();
        AssignSafeFont(text);
        text.text = FormatBuff(buff);
        text.fontSize = 14f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;

        EffectIconView.Attach(go.transform, EffectDescriptionFormatter.IconKeyForStat(buff.Modifier.Type), text, 16f);

        return go;
    }

    private static string FormatBuff(ActiveRoomBuff buff)
        => EffectDescriptionFormatter.FormatStatBuff(
            buff.Modifier.Type, buff.Modifier.Value, buff.IsPercent, buff.RoomsRemaining);

    // ─────────────────────────────────────────────────────────
    // 아이템 효과 발동 알림 (스택형, 왼쪽 하단)
    // ─────────────────────────────────────────────────────────

    private const float ItemNoticeDuration = 2f;
    private const float ItemNoticeFadeTime = 0.5f;
    private const int MaxItemNotices = 5;

    private Transform _itemNoticeRoot;
    private readonly List<ItemNoticeEntry> _itemNotices = new();

    private struct ItemNoticeEntry
    {
        public GameObject go;
        public TMP_Text text;
        public float timer;
    }

    /// <summary>아이템 효과 발동 시 왼쪽에 스택형 알림 표시.</summary>
    public void ShowItemEffectNotice(string message)
    {
        EnsureItemNoticeRoot();
        if (_itemNoticeRoot == null) return;

        // 최대 개수 초과 시 가장 오래된 것 제거
        if (_itemNotices.Count >= MaxItemNotices)
        {
            if (_itemNotices[0].go != null)
                Destroy(_itemNotices[0].go);
            _itemNotices.RemoveAt(0);
        }

        var go = new GameObject($"ItemNotice_{_itemNotices.Count}", typeof(RectTransform));
        go.transform.SetParent(_itemNoticeRoot, false);

        var rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(280f, 22f);

        var text = go.AddComponent<TextMeshProUGUI>();
        AssignSafeFont(text);
        text.text = message;
        text.fontSize = 13f;
        text.color = new Color(1f, 0.85f, 0.4f);
        text.alignment = TextAlignmentOptions.MidlineLeft;

        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        outline.effectDistance = new Vector2(1f, -1f);

        _itemNotices.Add(new ItemNoticeEntry
        {
            go = go,
            text = text,
            timer = ItemNoticeDuration + ItemNoticeFadeTime,
        });
    }

    private void UpdateItemNotices()
    {
        for (int i = _itemNotices.Count - 1; i >= 0; i--)
        {
            var entry = _itemNotices[i];
            entry.timer -= Time.deltaTime;
            _itemNotices[i] = entry;

            if (entry.timer <= 0f)
            {
                if (entry.go != null) Destroy(entry.go);
                _itemNotices.RemoveAt(i);
            }
            else if (entry.timer < ItemNoticeFadeTime && entry.text != null)
            {
                float alpha = entry.timer / ItemNoticeFadeTime;
                var c = entry.text.color;
                c.a = alpha;
                entry.text.color = c;
            }
        }
    }

    private void EnsureItemNoticeRoot()
    {
        if (_itemNoticeRoot != null) return;

        var go = new GameObject("ItemNoticeRoot", typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.3f);
        rect.anchorMax = new Vector2(0f, 0.3f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(10f, 0f);
        rect.sizeDelta = new Vector2(290f, 200f);

        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 2f;
        layout.childAlignment = TextAnchor.LowerLeft;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = false;

        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _itemNoticeRoot = go.transform;
    }

    // ─────────────────────────────────────────────────────────
    // R 스킬 슬롯 자동 생성 (Q=138, E=212, R=286 px 배치)
    // ─────────────────────────────────────────────────────────

    private void EnsureRSlot()
    {
        if (_rIconImg != null) return;

        var combatRoot = FindChildRecursive(transform, "CombatStatusRoot");
        if (combatRoot == null) return;

        var rGO = new GameObject("HUD_RSkile", typeof(RectTransform));
        rGO.transform.SetParent(combatRoot, false);
        var rt = rGO.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0f);
        rt.anchorMax        = new Vector2(0.5f, 0f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = new Vector2(68f, 68f);
        rt.anchoredPosition = new Vector2(286f, 50.8f);

        var slotBG = rGO.AddComponent<Image>();
        slotBG.color = new Color(0.08f, 0.08f, 0.14f, 0.85f);

        // 스킬 아이콘
        var iconGO = new GameObject("SkillIcon", typeof(RectTransform));
        iconGO.transform.SetParent(rGO.transform, false);
        var iconRT = iconGO.GetComponent<RectTransform>();
        iconRT.anchorMin = Vector2.zero;
        iconRT.anchorMax = Vector2.one;
        iconRT.offsetMin = new Vector2(5f, 5f);
        iconRT.offsetMax = new Vector2(-5f, -5f);
        _rIconImg = iconGO.AddComponent<Image>();
        _rIconImg.preserveAspect = true;
        _rIconImg.gameObject.SetActive(false);

        // 쿨다운 오버레이
        var cdBG = new GameObject("Cooldown_BG", typeof(RectTransform));
        cdBG.transform.SetParent(rGO.transform, false);
        var cdRT = cdBG.GetComponent<RectTransform>();
        cdRT.anchorMin = Vector2.zero;
        cdRT.anchorMax = Vector2.one;
        cdRT.sizeDelta  = Vector2.zero;
        cdBG.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);
        _rCooldownBg = cdBG;
        _rCooldownBg.SetActive(false);

        // 쿨다운 숫자
        var cdTxtGO = new GameObject("CooldownText", typeof(RectTransform));
        cdTxtGO.transform.SetParent(cdBG.transform, false);
        var cdTxtRT = cdTxtGO.GetComponent<RectTransform>();
        cdTxtRT.anchorMin = Vector2.zero;
        cdTxtRT.anchorMax = Vector2.one;
        cdTxtRT.sizeDelta = Vector2.zero;
        _rCooldownTxt = cdTxtGO.AddComponent<TextMeshProUGUI>();
        AssignSafeFont(_rCooldownTxt);
        _rCooldownTxt.fontSize  = 18f;
        _rCooldownTxt.fontStyle = FontStyles.Bold;
        _rCooldownTxt.alignment = TextAlignmentOptions.Center;
        _rCooldownTxt.color     = Color.white;

        // 키 레이블 "R"
        CreateCornerLabel(rGO.transform, "R");
    }

    // ─────────────────────────────────────────────────────────
    // 스탯 표시 패널 (ATK / DEF — 자동 생성)
    // ─────────────────────────────────────────────────────────

    /// <summary>공격력·방어력을 HUD에 실시간 반영한다. HudPresenter.RefreshStats에서 호출.</summary>
    public void SetStats(int atk, int def)
    {
        if (_atkText != null) _atkText.SetText($"⚔ {atk}");
        if (_defText != null) _defText.SetText($"🛡 {def}");
    }

    private void EnsureStatPanel()
    {
        if (_statRoot != null) return;

        // CombatStatusRoot 탐색
        var combatRoot = FindChildRecursive(transform, "CombatStatusRoot");
        if (combatRoot == null) return;

        // CombatStatusRoot 크기 확대: 650×130 → 750×168
        if (combatRoot is RectTransform crt)
        {
            crt.sizeDelta = new Vector2(750f, 168f);
        }

        // 스탯 행 배치: CombatStatusRoot 상단 28px 영역
        var statGO = new GameObject("StatRow", typeof(RectTransform));
        statGO.transform.SetParent(combatRoot, false);
        _statRoot = statGO;

        var rt = statGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, 30f);
        rt.anchoredPosition = Vector2.zero;

        // 반투명 배경
        var bg = statGO.AddComponent<Image>();
        bg.color         = new Color(0f, 0f, 0f, 0.35f);
        bg.raycastTarget = false;

        // ── ATK 텍스트 (좌측 절반) ──
        _atkText = MakeStatText(statGO.transform, "AtkText",
            new Vector2(0f, 0f), new Vector2(0.5f, 1f),
            new Color(1.0f, 0.55f, 0.25f, 1f));

        // ── DEF 텍스트 (우측 절반) ──
        _defText = MakeStatText(statGO.transform, "DefText",
            new Vector2(0.5f, 0f), new Vector2(1f, 1f),
            new Color(0.35f, 0.70f, 1.00f, 1f));
    }

    private TMP_Text MakeStatText(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = new Vector2(6f, 0f);
        rt.offsetMax = new Vector2(-6f, 0f);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        AssignSafeFont(tmp);
        tmp.text      = "—";
        tmp.fontSize  = 15f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color     = color;
        tmp.alignment = TextAlignmentOptions.Midline;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;

        var ol = go.AddComponent<Outline>();
        ol.effectColor    = new Color(0f, 0f, 0f, 0.7f);
        ol.effectDistance = new Vector2(1f, -1f);

        return tmp;
    }

    // ── 자동 생성 (Inspector 미연결 시 런타임 폴백) ──

    private void EnsureBuffListRoot()
    {
        if (buffListRoot != null) return;

        var go = new GameObject("BuffListRoot", typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(10f, 0f);
        rect.sizeDelta = new Vector2(210f, 400f);

        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 4f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = false;

        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        buffListRoot = go.transform;
    }

    private void EnsureBuffNoticeText()
    {
        if (buffNoticeText != null) return;

        var go = new GameObject("BuffNoticeText", typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.7f);
        rect.anchorMax = new Vector2(0.5f, 0.7f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(400f, 40f);

        buffNoticeText = go.AddComponent<TextMeshProUGUI>();
        AssignSafeFont(buffNoticeText);
        buffNoticeText.fontSize = 22f;
        buffNoticeText.color = Color.yellow;
        buffNoticeText.alignment = TextAlignmentOptions.Center;
        buffNoticeText.fontStyle = FontStyles.Bold;

        // Outline 효과
        var outline = go.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(1f, -1f);

        go.SetActive(false);
    }
}
