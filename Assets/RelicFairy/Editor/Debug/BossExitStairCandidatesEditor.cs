#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-10 보스 클리어 출구(계단 A안) 리소스 후보 렌더 — 사용자 「보스를 잡은 뒤 포탈 · 계단이 초라하다 — 계단이 느낌 있게 생성되고
/// 올라가는 느낌 · 제대로 된 연출과 리소스」. 씬을 건드리지 않고(미리보기 씬) 후보마다 게임 카메라 구도(위에서 37°)와 정면을
/// 나란히 찍고 크기(m)를 적는다. 이펙트는 1.2초 흐른 모습. 결과: Temp/stair_candidates/*.png + info.txt · 로그 「[StairCandidates] 끝」.
/// </summary>
public static class BossExitStairCandidatesEditor
{
    private const string Out  = "Temp/stair_candidates";
    private const string Goth = "Assets/RelicFairy/_Imported/Gothic_Interior/Environment/Asset/Prefabs/";
    private const string Lea  = "Assets/RelicFairy/_Imported/LeartesStudios/FantasyCastle/~HDRP/Art/Prefabs/";
    private const string Ssep = "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/";
    private const string Hovl = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/Magic circles/Prefabs/Loop version/";
    private const string Pil  = "Assets/RelicFairy/_Imported/EffectSource/Piloto Studio/Super Realistic FX Bundle/ARPG_Realistic Essentials Fire/Prefabs/Complex_VFXs/Portal_VFX/";
    private const int   Px      = 512;
    private const float VfxTime = 1.2f;

    private static readonly (string label, string path, bool vfx)[] Cands =
    {
        // 계단
        ("s1_goth_StairStoneKit_01a", Goth + "SM_StairStoneKit_01a.prefab", false),
        ("s2_goth_StairStoneKit_02a", Goth + "SM_StairStoneKit_02a.prefab", false),
        ("s3_goth_StairStoneKit_02b", Goth + "SM_StairStoneKit_02b.prefab", false),
        ("s4_goth_StairStoneKit_02c", Goth + "SM_StairStoneKit_02c.prefab", false),
        ("s5_goth_Stairs_Left_01a",   Goth + "SM_Stairs_Left_01a.prefab", false),
        ("s6_goth_StoneStairMerge",   Goth + "SM_StoneStairMerge_01a.prefab", false),
        ("s7_goth_Ambo_Stairs",       Goth + "SM_Ambo_Stairs_01a.prefab", false),
        ("s8_lea_Stairs_A_01",        Lea + "SM_Stairs_A_01.prefab", false),
        ("s9_lea_Stairs_A_02",        Lea + "SM_Stairs_A_02.prefab", false),
        ("s10_lea_Stairs_B_01",       Lea + "SM_Stairs_B_01.prefab", false),
        ("s11_lea_Stairs_03",         Lea + "SM_Stairs_03.prefab", false),
        // 꼭대기 아치
        ("a1_goth_StoneArch_01a",     Goth + "SM_StoneArch_01a.prefab", false),
        ("a2_goth_StoneArch_01b",     Goth + "SM_StoneArch_01b.prefab", false),
        ("a3_goth_ArchWallDoor_01a",  Goth + "SM_ArchWallDoor_01a.prefab", false),
        ("a4_goth_Arche_01b",         Goth + "SM_Arche_01b.prefab", false),
        ("a5_goth_Arche_02a",         Goth + "SM_Arche_02a.prefab", false),
        // 포탈
        ("p1_ssep_SpaceWarpPortal_now", Ssep + "EffectsSet_1(NotScriptBased)/Effects/Effect_16_SpaceWarpPortal/Effect_16_SpaceWarpPortal.prefab", true),
        ("p2_piloto_Portal_FX",         Pil + "Portal_FX.prefab", true),
        ("p3_ssep2_DarkPortal",         Ssep + "EffectsSet_2(ScriptBased)/Effects/Effect_43_DarkDraw/Effect_43_Parts/Effect_43_DarkPortal.prefab", true),
        ("p4_ssep_WarpArrive",          Ssep + "EffectsSet_1(NotScriptBased)/Effects/Effect_16_SpaceWarpPortal/Effect_16_WarpArrive.prefab", true),
        // 꼭대기 바닥 마법진(대기방 도착 마법진과 같은 챕터 짝)
        ("m1_hovl_forest_archer_Ch1", Hovl + "Magic circle forest archer loop.prefab", true),
        ("m2_hovl_fire_Ch2",          Hovl + "Magic circle fire loop.prefab", true),
        ("m3_hovl_sun_Ch3",           Hovl + "Magic circle sun loop.prefab", true),
        ("m4_hovl_suns_Ch4",          Hovl + "Magic circle suns loop.prefab", true),
    };

    [MenuItem("RelicFairy/Debug/10-10 보스 클리어 계단 · 포탈 후보 렌더 (편집 모드)")]
    private static void Run()
    {
        if (Application.isPlaying) { Debug.LogWarning("[StairCandidates] 편집 모드에서만"); return; }
        Directory.CreateDirectory(Out);
        var info = new StringBuilder("보스 클리어 계단 · 포탈 후보 — 왼쪽 = 게임 구도(위 37°), 오른쪽 = 정면 · 크기 = 가로(X)×높이(Y)×깊이(Z) m\n");
        int ok = 0;
        foreach (var c in Cands)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(c.path);
            if (asset == null) { info.AppendLine($"{c.label} | 없음 | {c.path}"); continue; }
            if (Render(c.label, asset, c.vfx, c.path, info)) ok++;
        }
        File.WriteAllText($"{Out}/info.txt", info.ToString());
        Debug.Log($"[StairCandidates] 끝 — {ok}/{Cands.Length}장 → {Out}\n{info}");
    }

    private static bool Render(string label, GameObject asset, bool vfx, string path, StringBuilder info)
    {
        var pru = new PreviewRenderUtility();
        try
        {
            var cam = pru.camera;
            cam.fieldOfView     = 30f;
            cam.nearClipPlane   = 0.05f;
            cam.farClipPlane    = 400f;
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.10f, 0.12f, 1f);
            pru.lights[0].intensity = 1.3f;
            pru.lights[0].transform.rotation = Quaternion.Euler(40f, -35f, 0f);
            pru.lights[1].intensity = 0.6f;
            pru.lights[1].transform.rotation = Quaternion.Euler(-15f, 150f, 0f);
            pru.ambientColor = new Color(0.32f, 0.31f, 0.36f, 1f);

            var go = pru.InstantiatePrefabInScene(asset);
            if (vfx)
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                    if (ps.transform.parent == null || ps.transform.parent.GetComponentInParent<ParticleSystem>() == null)
                        ps.Simulate(VfxTime, true, true);

            var rends = go.GetComponentsInChildren<Renderer>(false);
            if (rends.Length == 0) { info.AppendLine($"{label} | 렌더러 없음 | {path}"); return false; }
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            info.AppendLine($"{label} | {b.size.x:0.00}×{b.size.y:0.00}×{b.size.z:0.00} m | {path}");

            float size = Mathf.Max(b.size.x, b.size.y, b.size.z, 0.3f) * 1.15f;
            float dist = size * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.25f;
            var sheet = new Texture2D(Px * 2, Px, TextureFormat.RGBA32, false);
            var views = new[] { Quaternion.Euler(37f, 25f, 0f), Quaternion.Euler(8f, 0f, 0f) };
            for (int v = 0; v < views.Length; v++)
            {
                pru.BeginStaticPreview(new Rect(0, 0, Px, Px));
                cam.transform.rotation = views[v];
                cam.transform.position = b.center - views[v] * Vector3.forward * dist;
                cam.Render();
                var tex = pru.EndStaticPreview();
                var rt  = RenderTexture.GetTemporary(Px, Px, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(tex, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                sheet.ReadPixels(new Rect(0, 0, Px, Px), v * Px, 0);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(tex);
            }
            sheet.Apply();
            File.WriteAllBytes($"{Out}/{label}.png", sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            return true;
        }
        catch (System.Exception e)
        {
            info.AppendLine($"{label} | 예외 {e.Message} | {path}");
            return false;
        }
        finally { pru.Cleanup(); }
    }
}
#endif
