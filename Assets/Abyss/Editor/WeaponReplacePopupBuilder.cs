// WeaponReplacePopupBuilder.cs  — Tools → "Build WeaponReplacePopup Prefab"
// V2: 상단 새 장비 + 하단 슬롯 2개 비교 + 델타 표시
#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

public static class WeaponReplacePopupBuilder
{
    private const string PrefabPath = "Assets/Abyss/UI/Popup/UI_WeaponReplacePopup.prefab";
    private const string FontPath   = "Assets/Abyss/Fonts/NotoSansKR-VariableFont_wght SDF.asset";
    private const string Bamao     = "Assets/Abyss/Prefabs/UI/Bamao/BamaoUIPack/Sprites/";

    // ── 색상 (나무 테마 — 밝은 텍스트 on 어두운 배경) ─────────────────
    private static readonly Color BgBlocker    = new Color(0.00f, 0.00f, 0.00f, 0.70f);
    private static readonly Color ColorGold    = new Color(0.95f, 0.82f, 0.45f, 1.00f);
    private static readonly Color ColorLight   = new Color(0.92f, 0.88f, 0.78f, 1.00f);
    private static readonly Color ColorLabel   = new Color(0.75f, 0.68f, 0.55f, 1.00f);
    private static readonly Color ColorDark    = ColorLight;  // 나무 배경에서 밝은 텍스트
    private static readonly Color ColorStat    = ColorLight;

    private const float PanelW = 920f;
    private const float PanelH = 620f;

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

        // ── Panel (Bamao Popup_paper 배경) ─────────────────────────────
        var panel = O("Panel", root.transform);
        Center(panel, PanelW, PanelH, 0, 0);
        var panelImg = panel.AddComponent<Image>();
        panelImg.sprite = S(Bamao + "Button/Popup_wood.png");
        panelImg.type = Image.Type.Sliced;

        // ── Title (Des_title 배너) ────────────────────────────────────
        var titleBG = O("TitleBG", panel.transform);
        AnchorTop(titleBG, PanelW * 0.6f, 44f, 0, -8f);
        var titleBGImg = titleBG.AddComponent<Image>();
        titleBGImg.sprite = S(Bamao + "Button/Popup_wood_title.png");
        titleBGImg.type = Image.Type.Sliced;

        var titleGO = O("Title", titleBG.transform);
        Stretch(titleGO.GetComponent<RectTransform>());
        T(titleGO, "장비 교체", 22f, FontStyles.Bold,
            TextAlignmentOptions.Center, ColorGold, font);

        // ══════════════════════════════════════════════════════════════
        // 상단: 새 장비 카드 (Des_board 배경)
        // ══════════════════════════════════════════════════════════════
        var newArea = O("NewWeaponArea", panel.transform);
        AnchorTop(newArea, PanelW - 40f, 160f, 0, -58f);
        var newAreaImg = newArea.AddComponent<Image>();
        newAreaImg.sprite = S(Bamao + "Button/Popup_wood_bg.png");
        newAreaImg.type = Image.Type.Sliced;
        newAreaImg.type = Image.Type.Sliced;

        // 새 장비 라벨
        var newLblGO = O("NewLabel", newArea.transform);
        Split(newLblGO, 0f, 0.82f, 1f, 1f);
        T(newLblGO, "★ 새로운 장비 ★", 15f, FontStyles.Bold,
            TextAlignmentOptions.Center, ColorGold, font);

        // 아이콘 (E_Blank 프레임)
        var newIconBG = O("NewIconBG", newArea.transform);
        Split(newIconBG, 0.04f, 0.08f, 0.25f, 0.78f);
        var newIconBGImg = newIconBG.AddComponent<Image>();
        newIconBGImg.sprite = S(Bamao + "Equipment/E_BG.png");
        newIconBGImg.type = Image.Type.Sliced;

        var newIconGO = O("NewIcon", newIconBG.transform);
        Stretch(newIconGO.GetComponent<RectTransform>());
        var newIconImg = newIconGO.AddComponent<Image>();
        newIconImg.color = new Color(1, 1, 1, 0.3f);
        newIconImg.preserveAspect = true;

        // 이름
        var newNameGO = O("NewName", newArea.transform);
        Split(newNameGO, 0.28f, 0.58f, 0.95f, 0.78f);
        var newNameTmp = T(newNameGO, "—", 18f, FontStyles.Bold,
            TextAlignmentOptions.MidlineLeft, ColorDark, font);

        // ATK
        var newAtkGO = O("NewAtk", newArea.transform);
        Split(newAtkGO, 0.28f, 0.38f, 0.60f, 0.56f);
        var newAtkTmp = T(newAtkGO, "ATK: 0", 15f, FontStyles.Normal,
            TextAlignmentOptions.MidlineLeft, ColorStat, font);

        // DEF
        var newDefGO = O("NewDef", newArea.transform);
        Split(newDefGO, 0.28f, 0.18f, 0.60f, 0.36f);
        var newDefTmp = T(newDefGO, "DEF: 0", 15f, FontStyles.Normal,
            TextAlignmentOptions.MidlineLeft, ColorStat, font);

        // Skill text
        var newSkillGO = O("NewSkill", newArea.transform);
        Split(newSkillGO, 0.28f, 0.02f, 0.55f, 0.18f);
        var newSkillTmp = T(newSkillGO, "---", 13f, FontStyles.Italic,
            TextAlignmentOptions.MidlineLeft, ColorLabel, font);

        // Q/E 스킬 아이콘 (새 장비)
        var newQIcon = O("NewQSkillIcon", newArea.transform);
        Split(newQIcon, 0.68f, 0.02f, 0.82f, 0.18f);
        var newQIconImg = newQIcon.AddComponent<Image>();
        newQIconImg.color = new Color(1, 1, 1, 0.2f); newQIconImg.preserveAspect = true;

        var newEIcon = O("NewESkillIcon", newArea.transform);
        Split(newEIcon, 0.84f, 0.02f, 0.98f, 0.18f);
        var newEIconImg = newEIcon.AddComponent<Image>();
        newEIconImg.color = new Color(1, 1, 1, 0.2f); newEIconImg.preserveAspect = true;

        // ── 프롬프트 (구분선 + 텍스트) ─────────────────────────────────
        var promptGO = O("Prompt", panel.transform);
        AnchorTop(promptGO, PanelW - 40f, 28f, 0, -224f);
        T(promptGO, "어떤 장비와 교체하시겠습니까?", 14f, FontStyles.Bold,
            TextAlignmentOptions.Center, ColorGold, font);

        // ══════════════════════════════════════════════════════════════
        // 하단: 슬롯 0 / 슬롯 1 카드 (좌우 배치)
        // ══════════════════════════════════════════════════════════════
        float slotAreaTop = -260f;
        float slotAreaH   = 250f;
        float slotW       = (PanelW - 60f) / 2f; // 각 슬롯 카드 폭

        // --- Slot 0 (왼쪽, Des_frame 배경) ---
        var slot0Card = O("Slot0Card", panel.transform);
        AnchorTop(slot0Card, slotW, slotAreaH, -(slotW / 2f + 10f), slotAreaTop);
        var s0CardImg = slot0Card.AddComponent<Image>();
        s0CardImg.sprite = S(Bamao + "Equipment/Wood block.png");
        s0CardImg.type = Image.Type.Sliced;

        var (s0Icon, s0Name, s0Atk, s0AtkDelta, s0Def, s0DefDelta, s0Skill, s0QIcon, s0EIcon) =
            BuildSlotCard(slot0Card.transform, "슬롯 1", font);

        var s0Btn = SpriteBtn("Slot0ReplaceBtn", slot0Card.transform,
            "교체", S(Bamao + "Button/claim blue.png"), font, 0f, -slotAreaH / 2f + 30f, slotW - 40f, 42f);

        // --- Slot 1 (오른쪽, Des_frame 배경) ---
        var slot1Card = O("Slot1Card", panel.transform);
        AnchorTop(slot1Card, slotW, slotAreaH, slotW / 2f + 10f, slotAreaTop);
        var s1CardImg = slot1Card.AddComponent<Image>();
        s1CardImg.sprite = S(Bamao + "Equipment/Wood block.png");
        s1CardImg.type = Image.Type.Sliced;

        var (s1Icon, s1Name, s1Atk, s1AtkDelta, s1Def, s1DefDelta, s1Skill, s1QIcon, s1EIcon) =
            BuildSlotCard(slot1Card.transform, "슬롯 2", font);

        var s1Btn = SpriteBtn("Slot1ReplaceBtn", slot1Card.transform,
            "교체", S(Bamao + "Button/claim blue.png"), font,
            0f, -slotAreaH / 2f + 30f, slotW - 40f, 42f);

        // ── 버리기 버튼 (맨 아래, button short red) ───────────────────
        var discardBtn = SpriteBtn("DiscardButton", panel.transform,
            "버리기", S(Bamao + "Button/claim red.png"), font,
            0f, -PanelH / 2f + 30f, 280f, 42f);

        // ══════════════════════════════════════════════════════════════
        // 툴팁 패널 (스킬 아이콘 호버 시 표시)
        // ══════════════════════════════════════════════════════════════
        var tooltip = O("TooltipPanel", root.transform);
        var tooltipRT = tooltip.GetComponent<RectTransform>();
        tooltipRT.anchorMin = tooltipRT.anchorMax = tooltipRT.pivot = new Vector2(0f, 0f);
        tooltipRT.sizeDelta = new Vector2(280f, 120f);
        var tooltipImg = tooltip.AddComponent<Image>();
        tooltipImg.sprite = S(Bamao + "Panel/Des_board.png");
        tooltipImg.type = Image.Type.Sliced;
        tooltip.SetActive(false);

        // 위에 배치될 수 있도록 Canvas 오버라이드
        var tooltipCanvas = tooltip.AddComponent<Canvas>();
        tooltipCanvas.overrideSorting = true;
        tooltipCanvas.sortingOrder = 100;
        tooltip.AddComponent<GraphicRaycaster>();

        var tooltipVLG = tooltip.AddComponent<VerticalLayoutGroup>();
        tooltipVLG.padding = new RectOffset(10, 10, 8, 8);
        tooltipVLG.spacing = 4f;
        tooltipVLG.childControlWidth = true;
        tooltipVLG.childControlHeight = false;
        tooltipVLG.childForceExpandWidth = true;
        tooltipVLG.childForceExpandHeight = false;

        var ttNameGO = O("TT_Name", tooltip.transform);
        ttNameGO.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 24);
        ttNameGO.AddComponent<LayoutElement>().preferredHeight = 24;
        var ttNameTmp = T(ttNameGO, "스킬 이름", 16f, FontStyles.Bold,
            TextAlignmentOptions.TopLeft, Color.white, font);

        var ttDescGO = O("TT_Desc", tooltip.transform);
        ttDescGO.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 50);
        ttDescGO.AddComponent<LayoutElement>().preferredHeight = 50;
        var ttDescTmp = T(ttDescGO, "설명", 12f, FontStyles.Normal,
            TextAlignmentOptions.TopLeft, ColorLabel, font);
        ttDescTmp.enableWordWrapping = true;

        var ttCoolGO = O("TT_Cooldown", tooltip.transform);
        ttCoolGO.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 20);
        ttCoolGO.AddComponent<LayoutElement>().preferredHeight = 20;
        var ttCoolTmp = T(ttCoolGO, "쿨다운: 0초", 12f, FontStyles.Normal,
            TextAlignmentOptions.TopLeft, ColorGold, font);

        // ══════════════════════════════════════════════════════════════
        // 바인딩
        // ══════════════════════════════════════════════════════════════
        var popup = root.GetComponent<UI_WeaponReplacePopup>()
                    ?? root.AddComponent<UI_WeaponReplacePopup>();
        var so = new SerializedObject(popup);

        B(so, "slot0Icon",          s0Icon);
        B(so, "slot0Name",          s0Name);
        B(so, "slot0AtkText",       s0Atk);
        B(so, "slot0AtkDelta",      s0AtkDelta);
        B(so, "slot0DefText",       s0Def);
        B(so, "slot0DefDelta",      s0DefDelta);
        B(so, "slot0SkillText",     s0Skill);
        B(so, "slot0QSkillIcon",   s0QIcon);
        B(so, "slot0ESkillIcon",   s0EIcon);
        B(so, "slot0ReplaceButton", s0Btn);

        B(so, "newIcon",            newIconImg);
        B(so, "newName",            newNameTmp);
        B(so, "newAtkText",         newAtkTmp);
        B(so, "newDefText",         newDefTmp);
        B(so, "newSkillText",       newSkillTmp);
        B(so, "newQSkillIcon",     newQIconImg);
        B(so, "newESkillIcon",     newEIconImg);
        B(so, "discardButton",      discardBtn);

        B(so, "slot1Icon",          s1Icon);
        B(so, "slot1Name",          s1Name);
        B(so, "slot1AtkText",       s1Atk);
        B(so, "slot1AtkDelta",      s1AtkDelta);
        B(so, "slot1DefText",       s1Def);
        B(so, "slot1DefDelta",      s1DefDelta);
        B(so, "slot1SkillText",     s1Skill);
        B(so, "slot1QSkillIcon",   s1QIcon);
        B(so, "slot1ESkillIcon",   s1EIcon);
        B(so, "slot1ReplaceButton", s1Btn);

        B(so, "tooltipPanel",      tooltip);
        B(so, "tooltipName",       ttNameTmp);
        B(so, "tooltipDesc",       ttDescTmp);
        B(so, "tooltipCooldown",   ttCoolTmp);

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[WeaponReplacePopupBuilder] V2 재빌드 완료.");
    }

    // ──────────────────────────────────────────────────────────────────
    // 슬롯 카드 빌더 (아이콘 왼쪽 + 이름/스탯 오른쪽 + 델타)
    // ──────────────────────────────────────────────────────────────────

    private static (Image icon, TextMeshProUGUI name,
        TextMeshProUGUI atk, TextMeshProUGUI atkDelta,
        TextMeshProUGUI def, TextMeshProUGUI defDelta,
        TextMeshProUGUI skill, Image qSkillIcon, Image eSkillIcon)
        BuildSlotCard(Transform parent, string label, TMP_FontAsset font)
    {
        // 라벨
        var lblGO = O("Label", parent);
        Split(lblGO, 0f, 0.88f, 1f, 1f);
        T(lblGO, label, 13f, FontStyles.Bold, TextAlignmentOptions.Center, ColorGold, font);

        // 아이콘 (E_Blank 프레임)
        var iconBG = O("IconBG", parent);
        Split(iconBG, 0.04f, 0.35f, 0.32f, 0.85f);
        var iconBGImg = iconBG.AddComponent<Image>();
        iconBGImg.sprite = S(Bamao + "Equipment/E_BG.png");
        iconBGImg.type = Image.Type.Sliced;

        var iconGO = O("Icon", iconBG.transform);
        Stretch(iconGO.GetComponent<RectTransform>());
        var iconImg = iconGO.AddComponent<Image>();
        iconImg.color = new Color(1, 1, 1, 0.2f);
        iconImg.preserveAspect = true;

        // 이름
        var nameGO = O("Name", parent);
        Split(nameGO, 0.35f, 0.72f, 0.95f, 0.85f);
        var nameTmp = T(nameGO, "—", 15f, FontStyles.Bold,
            TextAlignmentOptions.MidlineLeft, ColorDark, font);

        // ATK + Delta
        var atkGO = O("Atk", parent);
        Split(atkGO, 0.35f, 0.55f, 0.68f, 0.70f);
        var atkTmp = T(atkGO, "ATK: 0", 13f, FontStyles.Normal,
            TextAlignmentOptions.MidlineLeft, ColorStat, font);

        var atkDeltaGO = O("AtkDelta", parent);
        Split(atkDeltaGO, 0.68f, 0.55f, 0.98f, 0.70f);
        var atkDeltaTmp = T(atkDeltaGO, "", 12f, FontStyles.Bold,
            TextAlignmentOptions.MidlineLeft, ColorStat, font);

        // DEF + Delta
        var defGO = O("Def", parent);
        Split(defGO, 0.35f, 0.38f, 0.68f, 0.53f);
        var defTmp = T(defGO, "DEF: 0", 13f, FontStyles.Normal,
            TextAlignmentOptions.MidlineLeft, ColorStat, font);

        var defDeltaGO = O("DefDelta", parent);
        Split(defDeltaGO, 0.68f, 0.38f, 0.98f, 0.53f);
        var defDeltaTmp = T(defDeltaGO, "", 12f, FontStyles.Bold,
            TextAlignmentOptions.MidlineLeft, ColorStat, font);

        // Skill text
        var skillGO = O("Skill", parent);
        Split(skillGO, 0.35f, 0.22f, 0.68f, 0.37f);
        var skillTmp = T(skillGO, "---", 12f, FontStyles.Italic,
            TextAlignmentOptions.MidlineLeft, ColorLabel, font);

        // Q/E 스킬 아이콘
        var qIconGO = O("QSkillIcon", parent);
        Split(qIconGO, 0.70f, 0.22f, 0.84f, 0.37f);
        var qIconImg = qIconGO.AddComponent<Image>();
        qIconImg.color = new Color(1, 1, 1, 0.2f); qIconImg.preserveAspect = true;

        var eIconGO = O("ESkillIcon", parent);
        Split(eIconGO, 0.86f, 0.22f, 1.0f, 0.37f);
        var eIconImg = eIconGO.AddComponent<Image>();
        eIconImg.color = new Color(1, 1, 1, 0.2f); eIconImg.preserveAspect = true;

        return (iconImg, nameTmp, atkTmp, atkDeltaTmp, defTmp, defDeltaTmp, skillTmp, qIconImg, eIconImg);
    }

    // ──────────────────────────────────────────────────────────────────
    // 헬퍼
    // ──────────────────────────────────────────────────────────────────

    private static Button SpriteBtn(string name, Transform parent,
        string label, Sprite bg, TMP_FontAsset font,
        float x, float y, float w, float h)
    {
        var go = O(name, parent);
        Center(go, w, h, x, y);
        var img = go.AddComponent<Image>();
        img.sprite = bg;
        img.type = Image.Type.Sliced;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
        var txtGO = O("BtnText", go.transform);
        Stretch(txtGO.GetComponent<RectTransform>());
        T(txtGO, label, 15f, FontStyles.Bold, TextAlignmentOptions.Center, Color.white, font);
        return btn;
    }

    private static Sprite S(string path)
    {
        var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (s == null) Debug.LogWarning($"[Builder] 스프라이트 없음: {path}");
        return s;
    }

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

    private static void Split(GameObject go,
        float minX, float minY, float maxX, float maxY)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(minX, minY); rt.anchorMax = new Vector2(maxX, maxY);
        rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
    }

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
