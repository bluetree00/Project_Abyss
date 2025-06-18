using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

public class MonsterAbilitySOGeneratorEditor : EditorWindow
{
    private Define.MonsterAbilityType selectedType;
    private string abilityName = "NewMonsterAbility";

    private float range = 5f;
    private float duration = 1f;
    private float damage = 10f;
    private float chaseSpeed = 3f;
    private float chaseAttackRange = 2f;

    private Vector2 scrollPos;
    private List<Vector3> patrolPoints = new List<Vector3>();

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
            case Define.MonsterAbilityType.Chase:
                chaseSpeed = EditorGUILayout.FloatField("Chase Speed", chaseSpeed);
                chaseAttackRange = EditorGUILayout.FloatField("Attack Range", chaseAttackRange);
                break;
             case Define.MonsterAbilityType.Patrol:
                EditorGUILayout.LabelField("Patrol Waypoints (World Space)");

                scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(100));
                for (int i = 0; i < patrolPoints.Count; i++)
                {
                    patrolPoints[i] = EditorGUILayout.Vector3Field($"Waypoint {i + 1}", patrolPoints[i]);
                }
                EditorGUILayout.EndScrollView();

                if (GUILayout.Button("Add Waypoint"))
                {
                    patrolPoints.Add(Vector3.zero);
                }

                if (GUILayout.Button("Clear Waypoints"))
                {
                    patrolPoints.Clear();
                }
                break;
           
        }

        if (GUILayout.Button("Generate Monster Ability SO"))
        {
            GenerateSO();
        }
    }

    private void GenerateSO()
    {
        string path = "Assets/02.Scripts/Character/Monster/MonsterAbilitySo/"; // 생성할 경로
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);

        ScriptableObject asset = null;

        switch (selectedType)
        {
            case Define.MonsterAbilityType.Detect:
                var detect = ScriptableObject.CreateInstance<DetectAbilitySO>();
                detect.SetRange(range);
                asset = detect;
                break;
            case Define.MonsterAbilityType.Chase:
                var chase = ScriptableObject.CreateInstance<ChaseAbilitySO>();
                chase.SetChaseSpeed(chaseSpeed);
                chase.SetAttackRange(chaseAttackRange);
                asset = chase;
                break;
            case Define.MonsterAbilityType.Patrol:
                var patrol = ScriptableObject.CreateInstance<PatrolAbilitySO>();
                patrol.SetWaypoints(patrolPoints.ToArray());
                asset = patrol;
                break;
            
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
