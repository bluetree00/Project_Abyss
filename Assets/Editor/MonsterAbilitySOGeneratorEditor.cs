using UnityEditor;
using UnityEngine;
using System.IO;

public class MonsterAbilitySOGeneratorEditor : EditorWindow
{
    private Define.MonsterAbilityType selectedType;
    private string abilityName = "NewMonsterAbility";

    private float range = 5f;
    private float duration = 1f;
    private float damage = 10f;

    [MenuItem("Tools/Monster Ability SO Generator")]
    public static void ShowWindow()
    {
        GetWindow<MonsterAbilitySOGeneratorEditor>("Monster Ability SO Generator");
    }

    private void OnGUI()
    {
        GUILayout.Label("Create New Monster Ability SO", EditorStyles.boldLabel);

        selectedType = (Define.MonsterAbilityType)EditorGUILayout.EnumPopup("Monster Ability Type", selectedType);
        abilityName = EditorGUILayout.TextField("Ability Name", abilityName);

        switch (selectedType)
        {
            case Define.MonsterAbilityType.Detect:
                range = EditorGUILayout.FloatField("Detection Range", range);
                break;
            case Define.MonsterAbilityType.Dash:
                duration = EditorGUILayout.FloatField("Dash Duration", duration);
                break;
            case Define.MonsterAbilityType.Fireball:
                damage = EditorGUILayout.FloatField("Fireball Damage", damage);
                break;
        }

        if (GUILayout.Button("Generate Monster Ability SO"))
        {
            GenerateSO();
        }
    }

    private void GenerateSO()
    {
        string path = "Assets/GameData/MonsterAbilities/";
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);

        ScriptableObject asset = null;

        switch (selectedType)
        {
            case Define.MonsterAbilityType.Detect:
                var detect = ScriptableObject.CreateInstance<DetectAbilitySO>();
            //    detect.type = (Define.AbilityType)selectedType;  // 필요 시 변환
                detect.SetRange(range);
                asset = detect;
                break;
            // 기타 몬스터 어빌리티 생성 로직...
        }

        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{path}{abilityName}.asset");
        AssetDatabase.CreateAsset(asset, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.FocusProjectWindow();
        Selection.activeObject = asset;

        Debug.Log($"Monster Ability SO '{abilityName}' created at {assetPath}");
    }
}
