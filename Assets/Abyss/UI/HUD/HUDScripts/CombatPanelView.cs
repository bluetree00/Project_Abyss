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

    // ── HP 애니메이션 런타임 ──
    private float _hpTargetRatio;
    private float _hpDisplayRatio;
    private float _ghostRatio;
    private float _ghostTimer;
    private bool  _ghostActive;
    private bool  _hpInitialized;

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

    // ─────────────────────────────────────────────────────────
    // 버프 목록 + 획득 알림
    // ─────────────────────────────────────────────────────────

    private const float NoticeDuration = 2f;
    private const float NoticeFadeTime = 0.5f;

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
        text.text = FormatBuff(buff);
        text.fontSize = 14f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;

        return go;
    }

    private static string FormatBuff(ActiveRoomBuff buff)
    {
        string typeName = buff.Modifier.Type switch
        {
            StatType.MoveSpeed    => "이동속도",
            StatType.AttackPower  => "공격력",
            StatType.MeleeAttack  => "근접공격",
            StatType.RangedAttack => "원거리공격",
            StatType.Defense      => "방어력",
            StatType.AttackSpeed  => "공격속도",
            StatType.Projectile   => "투사체",
            _                     => buff.Modifier.Type.ToString(),
        };

        string sign = buff.Modifier.Value >= 0 ? "+" : "";

        string valueStr = buff.IsPercent
            ? $"{sign}{buff.Modifier.Value * 100f:F0}%"
            : $"{sign}{buff.Modifier.Value:F0}";

        string remaining = buff.RoomsRemaining > 0 ? $" [{buff.RoomsRemaining}방]" : "";

        return $"{typeName} {valueStr}{remaining}";
    }

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
