using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 무기 교체 팝업 — 상단 새 장비 + 하단 슬롯 2개 비교
/// Q/E 스킬 아이콘 + 호버 툴팁 포함
/// </summary>
public class UI_WeaponReplacePopup : UI_Popup
{
    public override bool BlocksGameplay => true; // 무기 교체 선택 중 시간정지 + 입력잠금

    // ── 슬롯 0 (왼쪽) ─────────────────────────────────────────
    [Header("슬롯 0")]
    [SerializeField] private Image    slot0Icon;
    [SerializeField] private TMP_Text slot0Name;
    [SerializeField] private TMP_Text slot0AtkText;
    [SerializeField] private TMP_Text slot0AtkDelta;
    [SerializeField] private TMP_Text slot0DefText;
    [SerializeField] private TMP_Text slot0DefDelta;
    [SerializeField] private TMP_Text slot0SkillText;
    [SerializeField] private Image    slot0QSkillIcon;
    [SerializeField] private Image    slot0ESkillIcon;
    [SerializeField] private Button   slot0ReplaceButton;

    // ── 새 장비 (상단) ────────────────────────────────────────
    [Header("새 장비")]
    [SerializeField] private Image    newIcon;
    [SerializeField] private TMP_Text newName;
    [SerializeField] private TMP_Text newAtkText;
    [SerializeField] private TMP_Text newDefText;
    [SerializeField] private TMP_Text newSkillText;
    [SerializeField] private Image    newQSkillIcon;
    [SerializeField] private Image    newESkillIcon;
    [SerializeField] private Button   discardButton;

    // ── 슬롯 1 (오른쪽) ──────────────────────────────────────
    [Header("슬롯 1")]
    [SerializeField] private Image    slot1Icon;
    [SerializeField] private TMP_Text slot1Name;
    [SerializeField] private TMP_Text slot1AtkText;
    [SerializeField] private TMP_Text slot1AtkDelta;
    [SerializeField] private TMP_Text slot1DefText;
    [SerializeField] private TMP_Text slot1DefDelta;
    [SerializeField] private TMP_Text slot1SkillText;
    [SerializeField] private Image    slot1QSkillIcon;
    [SerializeField] private Image    slot1ESkillIcon;
    [SerializeField] private Button   slot1ReplaceButton;

    // ── 툴팁 ──────────────────────────────────────────────────
    [Header("툴팁")]
    [SerializeField] private GameObject tooltipPanel;
    [SerializeField] private TMP_Text   tooltipName;
    [SerializeField] private TMP_Text   tooltipDesc;
    [SerializeField] private TMP_Text   tooltipCooldown;

    // ── 색상 ──────────────────────────────────────────────────
    private static readonly Color ColorUp      = new Color(0.20f, 0.90f, 0.30f);
    private static readonly Color ColorDown    = new Color(0.95f, 0.30f, 0.30f);
    private static readonly Color ColorNeutral = new Color(0.75f, 0.75f, 0.75f);

    private UniTaskCompletionSource<int?> _tcs;

    // ──────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────

    public void Setup(WeaponData slot0Data, WeaponData slot1Data, WeaponData incoming)
    {
        BindColumn(slot0Icon, slot0Name, slot0AtkText, slot0DefText, slot0SkillText, slot0Data);
        BindColumn(newIcon,   newName,   newAtkText,   newDefText,   newSkillText,   incoming);
        BindColumn(slot1Icon, slot1Name, slot1AtkText, slot1DefText, slot1SkillText, slot1Data);

        // 스킬 아이콘 바인딩
        BindSkillIcons(slot0QSkillIcon, slot0ESkillIcon, slot0Data);
        BindSkillIcons(newQSkillIcon,   newESkillIcon,   incoming);
        BindSkillIcons(slot1QSkillIcon, slot1ESkillIcon, slot1Data);

        // 툴팁 트리거 설정
        SetupTooltipTrigger(slot0QSkillIcon, slot0Data?.skillQ);
        SetupTooltipTrigger(slot0ESkillIcon, slot0Data?.skillE);
        SetupTooltipTrigger(newQSkillIcon,   incoming?.skillQ);
        SetupTooltipTrigger(newESkillIcon,   incoming?.skillE);
        SetupTooltipTrigger(slot1QSkillIcon, slot1Data?.skillQ);
        SetupTooltipTrigger(slot1ESkillIcon, slot1Data?.skillE);

        // 툴팁 초기 비활성
        if (tooltipPanel != null) tooltipPanel.SetActive(false);

        // 델타 표시
        ApplyDelta(slot0AtkText, slot0AtkDelta, slot0Data?.baseAttack ?? 0, incoming?.baseAttack ?? 0);
        ApplyDelta(slot0DefText, slot0DefDelta, slot0Data?.baseDefense ?? 0, incoming?.baseDefense ?? 0);
        ApplyDelta(slot1AtkText, slot1AtkDelta, slot1Data?.baseAttack ?? 0, incoming?.baseAttack ?? 0);
        ApplyDelta(slot1DefText, slot1DefDelta, slot1Data?.baseDefense ?? 0, incoming?.baseDefense ?? 0);

        _tcs = new UniTaskCompletionSource<int?>();

        slot0ReplaceButton?.onClick.RemoveAllListeners();
        slot0ReplaceButton?.onClick.AddListener(() => Complete(0));

        slot1ReplaceButton?.onClick.RemoveAllListeners();
        slot1ReplaceButton?.onClick.AddListener(() => Complete(1));

        discardButton?.onClick.RemoveAllListeners();
        discardButton?.onClick.AddListener(() => Complete(null));
    }

    public UniTask<int?> WaitForChoiceAsync() => _tcs.Task;

    /// <summary>
    /// 정상 경로(Complete) 없이 파괴돼도 대기를 끝낸다 — 씬 전환·CloseAllPopupUI 등.
    /// 이게 없으면 _tcs가 영구 미완료라 WaitForChoiceAsync가 무한 대기하고, 그 뒤 흐름(픽업 처리)이 멈춘다.
    /// null = '버림'(아무 슬롯도 교체 안 함)이라 아이템을 잃되 상태는 어긋나지 않는 안전한 기본값이다.
    /// Complete가 이미 결과를 넣었으면 TrySetResult가 false를 반환하고 무시된다.
    /// </summary>
    protected override void OnDestroy()
    {
        base.OnDestroy();   // 차단 잠금 누수 방지(UI_Popup)
        _tcs?.TrySetResult(null);
    }

    // ──────────────────────────────────────────────────────────
    // 내부
    // ──────────────────────────────────────────────────────────

    private void Complete(int? slotIndex)
    {
        _tcs?.TrySetResult(slotIndex);
        ClosePopupUI();
    }

    private static void BindColumn(Image icon, TMP_Text name, TMP_Text atk, TMP_Text def, TMP_Text skill, WeaponData data)
    {
        ApplyIcon(icon, data?.icon);
        SetText(name,  data?.displayName ?? "없음");
        SetText(atk,   $"ATK: {data?.baseAttack ?? 0f:F0}");
        SetText(def,   $"DEF: {data?.baseDefense ?? 0f:F0}");
        SetText(skill, data?.skillName ?? "---");
    }

    private static void BindSkillIcons(Image qIcon, Image eIcon, WeaponData data)
    {
        ApplyIcon(qIcon, data?.skillQIcon);
        ApplyIcon(eIcon, data?.skillEIcon);
    }

    private void SetupTooltipTrigger(Image icon, SkillSO skill)
    {
        if (icon == null) return;

        var trigger = icon.GetComponent<SkillTooltipTrigger>();
        if (trigger == null) trigger = icon.gameObject.AddComponent<SkillTooltipTrigger>();

        // 공유 툴팁 패널 연결 (SerializedField 접근 불가하므로 리플렉션 대신 직접 설정)
        trigger.SetData(skill);
        trigger.SetTooltipPanel(tooltipPanel, tooltipName, tooltipDesc, tooltipCooldown);
    }

    private void ApplyDelta(TMP_Text statText, TMP_Text deltaText, float slotValue, float newValue)
    {
        float delta = newValue - slotValue;
        if (statText != null)
        {
            if (Mathf.Approximately(delta, 0f)) statText.color = ColorNeutral;
            else statText.color = delta > 0 ? ColorDown : ColorUp;
        }
        if (deltaText != null)
        {
            if (Mathf.Approximately(delta, 0f))
            {
                deltaText.text = "—"; deltaText.color = ColorNeutral;
            }
            else if (delta > 0)
            {
                deltaText.text = $"▲+{delta:F0}"; deltaText.color = ColorDown;
            }
            else
            {
                deltaText.text = $"▼{delta:F0}"; deltaText.color = ColorUp;
            }
        }
    }

    private static void ApplyIcon(Image img, Sprite sprite)
    {
        if (img == null) return;
        img.sprite = sprite;
        img.color  = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0.15f);
    }

    private static void SetText(TMP_Text t, string v) { if (t) t.text = v; }
}
