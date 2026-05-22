#if UNITY_EDITOR
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI_AwakeningPanel 독립 팝업 프리팹을 생성하고 Addressables에 등록.
/// LobbyRoot에서 Panel_Awakening 도 제거한다.
/// 메뉴: RelicFairy → Create Awakening Popup Prefab
/// </summary>
public static class AwakeningPopupCreator
{
    private const string PopupPath = "Assets/RelicFairy/UI/Popup/UI_AwakeningPanel.prefab";
    private const string LobbyPath = "Assets/RelicFairy/UI/Scene/ScenePrefabs/LobbyRoot.prefab";
    private const string Address   = "UI_AwakeningPanel";

    private static readonly string[] CatIds = { "sword", "shield", "heart", "step", "mana", "luck" };

    [MenuItem("RelicFairy/Create Awakening Popup Prefab")]
    public static void Run()
    {
        CreatePrefab();
        RemovePanelFromLobby();
        RegisterAddressable();
        Debug.Log("[AwakeningPopupCreator] 완료 — UI_AwakeningPanel.prefab 생성, Addressables 등록, LobbyRoot 정리");
    }

    // ── 프리팹 생성 ──────────────────────────────────────────────────────

    private static void CreatePrefab()
    {
        int layer = LayerMask.NameToLayer("UI");

        // Root: RectTransform + UI_AwakeningPanel
        var root = new GameObject("UI_AwakeningPanel") { layer = layer };
        root.AddComponent<RectTransform>();
        Stretch(root.GetComponent<RectTransform>());
        var panelComp = root.AddComponent<UI_AwakeningPanel>();

        // Dim (전체 화면 어두운 배경)
        var dim = MakeImage(root.transform, "Dim", layer, new Color(0f, 0f, 0f, 0.75f));
        Stretch(dim.rectTransform);

        // Panel_Main (1440×860 중앙 패널)
        var main = MakeImage(root.transform, "Panel_Main", layer, new Color(0.07f, 0.07f, 0.12f, 0.97f));
        SetRect(main.rectTransform,
            Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f,
            Vector2.zero, new Vector2(1440, 860));

        // Header (상단 높이 80)
        var header = NewContainer(main.transform, "Header", layer);
        SetRect(header.GetComponent<RectTransform>(),
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f),
            new Vector2(0, -16), new Vector2(-40, 80));

        // Txt_Title (헤더 좌측)
        var titleTMP = MakeTMP(header.transform, "Txt_Title", layer, 36,
            new Color(1f, 0.9f, 0.6f), TextAlignmentOptions.MidlineLeft);
        titleTMP.text = "유물 각성";
        titleTMP.fontStyle = FontStyles.Bold;
        SetRect(titleTMP.rectTransform,
            new Vector2(0, 0), new Vector2(0.55f, 1), new Vector2(0, 0.5f),
            new Vector2(12, 0), Vector2.zero);

        // Txt_Essence (헤더 우측) → essenceText 필드
        var essenceTMP = MakeTMP(header.transform, "Txt_Essence", layer, 28,
            new Color(0.9f, 0.8f, 0.3f), TextAlignmentOptions.MidlineRight);
        essenceTMP.text = "심연의 정수  0 ◆";
        SetRect(essenceTMP.rectTransform,
            new Vector2(0.55f, 0), new Vector2(1, 1), new Vector2(1, 0.5f),
            new Vector2(-12, 0), Vector2.zero);

        // CardGrid (3열 GridLayoutGroup)
        var grid = NewContainer(main.transform, "CardGrid", layer);
        SetRect(grid.GetComponent<RectTransform>(),
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            new Vector2(0, -48), new Vector2(-40, -176));
        var glg = grid.AddComponent<GridLayoutGroup>();
        glg.cellSize        = new Vector2(450, 260);
        glg.spacing         = new Vector2(20, 20);
        glg.padding         = new RectOffset(10, 10, 10, 10);
        glg.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = 3;
        glg.startCorner     = GridLayoutGroup.Corner.UpperLeft;
        glg.startAxis       = GridLayoutGroup.Axis.Horizontal;
        glg.childAlignment  = TextAnchor.UpperLeft;

        // 카드 6개
        var cardViews = CatIds.Select(id => CreateCard(grid.transform, id, layer)).ToArray();

        // Btn_Close (우하단)
        var closeGO = NewContainer(main.transform, "Btn_Close", layer);
        closeGO.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.25f, 1f);
        var closeBtn = closeGO.AddComponent<Button>();
        SetRect(closeGO.GetComponent<RectTransform>(),
            new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0),
            new Vector2(-20, 16), new Vector2(160, 56));
        var closeTMP = MakeTMP(closeGO.transform, "Txt_Close", layer, 24,
            Color.white, TextAlignmentOptions.Center);
        closeTMP.text = "닫기";
        Stretch(closeTMP.rectTransform);

        // SerializeField 연결 (UI_AwakeningPanel)
        var so = new SerializedObject(panelComp);
        so.FindProperty("essenceText").objectReferenceValue = essenceTMP;
        so.FindProperty("closeButton").objectReferenceValue = closeBtn;
        var cardsProp = so.FindProperty("cards");
        cardsProp.arraySize = cardViews.Length;
        for (int i = 0; i < cardViews.Length; i++)
            cardsProp.GetArrayElementAtIndex(i).objectReferenceValue = cardViews[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        // 프리팹 저장
        PrefabUtility.SaveAsPrefabAssetAndConnect(root, PopupPath, InteractionMode.AutomatedAction);
        Object.DestroyImmediate(root);
        AssetDatabase.Refresh();
        Debug.Log($"[AwakeningPopupCreator] 프리팹 저장: {PopupPath}");
    }

    private static AwakeningCategoryCardView CreateCard(Transform parent, string catId, int layer)
    {
        // 카드 루트
        var card = NewContainer(parent, $"Card_{catId}", layer);
        card.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f, 1f);
        var cardView = card.AddComponent<AwakeningCategoryCardView>();

        var vl = card.AddComponent<VerticalLayoutGroup>();
        vl.childControlWidth      = true;
        vl.childControlHeight     = false;
        vl.childForceExpandWidth  = true;
        vl.childForceExpandHeight = false;
        vl.spacing = 6;
        vl.padding = new RectOffset(12, 12, 14, 12);

        // Txt_CategoryName
        var catTMP = MakeTMP(card.transform, "Txt_CategoryName", layer,
            24, Color.white, TextAlignmentOptions.Left);
        AddLE(catTMP.gameObject, 36);

        // Txt_Level
        var lvlTMP = MakeTMP(card.transform, "Txt_Level", layer,
            20, new Color(0.7f, 0.7f, 0.7f), TextAlignmentOptions.Left);
        AddLE(lvlTMP.gameObject, 28);

        // PipBar (pip 10개)
        var pipBar = NewContainer(card.transform, "PipBar", layer);
        var hl = pipBar.AddComponent<HorizontalLayoutGroup>();
        hl.childControlWidth      = false;
        hl.childControlHeight     = false;
        hl.childForceExpandWidth  = false;
        hl.childForceExpandHeight = false;
        hl.spacing = 4;
        AddLE(pipBar, 20);

        var pips = new Image[10];
        for (int i = 0; i < 10; i++)
        {
            var pipGO  = NewContainer(pipBar.transform, $"Pip_{i}", layer);
            var pipImg = pipGO.AddComponent<Image>();
            pipImg.color = new Color(0.3f, 0.3f, 0.3f);
            pipGO.GetComponent<RectTransform>().sizeDelta = new Vector2(38, 16);
            pips[i] = pipImg;
        }

        // Txt_CurrentEffect
        var curTMP = MakeTMP(card.transform, "Txt_CurrentEffect", layer,
            18, new Color(0.9f, 0.7f, 0.2f), TextAlignmentOptions.Left);
        AddLE(curTMP.gameObject, 28);

        // Txt_NextEffect
        var nxtTMP = MakeTMP(card.transform, "Txt_NextEffect", layer,
            16, new Color(0.7f, 0.9f, 0.7f), TextAlignmentOptions.Left);
        AddLE(nxtTMP.gameObject, 26);

        // Btn_Upgrade
        var btnGO = NewContainer(card.transform, "Btn_Upgrade", layer);
        btnGO.AddComponent<Image>().color = new Color(0.9f, 0.7f, 0.1f);
        var btn = btnGO.AddComponent<Button>();
        btnGO.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 50);
        AddLE(btnGO, 50);

        var costTMP = MakeTMP(btnGO.transform, "Txt_Cost", layer,
            22, new Color(0.1f, 0.05f, 0f), TextAlignmentOptions.Center);
        costTMP.fontStyle = FontStyles.Bold;
        costTMP.text = "0 ◆";
        Stretch(costTMP.rectTransform);

        // AwakeningCategoryCardView SerializeField 연결
        var cso = new SerializedObject(cardView);
        cso.FindProperty("categoryNameText").objectReferenceValue  = catTMP;
        cso.FindProperty("levelText").objectReferenceValue         = lvlTMP;
        cso.FindProperty("currentEffectText").objectReferenceValue = curTMP;
        cso.FindProperty("nextEffectText").objectReferenceValue    = nxtTMP;
        cso.FindProperty("upgradeButton").objectReferenceValue     = btn;
        cso.FindProperty("costText").objectReferenceValue          = costTMP;
        var pipsProp = cso.FindProperty("levelPips");
        pipsProp.arraySize = pips.Length;
        for (int i = 0; i < pips.Length; i++)
            pipsProp.GetArrayElementAtIndex(i).objectReferenceValue = pips[i];
        cso.ApplyModifiedPropertiesWithoutUndo();

        return cardView;
    }

    // ── LobbyRoot 정리 ────────────────────────────────────────────────────

    private static void RemovePanelFromLobby()
    {
        if (!System.IO.File.Exists(LobbyPath))
        {
            Debug.LogWarning($"[AwakeningPopupCreator] {LobbyPath} 없음");
            return;
        }

        using var scope = new PrefabUtility.EditPrefabContentsScope(LobbyPath);
        var panel = scope.prefabContentsRoot.transform.Find("Panel_Awakening");
        if (panel == null)
        {
            Debug.Log("[AwakeningPopupCreator] Panel_Awakening 이미 없음");
            return;
        }
        Object.DestroyImmediate(panel.gameObject);
        Debug.Log("[AwakeningPopupCreator] Panel_Awakening LobbyRoot에서 제거 완료");
    }

    // ── Addressables 등록 ─────────────────────────────────────────────────

    private static void RegisterAddressable()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogWarning("[AwakeningPopupCreator] Addressable Settings 없음 — 수동 등록 필요");
            return;
        }
        var guid  = AssetDatabase.AssetPathToGUID(PopupPath);
        var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup, false, false);
        entry.address = Address;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[AwakeningPopupCreator] Addressables 등록: {Address}");
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────────

    private static GameObject NewContainer(Transform parent, string name, int layer)
    {
        var go = new GameObject(name) { layer = layer };
        go.AddComponent<RectTransform>();
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Image MakeImage(Transform parent, string name, int layer, Color color)
    {
        var go  = NewContainer(parent, name, layer);
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    private static TextMeshProUGUI MakeTMP(Transform parent, string name, int layer,
        float size, Color color, TextAlignmentOptions align)
    {
        var go  = NewContainer(parent, name, layer);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize      = size;
        tmp.color         = color;
        tmp.alignment     = align;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void AddLE(GameObject go, float height)
    {
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.minHeight       = height;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin        = Vector2.zero;
        rt.anchorMax        = Vector2.one;
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = Vector2.zero;
    }

    private static void SetRect(RectTransform rt,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin        = anchorMin;
        rt.anchorMax        = anchorMax;
        rt.pivot            = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
    }
}
#endif
