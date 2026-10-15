#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 재련소 대장장이 초상 굽기(10-15 · 사용자 결정 3 「초상은 방의 NPC 모델을 에디터에서 한 번 찍어 그림으로 — 크레딧 0」).
/// 재련소 NPC 프리팹을 Unity 미리보기 렌더(<see cref="PreviewRenderUtility"/> — 열린 장면과 별개 · 환경광이 있다)에 띄워
/// 몸 높이의 위쪽 절반(2등신이라 머리 + 어깨)을 정사각으로 찍는다. 처음엔 빈 미리보기 장면에서 찍어 환경광이 없어 얼굴이 검게 나왔다.
/// 배경은 검정 · 흰색 두 번 찍어 차이로 투명도를 얻는다(파이프라인이 알파를 지우는지와 상관없이 가장자리까지 투명).
/// 결과 = UI/Skin/Crucible/Art/BlacksmithPortrait.png(스프라이트) → CrucibleSkin.asset의 초상 칸. 스킨이 Addressable이라 따로 등록하지 않는다.
/// </summary>
public static class BlacksmithPortraitBaker
{
    private const string NpcPrefab = "Assets/RelicFairy/Systems/Stage/Crucible/Prefabs/CrucibleNpc.prefab";
    private const string SkinPath  = "Assets/RelicFairy/UI/Skin/Crucible/CrucibleSkin.asset";
    private const string OutPath   = "Assets/RelicFairy/UI/Skin/Crucible/Art/BlacksmithPortrait.png";
    private const string DebugDir  = "Temp/ui_shots";
    private const int    Size      = 512;
    private const float  Fov       = 18f;
    private const float  FrameK    = 0.52f;   // 몸 높이 중 위쪽 이만큼을 담는다(2등신 — 머리 + 어깨)
    // 얼굴 정면에서 이만큼 돌아 찍고 좌우를 뒤집는다 — 얼굴 오른쪽 반이 검은 칠(무늬)이라 흰 쪽이 보이게 왼쪽에서 찍고,
    // 뒤집어 얼굴이 화면 오른쪽(말풍선 쪽)을 보게 한다
    private const float  Yaw       = -25f;
    // 얼굴 정면 = NPC 뿌리 앞에서 −70°(10-15 네 방향 사진으로 잼 — 모델이 손님 쪽으로 비스듬히 서 있다)
    private const float  FaceYaw   = -70f;
    private const float  IdleTime  = 0.35f;

    [MenuItem("RelicFairy/UI/10-15 재련소 대장장이 초상 굽기 (편집 모드)")]
    private static void Bake()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[대장장이 초상] 편집 모드에서만"); return; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NpcPrefab);
        var skin   = AssetDatabase.LoadAssetAtPath<CrucibleSkinSO>(SkinPath);
        if (prefab == null || skin == null) { Debug.LogWarning($"[대장장이 초상] 프리팹 {prefab != null} · 스킨 {skin != null}"); return; }

        var pru = new PreviewRenderUtility();
        try
        {
            var go = pru.InstantiatePrefabInScene(prefab);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var anim = go.GetComponentInChildren<Animator>(true);
            if (anim == null) { Debug.LogWarning("[대장장이 초상] Animator 없음"); return; }
            string pose = Pose(anim);
            KeepOnlyModel(go, anim.transform);

            // 몸(스킨 메시) 경계 — 들고 있는 망치는 빼고 잰다(머리 위로 들면 구도가 흔들린다)
            Bounds body = default;
            bool has = false;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!r.enabled) continue;
                if (!has) { body = r.bounds; has = true; }
                else body.Encapsulate(r.bounds);
            }
            if (!has) { Debug.LogWarning("[대장장이 초상] 몸 메시 없음"); return; }
            var head = anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
            // 정면 = NPC 뿌리의 앞(모루가 놓인 쪽 · 프리팹 Anvil z +0.85). 모델 자식은 돌아 앉아 있을 수 있어 그 앞을 쓰지 않는다.
            Vector3 fwd  = Vector3.ProjectOnPlane(go.transform.forward, Vector3.up).normalized;
            float frameH = body.size.y * FrameK;
            float top    = body.max.y + body.size.y * 0.03f;
            Vector3 c    = head != null ? head.position : body.center;
            Vector3 look = new Vector3(c.x, top - frameH * 0.5f, c.z);
            float dist   = frameH / (2f * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad));
            Vector3 face = Quaternion.AngleAxis(FaceYaw, Vector3.up) * fwd;
            Vector3 dir  = Quaternion.AngleAxis(Yaw, Vector3.up) * face;

            var cam = pru.camera;
            pru.cameraFieldOfView = Fov;
            cam.fieldOfView   = Fov;
            cam.clearFlags    = CameraClearFlags.SolidColor;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane  = 20f;
            cam.transform.position = look + dir * dist + Vector3.up * frameH * 0.08f;
            cam.transform.LookAt(look);

            pru.ambientColor = new Color(0.42f, 0.40f, 0.46f);
            // 주광 = 카메라 쪽 조금 위에서(툰 셰이더는 그림자 면이 검게 끊긴다 — 얼굴이 빛 쪽을 보게) · 테 빛 = 얼굴 뒤에서(화로 빛)
            SetLight(pru.lights[0], new Color(1f, 0.90f, 0.78f), 1.2f, Quaternion.LookRotation(-(Quaternion.AngleAxis(Yaw - 10f, Vector3.up) * face) + Vector3.down * 0.45f));
            SetLight(pru.lights[1], new Color(1f, 0.62f, 0.32f), 0.7f, Quaternion.LookRotation(face + Vector3.down * 0.3f));

            // 방향 점검용 네 장(앞 · 오른쪽 · 뒤 · 왼쪽) — 실제 그림과 별개로 Temp에만
            Directory.CreateDirectory(DebugDir);
            for (int k = 0; k < 4; k++)
            {
                Vector3 d = Quaternion.AngleAxis(90f * k, Vector3.up) * face;
                cam.transform.position = look + d * dist;
                cam.transform.LookAt(look);
                var dbg = Render(pru, new Color(0.2f, 0.22f, 0.26f));
                File.WriteAllBytes(Path.Combine(DebugDir, $"Portrait_dbg_{90 * k}.png"), dbg.EncodeToPNG());
                Object.DestroyImmediate(dbg);
            }
            cam.transform.position = look + dir * dist + Vector3.up * frameH * 0.08f;
            cam.transform.LookAt(look);

            var black = Render(pru, Color.black);
            var white = Render(pru, Color.white);
            int w = black.width, h = black.height;
            var pb = black.GetPixels();
            var pw = white.GetPixels();
            var po = new Color[pb.Length];
            int opaque = 0;
            for (int i = 0; i < pb.Length; i++)
            {
                int o = (i / w) * w + (w - 1 - i % w);   // 좌우 뒤집기
                // 검정 바탕 = a·C · 흰 바탕 = a·C + (1 − a) → a = 1 − (흰 − 검정)
                float a = 1f - ((pw[i].r - pb[i].r) + (pw[i].g - pb[i].g) + (pw[i].b - pb[i].b)) / 3f;
                a = Mathf.Clamp01(a);
                if (a < 0.004f) { po[o] = Color.clear; continue; }
                po[o] = new Color(Mathf.Clamp01(pb[i].r / a), Mathf.Clamp01(pb[i].g / a), Mathf.Clamp01(pb[i].b / a), a);
                if (a > 0.5f) opaque++;
            }
            var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            outTex.SetPixels(po);
            outTex.Apply();

            File.WriteAllBytes(Path.Combine(DebugDir, "Portrait_black.png"), black.EncodeToPNG());
            File.WriteAllBytes(OutPath, outTex.EncodeToPNG());
            Object.DestroyImmediate(black);
            Object.DestroyImmediate(white);
            Object.DestroyImmediate(outTex);

            AssetDatabase.ImportAsset(OutPath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(OutPath) is TextureImporter imp)
            {
                imp.textureType         = TextureImporterType.Sprite;
                imp.spriteImportMode    = SpriteImportMode.Single;
                imp.mipmapEnabled       = false;
                imp.alphaIsTransparency = true;
                imp.filterMode          = FilterMode.Bilinear;
                imp.wrapMode            = TextureWrapMode.Clamp;
                var platform = imp.GetDefaultPlatformTextureSettings();
                platform.maxTextureSize     = 256;   // 화면 68px(4K 136) — 256이면 넉넉하다
                platform.textureCompression = TextureImporterCompression.Compressed;
                imp.SetPlatformTextureSettings(platform);
                imp.SaveAndReimport();
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(OutPath);
            var so = new SerializedObject(skin);
            var prop = so.FindProperty("blacksmithPortrait");
            if (prop != null && sprite != null)
            {
                prop.objectReferenceValue = sprite;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(skin);
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"[대장장이 초상] 끝 — 자세 {pose} · 몸 높이 {body.size.y:0.00}m · 담은 높이 {frameH:0.00}m · {w}×{h} · 불투명 픽셀 {opaque * 100f / pb.Length:0.#}% · 스프라이트 {sprite != null} · 스킨 칸 {prop != null}");
        }
        finally
        {
            pru.Cleanup();
        }
    }

    /// <summary>Idle 자세 — Animator를 편집 모드에서 한 번 돌리고, 안 움직이면 AnimationMode로 클립을 샘플링한다.</summary>
    private static string Pose(Animator anim)
    {
        var ctrl = anim.runtimeAnimatorController;
        if (ctrl == null) return "기본 자세(컨트롤러 없음)";
        AnimationClip idle = null;
        foreach (var c in ctrl.animationClips)
            if (c != null && c.name.ToLowerInvariant().Contains("idle")) { idle = c; break; }
        var probe = anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Spine) : null;
        Quaternion before = probe != null ? probe.localRotation : Quaternion.identity;

        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        anim.Rebind();
        anim.Play("Idle", 0, IdleTime);
        anim.Update(0f);
        if (probe == null || Quaternion.Angle(before, probe.localRotation) > 0.5f) return "Animator Idle";
        return idle != null ? $"기본 자세(편집 모드 Animator 무반응 · {idle.name})" : "기본 자세(Idle 클립 없음)";
    }

    /// <summary>모델(Animator 아래) 밖의 렌더러 · 파티클 · 빛은 끈다 — 모루 · 화로 불씨가 초상에 끼지 않게.</summary>
    private static void KeepOnlyModel(GameObject root, Transform model)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            if (!r.transform.IsChildOf(model)) r.enabled = false;
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            if (!ps.transform.IsChildOf(model)) ps.gameObject.SetActive(false);
        foreach (var l in root.GetComponentsInChildren<Light>(true)) l.enabled = false;
        foreach (var c in root.GetComponentsInChildren<Canvas>(true)) c.enabled = false;
    }

    private static void SetLight(Light l, Color color, float intensity, Quaternion rot)
    {
        l.color = color;
        l.intensity = intensity;
        l.transform.rotation = rot;
    }

    private static Texture2D Render(PreviewRenderUtility pru, Color bg)
    {
        pru.camera.backgroundColor = bg;
        pru.BeginStaticPreview(new Rect(0f, 0f, Size, Size));
        pru.Render(true, false);
        return pru.EndStaticPreview();
    }
}
#endif
