#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-02 상태 · 버프 아이콘 채우기 — 사용자 「상태이상 표시 UI가 그냥 원 모양」 → 「여전히 속성 · 디버프 UI 퀄리티가 안 좋다」.
/// 효과 아이콘 세트(<c>UI/EffectIconSet</c>)에 상점 그림 4개뿐이라 상태 · 버프 키(30여 개)가 전부 색 원(플레이스홀더)으로 그려졌다.
/// <list type="bullet">
/// <item>HUD 디자이너 글리프(흰 실루엣 — 날개 · 모래시계 · 꽃 · 반지 · 공격력 · 방어력)가 있는 키는 그 그림을 쓴다.</item>
/// <item>나머지는 같은 결로 그린 흰 글리프(<c>UI/HUD/Sprites/StatusGlyphs/StatusGlyph_*.png</c>, 128px).</item>
/// <item>색은 쓰는 쪽이 <see cref="EffectIconRegistry.TintFor"/>로 입힌다(불 = 주황 · 얼음 = 하늘 …).</item>
/// </list>
/// 룬 보석 그림은 쓰지 않는다 — 효과 줄에 룬 모양이 붙으면 「그 룬이 무슨 아이템인지」와 섞인다(09-14 원칙).
/// 글리프 PNG는 스프라이트로 가져오기 설정을 맞춘다. 있는 키는 그림만 바꾸고, 없는 키는 붙인다(상점 항목은 건드리지 않는다).
/// </summary>
public static class StatusIconFillEditor
{
    private const string SetPath   = "Assets/RelicFairy/Shared/Effect/EffectIconSet.asset";
    private const string GlyphDir  = "Assets/RelicFairy/UI/HUD/Sprites/StatusGlyphs/";
    private const string HudDir    = "Assets/RelicFairy/UI/HUD/Sprites/";

    // 키 → 그림 경로(글리프 폴더 기준 파일명, 또는 HUD 디자이너 그림)
    private static readonly (string key, string path)[] Table =
    {
        // 상태이상(몬스터 머리 위 · 효과 줄)
        ("fire",       GlyphDir + "StatusGlyph_fire.png"),
        ("freeze",     GlyphDir + "StatusGlyph_freeze.png"),
        ("lightning",  GlyphDir + "StatusGlyph_lightning.png"),
        ("poison",     GlyphDir + "StatusGlyph_poison.png"),
        ("bleed",      GlyphDir + "StatusGlyph_bleed.png"),
        ("dark",       GlyphDir + "StatusGlyph_dark.png"),
        ("light",      GlyphDir + "StatusGlyph_light.png"),
        ("stun",       GlyphDir + "StatusGlyph_stun.png"),
        ("slow",       GlyphDir + "StatusGlyph_slow.png"),
        ("vulnerable", GlyphDir + "StatusGlyph_vulnerable.png"),
        ("weaken",     GlyphDir + "StatusGlyph_weaken.png"),
        // 버프 · 효과(HUD 버프 칸 · 효과 줄)
        ("dmg",        GlyphDir + "StatusGlyph_dmg.png"),
        ("crit",       GlyphDir + "StatusGlyph_crit.png"),
        ("critdmg",    GlyphDir + "StatusGlyph_critdmg.png"),
        ("hp",         GlyphDir + "StatusGlyph_hp.png"),
        ("shield",     GlyphDir + "StatusGlyph_shield.png"),     // 보호막 — 방어력 그림과 나눔
        ("atkspeed",   GlyphDir + "StatusGlyph_atkspeed.png"),   // 공격 속도 — 이동 속도(날개)와 나눔
        ("gold",       GlyphDir + "StatusGlyph_gold.png"),
        ("skill",      GlyphDir + "StatusGlyph_skill.png"),
        ("special",    GlyphDir + "StatusGlyph_special.png"),
        ("projectile", GlyphDir + "StatusGlyph_projectile.png"),
        ("range",      GlyphDir + "StatusGlyph_range.png"),
        ("roll",       GlyphDir + "StatusGlyph_roll.png"),
        ("element",    GlyphDir + "StatusGlyph_element.png"),
        ("allstats",   GlyphDir + "StatusGlyph_allstats.png"),
        ("utility",    GlyphDir + "StatusGlyph_utility.png"),
        ("unknown",    GlyphDir + "StatusGlyph_unknown.png"),
        // HUD 디자이너 글리프
        ("atk",        HudDir + "공격력_1@2x.png"),
        ("def",        HudDir + "방어력_1@2x.png"),
        ("speed",      HudDir + "버프 내용1@2x.png"),
        ("cooldown",   HudDir + "버프 내용2@2x.png"),
        ("heal",       HudDir + "버프 내용3@2x.png"),
        ("luck",       HudDir + "버프 내용4@2x.png"),
    };

    [MenuItem("RelicFairy/UI/10-02 상태이상 아이콘 채우기")]
    public static void Fill()
    {
        EnsureGlyphImport();

        var set = AssetDatabase.LoadAssetAtPath<EffectIconSetSO>(SetPath);
        if (set == null) { Debug.LogError($"[StatusIcons] 아이콘 세트 없음: {SetPath}"); return; }

        var so = new SerializedObject(set);
        var entries = so.FindProperty("entries");
        var index = new Dictionary<string, int>();
        for (int i = 0; i < entries.arraySize; i++)
            index[entries.GetArrayElementAtIndex(i).FindPropertyRelative("key").stringValue] = i;

        int added = 0, updated = 0, missing = 0;
        foreach (var (key, path) in Table)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) { missing++; Debug.LogError($"[StatusIcons] 그림 없음(또는 스프라이트 아님): {path}"); continue; }
            if (!index.TryGetValue(key, out int i))
            {
                i = entries.arraySize;
                entries.arraySize++;
                entries.GetArrayElementAtIndex(i).FindPropertyRelative("key").stringValue = key;
                index[key] = i;
                added++;
            }
            else updated++;
            entries.GetArrayElementAtIndex(i).FindPropertyRelative("sprite").objectReferenceValue = sprite;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        Debug.Log($"[StatusIcons] 채움 — 새로 {added} · 바꿈 {updated} · 그림 없음 {missing} · 전체 {entries.arraySize}");
    }

    /// <summary>글리프 PNG를 UI 스프라이트로 가져오게 맞춘다(투명 · 밉맵 없음 · 256). Unity가 가져오기 설정을 기록한다.</summary>
    private static void EnsureGlyphImport()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { GlyphDir.TrimEnd('/') }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
            bool dirty = false;
            if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; dirty = true; }
            if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; dirty = true; }
            if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; dirty = true; }
            if (ti.mipmapEnabled) { ti.mipmapEnabled = false; dirty = true; }
            if (ti.maxTextureSize != 256) { ti.maxTextureSize = 256; dirty = true; }
            if (ti.textureCompression != TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
            if (dirty) ti.SaveAndReimport();
        }
    }
}
#endif
