#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q1 사용자 「길 자체와 입구가 대칭이 안 맞는다」(인트로 입구 · 챕터 3 보스 길).
/// 실측(「11-02 Q1 길 · 입구 대칭」): Arena_Boss_Ch3의 건축 묶음 「Meshes」(기둥 · 벽 · 아치 · 바닥 6,294개)가 프리팹 중심선(x = 0 —
/// 보스 자리 · 제단 · 의자 ±5 m · 카펫 · 위 창 ±20 m · 인트로 회랑)보다 +0.775 m 오른쪽에 있었다(좌우 벽 16.43 / 17.98 m · 기둥 8.81 / 10.36 m).
/// 「Meshes」 하나만 x −0.775 m 옮긴다. 인트로 변형(Arena_Boss_Ch3_Intro)은 이 묶음 위치를 덮어쓰지 않아 같이 맞는다. NavMesh는 둘 다 실행 중에 굽는다.
/// 결과: 로그 「[Q1Axis] 끝」.
/// </summary>
public static class Q1ArenaAxisFixEditor
{
    private const string PrefabPath = "Assets/RelicFairy/Systems/Stage/MapGen/Prefabs/Arena_Boss_Ch3.prefab";
    private const float  ShiftX     = -0.775f;
    private const float  ExpectedX  = -282.7f;   // 고치기 전 값 — 이미 옮겼으면 다시 옮기지 않는다

    [MenuItem("RelicFairy/Debug/11-02 Q1 챕터 3 아레나 건축 축 맞추기 (편집 모드, 1회)")]
    private static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[Q1Axis] 편집 모드에서"); return; }
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var meshes = root.transform.Find("Meshes");
            if (meshes == null) { Debug.LogWarning("[Q1Axis] 끝 — Meshes 없음(바꾸지 않음)"); return; }
            Vector3 p = meshes.localPosition;
            if (Mathf.Abs(p.x - ExpectedX) > 0.01f) { Debug.LogWarning($"[Q1Axis] 끝 — Meshes x {p.x:0.000}(예상 {ExpectedX}) · 바꾸지 않음"); return; }
            meshes.localPosition = new Vector3(p.x + ShiftX, p.y, p.z);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[Q1Axis] 끝 — Meshes x {p.x:0.000} → {meshes.localPosition.x:0.000}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
#endif
