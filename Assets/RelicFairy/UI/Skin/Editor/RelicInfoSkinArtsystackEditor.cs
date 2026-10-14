using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-13 유물 정보 팝업 스킨에 Artsystack 그림을 건다(분석 문서 §3-5) — 능력 카드 남색 판 · Q/패시브 배지 판 · 초상 받침 고리 · 빛 ·
/// 임시 문장(가웨인 태양 · 랜슬롯 검) · 태그 칩(Hexagon_01 — 무채 · 금선은 코드). 구운 글자가 틀린 완성 칩(tagArts)은 걷어낸다.
/// 판 6장 · 버튼은 그대로(유물 컨셉 판 — 분석 문서 D5). 여러 번 눌러도 같다. 로그 「[RelicInfoSkin] 끝」.
/// </summary>
public static class RelicInfoSkinArtsystackEditor
{
    private const string SkinPath = "Assets/RelicFairy/UI/Skin/RelicInfo/RelicInfoSkin.asset";
    private const string Art      = "Assets/RelicFairy/_Imported/Artsystack/";

    [MenuItem("RelicFairy/UI/10-13 유물 정보 스킨 걸기 (Artsystack)")]
    private static void Run()
    {
        var skin = AssetDatabase.LoadAssetAtPath<RelicInfoSkinSO>(SkinPath);
        if (skin == null) { Debug.LogError($"[RelicInfoSkin] 없음: {SkinPath}"); return; }

        int set = 0, missing = 0;
        var ser = new SerializedObject(skin);
        void Sprite(string field, string path)
        {
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sp == null) { missing++; Debug.LogWarning($"[RelicInfoSkin] 없음: {path}"); return; }
            ser.FindProperty(field).objectReferenceValue = sp;
            set++;
        }

        Sprite("abilityPanel", Art + "Recolor/store_item_frame_03_navy.png");
        Sprite("kindBadge",    Art + "Recolor/Hexagon_02_navy.png");
        Sprite("portraitRing", Art + "Sprites/components/Design_01.png");
        Sprite("portraitGlow", Art + "Sprites/components/Glow_Circle.png");
        Sprite("tagChip",      Art + "Sprites/components/Hexagon_01.png");
        ser.FindProperty("tagArts").arraySize = 0;   // 완성 칩 3장(하나는 구운 글자 오류) → 글자 칩으로 통일

        var emblems = new[] { ("Gawain", Art + "Sprites/flaticon/white/128/sun_1_128.png"), ("Lancelot", Art + "Sprites/flaticon/white/128/sword_1_128.png") };
        var arr = ser.FindProperty("relicEmblem");
        arr.arraySize = emblems.Length;
        for (int i = 0; i < emblems.Length; i++)
        {
            var el = arr.GetArrayElementAtIndex(i);
            el.FindPropertyRelative("key").stringValue = emblems[i].Item1;
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>(emblems[i].Item2);
            if (sp == null) { missing++; Debug.LogWarning($"[RelicInfoSkin] 없음: {emblems[i].Item2}"); }
            else set++;
            el.FindPropertyRelative("sprite").objectReferenceValue = sp;
        }

        ser.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(skin);
        AssetDatabase.SaveAssets();
        Debug.Log($"[RelicInfoSkin] 끝 — 그림 {set} · 없음 {missing} · 완성 칩 걷어냄");
    }
}
