using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 각성 패널 내 카테고리 카드 1개.
/// UI_AwakeningPanel에서 6개 인스턴스를 관리한다.
/// </summary>
public class AwakeningCategoryCardView : MonoBehaviour
{
    // ── 직렬화 필드 ──────────────────────────────────────────────────────
    [Header("카테고리 정보")]
    [SerializeField] private Image     iconImage;
    [SerializeField] private TMP_Text  categoryNameText;
    [SerializeField] private TMP_Text  levelText;

    [Header("레벨 Pip 바 (10개)")]
    [SerializeField] private Image[]   levelPips;   // Inspector에 10개 연결
    [SerializeField] private Color     pipFilled  = new Color(0.9f, 0.7f, 0.2f);
    [SerializeField] private Color     pipEmpty   = new Color(0.3f, 0.3f, 0.3f);

    [Header("효과 텍스트")]
    [SerializeField] private TMP_Text  currentEffectText;
    [SerializeField] private TMP_Text  nextEffectText;

    [Header("업그레이드 버튼")]
    [SerializeField] private Button    upgradeButton;
    [SerializeField] private TMP_Text  costText;
    [SerializeField] private Color     btnAffordable   = new Color(0.9f, 0.7f, 0.1f);
    [SerializeField] private Color     btnUnaffordable = new Color(0.5f, 0.5f, 0.5f);

    // ── 비공개 필드 ──────────────────────────────────────────────────────
    private Action _onUpgrade;

    // ── Public API ───────────────────────────────────────────────────────

    /// <summary>카드 데이터 갱신.</summary>
    public void Refresh(AwakeningCardData data)
    {
        if (categoryNameText) categoryNameText.text = data.DisplayName;

        int maxLevel = data.MaxLevel > 0 ? data.MaxLevel : 10;
        if (levelText) levelText.text = $"Lv {data.CurrentLevel}/{maxLevel}";

        RefreshPips(data.CurrentLevel, maxLevel);

        if (currentEffectText) currentEffectText.text = data.CurrentEffectText;

        bool maxed = data.CurrentLevel >= maxLevel;
        if (nextEffectText)
        {
            nextEffectText.text = maxed ? "최대 레벨" : $"다음: {data.NextEffectText}";
            nextEffectText.color = maxed ? new Color(1f, 0.8f, 0.3f) : Color.white;
        }

        bool canUpgrade = !maxed && data.CanAfford;
        if (upgradeButton)
        {
            // 비용을 못 내거나(정수 부족·cost 0 = 차트 미로드) 최대치면 눌리지 않는다.
            // 예전엔 !maxed만 봐서 cost 0인 상태에서 버튼이 계속 눌렸다.
            upgradeButton.interactable = canUpgrade;
            var colors = upgradeButton.colors;
            colors.normalColor = canUpgrade ? btnAffordable : btnUnaffordable;
            upgradeButton.colors = colors;
        }

        if (costText)
            costText.text = maxed ? "MAX" : $"{data.UpgradeCost} ◆";
    }

    /// <summary>업그레이드 버튼 콜백 등록.</summary>
    public void SetOnUpgrade(Action callback)
    {
        _onUpgrade = callback;
        if (upgradeButton != null)
        {
            upgradeButton.onClick.RemoveAllListeners();
            upgradeButton.onClick.AddListener(() => _onUpgrade?.Invoke());
        }
    }

    // ── 내부 ─────────────────────────────────────────────────────────────

    private void RefreshPips(int current, int max)
    {
        if (levelPips == null) return;
        for (int i = 0; i < levelPips.Length; i++)
        {
            if (levelPips[i] == null) continue;
            levelPips[i].color = i < current ? pipFilled : pipEmpty;
        }
    }
}

/// <summary>UI_AwakeningPanel → AwakeningCategoryCardView 데이터 전달 구조체.</summary>
public struct AwakeningCardData
{
    public string CategoryId;
    public string DisplayName;
    public int    CurrentLevel;
    public int    MaxLevel;
    public string CurrentEffectText;
    public string NextEffectText;
    public int    UpgradeCost;
    public bool   CanAfford;
}
