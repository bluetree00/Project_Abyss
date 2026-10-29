#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-26 사용자 「힉스필드로 실제 오브젝트 모양 살려서 무기 선택 · 무기 능력 선택 팝업」(재련소 「무엇을 재련할까」 · 증강 고르기) — 힉스필드 참조용으로 실제 무기 모델을 크게 찍는다.
/// 미리보기 장면(PreviewRenderUtility)에서 찍으므로 열린 씬은 건드리지 않는다. 메시만 남기고(이펙트 끔) 가장 긴 축을 세로로 세운 뒤
/// 넓은 면이 보이게 돌려 중간 회색 바탕 1024×2048로 저장한다. 결과 Temp/weapon_art/*.png · 로그 「[WeaponArt] 끝」.
/// </summary>
public static class WeaponArtRenderEditor
{
    private const string Tag = "[WeaponArt]";
    private const int W = 1024, H = 2048;

    private static readonly (string key, string path)[] Targets =
    {
        ("caliburn_katana",   "Assets/RelicFairy/Weapon/Katana/Prefabs/T1_Katana_Weapon.prefab"),
        ("clarent_greatsword","Assets/RelicFairy/Weapon/Greatsword/Prefabs/T1_Greatsword_Weapon.prefab"),
        ("nameless_sword",    "Assets/RelicFairy/Weapon/Nameless/Prefabs/T0_Nameless_Weapon.prefab"),
        ("bow",               "Assets/RelicFairy/Weapon/Bow/Prefabs/T1_Bow_Weapon.prefab"),
        ("crossbow",          "Assets/RelicFairy/Weapon/Crossbow/Prefabs/T1_Crossbow_Weapon.prefab"),
    };

    [MenuItem("RelicFairy/Tools/10-26 무기 그림 기준 렌더 (무형검 · 카타나 · 대검 · 활 · 석궁)")]
    private static void Run()
    {
        Directory.CreateDirectory("Temp/weapon_art");
        foreach (var (key, path) in Targets)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning($"{Tag} 없음 {path}"); continue; }
            Render(prefab, $"Temp/weapon_art/{key}.png");
        }
        Debug.Log($"{Tag} 끝 — Temp/weapon_art");
    }

    private static void Render(GameObject prefab, string outPath)
    {
        var pru = new PreviewRenderUtility();
        try
        {
            var go = pru.InstantiatePrefabInScene(prefab);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (r is not MeshRenderer && r is not SkinnedMeshRenderer) r.enabled = false;

            // 가장 긴 축을 세로로 — 세 방향 중 세로가 가장 긴 것
            Quaternion best = Quaternion.identity; float bestY = -1f;
            foreach (var q in new[] { Quaternion.identity, Quaternion.Euler(0f, 0f, 90f), Quaternion.Euler(90f, 0f, 0f) })
            {
                go.transform.rotation = q;
                var b = Bounds(go);
                if (b.size.y > bestY) { bestY = b.size.y; best = q; }
            }
            go.transform.rotation = best;
            // 넓은 면이 카메라 쪽(가로 폭이 넓은 쪽)으로
            var b0 = Bounds(go);
            go.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * best;
            var b90 = Bounds(go);
            if (b0.size.x >= b90.size.x) go.transform.rotation = best;
            var bounds = Bounds(go);
            // 칼끝이 위 — 무게 중심(손잡이 쪽 메시가 몰림)이 아래 절반이 되게: 메시 꼭짓점 평균이 위쪽이면 뒤집는다
            if (VertexCenterY(go) > bounds.center.y) { go.transform.rotation = Quaternion.Euler(0f, 0f, 180f) * go.transform.rotation; bounds = Bounds(go); }

            var cam = pru.camera;
            cam.fieldOfView      = 18f;
            cam.clearFlags       = CameraClearFlags.SolidColor;
            cam.backgroundColor  = new Color(0.36f, 0.36f, 0.37f, 1f);
            cam.nearClipPlane    = 0.01f;
            cam.farClipPlane     = 100f;
            float half = Mathf.Max(bounds.extents.y, bounds.extents.x * H / W) * 1.08f;
            float dist = half / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            cam.transform.position = bounds.center + new Vector3(0f, 0f, -dist);
            cam.transform.LookAt(bounds.center);

            pru.lights[0].intensity = 1.25f;
            pru.lights[0].transform.rotation = Quaternion.Euler(35f, 40f, 0f);
            pru.lights[1].intensity = 0.7f;
            pru.lights[1].transform.rotation = Quaternion.Euler(-20f, -140f, 0f);
            pru.ambientColor = new Color(0.32f, 0.32f, 0.34f);

            pru.BeginStaticPreview(new Rect(0, 0, W, H));
            pru.Render(true);
            var tex = pru.EndStaticPreview();
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log($"{Tag} {outPath} — 크기 {bounds.size}");
        }
        finally { pru.Cleanup(); }
    }

    private static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = new Bounds(go.transform.position, Vector3.zero);
        bool any = false;
        foreach (var r in rs)
        {
            if (!r.enabled) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    /// <summary>메시 꼭짓점의 월드 높이 평균 — 손잡이 · 코등이처럼 꼭짓점이 몰린 쪽이 낮게 나온다.</summary>
    private static float VertexCenterY(GameObject go)
    {
        double sum = 0; long n = 0;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
            var m = mf.transform.localToWorldMatrix;
            foreach (var v in mf.sharedMesh.vertices) { sum += m.MultiplyPoint3x4(v).y; n++; }
        }
        return n == 0 ? Bounds(go).center.y : (float)(sum / n);
    }
}
#endif
