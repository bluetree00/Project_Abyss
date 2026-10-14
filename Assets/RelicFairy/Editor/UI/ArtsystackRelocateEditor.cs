#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-13 Artsystack UI 팩 정리(분석 문서 `구현설계/UI팩_Artsystack_분석·매핑_20261012.md` 0단계) —
/// 받은 팩이 <c>Assets/</c> 바로 아래에 <c>Resources</c> 폴더째 있어 빌드에 통째로(약 270MB) 실린다.
/// 규칙(받은 팩 = <c>_ThirdParty</c> · 쓰는 것만 <c>_Imported</c>)대로 <c>Assets/_ThirdParty/Artsystack</c>로 옮기고 <c>Resources</c>를 <c>Art</c>로 바꾼다.
/// AssetDatabase로 옮겨 GUID가 그대로라 나중에 쓰는 곳이 생겨도 참조가 깨지지 않는다. 로그 「[ArtsystackRelocate] 끝」.
/// </summary>
public static class ArtsystackRelocateEditor
{
    private const string From = "Assets/Artsystack - Fantasy RPG GUI";
    private const string To   = "Assets/_ThirdParty/Artsystack";

    [MenuItem("RelicFairy/Tools/10-13 Artsystack 팩 정리 — _ThirdParty로 옮기고 Resources 이름 바꾸기")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ArtsystackRelocate] 플레이 중엔 하지 않는다"); return; }

        if (AssetDatabase.IsValidFolder(From))
        {
            if (AssetDatabase.IsValidFolder(To)) { Debug.LogError($"[ArtsystackRelocate] {To}가 이미 있다 — 손으로 확인 필요"); return; }
            string err = AssetDatabase.MoveAsset(From, To);
            if (!string.IsNullOrEmpty(err)) { Debug.LogError($"[ArtsystackRelocate] 옮기기 실패: {err}"); return; }
        }
        else if (!AssetDatabase.IsValidFolder(To)) { Debug.LogError($"[ArtsystackRelocate] {From}도 {To}도 없다"); return; }

        string res = To + "/Resources";
        if (AssetDatabase.IsValidFolder(res))
        {
            string err = AssetDatabase.RenameAsset(res, "Art");
            if (!string.IsNullOrEmpty(err)) { Debug.LogError($"[ArtsystackRelocate] Resources 이름 바꾸기 실패: {err}"); return; }
        }
        AssetDatabase.Refresh();

        int left = AssetDatabase.FindAssets("", new[] { To + "/Art" }).Length;
        bool noRes = !AssetDatabase.IsValidFolder(res) && !AssetDatabase.IsValidFolder(From);
        Debug.Log($"[ArtsystackRelocate] 끝 — {To} · Art 안 에셋 {left}개 · Resources 없음 {noRes}");
    }
}
#endif
