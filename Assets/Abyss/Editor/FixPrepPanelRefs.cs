using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

/// <summary>
/// LobbyRoot.prefab 안의 Panel_Prep을 새 3-스테이트 구조로 재구성하고
/// UI_PrepPanel의 모든 SerializeField를 자동 연결합니다.
///
/// 생성 구조:
///   Panel_Prep
///   ├─ Panel_Main          ← 메인 (캐릭터 슬롯 + 무기 슬롯 + 게임 시작)
///   ├─ Panel_CharSelect    ← 캐릭터 선택 (그리드 + 오른쪽 프리뷰)
///   └─ Panel_WeaponSelect  ← 무기 선택 (그리드 + 오른쪽 프리뷰)
/// </summary>
public static class FixPrepPanelRefs
{
    private const string PrefabPath      = "Assets/Abyss/UI/Scene/ScenePrefabs/LobbyRoot.prefab";
    private const string WeaponRosterPath = "Assets/Abyss/Shared/Characters/Weapon/WeaponRoster.asset";
    private const string CharRosterPath   = "Assets/Abyss/Characters/Player/Knight/Roster.asset";
    private const string FontPath         = "Assets/Abyss/Fonts/NotoSansKR-VariableFont_wght SDF.asset";

    private static TMP_FontAsset _font;

    [MenuItem("Tools/Fix PrepPanel Inspector Refs")]
    public static void Fix()
    {
        _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (_font == null) Debug.LogWarning("[FixPrepPanelRefs] 폰트 로드 실패 — 기본 폰트 사용");

        using var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath);
        var root = scope.prefabContentsRoot;

        var prepPanel = root.transform.Find("Panel_Prep");
        if (prepPanel == null) { Debug.LogError("[FixPrepPanelRefs] Panel_Prep not found"); return; }

        var ui = prepPanel.GetComponent<UI_PrepPanel>();
        if (ui == null) { Debug.LogError("[FixPrepPanelRefs] UI_PrepPanel not found"); return; }

        // 기존 하위 오브젝트 정리 후 새 구조 생성
        ClearChildren(prepPanel);

        var main        = CreateMainPanel(prepPanel);
        var charSelect  = CreateCharSelectPanel(prepPanel);
        var weaponSelect= CreateWeaponSelectPanel(prepPanel);

        // ─── SerializedObject로 필드 연결 ─────────────
        var so = new SerializedObject(ui);

        // 서브 패널
        so.FindProperty("panelMain").objectReferenceValue          = main.root.gameObject;
        so.FindProperty("panelCharSelect").objectReferenceValue    = charSelect.root.gameObject;
        so.FindProperty("panelWeaponSelect").objectReferenceValue  = weaponSelect.root.gameObject;

        // 메인
        so.FindProperty("charSlotButton").objectReferenceValue     = main.charSlotBtn;
        so.FindProperty("charSlotPortrait").objectReferenceValue   = main.charSlotImg;
        so.FindProperty("charSlotName").objectReferenceValue       = main.charSlotTxt;
        so.FindProperty("weaponSlotButton").objectReferenceValue   = main.weaponSlotBtn;
        so.FindProperty("weaponSlotIcon").objectReferenceValue     = main.weaponSlotImg;
        so.FindProperty("weaponSlotName").objectReferenceValue     = main.weaponSlotTxt;
        so.FindProperty("gameStartButton").objectReferenceValue    = main.gameStartBtn;
        so.FindProperty("cancelButton").objectReferenceValue       = main.cancelBtn;

        // 캐릭터 선택
        var charRoster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(CharRosterPath);
        if (charRoster != null)
            so.FindProperty("roster").objectReferenceValue = charRoster;
        else
            Debug.LogWarning("[FixPrepPanelRefs] CharacterRoster.asset 없음 — 수동 연결 필요");

        so.FindProperty("characterListRoot").objectReferenceValue  = charSelect.listContent;
        so.FindProperty("itemTemplate").objectReferenceValue       = charSelect.itemTemplate;
        so.FindProperty("charPreviewImage").objectReferenceValue   = charSelect.previewImg;
        so.FindProperty("charPreviewName").objectReferenceValue    = charSelect.previewName;
        so.FindProperty("sliderHp").objectReferenceValue           = charSelect.sliderHp;
        so.FindProperty("sliderHpText").objectReferenceValue       = charSelect.sliderHpTxt;
        so.FindProperty("sliderAtk").objectReferenceValue          = charSelect.sliderAtk;
        so.FindProperty("sliderAtkText").objectReferenceValue      = charSelect.sliderAtkTxt;
        so.FindProperty("sliderSpd").objectReferenceValue          = charSelect.sliderSpd;
        so.FindProperty("sliderSpdText").objectReferenceValue      = charSelect.sliderSpdTxt;
        so.FindProperty("charConfirmButton").objectReferenceValue  = charSelect.confirmBtn;

        // 무기 선택
        var weaponRoster = AssetDatabase.LoadAssetAtPath<WeaponRoster>(WeaponRosterPath);
        if (weaponRoster != null)
            so.FindProperty("weaponRoster").objectReferenceValue = weaponRoster;
        else
            Debug.LogWarning("[FixPrepPanelRefs] WeaponRoster.asset 없음 — 수동 연결 필요");

        so.FindProperty("weaponListRoot").objectReferenceValue      = weaponSelect.listContent;
        so.FindProperty("weaponItemTemplate").objectReferenceValue  = weaponSelect.itemTemplate;
        so.FindProperty("weaponPreviewImage").objectReferenceValue  = weaponSelect.previewImg;
        so.FindProperty("weaponPreviewName").objectReferenceValue   = weaponSelect.previewName;
        so.FindProperty("weaponAtkText").objectReferenceValue       = weaponSelect.atkText;
        so.FindProperty("weaponAtkSpeedText").objectReferenceValue  = weaponSelect.atkSpeedText;
        so.FindProperty("weaponRangeText").objectReferenceValue     = weaponSelect.rangeText;
        so.FindProperty("weaponConfirmButton").objectReferenceValue = weaponSelect.confirmBtn;

        so.ApplyModifiedProperties();

        // ─── 연결 검증 ─────────────────────────────────────
        var ui2 = prepPanel.GetComponent<UI_PrepPanel>();
        var so2 = new SerializedObject(ui2);
        so2.Update();
        bool rosterOK       = so2.FindProperty("roster").objectReferenceValue != null;
        bool listRootOK     = so2.FindProperty("characterListRoot").objectReferenceValue != null;
        bool itemTmplOK     = so2.FindProperty("itemTemplate").objectReferenceValue != null;
        bool wRosterOK      = so2.FindProperty("weaponRoster").objectReferenceValue != null;
        bool wListRootOK    = so2.FindProperty("weaponListRoot").objectReferenceValue != null;
        bool wItemTmplOK    = so2.FindProperty("weaponItemTemplate").objectReferenceValue != null;

        if (!rosterOK)    Debug.LogError("[FixPrepPanelRefs] ❌ roster 연결 실패");
        if (!listRootOK)  Debug.LogError("[FixPrepPanelRefs] ❌ characterListRoot 연결 실패");
        if (!itemTmplOK)  Debug.LogError("[FixPrepPanelRefs] ❌ itemTemplate 연결 실패");
        if (!wRosterOK)   Debug.LogError("[FixPrepPanelRefs] ❌ weaponRoster 연결 실패");
        if (!wListRootOK) Debug.LogError("[FixPrepPanelRefs] ❌ weaponListRoot 연결 실패");
        if (!wItemTmplOK) Debug.LogError("[FixPrepPanelRefs] ❌ weaponItemTemplate 연결 실패");

        if (rosterOK && listRootOK && itemTmplOK && wRosterOK && wListRootOK && wItemTmplOK)
            Debug.Log("[FixPrepPanelRefs] ✅ 3-State 구조 생성 및 전체 필드 연결 완료!");
        else
            Debug.LogWarning("[FixPrepPanelRefs] ⚠️ 일부 필드 연결 실패 — 위 ❌ 항목 확인");
    }

    /// <summary>
    /// Assets/Abyss/UI 폴더 안 모든 프리팹의 TMP 텍스트에
    /// NotoSansKR 폰트를 적용하고 가시성 기준으로 크기를 보정합니다.
    /// </summary>
    [MenuItem("Tools/Fix All UI Fonts")]
    public static void FixAllFonts()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null) { Debug.LogError("[FixAllFonts] 폰트를 찾을 수 없음: " + FontPath); return; }

        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Abyss/UI" });
        int totalTexts = 0, totalPrefabs = 0;

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            using var scope = new PrefabUtility.EditPrefabContentsScope(path);
            var root = scope.prefabContentsRoot;

            int before = totalTexts;
            foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                t.font = font;
                // 가시성 보정: 최소 16pt, 버튼 텍스트 최소 18pt
                if (t.fontSize < 16f) t.fontSize = 16f;
                else if (t.fontSize < 18f && t.GetComponentInParent<Button>() != null)
                    t.fontSize = 18f;
                totalTexts++;
            }
            if (totalTexts > before) totalPrefabs++;
        }

        Debug.Log($"[FixAllFonts] {totalPrefabs}개 프리팹 · {totalTexts}개 텍스트에 NotoSansKR 적용 완료");
    }

    // ══════════════════════════════════════════════════════
    // 패널 생성
    // ══════════════════════════════════════════════════════

    private struct MainPanelRefs
    {
        public RectTransform root;
        public Button  charSlotBtn;   public Image    charSlotImg;   public TMP_Text charSlotTxt;
        public Button  weaponSlotBtn; public Image    weaponSlotImg; public TMP_Text weaponSlotTxt;
        public Button  gameStartBtn;
        public Button  cancelBtn;
    }

    private struct CharSelectRefs
    {
        public RectTransform root;
        public Transform listContent;
        public UI_CharacterSelectItem itemTemplate;
        public Image    previewImg;  public TMP_Text previewName;
        public Slider   sliderHp;   public TMP_Text sliderHpTxt;
        public Slider   sliderAtk;  public TMP_Text sliderAtkTxt;
        public Slider   sliderSpd;  public TMP_Text sliderSpdTxt;
        public Button   confirmBtn;
    }

    private struct WeaponSelectRefs
    {
        public RectTransform root;
        public Transform listContent;
        public UI_WeaponSelectItem itemTemplate;
        public Image    previewImg;  public TMP_Text previewName;
        public TMP_Text atkText;
        public TMP_Text atkSpeedText;
        public TMP_Text rangeText;
        public Button   confirmBtn;
    }

    // ── 메인 패널 ─────────────────────────────────────────
    private static MainPanelRefs CreateMainPanel(Transform parent)
    {
        var refs = new MainPanelRefs();
        var panelGO = CreateFullStretchPanel(parent, "Panel_Main");
        refs.root = panelGO;

        // 배경
        var bg = panelGO.gameObject.AddComponent<Image>();
        bg.color = new Color(0.95f, 0.85f, 0.75f, 1f);

        // 제목
        CreateLabel(panelGO, "Txt_Title", "게임 준비",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -80f), new Vector2(0f, 0f), 36);

        // ── 캐릭터 슬롯 (왼쪽) ──
        var charSlot = CreatePanel(panelGO, "Slot_Character",
            new Vector2(0.05f, 0.15f), new Vector2(0.45f, 0.9f));
        var charSlotBg = charSlot.gameObject.AddComponent<Image>();
        charSlotBg.color = new Color(0.25f, 0.45f, 0.65f, 1f);

        refs.charSlotBtn = charSlot.gameObject.AddComponent<Button>();
        refs.charSlotImg = CreateChildImage(charSlot, "Img_Portrait",
            new Vector2(0.1f, 0.3f), new Vector2(0.9f, 0.95f));
        refs.charSlotTxt = CreateLabel(charSlot, "Txt_CharName", "캐릭터 선택",
            new Vector2(0f, 0f), new Vector2(1f, 0.28f), Vector2.zero, Vector2.zero, 22);

        // ── 무기 슬롯 (오른쪽) ──
        var weaponSlot = CreatePanel(panelGO, "Slot_Weapon",
            new Vector2(0.55f, 0.15f), new Vector2(0.95f, 0.9f));
        var weaponSlotBg = weaponSlot.gameObject.AddComponent<Image>();
        weaponSlotBg.color = new Color(0.25f, 0.45f, 0.65f, 1f);

        refs.weaponSlotBtn = weaponSlot.gameObject.AddComponent<Button>();
        refs.weaponSlotImg = CreateChildImage(weaponSlot, "Img_WeaponIcon",
            new Vector2(0.1f, 0.3f), new Vector2(0.9f, 0.95f));
        refs.weaponSlotTxt = CreateLabel(weaponSlot, "Txt_WeaponName", "무기 선택",
            new Vector2(0f, 0f), new Vector2(1f, 0.28f), Vector2.zero, Vector2.zero, 22);

        // ── 게임 시작 버튼 ──
        var gameStartGO = CreatePanel(panelGO, "Btn_GameStart",
            new Vector2(0.6f, 0.02f), new Vector2(0.95f, 0.13f));
        var gameStartImg = gameStartGO.gameObject.AddComponent<Image>();
        gameStartImg.color = new Color(0.15f, 0.35f, 0.55f, 1f);
        refs.gameStartBtn = gameStartGO.gameObject.AddComponent<Button>();
        CreateLabel(gameStartGO, "Txt_GameStart", "게임 시작",
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 26);

        // ── 취소 버튼 ──
        var cancelGO = CreatePanel(panelGO, "Btn_Cancel",
            new Vector2(0.05f, 0.02f), new Vector2(0.3f, 0.13f));
        cancelGO.gameObject.AddComponent<Image>().color = new Color(0.5f, 0.3f, 0.3f, 1f);
        refs.cancelBtn = cancelGO.gameObject.AddComponent<Button>();
        CreateLabel(cancelGO, "Txt_Cancel", "취소",
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 22);

        return refs;
    }

    // ── 캐릭터 선택 패널 ──────────────────────────────────
    private static CharSelectRefs CreateCharSelectPanel(Transform parent)
    {
        var refs = new CharSelectRefs();
        var panelGO = CreateFullStretchPanel(parent, "Panel_CharSelect");
        panelGO.gameObject.AddComponent<Image>().color = new Color(0.93f, 0.83f, 0.72f, 1f);
        panelGO.gameObject.SetActive(false);
        refs.root = panelGO;

        // 제목 (상단 중앙 배너)
        var titleBG = CreatePanel(panelGO, "Txt_Title_BG",
            new Vector2(0.25f, 0.88f), new Vector2(0.75f, 1.0f));
        titleBG.gameObject.AddComponent<Image>().color = new Color(0.25f, 0.45f, 0.65f, 1f);
        CreateLabel(titleBG, "Txt_Title", "캐릭터",
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 30);

        // ── 왼쪽: 캐릭터 선택 박스 ──
        var leftBG = CreatePanel(panelGO, "Panel_CharList",
            new Vector2(0.02f, 0.10f), new Vector2(0.43f, 0.87f));
        leftBG.gameObject.AddComponent<Image>().color = new Color(0.80f, 0.70f, 0.60f, 1f);

        // "캐릭터 선택" 헤더
        var listHeaderBG = CreatePanel(leftBG, "Txt_ListHeader_BG",
            new Vector2(0f, 0.88f), new Vector2(1f, 1f));
        listHeaderBG.gameObject.AddComponent<Image>().color = new Color(0.25f, 0.45f, 0.65f, 1f);
        CreateLabel(listHeaderBG, "Txt_ListHeader", "캐릭터 선택",
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 20);

        // 그리드 스크롤
        var (scrollContent, charItemTemplate) = CreateCharGridScrollView(leftBG, "CharScrollPanel",
            new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.87f));
        refs.listContent  = scrollContent;
        refs.itemTemplate = charItemTemplate;

        // ── 오른쪽: 프리뷰 패널 ──
        var rightBG = CreatePanel(panelGO, "Panel_CharPreview",
            new Vector2(0.45f, 0.10f), new Vector2(0.98f, 0.87f));
        rightBG.gameObject.AddComponent<Image>().color = new Color(0.80f, 0.70f, 0.60f, 1f);

        // 오른쪽 > 초상화 (왼쪽 절반)
        refs.previewImg = CreateChildImage(rightBG, "Img_Portrait",
            new Vector2(0.03f, 0.05f), new Vector2(0.50f, 0.95f));
        refs.previewImg.color = new Color(0.25f, 0.45f, 0.65f, 1f);

        // 오른쪽 > 스탯 영역 (오른쪽 절반)
        var statsPanel = CreatePanel(rightBG, "Panel_Stats",
            new Vector2(0.53f, 0.05f), new Vector2(0.97f, 0.95f));

        refs.previewName = CreateLabel(statsPanel, "Txt_CharName", "캐릭터 이름",
            new Vector2(0f, 0.82f), new Vector2(1f, 0.98f), Vector2.zero, Vector2.zero, 18);

        (refs.sliderHp,  refs.sliderHpTxt)  = CreateSliderRow(statsPanel, "Row_Hp",  "체력",
            new Vector2(0f, 0.60f), new Vector2(1f, 0.76f));
        (refs.sliderAtk, refs.sliderAtkTxt) = CreateSliderRow(statsPanel, "Row_Atk", "방어력",
            new Vector2(0f, 0.40f), new Vector2(1f, 0.56f));
        (refs.sliderSpd, refs.sliderSpdTxt) = CreateSliderRow(statsPanel, "Row_Spd", "이동속도",
            new Vector2(0f, 0.20f), new Vector2(1f, 0.36f));

        // 선택 완료 버튼 (우하단)
        var confirmGO = CreatePanel(panelGO, "Btn_CharConfirm",
            new Vector2(0.60f, 0.01f), new Vector2(0.98f, 0.09f));
        confirmGO.gameObject.AddComponent<Image>().color = new Color(0.15f, 0.35f, 0.55f, 1f);
        refs.confirmBtn = confirmGO.gameObject.AddComponent<Button>();
        refs.confirmBtn.interactable = false;
        CreateLabel(confirmGO, "Txt_Confirm", "선택 완료",
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 22);

        return refs;
    }

    // ── 무기 선택 패널 ────────────────────────────────────
    private static WeaponSelectRefs CreateWeaponSelectPanel(Transform parent)
    {
        var refs = new WeaponSelectRefs();
        var panelGO = CreateFullStretchPanel(parent, "Panel_WeaponSelect");
        panelGO.gameObject.AddComponent<Image>().color = new Color(0.95f, 0.85f, 0.75f, 1f);
        panelGO.gameObject.SetActive(false);
        refs.root = panelGO;

        // 제목
        CreateLabel(panelGO, "Txt_Title", "무기",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -80f), new Vector2(0f, 0f), 36);

        // ── 왼쪽: 그리드 스크롤 ──
        var (scrollContent, weaponItemTemplate) = CreateWeaponGridScrollView(panelGO, "WeaponScrollPanel",
            new Vector2(0.02f, 0.1f), new Vector2(0.42f, 0.92f));
        refs.listContent  = scrollContent;
        refs.itemTemplate = weaponItemTemplate;

        // ── 오른쪽: 프리뷰 + 스탯 ──
        var right = CreatePanel(panelGO, "Panel_WeaponPreview",
            new Vector2(0.45f, 0.1f), new Vector2(0.98f, 0.92f));

        refs.previewImg  = CreateChildImage(right, "Img_WeaponIcon",
            new Vector2(0.1f, 0.45f), new Vector2(0.9f, 0.95f));
        refs.previewName = CreateLabel(right, "Txt_WeaponName", "무기 이름",
            new Vector2(0f, 0.35f), new Vector2(1f, 0.44f), Vector2.zero, Vector2.zero, 22);

        refs.atkText      = CreateLabel(right, "Txt_Atk",      "공격력  —",
            new Vector2(0f, 0.25f), new Vector2(1f, 0.34f), Vector2.zero, Vector2.zero, 18);
        refs.atkSpeedText = CreateLabel(right, "Txt_AtkSpeed", "공격속도  —",
            new Vector2(0f, 0.16f), new Vector2(1f, 0.25f), Vector2.zero, Vector2.zero, 18);
        refs.rangeText    = CreateLabel(right, "Txt_Range",    "공격 거리  —",
            new Vector2(0f, 0.07f), new Vector2(1f, 0.16f), Vector2.zero, Vector2.zero, 18);

        // 무기 선택 버튼
        var confirmGO = CreatePanel(panelGO, "Btn_WeaponConfirm",
            new Vector2(0.6f, 0.01f), new Vector2(0.98f, 0.10f));
        confirmGO.gameObject.AddComponent<Image>().color = new Color(0.15f, 0.35f, 0.55f, 1f);
        refs.confirmBtn = confirmGO.gameObject.AddComponent<Button>();
        refs.confirmBtn.interactable = false;
        CreateLabel(confirmGO, "Txt_Confirm", "무기 선택",
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 24);

        return refs;
    }

    // ══════════════════════════════════════════════════════
    // 그리드 스크롤 생성
    // ══════════════════════════════════════════════════════
    private static (Transform content, UI_CharacterSelectItem charTemplate) CreateCharGridScrollView(
        RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        var panel = CreatePanel(parent, name, anchorMin, anchorMax);

        var svGO = new GameObject("ScrollView");
        svGO.transform.SetParent(panel, false);
        var svRT = svGO.AddComponent<RectTransform>();
        svRT.anchorMin = Vector2.zero; svRT.anchorMax = Vector2.one;
        svRT.offsetMin = Vector2.zero; svRT.offsetMax = Vector2.zero;
        var sr = svGO.AddComponent<ScrollRect>();
        sr.horizontal = false; sr.vertical = true;

        var vpGO = new GameObject("Viewport");
        vpGO.transform.SetParent(svGO.transform, false);
        var vpRT = vpGO.AddComponent<RectTransform>();
        vpRT.anchorMin = Vector2.zero; vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin = Vector2.zero; vpRT.offsetMax = Vector2.zero;
        vpGO.AddComponent<Image>().color = new Color(0, 0, 0, 0);
        vpGO.AddComponent<RectMask2D>();
        sr.viewport = vpRT;

        var ctGO = new GameObject("Content");
        ctGO.transform.SetParent(vpGO.transform, false);
        var ctRT = ctGO.AddComponent<RectTransform>();
        ctRT.anchorMin = new Vector2(0f, 1f); ctRT.anchorMax = new Vector2(1f, 1f);
        ctRT.pivot = new Vector2(0.5f, 1f);
        ctRT.offsetMin = Vector2.zero; ctRT.offsetMax = Vector2.zero;

        var glg = ctGO.AddComponent<GridLayoutGroup>();
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = 3;
        glg.cellSize = new Vector2(90f, 90f);
        glg.spacing = new Vector2(8f, 8f);
        glg.padding = new RectOffset(8, 8, 8, 8);

        var csf = ctGO.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sr.content = ctRT;

        // ItemTemplate
        var tmplGO = new GameObject("ItemTemplate");
        tmplGO.transform.SetParent(ctGO.transform, false);
        tmplGO.SetActive(false);
        tmplGO.AddComponent<RectTransform>().sizeDelta = new Vector2(90f, 90f);

        var charTmpl = tmplGO.AddComponent<UI_CharacterSelectItem>();
        var btn  = tmplGO.AddComponent<Button>();
        tmplGO.AddComponent<Image>().color = new Color(0.25f, 0.45f, 0.65f, 1f);

        var portGO  = new GameObject("Img_Portrait");
        portGO.transform.SetParent(tmplGO.transform, false);
        var portRT  = portGO.AddComponent<RectTransform>();
        portRT.anchorMin = new Vector2(0f, 0.35f); portRT.anchorMax = Vector2.one;
        portRT.offsetMin = new Vector2(4f, 0f);    portRT.offsetMax = new Vector2(-4f, -4f);
        var portImg = portGO.AddComponent<Image>();

        var nameTxt = CreateLabel(tmplGO.GetComponent<RectTransform>(), "Txt_Name", "—",
            new Vector2(0f, 0f), new Vector2(1f, 0.34f), Vector2.zero, Vector2.zero, 16);

        var selGO = new GameObject("Img_Selected");
        selGO.transform.SetParent(tmplGO.transform, false);
        selGO.SetActive(false);
        var selRT = selGO.AddComponent<RectTransform>();
        selRT.anchorMin = Vector2.zero; selRT.anchorMax = Vector2.one;
        selRT.offsetMin = Vector2.zero; selRT.offsetMax = Vector2.zero;
        selGO.AddComponent<Image>().color = new Color(1f, 0.9f, 0f, 0.4f);

        var tmplSO = new SerializedObject(charTmpl);
        tmplSO.FindProperty("portrait").objectReferenceValue      = portImg;
        tmplSO.FindProperty("nameText").objectReferenceValue      = nameTxt;
        tmplSO.FindProperty("selectedMark").objectReferenceValue  = selGO;
        tmplSO.FindProperty("button").objectReferenceValue        = btn;
        tmplSO.ApplyModifiedProperties();

        return (ctGO.transform, charTmpl);
    }

    private static (Transform content, UI_WeaponSelectItem weaponTemplate) CreateWeaponGridScrollView(
        RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        // 무기용: isChar=false인 경우 동일 구조로 UI_WeaponSelectItem 반환
        var panel = CreatePanel(parent, name, anchorMin, anchorMax);

        var svGO = new GameObject("ScrollView");
        svGO.transform.SetParent(panel, false);
        var svRT = svGO.AddComponent<RectTransform>();
        svRT.anchorMin = Vector2.zero; svRT.anchorMax = Vector2.one;
        svRT.offsetMin = Vector2.zero; svRT.offsetMax = Vector2.zero;
        var sr = svGO.AddComponent<ScrollRect>();
        sr.horizontal = false; sr.vertical = true;

        var vpGO = new GameObject("Viewport");
        vpGO.transform.SetParent(svGO.transform, false);
        var vpRT = vpGO.AddComponent<RectTransform>();
        vpRT.anchorMin = Vector2.zero; vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin = Vector2.zero; vpRT.offsetMax = Vector2.zero;
        vpGO.AddComponent<Image>().color = new Color(0, 0, 0, 0);
        vpGO.AddComponent<RectMask2D>();
        sr.viewport = vpRT;

        var ctGO = new GameObject("Content");
        ctGO.transform.SetParent(vpGO.transform, false);
        var ctRT = ctGO.AddComponent<RectTransform>();
        ctRT.anchorMin = new Vector2(0f, 1f); ctRT.anchorMax = new Vector2(1f, 1f);
        ctRT.pivot = new Vector2(0.5f, 1f);
        ctRT.offsetMin = Vector2.zero; ctRT.offsetMax = Vector2.zero;

        var glg = ctGO.AddComponent<GridLayoutGroup>();
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = 3;
        glg.cellSize = new Vector2(90f, 90f);
        glg.spacing = new Vector2(8f, 8f);
        glg.padding = new RectOffset(8, 8, 8, 8);

        var csf = ctGO.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sr.content = ctRT;

        var tmplGO = new GameObject("ItemTemplate");
        tmplGO.transform.SetParent(ctGO.transform, false);
        tmplGO.SetActive(false);
        tmplGO.AddComponent<RectTransform>().sizeDelta = new Vector2(90f, 90f);

        var weaponTmpl = tmplGO.AddComponent<UI_WeaponSelectItem>();
        var wBtn = tmplGO.AddComponent<Button>();
        tmplGO.AddComponent<Image>().color = new Color(0.25f, 0.45f, 0.65f, 1f);

        var iconGO = new GameObject("Img_Icon");
        iconGO.transform.SetParent(tmplGO.transform, false);
        var iconRT = iconGO.AddComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0f, 0.35f); iconRT.anchorMax = Vector2.one;
        iconRT.offsetMin = new Vector2(4f, 0f);    iconRT.offsetMax = new Vector2(-4f, -4f);
        var iconImg = iconGO.AddComponent<Image>();

        var wNameTxt = CreateLabel(tmplGO.GetComponent<RectTransform>(), "Txt_Name", "—",
            new Vector2(0f, 0f), new Vector2(1f, 0.34f), Vector2.zero, Vector2.zero, 16);

        var wSelGO = new GameObject("Img_Selected");
        wSelGO.transform.SetParent(tmplGO.transform, false);
        wSelGO.SetActive(false);
        var wSelRT = wSelGO.AddComponent<RectTransform>();
        wSelRT.anchorMin = Vector2.zero; wSelRT.anchorMax = Vector2.one;
        wSelRT.offsetMin = Vector2.zero; wSelRT.offsetMax = Vector2.zero;
        wSelGO.AddComponent<Image>().color = new Color(1f, 0.9f, 0f, 0.4f);

        var wTmplSO = new SerializedObject(weaponTmpl);
        wTmplSO.FindProperty("iconImage").objectReferenceValue    = iconImg;
        wTmplSO.FindProperty("nameText").objectReferenceValue     = wNameTxt;
        wTmplSO.FindProperty("selectedMark").objectReferenceValue = wSelGO;
        wTmplSO.FindProperty("button").objectReferenceValue       = wBtn;
        wTmplSO.ApplyModifiedProperties();

        return (ctGO.transform, weaponTmpl);
    }

    // ══════════════════════════════════════════════════════
    // 공통 헬퍼
    // ══════════════════════════════════════════════════════

    private static void ClearChildren(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(t.GetChild(i).gameObject);
    }

    private static RectTransform CreateFullStretchPanel(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static RectTransform CreatePanel(RectTransform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static Image CreateChildImage(RectTransform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return go.AddComponent<Image>();
    }

    private static TMP_Text CreateLabel(RectTransform parent, string name, string text,
        Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax, float fontSize)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        if (_font != null) tmp.font = _font;
        return tmp;
    }

    private static (Slider slider, TMP_Text label) CreateSliderRow(RectTransform parent,
        string name, string labelText, Vector2 anchorMin, Vector2 anchorMax)
    {
        var rowGO = new GameObject(name);
        rowGO.transform.SetParent(parent, false);
        var rowRT = rowGO.AddComponent<RectTransform>();
        rowRT.anchorMin = anchorMin; rowRT.anchorMax = anchorMax;
        rowRT.offsetMin = new Vector2(4f, 0f); rowRT.offsetMax = new Vector2(-4f, 0f);

        var lbl = CreateLabel(rowRT, "Lbl", labelText,
            new Vector2(0f, 0f), new Vector2(0.35f, 1f), Vector2.zero, Vector2.zero, 18);

        var sliderGO = new GameObject("Slider");
        sliderGO.transform.SetParent(rowGO.transform, false);
        var sliderRT = sliderGO.AddComponent<RectTransform>();
        sliderRT.anchorMin = new Vector2(0.36f, 0.15f);
        sliderRT.anchorMax = new Vector2(0.85f, 0.85f);
        sliderRT.offsetMin = Vector2.zero; sliderRT.offsetMax = Vector2.zero;

        var bgGO = new GameObject("Background");
        bgGO.transform.SetParent(sliderGO.transform, false);
        var bgRT = bgGO.AddComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0f, 0.25f); bgRT.anchorMax = new Vector2(1f, 0.75f);
        bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
        bgGO.AddComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f, 1f);

        var fillAreaGO = new GameObject("Fill Area");
        fillAreaGO.transform.SetParent(sliderGO.transform, false);
        var fillAreaRT = fillAreaGO.AddComponent<RectTransform>();
        fillAreaRT.anchorMin = new Vector2(0f, 0.25f); fillAreaRT.anchorMax = new Vector2(1f, 0.75f);
        fillAreaRT.offsetMin = new Vector2(0f, 0f); fillAreaRT.offsetMax = new Vector2(-5f, 0f);
        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(fillAreaGO.transform, false);
        var fillRT = fillGO.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = Vector2.zero; fillRT.offsetMax = new Vector2(5f, 0f);
        var fillImg = fillGO.AddComponent<Image>();
        fillImg.color = new Color(0.2f, 0.6f, 1f, 1f);

        var slider = sliderGO.AddComponent<Slider>();
        slider.fillRect   = fillRT;
        slider.direction  = Slider.Direction.LeftToRight;
        slider.minValue   = 0f;
        slider.maxValue   = 1f;
        slider.value      = 0f;

        var valTxt = CreateLabel(rowRT, "Txt_Val", "0",
            new Vector2(0.86f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero, 18);

        return (slider, valTxt);
    }
}
