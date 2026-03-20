// WeaponReplacePopupBuilder.cs  — Tools → "Build WeaponReplacePopup Prefab"
#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

public static class WeaponReplacePopupBuilder
{
    private const string PrefabPath = "Assets/Abyss/UI/Popup/UI_WeaponReplacePopup.prefab";
    private const string FontPath   = "Assets/Abyss/Fonts/NotoSansKR-VariableFont_wght SDF.asset";

    // ── 색상 ──────────────────────────────────────────────────────────
    private static readonly Color BgPanel      = new Color(0.10f, 0.11f, 0.15f, 1.00f);
    private static readonly Color BgBlocker    = new Color(0.00f, 0.00f, 0.00f, 0.65f);
    private static readonly Color BgSide       = new Color(1.00f, 1.00f, 1.00f, 0.04f);
    private static readonly Color BgStats      = new Color(1.00f, 1.00f, 1.00f, 0.03f);
    private static readonly Color ColorGold    = new Color(1.00f, 0.85f, 0.40f, 1.00f);
    private static readonly Color ColorLabel   = new Color(0.65f, 0.65f, 0.65f, 1.00f);
    private static readonly Color ColorDivider = new Color(1.00f, 1.00f, 1.00f, 0.10f);
    private static readonly Color BtnReplace   = new Color(0.18f, 0.55f, 0.28f, 1.00f);
    private static readonly Color BtnDiscard   = new Color(0.60f, 0.18f, 0.18f, 1.00f);

    private const float PanelW   = 920f;
    private const float PanelH   = 580f;
    private const float HeaderH  = 200f; // 헤더(아이콘) 영역 높이
    private const float BtnAreaH = 72f;

    [MenuItem("Tools/Build WeaponReplacePopup Prefab")]
    public static void Build()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null) Debug.LogWarning($"[Builder] 폰트 없음: {FontPath}");

        using var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath);
        var root = scope.prefabContentsRoot;

        while (root.transform.childCount > 0)
            GameObject.DestroyImmediate(root.transform.GetChild(0).gameObject);

        // ── 루트 블로커 ───────────────────────────────────────────────
        Stretch(root.GetComponent<RectTransform>());
        EnsureImg(root).color = BgBlocker;

        // ── Panel ─────────────────────────────────────────────────────
        var panel = O("Panel", root.transform);
        Center(panel, PanelW, PanelH, 0, 0);
        panel.AddComponent<Image>().color = BgPanel;

        // ── Title / SlotLabel ─────────────────────────────────────────
        var titleGO = O("Title", panel.transform);
        AnchorTop(titleGO, PanelW - 40f, 44f, 0, -26f);
        T(titleGO, "새 무기 획득!", 25f, FontStyles.Bold,
            TextAlignmentOptions.Center, Color.white, font);

        var slotLabelGO = O("SlotLabel", panel.transform);
        AnchorTop(slotLabelGO, PanelW - 40f, 26f, 0, -64f);
        var slotLabelTmp = T(slotLabelGO, "─── 메인 무기 ───", 14f,
            FontStyles.Normal, TextAlignmentOptions.Center, ColorGold, font);

        // ── HeaderArea (좌/우 아이콘 영역) ────────────────────────────
        float statsTop = -(88f + HeaderH);          // Title+SlotLabel 여백
        var headerGO = O("HeaderArea", panel.transform);
        AnchorTop(headerGO, PanelW, HeaderH, 0, -88f);

        var leftGO = O("LeftPanel", headerGO.transform);
        Split(leftGO, 0f, 0f, 0.5f, 1f);
        leftGO.AddComponent<Image>().color = BgSide;

        var divV = O("DividerV", headerGO.transform);
        Split(divV, 0.5f, 0f, 0.5f, 1f);
        divV.GetComponent<RectTransform>().sizeDelta = new Vector2(1f, 0f);
        divV.AddComponent<Image>().color = ColorDivider;

        var rightGO = O("RightPanel", headerGO.transform);
        Split(rightGO, 0.5f, 0f, 1f, 1f);
        rightGO.AddComponent<Image>().color = BgSide;

        var (curIcon, curName) = BuildHeader(leftGO.transform,  "현재 장비", ColorLabel, font);
        var (newIcon, newName) = BuildHeader(rightGO.transform, "새 장비",   Color.white, font);

        // ── 구분선 ────────────────────────────────────────────────────
        var divH = O("DividerH", panel.transform);
        AnchorTop(divH, PanelW, 1f, 0, statsTop);
        divH.AddComponent<Image>().color = ColorDivider;

        // ── 메인 스탯 섹션 ────────────────────────────────────────────
        float statsAreaH = PanelH - 88f - HeaderH - 1f - BtnAreaH - 8f;
        var mainSec = O("MainStatsSection", panel.transform);
        AnchorTop(mainSec, PanelW, statsAreaH, 0, statsTop - 1f);
        mainSec.AddComponent<Image>().color = BgStats;

        var mainVlg = mainSec.AddComponent<VerticalLayoutGroup>();
        mainVlg.padding = new RectOffset(24, 24, 12, 12);
        mainVlg.spacing = 8f;
        mainVlg.childAlignment       = TextAnchor.UpperCenter;
        mainVlg.childControlWidth    = true;
        mainVlg.childControlHeight   = false;
        mainVlg.childForceExpandWidth  = true;
        mainVlg.childForceExpandHeight = false;

        var (atkCur, atkDelta, atkNew) = StatRow(mainSec.transform, "공격력", font);
        var (defCur, defDelta, defNew) = StatRow(mainSec.transform, "방어력", font);

        // ── 서브 스탯 섹션 ────────────────────────────────────────────
        var subSec = O("SubStatsSection", panel.transform);
        AnchorTop(subSec, PanelW, statsAreaH, 0, statsTop - 1f);
        subSec.AddComponent<Image>().color = BgStats;
        subSec.SetActive(false);

        var subVlg = subSec.AddComponent<VerticalLayoutGroup>();
        subVlg.padding = new RectOffset(24, 24, 12, 12);
        subVlg.spacing = 8f;
        subVlg.childAlignment       = TextAnchor.UpperCenter;
        subVlg.childControlWidth    = true;
        subVlg.childControlHeight   = false;
        subVlg.childForceExpandWidth  = true;
        subVlg.childForceExpandHeight = false;

        var (qSkillCur, _, qSkillNew)    = LabelRow(subSec.transform, "Q 스킬", font);
        var (qCoolCur, qCoolDelta, qCoolNew) = StatRow(subSec.transform, "Q 쿨다운", font);
        var (qDescCur, qDescNew)         = DescRow(subSec.transform, font);

        // ── 버튼 영역 ─────────────────────────────────────────────────
        var btnArea = O("ButtonArea", panel.transform);
        AnchorBottom(btnArea, PanelW, BtnAreaH, 0, 0);

        var replaceBtn = Btn("ReplaceButton", btnArea.transform,
            "교체하기", BtnReplace, font, -150f, 0f, 260f, 50f);
        var discardBtn = Btn("DiscardButton", btnArea.transform,
            "버리기",   BtnDiscard, font,  150f, 0f, 260f, 50f);

        // ── 바인딩 ────────────────────────────────────────────────────
        var popup = root.GetComponent<UI_WeaponReplacePopup>()
                    ?? root.AddComponent<UI_WeaponReplacePopup>();
        var so = new SerializedObject(popup);

        B(so, "slotLabelText",     slotLabelTmp);
        B(so, "currentIcon",       curIcon);
        B(so, "currentName",       curName);
        B(so, "newIcon",           newIcon);
        B(so, "newName",           newName);
        B(so, "mainStatsSection",  mainSec);
        B(so, "atkCurrentText",    atkCur);
        B(so, "atkDeltaText",      atkDelta);
        B(so, "atkNewText",        atkNew);
        B(so, "defCurrentText",    defCur);
        B(so, "defDeltaText",      defDelta);
        B(so, "defNewText",        defNew);
        B(so, "subStatsSection",   subSec);
        B(so, "qSkillCurrentText", qSkillCur);
        B(so, "qSkillNewText",     qSkillNew);
        B(so, "qCoolCurrentText",  qCoolCur);
        B(so, "qCoolDeltaText",    qCoolDelta);
        B(so, "qCoolNewText",      qCoolNew);
        B(so, "qDescCurrentText",  qDescCur);
        B(so, "qDescNewText",      qDescNew);
        B(so, "replaceButton",     replaceBtn);
        B(so, "discardButton",     discardBtn);

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[WeaponReplacePopupBuilder] 재빌드 완료.");
    }

    // ──────────────────────────────────────────────────────────────────
    // 섹션/행 빌더
    // ──────────────────────────────────────────────────────────────────

    /// <summary>아이콘 + 이름 헤더 (단일 패널 내)</summary>
    private static (Image icon, TextMeshProUGUI name)
        BuildHeader(Transform parent, string label, Color labelColor, TMP_FontAsset font)
    {
        var vlg = parent.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 12, 8);
        vlg.spacing = 6f;
        vlg.childAlignment       = TextAnchor.UpperCenter;
        vlg.childControlWidth    = true;
        vlg.childControlHeight   = false;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        // 레이블 행
        var lblGO = Flex(parent, "_Lbl", 24f);
        T(lblGO, label, 12f, FontStyles.Normal,
            TextAlignmentOptions.Center, labelColor, font);

        // 아이콘 행 (HLG로 고정 크기 유지)
        var iconRowGO = Flex(parent, "IconRow", 86f);
        var hlg = iconRowGO.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = hlg.childControlHeight = false;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;

        var iconGO = O("Icon", iconRowGO.transform);
        iconGO.GetComponent<RectTransform>().sizeDelta = new Vector2(80f, 80f);
        var iconImg = iconGO.AddComponent<Image>();
        iconImg.color = new Color(1f, 1f, 1f, 0.12f);

        // 이름 행
        var nameGO = Flex(parent, "Name", 32f);
        var nameTmp = T(nameGO, "—", 14f, FontStyles.Bold,
            TextAlignmentOptions.Center, Color.white, font);

        return (iconImg, nameTmp);
    }

    /// <summary>스탯 행: [라벨] [현재값] [▲▼ 델타] [새값]</summary>
    private static (TextMeshProUGUI cur, TextMeshProUGUI delta, TextMeshProUGUI next)
        StatRow(Transform parent, string label, TMP_FontAsset font)
    {
        var row = Flex(parent, "_SR_" + label, 30f);
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 0f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false; hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        var lbl   = S(row.transform, "_L",  90f, 30f); T(lbl,  label, 13f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft,   ColorLabel,  font);
        var curGO = S(row.transform, "_C", 100f, 30f); var curTmp   = T(curGO,  "—", 15f, FontStyles.Bold,   TextAlignmentOptions.MidlineRight,  Color.white, font);
        var dltGO = S(row.transform, "_D", 120f, 30f); var deltaTmp = T(dltGO,  "—", 14f, FontStyles.Normal, TextAlignmentOptions.Midline, ColorLabel,  font);
        var newGO = S(row.transform, "_N", 100f, 30f); var newTmp   = T(newGO,  "—", 15f, FontStyles.Bold,   TextAlignmentOptions.MidlineLeft,   Color.white, font);

        return (curTmp, deltaTmp, newTmp);
    }

    /// <summary>레이블 행 (Q 스킬명 등 델타 없음): [라벨] [현재] [→] [새]</summary>
    private static (TextMeshProUGUI cur, TextMeshProUGUI arrow, TextMeshProUGUI next)
        LabelRow(Transform parent, string label, TMP_FontAsset font)
    {
        var row = Flex(parent, "_LR_" + label, 30f);
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 0f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false; hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        var lbl  = S(row.transform, "_L",  90f, 30f); T(lbl,  label, 13f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft,   ColorLabel,  font);
        var curGO = S(row.transform, "_C", 130f, 30f); var curTmp  = T(curGO,  "—", 13f, FontStyles.Bold,   TextAlignmentOptions.MidlineRight,  Color.white, font);
        var arrGO = S(row.transform, "_A",  40f, 30f); var arrTmp  = T(arrGO,  "→", 13f, FontStyles.Normal, TextAlignmentOptions.Midline, ColorLabel,  font);
        var newGO = S(row.transform, "_N", 130f, 30f); var newTmp  = T(newGO,  "—", 13f, FontStyles.Bold,   TextAlignmentOptions.MidlineLeft,   Color.white, font);

        return (curTmp, arrTmp, newTmp);
    }

    /// <summary>설명 행 (좌/우 텍스트): [현재설명] | [새설명]</summary>
    private static (TextMeshProUGUI cur, TextMeshProUGUI next)
        DescRow(Transform parent, TMP_FontAsset font)
    {
        var row = Flex(parent, "_DescRow", 52f);
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8f;
        hlg.childAlignment = TextAnchor.UpperCenter;
        hlg.childControlWidth = false; hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        var curGO = S(row.transform, "_DC", 390f, 52f);
        var curTmp = T(curGO, "—", 11f, FontStyles.Normal,
            TextAlignmentOptions.TopRight, ColorLabel, font);
        curTmp.enableWordWrapping = true;

        var divGO = S(row.transform, "_DD", 2f, 52f);
        divGO.AddComponent<Image>().color = ColorDivider;

        var newGO = S(row.transform, "_DN", 390f, 52f);
        var newTmp = T(newGO, "—", 11f, FontStyles.Normal,
            TextAlignmentOptions.TopLeft, ColorLabel, font);
        newTmp.enableWordWrapping = true;

        return (curTmp, newTmp);
    }

    // ──────────────────────────────────────────────────────────────────
    // 레이아웃 헬퍼
    // ──────────────────────────────────────────────────────────────────

    private static GameObject Flex(Transform parent, string name, float h)
    {
        var go = O(name, parent);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, h);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = h; le.preferredHeight = h;
        return go;
    }

    private static GameObject S(Transform parent, string name, float w, float h)
    {
        var go = O(name, parent);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(w, h);
        return go;
    }

    private static Button Btn(string name, Transform parent,
        string label, Color bg, TMP_FontAsset font,
        float x, float y, float w, float h)
    {
        var go = O(name, parent);
        Center(go, w, h, x, y);
        go.AddComponent<Image>().color = bg;
        var btn = go.AddComponent<Button>();
        var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
        var txtGO = O("BtnText", go.transform);
        Stretch(txtGO.GetComponent<RectTransform>());
        T(txtGO, label, 15f, FontStyles.Bold, TextAlignmentOptions.Center, Color.white, font);
        return btn;
    }

    // ──────────────────────────────────────────────────────────────────
    // RectTransform
    // ──────────────────────────────────────────────────────────────────

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
    }

    private static void Center(GameObject go, float w, float h, float x, float y)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = new Vector2(x, y);
    }

    private static void AnchorTop(GameObject go, float w, float h, float x, float y)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = new Vector2(x, y);
    }

    private static void AnchorBottom(GameObject go, float w, float h, float x, float y)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = new Vector2(x, y);
    }

    private static void Split(GameObject go,
        float minX, float minY, float maxX, float maxY)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(minX, minY); rt.anchorMax = new Vector2(maxX, maxY);
        rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
    }

    // ──────────────────────────────────────────────────────────────────
    // 기타
    // ──────────────────────────────────────────────────────────────────

    private static GameObject O(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static TextMeshProUGUI T(GameObject go, string text, float size,
        FontStyles style, TextAlignmentOptions align, Color color, TMP_FontAsset font)
    {
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size; tmp.fontStyle = style;
        tmp.alignment = align; tmp.color = color;
        if (font != null) tmp.font = font;
        return tmp;
    }

    private static Image EnsureImg(GameObject go)
    {
        var img = go.GetComponent<Image>();
        return img ? img : go.AddComponent<Image>();
    }

    private static void B(SerializedObject so, string field, Object value)
    {
        var prop = so.FindProperty(field);
        if (prop != null) prop.objectReferenceValue = value;
        else Debug.LogWarning($"[Builder] 필드 없음: {field}");
    }
}
#endif
