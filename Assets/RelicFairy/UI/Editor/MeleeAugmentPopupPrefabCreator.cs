#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 증강 고르기 팝업(<see cref="UI_MeleeAugmentPopup"/>) 빈 프리팹 만들기(10-15) — 화면은 Init 때 코드로 짓는다(룬 선택 · 파츠 드래프트와 같은 길).
/// 프리팹 = 화면 가득 RectTransform + 컴포넌트 하나. 주소 「UI/Popup/UI_MeleeAugmentPopup」 · 그룹은 룬 선택 팝업과 같은 곳.
/// 열린 장면을 건드리지 않게 미리보기 장면에서 짓는다. 다시 돌리면 덮어쓴다.
/// </summary>
public static class MeleeAugmentPopupPrefabCreator
{
    private const string PrefabPath    = "Assets/RelicFairy/UI/Popup/UI_MeleeAugmentPopup.prefab";
    private const string Address       = "UI/Popup/UI_MeleeAugmentPopup";
    private const string SiblingPrefab = "Assets/RelicFairy/UI/Popup/UI_RuneSelectPopup.prefab";

    [MenuItem("RelicFairy/UI/10-15 증강 고르기 팝업 프리팹 만들기 (편집 모드)")]
    private static void Create()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[증강 창] 편집 모드에서만"); return; }

        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var go = new GameObject("UI_MeleeAugmentPopup", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(go, scene);
            go.layer = LayerMask.NameToLayer("UI");
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            go.AddComponent<UI_MeleeAugmentPopup>();
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogWarning("[증강 창] Addressables 설정 없음"); return; }
        var sibling = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(SiblingPrefab));
        var group   = sibling != null ? sibling.parentGroup : settings.DefaultGroup;
        var entry   = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(PrefabPath), group);
        entry.address = Address;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[증강 창] 프리팹 끝 — {PrefabPath} · 주소 {entry.address} · 그룹 {group.Name}");
    }
}
#endif
