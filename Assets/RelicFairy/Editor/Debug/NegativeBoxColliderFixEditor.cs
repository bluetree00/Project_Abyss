#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 「요청한 것 다 체크」 — 「BoxCollider does not support negative scale」 경고(14차 빌드 로그 937번 · 10-10 첫 제보).
/// 아레나 안에 거울로 뒤집어(음수 스케일) 놓은 촛대 · 계단 · 아치 벽 · 석상의 박스 판정이 원인 — 아레나를 읽을 때마다 하나씩 경고를 쓴다.
/// 고침: 그 박스 판정을 끄고, 부호를 되돌린 자식(@BoxCol · 스케일 = 부호)에 같은 판정을 옮긴다(가운데만 부호대로 — 상자라 모양 · 자리 그대로).
/// 층 · 태그 · 정적 표시 · 트리거 · 재질 · 켜짐을 그대로 옮긴다. 원본 소품 프리팹은 건드리지 않는다(아레나 안 덮어쓰기).
/// 결과: 로그 「[NegBox] 끝」.
/// </summary>
public static class NegativeBoxColliderFixEditor
{
    private static readonly string[] Prefabs =
    {
        "Assets/RelicFairy/Systems/Stage/MapGen/Prefabs/Arena_Boss_Ch1.prefab",
        "Assets/RelicFairy/Systems/Stage/MapGen/Prefabs/Arena_Boss_Ch2.prefab",
        "Assets/RelicFairy/Systems/Stage/MapGen/Prefabs/Arena_Boss_Ch3.prefab",
        "Assets/RelicFairy/Systems/Stage/MapGen/Prefabs/Arena_Boss_Ch4.prefab",
        "Assets/RelicFairy/Systems/Stage/MapGen/Prefabs/Plaza_Boss_Ch4.prefab",
        "Assets/RelicFairy/Systems/Stage/MapGen/Prefabs/Road_Boss_Ch1.prefab",
        "Assets/RelicFairy/Systems/Stage/MapGen/Prefabs/Road_Boss_Ch2.prefab",
        "Assets/RelicFairy/Systems/Stage/MapGen/Prefabs/Road_Boss_Ch3.prefab",
    };

    [MenuItem("RelicFairy/Debug/11-02 음수 스케일 박스 판정 고치기 (편집 모드, 1회)")]
    private static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[NegBox] 편집 모드에서"); return; }
        int total = 0;
        foreach (var path in Prefabs)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            int moved = 0;
            try
            {
                foreach (var bc in root.GetComponentsInChildren<BoxCollider>(true))
                {
                    if (!bc.enabled || bc.gameObject.name == "@BoxCol") continue;   // 이미 옮겼거나 원래 꺼진 것(다시 돌려도 겹치지 않게)
                    Vector3 ls = bc.transform.lossyScale;
                    if (ls.x >= 0f && ls.y >= 0f && ls.z >= 0f) continue;
                    var sign = new Vector3(Mathf.Sign(ls.x), Mathf.Sign(ls.y), Mathf.Sign(ls.z));
                    var src = bc.gameObject;

                    var child = new GameObject("@BoxCol");
                    child.layer = src.layer;
                    child.tag   = src.tag;
                    GameObjectUtility.SetStaticEditorFlags(child, GameObjectUtility.GetStaticEditorFlags(src));
                    child.transform.SetParent(src.transform, false);
                    child.transform.localScale = sign;

                    var nb = child.AddComponent<BoxCollider>();
                    nb.center         = Vector3.Scale(bc.center, sign);
                    nb.size           = bc.size;
                    nb.isTrigger      = bc.isTrigger;
                    nb.sharedMaterial = bc.sharedMaterial;
                    nb.enabled        = bc.enabled;
                    bc.enabled = false;   // 지우지 않고 끈다 — 중첩 프리팹 덮어쓰기 하나(켜짐)로 끝나고, 꺼진 박스 판정은 경고 · 충돌을 만들지 않는다
                    moved++;
                }
                if (moved > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log($"[NegBox] {System.IO.Path.GetFileNameWithoutExtension(path)} — 옮긴 박스 판정 {moved}");
            total += moved;
        }
        Debug.Log($"[NegBox] 끝 — 합계 {total}");
    }
}
#endif
