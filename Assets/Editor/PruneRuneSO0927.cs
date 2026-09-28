using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 룬 72종 정리(09-19 축소안 · 사용자 09-27 확정) — ItemSO 쪽 반영. CSV·오프라인 JSON은 따로 고친다.
/// <para>생성기(Generate SO from CSV)는 <b>추가만</b> 하고 삭제·이름 변경을 반영하지 않는다. 드롭 풀은 SO(ItemSORegistry)라서
/// SO를 안 지우면 CSV에서 지운 룬이 계속 떨어진다.</para>
/// ① 삭제 목록 48개 SO를 지운다(AssetDatabase.DeleteAsset — .meta는 Unity가 정리) ② 남은 SO의 displayName을 오프라인 ITEM_DATA 이름으로
/// ③ ItemSODatabase를 남은 SO로 다시 채운다(Addressable 「ItemSODatabase」가 번들에 끌고 가는 목록).
/// </summary>
public static class PruneRuneSO0927
{
    private const string SOFolder     = "Assets/RelicFairy/Shared/Item/SOdata";
    private const string DatabasePath = SOFolder + "/ItemSODatabase.asset";
    private const string ItemJsonPath = "Assets/RelicFairy/Resources/ITEM_DATA.json";

    private static readonly string[] Delete =
    {
        "item_t1_battle_awaken", "item_t1_battle_focus", "item_t1_boss_resolve",
        "item_t1_calm_blade", "item_t1_consecutive_sharp", "item_t1_cooldown_accel",
        "item_t1_crisis_blade", "item_t1_crude_charm", "item_t1_desperate_speed",
        "item_t1_dull_awl", "item_t1_focus_eye", "item_t1_focus_hunter",
        "item_t1_guard_instinct", "item_t1_guard_oath", "item_t1_heavy_blade",
        "item_t1_hit_recoil", "item_t1_keen_focus", "item_t1_move_power",
        "item_t1_patched_cloth", "item_t1_preempt_blade", "item_t1_rage_armor",
        "item_t1_skill_lingering", "item_t1_small_weight", "item_t1_stillness_power",
        "item_t1_survival_intuition", "item_t1_threat_armor", "item_t1_unbreakable_heart",
        "item_t2_battle_charm", "item_t2_defense_awaken", "item_t2_destruction_ring",
        "item_t2_first_strike", "item_t2_forged_hammer", "item_t2_giant_nail",
        "item_t2_heavy_chain", "item_t2_heavy_deck", "item_t2_light_charm",
        "item_t2_preempt_armor", "item_t2_rage_shield", "item_t2_shield_enhance",
        "item_t2_skill_guard", "item_t2_steel_part", "item_t2_stillness_burst",
        "item_t2_strong_will", "item_t2_survival_will", "item_t2_thick_awl",
        "item_t2_travel_bag", "item_t2_unbreakable_attack", "item_t2_warrior_heart"
    };

    [System.Serializable] private sealed class Row { public string item_id; public string item_name; }
    [System.Serializable] private sealed class Rows { public List<Row> items; }

    [MenuItem("RelicFairy/Gameplay/Item/룬 72종 정리 반영 (SO 삭제·이름 동기화)")]
    private static void Run()
    {
        var text = AssetDatabase.LoadAssetAtPath<TextAsset>(ItemJsonPath);
        var rows = text != null ? JsonUtility.FromJson<Rows>(text.text) : null;
        if (rows?.items == null) { Debug.LogError("[PruneRuneSO] ITEM_DATA.json 읽기 실패"); return; }
        var names = new Dictionary<string, string>();
        foreach (var r in rows.items)
            if (!string.IsNullOrEmpty(r.item_id) && !names.ContainsKey(r.item_id)) names[r.item_id] = r.item_name;

        var del  = new HashSet<string>(Delete);
        var keep = new List<ItemSO>();
        int deleted = 0, renamed = 0, orphan = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:ItemSO", new[] { SOFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<ItemSO>(path);
            if (so == null) continue;
            if (del.Contains(so.itemId))
            {
                if (AssetDatabase.DeleteAsset(path)) deleted++;
                continue;
            }
            if (names.TryGetValue(so.itemId, out var n))
            {
                if (!string.IsNullOrEmpty(n) && so.displayName != n) { so.displayName = n; EditorUtility.SetDirty(so); renamed++; }
            }
            else orphan++;   // 차트에 없는 SO — 지우지 않고 보고만(상점 폴백 등 다른 쓰임이 있을 수 있다)
            keep.Add(so);
        }

        var db = AssetDatabase.LoadAssetAtPath<ItemSODatabase>(DatabasePath);
        var field = typeof(ItemSODatabase).GetField("items",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (db != null && field != null)
        {
            keep.Sort((a, b) => string.CompareOrdinal(a.itemId, b.itemId));
            field.SetValue(db, keep);
            EditorUtility.SetDirty(db);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[PruneRuneSO] 삭제 {deleted}/{Delete.Length} · 이름 동기화 {renamed} · 남은 SO {keep.Count} · 차트에 없는 SO {orphan}");
    }
}
