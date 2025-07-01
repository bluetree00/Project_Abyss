using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class MonsterAbilitySetEditorWindow : EditorWindow
{
    private Define.MonsterType selectedMonsterType;
    private float attackRange = 2f;
    private float attackCooldown = 1.5f;

    private float detectRange = 5f;
    private float chaseSpeed = 3.5f;
    private float chaseAttackRange = 1.8f;

    private List<Vector3> patrolWaypoints = new();
    private Vector2 patrolScrollPos;

    private List<AttackAbilitySO> availableAttackAbilities = new();
    private List<bool> selectedAttackAbilityFlags = new();

    private string[] attackSOTypeNames;
    private Type[] attackSOTypes;
    private int selectedAttackTypeIndex = 0;

    private string newAttackSOName = "NewAttackSO";
    private AttackAbilitySO tempAttackSO;
    private Editor tempEditor;
    private SerializedObject serializedTempAttackSO;

    private string basePath = "Assets/02.Scripts/Character/Monster/MonsterAbilitySo";

    [MenuItem("Tools/몬스터 AbilitySet 생성기")]
    public static void ShowWindow()
    {
        var window = GetWindow<MonsterAbilitySetEditorWindow>("몬스터 AbilitySet 생성기");
        window.LoadAvailableAttackAbilities();
        window.LoadAttackAbilityTypes();
    }

    private void OnEnable()
    {
        LoadAvailableAttackAbilities();
        LoadAttackAbilityTypes();
    }

    private void LoadAvailableAttackAbilities()
    {
        availableAttackAbilities.Clear();
        selectedAttackAbilityFlags.Clear();

        string[] guids = AssetDatabase.FindAssets("t:AttackAbilitySO");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AttackAbilitySO ability = AssetDatabase.LoadAssetAtPath<AttackAbilitySO>(path);
            if (ability != null)
            {
                availableAttackAbilities.Add(ability);
                selectedAttackAbilityFlags.Add(false);
            }
        }
    }

    private void LoadAttackAbilityTypes()
    {
        attackSOTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(t => t.IsSubclassOf(typeof(AttackAbilitySO)) && !t.IsAbstract)
            .ToArray();

        attackSOTypeNames = attackSOTypes.Select(t => t.Name).ToArray();
    }

    private void OnGUI()
    {
        GUILayout.Label("몬스터 AbilitySet 생성기", EditorStyles.boldLabel);
        selectedMonsterType = (Define.MonsterType)EditorGUILayout.EnumPopup("몬스터 타입", selectedMonsterType);

        GUILayout.Space(10);
        GUILayout.Label("새 공격 SO 생성", EditorStyles.boldLabel);
        selectedAttackTypeIndex = EditorGUILayout.Popup("공격 타입 선택", selectedAttackTypeIndex, attackSOTypeNames);

        newAttackSOName = EditorGUILayout.TextField("SO 파일 이름", newAttackSOName);

        if (attackSOTypes.Length > 0)
        {
            Type selectedType = attackSOTypes[selectedAttackTypeIndex];
            if (tempAttackSO == null || tempAttackSO.GetType() != selectedType)
            {
                tempAttackSO = ScriptableObject.CreateInstance(selectedType) as AttackAbilitySO;
                tempEditor = Editor.CreateEditor(tempAttackSO);
                serializedTempAttackSO = new SerializedObject(tempAttackSO);
            }

            serializedTempAttackSO.Update();
            tempEditor.OnInspectorGUI();
            serializedTempAttackSO.ApplyModifiedProperties();

            if (GUILayout.Button("공격 SO 저장"))
            {
                string attackFolder = $"{basePath}/AttackAbilities";
                if (!Directory.Exists(attackFolder))
                    Directory.CreateDirectory(attackFolder);

                string name = string.IsNullOrWhiteSpace(newAttackSOName) ? selectedType.Name : newAttackSOName;
                string path = AssetDatabase.GenerateUniqueAssetPath($"{attackFolder}/{name}.asset");

                AssetDatabase.CreateAsset(UnityEngine.Object.Instantiate(tempAttackSO), path);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                LoadAvailableAttackAbilities();
                Debug.Log($"공격 SO 저장됨: {path}");
            }
        }

        GUILayout.Space(10);
        GUILayout.Label("AttackAbilitySet에 포함할 개별 공격 선택", EditorStyles.boldLabel);

        using (var scroll = new EditorGUILayout.ScrollViewScope(new Vector2(0, 0), GUILayout.Height(150)))
        {
            for (int i = 0; i < availableAttackAbilities.Count; i++)
            {
                if (availableAttackAbilities[i] == null) continue;
                selectedAttackAbilityFlags[i] = EditorGUILayout.ToggleLeft(availableAttackAbilities[i].name, selectedAttackAbilityFlags[i]);
            }
        }

        GUILayout.Space(10);
        GUILayout.Label("Detect Ability 설정", EditorStyles.boldLabel);
        detectRange = EditorGUILayout.FloatField("Detect Range", detectRange);

        GUILayout.Space(10);
        GUILayout.Label("Chase Ability 설정", EditorStyles.boldLabel);
        chaseSpeed = EditorGUILayout.FloatField("Chase Speed", chaseSpeed);
        chaseAttackRange = EditorGUILayout.FloatField("Chase Attack Range", chaseAttackRange);

        GUILayout.Space(10);
        GUILayout.Label("Patrol Ability 설정 - Waypoints", EditorStyles.boldLabel);
        patrolScrollPos = EditorGUILayout.BeginScrollView(patrolScrollPos, GUILayout.Height(100));
        for (int i = 0; i < patrolWaypoints.Count; i++)
        {
            patrolWaypoints[i] = EditorGUILayout.Vector3Field($"Waypoint {i + 1}", patrolWaypoints[i]);
        }
        EditorGUILayout.EndScrollView();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("웨이포인트 추가")) patrolWaypoints.Add(Vector3.zero);
        if (GUILayout.Button("웨이포인트 모두 삭제")) patrolWaypoints.Clear();
        GUILayout.EndHorizontal();

        GUILayout.Space(20);
        if (GUILayout.Button("AbilitySet SO 생성"))
        {
            GenerateAbilitySetSO();
        }
    }

    private void GenerateAbilitySetSO()
    {
        string folderPath = $"{basePath}/{selectedMonsterType}";
        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

        string assetPath = $"{folderPath}/{selectedMonsterType}AbilitySet.asset";
        if (File.Exists(assetPath) &&
            !EditorUtility.DisplayDialog("덮어쓰기 확인", $"{selectedMonsterType}AbilitySet SO가 이미 존재합니다. 덮어쓰시겠습니까?", "예", "아니오"))
        {
            Debug.Log("생성 취소됨");
            return;
        }

        var abilitySetSO = AssetDatabase.LoadAssetAtPath<MonsterAbilitySetSO>(assetPath);
        if (abilitySetSO == null)
        {
            abilitySetSO = ScriptableObject.CreateInstance<MonsterAbilitySetSO>();
            AssetDatabase.CreateAsset(abilitySetSO, assetPath);
        }
        abilitySetSO.abilities.Clear();

        CreateAndAddAbility<AttackAbilitySetSO>(abilitySetSO, folderPath, $"{selectedMonsterType}_Attack", so =>
        {
            so.attackAbilities.Clear();

            for (int i = 0; i < availableAttackAbilities.Count; i++)
            {
                if (selectedAttackAbilityFlags[i])
                    so.attackAbilities.Add(availableAttackAbilities[i]);
            }
        });

        CreateAndAddAbility<DetectAbilitySO>(abilitySetSO, folderPath, $"{selectedMonsterType}_Detect", so => so.SetRange(detectRange));
        CreateAndAddAbility<ChaseAbilitySO>(abilitySetSO, folderPath, $"{selectedMonsterType}_Chase", so =>
        {
            so.SetChaseSpeed(chaseSpeed);
            so.SetAttackRange(chaseAttackRange);
        });
        CreateAndAddAbility<PatrolAbilitySO>(abilitySetSO, folderPath, $"{selectedMonsterType}_Patrol", so => so.SetWaypoints(patrolWaypoints.ToArray()));

        EditorUtility.SetDirty(abilitySetSO);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"{selectedMonsterType} AbilitySet SO 생성 완료");
    }

    private void CreateAndAddAbility<T>(MonsterAbilitySetSO setSO, string folder, string name, Action<T> initializer) where T : MonsterAbilitySO
    {
        string path = $"{folder}/{name}.asset";
        T abilitySO = AssetDatabase.LoadAssetAtPath<T>(path);
        if (abilitySO == null)
        {
            abilitySO = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(abilitySO, path);
        }
        initializer?.Invoke(abilitySO);
        EditorUtility.SetDirty(abilitySO);
        AssetDatabase.SaveAssets();
        setSO.abilities.Add(abilitySO);
    }
}
