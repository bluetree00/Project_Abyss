// WeaponReplacePopupBuilder.cs
// Tools 메뉴 → "Build WeaponReplacePopup Prefab" 실행 시 프리팹을 완전히 재빌드합니다.
#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

public static class WeaponReplacePopupBuilder
{
    private const string PrefabPath = "Assets/Abyss/UI/Popup/UI_WeaponReplacePopup.prefab";
    private const string FontPath   = "Assets/Abyss/Fonts/NotoSansKR-VariableFont_wght SDF.asset";

    // ── 색상 상수 ──────────────────────────────────────────────────────
    private static readonly Color BgPanel      = new Color(0.10f, 0.11f, 0.15f, 1.00f);
    private static readonly Color BgBlocker    = new Color(0.00f, 0.00f, 0.00f, 0.65f);
    private static readonly Color ColorGold    = new Color(1.00f, 0.85f, 0.40f, 1.00f);
    private static readonly Color ColorGreen   = new Color(0.20f, 0.85f, 0.40f, 1.00f);
    private static readonly Color ColorGray    = new Color(0.65f, 0.65f, 0.65f, 1.00f);
    private static readonly Color ColorSubText = new Color(0.75f, 0.75f, 0.75f, 1.00f);
    private static readonly Color BtnReplace   = new Color(0.18f, 0.55f, 0.28f, 1.00f);
    private static readonly Color BtnDiscard   = new Color(0.60f, 0.18f, 0.18f, 1.00f);
    private static readonly Color BtnHover     = new Color(1.00f, 1.00f, 1.00f, 0.15f);

    [MenuItem("Tools/Build WeaponReplacePopup Prefab")]
    public static void Build()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
            Debug.LogWarning($"[Builder] 폰트를 찾을 수 없습니다: {FontPath} — 기본 폰트 사용");

        using (var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath))
        {
            var root = scope.prefabContentsRoot;

            // ── 기존 자식 전부 제거 ────────────────────────────────────
            while (root.transform.childCount > 0)
                GameObject.DestroyImmediate(root.transform.GetChild(0).gameObject);

            // ── 루트: 풀스크린 블로커 ─────────────────────────────────
            SetStretch(root.GetComponent<RectTransform>());
            var rootImg = EnsureComponent<Image>(root);
            rootImg.color = BgBlocker;

            // ── Panel: 팝업 바디 (820 × 540) ─────────────────────────
            var panel = UIObj("Panel", root.transform);
            Center(panel, 820f, 540f, 0f, 0f);
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = BgPanel;

            // ── Title: "새 무기 획득!" ──────────────────────────────
            var titleGO  = UIObj("Title", panel.transform);
            AnchorTop(titleGO, 760f, 50f, 0f, -38f);
            AddTMP(titleGO, "새 무기 획득!", 28f, FontStyles.Bold, TextAlignmentOptions.Center, Color.white, font);

            // ── SlotLabel: "─── 메인 무기 ───" ───────────────────────
            var slotLabelGO  = UIObj("SlotLabel", panel.transform);
            AnchorTop(slotLabelGO, 760f, 34f, 0f, -82f);
            var slotLabelTmp = AddTMP(slotLabelGO, "─── 메인 무기 ───", 17f, FontStyles.Normal, TextAlignmentOptions.Center, ColorGold, font);

            // ── Divider ───────────────────────────────────────────────
            var divGO  = UIObj("Divider", panel.transform);
            AnchorTop(divGO, 720f, 1f, 0f, -118f);
            var divImg = divGO.AddComponent<Image>();
            divImg.color = new Color(1f, 1f, 1f, 0.12f);

            // ── 현재 무기 패널 (왼쪽) ────────────────────────────────
            var curPanel = UIObj("CurrentPanel", panel.transform);
            Center(curPanel, 240f, 220f, -230f, 40f);

            UIObj_TMP("Label_Current", curPanel.transform, "현재",
                16f, FontStyles.Normal, TextAlignmentOptions.Center, ColorSubText, font,
                w: 220f, h: 28f, anchorX: 0.5f, anchorY: 1f, posX: 0f, posY: -16f);

            var curIconGO  = UIObj("CurrentIcon", curPanel.transform);
            Center(curIconGO, 96f, 96f, 0f, 26f);
            var curIconImg = curIconGO.AddComponent<Image>();
            curIconImg.color = new Color(1f, 1f, 1f, 0.12f);

            var curNameGO  = UIObj("CurrentName", curPanel.transform);
            AnchorBottom(curNameGO, 220f, 34f, 0f, 52f);
            var curNameTmp = AddTMP(curNameGO, "—", 17f, FontStyles.Bold, TextAlignmentOptions.Center, Color.white, font);

            // ── VS 구분자 ─────────────────────────────────────────────
            var vsGO  = UIObj("VS", panel.transform);
            Center(vsGO, 44f, 44f, 0f, 40f);
            AddTMP(vsGO, "VS", 16f, FontStyles.Bold, TextAlignmentOptions.Center, ColorGray, font);

            // ── 새 무기 패널 (오른쪽) ─────────────────────────────────
            var newPanel = UIObj("NewPanel", panel.transform);
            Center(newPanel, 240f, 220f, 230f, 40f);

            UIObj_TMP("Label_New", newPanel.transform, "새 무기",
                16f, FontStyles.Normal, TextAlignmentOptions.Center, ColorGreen, font,
                w: 220f, h: 28f, anchorX: 0.5f, anchorY: 1f, posX: 0f, posY: -16f);

            var newIconGO  = UIObj("NewIcon", newPanel.transform);
            Center(newIconGO, 96f, 96f, 0f, 26f);
            var newIconImg = newIconGO.AddComponent<Image>();
            newIconImg.color = new Color(1f, 1f, 1f, 0.12f);

            var newNameGO  = UIObj("NewName", newPanel.transform);
            AnchorBottom(newNameGO, 220f, 34f, 0f, 52f);
            var newNameTmp = AddTMP(newNameGO, "—", 17f, FontStyles.Bold, TextAlignmentOptions.Center, Color.white, font);

            // ── 스탯 비교 행: 공격력 ──────────────────────────────────
            var atkRowGO = UIObj("StatRow_Atk", panel.transform);
            Center(atkRowGO, 700f, 38f, 0f, -128f);
            BuildStatRow(atkRowGO, "공격력", font,
                out var atkCurTmp, out var atkNewTmp, out var atkDeltaTmp);

            // ── 스탯 비교 행: 방어력 ──────────────────────────────────
            var defRowGO = UIObj("StatRow_Def", panel.transform);
            Center(defRowGO, 700f, 38f, 0f, -175f);
            BuildStatRow(defRowGO, "방어력", font,
                out var defCurTmp, out var defNewTmp, out var defDeltaTmp);

            // ── 버튼: 교체하기 / 버리기 ───────────────────────────────
            var replaceBtn = BuildButton("ReplaceButton", panel.transform, "교체하기", BtnReplace, font, -145f, -232f, 280f, 52f);
            var discardBtn = BuildButton("DiscardButton", panel.transform, "버리기",   BtnDiscard, font,  145f, -232f, 280f, 52f);

            // ── 직렬화 필드 바인딩 ────────────────────────────────────
            var popup = EnsureComponent<UI_WeaponReplacePopup>(root);
            var so    = new SerializedObject(popup);

            Bind(so, "slotLabelText",   slotLabelTmp);
            Bind(so, "currentIcon",     curIconImg);
            Bind(so, "currentName",     curNameTmp);
            Bind(so, "newIcon",         newIconImg);
            Bind(so, "newName",         newNameTmp);
            Bind(so, "atkCurrentText",  atkCurTmp);
            Bind(so, "atkNewText",      atkNewTmp);
            Bind(so, "atkDeltaText",    atkDeltaTmp);
            Bind(so, "defCurrentText",  defCurTmp);
            Bind(so, "defNewText",      defNewTmp);
            Bind(so, "defDeltaText",    defDeltaTmp);
            Bind(so, "replaceButton",   replaceBtn);
            Bind(so, "discardButton",   discardBtn);

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[WeaponReplacePopupBuilder] 프리팹 재빌드 완료.");
    }

    // ── UI 생성 헬퍼 ──────────────────────────────────────────────────

    private static GameObject UIObj(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static TextMeshProUGUI UIObj_TMP(string name, Transform parent,
        string text, float fontSize, FontStyles style, TextAlignmentOptions align,
        Color color, TMP_FontAsset font,
        float w, float h, float anchorX, float anchorY, float posX, float posY)
    {
        var go = UIObj(name, parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(anchorX, anchorY);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(posX, posY);
        return AddTMP(go, text, fontSize, style, align, color, font);
    }

    private static TextMeshProUGUI AddTMP(GameObject go, string text, float fontSize,
        FontStyles style, TextAlignmentOptions align, Color color, TMP_FontAsset font)
    {
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color     = color;
        if (font != null) tmp.font = font;
        return tmp;
    }

    private static Button BuildButton(string name, Transform parent, string label,
        Color bgColor, TMP_FontAsset font, float posX, float posY, float w, float h)
    {
        var go = UIObj(name, parent);
        Center(go, w, h, posX, posY);
        var img = go.AddComponent<Image>();
        img.color = bgColor;

        var btn = go.AddComponent<Button>();
        var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;

        var colors    = btn.colors;
        colors.highlightedColor = BtnHover;
        btn.colors    = colors;

        var textGO = UIObj("BtnText", go.transform);
        var rt     = textGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
        AddTMP(textGO, label, 18f, FontStyles.Bold, TextAlignmentOptions.Center, Color.white, font);

        return btn;
    }

    private static void BuildStatRow(GameObject rowGO, string statLabel, TMP_FontAsset font,
        out TextMeshProUGUI curTmp, out TextMeshProUGUI newTmp, out TextMeshProUGUI deltaTmp)
    {
        var hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing              = 10f;
        hlg.childAlignment       = TextAnchor.MiddleCenter;
        hlg.childControlWidth    = false;
        hlg.childControlHeight   = false;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;

        Cell(rowGO.transform, statLabel, 80f, 38f, 14f, ColorSubText, font);

        var curGO = Cell(rowGO.transform, "—", 64f, 38f, 17f, Color.white, font);
        curTmp    = curGO.GetComponent<TextMeshProUGUI>();

        Cell(rowGO.transform, "→", 28f, 38f, 14f, ColorGray, font);

        var newGO = Cell(rowGO.transform, "—", 64f, 38f, 17f, Color.white, font);
        newTmp    = newGO.GetComponent<TextMeshProUGUI>();

        var deltaGO = Cell(rowGO.transform, "—", 108f, 38f, 15f, ColorSubText, font);
        deltaTmp    = deltaGO.GetComponent<TextMeshProUGUI>();
    }

    private static GameObject Cell(Transform parent, string text, float w, float h,
        float fontSize, Color color, TMP_FontAsset font)
    {
        var go = UIObj("_" + text, parent);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        AddTMP(go, text, fontSize, FontStyles.Normal, TextAlignmentOptions.Center, color, font);
        return go;
    }

    // ── RectTransform 레이아웃 ─────────────────────────────────────────

    private static void SetStretch(RectTransform rt)
    {
        rt.anchorMin        = Vector2.zero;
        rt.anchorMax        = Vector2.one;
        rt.sizeDelta        = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
    }

    private static void Center(GameObject go, float w, float h, float x, float y)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, y);
    }

    private static void AnchorTop(GameObject go, float w, float h, float x, float y)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, y);
    }

    private static void AnchorBottom(GameObject go, float w, float h, float x, float y)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.sizeDelta        = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, y);
    }

    // ── 기타 헬퍼 ─────────────────────────────────────────────────────

    private static T EnsureComponent<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    private static void Bind(SerializedObject so, string fieldName, Object value)
    {
        var prop = so.FindProperty(fieldName);
        if (prop != null) prop.objectReferenceValue = value;
        else Debug.LogWarning($"[Builder] SerializedProperty not found: {fieldName}");
    }
}
#endif
