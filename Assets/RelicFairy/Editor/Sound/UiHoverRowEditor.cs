#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// [1회 · 편집 모드] 10-29 효과음 표(SoundEventTable)에 <c>ui_hover</c> 줄을 더한다 — 상수(<see cref="SoundEvent.UiHover"/>)를 부르는 곳
/// (재련소 대장장이 말소리 · 근접 증강 뒤집기 · 로비 버튼 올림)이 표에 줄이 없어 소리 없이 지나갔다.
/// 소리는 놀이 카운트다운과 같은 짧은 틱(sfx_ui_tick)을 작게 · 살짝 흔들어 쓴다.
/// 같은 eventId가 이미 있으면 아무것도 쓰지 않는다(다시 돌려도 안전) — 다른 줄은 건드리지 않는다(FantasyUiSfxImporter와 같은 결).
/// </summary>
public static class UiHoverRowEditor
{
    // ── Constants ────────────────────────────────────────────────
    private const string Tag       = "[UiHoverRow]";
    private const string TablePath = "Assets/RelicFairy/Systems/Sound/SoundEventTable.asset";
    private const string SfxKey    = "sfx_ui_tick";
    private const float  Volume    = 0.2f;
    private const float  Jitter    = 0.05f;

    // ── Private Methods ──────────────────────────────────────────
    [MenuItem("RelicFairy/Sound/10-29 효과음 표 — ui_hover 줄 더하기 (Edit)")]
    private static void Run()
    {
        var table = AssetDatabase.LoadAssetAtPath<SoundEventTableSO>(TablePath);
        if (table == null) { Debug.LogWarning($"{Tag} 표가 없다 — {TablePath}"); return; }

        var so      = new SerializedObject(table);
        var entries = so.FindProperty("_entries");
        for (int i = 0; i < entries.arraySize; i++)
        {
            if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("eventId").stringValue != SoundEvent.UiHover) continue;
            Debug.Log($"{Tag} 이미 있음 — 그대로 둔다(전체 {entries.arraySize}줄)");
            return;
        }

        int idx = entries.arraySize;
        entries.arraySize++;
        var e = entries.GetArrayElementAtIndex(idx);
        e.FindPropertyRelative("eventId").stringValue    = SoundEvent.UiHover;
        e.FindPropertyRelative("sfxKey").stringValue     = SfxKey;
        e.FindPropertyRelative("volume").floatValue      = Volume;
        e.FindPropertyRelative("pitchJitter").floatValue = Jitter;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(table);
        AssetDatabase.SaveAssets();
        Debug.Log($"{Tag} 더함 — {SoundEvent.UiHover} → {SfxKey} 볼륨 {Volume:0.00} · 흔들림 {Jitter:0.00}(전체 {entries.arraySize}줄)");
    }
}
#endif
