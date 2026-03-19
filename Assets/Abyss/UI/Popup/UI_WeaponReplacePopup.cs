using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 무기 교체 팝업 — 헤더 좌우 분리 + 스탯 중앙 비교
///
/// 레이아웃:
///   ┌────────────────────┬─────────────────────────┐
///   │    [현재 장비]      │       [새 장비]          │
///   │      아이콘         │        아이콘            │
///   │      이름           │        이름              │
///   ├────────────────────┴─────────────────────────┤
///   │  공격력    45        ▲ +27        72         │  ← 메인
///   │  방어력    10        ▼  -5         5         │
///   ├──────────────────────────────────────────────┤
///   │  Q 스킬  화염의 화살    →    폭발의 화살      │  ← 서브
///   │  Q 쿨    5.0s        ▲ -1.0s     4.0s       │
///   └──────────────────────────────────────────────┘
/// </summary>
public class UI_WeaponReplacePopup : UI_Popup
{
    // ── 공통 ──────────────────────────────────────────────────────────
    [Header("공통")]
    [SerializeField] private TMP_Text slotLabelText;

    // ── 헤더: 현재 장비 (왼쪽) ────────────────────────────────────────
    [Header("현재 장비 헤더")]
    [SerializeField] private Image    currentIcon;
    [SerializeField] private TMP_Text currentName;

    // ── 헤더: 새 장비 (오른쪽) ────────────────────────────────────────
    [Header("새 장비 헤더")]
    [SerializeField] private Image    newIcon;
    [SerializeField] private TMP_Text newName;

    // ── 스탯 비교: 메인 섹션 (전체 폭) ───────────────────────────────
    // 행 구조: [라벨] [현재값] [▲▼ 델타] [새값]
    [Header("메인 스탯 섹션")]
    [SerializeField] private GameObject mainStatsSection;
    [SerializeField] private TMP_Text   atkCurrentText;
    [SerializeField] private TMP_Text   atkDeltaText;
    [SerializeField] private TMP_Text   atkNewText;
    [SerializeField] private TMP_Text   defCurrentText;
    [SerializeField] private TMP_Text   defDeltaText;
    [SerializeField] private TMP_Text   defNewText;

    // ── 스탯 비교: 서브 섹션 (전체 폭) ───────────────────────────────
    // Q스킬명: [현재] [→] [새]
    // Q쿨다운: [현재] [▲▼ 델타] [새]
    // 스킬설명: [현재] | [새]
    [Header("서브 스탯 섹션")]
    [SerializeField] private GameObject subStatsSection;
    [SerializeField] private TMP_Text   qSkillCurrentText;
    [SerializeField] private TMP_Text   qSkillNewText;
    [SerializeField] private TMP_Text   qCoolCurrentText;
    [SerializeField] private TMP_Text   qCoolDeltaText;
    [SerializeField] private TMP_Text   qCoolNewText;
    [SerializeField] private TMP_Text   qDescCurrentText;
    [SerializeField] private TMP_Text   qDescNewText;

    // ── 버튼 ──────────────────────────────────────────────────────────
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

    public void Setup(WeaponData current, WeaponData incoming, int slotIndex)
    {
        bool isMain = slotIndex == 0;

        SetText(slotLabelText, isMain ? "메인 무기" : "서브 장비");

        ApplyIcon(currentIcon, current?.icon);
        ApplyIcon(newIcon,     incoming?.icon);
        SetText(currentName, current?.displayName  ?? "없음");
        SetText(newName,     incoming?.displayName ?? "없음");

        mainStatsSection?.SetActive(isMain);
        subStatsSection?.SetActive(!isMain);

        if (isMain) BindMainStats(current, incoming);
        else        BindSubStats(current, incoming);

        _tcs = new UniTaskCompletionSource<bool>();
        replaceButton?.onClick.RemoveAllListeners();
        replaceButton?.onClick.AddListener(() => Complete(true));
        discardButton?.onClick.RemoveAllListeners();
        discardButton?.onClick.AddListener(() => Complete(false));
    }

    public UniTask<bool> WaitForChoiceAsync() => _tcs.Task;

    // ──────────────────────────────────────────────────────────────────
    // 바인딩
    // ──────────────────────────────────────────────────────────────────

    private void BindMainStats(WeaponData cur, WeaponData inc)
    {
        SetText(atkCurrentText, $"{cur?.baseAttack  ?? 0f:F0}");
        SetText(atkNewText,     $"{inc?.baseAttack  ?? 0f:F0}");
        ApplyDelta(atkDeltaText, cur?.baseAttack ?? 0f, inc?.baseAttack ?? 0f);

        SetText(defCurrentText, $"{cur?.baseDefense ?? 0f:F0}");
        SetText(defNewText,     $"{inc?.baseDefense ?? 0f:F0}");
        ApplyDelta(defDeltaText, cur?.baseDefense ?? 0f, inc?.baseDefense ?? 0f);
    }

    private void BindSubStats(WeaponData cur, WeaponData inc)
    {
        SetText(qSkillCurrentText, cur?.skillName  ?? "—");
        SetText(qSkillNewText,     inc?.skillName  ?? "—");

        SetText(qCoolCurrentText, FormatCool(cur?.skillQCooldown));
        SetText(qCoolNewText,     FormatCool(inc?.skillQCooldown));
        ApplyDelta(qCoolDeltaText,
            cur?.skillQCooldown ?? 0f,
            inc?.skillQCooldown ?? 0f,
            invertColor: true);

        SetText(qDescCurrentText, cur?.skillDescription ?? "—");
        SetText(qDescNewText,     inc?.skillDescription ?? "—");
    }

    // ──────────────────────────────────────────────────────────────────
    // 내부
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

    private void ApplyDelta(TMP_Text t, float cur, float inc, bool invertColor = false)
    {
        if (t == null) return;
        float delta = inc - cur;
        if (Mathf.Approximately(delta, 0f))
        {
            t.text = "—"; t.color = ColorNeutral; return;
        }
        bool positive = delta > 0f;
        bool good     = invertColor ? !positive : positive;
        t.text  = positive ? $"▲ +{delta:F1}" : $"▼ {delta:F1}";
        t.color = good ? ColorUp : ColorDown;
    }

    private static string FormatCool(float? v) => v.HasValue ? $"{v.Value:F1}s" : "—";

    private static void SetText(TMP_Text t, string v) { if (t) t.text = v; }
}
