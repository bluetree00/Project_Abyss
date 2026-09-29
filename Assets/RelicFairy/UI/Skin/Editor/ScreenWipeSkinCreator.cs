using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>
/// 방 전환 와이프 스킨(<see cref="ScreenWipeSkinSO"/>)을 만들고 Addressables에 <see cref="UISkin.ScreenWipeAddress"/>로 등록한다.
/// 이미 있으면 텍스처·셰이더 참조만 다시 채운다(수치는 건드리지 않는다). 다른 스킨과 같은 그룹(Default Local Group).
/// </summary>
public static class ScreenWipeSkinCreator
{
    private const string Folder    = "Assets/RelicFairy/UI/Skin/ScreenWipe";
    private const string AssetPath = Folder + "/ScreenWipeSkin.asset";
    private const string ShaderPath = Folder + "/UIRoomWipe.shader";
    // 09-26 렌더 비교로 고름 — 잉크 가장자리는 큰 결의 Noise65, 마법진은 룬 글자 고리 MagicCircle23.
    private const string NoisePath = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio 1/HSFiles/Textures/Noise65.png";
    private const string SigilPath = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/HSFiles/Textures/MagicCircle23.png";

    [MenuItem("RelicFairy/UI/방 전환 와이프 스킨 만들기·등록")]
    private static void CreateOrUpdate()
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        var noise  = AssetDatabase.LoadAssetAtPath<Texture2D>(NoisePath);
        var sigil  = AssetDatabase.LoadAssetAtPath<Texture2D>(SigilPath);
        if (shader == null || noise == null || sigil == null)
        {
            Debug.LogError($"[ScreenWipeSkin] 재료 누락 — shader:{shader != null} noise:{noise != null} sigil:{sigil != null}");
            return;
        }

        var so = AssetDatabase.LoadAssetAtPath<ScreenWipeSkinSO>(AssetPath);
        if (so == null)
        {
            so = ScriptableObject.CreateInstance<ScreenWipeSkinSO>();
            AssetDatabase.CreateAsset(so, AssetPath);
        }
        var ser = new SerializedObject(so);
        ser.FindProperty("_shader").objectReferenceValue   = shader;
        ser.FindProperty("_inkNoise").objectReferenceValue = noise;
        ser.FindProperty("_sigil").objectReferenceValue    = sigil;
        ser.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(so);
        AssetDatabase.SaveAssets();

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogError("[ScreenWipeSkin] AddressableAssetSettings 없음"); return; }
        var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(AssetPath), settings.DefaultGroup, false, false);
        entry.address = UISkin.ScreenWipeAddress;
        settings.SetDirty(UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ScreenWipeSkin] {AssetPath} → '{entry.address}' ({settings.DefaultGroup.Name})");
    }
}
