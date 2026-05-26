using RelicFairy.Monster;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// MonsterSpawnTableSO 전용 커스텀 인스펙터.
///
/// "Auto-Populate" 버튼을 누르면 현재 어셈블리에서
/// MonsterBase를 상속하면서 public const PrefabAddress 를 가진
/// 모든 구체 클래스를 스캔해 엔트리 행을 자동으로 추가한다.
/// displayName, addressableKey, nativeElement가 자동으로 채워진다.
///
/// nativeElement는 MonsterConfigSO 에셋(…/SO/<Name>Config.asset)을 AssetDatabase로
/// 찾아서 stat.nativeElement 값을 읽어 매칭한다.
///
/// 새 몬스터 추가 흐름:
///   1) MonsterName : MonsterBase 작성
///   2) public const string PrefabAddress = "..." 추가
///   3) 프리팹 + MonsterConfigSO 에셋 생성 후 Addressables 등록
///   4) SpawnTableSO Inspector → Auto-Populate
/// </summary>
[CustomEditor(typeof(MonsterSpawnTableSO))]
public class MonsterSpawnTableEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox(
            "Auto-Populate: 어셈블리 스캔으로 MonsterBase 서브클래스 엔트리 행을 추가/갱신합니다.\n" +
            "• PrefabAddress → addressableKey\n" +
            "• MonsterConfigSO.stat.nativeElement → nativeElement\n" +
            "• Resources/MONSTER_ELEMENT_STAT_DATA.json 의 monster_pool_tag → poolTags\n" +
            "이미 존재하는 엔트리는 nativeElement/poolTags만 최신값으로 갱신됩니다.",
            MessageType.Info);

        if (GUILayout.Button("Auto-Populate (어셈블리 스캔)", GUILayout.Height(32)))
            AutoPopulate((MonsterSpawnTableSO)target);
    }

    /// <summary>프로젝트의 모든 MonsterSpawnTableSO에 Auto-Populate를 일괄 실행.
    /// 새 grade 필드를 기존 테이블에 주입할 때 유용.
    /// 메뉴: RelicFairy/Monster/Auto-Populate All Spawn Tables</summary>
    [MenuItem("RelicFairy/Monster/Auto-Populate All Spawn Tables")]
    public static void AutoPopulateAll()
    {
        var guids = AssetDatabase.FindAssets("t:MonsterSpawnTableSO");
        int count = 0;
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<MonsterSpawnTableSO>(path);
            if (so == null) continue;
            AutoPopulate(so);
            count++;
        }
        Debug.Log($"[SpawnTable] Auto-Populate 일괄 실행 완료 ({count}개 테이블).");
    }

    private static void AutoPopulate(MonsterSpawnTableSO so)
    {
        var baseType = typeof(MonsterBase);
        int added    = 0;
        int updated  = 0;

        var configByName = BuildConfigIndex();
        var statById     = BuildStatIndex(); // monster_id → pool tags

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

                string className   = type.Name;
                // 클래스 이름의 "Monster" 접미사 제거 (SlimeMonster → Slime) —
                // Config 파일명/JSON monster_id 와 매칭하기 위함.
                string lookupKey   = className.EndsWith("Monster")
                    ? className.Substring(0, className.Length - "Monster".Length)
                    : className;
                string address     = (string)field.GetValue(null);
                ElementType native = LookupNativeElement(configByName, lookupKey);
                MonsterGrade grade = LookupGrade(configByName, lookupKey);
                int[] poolTags     = LookupPoolTags(statById, lookupKey);

                var existing = so.entries.Find(e => e.displayName == className);
                if (existing != null)
                {
                    // 기존 엔트리 — nativeElement, grade, poolTags만 최신값으로 덮어씀 (weight/enabled 등 유지)
                    bool changed = false;
                    if (existing.nativeElement != native) { existing.nativeElement = native; changed = true; }
                    if (existing.grade != grade) { existing.grade = grade; changed = true; }
                    if (!AreEqual(existing.poolTags, poolTags)) { existing.poolTags = poolTags; changed = true; }
                    if (changed) updated++;
                    continue;
                }

                so.entries.Add(new SpawnEntry
                {
                    displayName    = className,
                    addressableKey = address,
                    nativeElement  = native,
                    grade          = grade,
                    poolTags       = poolTags,
                    weight         = 1f,
                    enabled        = true,
                });
                added++;
            }
        }

        if (added > 0 || updated > 0)
        {
            EditorUtility.SetDirty(so);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SpawnTable] 추가 {added}개 / nativeElement 갱신 {updated}개.");
        }
        else
        {
            Debug.Log("[SpawnTable] 변경 없음 (이미 모두 최신).");
        }
    }

    /// <summary>프로젝트 내 모든 MonsterConfigSO를 스캔하여 "클래스명 → Config" 맵 생성.
    /// 파일명이 "{ClassName}Config.asset" 규칙을 따른다고 가정.</summary>
    private static Dictionary<string, MonsterConfigSO> BuildConfigIndex()
    {
        var map = new Dictionary<string, MonsterConfigSO>();
        var guids = AssetDatabase.FindAssets("t:MonsterConfigSO");

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var cfg  = AssetDatabase.LoadAssetAtPath<MonsterConfigSO>(path);
            if (cfg == null) continue;

            string fileName = Path.GetFileNameWithoutExtension(path);
            if (fileName.EndsWith("Config"))
                fileName = fileName.Substring(0, fileName.Length - "Config".Length);

            map[fileName] = cfg;
        }

        return map;
    }

    private static ElementType LookupNativeElement(Dictionary<string, MonsterConfigSO> index, string className)
    {
        if (index.TryGetValue(className, out var cfg) && cfg != null)
            return cfg.stat.nativeElement;
        return ElementType.None;
    }

    private static MonsterGrade LookupGrade(Dictionary<string, MonsterConfigSO> index, string className)
    {
        if (index.TryGetValue(className, out var cfg) && cfg != null)
            return cfg.grade;
        return MonsterGrade.Common;
    }

    /// <summary>Resources/MONSTER_ELEMENT_STAT_DATA.json 을 로드하여 monster_id → PoolTags 맵 생성.
    /// 파일이 없거나 파싱 실패 시 빈 맵 반환.</summary>
    private static Dictionary<string, int[]> BuildStatIndex()
    {
        var map = new Dictionary<string, int[]>();
        var textAsset = Resources.Load<TextAsset>("MONSTER_ELEMENT_STAT_DATA");
        if (textAsset == null)
        {
            Debug.LogWarning("[SpawnTable] MONSTER_ELEMENT_STAT_DATA.json 을 찾지 못해 poolTags를 채우지 못했습니다.");
            return map;
        }

        try
        {
            var col = JsonUtility.FromJson<MonsterElementStatEntryCollection>(textAsset.text);
            if (col?.monsters == null) return map;

            foreach (var m in col.monsters)
            {
                if (string.IsNullOrEmpty(m?.monster_id)) continue;
                map[m.monster_id] = m.PoolTags; // Entry 내부 파싱 프로퍼티 사용
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SpawnTable] MONSTER_ELEMENT_STAT_DATA.json 파싱 실패: {e.Message}");
        }

        return map;
    }

    private static int[] LookupPoolTags(Dictionary<string, int[]> index, string className)
    {
        // className(예: "Slime") = monster_id 규칙으로 매칭
        return index.TryGetValue(className, out var tags) && tags != null
            ? tags
            : Array.Empty<int>();
    }

    private static bool AreEqual(int[] a, int[] b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.SequenceEqual(b);
    }
}
