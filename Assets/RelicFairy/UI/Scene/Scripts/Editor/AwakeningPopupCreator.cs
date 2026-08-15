#if UNITY_EDITOR
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「기억의 제단」 팝업 프리팹을 생성하고 Addressables에 등록. LobbyRoot에서 Panel_Awakening 도 제거한다.
/// 메뉴: RelicFairy → Create Awakening Popup Prefab
/// <para>레이아웃이 <b>각성 6계열 카드 → 4갈래 탭 + 해금 노드</b>로 바뀌었다.
/// 카드는 런타임에 갈래별로 복제되므로, 여기서는 <b>비활성 템플릿 1장</b>만 만든다.</para>
/// </summary>
public static class AwakeningPopupCreator
{
    private const string PopupPath = "Assets/RelicFairy/UI/Popup/UI_AwakeningPanel.prefab";
    private const string LobbyPath = "Assets/RelicFairy/UI/Scene/ScenePrefabs/LobbyRoot.prefab";
    /// <summary>팝업 주소는 <c>UI/Popup/{타입명}</c> 규약이다 — <c>UIManager.ShowPopupUIAndGetAsync</c>가
    /// <c>$"UI/Popup/{name}"</c>로 키를 만든다. 접두어 없이 등록하면 로드가 InvalidKeyException으로 죽는다.</summary>
    private const string Address   = "UI/Popup/UI_AwakeningPanel";

    private static readonly AltarBranch[] Branches =
        { AltarBranch.Start, AltarBranch.Appear, AltarBranch.Endure, AltarBranch.Abyss };

    // ── 레이아웃 치수 ─────────────────────────────────────────────────────
    // 한 갈래 최대 8노드(Ⅱ 등장) = 4열 × 2행이 <b>잘리지 않고</b> 들어가야 한다.
    // 초안은 카드 높이가 내용보다 작아 첫 행 버튼이 찌그러졌다 — 아래 숫자는 그 역산이다.
    private const float PanelW  = 1560f;
    private const float PanelH  = 1000f;
    private const float Pad     = 44f;
    private const float HeaderH = 172f;   // 제목 + 정수 + 탭 + 구분선
    private const float ColsH   = 660f;   // 열머리 66 + 간격 6 + 본문 588. Ⅱ 등장 8노드 = 8×66 + 7×6 = 570
    private const float ActionH = 126f;

    private static readonly string[] Questions =
        { "무엇으로 시작하는가", "무엇이 나올 수 있는가", "얼마나 버틸 수 있는가", "얼마나 깊이 갈 수 있는가" };

    private static readonly Color CInk     = new(0.910f, 0.890f, 0.960f);
    private static readonly Color CDim     = new(0.494f, 0.471f, 0.588f);
    private static readonly Color CFaint   = new(0.306f, 0.290f, 0.380f);
    private static readonly Color CGold    = new(0.890f, 0.659f, 0.298f);
    private static readonly Color CEssence = new(0.388f, 0.851f, 0.749f);
    private static readonly Color CLine    = new(0.133f, 0.118f, 0.200f, 1f);

    [MenuItem("RelicFairy/UI/Create Awakening Popup Prefab")]
    public static void Run()
    {
        CreatePrefab();
        RemovePanelFromLobby();
        RegisterAddressable();
        Debug.Log("[AwakeningPopupCreator] 완료 — 기억의 제단 프리팹 생성, Addressables 등록, LobbyRoot 정리");
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

        // Panel_Main
        var main = MakeImage(root.transform, "Panel_Main", layer, new Color(0.059f, 0.055f, 0.094f, 0.99f));
        SetRect(main.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f,
            Vector2.zero, new Vector2(PanelW, PanelH));

        var TopLeft  = new Vector2(0f, 1f);
        var TopRight = new Vector2(1f, 1f);

        // ── 헤더 — 정수를 크게. 이 화면의 주어다.
        var titleTMP = MakeTMP(main.transform, "Txt_Title", layer, 38,
            new Color(1f, 0.89f, 0.63f), TextAlignmentOptions.TopLeft);
        titleTMP.text = "기억의 제단";
        SetRect(titleTMP.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(Pad, -26), new Vector2(560, 46));

        var essLabel = MakeTMP(main.transform, "Txt_EssenceLabel", layer, 12, CDim, TextAlignmentOptions.TopRight);
        essLabel.text = "심연의 정수";
        SetRect(essLabel.rectTransform, TopRight, TopRight, TopRight, new Vector2(-Pad, -26), new Vector2(300, 18));

        var essenceTMP = MakeTMP(main.transform, "Txt_Essence", layer, 44, CEssence, TextAlignmentOptions.TopRight);
        essenceTMP.text = "0";
        SetRect(essenceTMP.rectTransform, TopRight, TopRight, TopRight, new Vector2(-Pad, -46), new Vector2(300, 52));

        // ── 상위 탭(밑줄형)
        var (achTabBtn, achTabLbl, achUnder, badge) = MakeTopTab(main.transform, "Tab_Achievements", "업적", Pad, layer, true);
        var (unlTabBtn, unlTabLbl, unlUnder, _)     = MakeTopTab(main.transform, "Tab_Unlocks", "해금", Pad + 120f, layer, false);

        var closeHint = MakeTMP(main.transform, "Txt_CloseHint", layer, 15, CFaint, TextAlignmentOptions.TopRight);
        closeHint.text = "ESC 닫기";
        SetRect(closeHint.rectTransform, TopRight, TopRight, TopRight, new Vector2(-Pad, -114), new Vector2(200, 22));

        var rule = MakeImage(main.transform, "Rule", layer, CLine);
        SetRect(rule.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
            new Vector2(0, -(HeaderH - 25f)), new Vector2(-Pad * 2, 1));

        var closeGO = NewContainer(main.transform, "Btn_Close", layer);
        closeGO.AddComponent<Image>().color = new Color(1, 1, 1, 0);
        var closeBtn = closeGO.AddComponent<Button>();
        SetRect(closeGO.GetComponent<RectTransform>(), TopRight, TopRight, TopRight,
            new Vector2(-Pad, -110), new Vector2(120, 32));

        // ── 해금 루트: 4열을 한 화면에. 탭으로 나누면 18노드 중 일부만 보여 목표를 정할 수 없다.
        var unlockRoot = NewContainer(main.transform, "Root_Unlocks", layer);
        Stretch(unlockRoot.GetComponent<RectTransform>());

        var cols = NewContainer(unlockRoot.transform, "Columns", layer);
        SetRect(cols.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
            new Vector2(0, -HeaderH), new Vector2(-Pad * 2, ColsH));
        var colLayout = cols.AddComponent<HorizontalLayoutGroup>();
        colLayout.childControlWidth      = true;
        colLayout.childControlHeight     = true;
        colLayout.childForceExpandWidth  = true;
        colLayout.childForceExpandHeight = true;
        colLayout.spacing                = 22;

        var colTf  = new Transform[Branches.Length];
        var colTtl = new TextMeshProUGUI[Branches.Length];
        var colQ   = new TextMeshProUGUI[Branches.Length];
        var colN   = new TextMeshProUGUI[Branches.Length];
        for (int i = 0; i < Branches.Length; i++)
            (colTf[i], colTtl[i], colQ[i], colN[i]) = MakeColumn(cols.transform, Branches[i], Questions[i], layer);

        var rowTemplate = MakeNodeRow(colTf[0], layer);
        rowTemplate.gameObject.SetActive(false);

        // 업적 모드 루트 — 해금과 <b>같은 자리·같은 크기</b>를 쓴다. 두 탭이 한 골격으로 읽혀야 한다.
        var achRoot = NewContainer(main.transform, "Root_Achievements", layer);
        SetRect(achRoot.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
            new Vector2(0, -HeaderH), new Vector2(-Pad * 2, ColsH));
        var achList = CreateAchievementList(achRoot.transform, layer);

        // ── 하단 행동 바 — 화면의 <b>유일한</b> 행동 지점.
        // 고른 것이 없으면 이 자리가 「다음 목표」를 말하므로 헤더에 목표 줄을 따로 붙이지 않는다.
        var act = MakeImage(main.transform, "ActionBar", layer, new Color(0.082f, 0.071f, 0.129f, 1f));
        SetRect(act.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0),
            new Vector2(0, 26), new Vector2(-Pad * 2, ActionH));

        var aName = MakeTMP(act.transform, "Txt_ActionName", layer, 25, CInk, TextAlignmentOptions.TopLeft);
        SetRect(aName.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(26, -20), new Vector2(920, 32));
        var aDesc = MakeTMP(act.transform, "Txt_ActionDesc", layer, 15, CDim, TextAlignmentOptions.TopLeft);
        SetRect(aDesc.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(26, -54), new Vector2(920, 24));
        var aCond = MakeTMP(act.transform, "Txt_ActionCondition", layer, 14, CFaint, TextAlignmentOptions.TopLeft);
        SetRect(aCond.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(26, -82), new Vector2(920, 22));

        var aBtnGO = NewContainer(act.transform, "Btn_Action", layer);
        var aBtnImg = aBtnGO.AddComponent<Image>();
        aBtnImg.color = CGold;
        var aBtn = aBtnGO.AddComponent<Button>();
        SetRect(aBtnGO.GetComponent<RectTransform>(), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f),
            new Vector2(-26, 0), new Vector2(270, 70));
        var aBtnLbl = MakeTMP(aBtnGO.transform, "Txt_ActionBtn", layer, 23,
            new Color(0.06f, 0.04f, 0.02f), TextAlignmentOptions.Center);
        aBtnLbl.fontStyle = FontStyles.Bold;
        aBtnLbl.text = "해금";
        SetRect(aBtnLbl.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f,
            new Vector2(0, 8), new Vector2(250, 30));
        var aBtnSub = MakeTMP(aBtnGO.transform, "Txt_ActionSub", layer, 13,
            new Color(0.06f, 0.04f, 0.02f, 0.75f), TextAlignmentOptions.Center);
        SetRect(aBtnSub.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f,
            new Vector2(0, -14), new Vector2(250, 20));

        // ── 배선
        var so = new SerializedObject(panelComp);
        so.FindProperty("essenceText").objectReferenceValue             = essenceTMP;
        so.FindProperty("achievementTab").objectReferenceValue          = achTabBtn;
        so.FindProperty("achievementTabLabel").objectReferenceValue     = achTabLbl;
        so.FindProperty("achievementTabUnderline").objectReferenceValue = achUnder;
        so.FindProperty("achievementBadge").objectReferenceValue        = badge;
        so.FindProperty("unlockTab").objectReferenceValue               = unlTabBtn;
        so.FindProperty("unlockTabLabel").objectReferenceValue          = unlTabLbl;
        so.FindProperty("unlockTabUnderline").objectReferenceValue      = unlUnder;
        so.FindProperty("unlockRoot").objectReferenceValue              = unlockRoot;
        so.FindProperty("achievementRoot").objectReferenceValue         = achRoot;
        so.FindProperty("achievementList").objectReferenceValue         = achList;
        so.FindProperty("rowTemplate").objectReferenceValue             = rowTemplate;
        so.FindProperty("actionName").objectReferenceValue              = aName;
        so.FindProperty("actionDesc").objectReferenceValue              = aDesc;
        so.FindProperty("actionCondition").objectReferenceValue         = aCond;
        so.FindProperty("actionButton").objectReferenceValue            = aBtn;
        so.FindProperty("actionButtonImage").objectReferenceValue       = aBtnImg;
        so.FindProperty("actionButtonLabel").objectReferenceValue       = aBtnLbl;
        so.FindProperty("actionButtonSub").objectReferenceValue         = aBtnSub;
        so.FindProperty("closeButton").objectReferenceValue             = closeBtn;
        SetArray(so, "branchColumns",   colTf);
        SetArray(so, "branchTitles",    colTtl);
        SetArray(so, "branchQuestions", colQ);
        SetArray(so, "branchCounts",    colN);
        so.ApplyModifiedPropertiesWithoutUndo();

        // 프리팹 저장
        PrefabUtility.SaveAsPrefabAssetAndConnect(root, PopupPath, InteractionMode.AutomatedAction);
        Object.DestroyImmediate(root);
        AssetDatabase.Refresh();
        Debug.Log($"[AwakeningPopupCreator] 프리팹 저장: {PopupPath}");
    }

    /// <summary>상위 탭 1개 — 박스가 아니라 <b>밑줄</b>이다. 박스 두 개가 나란히 서면
    /// 탭인지 버튼인지 구분이 안 되고, 아래 4열과도 시각이 겹친다.</summary>
    private static (Button, TextMeshProUGUI, Image, Image)
        MakeTopTab(Transform parent, string name, string label, float x, int layer, bool withBadge)
    {
        var TopLeft = new Vector2(0f, 1f);

        var go = NewContainer(parent, name, layer);
        go.AddComponent<Image>().color = new Color(1, 1, 1, 0);   // 클릭 판정만
        var btn = go.AddComponent<Button>();
        SetRect(go.GetComponent<RectTransform>(), TopLeft, TopLeft, TopLeft,
            new Vector2(x, -108), new Vector2(104, 40));

        var tmp = MakeTMP(go.transform, "Txt_Tab", layer, 20, CDim, TextAlignmentOptions.MidlineLeft);
        tmp.fontStyle = FontStyles.Bold;
        tmp.text = label;
        Stretch(tmp.rectTransform);

        var under = MakeImage(go.transform, "Underline", layer, CGold);
        SetRect(under.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0),
            Vector2.zero, new Vector2(52, 2));

        Image badge = null;
        if (withBadge)
        {
            badge = MakeImage(go.transform, "Badge", layer, CGold);
            SetRect(badge.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(58, -6), new Vector2(8, 8));
        }
        return (btn, tmp, under, badge);
    }

    /// <summary>갈래 열 하나. 머리에 <b>분류명이 아니라 플레이어의 질문</b>을 적는다 —
    /// 「Ⅱ 등장」만으론 그 열이 왜 중요한지 알 수 없다.</summary>
    private static (Transform, TextMeshProUGUI, TextMeshProUGUI, TextMeshProUGUI)
        MakeColumn(Transform parent, AltarBranch branch, string question, int layer)
    {
        var TopLeft  = new Vector2(0f, 1f);
        var TopRight = new Vector2(1f, 1f);

        var col = NewContainer(parent, $"Col_{branch}", layer);
        var v = col.AddComponent<VerticalLayoutGroup>();
        v.childControlWidth = true;  v.childControlHeight = false;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        v.spacing = 6;

        var head = NewContainer(col.transform, "Head", layer);
        AddLE(head, 66);
        var t = MakeTMP(head.transform, "Txt_Branch", layer, 21, CInk, TextAlignmentOptions.TopLeft);
        t.text = MemoryAltarCatalog.BranchLabel(branch);
        SetRect(t.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(4, -2), new Vector2(240, 26));
        var q = MakeTMP(head.transform, "Txt_Question", layer, 13, CFaint, TextAlignmentOptions.TopLeft);
        q.text = question;
        SetRect(q.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(4, -28), new Vector2(280, 20));
        var n = MakeTMP(head.transform, "Txt_Count", layer, 12, CFaint, TextAlignmentOptions.TopRight);
        SetRect(n.rectTransform, TopRight, TopRight, TopRight, new Vector2(-4, -2), new Vector2(40, 20));
        var line = MakeImage(head.transform, "Underline", layer, CLine);
        SetRect(line.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0),
            new Vector2(0, 6), new Vector2(0, 1));

        return (col.transform, t, q, n);
    }

    /// <summary>노드 행 — <b>버튼이 없다.</b> 고르는 것이고, 사는 것은 하단 바에서만 한다.</summary>
    private static AltarNodeRowView MakeNodeRow(Transform parent, int layer)
    {
        var TopLeft  = new Vector2(0f, 1f);
        var TopRight = new Vector2(1f, 1f);

        var row = NewContainer(parent, "Row_NodeTemplate", layer);
        var bg = row.AddComponent<Image>();
        bg.color = new Color(0.082f, 0.075f, 0.122f, 1f);
        var btn = row.AddComponent<Button>();
        var view = row.AddComponent<AltarNodeRowView>();
        var le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 66; le.minHeight = 66;

        var rail = MakeImage(row.transform, "AccentBar", layer, new Color(0.165f, 0.153f, 0.224f));
        SetRect(rail.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f),
            Vector2.zero, new Vector2(3, 0));

        var outline = MakeImage(row.transform, "SelectOutline", layer, new Color(0.890f, 0.659f, 0.298f, 0.20f));
        Stretch(outline.rectTransform);
        outline.raycastTarget = false;
        outline.gameObject.SetActive(false);

        var nm = MakeTMP(row.transform, "Txt_Name", layer, 17, CInk, TextAlignmentOptions.TopLeft);
        nm.fontStyle = FontStyles.Bold;
        SetRect(nm.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(16, -11), new Vector2(190, 24));

        var cost = MakeTMP(row.transform, "Txt_Cost", layer, 17, CDim, TextAlignmentOptions.TopRight);
        SetRect(cost.rectTransform, TopRight, TopRight, TopRight, new Vector2(-14, -11), new Vector2(120, 24));

        var cond = MakeTMP(row.transform, "Txt_Condition", layer, 12.5f, CFaint, TextAlignmentOptions.TopLeft);
        SetRect(cond.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(16, -37), new Vector2(300, 18));

        // 접근도 막대 — 비싼 것으로 조금씩 다가가는 게 보여야 "문턱을 못 넘으면 빈손"이 안 된다.
        var reach = NewContainer(row.transform, "ReachRow", layer);
        reach.AddComponent<Image>().color = new Color(0.133f, 0.122f, 0.188f, 1f);
        SetRect(reach.GetComponent<RectTransform>(), new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0),
            new Vector2(0, 9), new Vector2(-32, 3));
        var reachFill = MakeImage(reach.transform, "Fill", layer, new Color(0.235f, 0.216f, 0.322f));
        Stretch(reachFill.rectTransform);
        reachFill.type = Image.Type.Filled;
        reachFill.fillMethod = Image.FillMethod.Horizontal;
        reachFill.fillAmount = 0f;

        var vso = new SerializedObject(view);
        vso.FindProperty("background").objectReferenceValue       = bg;
        vso.FindProperty("accentBar").objectReferenceValue        = rail;
        vso.FindProperty("selectionOutline").objectReferenceValue = outline;
        vso.FindProperty("nameText").objectReferenceValue         = nm;
        vso.FindProperty("costText").objectReferenceValue         = cost;
        vso.FindProperty("conditionText").objectReferenceValue    = cond;
        vso.FindProperty("reachRow").objectReferenceValue         = reach.GetComponent<RectTransform>();
        vso.FindProperty("reachFill").objectReferenceValue        = reachFill;
        vso.FindProperty("selectButton").objectReferenceValue     = btn;
        vso.FindProperty("layoutElement").objectReferenceValue    = le;
        vso.ApplyModifiedPropertiesWithoutUndo();

        return view;
    }

    private static void SetArray(SerializedObject so, string field, Object[] values)
    {
        var prop = so.FindProperty(field);
        prop.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    /// <summary>
    /// 업적 목록. 행 레이아웃은 <c>기호48 | 이름280 | 조건·진척380 | 보상120 | 액션160</c>, 높이 72.
    /// 「액션」 열이 상태에 따라 내용만 바뀌므로 <b>우측 한 열만</b> 훑으면 된다.
    /// </summary>
    private static AchievementListView CreateAchievementList(Transform parent, int layer)
    {
        var root = NewContainer(parent, "AchievementList", layer);
        Stretch(root.GetComponent<RectTransform>());
        var view = root.AddComponent<AchievementListView>();

        // 스크롤 — 업적이 20개가 되면 6행 한계로는 절반 이상이 잘린다.
        var scrollGO = NewContainer(root.transform, "Scroll", layer);
        Stretch(scrollGO.GetComponent<RectTransform>());
        var scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 32f;

        var viewport = NewContainer(scrollGO.transform, "Viewport", layer);
        Stretch(viewport.GetComponent<RectTransform>());
        viewport.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.004f);   // 마스크는 그래픽이 있어야 동작
        viewport.AddComponent<Mask>().showMaskGraphic = false;
        scroll.viewport = viewport.GetComponent<RectTransform>();

        // 내용은 높이가 <b>행 수에 따라 자란다</b> — 고정 높이면 스크롤이 생기지 않는다.
        var content = NewContainer(viewport.transform, "Content", layer);
        SetRect(content.GetComponent<RectTransform>(),
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 0));
        var col = content.AddComponent<VerticalLayoutGroup>();
        col.childControlWidth      = true;
        col.childControlHeight     = false;
        col.childForceExpandWidth  = true;
        col.childForceExpandHeight = false;
        col.spacing = 4;
        col.padding = new RectOffset(24, 24, 8, 16);
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content.GetComponent<RectTransform>();

        // ── 구간 1: 수령 가능 ── 머리 + [모두 받기] + 전용 행 컨테이너
        var claimHead = NewContainer(content.transform, "Head_Claimable", layer);
        AddLE(claimHead, 46);
        var claimHeadTMP = MakeTMP(claimHead.transform, "Txt_Claimable", layer, 18,
            new Color(0.890f, 0.659f, 0.298f), TextAlignmentOptions.MidlineLeft);
        SetRect(claimHeadTMP.rectTransform,
            new Vector2(0, 0), new Vector2(0.5f, 1), new Vector2(0, 0.5f), new Vector2(4, 0), Vector2.zero);

        var allGO = NewContainer(claimHead.transform, "Btn_ClaimAll", layer);
        allGO.AddComponent<Image>().color = new Color(0.890f, 0.659f, 0.298f);
        var allBtn = allGO.AddComponent<Button>();
        SetRect(allGO.GetComponent<RectTransform>(),
            new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-4, 0), new Vector2(190, 40));
        var allTMP = MakeTMP(allGO.transform, "Txt_ClaimAll", layer, 18,
            new Color(0.059f, 0.039f, 0.020f), TextAlignmentOptions.Center);
        allTMP.fontStyle = FontStyles.Bold;
        allTMP.text = "모두 받기";
        Stretch(allTMP.rectTransform);

        var rowsClaimable = MakeRowSection(content.transform, "Rows_Claimable", layer);

        // ── 구간 2: 진행 중 ──
        var progHeadGO = NewContainer(content.transform, "Head_Progress", layer);
        AddLE(progHeadGO, 34);
        var progHead = MakeTMP(progHeadGO.transform, "Txt_Progress", layer, 16,
            new Color(0.482f, 0.459f, 0.573f), TextAlignmentOptions.MidlineLeft);
        progHead.text = "진행 중";
        Stretch(progHead.rectTransform);
        var rowsProgress = MakeRowSection(content.transform, "Rows_Progress", layer);

        // ── 구간 3: 받음 (접기) ── 성취는 남기되 할 일을 가리지 않는다.
        var claimedGO = NewContainer(content.transform, "Head_Claimed", layer);
        claimedGO.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0f);   // 클릭 판정만
        var claimedBtn = claimedGO.AddComponent<Button>();
        AddLE(claimedGO, 34);
        var claimedTMP = MakeTMP(claimedGO.transform, "Txt_Claimed", layer, 16,
            new Color(0.325f, 0.310f, 0.396f), TextAlignmentOptions.MidlineLeft);
        claimedTMP.text = "받음  0  ▼";
        Stretch(claimedTMP.rectTransform);
        var rowsClaimed = MakeRowSection(content.transform, "Rows_Claimed", layer);

        var rowTemplate = CreateAchievementRow(rowsProgress, layer);
        rowTemplate.gameObject.SetActive(false);

        var emptyTMP = MakeTMP(content.transform, "Txt_Empty", layer, 17,
            new Color(0.325f, 0.310f, 0.396f), TextAlignmentOptions.Center);
        emptyTMP.text = "아직 업적이 없다.";
        AddLE(emptyTMP.gameObject, 60);
        emptyTMP.gameObject.SetActive(false);

        var vso = new SerializedObject(view);
        vso.FindProperty("claimableHeaderRow").objectReferenceValue  = claimHead;
        vso.FindProperty("progressHeaderRow").objectReferenceValue   = progHeadGO;
        vso.FindProperty("claimedHeaderRow").objectReferenceValue    = claimedGO;
        vso.FindProperty("claimableHeader").objectReferenceValue     = claimHeadTMP;
        vso.FindProperty("progressHeader").objectReferenceValue      = progHead;
        vso.FindProperty("claimedHeaderButton").objectReferenceValue = claimedBtn;
        vso.FindProperty("claimedHeader").objectReferenceValue       = claimedTMP;
        vso.FindProperty("claimAllButton").objectReferenceValue      = allBtn;
        vso.FindProperty("claimAllLabel").objectReferenceValue       = allTMP;
        vso.FindProperty("claimableRows").objectReferenceValue       = rowsClaimable;
        vso.FindProperty("progressRows").objectReferenceValue        = rowsProgress;
        vso.FindProperty("claimedRows").objectReferenceValue         = rowsClaimed;
        vso.FindProperty("rowTemplate").objectReferenceValue         = rowTemplate;
        vso.FindProperty("emptyText").objectReferenceValue           = emptyTMP;
        vso.ApplyModifiedPropertiesWithoutUndo();

        return view;
    }

    /// <summary>구간 하나의 행이 쌓이는 자리. 높이는 내용에 따라 자란다.</summary>
    private static Transform MakeRowSection(Transform parent, string name, int layer)
    {
        var go = NewContainer(parent, name, layer);
        var col = go.AddComponent<VerticalLayoutGroup>();
        col.childControlWidth      = true;
        col.childControlHeight     = false;
        col.childForceExpandWidth  = true;
        col.childForceExpandHeight = false;
        col.spacing = 4;
        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return go.transform;
    }

    /// <summary>업적 행 템플릿. 열 폭은 기획 §5-1a 확정치를 그대로 쓴다.</summary>
    private static AchievementRowView CreateAchievementRow(Transform parent, int layer)
    {
        const float RowH = 72f;

        var row = NewContainer(parent, "Row_Template", layer);
        var bg = row.AddComponent<Image>();
        bg.color = new Color(0.106f, 0.098f, 0.161f, 1f);
        var view = row.AddComponent<AchievementRowView>();
        AddLE(row, RowH);

        // 기호 48
        var sym = MakeTMP(row.transform, "Txt_Symbol", layer, 20,
            new Color(0.482f, 0.459f, 0.573f), TextAlignmentOptions.Center);
        SetRect(sym.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f),
            new Vector2(0, 0), new Vector2(48, 0));

        // 이름 280
        var name = MakeTMP(row.transform, "Txt_Name", layer, 19,
            new Color(0.902f, 0.882f, 0.957f), TextAlignmentOptions.MidlineLeft);
        name.fontStyle = FontStyles.Bold;
        SetRect(name.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f),
            new Vector2(48, 0), new Vector2(280, 0));

        // 조건·진척 — 좌 328부터 우측 보상/액션 열(280+40) 앞까지 <b>늘어난다</b>.
        // 고정 380으로 두면 창이 넓어질 때 우측에 죽은 공간이 생기고,
        // 「액션」 열이 우측 끝이 아닌 중간에 떠서 "우측 한 열만 훑는다"가 성립하지 않는다.
        var cond = MakeTMP(row.transform, "Txt_Condition", layer, 15,
            new Color(0.482f, 0.459f, 0.573f), TextAlignmentOptions.BottomLeft);
        SetRect(cond.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 1), new Vector2(0, 0.5f),
            Vector2.zero, Vector2.zero);
        cond.rectTransform.offsetMin = new Vector2(328, 2);
        cond.rectTransform.offsetMax = new Vector2(-320, -8);

        var barBG = MakeImage(row.transform, "Bar_BG", layer, new Color(0.153f, 0.145f, 0.216f, 1f));
        SetRect(barBG.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 0.5f),
            Vector2.zero, Vector2.zero);
        barBG.rectTransform.offsetMin = new Vector2(328, -19);
        barBG.rectTransform.offsetMax = new Vector2(-320, -14);
        var barFill = MakeImage(barBG.transform, "Bar_Fill", layer, new Color(0.388f, 0.851f, 0.749f));
        Stretch(barFill.rectTransform);
        barFill.type       = Image.Type.Filled;
        barFill.fillMethod = Image.FillMethod.Horizontal;
        barFill.fillAmount = 0f;

        // 보상 120
        var reward = MakeTMP(row.transform, "Txt_Reward", layer, 18,
            new Color(0.388f, 0.851f, 0.749f), TextAlignmentOptions.MidlineRight);
        SetRect(reward.rectTransform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f),
            new Vector2(-176, 0), new Vector2(120, 0));

        // 액션 160 — 버튼과 상태 표시가 같은 자리를 나눠 쓴다.
        var btnGO = NewContainer(row.transform, "Btn_Claim", layer);
        var btnImg = btnGO.AddComponent<Image>();
        btnImg.color = new Color(0.890f, 0.659f, 0.298f);
        var btn = btnGO.AddComponent<Button>();
        SetRect(btnGO.GetComponent<RectTransform>(), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f),
            new Vector2(-16, 0), new Vector2(140, 40));
        var btnTMP = MakeTMP(btnGO.transform, "Txt_Claim", layer, 17,
            new Color(0.059f, 0.039f, 0.020f), TextAlignmentOptions.Center);
        btnTMP.fontStyle = FontStyles.Bold;
        btnTMP.text = "받기";
        Stretch(btnTMP.rectTransform);

        var status = MakeTMP(row.transform, "Txt_Status", layer, 16,
            new Color(0.482f, 0.459f, 0.573f), TextAlignmentOptions.Center);
        SetRect(status.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f),
            new Vector2(-16, 0), new Vector2(140, 40));

        var rso = new SerializedObject(view);
        rso.FindProperty("background").objectReferenceValue       = bg;
        rso.FindProperty("symbolText").objectReferenceValue       = sym;
        rso.FindProperty("nameText").objectReferenceValue         = name;
        rso.FindProperty("conditionText").objectReferenceValue    = cond;
        rso.FindProperty("progressFill").objectReferenceValue     = barFill;
        rso.FindProperty("rewardText").objectReferenceValue       = reward;
        rso.FindProperty("claimButton").objectReferenceValue      = btn;
        rso.FindProperty("claimButtonImage").objectReferenceValue = btnImg;
        rso.FindProperty("claimLabel").objectReferenceValue       = btnTMP;
        rso.FindProperty("statusText").objectReferenceValue       = status;
        rso.ApplyModifiedPropertiesWithoutUndo();

        return view;
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
