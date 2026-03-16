using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// LeeMonsterSpawnTableSO 전용 커스텀 인스펙터.
///
/// "Auto-Populate" 버튼을 누르면 현재 어셈블리에서
/// LeeMonsterBase를 상속하면서 public const PrefabAddress 를 가진
/// 모든 구체 클래스를 스캔해 엔트리 행(displayName만)을 자동으로 추가한다.
/// 프리팹 슬롯은 이후 한 번만 채워주면 모든 스포너에 공유된다.
///
/// 새 몬스터 추가 흐름:
///   1) MonsterName : LeeMonsterBase 작성
///   2) public const string PrefabAddress = "..." 추가 (어셈블리 식별용)
///   3) 프리팹 생성
///   4) SpawnTableSO Inspector → Auto-Populate → 생긴 행에 프리팹 드래그
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
            "추가된 행의 Prefab 슬롯에 해당 몬스터 프리팹을 드래그해주세요.\n" +
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

                // 같은 displayName이 이미 있으면 스킵
                if (so.entries.Exists(e => e.displayName == className)) continue;

                so.entries.Add(new LeeSpawnEntry
                {
                    displayName = className,
                    prefab      = null,   // 프리팹은 사용자가 직접 할당
                    weight      = 1f,
                    enabled     = true,
                });
                added++;
            }
        }

        if (added > 0)
        {
            EditorUtility.SetDirty(so);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SpawnTable] {added}개 몬스터 엔트리 행 추가. 각 행의 Prefab 슬롯을 채워주세요.");
        }
        else
        {
            Debug.Log("[SpawnTable] 새로 추가된 엔트리 없음 (이미 모두 등록됨).");
        }
    }
}
