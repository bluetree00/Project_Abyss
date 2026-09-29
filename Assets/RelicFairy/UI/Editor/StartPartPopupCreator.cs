using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 시작 파츠 선택창(<see cref="UI_StartPartPopup"/>) 프리팹을 만들고 Addressables에 <c>UI/Popup/UI_StartPartPopup</c>으로 등록한다.
/// 창은 Init에서 코드로 짓는다 — 프리팹은 루트(RectTransform + 컴포넌트)뿐이다.
/// <b>격리된 미리보기 씬</b>에서 만들어 열린 씬을 건드리지 않는다(「에디터 스크립트로 씬 수정 금지」).
/// </summary>
public static class StartPartPopupCreator
{
    private const string PrefabPath = "Assets/RelicFairy/UI/Popup/UI_StartPartPopup.prefab";
    private const string Address    = "UI/Popup/UI_StartPartPopup";

    [MenuItem("RelicFairy/UI/시작 파츠 선택창 프리팹 만들기·등록")]
    private static void CreateOrRegister()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = new GameObject("UI_StartPartPopup", typeof(RectTransform));
                SceneManager.MoveGameObjectToScene(go, preview);
                go.AddComponent<UI_StartPartPopup>();
                PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogError("[StartPartPopup] AddressableAssetSettings 없음"); return; }
        var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(PrefabPath), settings.DefaultGroup, false, false);
        entry.address = Address;
        settings.SetDirty(UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[StartPartPopup] {PrefabPath} → '{Address}' ({settings.DefaultGroup.Name})");
    }
}
