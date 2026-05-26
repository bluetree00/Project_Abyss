#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// LobbyRoot 프리팹을 수정하는 에디터 유틸리티.
/// Step 1: 불필요한 오브젝트 제거 + 레이아웃 변경 + 이미지 교체 (저장 포함)
/// Step 2: LobbyButtonHover 컴포넌트 추가 (별도 메뉴 아이템)
/// </summary>
public static class LobbyPrefabEditor
{
    private const string PrefabPath = "Assets/RelicFairy/UI/Scene/ScenePrefabs/LobbyRoot.prefab";

    private const string StartOffPath   = "Assets/RelicFairy/UI/AI Resources/Start off.png";
    private const string StartOnPath    = "Assets/RelicFairy/UI/AI Resources/Start on.png";
    private const string SettingOffPath = "Assets/RelicFairy/UI/AI Resources/Setting off.png";
    private const string SettingOnPath  = "Assets/RelicFairy/UI/AI Resources/Setting on.png";
    private const string ExitOffPath    = "Assets/RelicFairy/UI/AI Resources/Exit off.png";
    private const string ExitOnPath     = "Assets/RelicFairy/UI/AI Resources/Exit on.png";

    // ── Step 1: 구조 정리 + 이미지 교체 ────────────────────────────────────
    [MenuItem("RelicFairy/UI/Setup Lobby (Step1 - Layout)")]
    public static void SetupLobbyLayout()
    {
        Sprite startOff  = LoadSprite(StartOffPath);
        Sprite settOff   = LoadSprite(SettingOffPath);
        Sprite exitOff   = LoadSprite(ExitOffPath);

        if (startOff == null || settOff == null || exitOff == null)
        {
            Debug.LogError("[LobbyPrefabEditor] 스프라이트 로드 실패.");
            return;
        }

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError("[LobbyPrefabEditor] 프리팹 로드 실패: " + PrefabPath);
            return;
        }

        try
        {
            // 1. Btn_Continue 삭제
            DestroyChild(prefabRoot.transform, "MenuPanel/Btn_Continue", "Btn_Continue 삭제");

            // 2. MenuTitleBG 삭제
            DestroyChild(prefabRoot.transform, "MenuPanel/MenuTitleBG", "MenuTitleBG 삭제");

            // 3. LeftPanel 삭제
            DestroyChild(prefabRoot.transform, "LeftPanel", "LeftPanel 삭제");

            // 4. MenuPanel 레이아웃 재설정
            Transform menuPanel = prefabRoot.transform.Find("MenuPanel");
            if (menuPanel == null)
            {
                Debug.LogError("[LobbyPrefabEditor] MenuPanel 없음");
                return;
            }

            RectTransform menuRect = menuPanel.GetComponent<RectTransform>();
            menuRect.anchorMin        = Vector2.zero;
            menuRect.anchorMax        = Vector2.zero;
            menuRect.pivot            = Vector2.zero;
            menuRect.anchoredPosition = new Vector2(40f, 40f);
            menuRect.sizeDelta        = new Vector2(220f, 300f);

            // 5. MenuBoardBG 삭제
            DestroyChild(menuPanel, "MenuBoardBG", "MenuBoardBG 삭제");

            // 6. 버튼 위치 + off 이미지 교체
            SetupButtonLayout(menuPanel, "Btn_StartRun", startOff, new Vector2(110f, 250f), new Vector2(200f, 80f));
            SetupButtonLayout(menuPanel, "Btn_Settings", settOff,  new Vector2(110f, 160f), new Vector2(200f, 80f));
            SetupButtonLayout(menuPanel, "Btn_Exit",     exitOff,  new Vector2(110f,  70f), new Vector2(200f, 80f));

            // missing script 제거 후 저장
            RemoveMissingScripts(prefabRoot);
            bool saved = PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            Debug.Log(saved
                ? "[LobbyPrefabEditor] Step1 완료 — LobbyRoot 저장됨"
                : "[LobbyPrefabEditor] Step1 경고 — SaveAsPrefabAsset 반환값 false");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    // missing MonoBehaviour 컴포넌트를 재귀적으로 제거
    private static void RemoveMissingScripts(GameObject root)
    {
        int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
        if (removed > 0)
            Debug.Log($"[LobbyPrefabEditor] {root.name}에서 missing script {removed}개 제거");
        foreach (Transform child in root.transform)
            RemoveMissingScripts(child.gameObject);
    }

    // ── Step 2: LobbyButtonHover 컴포넌트 추가 ──────────────────────────────
    [MenuItem("RelicFairy/UI/Setup Lobby (Step2 - Hover)")]
    public static void SetupLobbyHover()
    {
        System.Type hoverType = System.Type.GetType("LobbyButtonHover, Assembly-CSharp");
        if (hoverType == null)
        {
            Debug.LogError("[LobbyPrefabEditor] LobbyButtonHover 타입 없음. 컴파일 완료 후 재실행하세요.");
            return;
        }

        Sprite startOff  = LoadSprite(StartOffPath);
        Sprite startOn   = LoadSprite(StartOnPath);
        Sprite settOff   = LoadSprite(SettingOffPath);
        Sprite settOn    = LoadSprite(SettingOnPath);
        Sprite exitOff   = LoadSprite(ExitOffPath);
        Sprite exitOn    = LoadSprite(ExitOnPath);

        if (startOff == null || startOn == null || settOff == null ||
            settOn   == null || exitOff == null || exitOn  == null)
        {
            Debug.LogError("[LobbyPrefabEditor] 스프라이트 로드 실패.");
            return;
        }

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError("[LobbyPrefabEditor] 프리팹 로드 실패");
            return;
        }

        try
        {
            Transform menuPanel = prefabRoot.transform.Find("MenuPanel");
            if (menuPanel == null)
            {
                Debug.LogError("[LobbyPrefabEditor] MenuPanel 없음");
                return;
            }

            AddHover(menuPanel, "Btn_StartRun", hoverType, startOff, startOn);
            AddHover(menuPanel, "Btn_Settings", hoverType, settOff,  settOn);
            AddHover(menuPanel, "Btn_Exit",     hoverType, exitOff,  exitOn);

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            Debug.Log("[LobbyPrefabEditor] Step2 완료 — Hover 컴포넌트 추가됨");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // 내부 헬퍼
    // ────────────────────────────────────────────────────────────────────────

    private static void DestroyChild(Transform root, string path, string logLabel)
    {
        Transform tr = root.Find(path);
        if (tr != null)
        {
            Object.DestroyImmediate(tr.gameObject);
            Debug.Log($"[LobbyPrefabEditor] {logLabel} 완료");
        }
    }

    private static void SetupButtonLayout(Transform parent, string btnName, Sprite offSprite,
        Vector2 anchoredPos, Vector2 sizeDelta)
    {
        Transform btnTr = parent.Find(btnName);
        if (btnTr == null) { Debug.LogWarning("[LobbyPrefabEditor] 버튼 없음: " + btnName); return; }

        RectTransform rect = btnTr.GetComponent<RectTransform>();
        rect.anchorMin        = Vector2.zero;
        rect.anchorMax        = Vector2.zero;
        rect.pivot            = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta        = sizeDelta;

        Image img = btnTr.GetComponent<Image>();
        if (img != null)
        {
            img.sprite   = offSprite;
            img.type     = Image.Type.Simple;
            img.preserveAspect = true;
            rect.sizeDelta = sizeDelta; // SetNativeSize 방지
        }

        Debug.Log($"[LobbyPrefabEditor] {btnName} 레이아웃 설정 완료");
    }

    private static void AddHover(Transform parent, string btnName,
        System.Type hoverType, Sprite offSprite, Sprite onSprite)
    {
        Transform btnTr = parent.Find(btnName);
        if (btnTr == null) { Debug.LogWarning("[LobbyPrefabEditor] 버튼 없음: " + btnName); return; }

        Component hover = btnTr.GetComponent(hoverType) ?? btnTr.gameObject.AddComponent(hoverType);

        SerializedObject so = new SerializedObject(hover);
        so.FindProperty("_offSprite").objectReferenceValue = offSprite;
        so.FindProperty("_onSprite").objectReferenceValue  = onSprite;
        so.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log($"[LobbyPrefabEditor] {btnName} Hover 추가 완료");
    }

    private static Sprite LoadSprite(string path)
    {
        // TextureImporter를 통해 강제로 Sprite 타입으로 설정 후 임포트
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null && importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        Sprite sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sp == null)
            Debug.LogWarning("[LobbyPrefabEditor] 스프라이트 없음: " + path);
        return sp;
    }
}
#endif
