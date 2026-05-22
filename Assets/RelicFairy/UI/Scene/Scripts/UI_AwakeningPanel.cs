using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 각성 팝업.
/// 로비(Btn_Awakening)와 BaseCamp(WorldAwakeningAltar) 양쪽에서
/// Managers.UI.ShowPopupUI&lt;UI_AwakeningPanel&gt;() 로 열린다.
///
/// 각성 업그레이드 흐름:
///   카드 버튼 클릭 → UserGameData.TryUpgradeAwakening() → BackendGameData.SaveAsync() → Refresh()
/// </summary>
public class UI_AwakeningPanel : UI_Popup
{
    // ── 직렬화 필드 ────────────────────────────────────────────────────────
    [Header("헤더")]
    [SerializeField] private TMP_Text essenceText;

    [Header("카테고리 카드 (sword/shield/heart/step/mana/luck 순)")]
    [SerializeField] private AwakeningCategoryCardView[] cards;  // 6개

    [Header("닫기")]
    [SerializeField] private Button closeButton;

    // ── 카테고리 메타 ────────────────────────────────────────────────────
    private static readonly string[] CategoryIds =
        { AwakeningCategory.Sword, AwakeningCategory.Shield, AwakeningCategory.Heart,
          AwakeningCategory.Step,  AwakeningCategory.Mana,   AwakeningCategory.Luck };

    private static readonly string[] CategoryNames =
        { "검", "방패", "심장", "발걸음", "마력", "행운" };

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public override void Init()
    {
        base.Init();

        if (closeButton != null)
            closeButton.onClick.AddListener(ClosePopupUI);

        for (int i = 0; i < cards.Length && i < CategoryIds.Length; i++)
        {
            int idx = i;
            cards[i]?.SetOnUpgrade(() => OnUpgradeClicked(CategoryIds[idx]));
        }

        Refresh();
    }

    // ── 내부 ──────────────────────────────────────────────────────────────

    private void Refresh()
    {
        var userData = BackendGameData.Instance?.Data;
        var awakening = Managers.RelicAwakening;

        int essence = userData?.abyssEssence ?? 0;
        if (essenceText) essenceText.text = $"심연의 정수  {essence} ◆";

        for (int i = 0; i < cards.Length && i < CategoryIds.Length; i++)
        {
            if (cards[i] == null) continue;

            string catId     = CategoryIds[i];
            int    curLevel  = userData?.GetAwakeningLevel(catId) ?? 0;
            int    maxLevel  = awakening?.GetMaxLevel(catId) ?? 10;
            int    cost      = awakening?.GetUpgradeCost(catId, curLevel) ?? 0;

            string curEffect  = BuildCumulativeEffectText(catId, curLevel, awakening);
            string nextEffect = BuildNextEffectText(catId, curLevel + 1, awakening);

            cards[i].Refresh(new AwakeningCardData
            {
                CategoryId       = catId,
                DisplayName      = CategoryNames[i],
                CurrentLevel     = curLevel,
                MaxLevel         = maxLevel,
                CurrentEffectText = curEffect,
                NextEffectText   = nextEffect,
                UpgradeCost      = cost,
                CanAfford        = essence >= cost && cost > 0,
            });
        }
    }

    private void OnUpgradeClicked(string categoryId)
    {
        var userData = BackendGameData.Instance?.Data;
        if (userData == null) return;

        var awakening = Managers.RelicAwakening;
        int curLevel  = userData.GetAwakeningLevel(categoryId);
        int cost      = awakening?.GetUpgradeCost(categoryId, curLevel) ?? 0;

        if (!userData.TryUpgradeAwakening(categoryId, cost))
        {
            Debug.Log($"[UI_AwakeningPanel] 정수 부족 또는 최대 레벨: {categoryId}");
            return;
        }

        SaveAndRefreshAsync().Forget();
    }

    private async UniTaskVoid SaveAndRefreshAsync()
    {
        try
        {
            await BackendGameData.Instance.SaveAsync();
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e)
        {
            Debug.LogWarning($"[UI_AwakeningPanel] 저장 실패: {e.Message}");
        }

        Refresh();
    }

    // ── 효과 텍스트 빌더 ─────────────────────────────────────────────────

    private static string BuildCumulativeEffectText(string catId, int curLevel, RelicAwakeningDataManager mgr)
    {
        if (mgr == null || curLevel <= 0) return "—";

        float total = 0f;
        string statType = null;
        var entries = mgr.GetEntriesUpToLevel(catId, curLevel);
        foreach (var e in entries)
        {
            total += e.value;
            statType ??= e.stat_type;
        }

        return FormatEffect(statType, total, prefix: "+");
    }

    private static string BuildNextEffectText(string catId, int nextLevel, RelicAwakeningDataManager mgr)
    {
        if (mgr == null) return string.Empty;

        var entries = mgr.GetEntries(catId, nextLevel);
        if (entries == null || entries.Count == 0) return string.Empty;

        float total = 0f;
        string statType = null;
        foreach (var e in entries)
        {
            total += e.value;
            statType ??= e.stat_type;
        }

        return FormatEffect(statType, total, prefix: "+");
    }

    private static string FormatEffect(string statType, float value, string prefix = "")
    {
        if (string.IsNullOrEmpty(statType)) return string.Empty;

        return statType switch
        {
            "AttackPower"            => $"{prefix}{value:0} 공격력",
            "Defense"                => $"{prefix}{value:0} 방어력",
            "MaxHp"                  => $"{prefix}{value:0} 체력",
            "MoveSpeed"              => $"{prefix}{value * 100f:0.#}% 이동속도",
            "SkillCooldownReduction" => $"{prefix}{value * 100f:0.#}% 쿨타임↓",
            "Luck"                   => $"{prefix}{value:0} 행운",
            _                        => $"{prefix}{value}",
        };
    }
}
