#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

/// <summary>
/// 스테이지 맵 아이콘 스프라이트를 Addressable에 일괄 등록하는 에디터 유틸.
/// 메뉴: Abyss/Stage/Register Map Icon Addressables
/// </summary>
public static class RegisterStageIconsAddressable
{
    private static readonly (string path, string address)[] Icons =
    {
        ("Assets/Abyss/Prefabs/UI/Bamao/BamaoUIPack/Sprites/MAP/flag_1.png",     "flag_1"),
        ("Assets/Abyss/Prefabs/UI/Bamao/BamaoUIPack/Sprites/MAP/dragon.png",     "dragon"),
        ("Assets/Abyss/Prefabs/UI/Bamao/BamaoUIPack/Sprites/MAP/sword_1.png",    "sword_1"),
        ("Assets/Abyss/Prefabs/UI/Bamao/BamaoUIPack/Sprites/MAP/skeleton_1.png", "skeleton_1"),
        ("Assets/Abyss/Prefabs/UI/Bamao/BamaoUIPack/Sprites/MAP/symbol_1.png",   "symbol_1"),
        ("Assets/Abyss/Prefabs/UI/Bamao/BamaoUIPack/Sprites/MAP/chest_1.png",    "chest_1"),
    };

    [MenuItem("Abyss/Stage/Create StageNodeIconMap SO")]
    public static void CreateIconMapSO()
    {
        var so = UnityEngine.ScriptableObject.CreateInstance<StageNodeIconMap>();
        AssetDatabase.CreateAsset(so, "Assets/Abyss/UI/Stage/StageNodeIconMap.asset");
        AssetDatabase.SaveAssets();
        UnityEngine.Debug.Log("[RegisterStageIcons] StageNodeIconMap.asset 생성 완료.");
        Selection.activeObject = so;
    }

    [MenuItem("Abyss/Stage/Register Map Icon Addressables")]
    public static void Execute()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            UnityEngine.Debug.LogError("[RegisterStageIcons] Addressable Settings not found.");
            return;
        }

        var defaultGroup = settings.DefaultGroup;

        foreach (var (path, address) in Icons)
        {
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
            {
                UnityEngine.Debug.LogWarning($"[RegisterStageIcons] 에셋 없음: {path}");
                continue;
            }

            var entry = settings.FindAssetEntry(guid);
            if (entry == null)
                entry = settings.CreateOrMoveEntry(guid, defaultGroup, readOnly: false, postEvent: false);

            entry.address = address;
            UnityEngine.Debug.Log($"[RegisterStageIcons] 등록: {address} → {path}");
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
        AssetDatabase.SaveAssets();
        UnityEngine.Debug.Log("[RegisterStageIcons] 완료. 6개 아이콘 Addressable 등록.");
    }
}
#endif
