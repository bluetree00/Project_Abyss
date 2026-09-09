#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 무기별 로코모션 클립을 어드레서블에 일괄 등록한다.
/// 메뉴: RelicFairy/Animation/Register Locomotion Addressables
///
/// 무기 하나당 17개(Idle + 걷기 8 + 달리기 8)라 손으로 넣으면 오타 하나에 그 방향만 조용히
/// 비어 버린다. 주소는 <b>클립 이름 그대로</b> 쓴다 — WeaponAnimationSetSO의 addressableKey와
/// 1:1로 맞춰 헷갈릴 여지를 없앤다.
/// </summary>
public static class LocomotionAddressableRegistrar
{
    private const string Apose = "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/APose";
    private const string Bow   = "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/Bow/Movement/Inplace";

    /// <summary>8방향 접미사. 세트마다 명명이 달라 접두/접미를 따로 준다.</summary>
    private static readonly string[] Dirs = { "F", "FL", "FR", "L", "R", "B", "BL", "BR" };

    [MenuItem("RelicFairy/Animation/Register Locomotion Addressables")]
    public static void Register()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogError("[로코모션 등록] AddressableAssetSettings 없음"); return; }

        var group = settings.FindGroup("WeaponAnimation") ?? settings.DefaultGroup;
        var paths = new List<string>();

        // 검(APose) — F만 _Loop 접미가 붙는다(팩 명명 규약 불일치).
        paths.Add($"{Apose}/GhostSamurai_APose_Idle.FBX");
        foreach (var d in Dirs)
            paths.Add($"{Apose}/Movement/Inplace/GhostSamurai_APose_Strafe_Walk_{(d == "F" ? "F_Loop" : d)}_Inplace.FBX");
        foreach (var d in Dirs)
            paths.Add($"{Apose}/Movement/Inplace/GhostSamurai_APose_Strafe_Run_{(d == "F" ? "F_Loop" : d)}_Inplace.FBX");

        int ok = 0, miss = 0;
        foreach (var p in paths)
        {
            string guid = AssetDatabase.AssetPathToGUID(p);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogWarning($"[로코모션 등록] 에셋 없음: {p}");
                miss++;
                continue;
            }

            // 주소 = 클립 이름. FBX 파일명에서 접두(GhostSamurai_)와 접미(_Inplace)를 벗긴다.
            string addr = System.IO.Path.GetFileNameWithoutExtension(p)
                                .Replace("GhostSamurai_", "").Replace("_Inplace", "");

            var entry = settings.CreateOrMoveEntry(guid, group, false, false);
            entry.address = addr;
            ok++;
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[로코모션 등록] 완료 — 등록 {ok} · 누락 {miss} (그룹 '{group.Name}')");
    }
}
#endif
