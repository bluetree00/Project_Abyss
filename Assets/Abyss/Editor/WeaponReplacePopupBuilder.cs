// WeaponReplacePopupBuilder.cs
// Tools → "Build WeaponReplacePopup Prefab" 실행 시 프리팹을 완전히 재빌드합니다.
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
    private static readonly Color ColorGold    = new Color(1.00f, 0.85f, 0.40f, 1.00f);
    private static readonly Color ColorLabel   = new Color(0.65f, 0.65f, 0.65f, 1.00f);
    private static readonly Color ColorDivider = new Color(1.00f, 1.00f, 1.00f, 0.12f);
    private static readonly Color BtnReplace   = new Color(0.18f, 0.55f, 0.28f, 1.00f);
    private static readonly Color BtnDiscard   = new Color(0.60f, 0.18f, 0.18f, 1.00f);

    private const float PanelW = 920f;
    private const float PanelH = 600f;
    private const float PadTop = 120f;
    private const float PadBot = 80f;

    [MenuItem("Tools/Build WeaponReplacePopup Prefab")]
    public static void Build()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
            Debug.LogWarning($"[Builder] 폰트 없음: {FontPath}");

        using var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath);
        var root = scope.prefabContentsRoot;

        while (root.transform.childCount > 0)
            GameObject.DestroyImmediate(root.transform.GetChild(0).gameObject);

        // ── 루트: 풀스크린 블로커 ─────────────────────────────────────
        Stretch(root.GetComponent<RectTransform>());
        EnsureImage(root).color = BgBlocker;

        // ── Panel ─────────────────────────────────────────────────────
        var panel = Obj("Panel", root.transform);
        SetCenter(panel, PanelW, PanelH, 0, 0);
        panel.AddComponent<Image>().color = BgPanel;

        // ── Title ─────────────────────────────────────────────────────
        var titleGO = Obj("Title", panel.transform);
        AnchorTop(titleGO, PanelW - 40f, 46f, 0, -28f);
        TMP(titleGO, "새 무기 획득!", 26f, FontStyles.Bold,
            TextAlignmentOptions.Center, Color.white, font);

        // ── SlotLabel ─────────────────────────────────────────────────
        var slotLabelGO = Obj("SlotLabel", panel.transform);
        AnchorTop(slotLabelGO, PanelW - 40f, 28f, 0, -68f);
        var slotLabelTmp = TMP(slotLabelGO, "─── 메인 무기 ───", 14f,
            FontStyles.Normal, TextAlignmentOptions.Center, ColorGold, font);

        // ── CompareArea ───────────────────────────────────────────────
        float compareH = PanelH - PadTop - PadBot;
        var compareGO = Obj("CompareArea", panel.transform);
        SetCenter(compareGO, PanelW, compareH, 0, (PadBot - PadTop) * 0.5f);

        // 왼쪽 패널
        var leftGO = Obj("LeftPanel", compareGO.transform);
        AnchorSplit(leftGO, 0f, 0f, 0.5f, 1f);
        leftGO.AddComponent<Image>().color = BgSide;

        // 세로 구분선
        var divV = Obj("DividerV", compareGO.transform);
        AnchorSplit(divV, 0.5f, 0f, 0.5f, 1f);
        divV.GetComponent<RectTransform>().sizeDelta = new Vector2(1f, 0f);
        divV.AddComponent<Image>().color = ColorDivider;

        // 오른쪽 패널
        var rightGO = Obj("RightPanel", compareGO.transform);
        AnchorSplit(rightGO, 0.5f, 0f, 1f, 1f);
        rightGO.AddComponent<Image>().color = BgSide;

        // ── 왼쪽 내용 ─────────────────────────────────────────────────
        var (curIcon, curName)       = BuildHeader(leftGO.transform, "현재 장비", ColorLabel, font);
        var (curMainSec, curAtk, curDef)
                                     = BuildMainSection(leftGO.transform, font, isRight: false,
                                           out _, out _);
        var (curSubSec, curQName, curQCool, _, curQDesc)
                                     = BuildSubSection(leftGO.transform, font, isRight: false,
                                           out _);

        // ── 오른쪽 내용 ───────────────────────────────────────────────
        var (newIcon, newName)       = BuildHeader(rightGO.transform, "새 장비", Color.white, font);
        var (newMainSec, newAtk, newDef)
                                     = BuildMainSection(rightGO.transform, font, isRight: true,
                                           out var atkDelta, out var defDelta);
        var (newSubSec, newQName, newQCool, qCoolDelta, newQDesc)
                                     = BuildSubSection(rightGO.transform, font, isRight: true,
                                           out _);

        // ── 버튼 영역 ─────────────────────────────────────────────────
        var btnArea = Obj("ButtonArea", panel.transform);
        AnchorBottom(btnArea, PanelW, PadBot, 0, 0);

        var replaceBtn = BuildBtn("ReplaceButton", btnArea.transform,
            "교체하기", BtnReplace, font, -145f, 0f, 260f, 50f);
        var discardBtn = BuildBtn("DiscardButton", btnArea.transform,
            "버리기", BtnDiscard, font, 145f, 0f, 260f, 50f);

        // ── 바인딩 ────────────────────────────────────────────────────
        var popup = root.GetComponent<UI_WeaponReplacePopup>()
                    ?? root.AddComponent<UI_WeaponReplacePopup>();
        var so = new SerializedObject(popup);

        B(so, "slotLabelText",         slotLabelTmp);
        B(so, "currentIcon",           curIcon);
        B(so, "currentName",           curName);
        B(so, "currentMainSection",    curMainSec);
        B(so, "currentAtkText",        curAtk);
        B(so, "currentDefText",        curDef);
        B(so, "currentSubSection",     curSubSec);
        B(so, "currentQSkillNameText", curQName);
        B(so, "currentQCoolText",      curQCool);
        B(so, "currentQDescText",      curQDesc);
        B(so, "newIcon",               newIcon);
        B(so, "newName",               newName);
        B(so, "newMainSection",        newMainSec);
        B(so, "newAtkText",            newAtk);
        B(so, "atkDeltaText",          atkDelta);
        B(so, "newDefText",            newDef);
        B(so, "defDeltaText",          defDelta);
        B(so, "newSubSection",         newSubSec);
        B(so, "newQSkillNameText",     newQName);
        B(so, "newQCoolText",          newQCool);
        B(so, "qCoolDeltaText",        qCoolDelta);
        B(so, "newQDescText",          newQDesc);
        B(so, "replaceButton",         replaceBtn);
        B(so, "discardButton",         discardBtn);

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[WeaponReplacePopupBuilder] 재빌드 완료.");
    }

    // ──────────────────────────────────────────────────────────────────
    // 섹션 빌더
    // ──────────────────────────────────────────────────────────────────

    private static (Image icon, TextMeshProUGUI name)
        BuildHeader(Transform parent, string label, Color labelColor, TMP_FontAsset font)
    {
        var vlg = VLG(parent, "Header", 6f, new RectOffset(12, 12, 14, 8));

        var lblGO = FlexChild(vlg.transform, "_Lbl", 26f);
        TMP(lblGO, label, 12f, FontStyles.Normal,
            TextAlignmentOptions.Center, labelColor, font);

        // 아이콘: HLG 행으로 감싸서 고정 80×80 유지
        var iconRowGO = FlexChild(vlg.transform, "IconRow", 88f);
        var iconRowHlg = iconRowGO.AddComponent<HorizontalLayoutGroup>();
        iconRowHlg.childAlignment       = TextAnchor.MiddleCenter;
        iconRowHlg.childControlWidth    = false;
        iconRowHlg.childControlHeight   = false;
        iconRowHlg.childForceExpandWidth  = false;
        iconRowHlg.childForceExpandHeight = false;

        var iconGO = Obj("Icon", iconRowGO.transform);
        iconGO.GetComponent<RectTransform>().sizeDelta = new Vector2(80f, 80f);
        var iconImg = iconGO.AddComponent<Image>();
        iconImg.color = new Color(1f, 1f, 1f, 0.12f);

        var nameGO = FlexChild(vlg.transform, "Name", 34f);
        var nameTmp = TMP(nameGO, "—", 14f, FontStyles.Bold,
            TextAlignmentOptions.Center, Color.white, font);

        return (iconImg, nameTmp);
    }

    private static (GameObject sec, TextMeshProUGUI atk, TextMeshProUGUI def)
        BuildMainSection(Transform parent, TMP_FontAsset font, bool isRight,
            out TextMeshProUGUI atkDelta, out TextMeshProUGUI defDelta)
    {
        atkDelta = null; defDelta = null;
        var sec = VLG(parent, "MainSection", 4f, new RectOffset(12, 12, 8, 8));

        Divider(sec.transform, "── 기본 스탯 ──", font);
        var atkTmp = StatRow(sec.transform, "공격력", font, isRight, out atkDelta);
        var defTmp = StatRow(sec.transform, "방어력", font, isRight, out defDelta);

        return (sec.gameObject, atkTmp, defTmp);
    }

    private static (GameObject sec,
        TextMeshProUGUI qName, TextMeshProUGUI qCool,
        TextMeshProUGUI qCoolDelta, TextMeshProUGUI qDesc)
        BuildSubSection(Transform parent, TMP_FontAsset font, bool isRight,
            out TextMeshProUGUI qCoolDeltaOut)
    {
        qCoolDeltaOut = null;
        var sec = VLG(parent, "SubSection", 4f, new RectOffset(12, 12, 8, 8));

        Divider(sec.transform, "── Q 스킬 ──", font);
        var qNameTmp = LabelRow(sec.transform, "Q 스킬",  font);
        var qCoolTmp = StatRow(sec.transform,  "Q 쿨다운", font, isRight, out var qCD);

        var descGO = FlexChild(sec.transform, "QDesc", 58f);
        var descTmp = TMP(descGO, "—", 11f, FontStyles.Normal,
            TextAlignmentOptions.TopLeft, ColorLabel, font);
        descTmp.enableWordWrapping = true;

        qCoolDeltaOut = qCD;
        return (sec.gameObject, qNameTmp, qCoolTmp, qCD, descTmp);
    }

    // ──────────────────────────────────────────────────────────────────
    // 행 빌더
    // ──────────────────────────────────────────────────────────────────

    private static TextMeshProUGUI StatRow(Transform parent, string label,
        TMP_FontAsset font, bool isRight, out TextMeshProUGUI deltaTmp)
    {
        deltaTmp = null;
        var row = FlexChild(parent, "_R_" + label, 26f);
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 6f; hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = false; hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        var lbl = Sized(row.transform, "_L", 72f, 26f);
        TMP(lbl, label, 12f, FontStyles.Normal,
            TextAlignmentOptions.MidlineLeft, ColorLabel, font);

        var val = Sized(row.transform, "_V", 60f, 26f);
        var valTmp = TMP(val, "—", 14f, FontStyles.Bold,
            TextAlignmentOptions.MidlineLeft, Color.white, font);

        if (isRight)
        {
            var d = Sized(row.transform, "_D", 88f, 26f);
            deltaTmp = TMP(d, "—", 12f, FontStyles.Normal,
                TextAlignmentOptions.MidlineLeft, ColorLabel, font);
        }
        return valTmp;
    }

    private static TextMeshProUGUI LabelRow(Transform parent, string label, TMP_FontAsset font)
    {
        var row = FlexChild(parent, "_LR_" + label, 26f);
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 6f; hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = false; hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        var lbl = Sized(row.transform, "_L", 72f, 26f);
        TMP(lbl, label, 12f, FontStyles.Normal,
            TextAlignmentOptions.MidlineLeft, ColorLabel, font);

        var val = Sized(row.transform, "_V", 140f, 26f);
        return TMP(val, "—", 13f, FontStyles.Bold,
            TextAlignmentOptions.MidlineLeft, Color.white, font);
    }

    private static void Divider(Transform parent, string text, TMP_FontAsset font)
    {
        var go = FlexChild(parent, "_Div", 22f);
        TMP(go, text, 10f, FontStyles.Normal,
            TextAlignmentOptions.Center, ColorDivider, font);
    }

    // ──────────────────────────────────────────────────────────────────
    // 레이아웃 헬퍼
    // ──────────────────────────────────────────────────────────────────

    private static VerticalLayoutGroup VLG(Transform parent, string name,
        float spacing, RectOffset padding = null)
    {
        var go = Obj(name, parent);
        Stretch(go.GetComponent<RectTransform>());
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = spacing;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true; vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        if (padding != null) vlg.padding = padding;
        return vlg;
    }

    private static GameObject FlexChild(Transform parent, string name, float h)
    {
        var go = Obj(name, parent);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, h);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = h; le.preferredHeight = h;
        return go;
    }

    private static GameObject Sized(Transform parent, string name, float w, float h)
    {
        var go = Obj(name, parent);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(w, h);
        return go;
    }

    private static Button BuildBtn(string name, Transform parent,
        string label, Color bg, TMP_FontAsset font,
        float x, float y, float w, float h)
    {
        var go = Obj(name, parent);
        SetCenter(go, w, h, x, y);
        go.AddComponent<Image>().color = bg;
        var btn = go.AddComponent<Button>();
        var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
        var txtGO = Obj("BtnText", go.transform);
        Stretch(txtGO.GetComponent<RectTransform>());
        TMP(txtGO, label, 15f, FontStyles.Bold, TextAlignmentOptions.Center, Color.white, font);
        return btn;
    }

    // ──────────────────────────────────────────────────────────────────
    // RectTransform 헬퍼
    // ──────────────────────────────────────────────────────────────────

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
    }

    private static void SetCenter(GameObject go, float w, float h, float x, float y)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, y);
    }

    private static void AnchorTop(GameObject go, float w, float h, float x, float y)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = new Vector2(x, y);
    }

    private static void AnchorBottom(GameObject go, float w, float h, float x, float y)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = new Vector2(x, y);
    }

    private static void AnchorSplit(GameObject go,
        float minX, float minY, float maxX, float maxY)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(minX, minY);
        rt.anchorMax = new Vector2(maxX, maxY);
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
    }

    // ──────────────────────────────────────────────────────────────────
    // 기타
    // ──────────────────────────────────────────────────────────────────

    private static GameObject Obj(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static TextMeshProUGUI TMP(GameObject go, string text, float size,
        FontStyles style, TextAlignmentOptions align, Color color, TMP_FontAsset font)
    {
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size; tmp.fontStyle = style;
        tmp.alignment = align; tmp.color = color;
        if (font != null) tmp.font = font;
        return tmp;
    }

    private static Image EnsureImage(GameObject go)
    {
        var img = go.GetComponent<Image>();
        return img != null ? img : go.AddComponent<Image>();
    }

    private static void B(SerializedObject so, string field, Object value)
    {
        var prop = so.FindProperty(field);
        if (prop != null) prop.objectReferenceValue = value;
        else Debug.LogWarning($"[Builder] 필드 없음: {field}");
    }
}
#endif
