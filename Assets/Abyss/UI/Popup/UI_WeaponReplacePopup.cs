using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 무기 교체 팝업 — 현재 장착 무기 vs 새 무기 1:1 비교
///
/// 레이아웃 구조:
///   ┌──────────────────────────────────────┐
///   │  새 무기 획득!                        │
///   │  ─── 메인 무기 ───                   │
///   │                                     │
///   │  [현재]           [새 무기]           │
///   │  아이콘            아이콘             │
///   │  이름              이름              │
///   │                                     │
///   │  공격력   50  →  75   ▲ +25 (녹색)  │
///   │  방어력   10  →   5   ▼  -5 (빨강)  │
///   │                                     │
///   │     [교체하기]      [버리기]          │
///   └──────────────────────────────────────┘
/// </summary>
public class UI_WeaponReplacePopup : UI_Popup
{
    // ── 슬롯 레이블 ────────────────────────────────────────────────
    [Header("슬롯 레이블")]
    [SerializeField] private TMP_Text slotLabelText;        // "메인 무기" / "서브 무기"

    // ── 현재 무기 패널 ──────────────────────────────────────────────
    [Header("현재 무기")]
    [SerializeField] private Image    currentIcon;
    [SerializeField] private TMP_Text currentName;

    // ── 새 무기 패널 ────────────────────────────────────────────────
    [Header("새 무기")]
    [SerializeField] private Image    newIcon;
    [SerializeField] private TMP_Text newName;

    // ── 스탯 비교 — 공격력 ──────────────────────────────────────────
    [Header("스탯 비교 — 공격력")]
    [SerializeField] private TMP_Text atkCurrentText;       // 현재값
    [SerializeField] private TMP_Text atkNewText;           // 새 값
    [SerializeField] private TMP_Text atkDeltaText;         // ▲+25 / ▼-5 / —

    // ── 스탯 비교 — 방어력 ──────────────────────────────────────────
    [Header("스탯 비교 — 방어력")]
    [SerializeField] private TMP_Text defCurrentText;
    [SerializeField] private TMP_Text defNewText;
    [SerializeField] private TMP_Text defDeltaText;

    // ── 버튼 ────────────────────────────────────────────────────────
    [Header("버튼")]
    [SerializeField] private Button replaceButton;          // 교체하기
    [SerializeField] private Button discardButton;          // 버리기

    // ── 색상 상수 ────────────────────────────────────────────────────
    private static readonly Color ColorUp      = new Color(0.20f, 0.90f, 0.30f); // 녹색
    private static readonly Color ColorDown    = new Color(0.95f, 0.30f, 0.30f); // 빨강
    private static readonly Color ColorNeutral = new Color(0.75f, 0.75f, 0.75f); // 회색

    private UniTaskCompletionSource<bool> _tcs;

    // ──────────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 팝업 데이터 세팅. WaitForChoiceAsync() 로 결과를 기다린다.
    /// </summary>
    /// <param name="current">현재 장착 무기 (null이면 "없음" 표시)</param>
    /// <param name="incoming">새로 획득한 무기</param>
    /// <param name="slotIndex">0=메인, 1=서브</param>
    public void Setup(WeaponData current, WeaponData incoming, int slotIndex)
    {
        // 슬롯 레이블
        if (slotLabelText != null)
            slotLabelText.text = slotIndex == 0 ? "메인 무기" : "서브 무기";

        // 아이콘 / 이름
        ApplyIcon(currentIcon, current?.icon);
        ApplyIcon(newIcon,     incoming?.icon);
        if (currentName != null) currentName.text = current?.displayName  ?? "없음";
        if (newName     != null) newName.text     = incoming?.displayName ?? "없음";

        // 스탯 비교
        float curAtk = current?.baseAttack   ?? 0f;
        float newAtk = incoming?.baseAttack  ?? 0f;
        float curDef = current?.baseDefense  ?? 0f;
        float newDef = incoming?.baseDefense ?? 0f;

        ApplyStat(atkCurrentText, atkNewText, atkDeltaText, curAtk, newAtk);
        ApplyStat(defCurrentText, defNewText, defDeltaText, curDef, newDef);

        // 버튼
        _tcs = new UniTaskCompletionSource<bool>();

        replaceButton?.onClick.RemoveAllListeners();
        replaceButton?.onClick.AddListener(() => Complete(true));

        discardButton?.onClick.RemoveAllListeners();
        discardButton?.onClick.AddListener(() => Complete(false));
    }

    /// <summary>
    /// true = 교체하기 / false = 버리기(취소)
    /// </summary>
    public UniTask<bool> WaitForChoiceAsync() => _tcs.Task;

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

    private void ApplyStat(TMP_Text curText, TMP_Text newText, TMP_Text deltaText,
                            float curVal, float newVal)
    {
        if (curText != null) curText.text = $"{curVal:F0}";
        if (newText != null) newText.text = $"{newVal:F0}";

        if (deltaText == null) return;

        float delta = newVal - curVal;
        if (Mathf.Approximately(delta, 0f))
        {
            deltaText.text  = "—";
            deltaText.color = ColorNeutral;
        }
        else if (delta > 0f)
        {
            deltaText.text  = $"▲ +{delta:F0}";
            deltaText.color = ColorUp;
        }
        else
        {
            deltaText.text  = $"▼ {delta:F0}";
            deltaText.color = ColorDown;
        }
    }
}
