using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>
/// 10-13 「되살아난 기억 · 되찾은 기억」 스킨(<see cref="RelicPartDraftSkinSO"/>)을 만들고 Addressables에 <c>UI/RelicPartDraftSkin</c>으로 등록한 뒤
/// Artsystack 그림을 건다(분석 문서 §3-6 · §4-3). 카드 테(cardFrame)는 비워 둔다 — 등급 테 3단은 코드가 그린다(얇은 v2 경로 유지).
/// 시작 파츠 창(<see cref="UI_StartPartPopup"/>)도 같은 스킨이라 창 판 · 제목 띠 · 카드 채움이 같이 바뀐다.
/// 여러 번 눌러도 같다. 로그 「[RelicPartDraftSkin] 끝」.
/// </summary>
public static class RelicPartDraftSkinCreator
{
    private const string Folder    = "Assets/RelicFairy/UI/Skin/RelicPartDraft";
    private const string AssetPath = Folder + "/RelicPartDraftSkin.asset";
    private const string Address   = "UI/RelicPartDraftSkin";
    private const string Art       = "Assets/RelicFairy/_Imported/Artsystack/";
    private const string Icon      = Art + "Sprites/flaticon/white/128/";
    private const string Glyphs    = "Assets/RelicFairy/UI/HUD/Sprites/StatusGlyphs/";

    // 자리 · 반응 키 → 그림(분석 문서 §4-3). 반응은 우리 상태 문양.
    private static readonly (string key, string path)[] AnchorArt =
    {
        ("dawn", Icon + "sun_and_cloud_128.png"), ("noon", Icon + "sun_128.png"), ("dusk", Icon + "moon_128.png"),
        ("dawn+noon", Icon + "sand_glass_1_128.png"), ("noon+dusk", Icon + "sand_glass_1_128.png"),
        ("frenzy", Icon + "flame_128.png"), ("judgment", Icon + "scale_128.png"), ("residue", Icon + "wind_spell_128.png"),
        ("fire", Glyphs + "StatusGlyph_fire.png"), ("ice", Glyphs + "StatusGlyph_freeze.png"), ("electric", Glyphs + "StatusGlyph_lightning.png"),
        ("grass", Glyphs + "StatusGlyph_poison.png"), ("light", Glyphs + "StatusGlyph_light.png"), ("dark", Glyphs + "StatusGlyph_dark.png"),
    };

    private static readonly (string key, string path)[] RelicBadge =
    {
        ("Gawain", Icon + "sun_1_128.png"), ("Lancelot", Icon + "sword_1_128.png"),
    };

    // 계열 문양 — core · effect · behavior · trigger
    private static readonly string[] KindEmblem =
    {
        Icon + "crown_1_128.png", Icon + "wand_128.png", Icon + "tools_128.png", Icon + "target_1_128.png",
    };

    [MenuItem("RelicFairy/UI/10-13 되살아난 기억 스킨 만들기·등록 (Artsystack)")]
    private static void CreateOrUpdate()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/RelicFairy/UI/Skin", "RelicPartDraft");
        var so = AssetDatabase.LoadAssetAtPath<RelicPartDraftSkinSO>(AssetPath);
        if (so == null)
        {
            so = ScriptableObject.CreateInstance<RelicPartDraftSkinSO>();
            AssetDatabase.CreateAsset(so, AssetPath);
        }

        int set = 0, missing = 0;
        var ser = new SerializedObject(so);
        void Sprite(string field, string path)
        {
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sp == null) { missing++; Debug.LogWarning($"[RelicPartDraftSkin] 없음: {path}"); return; }
            ser.FindProperty(field).objectReferenceValue = sp;
            set++;
        }
        void Pairs(string field, (string key, string path)[] table)
        {
            var arr = ser.FindProperty(field);
            arr.arraySize = table.Length;
            for (int i = 0; i < table.Length; i++)
            {
                var el = arr.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("key").stringValue = table[i].key;
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(table[i].path);
                if (sp == null) { missing++; Debug.LogWarning($"[RelicPartDraftSkin] 없음: {table[i].path}"); }
                else set++;
                el.FindPropertyRelative("sprite").objectReferenceValue = sp;
            }
        }

        Sprite("background", Art + "Recolor/InfoBox_navy.png");
        Sprite("titleBar",   Art + "Recolor/header_box_navy.png");
        Sprite("cardFill",   Art + "Recolor/description_box_05_navy.png");
        ser.FindProperty("cardFrame").objectReferenceValue = null;   // 등급 테는 코드(얇은 v2)
        var emblems = ser.FindProperty("kindEmblem");
        emblems.arraySize = KindEmblem.Length;
        for (int i = 0; i < KindEmblem.Length; i++)
        {
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>(KindEmblem[i]);
            if (sp == null) { missing++; Debug.LogWarning($"[RelicPartDraftSkin] 없음: {KindEmblem[i]}"); }
            else set++;
            emblems.GetArrayElementAtIndex(i).objectReferenceValue = sp;
        }
        Pairs("anchorArt",  AnchorArt);
        Pairs("relicBadge", RelicBadge);
        Sprite("groupUnderline", Art + "Sprites/components/selected_line.png");
        Sprite("groupDivider",   Art + "Sprites/components/Line_1.png");
        ser.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(so);
        AssetDatabase.SaveAssets();

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogError("[RelicPartDraftSkin] AddressableAssetSettings 없음"); return; }
        var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(AssetPath), settings.DefaultGroup, false, false);
        entry.address = Address;
        settings.SetDirty(UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[RelicPartDraftSkin] 끝 — {AssetPath} → '{entry.address}' · 그림 {set} · 없음 {missing}");
    }
}
