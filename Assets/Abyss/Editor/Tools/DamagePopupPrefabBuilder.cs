using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 일회성 빌더 — DamagePopup 프리팹을 생성하고 Addressable에 'DamagePopup' 키로 등록.
/// 메뉴: Tools/Abyss/Build DamagePopup Prefab
/// </summary>
public static class DamagePopupPrefabBuilder
{
    private const string PrefabPath  = "Assets/Abyss/Prefabs/UI/DamagePopup.prefab";
    private const string AddressKey  = "DamagePopup";
    private const string GroupName   = "Effects";

    [MenuItem("Tools/Abyss/Build DamagePopup Prefab")]
    public static void Build()
    {
        // ── 디렉토리 보장 ────────────────────────────────────────────
        var dir = Path.GetDirectoryName(PrefabPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        // ── GameObject 구성 ──────────────────────────────────────────
        var root = new GameObject("DamagePopup", typeof(Canvas), typeof(CanvasGroup), typeof(DamagePopup));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        root.transform.localScale = Vector3.one * 0.005f;  // 월드 1m ≈ TMP 200pt

        var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(root.transform, false);
        var rt = (RectTransform)label.transform;
        rt.sizeDelta = new Vector2(300, 80);
        rt.anchoredPosition = Vector2.zero;
        var tmp = label.GetComponent<TextMeshProUGUI>();
        tmp.text = "0";
        tmp.fontSize = 64;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.fontStyle = FontStyles.Bold;
        tmp.outlineWidth = 0.25f;
        tmp.outlineColor = new Color(0, 0, 0, 1);
        tmp.raycastTarget = false;

        // ── Popup 컴포넌트에 참조 와이어링 ────────────────────────────
        var popup = root.GetComponent<DamagePopup>();
        var so = new SerializedObject(popup);
        so.FindProperty("label").objectReferenceValue       = tmp;
        so.FindProperty("canvasGroup").objectReferenceValue = root.GetComponent<CanvasGroup>();
        so.ApplyModifiedPropertiesWithoutUndo();

        // ── 프리팹 저장 ──────────────────────────────────────────────
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        if (prefab == null)
        {
            Debug.LogError("[Builder] DamagePopup 프리팹 저장 실패");
            return;
        }

        // ── Addressable 등록 ─────────────────────────────────────────
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogWarning("[Builder] Addressable Settings 없음 — 프리팹은 저장됐지만 키 등록 못함");
            Debug.Log("[Builder] DamagePopup 프리팹 빌드 완료 (Addressable 미등록)");
            return;
        }

        var group = settings.FindGroup(GroupName);
        if (group == null) group = settings.DefaultGroup;

        var guid = AssetDatabase.AssetPathToGUID(PrefabPath);
        var entry = settings.CreateOrMoveEntry(guid, group);
        entry.address = AddressKey;

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Builder] DamagePopup 프리팹 빌드 + Addressable '{AddressKey}' 등록 완료");
    }
}
