using UnityEditor;
using UnityEngine;

/// <summary>
/// 무기 SO의 icon / skillQIcon / skillEIcon 을 Bamao 스프라이트로 할당합니다.
/// </summary>
public static class AssignWeaponIcons
{
    [MenuItem("Tools/Assign Weapon Icons")]
    public static void Execute()
    {
        // ── 스프라이트 로드 ────────────────────────────────────────────────────
        var swordIcon      = LoadSprite("E_sword");
        var bowIcon        = LoadSprite("E_sword 2");
        var skillQIcon     = LoadSprite("icon white_lightning");
        var skillEIcon     = LoadSprite("icon white_star");

        LogMissing(swordIcon,  "E_sword");
        LogMissing(bowIcon,    "E_sword 2");
        LogMissing(skillQIcon, "icon white_lightning");
        LogMissing(skillEIcon, "icon white_star");

        // ── WeaponSO 검색 및 적용 ──────────────────────────────────────────────
        var guids = AssetDatabase.FindAssets("t:WeaponSO");
        int count = 0;

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var so   = AssetDatabase.LoadAssetAtPath<WeaponSO>(path);
            if (so == null) continue;

            bool isBow = so.weaponType == WeaponType.Bow;

            bool dirty = false;

            // 무기 슬롯 아이콘
            var weaponIcon = isBow ? bowIcon : swordIcon;
            if (weaponIcon != null && so.icon != weaponIcon)
            {
                so.icon = weaponIcon;
                dirty = true;
            }

            // 스킬 아이콘 (MainWeaponSO 에만 해당)
            if (so is MainWeaponSO)
            {
                if (skillQIcon != null && so.skillQIcon != skillQIcon)
                {
                    so.skillQIcon = skillQIcon;
                    dirty = true;
                }
                if (skillEIcon != null && so.skillEIcon != skillEIcon)
                {
                    so.skillEIcon = skillEIcon;
                    dirty = true;
                }
            }

            if (dirty)
            {
                EditorUtility.SetDirty(so);
                count++;
                Debug.Log($"[AssignWeaponIcons] {so.displayName} ({(isBow ? "Bow" : "Sword")}) → icons assigned");
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[AssignWeaponIcons] 완료: {count}개 SO 갱신");
    }

    static Sprite LoadSprite(string name)
    {
        var guids = AssetDatabase.FindAssets($"t:Sprite {name}");
        foreach (var g in guids)
        {
            var path   = AssetDatabase.GUIDToAssetPath(g);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null && sprite.name == name) return sprite;
        }
        return null;
    }

    static void LogMissing(Sprite s, string name)
    {
        if (s == null)
            Debug.LogWarning($"[AssignWeaponIcons] sprite not found: '{name}'");
    }
}
