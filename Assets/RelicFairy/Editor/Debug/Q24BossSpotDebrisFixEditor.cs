#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q24 사용자 「모드레드 자리 타일이 가까이 보면 깨진다」(사진 09 — 챕터 3 보스 등장 클로즈업).
/// 원인: 보스 자리 바로 뒤(1.75 m) 바닥에 박힌 돌 잔해 하나(SM_StoneDebris_01a25_217 · 바닥 14 cm 아래까지)가 낮은 클로즈업에서 깨진 바닥처럼 보였다
/// (실측 메뉴 「11-02 Q24 챕터 3 보스 등장 클로즈업」 — 끄면 바닥이 깨끗). Arena_Boss_Ch3에서 보스 생성 자리 3 m 안 돌 잔해를 끈다(지우지 않음 — 켜면 되돌림).
/// 결과: 로그 「[Q24Fix] 끝」.
/// </summary>
public static class Q24BossSpotDebrisFixEditor
{
    private const string PrefabPath = "Assets/RelicFairy/Systems/Stage/MapGen/Prefabs/Arena_Boss_Ch3.prefab";
    private const float  ClearRadius = 3f;

    [MenuItem("RelicFairy/Debug/11-02 Q24 챕터 3 보스 자리 잔해 끄기 (편집 모드, 1회)")]
    private static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[Q24Fix] 편집 모드에서"); return; }
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform spawn = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "BossSpawner") { spawn = t; break; }
            if (spawn == null) { Debug.LogWarning("[Q24Fix] 끝 — BossSpawner 없음(바꾸지 않음)"); return; }

            int off = 0;
            foreach (var lod in root.GetComponentsInChildren<LODGroup>(true))
            {
                var go = lod.gameObject;
                if (!go.name.StartsWith("SM_StoneDebris") || !go.activeSelf) continue;
                Vector3 d = go.transform.position - spawn.position;
                d.y = 0f;
                if (d.magnitude >= ClearRadius) continue;
                go.SetActive(false);
                off++;
                Debug.Log($"[Q24Fix] 끔 — {go.name} · 보스 자리에서 {d.magnitude:0.00} m");
            }
            if (off > 0) PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[Q24Fix] 끝 — 끈 잔해 {off}개 · 보스 자리 {spawn.position}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
#endif
