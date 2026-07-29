#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

/// <summary>
/// 게임플레이 VFX 프리팹을 Addressable에 일괄 등록하는 에디터 유틸.
/// 새 VFX 추가 시 Vfx 배열에 경로/주소 한 줄만 추가.
/// 메뉴: RelicFairy/VFX/Register VFX Addressables
/// </summary>
public static class RegisterVfxAddressables
{
    private static readonly (string path, string address)[] Vfx =
    {
        (
            "Assets/_ThirdParty/EffectSource/Hovl Studio/RPG VFX Bundle/Prefabs/Magic buffs and hits/Hyperdymension circle.prefab",
            "VFX_MonsterSpawn"
        ),
    };

    [MenuItem("RelicFairy/Addressables/Register VFX")]
    public static void Execute()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            UnityEngine.Debug.LogError("[RegisterVfx] Addressable Settings not found.");
            return;
        }

        var defaultGroup = settings.DefaultGroup;

        foreach (var (path, address) in Vfx)
        {
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
            {
                UnityEngine.Debug.LogWarning($"[RegisterVfx] 에셋 없음: {path}");
                continue;
            }

            var entry = settings.FindAssetEntry(guid);
            if (entry == null)
                entry = settings.CreateOrMoveEntry(guid, defaultGroup, readOnly: false, postEvent: false);

            entry.address = address;
            UnityEngine.Debug.Log($"[RegisterVfx] 등록: {address} → {path}");
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
        AssetDatabase.SaveAssets();
        UnityEngine.Debug.Log("[RegisterVfx] 완료.");
    }
}
#endif
