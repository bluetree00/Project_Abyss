#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-13 Artsystack 3단계(분석 문서 §3-1 · §3-3 · §4-2) — 기억의 제단 판 · 해금 탭 · 해금 배너 · 잠긴 칸 · 구역 이름 밑줄 그림을
/// 업적 스킨(<c>AchievementSkin.asset</c>)의 「제단」 칸에 건다. 노드 아이콘 30칸(29종)은 노드 정의가 코드라 아래 표(노드 id → 흰 아이콘)로 채운다.
/// 그림은 반입 2단계(<c>_Imported/Artsystack</c>) · 임포트 설정 뒤에 있어야 한다. 여러 번 눌러도 같다. 로그 「[AltarSkin] 끝」.
/// </summary>
public static class AltarSkinAssignEditor
{
    private const string Tag        = "[AltarSkin]";
    private const string SkinPath   = "Assets/RelicFairy/UI/Skin/Achievement/AchievementSkin.asset";
    private const string Root       = "Assets/RelicFairy/_Imported/Artsystack";
    private const string CompDir    = Root + "/Sprites/components";
    private const string IconDir    = Root + "/Sprites/flaticon/white/128";
    private const string RecolorDir = Root + "/Recolor";

    // 판 · 배지 · 갈래 문장 — (스킨 칸, 그림 경로)
    private static readonly (string field, string path)[] Slots =
    {
        ("altarWindow",       RecolorDir + "/InfoBox_navy.png"),
        ("altarHeaderRule",   CompDir    + "/splitt_divider.png"),
        ("altarTabUnderline", CompDir    + "/selected_line.png"),
        ("altarNewsDot",      CompDir    + "/slider_scroller.png"),
        ("altarCloseButton",  RecolorDir + "/slot_short_inactive_navy.png"),
        ("altarNodeRing",     CompDir    + "/icon_slot_active.png"),
        ("altarKeyRing",      CompDir    + "/skill_slot.png"),
        ("altarLock",         IconDir    + "/lock_1_128.png"),
        ("altarUnlock",       IconDir    + "/lock_3_128.png"),
        ("altarNext",         IconDir    + "/star_3_128.png"),
        ("altarDetailPanel",  RecolorDir + "/description_box_05_navy.png"),
        ("altarDetailRule",   CompDir    + "/Line_1.png"),
        ("altarBannerStrip",  CompDir    + "/message_slot_1.png"),
        ("zoneTitleRule",     CompDir    + "/direction_line.png"),
        ("emblemRune",        IconDir    + "/gemstone_128.png"),
        ("emblemCovenant",    IconDir    + "/scroll_128.png"),
        ("emblemWeapon",      IconDir    + "/sword_2_128.png"),
        ("emblemJourney",     IconDir    + "/explore_128.png"),
        ("emblemMemory",      IconDir    + "/book_2_128.png"),
    };

    // 노드 id → 흰 아이콘 128 이름(분석 문서 §4-2). 보관함 1 · 2는 같은 그림. 노드를 더하면 여기 한 줄.
    private static readonly (string id, string icon)[] NodeIcons =
    {
        (MemoryAltarCatalog.MemRedraw,      "refresh"),
        (MemoryAltarCatalog.MemClear,       "eye_show"),
        (MemoryAltarCatalog.MemRadiant,     "sun"),
        (MemoryAltarCatalog.RuneStorage1,   "chest_2"),
        (MemoryAltarCatalog.RuneStorage2,   "chest_2"),
        (MemoryAltarCatalog.RefinePick,     "crystal_2"),
        (MemoryAltarCatalog.RefineQuality,  "ruby"),
        (MemoryAltarCatalog.RuneEpic,       "gemstone"),
        (MemoryAltarCatalog.RuneLegendary,  "diamond"),
        (MemoryAltarCatalog.CovenantParts1, "document"),
        (MemoryAltarCatalog.CovenantParts2, "book"),
        (MemoryAltarCatalog.CovenantParts3, "book_open"),
        (MemoryAltarCatalog.CovenantSlot,   "inkpen"),
        (MemoryAltarCatalog.PartPierce,     "arrow"),
        (MemoryAltarCatalog.WeaponCrossbow, "archery"),
        (MemoryAltarCatalog.PartPower,      "power_1"),
        (MemoryAltarCatalog.SigilSmith,     "hammer"),
        (MemoryAltarCatalog.PartHoming,     "feather"),
        (MemoryAltarCatalog.StartParts2,    "quiver"),
        (MemoryAltarCatalog.WeaponEvolve,   "sword_3"),
        (MemoryAltarCatalog.PartExplode,    "bomb"),
        (MemoryAltarCatalog.Revive,         "Heart"),
        (MemoryAltarCatalog.SigilMerchant,  "tag"),
        (MemoryAltarCatalog.ShopReroll,     "dice"),
        (MemoryAltarCatalog.MaxHpUp,        "increse_health"),
        (MemoryAltarCatalog.PotionSlot,     "potion_1"),
        (MemoryAltarCatalog.AbyssDepth,     "anchor"),
        (MemoryAltarCatalog.DepthReward,    "water_drop"),
        (MemoryAltarCatalog.SigilAscetic,   "scale"),
        // 10-14 강화형 신설
        (MemoryAltarCatalog.RuneRareOdds,   "star_3"),
        (MemoryAltarCatalog.RuneEpicOdds,   "star_and_moon"),
        (MemoryAltarCatalog.RuneLegendOdds, "crown_1"),
        (MemoryAltarCatalog.OreGain,        "crystal_1"),
        (MemoryAltarCatalog.EnhanceGain,    "tools"),
        (MemoryAltarCatalog.GoldGain,       "coin"),
        (MemoryAltarCatalog.EssenceGain,    "moon"),
        (MemoryAltarCatalog.PotionHeal,     "wand"),
    };

    [MenuItem("RelicFairy/Tools/10-13 Artsystack 기억의 제단 스킨 걸기")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning($"{Tag} 플레이 중엔 하지 않는다"); return; }
        var skin = AssetDatabase.LoadAssetAtPath<AchievementSkinSO>(SkinPath);
        if (skin == null) { Debug.LogWarning($"{Tag} 스킨 없음: {SkinPath}"); return; }

        var so = new SerializedObject(skin);
        int slots = 0, icons = 0, missing = 0;

        foreach (var (field, path) in Slots)
        {
            var prop = so.FindProperty(field);
            var sp   = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (prop == null || sp == null) { missing++; Debug.LogWarning($"{Tag} 없음: {field} ← {path}"); continue; }
            prop.objectReferenceValue = sp;
            slots++;
        }

        var list = so.FindProperty("altarNodeIcons");
        var seen = new HashSet<string>();
        list.arraySize = NodeIcons.Length;
        for (int i = 0; i < NodeIcons.Length; i++)
        {
            var (id, icon) = NodeIcons[i];
            string path = $"{IconDir}/{icon}_128.png";
            var sp  = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            var row = list.GetArrayElementAtIndex(i);
            row.FindPropertyRelative("id").stringValue           = id;
            row.FindPropertyRelative("icon").objectReferenceValue = sp;
            seen.Add(id);
            if (sp != null) icons++;
            else { missing++; Debug.LogWarning($"{Tag} 아이콘 없음: {id} ← {path}"); }
        }

        // 카탈로그에 있는데 표에 없는 노드 — 그 노드는 아이콘 없이 지금 표식(◆ · 점)으로 남는다
        int unmapped = 0;
        foreach (var n in MemoryAltarCatalog.All)
            if (!seen.Contains(n.Id)) { unmapped++; Debug.LogWarning($"{Tag} 아이콘 표에 없는 노드: {n.Id} {n.DisplayName}"); }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(skin);
        AssetDatabase.SaveAssets();
        Debug.Log($"{Tag} 끝 — 걸음 {slots + icons} · 없음 {missing} (판 · 배지 · 문장 {slots}/{Slots.Length} · 노드 아이콘 {icons}/{NodeIcons.Length} · 표에 없는 노드 {unmapped})");
    }
}
#endif
