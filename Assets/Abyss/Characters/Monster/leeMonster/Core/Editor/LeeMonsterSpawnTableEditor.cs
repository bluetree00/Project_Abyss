using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// LeeMonsterSpawnTableSO 전용 커스텀 인스펙터.
///
/// "Auto-Populate" 버튼을 누르면 현재 어셈블리에서
/// LeeMonsterBase를 상속하면서 public const PrefabAddress 를 가진
/// 모든 구체 클래스를 스캔해 엔트리 행을 자동으로 추가한다.
/// displayName과 addressableKey가 모두 자동으로 채워진다.
///
/// 새 몬스터 추가 흐름:
///   1) MonsterName : LeeMonsterBase 작성
///   2) public const string PrefabAddress = "..." 추가 (Addressables에 등록된 키)
///   3) 프리팹 생성 후 Addressables에 해당 키로 등록
///   4) SpawnTableSO Inspector → Auto-Populate
/// </summary>
[CustomEditor(typeof(LeeMonsterSpawnTableSO))]
public class LeeMonsterSpawnTableEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox(
            "Auto-Populate: 어셈블리를 스캔해 LeeMonsterBase 서브클래스를 찾아 엔트리 행을 추가합니다.\n" +
            "PrefabAddress 상수가 addressableKey로 자동 입력됩니다.\n" +
            "이미 같은 이름의 엔트리가 있으면 중복 추가하지 않습니다.",
            MessageType.Info);

        if (GUILayout.Button("Auto-Populate (어셈블리 스캔)", GUILayout.Height(32)))
            AutoPopulate((LeeMonsterSpawnTableSO)target);
    }

    private static void AutoPopulate(LeeMonsterSpawnTableSO so)
    {
        var baseType = typeof(LeeMonsterBase);
        int added    = 0;

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            // 엔진/시스템 어셈블리 스킵
            string asmName = assembly.FullName;
            if (asmName.StartsWith("UnityEditor") ||
                asmName.StartsWith("Unity.")      ||
                asmName.StartsWith("System")      ||
                asmName.StartsWith("Mono.")       ||
                asmName.StartsWith("mscorlib"))
                continue;

            foreach (Type type in assembly.GetTypes())
            {
                if (type.IsAbstract)                  continue;
                if (!baseType.IsAssignableFrom(type)) continue;

                // public const string PrefabAddress 가 있는 클래스만 처리
                FieldInfo field = type.GetField(
                    "PrefabAddress",
                    BindingFlags.Public | BindingFlags.Static);

                if (field == null || field.FieldType != typeof(string)) continue;

                string className = type.Name;
                string address  = (string)field.GetValue(null);

                // 같은 displayName이 이미 있으면 스킵
                if (so.entries.Exists(e => e.displayName == className)) continue;

                so.entries.Add(new LeeSpawnEntry
                {
                    displayName    = className,
                    addressableKey = address,
                    weight         = 1f,
                    enabled        = true,
                });
                added++;
            }
        }

        if (added > 0)
        {
            EditorUtility.SetDirty(so);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SpawnTable] {added}개 몬스터 엔트리 행 추가. Addressables에 프리팹을 등록했는지 확인해주세요.");
        }
        else
        {
            Debug.Log("[SpawnTable] 새로 추가된 엔트리 없음 (이미 모두 등록됨).");
        }
    }
}
