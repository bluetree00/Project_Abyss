using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

/// <summary>
/// CharInfoPopup 구조를 Bamao CharacterSheetFrame + CharacterStatusFrame 기반으로 재구성하는 Editor 도구.
/// Menu: Abyss/UI/Rebuild CharInfoPopup with Bamao Layout
/// </summary>
public static class CharInfoPopupRebuilder
{
    const string LobbyRootPath    = "Assets/Abyss/UI/Scene/ScenePrefabs/LobbyRoot.prefab";
    const string SheetFramePath   = "Assets/Abyss/Prefabs/UI/Bamao/BamaoUIPack/Prefabs/Frame/CharacterSheetFrame.prefab";
    const string StatusFramePath  = "Assets/Abyss/Prefabs/UI/Bamao/BamaoUIPack/Prefabs/Frame/CharacterStatusFrame.prefab";

    [MenuItem("Abyss/UI/Rebuild CharInfoPopup with Bamao Layout")]
    public static void Rebuild()
    {
        var lobbyPrefab  = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyRootPath);
        var sheetPrefab  = AssetDatabase.LoadAssetAtPath<GameObject>(SheetFramePath);
        var statusPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StatusFramePath);

        if (lobbyPrefab  == null) { Debug.LogError("[CharInfoPopupRebuilder] LobbyRoot prefab을 찾을 수 없습니다."); return; }
        if (sheetPrefab  == null) { Debug.LogError("[CharInfoPopupRebuilder] CharacterSheetFrame prefab을 찾을 수 없습니다."); return; }
        if (statusPrefab == null) { Debug.LogError("[CharInfoPopupRebuilder] CharacterStatusFrame prefab을 찾을 수 없습니다."); return; }

        string path = AssetDatabase.GetAssetPath(lobbyPrefab);
        var root = PrefabUtility.LoadPrefabContents(path);

        try
        {
            var charInfoPopup = root.transform.Find("Panel_Prep/CharInfoPopup");
            if (charInfoPopup == null)
            {
                Debug.LogError("[CharInfoPopupRebuilder] Panel_Prep/CharInfoPopup 를 찾을 수 없습니다.");
                return;
            }

            // 기존 단순 텍스트 자식 제거 (Btn_Confirm, Btn_ClosePopup은 유지)
            var toRemove = new List<GameObject>();
            foreach (Transform child in charInfoPopup)
            {
                switch (child.name)
                {
                    case "Img_PreviewPortrait":
                    case "Txt_PreviewName":
                    case "Txt_PreviewHp":
                    case "Txt_PreviewAttack":
                        toRemove.Add(child.gameObject);
                        break;
                }
            }
            foreach (var go in toRemove)
                Object.DestroyImmediate(go);

            // CharInfoPopup 크기 확장 (두 Bamao 프레임을 수용)
            var popupRect = charInfoPopup.GetComponent<RectTransform>();
            if (popupRect != null)
            {
                popupRect.sizeDelta       = new Vector2(860f, 600f);
                popupRect.anchoredPosition = Vector2.zero;
            }

            // CharacterSheetFrame 추가 (왼쪽)
            var sheetGo = PrefabUtility.InstantiatePrefab(sheetPrefab, charInfoPopup) as GameObject;
            if (sheetGo != null)
            {
                sheetGo.name = "CharacterSheetFrame";
                var rt = sheetGo.GetComponent<RectTransform>();
                rt.anchorMin        = new Vector2(0f, 0.5f);
                rt.anchorMax        = new Vector2(0f, 0.5f);
                rt.pivot            = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(10f, 0f);
                sheetGo.transform.SetAsFirstSibling();
            }

            // CharacterStatusFrame 추가 (오른쪽)
            var statusGo = PrefabUtility.InstantiatePrefab(statusPrefab, charInfoPopup) as GameObject;
            if (statusGo != null)
            {
                statusGo.name = "CharacterStatusFrame";
                var rt = statusGo.GetComponent<RectTransform>();
                rt.anchorMin        = new Vector2(1f, 0.5f);
                rt.anchorMax        = new Vector2(1f, 0.5f);
                rt.pivot            = new Vector2(1f, 0.5f);
                rt.anchoredPosition = new Vector2(-10f, 0f);
                statusGo.transform.SetSiblingIndex(1);
            }

            // Btn_Confirm, Btn_ClosePopup을 맨 뒤로 이동 (두 프레임 위에 렌더)
            MoveToEnd(charInfoPopup, "Btn_Confirm");
            MoveToEnd(charInfoPopup, "Btn_ClosePopup");

            PrefabUtility.SaveAsPrefabAsset(root, path);
            AssetDatabase.Refresh();
            Debug.Log("[CharInfoPopupRebuilder] 완료 — LobbyRoot.prefab 저장되었습니다.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void MoveToEnd(Transform parent, string childName)
    {
        var t = parent.Find(childName);
        if (t != null) t.SetAsLastSibling();
    }
}
