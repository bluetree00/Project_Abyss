using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Slime/FairyBat/BlackKnight 프리팹의 커스텀 자식(NormalSlimeFSM 등)을
/// Polyart의 *PAMaskTint 프리팹으로 교체해 ElementNativePalette 색 주입을 적용.
///
/// Unity 메뉴: Tools/Abyss/Monster/Convert Stragglers to PAMaskTint
///
/// 처리 대상 3종:
///   Slime       → SlimePAMaskTint
///   FairyBat    → BatPAMaskTint
///   BlackKnight → BlackKnightPAMaskTint
/// </summary>
public static class ConvertMonsterToPAMaskTint
{
    private struct ConvertTask
    {
        public string MonsterName;
        public string ParentPrefabPath;
        public string NewChildPrefabPath;
    }

    private static readonly ConvertTask[] Tasks =
    {
        new() {
            MonsterName = "Slime",
            ParentPrefabPath = "Assets/Abyss/Characters/Monster/Monster/Slime/Prefab/Slime.prefab",
            NewChildPrefabPath = "Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave01/CharacterMaskTint/SlimePAMaskTint.prefab",
        },
        new() {
            MonsterName = "FairyBat",
            ParentPrefabPath = "Assets/Abyss/Characters/Monster/Monster/FairyBat/Prefab/FairyBat.prefab",
            NewChildPrefabPath = "Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave01/CharacterMaskTint/BatPAMaskTint.prefab",
        },
        new() {
            MonsterName = "BlackKnight",
            ParentPrefabPath = "Assets/Abyss/Characters/Monster/Monster/BlackKnight/Prefab/BlackKnight.prefab",
            NewChildPrefabPath = "Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave02/CharacterMaskTint/BlackKnightPAMaskTint.prefab",
        },
    };

    [MenuItem("Tools/Abyss/Monster/Convert Stragglers to PAMaskTint")]
    public static void ConvertAll()
    {
        if (!EditorUtility.DisplayDialog(
                "PAMaskTint 변환",
                "Slime / FairyBat / BlackKnight 3종의 자식 프리팹을 *PAMaskTint 로 교체합니다.\n" +
                "자식에 있던 MonsterAnimEventReceiver 는 자동으로 재부착됩니다.\n\n" +
                "계속하시겠습니까?",
                "진행", "취소"))
        {
            return;
        }

        int success = 0;
        int failed = 0;

        foreach (var task in Tasks)
        {
            if (ConvertOne(task))
                success++;
            else
                failed++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "완료",
            $"성공 {success}개 / 실패 {failed}개.\nConsole 로그에서 상세 내역 확인.",
            "OK");
    }

    private static bool ConvertOne(ConvertTask task)
    {
        var parent = AssetDatabase.LoadAssetAtPath<GameObject>(task.ParentPrefabPath);
        if (parent == null)
        {
            Debug.LogError($"[ConvertMonster] 부모 프리팹 없음: {task.ParentPrefabPath}");
            return false;
        }

        var newChildAsset = AssetDatabase.LoadAssetAtPath<GameObject>(task.NewChildPrefabPath);
        if (newChildAsset == null)
        {
            Debug.LogError($"[ConvertMonster] 새 자식 프리팹 없음: {task.NewChildPrefabPath}");
            return false;
        }

        // 프리팹을 isolated scene으로 로드해서 편집
        var contents = PrefabUtility.LoadPrefabContents(task.ParentPrefabPath);
        try
        {
            // 1. 기존 자식 제거 — MonsterBase 스크립트가 붙은 루트 자체는 남김
            // 루트의 자식 중 프리팹 인스턴스(nested prefab)를 찾아 제거
            var oldChildren = new List<Transform>();
            for (int i = 0; i < contents.transform.childCount; i++)
            {
                var c = contents.transform.GetChild(i);
                oldChildren.Add(c);
            }

            Vector3 oldPos = Vector3.zero;
            Quaternion oldRot = Quaternion.identity;
            Vector3 oldScale = Vector3.one;
            bool hadOld = oldChildren.Count > 0;

            if (hadOld)
            {
                var oldFirst = oldChildren[0];
                oldPos = oldFirst.localPosition;
                oldRot = oldFirst.localRotation;
                oldScale = oldFirst.localScale;
            }

            foreach (var c in oldChildren)
                Object.DestroyImmediate(c.gameObject, false);

            // 2. 새 PAMaskTint 프리팹을 자식으로 Instantiate
            var newInstance = (GameObject)PrefabUtility.InstantiatePrefab(newChildAsset, contents.transform);
            if (newInstance == null)
            {
                Debug.LogError($"[ConvertMonster] {task.MonsterName}: 새 자식 Instantiate 실패");
                return false;
            }

            newInstance.transform.localPosition = oldPos;
            newInstance.transform.localRotation = oldRot;
            newInstance.transform.localScale    = oldScale;

            // 3. MonsterAnimEventReceiver 재부착 — 자식 루트에 이미 있으면 skip, 없으면 AddComponent
            //    (MonsterBase 가 자식 Animator 에서 이벤트 수신 대상을 찾으므로 필수)
            var receiverType = System.Type.GetType("Abyss.Monster.LeeMonsterAnimEventReceiver") ??
                               System.Type.GetType("LeeMonsterAnimEventReceiver");
            if (receiverType != null && newInstance.GetComponent(receiverType) == null)
            {
                newInstance.AddComponent(receiverType);
            }

            // 4. 프리팹 저장
            PrefabUtility.SaveAsPrefabAsset(contents, task.ParentPrefabPath);
            Debug.Log($"[ConvertMonster] ✓ {task.MonsterName}: '{newChildAsset.name}' 로 교체 완료");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ConvertMonster] {task.MonsterName} 변환 실패: {e.Message}\n{e.StackTrace}");
            return false;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }
}
