using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 보스 클리어 출구 계단 리소스 묶음(<see cref="StairwaySetSO"/>)을 만들고 Addressables에 올린다(다시 눌러도 같은 결과).
/// 고른 기준(10-10 렌더 비교 — 바탕화면 기획/보스클리어_계단_후보_20261010/): 한 단씩 솟는 연출에 맞는 돌 디딤판 한 장짜리 ·
/// URP에서 바로 그려지는 Gothic 묶음(Ch3 아레나가 이미 씀) · 꼭대기 문은 혼자 서는 돌 아치 · 마법진은 대기방 도착 마법진과 같은 짝.
/// </summary>
public static class StairwaySetBuilder
{
    private const string SetPath   = "Assets/RelicFairy/Systems/Stage/RunGame/Fx/StairwaySet.asset";
    private const string GroupName = "Effects";
    private const string Goth      = "Assets/RelicFairy/_Imported/Gothic_Interior/Environment/Asset/Prefabs/";
    private const string Hovl      = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/Magic circles/Prefabs/Loop version/";

    private static readonly string[] Slabs =
        { Goth + "SM_StairStoneKit_02a.prefab", Goth + "SM_StairStoneKit_02b.prefab", Goth + "SM_StairStoneKit_02c.prefab" };
    private const string Arch = Goth + "SM_StoneArch_01a.prefab";
    private const float  ArchScale = 1.2f;
    private static readonly string[] Circles =   // Ch1~4 — DungeonSpaceSetCreatorEditor 도착 마법진과 같다
    {
        Hovl + "Magic circle forest archer loop.prefab", Hovl + "Magic circle fire loop.prefab",
        Hovl + "Magic circle sun loop.prefab",           Hovl + "Magic circle suns loop.prefab",
    };

    [MenuItem("RelicFairy/Setup/10-10 보스 클리어 계단 세트 만들기")]
    public static void Build()
    {
        var set = AssetDatabase.LoadAssetAtPath<StairwaySetSO>(SetPath);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<StairwaySetSO>();
            AssetDatabase.CreateAsset(set, SetPath);
        }

        var slabs = new GameObject[Slabs.Length];
        for (int i = 0; i < Slabs.Length; i++) slabs[i] = Load(Slabs[i]);
        var circles = new GameObject[Circles.Length];
        for (int i = 0; i < Circles.Length; i++) circles[i] = Load(Circles[i]);
        set.EditorAssign(slabs, Load(Arch), circles, ArchScale);
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogError("[계단 세트] AddressableAssetSettings 없음"); return; }
        var group = settings.FindGroup(GroupName) ?? settings.DefaultGroup;
        var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(SetPath), group, false, false);
        entry.address = StairwaySetSO.Address;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        AssetDatabase.SaveAssets();

        int ok = 0;
        foreach (var s in slabs) if (s != null) ok++;
        Debug.Log($"[계단 세트] 끝 — 디딤판 {ok}/{Slabs.Length} · 아치 {(set.Arch != null)} · 마법진 {System.Array.FindAll(circles, c => c != null).Length}/4 · Addressables {group.Name} / {StairwaySetSO.Address}");
    }

    private static GameObject Load(string path)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null) Debug.LogWarning($"[계단 세트] 프리팹 없음 — {path}");
        return go;
    }
}
