using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 무기 교체 팝업 — 현재 장비(왼쪽) vs 새 장비(오른쪽) 1:1 비교
///
/// slotIndex == 0 (메인): ATK / DEF 비교
/// slotIndex == 1 (서브): Q 스킬명 / Q 쿨다운 / 스킬 설명 비교
///
/// 레이아웃:
///   ┌──────────────────────────────────────────┐
///   │           새 무기 획득!                   │
///   │       ─── 메인 무기 / 서브 장비 ───       │
///   ├──────────────────┬───────────────────────┤
///   │   [현재 장비]     │      [새 장비]         │
///   │     아이콘        │       아이콘           │
///   │     이름          │       이름             │
///   │  -- 비교 항목 -- │  -- 비교 항목 --      │
///   ├──────────────────┴───────────────────────┤
///   │     [교체하기]          [버리기]           │
///   └──────────────────────────────────────────┘
/// </summary>
public class UI_WeaponReplacePopup : UI_Popup
{
    // ── 공통 ─────────────────────────────────────────────────────────
    [Header("공통")]
    [SerializeField] private TMP_Text slotLabelText;

    // ── 왼쪽 패널 (현재 장비) ─────────────────────────────────────────
    [Header("현재 장비")]
    [SerializeField] private Image    currentIcon;
    [SerializeField] private TMP_Text currentName;

    [Header("현재 — 메인 섹션")]
    [SerializeField] private GameObject currentMainSection;
    [SerializeField] private TMP_Text   currentAtkText;
    [SerializeField] private TMP_Text   currentDefText;

    [Header("현재 — 서브 섹션")]
    [SerializeField] private GameObject currentSubSection;
    [SerializeField] private TMP_Text   currentQSkillNameText;
    [SerializeField] private TMP_Text   currentQCoolText;
    [SerializeField] private TMP_Text   currentQDescText;

    // ── 오른쪽 패널 (새 장비) ─────────────────────────────────────────
    [Header("새 장비")]
    [SerializeField] private Image    newIcon;
    [SerializeField] private TMP_Text newName;

    [Header("새 장비 — 메인 섹션")]
    [SerializeField] private GameObject newMainSection;
    [SerializeField] private TMP_Text   newAtkText;
    [SerializeField] private TMP_Text   atkDeltaText;
    [SerializeField] private TMP_Text   newDefText;
    [SerializeField] private TMP_Text   defDeltaText;

    [Header("새 장비 — 서브 섹션")]
    [SerializeField] private GameObject newSubSection;
    [SerializeField] private TMP_Text   newQSkillNameText;
    [SerializeField] private TMP_Text   newQCoolText;
    [SerializeField] private TMP_Text   qCoolDeltaText;
    [SerializeField] private TMP_Text   newQDescText;

    // ── 버튼 ─────────────────────────────────────────────────────────
    [Header("버튼")]
    [SerializeField] private Button replaceButton;
    [SerializeField] private Button discardButton;

    // ── 색상 상수 ─────────────────────────────────────────────────────
    private static readonly Color ColorUp      = new Color(0.20f, 0.90f, 0.30f);
    private static readonly Color ColorDown    = new Color(0.95f, 0.30f, 0.30f);
    private static readonly Color ColorNeutral = new Color(0.75f, 0.75f, 0.75f);

    private UniTaskCompletionSource<bool> _tcs;

    // ──────────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 팝업 세팅. WaitForChoiceAsync() 로 결과를 기다린다.
    /// </summary>
    /// <param name="current">현재 장착 장비</param>
    /// <param name="incoming">새로 획득한 장비</param>
    /// <param name="slotIndex">0 = 메인, 1 = 서브</param>
    public void Setup(WeaponData current, WeaponData incoming, int slotIndex)
    {
        bool isMain = slotIndex == 0;

        SetText(slotLabelText, isMain ? "메인 무기" : "서브 장비");

        ApplyIcon(currentIcon, current?.icon);
        ApplyIcon(newIcon,     incoming?.icon);
        SetText(currentName, current?.displayName  ?? "없음");
        SetText(newName,     incoming?.displayName ?? "없음");

        currentMainSection?.SetActive(isMain);
        newMainSection?.SetActive(isMain);
        currentSubSection?.SetActive(!isMain);
        newSubSection?.SetActive(!isMain);

        if (isMain)
            SetupMainStats(current, incoming);
        else
            SetupSubStats(current, incoming);

        _tcs = new UniTaskCompletionSource<bool>();
        replaceButton?.onClick.RemoveAllListeners();
        replaceButton?.onClick.AddListener(() => Complete(true));
        discardButton?.onClick.RemoveAllListeners();
        discardButton?.onClick.AddListener(() => Complete(false));
    }

    /// <summary>true = 교체 / false = 버리기</summary>
    public UniTask<bool> WaitForChoiceAsync() => _tcs.Task;

    // ──────────────────────────────────────────────────────────────────
    // 섹션 바인딩
    // ──────────────────────────────────────────────────────────────────

    private void SetupMainStats(WeaponData current, WeaponData incoming)
    {
        float curAtk = current?.baseAttack   ?? 0f;
        float newAtk = incoming?.baseAttack  ?? 0f;
        float curDef = current?.baseDefense  ?? 0f;
        float newDef = incoming?.baseDefense ?? 0f;

        SetText(currentAtkText, $"{curAtk:F0}");
        SetText(currentDefText, $"{curDef:F0}");
        SetText(newAtkText,     $"{newAtk:F0}");
        SetText(newDefText,     $"{newDef:F0}");
        ApplyDelta(atkDeltaText, curAtk, newAtk);
        ApplyDelta(defDeltaText, curDef, newDef);
    }

    private void SetupSubStats(WeaponData current, WeaponData incoming)
    {
        SetText(currentQSkillNameText, current?.skillName        ?? "—");
        SetText(currentQCoolText,      FormatCool(current?.skillQCooldown));
        SetText(currentQDescText,      current?.skillDescription ?? "—");

        SetText(newQSkillNameText, incoming?.skillName        ?? "—");
        SetText(newQCoolText,      FormatCool(incoming?.skillQCooldown));
        SetText(newQDescText,      incoming?.skillDescription ?? "—");

        // 쿨다운은 낮을수록 좋으므로 색상 반전
        ApplyDelta(qCoolDeltaText,
            current?.skillQCooldown  ?? 0f,
            incoming?.skillQCooldown ?? 0f,
            invertColor: true);
    }

    // ──────────────────────────────────────────────────────────────────
    // 내부 헬퍼
    // ──────────────────────────────────────────────────────────────────

    private void Complete(bool replace)
    {
        _tcs?.TrySetResult(replace);
        ClosePopupUI();
    }

    private static void ApplyIcon(Image img, Sprite sprite)
    {
        if (img == null) return;
        img.sprite = sprite;
        img.color  = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0.15f);
    }

    private void ApplyDelta(TMP_Text deltaText, float curVal, float newVal, bool invertColor = false)
    {
        if (deltaText == null) return;

        float delta = newVal - curVal;
        if (Mathf.Approximately(delta, 0f))
        {
            deltaText.text  = "—";
            deltaText.color = ColorNeutral;
            return;
        }

        bool isPositive = delta > 0f;
        bool isGood     = invertColor ? !isPositive : isPositive;

        deltaText.text  = isPositive ? $"▲ +{delta:F1}" : $"▼ {delta:F1}";
        deltaText.color = isGood ? ColorUp : ColorDown;
    }

    private static string FormatCool(float? val) =>
        val.HasValue ? $"{val.Value:F1}s" : "—";

    private static void SetText(TMP_Text label, string value)
    {
        if (label != null) label.text = value;
    }
}
