#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// @UIRoot 프리팹에 UI_BossBark 계층을 생성하는 1회성 에디터 툴.
/// 메뉴: RelicFairy/UI/Setup BossBark in @UIRoot
/// </summary>
public static class CreateBossBarkSetup
{
    private const string PrefabPath = "Assets/RelicFairy/UI/RootUI/@UIRoot.prefab";
    private const string CanvasHudPath = "@UIRoot/Canvas_HUD";
    private const string BossBarkName = "UI_BossBark";

    [MenuItem("RelicFairy/UI/Setup BossBark")]
    public static void Execute()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[BossBarkSetup] 프리팹을 찾을 수 없습니다: {PrefabPath}");
            return;
        }

        using var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath);
        SetupInRoot(scope.prefabContentsRoot);
    }

    /// <summary>이미 열린 EditPrefabContentsScope 안에서도 사용 가능한 내부 메서드.</summary>
    public static void SetupInRoot(GameObject root)
    {
        // Canvas_HUD 탐색
        var canvasHud = FindDeep(root.transform, "Canvas_HUD");
        if (canvasHud == null)
        {
            Debug.LogError("[BossBarkSetup] Canvas_HUD를 찾을 수 없습니다.");
            return;
        }

        // 이미 존재하면 스킵
        if (FindDeep(root.transform, BossBarkName) != null)
        {
            Debug.Log("[BossBarkSetup] UI_BossBark가 이미 존재합니다.");
            return;
        }

        // ── UI_BossBark (루트) ─────────────────────────────────────
        var barkGo = new GameObject(BossBarkName);
        barkGo.transform.SetParent(canvasHud, false);
        var barkRect = barkGo.AddComponent<RectTransform>();
        barkRect.anchorMin = Vector2.zero;
        barkRect.anchorMax = Vector2.one;
        barkRect.offsetMin = Vector2.zero;
        barkRect.offsetMax = Vector2.zero;

        var canvasGroup = barkGo.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        var bark = barkGo.AddComponent<RelicFairy.UI.UI_BossBark>();

        // ── Container (앵커 중앙) ──────────────────────────────────
        var containerGo = new GameObject("Container");
        containerGo.transform.SetParent(barkGo.transform, false);
        var containerRect = containerGo.AddComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.5f, 0.5f);
        containerRect.anchorMax = new Vector2(0.5f, 0.5f);
        containerRect.pivot = new Vector2(0.5f, 0.5f);
        containerRect.sizeDelta = new Vector2(1200f, 120f);
        containerRect.anchoredPosition = Vector2.zero;

        // ── Background Image ───────────────────────────────────────
        var bgGo = new GameObject("Background");
        bgGo.transform.SetParent(containerGo.transform, false);
        var bgRect = bgGo.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        var img = bgGo.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.72f);
        img.raycastTarget = false;

        // ── Label (TMP) ────────────────────────────────────────────
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(containerGo.transform, false);
        var labelRect = labelGo.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(24f, 8f);
        labelRect.offsetMax = new Vector2(-24f, -8f);
        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.text = "";
        tmp.fontSize = 44f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        // ── UI_BossBark 필드 연결 ──────────────────────────────────
        var so = new SerializedObject(bark);
        so.FindProperty("_canvasGroup").objectReferenceValue = canvasGroup;
        so.FindProperty("_background").objectReferenceValue  = img;
        so.FindProperty("_label").objectReferenceValue       = tmp;
        so.FindProperty("_container").objectReferenceValue   = containerRect;
        so.ApplyModifiedProperties();

        Debug.Log("[BossBarkSetup] UI_BossBark 계층 생성 완료. Canvas_HUD/UI_BossBark → Container/Background+Label");
    }

    private static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            var found = FindDeep(t.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
#endif
