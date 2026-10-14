#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-13 Artsystack 상점 빈 칸(분석 문서 `구현설계/UI팩_Artsystack_분석·매핑_20261012.md` §3-7 · §6-2 순 4 · D4 권장) —
/// <c>ShopSkin.asset</c>에 나가기 · 새로고침 폴백 · 이름 명판 · 가격 알약 판(Artsystack 부품)과 룬 · 재료 배지 리본(<c>categoryBadgeBg[1][2]</c>,
/// 납품 버프바탕 재색칠)을 걸고, <c>EffectIconSet.asset</c>에 원석 아이콘 키 <c>shop_ore</c>(HUD 아이콘_원석)를 더한다.
/// SerializedObject로만 고친다. 그림이 없거나 Sprite로 임포트되지 않았으면 그 칸은 건너뛴다(런타임은 지금 모습으로 폴백) — 끝줄에 센다.
/// 9-slice 경계가 0이면 경고만 한다(SkinArtImporter 규칙 누락 — 판이 통째로 늘어난다). 로그 끝줄 「[ShopSkinArtsystack] 끝 — …」.
/// </summary>
public static class ShopSkinArtsystackEditor
{
    private const string Tag         = "[ShopSkinArtsystack]";
    private const string SkinPath    = "Assets/RelicFairy/UI/Skin/Shop/ShopSkin.asset";
    private const string IconSetPath = "Assets/RelicFairy/Shared/Effect/EffectIconSet.asset";
    private const string PartDir     = "Assets/RelicFairy/_Imported/Artsystack/Sprites/components";
    private const string ShopArtDir  = "Assets/RelicFairy/UI/Skin/Shop/Sprites";
    private const string OreIconPath = "Assets/RelicFairy/UI/HUD/Sprites/Currency/아이콘_원석.png";
    private const string OreKey      = "shop_ore";
    private const int    SlotCount   = 7;   // 판 4 · 배지 2 · 원석 키 1

    [MenuItem("RelicFairy/Tools/10-13 Artsystack 상점 스킨 걸기")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning($"{Tag} 플레이 중엔 하지 않는다 — 정지 뒤 다시"); return; }

        var skin = AssetDatabase.LoadAssetAtPath<ShopSkinSO>(SkinPath);
        if (skin == null) { Debug.LogError($"{Tag} 상점 스킨 없음: {SkinPath}"); return; }

        int assigned = 0, missing = 0, noBorder = 0;
        var so = new SerializedObject(skin);
        SetSlot(so.FindProperty("exitPanel"),   $"{PartDir}/slot_short_inactive.png", ref assigned, ref missing, ref noBorder);
        SetSlot(so.FindProperty("rerollPanel"), $"{PartDir}/slot_short_active.png",   ref assigned, ref missing, ref noBorder);
        SetSlot(so.FindProperty("namePlate"),   $"{PartDir}/placeholder.png",         ref assigned, ref missing, ref noBorder);
        SetSlot(so.FindProperty("pricePill"),   $"{PartDir}/Rectangle_locked.png",    ref assigned, ref missing, ref noBorder);

        // 범주 배지 바탕 0=버프 1=룬 2=재료 3=포션 — 납품 리본(0 · 3)은 그대로 두고 빈 1 · 2만 채운다.
        var badges = so.FindProperty("categoryBadgeBg");
        if (badges.arraySize < 4) badges.arraySize = 4;
        SetSlot(badges.GetArrayElementAtIndex(1), $"{ShopArtDir}/룬바탕.png",   ref assigned, ref missing, ref noBorder);
        SetSlot(badges.GetArrayElementAtIndex(2), $"{ShopArtDir}/재료바탕.png", ref assigned, ref missing, ref noBorder);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(skin);

        // 원석 상품 아이콘 — HUD 재화 칸과 같은 얼굴(AbyssPeddlerCatalog.BuildOre가 shop_ore를 찾는다).
        var iconSet = AssetDatabase.LoadAssetAtPath<EffectIconSetSO>(IconSetPath);
        var ore     = AssetDatabase.LoadAssetAtPath<Sprite>(OreIconPath);
        if (iconSet == null) { missing++; Debug.LogWarning($"{Tag} 아이콘 세트 없음: {IconSetPath}"); }
        else if (ore == null) { missing++; Debug.LogWarning($"{Tag} 원석 아이콘 없음(또는 Sprite 아님): {OreIconPath}"); }
        else { SetIconKey(iconSet, OreKey, ore); assigned++; }

        AssetDatabase.SaveAssets();
        Debug.Log($"{Tag} 끝 — 건 칸 {assigned}/{SlotCount} · 없는 그림 {missing} · 9-slice 경계 없음 {noBorder}" +
                  (missing + noBorder > 0 ? " (위 경고 — 그림 반입 · SkinArtImporter 임포트 설정 뒤 다시)" : ""));
    }

    /// <summary>9-slice 스프라이트 칸 하나를 건다. 그림이 없으면 칸을 비우지 않고 그대로 둔다.</summary>
    private static void SetSlot(SerializedProperty prop, string path, ref int assigned, ref int missing, ref int noBorder)
    {
        if (prop == null) { missing++; Debug.LogWarning($"{Tag} 스킨 칸을 못 찾음 — {path}"); return; }
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
        {
            missing++;
            bool tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null;
            Debug.LogWarning($"{Tag} {prop.propertyPath} — {(tex ? "Sprite로 임포트되지 않음" : "그림 없음")}: {path}");
            return;
        }
        if (sprite.border == Vector4.zero)
        {
            noBorder++;
            Debug.LogWarning($"{Tag} {prop.propertyPath} — 9-slice 경계 0(SkinArtImporter 규칙 확인): {path}");
        }
        prop.objectReferenceValue = sprite;
        assigned++;
    }

    /// <summary>아이콘 세트에 키를 더하거나(없으면 끝에) 그 키의 그림을 바꾼다. 키 비교는 대소문자 무시(EffectIconSetSO 조회와 같다).</summary>
    private static void SetIconKey(EffectIconSetSO set, string key, Sprite sprite)
    {
        var so = new SerializedObject(set);
        var entries = so.FindProperty("entries");
        SerializedProperty hit = null;
        for (int i = 0; i < entries.arraySize && hit == null; i++)
        {
            var e = entries.GetArrayElementAtIndex(i);
            if (string.Equals(e.FindPropertyRelative("key").stringValue, key, System.StringComparison.OrdinalIgnoreCase)) hit = e;
        }
        if (hit == null)
        {
            entries.InsertArrayElementAtIndex(entries.arraySize);
            hit = entries.GetArrayElementAtIndex(entries.arraySize - 1);
            hit.FindPropertyRelative("key").stringValue = key;
        }
        hit.FindPropertyRelative("sprite").objectReferenceValue = sprite;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(set);
    }
}
#endif
