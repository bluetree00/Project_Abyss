#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 대검 지상 3연타 = Grruzam 대검 팩 「M_Big_Sword@Attack_7Combo_1 · 2 · 3」(사용자 결정 10-07).
/// 06-30 새벽 이 연결을 했다가 40분 뒤 사무라이 Attack02_2/3/4(카타나와 같은 묶음)로 되돌려져 있었다.
/// 주소 GreatswordAttack_01~03을 7연속 콤보로 옮기고, 사무라이 클립은 06-25 당시 키 GS_Attack02_2~4로 남긴다(지우지 않는다).
/// 슬래시 이펙트 회전(Z)도 새 클립의 칼끝 방향에 맞춘다 — 실측 8(10-07, 몸 기준 칼끝 이동 각 · 2회 평균):
/// 1타 −132° · 2타 +8° · 3타 −86°. 슬래시 프리팹은 회전 0°에서 오른쪽 → 왼쪽으로 쓴다(실측 8b) → 회전 = 칼끝 각 − 180°.
/// (무형검의 기존 값 30° · 180° · 180°가 같은 규칙으로 측정과 맞아 규칙을 확인했다.)
/// </summary>
public static class GreatswordComboClips
{
    private const string Grruzam = "Assets/RelicFairy/_Imported/Grruzam Powerful Sword Animation(Great Sword, Katana)/Animation/M_Big_Sword/2_Attacks/2__7Combos/";
    private const string Samurai = "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/";
    private const string Ability = "Assets/RelicFairy/Weapon/Greatsword/Data/GreatswordBase/GreatswordAbility.asset";
    private static readonly float[] SlashRoll = { 48f, -172f, 95f };   // 단계 0 · 1 · 2

    [MenuItem("RelicFairy/Weapon/Greatsword Ground Combo = Grruzam 7Combo 1~3")]
    public static void Apply()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var group = settings.FindGroup("WeaponAnimation") ?? settings.DefaultGroup;
        for (int i = 0; i < 3; i++)
        {
            string key = $"GreatswordAttack_0{i + 1}";
            Readdress(settings, group, Samurai + $"GhostSamurai_APose_Attack02_{i + 2}_Inplace.FBX", $"GS_Attack02_{i + 2}");
            Readdress(settings, group, Grruzam + $"M_Big_Sword@Attack_7Combo_{i + 1}.FBX", key);
        }
        SetSlashRolls();
        AssetDatabase.SaveAssets();
        Debug.Log("[GreatswordComboClips] 대검 지상 3연타 = 7Combo 1~3 · 사무라이 = GS_Attack02_2~4 · 슬래시 회전 48 / −172 / 95");
    }

    private static void SetSlashRolls()
    {
        var so = AssetDatabase.LoadAssetAtPath<WeaponAbilitySO>(Ability);
        if (so == null) { Debug.LogWarning("[GreatswordComboClips] 어빌리티 없음: " + Ability); return; }
        var o = new SerializedObject(so);
        var steps = o.FindProperty("steps");
        for (int i = 0; i < steps.arraySize; i++)
        {
            var s = steps.GetArrayElementAtIndex(i);
            int idx = s.FindPropertyRelative("stepIndex").intValue;
            if (idx < 0 || idx >= SlashRoll.Length) continue;
            s.FindPropertyRelative("effect.rotationEuler").vector3Value = new Vector3(0f, 0f, SlashRoll[idx]);
        }
        o.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(so);
    }

    private static void Readdress(AddressableAssetSettings settings, AddressableAssetGroup group, string path, string address)
    {
        string guid = AssetDatabase.AssetPathToGUID(path);
        if (string.IsNullOrEmpty(guid)) { Debug.LogWarning("[GreatswordComboClips] 없음: " + path); return; }
        var entry = settings.FindAssetEntry(guid) ?? settings.CreateOrMoveEntry(guid, group, false, false);
        if (entry.address == address) return;
        Debug.Log($"[GreatswordComboClips] {System.IO.Path.GetFileName(path)}: 「{entry.address}」 → 「{address}」");
        entry.address = address;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
    }
}
#endif
