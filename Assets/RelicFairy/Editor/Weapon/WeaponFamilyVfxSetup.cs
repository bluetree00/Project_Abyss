using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 10-22 B4 「카타나 · 대검 공격 · 피격 이펙트 톤이 붉은색 · 파란색 왔다 갔다 — 무기별 같은 계열로」 — 카타나용 붉은 검기.
/// 푸른 검기(Hovl Slash wave · 주소 vfx_form_windcut)를 복사해 입자 색의 색상(hue)만 붉게 바꾼다(재질은 전부 흰색이라 입자 색이 색을 정한다).
/// 흰 · 회색(채도 낮은 색)과 밝기 · 투명도는 그대로. 주소 vfx_form_windcut_red(같은 그룹).
/// </summary>
public static class WeaponFamilyVfxSetup
{
    private const string Src = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio 2/Sword slash VFX/Prefabs/Slash wave.prefab";
    private const string Dst = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio 2/Sword slash VFX/Prefabs/Slash wave Red.prefab";
    private const string Address = "vfx_form_windcut_red";
    private const float RedHue = 0.995f;
    private const float KeepGreyBelow = 0.08f;   // 이보다 채도가 낮은 색(흰 · 회색)은 그대로
    private const float MinSaturation = 0.85f;   // 원본 파랑은 채도 ≈ 0.5라 색상만 바꾸면 분홍으로 보였다(실측 85)

    [MenuItem("RelicFairy/Weapon/무기 계열 색 — 카타나 붉은 검기 만들기 · 등록 (10-22 B4)")]
    private static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Dst) == null && !AssetDatabase.CopyAsset(Src, Dst))
        {
            Debug.LogWarning("[WeaponFamilyVfx] 복사 실패: " + Src);
            return;
        }
        var root = PrefabUtility.LoadPrefabContents(Dst);
        int n = 0;
        try
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startColor = Shift(main.startColor);
                var col = ps.colorOverLifetime;
                if (col.enabled) col.color = Shift(col.color);
                var trails = ps.trails;
                if (trails.enabled) trails.colorOverLifetime = Shift(trails.colorOverLifetime);
                n++;
            }
            PrefabUtility.SaveAsPrefabAsset(root, Dst);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        AddressableAssetGroup group = null;
        foreach (var g in settings.groups)
            if (g != null)
                foreach (var e in g.entries)
                    if (e.address == "vfx_form_windcut") group = g;
        string guid = AssetDatabase.AssetPathToGUID(Dst);
        if (group == null || string.IsNullOrEmpty(guid)) { Debug.LogWarning($"[WeaponFamilyVfx] 그룹 {group != null} · 에셋 {!string.IsNullOrEmpty(guid)}"); return; }
        var entry = settings.CreateOrMoveEntry(guid, group, false, false);
        entry.address = Address;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[WeaponFamilyVfx] 붉은 검기 — 입자계 {n}개 색 바꿈 · {Address} · {group.Name}");
    }

    private static ParticleSystem.MinMaxGradient Shift(ParticleSystem.MinMaxGradient g)
    {
        switch (g.mode)
        {
            case ParticleSystemGradientMode.Color:        g.color = Shift(g.color); break;
            case ParticleSystemGradientMode.TwoColors:    g.colorMin = Shift(g.colorMin); g.colorMax = Shift(g.colorMax); break;
            case ParticleSystemGradientMode.Gradient:
            case ParticleSystemGradientMode.RandomColor:  g.gradient = Shift(g.gradient); break;
            case ParticleSystemGradientMode.TwoGradients: g.gradientMin = Shift(g.gradientMin); g.gradientMax = Shift(g.gradientMax); break;
        }
        return g;
    }

    private static Gradient Shift(Gradient src)
    {
        if (src == null) return null;
        var keys = src.colorKeys;
        for (int i = 0; i < keys.Length; i++) keys[i].color = Shift(keys[i].color);
        var g = new Gradient { mode = src.mode };
        g.SetKeys(keys, src.alphaKeys);
        return g;
    }

    private static Color Shift(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        if (s < KeepGreyBelow) return c;
        var r = Color.HSVToRGB(RedHue, Mathf.Max(s, MinSaturation), v, true);
        r.a = c.a;
        return r;
    }
}
