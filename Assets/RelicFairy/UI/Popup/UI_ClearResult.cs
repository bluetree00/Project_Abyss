using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보상 획득 완료 팝업. 획득한 아이템 카드를 가로 리스트로 표시하고,
/// 카드 클릭 시 상세정보 오버레이를 보여준다.
/// Addressable 키: UI/Popup/UI_ClearResult
///
/// 프리팹 계층 구조 (Bind 이름과 정확히 일치해야 함):
///   UI_ClearResult (Root)
///   └── Panel
///         ├── ItemCardContainer  (GameObject — HorizontalLayoutGroup)
///         ├── ItemDetailPanel    (GameObject — 상세정보 오버레이, 초기 비활성)
///         │    ├── DetailName     (TextMeshProUGUI)
///         │    ├── DetailRarity   (TextMeshProUGUI)
///         │    ├── DetailEffects  (TextMeshProUGUI)
///         │    └── DetailCloseBtn (Button)
///         └── ConfirmBtn         (Button)
/// </summary>
public class UI_ClearResult : UI_Popup
{
    private enum GameObjects { ItemCardContainer, ItemDetailPanel }
    private enum TMPTexts    { DetailName, DetailRarity, DetailEffects }
    private enum Buttons     { ConfirmBtn, DetailCloseBtn }

    private UniTaskCompletionSource _confirmTcs;

    public override void Init()
    {
        base.Init();
        Bind<GameObject>(typeof(GameObjects));
        Bind<TextMeshProUGUI>(typeof(TMPTexts));
        Bind<Button>(typeof(Buttons));

        GetButton((int)Buttons.ConfirmBtn).onClick.AddListener(OnConfirmClicked);
        GetButton((int)Buttons.DetailCloseBtn).onClick.AddListener(HideDetail);

        HideDetail();
    }

    public void Setup(IReadOnlyList<(RuntimeItemData data, ItemSO so)> rewards)
    {
        var container = Get<GameObject>((int)GameObjects.ItemCardContainer);
        if (container == null) return;

        foreach (Transform child in container.transform)
            Destroy(child.gameObject);

        if (rewards == null) return;

        foreach (var (data, so) in rewards)
            SpawnItemCard(container.transform, data, so);
    }

    public UniTask WaitForConfirmAsync()
    {
        _confirmTcs = new UniTaskCompletionSource();
        return _confirmTcs.Task;
    }

    private void OnDestroy() => _confirmTcs?.TrySetResult();

    private void OnConfirmClicked()
    {
        _confirmTcs?.TrySetResult();
        ClosePopupUI();
    }

    // ── Detail Panel ───────────────────────────────────────────

    private void ShowDetail(RuntimeItemData data, ItemSO so)
    {
        var panel = Get<GameObject>((int)GameObjects.ItemDetailPanel);
        if (panel == null) return;
        panel.SetActive(true);

        GetTMPText((int)TMPTexts.DetailName).text = data?.displayName ?? "";

        var rarityText = GetTMPText((int)TMPTexts.DetailRarity);
        if (rarityText != null && data != null)
            rarityText.text = $"<color={GetRarityColor(data.rarity)}>{data.rarity}</color>";

        var effectsText = GetTMPText((int)TMPTexts.DetailEffects);
        if (effectsText != null)
            effectsText.text = BuildEffectText(data);
    }

    private void HideDetail()
    {
        Get<GameObject>((int)GameObjects.ItemDetailPanel)?.SetActive(false);
    }

    // ── Card Spawning ──────────────────────────────────────────

    private void SpawnItemCard(Transform parent, RuntimeItemData data, ItemSO so)
    {
        var cardGO = new GameObject("ItemCard");
        cardGO.transform.SetParent(parent, false);
        var cardRT = cardGO.AddComponent<RectTransform>();
        cardRT.sizeDelta = new Vector2(140f, 160f);

        // 클릭 처리를 위해 카드에 Image + Button 추가
        var cardImg = cardGO.AddComponent<Image>();
        cardImg.color = new Color(0f, 0f, 0f, 0f); // 투명 (레이캐스트용)
        var cardBtn = cardGO.AddComponent<Button>();
        cardBtn.onClick.AddListener(() => ShowDetail(data, so));

        // 아이콘 배경
        var iconBgGO = new GameObject("IconBG");
        iconBgGO.transform.SetParent(cardGO.transform, false);
        var iconBgImg = iconBgGO.AddComponent<Image>();
        iconBgImg.color = new Color(0.165f, 0.165f, 0.290f, 1f);
        var iconBgRT = iconBgGO.GetComponent<RectTransform>();
        iconBgRT.anchorMin        = new Vector2(0.5f, 0.5f);
        iconBgRT.anchorMax        = new Vector2(0.5f, 0.5f);
        iconBgRT.pivot            = new Vector2(0.5f, 0.5f);
        iconBgRT.sizeDelta        = new Vector2(120f, 120f);
        iconBgRT.anchoredPosition = new Vector2(0f, 20f);

        // 등급 테두리 색 표시
        iconBgImg.color = GetRarityBgColor(data?.rarity ?? ItemRarity.Common);

        // 아이콘 이미지
        var iconGO = new GameObject("Icon");
        iconGO.transform.SetParent(iconBgGO.transform, false);
        var iconImg = iconGO.AddComponent<Image>();
        var sprite  = so?.icon ?? data?.icon;
        iconImg.sprite = sprite;
        iconImg.gameObject.SetActive(sprite != null);
        var iconRT = iconGO.GetComponent<RectTransform>();
        iconRT.anchorMin = Vector2.zero;
        iconRT.anchorMax = Vector2.one;
        iconRT.offsetMin = new Vector2(8f, 8f);
        iconRT.offsetMax = new Vector2(-8f, -8f);

        // 아이템 이름
        var nameGO = new GameObject("CardName");
        nameGO.transform.SetParent(cardGO.transform, false);
        var nameTmp = nameGO.AddComponent<TextMeshProUGUI>();
        nameTmp.text      = data?.displayName ?? "";
        nameTmp.fontSize  = 13f;
        nameTmp.alignment = TextAlignmentOptions.Center;
        nameTmp.color     = Color.white;
        var nameRT = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin        = new Vector2(0.5f, 0.5f);
        nameRT.anchorMax        = new Vector2(0.5f, 0.5f);
        nameRT.pivot            = new Vector2(0.5f, 0.5f);
        nameRT.sizeDelta        = new Vector2(130f, 30f);
        nameRT.anchoredPosition = new Vector2(0f, -55f);
    }

    // ── Helpers ────────────────────────────────────────────────

    private static string BuildEffectText(RuntimeItemData data)
    {
        if (data?.effects == null || data.effects.Count == 0) return "효과 없음";
        var sb = new System.Text.StringBuilder();
        foreach (var eff in data.effects)
        {
            if (string.IsNullOrEmpty(eff.effectType)) continue;
            // 표시 전용 포맷터로 한글 라벨/단위/조건/CSV 원문 일원화(raw enum 제거).
            var display = EffectDescriptionFormatter.Describe(eff);
            sb.AppendLine(display.Combined);
        }
        return sb.ToString().TrimEnd();
    }

    private static string GetRarityColor(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Common => "#FFFFFF",
        ItemRarity.Rare   => "#00FFFF",
        ItemRarity.Epic   => "#CC66FF",
        _                 => "#FFFFFF",
    };

    private static Color GetRarityBgColor(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Common => new Color(0.165f, 0.165f, 0.290f, 1f),
        ItemRarity.Rare   => new Color(0.0f,   0.25f,  0.35f,  1f),
        ItemRarity.Epic   => new Color(0.25f,  0.1f,   0.35f,  1f),
        _                 => new Color(0.165f, 0.165f, 0.290f, 1f),
    };
}
