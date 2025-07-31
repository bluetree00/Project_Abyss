using UnityEditor;
using UnityEngine;
using System.IO;

public class PlayerAbilitySOGeneratorEditor : EditorWindow
{
    private Define.PlayerAbilityType selectedType;
    private string abilityName = "NewPlayerAbility";

    private float duration = 1f;
    private float dashSpeed = 10f;
    private float dodgeCooldown = 1f;

    [MenuItem("Tools/Player Ability SO Generator")]
    public static void ShowWindow()
    {
        GetWindow<PlayerAbilitySOGeneratorEditor>("Player Ability SO Generator");
    }

    private void OnGUI()
    {
        GUILayout.Label("Create New Player Ability SO", EditorStyles.boldLabel);

        selectedType = (Define.PlayerAbilityType)EditorGUILayout.EnumPopup("Player Ability Type", selectedType);
        abilityName = EditorGUILayout.TextField("Ability Name", abilityName);

        switch (selectedType)
        {
            case Define.PlayerAbilityType.Dodge:
                duration = EditorGUILayout.FloatField("Dash Duration", duration);
                dashSpeed = EditorGUILayout.FloatField("Dash Speed", dashSpeed);
                dodgeCooldown = EditorGUILayout.FloatField("Dodge Cooldown", dodgeCooldown);
                break;
            // 추가 플레이어 어빌리티 속성 처리...
        }

        if (GUILayout.Button("Generate Player Ability SO"))
        {
            GenerateSO();
        }
    }

    private void GenerateSO()
    {
        string path = "Assets/GameData/PlayerAbilities/";
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);

        ScriptableObject asset = null;

        switch (selectedType)
        {
            case Define.PlayerAbilityType.Dodge:
                // var dodgeSO = ScriptableObject.CreateInstance<PlayerDodgeAbilitySO>();
                // dodgeSO.SetDuration(duration);
                // dodgeSO.SetDashSpeed(dashSpeed);
                // dodgeSO.SetDodgeCooldown(dodgeCooldown);
                // asset = dodgeSO;
                break;
            // 기타 플레이어 어빌리티 생성 로직...
        }

        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{path}{abilityName}.asset");
        AssetDatabase.CreateAsset(asset, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.FocusProjectWindow();
        Selection.activeObject = asset;

        Debug.Log($"Player Ability SO '{abilityName}' created at {assetPath}");
    }
}
