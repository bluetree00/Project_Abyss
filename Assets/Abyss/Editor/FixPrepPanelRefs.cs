using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

public static class FixPrepPanelRefs
{
    [MenuItem("Tools/Fix PrepPanel Inspector Refs")]
    public static void Fix()
    {
        string prefabPath = "Assets/Abyss/UI/Scene/ScenePrefabs/LobbyRoot.prefab";

        using var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath);
        var root = scope.prefabContentsRoot;

        var prepPanel = root.transform.Find("Panel_Prep");
        if (prepPanel == null) { Debug.LogError("Panel_Prep not found"); return; }

        var ui = prepPanel.GetComponent<UI_PrepPanel>();
        if (ui == null) { Debug.LogError("UI_PrepPanel not found"); return; }

        var popup = prepPanel.Find("CharInfoPopup");
        var sheet = popup.Find("CharacterSheetFrame");
        var status = popup.Find("CharacterStatusFrame");
        var sliderList = status.Find("SliderList");

        // SerializedObject로 private 필드 접근
        var so = new SerializedObject(ui);

        so.FindProperty("charImage").objectReferenceValue =
            sheet.Find("Charactor_AMeow").GetComponent<Image>();

        so.FindProperty("charNameText").objectReferenceValue =
            sheet.Find("InfoText").GetComponent<TextMeshProUGUI>();

        so.FindProperty("sliderHp").objectReferenceValue =
            sliderList.Find("SliderHeart").GetComponent<Slider>();
        so.FindProperty("sliderHpText").objectReferenceValue =
            sliderList.Find("SliderHeart/SliderValueText").GetComponent<TextMeshProUGUI>();

        so.FindProperty("sliderAtk").objectReferenceValue =
            sliderList.Find("SliderAtk").GetComponent<Slider>();
        so.FindProperty("sliderAtkText").objectReferenceValue =
            sliderList.Find("SliderAtk/SliderValueText").GetComponent<TextMeshProUGUI>();

        so.FindProperty("sliderSpd").objectReferenceValue =
            sliderList.Find("SliderStamina").GetComponent<Slider>();
        so.FindProperty("sliderSpdText").objectReferenceValue =
            sliderList.Find("SliderStamina/SliderValueText").GetComponent<TextMeshProUGUI>();

        so.ApplyModifiedProperties();

        Debug.Log("[FixPrepPanelRefs] 8개 필드 연결 완료!");
    }
}
