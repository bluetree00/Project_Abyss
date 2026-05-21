using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// CombatStatusRoot 레이아웃을 확정된 구조도대로 재배치합니다.
///
/// 구조도 (CombatStatusRoot 580×130, anchor center-bottom, pivot(0.5,0), pos(0,15)):
///   HUD_Active_01  x=-236  y=43  w=50 h=50   img=F_Red
///   HUD_Active_02  x=-178  y=43  w=50 h=50   img=F_Red
///   HUD_Active_03  x=-120  y=43  w=50 h=50   img=F_Red
///   WeaponPanel    x=  0   y=45  w=170 h=78  (container)
///     Weapon_01    x= -46  y= 0  w=78 h=78   img=E_BG
///     Weapon_02    x= +46  y= 0  w=78 h=78   img=E_BG
///   HUD_QSkile     x=+138  y=43  w=68 h=68   img=C_circle
///   HUD_ESkile     x=+212  y=43  w=68 h=68   img=C_circle
///   HP_Icon        x= +92  y=97  w=24 h=24   img=State_HP2
///   HUD_Hp         x=+184  y=97  w=152 h=24  img=State_blank (slider)
/// </summary>
public static class ApplyBamaoHUD
{
    [MenuItem("Tools/Apply Bamao HUD Layout")]
    public static void Execute()
    {
        const string prefabPath = "Assets/Abyss/UI/RootUI/@UIRoot.prefab";
        var root = PrefabUtility.LoadPrefabContents(prefabPath);

        var combatRoot = FindDeep(root.transform, "CombatStatusRoot");
        if (combatRoot == null)
        {
            Debug.LogError("[ApplyBamaoHUD] CombatStatusRoot not found");
            PrefabUtility.UnloadPrefabContents(root);
            return;
        }

        // ── CombatStatusRoot ──────────────────────────────────────────────────
        SetRT(combatRoot, anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 0f),
              pivot: new Vector2(0.5f, 0f), pos: new Vector2(0f, 15f), size: new Vector2(580f, 130f));
        SetSprite(combatRoot, "button short dark");

        // ── Active 슬롯 ───────────────────────────────────────────────────────
        SetChildRT(combatRoot, "HUD_Active_01", -236f, 43f, 50f, 50f);
        SetChildSprite(combatRoot, "HUD_Active_01", "F_Red");
        SetChildRT(combatRoot, "HUD_Active_02", -178f, 43f, 50f, 50f);
        SetChildSprite(combatRoot, "HUD_Active_02", "F_Red");
        SetChildRT(combatRoot, "HUD_Active_03", -120f, 43f, 50f, 50f);
        SetChildSprite(combatRoot, "HUD_Active_03", "F_Red");

        // ── 무기 패널 ─────────────────────────────────────────────────────────
        var weaponPanel = FindChild(combatRoot, "WeaponPanel");
        if (weaponPanel != null)
        {
            SetRT(weaponPanel, anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 0f),
                  pivot: new Vector2(0.5f, 0.5f), pos: new Vector2(0f, 45f), size: new Vector2(170f, 78f));

            // Weapon_01 / Weapon_02 는 WeaponPanel 기준 중앙
            SetChildRT(weaponPanel, "Weapon_01", -46f, 0f, 78f, 78f);
            SetChildSprite(weaponPanel, "Weapon_01", "E_BG");
            SetChildRT(weaponPanel, "Weapon_02", +46f, 0f, 78f, 78f);
            SetChildSprite(weaponPanel, "Weapon_02", "E_BG");
        }

        // ── Q / E 스킬 ────────────────────────────────────────────────────────
        SetChildRT(combatRoot, "HUD_QSkile", +138f, 43f, 68f, 68f);
        SetChildSprite(combatRoot, "HUD_QSkile", "C_circle");
        SetChildRT(combatRoot, "HUD_ESkile", +212f, 43f, 68f, 68f);
        SetChildSprite(combatRoot, "HUD_ESkile", "C_circle");

        // ── HP 슬라이더 ────────────────────────────────────────────────────────
        var hpSlider = FindChild(combatRoot, "HUD_Hp");
        if (hpSlider != null)
        {
            SetRT(hpSlider, anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 0f),
                  pivot: new Vector2(0.5f, 0.5f), pos: new Vector2(+184f, 97f), size: new Vector2(152f, 24f));

            // Background
            var bg = FindChild(hpSlider, "Background");
            if (bg != null)
            {
                SetRT(bg, new Vector2(0f, 0f), new Vector2(1f, 1f),
                      new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                SetSprite(bg, "State_blank");
            }

            // Fill
            var fill = FindDeep(hpSlider, "Fill");
            if (fill != null)
                SetSprite(fill, "State_red");

            // HP_Icon (슬라이더 내부 자식)
            var hpIcon = FindChild(hpSlider, "HP_Icon");
            if (hpIcon != null)
            {
                // 슬라이더 왼쪽 바깥에 표시: anchor left, pos x=-20 (슬라이더 중앙 기준)
                SetRT(hpIcon, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                      new Vector2(0.5f, 0.5f), new Vector2(-16f, 0f), new Vector2(24f, 24f));
                SetSprite(hpIcon, "State_HP2");
            }
        }

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        Debug.Log("[ApplyBamaoHUD] 레이아웃 적용 완료");
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────────────

    static void SetRT(Transform t, Vector2 anchorMin, Vector2 anchorMax,
                      Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var rt = t.GetComponent<RectTransform>();
        if (rt == null) return;
        rt.anchorMin       = anchorMin;
        rt.anchorMax       = anchorMax;
        rt.pivot           = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta       = size;
    }

    static void SetChildRT(Transform parent, string childName,
                            float x, float y, float w, float h)
    {
        var child = FindChild(parent, childName);
        if (child == null) { Debug.LogWarning($"[ApplyBamaoHUD] child not found: {childName}"); return; }
        SetRT(child, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
              new Vector2(0.5f, 0.5f), new Vector2(x, y), new Vector2(w, h));
    }

    static void SetSprite(Transform t, string spriteName)
    {
        var img = t.GetComponent<Image>();
        if (img == null) return;
        var sprite = FindSprite(spriteName);
        if (sprite != null) img.sprite = sprite;
        else Debug.LogWarning($"[ApplyBamaoHUD] sprite not found: {spriteName}");
    }

    static void SetChildSprite(Transform parent, string childName, string spriteName)
    {
        var child = FindChild(parent, childName);
        if (child == null) return;
        SetSprite(child, spriteName);
    }

    static Transform FindChild(Transform t, string name)
    {
        for (int i = 0; i < t.childCount; i++)
            if (t.GetChild(i).name == name) return t.GetChild(i);
        return null;
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var r = FindDeep(c, name); if (r != null) return r; }
        return null;
    }

    static Sprite FindSprite(string name)
    {
        var guids = AssetDatabase.FindAssets($"t:Sprite {name}");
        foreach (var g in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null && sprite.name == name) return sprite;
        }
        return null;
    }
}
