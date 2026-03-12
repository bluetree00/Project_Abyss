using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// LobbyRoot.prefab의 UI_PrepPanel SerializedField 참조를
/// Bamao 레이아웃 기준으로 자동 연결하는 Editor 도구.
/// Menu: Abyss/UI/Wire PrepPanel Inspector Fields
/// </summary>
public static class UI_PrepPanelWirer
{
    const string LobbyRootPath = "Assets/Abyss/UI/Scene/ScenePrefabs/LobbyRoot.prefab";

    [MenuItem("Abyss/UI/Wire PrepPanel Inspector Fields")]
    public static void Wire()
    {
        var lobbyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyRootPath);
        if (lobbyPrefab == null) { Debug.LogError("[PrepPanelWirer] LobbyRoot.prefab을 찾을 수 없습니다."); return; }

        string path = AssetDatabase.GetAssetPath(lobbyPrefab);
        var root = PrefabUtility.LoadPrefabContents(path);

        try
        {
            var panelPrep     = root.transform.Find("Panel_Prep");
            var charInfoPopup = panelPrep.Find("CharInfoPopup");
            var sheetFrame    = charInfoPopup.Find("CharacterSheetFrame");
            var statusFrame   = charInfoPopup.Find("CharacterStatusFrame");

            if (panelPrep == null || charInfoPopup == null || sheetFrame == null || statusFrame == null)
            {
                Debug.LogError("[PrepPanelWirer] 필요한 오브젝트를 찾을 수 없습니다. 먼저 Rebuild를 실행하세요.");
                return;
            }

            var prepPanel = panelPrep.GetComponent<UI_PrepPanel>();
            if (prepPanel == null) { Debug.LogError("[PrepPanelWirer] UI_PrepPanel 컴포넌트를 찾을 수 없습니다."); return; }

            var so = new SerializedObject(prepPanel);

            // ── 캐릭터 목록 그리드 ──
            SetObjectRef(so, "characterListRoot",
                panelPrep.Find("ScrollPanel/Scroll View/Viewport/Content"));
            SetObjectRef(so, "itemTemplate",
                panelPrep.Find("ScrollPanel/Scroll View/Viewport/Content/ItemTemplate")
                         ?.GetComponent<UI_CharacterSelectItem>());

            // ── 팝업 / 딤 ──
            SetObjectRef(so, "charInfoPopup", charInfoPopup.gameObject);
            SetObjectRef(so, "bgDim", panelPrep.Find("BG_Dim")?.gameObject);

            // ── CharacterSheetFrame: 캐릭터 이미지 + 이름 텍스트 ──
            SetObjectRef(so, "charImage",
                sheetFrame.Find("Charactor_AMeow")?.GetComponent<Image>());
            SetObjectRef(so, "charNameText",
                sheetFrame.Find("InfoText")?.GetComponent<TMP_Text>());

            // ── CharacterStatusFrame: 슬라이더 ──
            var sliderHeart   = statusFrame.Find("SliderList/SliderHeart");
            var sliderStamina = statusFrame.Find("SliderList/SliderStamina");
            var sliderAtk     = statusFrame.Find("SliderList/SliderAtk");

            SetObjectRef(so, "sliderHp",      sliderHeart?.GetComponent<Slider>());
            SetObjectRef(so, "sliderHpText",  sliderHeart?.Find("SliderValueText")?.GetComponent<TMP_Text>());
            SetObjectRef(so, "sliderSpd",     sliderStamina?.GetComponent<Slider>());
            SetObjectRef(so, "sliderSpdText", sliderStamina?.Find("SliderValueText")?.GetComponent<TMP_Text>());
            SetObjectRef(so, "sliderAtk",     sliderAtk?.GetComponent<Slider>());
            SetObjectRef(so, "sliderAtkText", sliderAtk?.Find("SliderValueText")?.GetComponent<TMP_Text>());

            // ── 버튼 ──
            SetObjectRef(so, "confirmButton",    charInfoPopup.Find("Btn_Confirm")?.GetComponent<Button>());
            SetObjectRef(so, "closePopupButton", charInfoPopup.Find("Btn_ClosePopup")?.GetComponent<Button>());
            SetObjectRef(so, "cancelButton",     panelPrep.Find("Btn_Cancel")?.GetComponent<Button>());

            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, path);
            AssetDatabase.Refresh();
            Debug.Log("[PrepPanelWirer] 완료 — UI_PrepPanel 필드가 모두 연결되었습니다.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void SetObjectRef(SerializedObject so, string fieldName, Object obj)
    {
        var prop = so.FindProperty(fieldName);
        if (prop == null)
        {
            Debug.LogWarning($"[PrepPanelWirer] 필드를 찾을 수 없음: {fieldName}");
            return;
        }
        if (obj == null)
        {
            Debug.LogWarning($"[PrepPanelWirer] 대상 오브젝트 null: {fieldName}");
            return;
        }
        prop.objectReferenceValue = obj;
    }
}
