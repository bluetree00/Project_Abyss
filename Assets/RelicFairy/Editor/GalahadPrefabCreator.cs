#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 갈라하드 프리팹 생성 에디터 유틸리티.
/// 상단 메뉴 RelicFairy → 캐릭터 → 갈라하드 프리팹 생성 으로 실행.
///
/// M02 Castle Guard 원본 프리팹 기반 Prefab Variant + Galahad.cs 부착.
/// </summary>
public static class GalahadPrefabCreator
{
    private const string BasePath = "Assets/_ThirdParty/Suriyun/Characters/Castle Guard/Prefab/M02.prefab";
    private const string SavePath = "Assets/RelicFairy/Characters/Player/Galahad/Prefabs/Galahad.prefab";

    [MenuItem("RelicFairy/Character/갈라하드 프리팹 생성")]
    public static void CreateGalahadPrefab()
    {
        var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePath);
        if (basePrefab == null)
        {
            Debug.LogError($"[GalahadCreator] 베이스 프리팹 없음: {BasePath}");
            return;
        }

        // 저장 폴더 생성
        string dir = Path.GetDirectoryName(SavePath).Replace("\\", "/");
        if (!AssetDatabase.IsValidFolder(dir))
        {
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
        }

        // Prefab Variant 인스턴스 생성
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        instance.name = "Galahad";

        // Galahad 스크립트 부착
        if (!instance.TryGetComponent<Galahad>(out _))
            instance.AddComponent<Galahad>();

        // 저장
        PrefabUtility.SaveAsPrefabAsset(instance, SavePath, out bool ok);
        Object.DestroyImmediate(instance);

        if (ok)
        {
            AssetDatabase.Refresh();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(SavePath);
            Debug.Log($"[GalahadCreator] 완료 → {SavePath}");
        }
        else
        {
            Debug.LogError("[GalahadCreator] 프리팹 저장 실패");
        }
    }
}
#endif
