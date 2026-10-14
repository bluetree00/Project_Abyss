#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 장비 스킬 소리(10-08 A6) — 소리 3개를 주소에 올리고 효과음 표(SoundEventTable)에 이벤트 3줄을 더하거나 고친다.
/// 표 전체의 정본은 PlayerResourceHookupEditor(같은 줄이 들어 있다 — 그 메뉴를 다시 눌러도 남는다). 여기선 다른 줄을 건드리지 않는다.
/// 메뉴: RelicFairy/Setup/Register Skill Sounds
/// </summary>
public static class SkillSoundSetup
{
    private const string TablePath = "Assets/RelicFairy/Systems/Sound/SoundEventTable.asset";
    private const string SoundGroup = "Default Local Group";
    private const string HovlSfx = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/HSFiles/Sounds/";

    private static readonly (string evt, string address, string clip, float volume, float jitter)[] Rows =
    {
        (SoundEvent.SkillCharge,      "sfx_skill_charge",       HovlSfx + "Skill 3.wav",  0.45f, 0.04f),
        (SoundEvent.SkillBuff,        "sfx_skill_buff",         HovlSfx + "Skill 1.wav",  0.80f, 0.05f),
        (SoundEvent.SkillHeavyImpact, "sfx_skill_heavy_impact", HovlSfx + "Skill 10.wav", 0.55f, 0.04f),
    };

    [MenuItem("RelicFairy/Setup/Register Skill Sounds")]
    public static void Register()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var group = settings != null ? settings.FindGroup(SoundGroup) : null;
        var table = AssetDatabase.LoadAssetAtPath<SoundEventTableSO>(TablePath);
        if (group == null || table == null) { Debug.LogError("[스킬 소리] 주소 그룹 또는 효과음 표 없음"); return; }

        var so = new SerializedObject(table);
        var entries = so.FindProperty("_entries");
        foreach (var (evt, address, clip, volume, jitter) in Rows)
        {
            string guid = AssetDatabase.AssetPathToGUID(clip);
            if (string.IsNullOrEmpty(guid)) { Debug.LogWarning($"[스킬 소리] 소리 없음 — {clip}"); continue; }
            var entry = settings.FindAssetEntry(guid);
            if (entry != null && entry.address != address) { Debug.LogWarning($"[스킬 소리] 이미 다른 주소 「{entry.address}」 — {clip} (건너뜀)"); continue; }
            entry ??= settings.CreateOrMoveEntry(guid, group, false, false);
            entry.address = address;

            int idx = -1;
            for (int i = 0; i < entries.arraySize; i++)
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("eventId").stringValue == evt) { idx = i; break; }
            if (idx < 0) { idx = entries.arraySize; entries.arraySize++; }
            var e = entries.GetArrayElementAtIndex(idx);
            e.FindPropertyRelative("eventId").stringValue    = evt;
            e.FindPropertyRelative("sfxKey").stringValue     = address;
            e.FindPropertyRelative("volume").floatValue      = volume;
            e.FindPropertyRelative("pitchJitter").floatValue = jitter;
            Debug.Log($"[스킬 소리] {evt} → {address} ({System.IO.Path.GetFileName(clip)}) 볼륨 {volume}");
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(table);
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[스킬 소리] 완료 — 표 {entries.arraySize}줄");
    }
}
#endif
