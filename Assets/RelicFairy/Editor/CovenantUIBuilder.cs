using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 서약 선택 UI 프리팹을 에디터에서 자동 생성하는 빌더.
/// 메뉴: RelicFairy/Build Covenant Choice Prefab
/// </summary>
public static class CovenantUIBuilder
{
    private const string PrefabSavePath  = "Assets/RelicFairy/UI/Popup/UI_CovenantChoice.prefab";
    private const string TriggerSavePath = "Assets/RelicFairy/UI/Popup/CovenantTestCanvas.prefab";

    // ── 색상 팔레트 ──────────────────────────────────────
    private static readonly Color ColorOverlay   = new Color(0f,    0f,    0f,    0.80f);
    private static readonly Color ColorPanel     = new Color(0.07f, 0.07f, 0.12f, 0.97f);
    private static readonly Color ColorCard      = new Color(0.10f, 0.10f, 0.18f, 1.00f);
    private static readonly Color ColorCardHover = new Color(0.16f, 0.14f, 0.28f, 1.00f);
    private static readonly Color ColorBtn       = new Color(0.40f, 0.20f, 0.70f, 1.00f);
    private static readonly Color ColorTitle     = new Color(0.90f, 0.85f, 0.60f, 1.00f);
    private static readonly Color ColorLore      = new Color(0.65f, 0.65f, 0.80f, 1.00f);
    private static readonly Color ColorBasic     = new Color(0.80f, 0.80f, 0.80f, 1.00f);
    private static readonly Color ColorEnhanced  = new Color(0.40f, 0.80f, 1.00f, 1.00f);
    private static readonly Color ColorEvolved   = new Color(1.00f, 0.75f, 0.20f, 1.00f);

    [MenuItem("RelicFairy/UI/Build Covenant Choice Prefab")]
    public static void Build()
    {
        // ── 루트 (UI_CovenantChoice) ──────────────────────
        var root = new GameObject("UI_CovenantChoice");
        SetStretch(root);
        root.AddComponent<UI_CovenantChoice>();

        // ── 배경 오버레이 ──────────────────────────────────
        var bg = CreateImage(root, "BG", ColorOverlay);
        SetStretch(bg);

        // ── 메인 패널 ────────────────────────────────────
        var panel = CreateImage(root, "Panel", ColorPanel);
        SetAnchored(panel, new Vector2(1600f, 660f), Vector2.zero);
        AddOutline(panel);

        // ── 제목 텍스트 ──────────────────────────────────
        var title = CreateTMP(panel, "Title_Text", "서약을 선택하세요", 30f, ColorTitle, FontStyles.Bold);
        var titleRT = title.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0f, 1f);
        titleRT.anchorMax = new Vector2(1f, 1f);
        titleRT.pivot     = new Vector2(0.5f, 1f);
        titleRT.anchoredPosition = new Vector2(0f, -20f);
        titleRT.sizeDelta = new Vector2(-40f, 44f);

        // ── 카드 컨테이너 ────────────────────────────────
        var container = new GameObject("CardContainer");
        container.transform.SetParent(panel.transform, false);
        var containerRT = container.AddComponent<RectTransform>();
        containerRT.anchorMin        = new Vector2(0f, 0f);
        containerRT.anchorMax        = new Vector2(1f, 1f);
        containerRT.offsetMin        = new Vector2(24f, 16f);
        containerRT.offsetMax        = new Vector2(-24f, -72f);
        var hlg = container.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = 16f;
        hlg.childAlignment         = TextAnchor.UpperCenter;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = true;
        hlg.childForceExpandHeight = true;
        hlg.padding = new RectOffset(0, 0, 0, 0);

        // ── 카드 3장 ────────────────────────────────────
        string[] cardNames = { "Card0", "Card1", "Card2" };
        var cards = new UI_CovenantCard[3];
        for (int i = 0; i < 3; i++)
            cards[i] = BuildCard(container, cardNames[i]);

        // ── UI_CovenantChoice 직렬화 필드 연결 ─────────
        var choiceComp = root.GetComponent<UI_CovenantChoice>();
        var so = new SerializedObject(choiceComp);
        so.FindProperty("_titleText").objectReferenceValue = title;
        var cardsProp = so.FindProperty("_cards");
        cardsProp.arraySize = 3;
        for (int i = 0; i < 3; i++)
            cardsProp.GetArrayElementAtIndex(i).objectReferenceValue = cards[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        // ── 프리팹 저장 ─────────────────────────────────
        EnsureDir("Assets/RelicFairy/UI/Popup");
        PrefabUtility.SaveAsPrefabAsset(root, PrefabSavePath);
        Object.DestroyImmediate(root);

        // ── 테스트 캔버스 + 트리거 씬 오브젝트 ──────────
        BuildTestTrigger();

        AssetDatabase.Refresh();
        Debug.Log($"[CovenantUIBuilder] 완료! 프리팹: {PrefabSavePath}");
    }

    // ── 카드 빌더 ─────────────────────────────────────────
    private static UI_CovenantCard BuildCard(GameObject parent, string cardName)
    {
        // ── 카드 루트 ─────────────────────────────────────
        var card     = CreateImage(parent, cardName, ColorCard);
        var cardComp = card.AddComponent<UI_CovenantCard>();
        AddOutline(card);

        var cardVlg = card.AddComponent<VerticalLayoutGroup>();
        cardVlg.padding                = new RectOffset(0, 0, 0, 0);
        cardVlg.spacing                = 0f;
        cardVlg.childAlignment         = TextAnchor.UpperCenter;
        cardVlg.childControlWidth      = true;
        cardVlg.childControlHeight     = true;
        cardVlg.childForceExpandWidth  = true;
        cardVlg.childForceExpandHeight = false;

        // ── [1] 상단 스트라이프 (6px) ─────────────────────
        var stripe   = CreateImage(card, "TopStripe", new Color(0.50f, 0.28f, 0.85f, 1f));
        stripe.AddComponent<LayoutElement>().preferredHeight = 6f;

        // ── [2] 콘텐츠 (남은 공간 전부) ───────────────────
        var content = new GameObject("Content");
        content.transform.SetParent(card.transform, false);
        content.AddComponent<RectTransform>();
        content.AddComponent<LayoutElement>().flexibleHeight = 1f;

        var contentVlg = content.AddComponent<VerticalLayoutGroup>();
        contentVlg.padding                = new RectOffset(20, 20, 24, 16);
        contentVlg.spacing                = 14f;
        contentVlg.childAlignment         = TextAnchor.UpperCenter;
        contentVlg.childControlWidth      = true;
        contentVlg.childControlHeight     = true;
        contentVlg.childForceExpandWidth  = true;
        contentVlg.childForceExpandHeight = false;

        // 아이콘
        var iconGO = CreateImage(content, "Icon", new Color(1f, 1f, 1f, 0.12f));
        var iconLE  = iconGO.AddComponent<LayoutElement>();
        iconLE.preferredHeight = 96f;
        iconLE.preferredWidth  = 96f;
        iconLE.flexibleWidth   = 0f;

        // 이름
        var nameTMP = CreateTMP(content, "Name_Text", "서약 이름", 22f, Color.white, FontStyles.Bold);
        nameTMP.enableAutoSizing = true;
        nameTMP.fontSizeMin      = 16f;
        nameTMP.fontSizeMax      = 24f;
        nameTMP.alignment        = TextAlignmentOptions.Center;
        nameTMP.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;

        // 설화
        var loreTMP = CreateTMP(content, "Lore_Text", "설화 텍스트", 13f, ColorLore, FontStyles.Italic);
        loreTMP.alignment = TextAlignmentOptions.Center;
        loreTMP.gameObject.AddComponent<LayoutElement>().preferredHeight = 54f;

        // 구분선
        var divider   = CreateImage(content, "Divider", new Color(0.5f, 0.4f, 0.8f, 0.4f));
        divider.AddComponent<LayoutElement>().preferredHeight = 1f;

        // 기본 효과 (Rich Text 강조 적용됨)
        var basicTMP = CreateTMP(content, "BasicDesc_Text", "기본 효과 설명", 15f, ColorBasic, FontStyles.Normal);
        basicTMP.alignment   = TextAlignmentOptions.Center;
        basicTMP.richText    = true;
        var basicLE = basicTMP.gameObject.AddComponent<LayoutElement>();
        basicLE.flexibleHeight = 1f;
        basicLE.minHeight      = 64f;

        // ── [3] 버튼 영역 (76px 고정) ────────────────────
        var btnWrapper = CreateImage(card, "ButtonWrapper", Color.clear);
        btnWrapper.AddComponent<LayoutElement>().preferredHeight = 76f;

        var btnGO = CreateImage(btnWrapper, "SelectButton", ColorBtn);
        var btn   = btnGO.AddComponent<Button>();
        var btnRT = btnGO.GetComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0.08f, 0.14f);
        btnRT.anchorMax = new Vector2(0.92f, 0.86f);
        btnRT.offsetMin = Vector2.zero;
        btnRT.offsetMax = Vector2.zero;
        var cb = btn.colors;
        cb.normalColor      = Color.white;
        cb.highlightedColor = new Color(0.80f, 0.65f, 1.00f, 1f);
        cb.pressedColor     = new Color(0.25f, 0.10f, 0.50f, 1f);
        btn.colors = cb;
        var btnTxt = CreateTMP(btnGO, "SelectButtonText", "선택", 19f, Color.white, FontStyles.Bold);
        btnTxt.alignment = TextAlignmentOptions.Center;
        SetStretch(btnTxt.gameObject);

        // ── [4] 플래시 오버레이 (레이아웃 외, 최상단 렌더) ─
        var flash   = CreateImage(card, "FlashOverlay", new Color(1f, 1f, 1f, 0f));
        flash.GetComponent<Image>().raycastTarget = false; // 투명해도 클릭 차단하지 않도록
        var flashLE = flash.AddComponent<LayoutElement>();
        flashLE.ignoreLayout = true;
        SetStretch(flash.gameObject);

        // ── UI_CovenantCard 필드 연결 ─────────────────────
        var so = new SerializedObject(cardComp);
        so.FindProperty("_icon").objectReferenceValue          = iconGO.GetComponent<Image>();
        so.FindProperty("_nameText").objectReferenceValue      = nameTMP;
        so.FindProperty("_loreText").objectReferenceValue      = loreTMP;
        so.FindProperty("_basicDesc").objectReferenceValue     = basicTMP;
        so.FindProperty("_selectButton").objectReferenceValue  = btn;
        so.FindProperty("_flashOverlay").objectReferenceValue  = flash.GetComponent<Image>();
        so.ApplyModifiedPropertiesWithoutUndo();

        return cardComp;
    }

    // ── 테스트 트리거 씬 오브젝트 ──────────────────────────
    [MenuItem("RelicFairy/UI/Place Covenant Test Trigger")]
    public static void PlaceTestTrigger()
    {
        // 기존 인스턴스 제거 (중복 방지)
        foreach (var existing in Object.FindObjectsByType<CovenantChoiceTestTrigger>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(existing.transform.root.gameObject);

        var canvasGO = new GameObject("CovenantTestCanvas");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        var triggerGO = new GameObject("CovenantTestTrigger");
        triggerGO.transform.SetParent(canvasGO.transform, false);
        var trigger = triggerGO.AddComponent<CovenantChoiceTestTrigger>();

        var popupPrefab = AssetDatabase.LoadAssetAtPath<UI_CovenantChoice>(PrefabSavePath);
        var so = new SerializedObject(trigger);
        so.FindProperty("_popupPrefab").objectReferenceValue = popupPrefab;
        so.FindProperty("_popupParent").objectReferenceValue = canvasGO.transform;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 씬에 직접 배치
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        Debug.Log("[CovenantUIBuilder] CovenantTestCanvas 씬에 배치 완료. T 키로 테스트.");
    }

    private static void BuildTestTrigger() { /* 별도 메뉴로 분리됨 */ }

    // ── 유틸리티 ──────────────────────────────────────────

    private static GameObject CreateImage(GameObject parent, string name, Color color)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.color = color;
        return go;
    }

    private static TMP_Text CreateTMP(GameObject parent, string name, string text, float size, Color color, FontStyles style)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<RectTransform>();
        var tmp   = go.AddComponent<TextMeshProUGUI>();
        tmp.text       = text;
        tmp.fontSize   = size;
        tmp.color      = color;
        tmp.fontStyle  = style;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode       = TextOverflowModes.Truncate;
        return tmp;
    }

    private static void SetStretch(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) rt = go.AddComponent<RectTransform>();
        rt.anchorMin  = Vector2.zero;
        rt.anchorMax  = Vector2.one;
        rt.offsetMin  = Vector2.zero;
        rt.offsetMax  = Vector2.zero;
    }

    private static void SetAnchored(GameObject go, Vector2 size, Vector2 pivot)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = size;
    }

    private static void AddOutline(GameObject go)
    {
        var outline = go.AddComponent<Outline>();
        outline.effectColor    = new Color(0.4f, 0.3f, 0.7f, 0.6f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
    }

    private static void AddLayoutElement(Component comp, float preferredHeight)
    {
        var le = comp.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = preferredHeight;
        le.flexibleWidth   = 1f;
    }

    private static void EnsureDir(string path)
    {
        if (!AssetDatabase.IsValidFolder(path))
        {
            var parts  = path.Split('/');
            var parent = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var full = parent + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(full))
                    AssetDatabase.CreateFolder(parent, parts[i]);
                parent = full;
            }
        }
    }
}
