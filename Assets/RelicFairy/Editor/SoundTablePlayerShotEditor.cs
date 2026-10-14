#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-12 사운드 점검표 4-1 P1 — 표의 <c>player_shot</c> 줄 소리 키를 활 발사(Arrow &amp; Bow 2-3 = <c>sfx_player_shot_bow</c>)로 바꾼다.
/// 다른 줄은 건드리지 않는다. 표를 통째로 덮는 메뉴 둘(플레이어 리소스 연결 · CSV 생성)은 쓰지 않는다 — 83 UI 소리 91줄이 사라진다.
/// 주소 <c>sfx_player_shot_bow</c>는 83 반입 도구(②b)가 등록한다. 없으면 바꾸지 않고 알린다.
/// </summary>
public static class SoundTablePlayerShotEditor
{
    private const string TablePath = "Assets/RelicFairy/Systems/Sound/SoundEventTable.asset";
    private const string EventId   = "player_shot";
    private const string NewKey    = "sfx_player_shot_bow";

    [MenuItem("RelicFairy/Setup/사운드 표 — player_shot = 활 발사 (10-12 P1)")]
    private static void Apply()
    {
        var table = AssetDatabase.LoadAssetAtPath<SoundEventTableSO>(TablePath);
        if (table == null) { Debug.LogWarning($"[사운드 표 P1] 표가 없다: {TablePath}"); return; }
        if (!HasAddress(NewKey)) { Debug.LogWarning($"[사운드 표 P1] 주소 {NewKey}가 Addressables에 없다 — 83 반입 뒤 다시"); return; }

        var o = new SerializedObject(table);
        var arr = o.FindProperty("_entries");
        int rows = arr.arraySize, hit = 0;
        for (int i = 0; i < rows; i++)
        {
            var e = arr.GetArrayElementAtIndex(i);
            if (e.FindPropertyRelative("eventId").stringValue != EventId) continue;
            var key = e.FindPropertyRelative("sfxKey");
            Debug.Log($"[사운드 표 P1] {EventId}: {key.stringValue} → {NewKey}");
            key.stringValue = NewKey;
            hit++;
        }
        if (hit == 0) { Debug.LogWarning($"[사운드 표 P1] {EventId} 줄이 없다(표 {rows}줄)"); return; }
        o.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(table);
        AssetDatabase.SaveAssets();
        Debug.Log($"[사운드 표 P1] 끝 — 표 {rows}줄 그대로 · {EventId} {hit}줄 바꿈");
    }

    private static bool HasAddress(string address)
    {
        var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return false;
        foreach (var g in settings.groups)
        {
            if (g == null) continue;
            foreach (var en in g.entries) if (en.address == address) return true;
        }
        return false;
    }
}
#endif
