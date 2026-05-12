using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI_DialoguePopup 프리팹을 에디터에서 생성하는 일회성 툴.
/// 생성 후 이 스크립트는 삭제해도 된다.
/// </summary>
public static class CreateDialoguePopupPrefab
{
    [MenuItem("Abyss/Dialogue/Register Illustrations")]
    public static void RegisterIllustrations()
    {
        RegisterAddressable("Assets/Abyss/UI/Popup/Quest/1RwGb.png", "Illust_God_Default");
        RegisterAddressable("Assets/Abyss/UI/Popup/Quest/dWhY5.png", "Illust_Shadow_Default");
        Debug.Log("[Dialogue] 일러스트 Addressables 등록 완료 — Illust_God_Default / Illust_Shadow_Default");
    }

    [MenuItem("Abyss/Dialogue/Bind Prefab References")]
    public static void BindDialoguePopupRefs()
    {
        const string prefabPath = "Assets/Abyss/UI/Popup/Dialogue/UI_DialoguePopup.prefab";

        var prefabContents = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var popup = prefabContents.GetComponent<UI_DialoguePopup>();
            if (popup == null) { Debug.LogError("[BindRefs] UI_DialoguePopup 컴포넌트 없음"); return; }

            var portrait     = prefabContents.transform.Find("Portrait")?.GetComponent<Image>();
            var textBox      = prefabContents.transform.Find("TextBox");
            var speakerName  = textBox?.Find("SpeakerName")?.GetComponent<TextMeshProUGUI>();
            var bodyText     = textBox?.Find("BodyText")?.GetComponent<TextMeshProUGUI>();
            var advanceBtn   = prefabContents.transform.Find("AdvanceButton")?.GetComponent<Button>();

            var so = new SerializedObject(popup);
            so.FindProperty("portrait").objectReferenceValue        = portrait;
            so.FindProperty("speakerNameText").objectReferenceValue = speakerName;
            so.FindProperty("bodyText").objectReferenceValue        = bodyText;
            so.FindProperty("advanceButton").objectReferenceValue   = advanceBtn;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabPath);
            Debug.Log("[BindRefs] UI_DialoguePopup 참조 바인딩 완료");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabContents);
        }
    }

    [MenuItem("Abyss/Create/Register Dialogue CSV")]
    public static void RegisterDialogueCsv()
    {
        const string csvPath = "Assets/Abyss/Systems/Dialogue/Data/DIALOGUE_DATA.csv";
        RegisterAddressable(csvPath, "DIALOGUE_DATA");
        Debug.Log("[CreateDialoguePopupPrefab] DIALOGUE_DATA CSV Addressables 등록 완료");
    }

    [MenuItem("Abyss/Create/UI_DialoguePopup Prefab")]
    public static void Create()
    {
        // ── Root ────────────────────────────────────────────────────
        var root = new GameObject("UI_DialoguePopup");
        root.layer = LayerMask.NameToLayer("UI");
        var rootRT = root.AddComponent<RectTransform>();
        Stretch(rootRT);
        root.AddComponent<UI_DialoguePopup>();

        // ── Background (반투명 검정 오버레이) ───────────────────────
        var bg = MakeImage(root.transform, "Background");
        bg.color = new Color(0f, 0f, 0f, 0.72f);
        Stretch(bg.rectTransform);

        // ── Portrait Left — 신(God) ─────────────────────────────────
        var godSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
            "Assets/Abyss/UI/Popup/Quest/1RwGb.png");
        var godImg = MakeImage(root.transform, "PortraitLeft");
        godImg.sprite = godSprite;
        godImg.preserveAspect = true;
        godImg.raycastTarget = false;
        SetRect(godImg.rectTransform, new Vector2(0, 0), new Vector2(0, 0),
            new Vector2(0, 0), new Vector2(30, 0), new Vector2(420, 630));

        // ── Portrait Right — 심연(Abyss) ───────────────────────────
        var abyssSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
            "Assets/Abyss/UI/Popup/Quest/dWhY5.png");
        var abyssImg = MakeImage(root.transform, "PortraitRight");
        abyssImg.sprite = abyssSprite;
        abyssImg.preserveAspect = true;
        abyssImg.raycastTarget = false;
        SetRect(abyssImg.rectTransform, new Vector2(1, 0), new Vector2(1, 0),
            new Vector2(1, 0), new Vector2(-30, 0), new Vector2(420, 630));

        // ── TextBox Panel ───────────────────────────────────────────
        var tbSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
            "Assets/Abyss/UI/Popup/Quest/BdyUj.png");
        var tbGO = new GameObject("TextBox");
        tbGO.layer = LayerMask.NameToLayer("UI");
        tbGO.transform.SetParent(root.transform, false);
        tbGO.AddComponent<CanvasRenderer>();
        var tbImg = tbGO.AddComponent<Image>();
        tbImg.sprite = tbSprite;
        tbImg.type = Image.Type.Sliced;
        tbImg.raycastTarget = false;
        var tbRT = tbGO.GetComponent<RectTransform>();
        tbRT.anchorMin = new Vector2(0f, 0f);
        tbRT.anchorMax = new Vector2(1f, 0f);
        tbRT.pivot     = new Vector2(0.5f, 0f);
        tbRT.anchoredPosition = new Vector2(0, 0);
        tbRT.sizeDelta        = new Vector2(0, 370);

        // ── SpeakerName ─────────────────────────────────────────────
        var nameGO = new GameObject("SpeakerName");
        nameGO.layer = LayerMask.NameToLayer("UI");
        nameGO.transform.SetParent(tbGO.transform, false);
        nameGO.AddComponent<CanvasRenderer>();
        var nameTMP = nameGO.AddComponent<TextMeshProUGUI>();
        nameTMP.text = "???";
        nameTMP.fontSize = 30;
        nameTMP.fontStyle = FontStyles.Bold;
        nameTMP.color = new Color(1f, 0.92f, 0.7f);
        nameTMP.alignment = TextAlignmentOptions.Left;
        nameTMP.raycastTarget = false;
        var nameRT = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0.08f, 0.72f);
        nameRT.anchorMax = new Vector2(0.55f, 1.00f);
        nameRT.pivot = new Vector2(0, 1);
        nameRT.anchoredPosition = new Vector2(0, -18);
        nameRT.sizeDelta = Vector2.zero;

        // ── BodyText ─────────────────────────────────────────────────
        var bodyGO = new GameObject("BodyText");
        bodyGO.layer = LayerMask.NameToLayer("UI");
        bodyGO.transform.SetParent(tbGO.transform, false);
        bodyGO.AddComponent<CanvasRenderer>();
        var bodyTMP = bodyGO.AddComponent<TextMeshProUGUI>();
        bodyTMP.text = "";
        bodyTMP.fontSize = 26;
        bodyTMP.color = Color.white;
        bodyTMP.enableWordWrapping = true;
        bodyTMP.raycastTarget = false;
        var bodyRT = bodyGO.GetComponent<RectTransform>();
        bodyRT.anchorMin = new Vector2(0.06f, 0.06f);
        bodyRT.anchorMax = new Vector2(0.94f, 0.68f);
        bodyRT.pivot = new Vector2(0.5f, 0.5f);
        bodyRT.anchoredPosition = Vector2.zero;
        bodyRT.sizeDelta = Vector2.zero;

        // ── AdvanceButton (전체 화면 투명, 클릭 전달용) ─────────────
        var btnGO = new GameObject("AdvanceButton");
        btnGO.layer = LayerMask.NameToLayer("UI");
        btnGO.transform.SetParent(root.transform, false);
        btnGO.AddComponent<CanvasRenderer>();
        var btnImg = btnGO.AddComponent<Image>();
        btnImg.color = new Color(0, 0, 0, 0.01f); // 완전 투명 대신 0.01 — Raycast 통과 목적
        var btn = btnGO.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        var btnRT = btnGO.GetComponent<RectTransform>();
        Stretch(btnRT);

        // ── SerializeField 연결 ──────────────────────────────────────
        var popup = root.GetComponent<UI_DialoguePopup>();
        var so    = new SerializedObject(popup);
        so.FindProperty("portraitLeft").objectReferenceValue   = godImg;
        so.FindProperty("portraitRight").objectReferenceValue  = abyssImg;
        so.FindProperty("speakerNameText").objectReferenceValue = nameTMP;
        so.FindProperty("bodyText").objectReferenceValue       = bodyTMP;
        so.FindProperty("advanceButton").objectReferenceValue  = btn;
        so.ApplyModifiedPropertiesWithoutUndo();

        // ── 프리팹 저장 ──────────────────────────────────────────────
        const string dir  = "Assets/Abyss/UI/Popup/Dialogue";
        const string path = dir + "/UI_DialoguePopup.prefab";
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        PrefabUtility.SaveAsPrefabAssetAndConnect(
            root, path, InteractionMode.AutomatedAction);

        Object.DestroyImmediate(root);
        AssetDatabase.Refresh();

        // ── Addressables 등록 ─────────────────────────────────────
        RegisterAddressable(path, "UI/Popup/UI_DialoguePopup");

        Debug.Log($"[CreateDialoguePopupPrefab] 생성 완료: {path}");
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static void RegisterAddressable(string assetPath, string address)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogWarning("[CreateDialoguePopupPrefab] Addressable Settings 없음 — 수동으로 등록하세요.");
            return;
        }

        var guid  = AssetDatabase.AssetPathToGUID(assetPath);
        var group = settings.DefaultGroup;
        var entry = settings.CreateOrMoveEntry(guid, group, false, false);
        entry.address = address;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[CreateDialoguePopupPrefab] Addressables 등록: {address}");
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static Image MakeImage(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        go.AddComponent<CanvasRenderer>();
        return go.AddComponent<Image>();
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
    }

    /// <summary>앵커/피봇/위치/크기를 한 번에 설정.</summary>
    private static void SetRect(RectTransform rt,
        Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pivot, Vector2 anchoredPos, Vector2 size)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
    }
}
