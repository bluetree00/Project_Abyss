using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 기억의 제단 노드 <b>행</b> 1개. 갈래 열 안에 세로로 쌓인다.
///
/// <para><b>버튼이 없다.</b> 행은 고르는 것이고, 사는 것은 화면 하단 행동 바 한 곳에서만 일어난다.
/// 카드마다 버튼을 달았던 초안은 화면에 버튼이 18개 깔려 정작 행동 지점이 어디인지 흐려졌다.</para>
///
/// <para><b>세 단계로 물러난다</b> — 살 수 있음 / 곧 닿음 / 아직 먼 것.
/// 먼 것은 이름과 값만 남기고 높이가 줄어든다. <b>잠그는 것이 아니라 물러나는 것</b>이라
/// 「조건=할인, 아무도 막히지 않는다」 원칙을 깨지 않으면서 초반 소음만 걷어낸다.</para>
/// </summary>
public class AltarNodeRowView : MonoBehaviour
{
    // ── 상수 ─────────────────────────────────────────────────────────────
    /// <summary>보유 정수의 몇 배까지를 "곧 닿음"으로 볼 것인가. 그 너머는 이름만 남는다.</summary>
    public const float NearReachMultiplier = 2.5f;

    private const float FullHeight    = 66f;
    private const float DistantHeight = 42f;

    // ── 직렬화 필드 ──────────────────────────────────────────────────────
    [Header("바탕 · 상태 띠")]
    [SerializeField] private Image background;
    [SerializeField] private Image accentBar;
    [SerializeField] private Image selectionOutline;

    [Header("본문")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private TMP_Text conditionText;

    [Header("접근도 막대 — 보유 정수 / 가격")]
    [SerializeField] private RectTransform reachRow;
    [SerializeField] private Image reachFill;

    [Header("클릭")]
    [SerializeField] private Button selectButton;
    [SerializeField] private LayoutElement layoutElement;

    // ── 비공개 필드 ──────────────────────────────────────────────────────
    private Action _onSelect;

    // ── Public Methods ───────────────────────────────────────────────────

    public void Refresh(AltarNodeState state, int essence, bool selected)
    {
        var node = state.Node;
        if (node == null) return;

        // 살 수 있으면 절대 물러나지 않는다 — 행동 가능한 것이 숨으면 안 된다.
        bool distant = !state.Unlocked && !state.CanBuy && state.Cost > essence * NearReachMultiplier;

        if (layoutElement != null)
        {
            float h = distant ? DistantHeight : FullHeight;
            layoutElement.preferredHeight = h;
            layoutElement.minHeight       = h;
        }

        RefreshTone(state, selected);
        RefreshText(state, node, distant);
        RefreshReach(state, essence, distant);
    }

    public void SetOnSelect(Action callback)
    {
        _onSelect = callback;
        if (selectButton == null) return;

        selectButton.onClick.RemoveAllListeners();
        selectButton.onClick.AddListener(() => _onSelect?.Invoke());
    }

    // ── Private Methods ──────────────────────────────────────────────────

    private void RefreshTone(AltarNodeState state, bool selected)
    {
        if (background)
            background.color = state.Unlocked ? AltarPalette.CardUnlocked
                             : state.CanBuy   ? AltarPalette.CardBuyable
                                              : AltarPalette.CardIdle;

        if (accentBar)
            accentBar.color = state.Unlocked ? AltarPalette.Essence
                            : state.CanBuy   ? AltarPalette.Gold
                                             : AltarPalette.AccentLocked;

        if (selectionOutline) selectionOutline.gameObject.SetActive(selected);
    }

    private void RefreshText(AltarNodeState state, MemoryAltarNode node, bool distant)
    {
        if (nameText)
        {
            nameText.text     = node.DisplayName;
            nameText.color    = state.Unlocked ? AltarPalette.Essence
                              : distant        ? AltarPalette.TextFaint
                                               : AltarPalette.TextPrimary;
            nameText.fontSize = distant ? 15f : 17f;
        }

        if (costText)
        {
            costText.text  = state.Unlocked ? "✔" : $"{state.Cost:N0} ◆";
            costText.color = state.Unlocked ? AltarPalette.Essence
                           : state.CanBuy   ? AltarPalette.Gold
                                            : AltarPalette.TextDim;
            costText.fontSize = distant ? 14f : 17f;
        }

        // 먼 노드에서는 조건 줄을 감춘다 — 아직 볼 때가 아닌 정보다.
        if (conditionText)
        {
            conditionText.gameObject.SetActive(!distant && !state.Unlocked);
            if (!distant && !state.Unlocked)
                conditionText.text = BuildCondition(state, node);
        }
    }

    private static string BuildCondition(AltarNodeState state, MemoryAltarNode node)
    {
        if (!node.HasCondition) return "조건 없음";

        if (state.ConditionMet) return $"✔ {node.ConditionLabel}";

        string progress = $"{Mathf.Min(state.Progress, state.Target)}/{state.Target}";
        return node.ConditionRequired
            ? $"{node.ConditionLabel} {progress} · 필수"
            : $"{node.ConditionLabel} {progress} · 반값";
    }

    /// <summary>
    /// 보유 정수가 가격의 몇 %인지. <b>비싼 것으로 조금씩 다가가는 게 보여야</b>
    /// "문턱을 못 넘으면 빈손"이라는 인상이 생기지 않는다.
    /// </summary>
    private void RefreshReach(AltarNodeState state, int essence, bool distant)
    {
        if (reachRow == null) return;

        bool show = !state.Unlocked && !state.CanBuy && !distant;
        reachRow.gameObject.SetActive(show);

        if (show && reachFill != null)
            reachFill.fillAmount = state.Cost > 0 ? Mathf.Clamp01((float)essence / state.Cost) : 0f;
    }
}
