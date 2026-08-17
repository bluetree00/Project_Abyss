#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// SoundManager가 기대하는 구조 그대로 GameAudioMixer.mixer를 만들고 Addressables에 등록한다.
///
/// <para>그룹은 Master 하위에 BGM/SFX/UI, 노출 파라미터는 MasterVolume/BgmVolume/SfxVolume/UiVolume.
/// 이름은 <see cref="SoundManager"/>의 kMixerParam*/kMixerGroup* 상수와 정확히 일치해야 한다 —
/// 어긋나면 SetFloat/FindMatchingGroups가 조용히 실패하고 폴백으로 되돌아간다.</para>
///
/// <para>믹서 그룹 생성·파라미터 노출 API(<c>UnityEditor.Audio.AudioMixerController</c> 등)는 에디터
/// 내부 타입이라 직접 참조하면 컴파일되지 않는다. 그래서 리플렉션으로 호출한다. 한 번 만들고 나면
/// 이 스크립트를 다시 돌릴 일은 없다(재생성은 노출 파라미터 GUID가 바뀌므로 믹서를 참조하는
/// 스냅샷/외부 설정이 있으면 주의).</para>
///
/// 메뉴: RelicFairy/Audio/Generate Game Audio Mixer
/// </summary>
public static class GameAudioMixerGenerator
{
    private const string MixerPath   = "Assets/RelicFairy/Systems/Sound/GameAudioMixer.mixer";
    private const string AddressKey  = "GameAudioMixer";

    // SoundManager의 kMixerGroup* / kMixerParam* 와 1:1 대응.
    private static readonly string[] ChildGroups = { "BGM", "SFX", "UI" };
    private static readonly string[] ParamNames  = { "MasterVolume", "BgmVolume", "SfxVolume", "UiVolume" };

    [MenuItem("RelicFairy/Audio/Generate Game Audio Mixer")]
    public static void Generate()
    {
        var editorAsm = typeof(UnityEditor.Editor).Assembly;
        var ctrlType  = editorAsm.GetType("UnityEditor.Audio.AudioMixerController", throwOnError: true);
        var groupType = editorAsm.GetType("UnityEditor.Audio.AudioMixerGroupController", throwOnError: true);
        var pathType  = editorAsm.GetType("UnityEditor.Audio.AudioGroupParameterPath", throwOnError: true);

        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic
                                 | BindingFlags.Instance | BindingFlags.Static;

        // 재생성 시 이전 에셋을 먼저 지운다 — CreateMixerControllerAtPath는 덮어쓰지 않는다.
        if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(MixerPath)))
            AssetDatabase.DeleteAsset(MixerPath);

        var controller = ctrlType
            .GetMethod("CreateMixerControllerAtPath", Any, null, new[] { typeof(string) }, null)
            .Invoke(null, new object[] { MixerPath });

        if (controller == null)
        {
            Debug.LogError($"[GameAudioMixer] 믹서 생성 실패: {MixerPath}");
            return;
        }

        var master = ctrlType.GetProperty("masterGroup", Any).GetValue(controller);

        var createGroup = ctrlType.GetMethod("CreateNewGroup", Any);
        var addChild    = ctrlType.GetMethod("AddChildToParent", Any);
        var addExposed  = ctrlType.GetMethod("AddExposedParameter", Any);
        var guidForVol  = groupType.GetMethod("GetGUIDForVolume", Any);

        // 볼륨 파라미터 GUID → 우리가 쓸 이름. exposedParameters는 노출한 순서로 담기지 않으므로
        // (Master와 BGM이 뒤바뀌어 들어간다) 인덱스가 아니라 이 GUID로 짝을 찾아 이름을 붙인다.
        var nameByGuid = new System.Collections.Generic.Dictionary<string, string>();

        nameByGuid[ExposeVolume(controller, master, pathType, addExposed, guidForVol)] = ParamNames[0];

        for (int i = 0; i < ChildGroups.Length; i++)
        {
            // AddGroupToCurrentView는 갓 만든 컨트롤러에선 뷰 배열이 비어 IndexOutOfRange가 난다.
            // 뷰 등록은 믹서 창이 열릴 때 SanitizeGroupViews가 알아서 하므로 생략한다.
            var group = createGroup.Invoke(controller, new object[] { ChildGroups[i], false });
            addChild.Invoke(controller, new[] { group, master });
            nameByGuid[ExposeVolume(controller, group, pathType, addExposed, guidForVol)] = ParamNames[i + 1];
        }

        RenameExposedParameters(controller, ctrlType, Any, nameByGuid);

        EditorUtility.SetDirty((UnityEngine.Object)controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(MixerPath, ImportAssetOptions.ForceUpdate);

        RegisterAddressable();

        Debug.Log($"[GameAudioMixer] 생성 완료: {MixerPath} " +
                  $"(groups=Master/{string.Join(",Master/", ChildGroups)}, params={string.Join(",", ParamNames)})");
    }

    /// <summary>그룹의 볼륨을 노출하고, 그 볼륨 파라미터 GUID를 문자열로 반환한다.</summary>
    private static string ExposeVolume(object controller, object group, Type pathType,
                                       MethodInfo addExposed, MethodInfo guidForVol)
    {
        var guid = guidForVol.Invoke(group, null);
        var path = Activator.CreateInstance(pathType, new[] { group, guid });
        addExposed.Invoke(controller, new[] { path });
        return guid.ToString();
    }

    /// <summary>노출 파라미터는 자동 생성 이름으로 붙는다. SoundManager가 SetFloat하는 이름으로 바꾼다.</summary>
    private static void RenameExposedParameters(object controller, Type ctrlType, BindingFlags any,
                                                System.Collections.Generic.Dictionary<string, string> nameByGuid)
    {
        var prop = ctrlType.GetProperty("exposedParameters", any);
        var arr  = (Array)prop.GetValue(controller);

        if (arr.Length != ParamNames.Length)
        {
            Debug.LogError($"[GameAudioMixer] 노출 파라미터 개수 불일치: {arr.Length} != {ParamNames.Length}");
            return;
        }

        var entryType = arr.GetValue(0).GetType();
        var nameField = entryType.GetField("name", any);
        var guidField = entryType.GetField("guid", any);

        for (int i = 0; i < arr.Length; i++)
        {
            var boxed = arr.GetValue(i); // struct → 박싱 후 되써야 반영된다
            var key   = guidField.GetValue(boxed).ToString();

            if (!nameByGuid.TryGetValue(key, out var paramName))
            {
                Debug.LogError($"[GameAudioMixer] 노출 파라미터 GUID를 그룹에 매칭하지 못했습니다: {key}");
                return;
            }

            nameField.SetValue(boxed, paramName);
            arr.SetValue(boxed, i);
        }

        prop.SetValue(controller, arr);
    }

    private static void RegisterAddressable()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[GameAudioMixer] Addressable 설정을 찾을 수 없습니다 — 등록 스킵.");
            return;
        }

        var guid = AssetDatabase.AssetPathToGUID(MixerPath);
        if (string.IsNullOrEmpty(guid))
        {
            Debug.LogError($"[GameAudioMixer] 믹서 GUID를 얻지 못했습니다 — 등록 스킵: {MixerPath}");
            return;
        }

        var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup, false, false);
        entry.address = AddressKey;

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log($"[GameAudioMixer] Addressable 등록: '{MixerPath}' → key='{AddressKey}'");
    }
}
#endif
