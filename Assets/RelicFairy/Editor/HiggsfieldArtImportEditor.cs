#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 10-13 힉스필드 그림 반입 — 사용자 「힉스필드로 필수 UI들을 제작해서 넣고 검증하자」.
/// <c>_Imported/Higgsfield/</c>에 둔 그림의 가져오기 설정을 프롤로그 그림과 같게 맞추고(스프라이트 · 밉맵 끔 · 2048 · 보통 압축),
/// 대사 · 장면이 부르는 주소를 달고(기본 로컬 그룹 — 프롤로그 그림과 같은 곳), 유물 초상 · 문장을 유물 데이터에 넣는다.
/// 여러 번 눌러도 같다. 로그 「[HiggsfieldArt] 끝」.
/// </summary>
public static class HiggsfieldArtImportEditor
{
    private const string Root      = "Assets/RelicFairy/_Imported/Higgsfield/";
    private const string GroupName = "Default Local Group";
    private const string RelicDir  = "Assets/RelicFairy/Systems/Relic/Data/";

    // 파일 → 주소(대사 · 장면 코드가 부르는 키)
    private static readonly (string file, string address)[] Addressed =
    {
        ("Story/Illust_Story_MissionSeal.png",     "Illust_Story_MissionSeal"),     // 봉인기 사명 — 넷을 묶어라
        ("Story/Illust_Story_MorganaReveal.png",   "Illust_Story_MorganaReveal"),   // 엔딩 — 모르가나의 정체
        ("Story/Illust_Story_EndingCard.png",      "Illust_Story_EndingCard"),      // 엔딩 카드 배경
        ("Portrait/Lich_Portrait.png",             "Lich_Portrait"),                // 대사 표가 이미 부르던 키(그림이 없어 빈칸이었다)
        ("Portrait/Lich_Portrait_Calm.png",        "Lich_Portrait_Calm"),
        ("Portrait/Lich_Portrait_Rage.png",        "Lich_Portrait_Rage"),
        ("Portrait/Lich_Portrait_Dying.png",       "Lich_Portrait_Dying"),
        ("Portrait/Portrait_Boss_ForestGuardian.png", "Portrait_Boss_ForestGuardian"),   // 보스 말풍선 초상
        ("Portrait/Portrait_Boss_Dragon.png",      "Portrait_Boss_Dragon"),
        ("Portrait/Portrait_Boss_Mordred.png",     "Portrait_Boss_Mordred"),
    };

    // 유물 데이터에 직접 넣는 것(주소 없음 — 데이터가 참조로 들고 간다)
    private static readonly (string relic, string portrait, string crest)[] Relics =
    {
        ("RelicClass_Gawain",   "Relic/Relic_Gawain_Portrait.png",   "Relic/Relic_Gawain_Crest.png"),
        ("RelicClass_Lancelot", "Relic/Relic_Lancelot_Portrait.png", "Relic/Relic_Lancelot_Crest.png"),
    };

    [MenuItem("RelicFairy/Tools/10-13 힉스필드 그림 반입 — 가져오기 설정 · 주소 · 유물 초상 · 문장")]
    private static void Run()
    {
        int imported = 0, addressed = 0, relics = 0, missing = 0;

        foreach (var (file, _) in Addressed) { if (Configure(Root + file)) imported++; else missing++; }
        foreach (var (_, p, c) in Relics)
        {
            if (Configure(Root + p)) imported++; else missing++;
            if (Configure(Root + c)) imported++; else missing++;
        }

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings != null)
        {
            var group = settings.FindGroup(GroupName) ?? settings.DefaultGroup;
            foreach (var (file, address) in Addressed)
            {
                string guid = AssetDatabase.AssetPathToGUID(Root + file);
                if (string.IsNullOrEmpty(guid)) continue;
                var entry = settings.CreateOrMoveEntry(guid, group, false, false);
                entry.address = address;
                addressed++;
            }
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
        }
        else Debug.LogWarning("[HiggsfieldArt] Addressables 설정 없음");

        foreach (var (relic, p, c) in Relics)
        {
            var so = AssetDatabase.LoadAssetAtPath<RelicClassSO>(RelicDir + relic + ".asset");
            if (so == null) { Debug.LogWarning($"[HiggsfieldArt] 유물 없음 — {relic}"); continue; }
            var s = new SerializedObject(so);
            s.FindProperty("portrait").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(Root + p);
            var crest = s.FindProperty("crest");
            if (crest != null) crest.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(Root + c);
            s.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(so);
            relics++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[HiggsfieldArt] 끝 — 가져오기 {imported} · 주소 {addressed} · 유물 {relics} · 없음 {missing}");
    }

    /// <summary>프롤로그 그림(Illust_Intro_Page)과 같은 가져오기 설정. 파일이 없으면 false.</summary>
    private static bool Configure(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogWarning($"[HiggsfieldArt] 그림 없음 — {path}"); return false; }
        ti.textureType         = TextureImporterType.Sprite;
        ti.spriteImportMode    = SpriteImportMode.Single;
        ti.mipmapEnabled       = false;
        ti.alphaIsTransparency = true;
        ti.sRGBTexture         = true;
        ti.maxTextureSize      = 2048;
        ti.textureCompression  = TextureImporterCompression.Compressed;
        ti.SaveAndReimport();
        return true;
    }
}
#endif
