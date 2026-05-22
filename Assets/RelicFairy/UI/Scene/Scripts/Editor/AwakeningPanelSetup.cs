using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// LobbyRoot 프리팹 내 각성 패널 참조 + 레이아웃을 일괄 설정하는 에디터 유틸리티.
/// 메뉴: RelicFairy → Setup Awakening Panel
/// </summary>
public static class AwakeningPanelSetup
{
    private const string PrefabPath = "Assets/RelicFairy/UI/Scene/ScenePrefabs/LobbyRoot.prefab";

    [MenuItem("RelicFairy/Setup Awakening Panel")]
    public static void Run()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) { Debug.LogError($"[Setup] 프리팹 없음: {PrefabPath}"); return; }

        using var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath);
        var root = scope.prefabContentsRoot;

        SetupLayout(root);
        SetupReferences(root);

        Debug.Log("[Setup] 각성 패널 참조 + 레이아웃 설정 완료!");
    }

    // ═══════════════════════════════════════════════════════════════
    // 레이아웃
    // ═══════════════════════════════════════════════════════════════

    private static void SetupLayout(GameObject root)
    {
        var t = root.transform;

        // Panel_Awakening — 전체 화면 스트레치
        SetStretch(t, "Panel_Awakening");

        // Dim — 전체 화면 스트레치
        SetStretch(t, "Panel_Awakening/Dim");
        SetColor(t, "Panel_Awakening/Dim", new Color(0f, 0f, 0f, 0.75f));

        // Panel_Main — 중앙 고정 1440×860
        SetRect(t, "Panel_Awakening/Panel_Main",
            anchorMin: Vector2.one * 0.5f, anchorMax: Vector2.one * 0.5f,
            pivot: Vector2.one * 0.5f,
            size: new Vector2(1440, 860), pos: Vector2.zero);
        SetColor(t, "Panel_Awakening/Panel_Main", new Color(0.07f, 0.07f, 0.12f, 0.97f));

        // Header — 상단 고정 높이 80
        SetRect(t, "Panel_Awakening/Panel_Main/Header",
            anchorMin: new Vector2(0, 1), anchorMax: new Vector2(1, 1),
            pivot: new Vector2(0.5f, 1f),
            size: new Vector2(-40, 80), pos: new Vector2(0, -16));

        // Txt_Title — 헤더 좌측
        SetRect(t, "Panel_Awakening/Panel_Main/Header/Txt_Title",
            anchorMin: new Vector2(0, 0), anchorMax: new Vector2(0.55f, 1),
            pivot: new Vector2(0, 0.5f),
            size: new Vector2(0, 0), pos: new Vector2(12, 0));
        SetTMPAlign(t, "Panel_Awakening/Panel_Main/Header/Txt_Title", TextAlignmentOptions.MidlineLeft);

        // Txt_Essence — 헤더 우측
        SetRect(t, "Panel_Awakening/Panel_Main/Header/Txt_Essence",
            anchorMin: new Vector2(0.55f, 0), anchorMax: new Vector2(1, 1),
            pivot: new Vector2(1, 0.5f),
            size: new Vector2(-12, 0), pos: new Vector2(0, 0));
        SetTMPAlign(t, "Panel_Awakening/Panel_Main/Header/Txt_Essence", TextAlignmentOptions.MidlineRight);

        // CardGrid — 헤더 아래 ~ 하단 여백 80
        SetRect(t, "Panel_Awakening/Panel_Main/CardGrid",
            anchorMin: new Vector2(0, 0), anchorMax: new Vector2(1, 1),
            pivot: new Vector2(0.5f, 0.5f),
            size: new Vector2(-40, -176), pos: new Vector2(0, -48));
        SetupGridLayout(t.Find("Panel_Awakening/Panel_Main/CardGrid")?.GetComponent<GridLayoutGroup>());

        // Btn_Close — 우하단
        SetRect(t, "Panel_Awakening/Panel_Main/Btn_Close",
            anchorMin: new Vector2(1, 0), anchorMax: new Vector2(1, 0),
            pivot: new Vector2(1, 0),
            size: new Vector2(160, 56), pos: new Vector2(-20, 16));
        SetTMPAlign(t, "Panel_Awakening/Panel_Main/Btn_Close/Txt_Close", TextAlignmentOptions.Center);

        // Btn_Awakening (MenuPanel) — 기존 버튼들 위에 추가 (y=305)
        var btnAwakeningRT = root.transform.Find("MenuPanel/Btn_Awakening")?.GetComponent<RectTransform>();
        if (btnAwakeningRT != null)
        {
            btnAwakeningRT.anchorMin        = Vector2.zero;
            btnAwakeningRT.anchorMax        = Vector2.zero;
            btnAwakeningRT.pivot            = new Vector2(0.5f, 0.5f);
            btnAwakeningRT.sizeDelta        = new Vector2(300, 120);
            btnAwakeningRT.anchoredPosition = new Vector2(0, 305);
        }

        // 카드 내부 레이아웃
        string[] catIds = { "sword", "shield", "heart", "step", "mana", "luck" };
        foreach (var id in catIds)
            SetupCardLayout(t, $"Panel_Awakening/Panel_Main/CardGrid/Card_{id}");
    }

    private static void SetupGridLayout(GridLayoutGroup grid)
    {
        if (grid == null) return;
        grid.cellSize        = new Vector2(450, 260);
        grid.spacing         = new Vector2(20, 20);
        grid.padding         = new RectOffset(10, 10, 10, 10);
        grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.startCorner     = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis       = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment  = TextAnchor.UpperLeft;
    }

    private static void SetupCardLayout(Transform root, string cardPath)
    {
        var cardT = root.Find(cardPath);
        if (cardT == null) return;

        // 카드 자체 — GridLayoutGroup이 크기 관리
        // VerticalLayoutGroup 추가 (없으면)
        var vl = cardT.GetComponent<VerticalLayoutGroup>();
        if (vl == null) vl = cardT.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.childControlWidth   = true;
        vl.childControlHeight  = false;
        vl.childForceExpandWidth  = true;
        vl.childForceExpandHeight = false;
        vl.spacing  = 6;
        vl.padding  = new RectOffset(12, 12, 14, 12);

        // Txt_CategoryName
        SetPreferredHeight(cardT, "Txt_CategoryName", 36);
        // Txt_Level
        SetPreferredHeight(cardT, "Txt_Level", 28);
        // PipBar
        var pipBarT = cardT.Find("PipBar");
        if (pipBarT != null)
        {
            var pipRT = pipBarT.GetComponent<RectTransform>();
            if (pipRT) pipRT.sizeDelta = new Vector2(0, 20);
            var hl = pipBarT.GetComponent<HorizontalLayoutGroup>();
            if (hl == null) hl = pipBarT.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.childControlWidth  = false;
            hl.childControlHeight = false;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;
            hl.spacing = 4;

            // pip 크기
            foreach (Transform pip in pipBarT)
            {
                var rt = pip.GetComponent<RectTransform>();
                if (rt) rt.sizeDelta = new Vector2(38, 16);
            }
        }
        // Txt_CurrentEffect
        SetPreferredHeight(cardT, "Txt_CurrentEffect", 28);
        // Txt_NextEffect
        SetPreferredHeight(cardT, "Txt_NextEffect", 26);
        // Btn_Upgrade — 나머지 공간 채움
        var btnT = cardT.Find("Btn_Upgrade");
        if (btnT != null)
        {
            var btnRT = btnT.GetComponent<RectTransform>();
            if (btnRT) btnRT.sizeDelta = new Vector2(0, 50);
            SetTMPAlign(cardT, "Btn_Upgrade/Txt_Cost", TextAlignmentOptions.Center);
        }
    }

    private static void SetPreferredHeight(Transform parent, string childName, float height)
    {
        var childT = parent.Find(childName);
        if (childT == null) return;
        var rt = childT.GetComponent<RectTransform>();
        if (rt) rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
        var le = childT.GetComponent<LayoutElement>() ?? childT.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.minHeight       = height;
    }

    // ═══════════════════════════════════════════════════════════════
    // 참조 연결
    // ═══════════════════════════════════════════════════════════════

    private static void SetupReferences(GameObject root)
    {
        var panelGO = root.transform.Find("Panel_Awakening")?.gameObject;
        if (panelGO == null) { Debug.LogError("[Setup] Panel_Awakening 없음"); return; }

        var panel = panelGO.GetComponent<UI_AwakeningPanel>();
        if (panel == null) { Debug.LogError("[Setup] UI_AwakeningPanel 없음"); return; }

        var t = root.transform;

        SetField(panel, "essenceText", t.Find("Panel_Awakening/Panel_Main/Header/Txt_Essence")?.GetComponent<TMP_Text>());
        SetField(panel, "closeButton", t.Find("Panel_Awakening/Panel_Main/Btn_Close")?.GetComponent<Button>());

        string[] catIds = { "sword", "shield", "heart", "step", "mana", "luck" };
        var cardViews = new AwakeningCategoryCardView[catIds.Length];
        for (int i = 0; i < catIds.Length; i++)
        {
            var cardGO = t.Find($"Panel_Awakening/Panel_Main/CardGrid/Card_{catIds[i]}")?.gameObject;
            if (cardGO == null) continue;
            var card = cardGO.GetComponent<AwakeningCategoryCardView>();
            if (card == null) continue;
            cardViews[i] = card;
            SetupCardReferences(card, cardGO);
        }
        SetField(panel, "cards", cardViews);

        var lobby = root.GetComponent<UI_Lobby>();
        if (lobby != null) SetField(lobby, "awakeningPanel", panel);
    }

    private static void SetupCardReferences(AwakeningCategoryCardView card, GameObject cardGO)
    {
        var t = cardGO.transform;

        SetField(card, "categoryNameText", t.Find("Txt_CategoryName")?.GetComponent<TMP_Text>());
        SetField(card, "levelText",        t.Find("Txt_Level")?.GetComponent<TMP_Text>());
        SetField(card, "currentEffectText",t.Find("Txt_CurrentEffect")?.GetComponent<TMP_Text>());
        SetField(card, "nextEffectText",   t.Find("Txt_NextEffect")?.GetComponent<TMP_Text>());

        var btnUpgrade = t.Find("Btn_Upgrade")?.GetComponent<Button>();
        SetField(card, "upgradeButton", btnUpgrade);
        if (btnUpgrade != null)
            SetField(card, "costText", btnUpgrade.transform.Find("Txt_Cost")?.GetComponent<TMP_Text>());

        var pipBarT = t.Find("PipBar");
        if (pipBarT == null) return;

        // 중복 pip 정리 후 10개 확보
        for (int i = pipBarT.childCount - 1; i >= 10; i--)
            Object.DestroyImmediate(pipBarT.GetChild(i).gameObject);
        for (int i = pipBarT.childCount; i < 10; i++)
        {
            var go = new GameObject($"Pip_{i}");
            go.transform.SetParent(pipBarT, false);
            go.AddComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(38, 16);
        }

        var pips = Enumerable.Range(0, pipBarT.childCount)
            .Select(i => pipBarT.GetChild(i).GetComponent<Image>())
            .Where(img => img != null).Take(10).ToArray();
        SetField(card, "levelPips", pips);
    }

    // ═══════════════════════════════════════════════════════════════
    // RectTransform 헬퍼
    // ═══════════════════════════════════════════════════════════════

    private static void SetStretch(Transform root, string path)
    {
        var rt = root.Find(path)?.GetComponent<RectTransform>();
        if (rt == null) return;
        rt.anchorMin        = Vector2.zero;
        rt.anchorMax        = Vector2.one;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = Vector2.zero;
    }

    private static void SetRect(Transform root, string path,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Vector2 pos)
    {
        var rt = root.Find(path)?.GetComponent<RectTransform>();
        if (rt == null) { Debug.LogWarning($"[Setup] RectTransform 없음: {path}"); return; }
        rt.anchorMin        = anchorMin;
        rt.anchorMax        = anchorMax;
        rt.pivot            = pivot;
        rt.sizeDelta        = size;
        rt.anchoredPosition = pos;
    }

    private static void SetColor(Transform root, string path, Color color)
    {
        var img = root.Find(path)?.GetComponent<Image>();
        if (img != null) img.color = color;
    }

    private static void SetTMPAlign(Transform root, string path, TextAlignmentOptions align)
    {
        var tmp = root.Find(path)?.GetComponent<TMP_Text>();
        if (tmp != null) tmp.alignment = align;
    }

    // ═══════════════════════════════════════════════════════════════
    // SerializedObject 헬퍼
    // ═══════════════════════════════════════════════════════════════

    private static void SetField(Object target, string fieldName, Object value)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(fieldName);
        if (prop == null) { Debug.LogWarning($"[Setup] 필드 없음: {target.GetType().Name}.{fieldName}"); return; }
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetField(Object target, string fieldName, Object[] values)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(fieldName);
        if (prop == null) { Debug.LogWarning($"[Setup] 필드 없음: {target.GetType().Name}.{fieldName}"); return; }
        prop.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetField(Object target, string fieldName, Image[] values)
        => SetField(target, fieldName, values.Cast<Object>().ToArray());

    private static void SetField(Object target, string fieldName, AwakeningCategoryCardView[] values)
        => SetField(target, fieldName, values.Cast<Object>().ToArray());
}
