// Editor-only: 로그라이크 로비 UI 자동 구성 도구
// Tools/Abyss/Build Lobby UI 메뉴로 실행
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class LobbyUIBuilder
{
    const string PREFAB_PATH = "Assets/Abyss/UI/Menu/MenuPrefabs/LobbyRoot.prefab";

    // ─── Bamao Sprite GUIDs ───────────────────────────────────────────────
    // MENU
    const string G_MENU_BG        = "2a05102aadcce0547a80c71ae23e8bcb";  // Menu_bg.png
    const string G_MENU_BG2       = "b534effd95649274795afe9f76b4dbce";  // Menu_bg2.png
    const string G_LOGO           = "8850841a7b732b346b1d092286f72bdf";  // logo.png
    const string G_SYMBOL         = "00ec36fb7f7512b448ca5d173cfc8fa6";  // symbol.png
    const string G_WORD_START     = "4ef2cb3afcd369b4ca9e890179563f87";  // Word_start.png
    const string G_WORD_CONTINUE  = "eac6be7d0c2f7914aaace7303928818c";  // Word_continue.png
    const string G_WORD_SETTING   = "315b1139867f5da45a32b3df3674bb9c";  // Word_Setting.png
    const string G_WORD_EXIT      = "6e589b3ec3cb5884fb314eea506f17e0";  // Word_EXIT.png
    // Panel
    const string G_DES_BOARD      = "7d5b95d4c3584d547a0505ba318f6c75";  // Des_board.png
    const string G_DES_FRAME      = "42a4d040318cdd8459c2690a11221aa4";  // Des_frame.png
    const string G_DES_TITLE      = "366d844361a4b994db8ef71c464470d3";  // Des_title.png
    const string G_DES_DIALOG     = "f0eaea1f2efece446b916a2f9fa69ac5";  // Des_dialog.png
    // Character sheet
    const string G_BULLETIN_BG    = "7d5b95d4c3584d547a0505ba318f6c75";  // reuse board
    const string G_CHAR_BG        = "38f9e3e09e2e0974198a5c8efba3e66d";  // bulletin background (will fallback)
    const string G_CHAR_GROUND    = "4d0a9b9614df74046acb80ef3eff5c74";  // character_ground
    const string G_ICON_COIN      = "8f2dea6b54264b240a3c8f4acff3b8a8";  // Icon_coin
    const string G_ICON_MANA      = "28f4a7b13a6e79744b5a29a36b25c10e";  // Object_mana
    // Buttons
    const string G_CLAIM_GREEN    = "d46103444c1a5db439680e0b3465acdd";  // claim green
    const string G_CLAIM_RED      = "1400701d3a35cb045ab73a8da00f3a1e";  // claim red
    const string G_BTN_DARK       = "36340a93604eba346bfd17184d478bd8";  // button short dark
    // Font
    const string G_FONT_SDF       = "7e3c63f00b766644d9aa34e1de75307c";  // Magical Neverland SDF

    [MenuItem("Tools/Abyss/Build Lobby UI")]
    public static void BuildLobby()
    {
        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
        if (prefabAsset == null)
        {
            Debug.LogError($"[LobbyUIBuilder] 프리팹 없음: {PREFAB_PATH}");
            return;
        }

        // Prefab Contents 열기
        var root = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
        if (root == null)
        {
            Debug.LogError("[LobbyUIBuilder] 프리팹 로드 실패");
            return;
        }

        try
        {
            BuildLobbyContents(root);
            PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
            Debug.Log("[LobbyUIBuilder] ✅ 로비 UI 빌드 완료!");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LobbyUIBuilder] 빌드 실패: {e}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.Refresh();
    }

    static void BuildLobbyContents(GameObject root)
    {
        // ─── 기존 자식 전부 제거 후 재구성 ────────────────────────────────
        for (int i = root.transform.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

        // ─── Canvas Scaler 설정 (1920×1080 ScaleWithScreenSize) ─────────
        var scaler = root.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // LAYER 0 : 배경
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        var bg = CreateImage(root.transform, "BG",
            LoadSprite(G_MENU_BG),
            new Color(1, 1, 1, 1),
            anchorMin: Vector2.zero, anchorMax: Vector2.one,
            offsetMin: Vector2.zero, offsetMax: Vector2.zero,
            raycast: false);
        bg.type = Image.Type.Simple;
        bg.preserveAspect = false;

        // 어두운 오버레이 – 로그라이크 분위기
        var overlay = CreateImage(root.transform, "DarkOverlay",
            null,
            new Color(0.04f, 0.02f, 0.08f, 0.62f),
            anchorMin: Vector2.zero, anchorMax: Vector2.one,
            offsetMin: Vector2.zero, offsetMax: Vector2.zero,
            raycast: false);

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // LAYER 1 : 왼쪽 캐릭터 패널
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        var leftPanel = CreateEmpty(root.transform, "LeftPanel",
            anchorMin: new Vector2(0, 0), anchorMax: new Vector2(0, 1),
            pivot: new Vector2(0, 0.5f),
            offsetMin: Vector2.zero, offsetMax: new Vector2(520, 0));

        // 왼쪽 프레임 배경 (Des_frame 슬라이스)
        var leftFrameBG = CreateImage(leftPanel.transform, "LeftFrameBG",
            LoadSprite(G_DES_FRAME),
            new Color(1, 1, 1, 0.92f),
            anchorMin: Vector2.zero, anchorMax: Vector2.one,
            offsetMin: new Vector2(20, 30), offsetMax: new Vector2(-20, -30),
            raycast: false);
        leftFrameBG.type = Image.Type.Sliced;

        // 게임 제목 타이틀 배경
        var titleBG = CreateImage(leftPanel.transform, "TitleBG",
            LoadSprite(G_DES_TITLE),
            new Color(1, 1, 1, 1),
            anchorMin: new Vector2(0, 1), anchorMax: new Vector2(1, 1),
            pivot: new Vector2(0.5f, 1),
            offsetMin: new Vector2(40, -180), offsetMax: new Vector2(-40, -60));
        titleBG.type = Image.Type.Sliced;

        // 제목 텍스트
        CreateTMPText(leftPanel.transform, "TitleText",
            "PROJECT  ABYSS",
            anchorMin: new Vector2(0, 1), anchorMax: new Vector2(1, 1),
            offsetMin: new Vector2(40, -175), offsetMax: new Vector2(-40, -75),
            fontSize: 34f, bold: true,
            color: new Color(0.18f, 0.08f, 0.02f));

        // 로고 이미지 (심볼)
        CreateImage(leftPanel.transform, "Logo",
            LoadSprite(G_LOGO),
            Color.white,
            anchorMin: new Vector2(0.5f, 1), anchorMax: new Vector2(0.5f, 1),
            pivot: new Vector2(0.5f, 1),
            offsetMin: new Vector2(-120, -350), offsetMax: new Vector2(120, -200),
            raycast: false);

        // 캐릭터 표시 영역
        var charDisplayBG = CreateImage(leftPanel.transform, "CharDisplayBG",
            LoadSprite(G_DES_DIALOG),
            new Color(0.9f, 0.85f, 0.75f, 0.9f),
            anchorMin: new Vector2(0.1f, 0.25f), anchorMax: new Vector2(0.9f, 0.78f),
            offsetMin: Vector2.zero, offsetMax: Vector2.zero,
            raycast: false);
        charDisplayBG.type = Image.Type.Sliced;

        // 심볼 장식 (중앙 하단)
        CreateImage(leftPanel.transform, "Symbol",
            LoadSprite(G_SYMBOL),
            new Color(1, 1, 1, 0.7f),
            anchorMin: new Vector2(0.5f, 0), anchorMax: new Vector2(0.5f, 0),
            pivot: new Vector2(0.5f, 0),
            offsetMin: new Vector2(-60, 40), offsetMax: new Vector2(60, 160),
            raycast: false);

        // 아이콘 행 (코인, 마나)
        var statsRow = CreateEmpty(leftPanel.transform, "StatsRow",
            anchorMin: new Vector2(0, 0), anchorMax: new Vector2(1, 0),
            pivot: new Vector2(0.5f, 0),
            offsetMin: new Vector2(30, 170), offsetMax: new Vector2(-30, 220));

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // LAYER 2 : 오른쪽 메뉴 패널
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        var menuPanel = CreateEmpty(root.transform, "MenuPanel",
            anchorMin: new Vector2(1, 0.5f), anchorMax: new Vector2(1, 0.5f),
            pivot: new Vector2(1, 0.5f),
            offsetMin: new Vector2(-520, -320), offsetMax: new Vector2(-40, 320));

        // 메뉴 배경 보드
        var menuBG = CreateImage(menuPanel.transform, "MenuBG",
            LoadSprite(G_DES_BOARD),
            new Color(1, 1, 1, 0.9f),
            anchorMin: Vector2.zero, anchorMax: Vector2.one,
            offsetMin: Vector2.zero, offsetMax: Vector2.zero,
            raycast: false);
        menuBG.type = Image.Type.Sliced;

        // 메뉴 타이틀
        var menuTitleBG = CreateImage(menuPanel.transform, "MenuTitleBG",
            LoadSprite(G_DES_TITLE),
            Color.white,
            anchorMin: new Vector2(0.5f, 1), anchorMax: new Vector2(0.5f, 1),
            pivot: new Vector2(0.5f, 1),
            offsetMin: new Vector2(-200, -85), offsetMax: new Vector2(200, -10));
        menuTitleBG.type = Image.Type.Sliced;

        CreateTMPText(menuPanel.transform, "MenuTitleText",
            "ABYSS",
            anchorMin: new Vector2(0.5f, 1), anchorMax: new Vector2(0.5f, 1),
            offsetMin: new Vector2(-200, -80), offsetMax: new Vector2(200, -15),
            fontSize: 28f, bold: true,
            color: new Color(0.15f, 0.06f, 0.02f));

        // 구분선
        var divider = CreateImage(menuPanel.transform, "Divider",
            null,
            new Color(0.35f, 0.2f, 0.1f, 0.5f),
            anchorMin: new Vector2(0.1f, 1), anchorMax: new Vector2(0.9f, 1),
            offsetMin: new Vector2(0, -95), offsetMax: new Vector2(0, -88));

        // ─── 버튼 4개 ──────────────────────────────────────────────────────
        float btnW = 340f;
        float btnH = 78f;
        float btnX = 0f;
        float[] btnYs = { 100f, 0f, -100f, -200f };
        string[] btnNames    = { "Btn_StartRun",  "Btn_Continue",  "Btn_Settings",  "Btn_Exit" };
        string[] btnSprites  = { G_WORD_START,    G_WORD_CONTINUE, G_WORD_SETTING,  G_WORD_EXIT };
        // hover tint colors for each button
        Color[] btnNormal = {
            new Color(1f, 1f, 1f, 1f),
            new Color(1f, 1f, 1f, 1f),
            new Color(1f, 1f, 1f, 1f),
            new Color(1f, 1f, 1f, 1f),
        };

        for (int i = 0; i < 4; i++)
        {
            var btnGO = CreateImage(menuPanel.transform, btnNames[i],
                LoadSprite(btnSprites[i]),
                btnNormal[i],
                anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f),
                offsetMin: new Vector2(btnX - btnW * 0.5f, btnYs[i] - btnH * 0.5f),
                offsetMax: new Vector2(btnX + btnW * 0.5f, btnYs[i] + btnH * 0.5f),
                raycast: true);
            btnGO.type = Image.Type.Simple;
            btnGO.preserveAspect = true;

            var btn = btnGO.gameObject.AddComponent<Button>();
            btn.targetGraphic = btnGO;

            var colors = btn.colors;
            colors.normalColor      = Color.white;
            colors.highlightedColor = new Color(1f, 0.92f, 0.6f, 1f);  // 황금빛 하이라이트
            colors.pressedColor     = new Color(0.7f, 0.55f, 0.2f, 1f);
            colors.fadeDuration     = 0.08f;
            btn.colors = colors;
            btn.transition = Selectable.Transition.ColorTint;
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // LAYER 3 : 장식 요소
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // 우상단 심볼 장식
        CreateImage(root.transform, "Deco_SymbolTop",
            LoadSprite(G_SYMBOL),
            new Color(1, 0.9f, 0.5f, 0.18f),
            anchorMin: new Vector2(1, 1), anchorMax: new Vector2(1, 1),
            pivot: new Vector2(1, 1),
            offsetMin: new Vector2(-240, -240), offsetMax: new Vector2(-40, -40),
            raycast: false);

        // 좌하단 로고 장식
        CreateImage(root.transform, "Deco_LogoBot",
            LoadSprite(G_LOGO),
            new Color(1, 1, 1, 0.06f),
            anchorMin: new Vector2(0, 0), anchorMax: new Vector2(0, 0),
            pivot: new Vector2(0, 0),
            offsetMin: new Vector2(10, 10), offsetMax: new Vector2(310, 160),
            raycast: false);

        Debug.Log("[LobbyUIBuilder] 계층 구성 완료");
    }

    // ─── Helper: Image 오브젝트 생성 ─────────────────────────────────────
    static Image CreateImage(Transform parent, string name, Sprite sprite, Color color,
        Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax,
        Vector2? pivot = null, bool raycast = true)
    {
        var go = new GameObject(name);
        go.layer = 5; // UI
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;

        var img = go.AddComponent<Image>();
        if (sprite != null) img.sprite = sprite;
        img.color = color;
        img.raycastTarget = raycast;
        return img;
    }

    // ─── Helper: Empty RectTransform ─────────────────────────────────────
    static RectTransform CreateEmpty(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax,
        Vector2? pivot = null)
    {
        var go = new GameObject(name);
        go.layer = 5;
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        return rt;
    }

    // ─── Helper: TextMeshProUGUI ──────────────────────────────────────────
    static TextMeshProUGUI CreateTMPText(Transform parent, string name, string text,
        Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax,
        float fontSize, bool bold, Color color,
        Vector2? pivot = null)
    {
        var go = new GameObject(name);
        go.layer = 5;
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        // Magical Neverland 폰트 로드 시도
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            AssetDatabase.GUIDToAssetPath(G_FONT_SDF));
        if (font != null) tmp.font = font;

        return tmp;
    }

    // ─── Helper: Sprite 로드 ─────────────────────────────────────────────
    static Sprite LoadSprite(string guid)
    {
        var path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path)) return null;
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
#endif
