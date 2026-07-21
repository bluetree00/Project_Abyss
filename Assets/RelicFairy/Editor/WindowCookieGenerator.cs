#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Gothic 이중 첨두 아치 창문 쿠키 텍스처 생성기.
/// Tools/Create Window Cookie 메뉴로 실행.
/// </summary>
public static class WindowCookieGenerator
{
    private const string SavePath = "Assets/RelicFairy/_Imported/Gothic_Interior/Environment/Asset/Materials/T_GothicWindowCookie.png";

    [MenuItem("Tools/Create Window Cookie")]
    public static void CreateAndAssign()
    {
        var tex = Generate(512, 512);

        var bytes = tex.EncodeToPNG();
        System.IO.File.WriteAllBytes(SavePath, bytes);
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(SavePath);
        var imp = AssetImporter.GetAtPath(SavePath) as TextureImporter;
        if (imp != null)
        {
            // URP에서는 Cookie 타입이 아닌 Default(일반 텍스처)여야 쿠키 아틀라스에 등록됨
            imp.textureType        = TextureImporterType.Default;
            imp.wrapMode           = TextureWrapMode.Clamp;
            imp.filterMode         = FilterMode.Bilinear;
            imp.alphaSource        = TextureImporterAlphaSource.FromGrayScale;
            imp.alphaIsTransparency = false;
            imp.sRGBTexture        = false;
            imp.mipmapEnabled      = true;
            AssetDatabase.ImportAsset(SavePath);
        }

        AssignToLight(SavePath);
        Debug.Log($"[WindowCookie] 생성 완료: {SavePath}");
    }

    // ── 텍스처 생성 ─────────────────────────────────────────────────────────

    private static Texture2D Generate(int w, int h)
    {
        var tex    = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var pixels = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            float v = (float)y / (h - 1);      // 0=아래, 1=위
            for (int x = 0; x < w; x++)
            {
                float u = (float)x / (w - 1);  // 0=왼쪽, 1=오른쪽
                float bright = IsInsideWindow(u, v) ? 1f : 0f;
                pixels[y * w + x] = new Color(bright, bright, bright, 1f);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // u,v ∈ [0,1]. (0,0)=좌하단, (1,1)=우상단
    private static bool IsInsideWindow(float u, float v)
    {
        // 외곽 프레임
        const float FX    = 0.09f;   // 좌우 프레임 두께
        const float FBOT  = 0.04f;   // 하단 프레임
        const float MUL   = 0.028f;  // 중앙 멀리언 반폭
        const float TRANS = 0.40f;   // 트랜섬 위치 (v)
        const float TRSH  = 0.022f;  // 트랜섬 반두께

        if (u < FX || u > 1f - FX) return false;
        if (v < FBOT)               return false;

        const float CX = 0.5f;

        // ── 아치 영역 (위쪽 40%) ───────────────────────────────────────────
        // v > 0.60 구간: 이중 첨두 아치 (Gothic 쌍 랜싯)
        const float ARCH_SPRING = 0.60f;
        const float ARCH_R      = 0.28f;   // 반원 반지름 (정규화)
        // 왼쪽 랜싯 아치 중심 X = 좌측 패널 중앙
        float leftPaneCX  = (FX + CX - MUL) * 0.5f;
        float rightPaneCX = (CX + MUL + (1f - FX)) * 0.5f;

        if (v > ARCH_SPRING)
        {
            float dv    = v - ARCH_SPRING;
            float dleft = Mathf.Sqrt((u - leftPaneCX) * (u - leftPaneCX) + dv * dv);
            float dright = Mathf.Sqrt((u - rightPaneCX) * (u - rightPaneCX) + dv * dv);

            // 첨두 아치: 두 원이 겹치는 영역 안에 있어야 함
            bool inLeft  = (u <= CX - MUL) && dleft  <= ARCH_R;
            bool inRight = (u >= CX + MUL) && dright <= ARCH_R;
            if (!inLeft && !inRight) return false;
        }

        // ── 서브 아치 영역 (트랜섬~ARCH_SPRING) ────────────────────────────
        // 두 패널 각각 위에 작은 반원 아치
        if (v > TRANS + TRSH && v <= ARCH_SPRING)
        {
            float subSpring = TRANS + TRSH;
            float subR      = (CX - MUL - FX) * 0.5f;   // 패널 폭의 절반
            float dv        = v - subSpring;

            float paneCX = u < CX ? leftPaneCX : rightPaneCX;
            float d      = Mathf.Sqrt((u - paneCX) * (u - paneCX) + dv * dv);

            // 아치 모서리: 아치 높이(subR)에서 원 밖 영역은 제거
            if (v > subSpring + subR * 0.5f && d > subR) return false;
        }

        // ── 멀리언 (중앙 수직 바) ──────────────────────────────────────────
        if (Mathf.Abs(u - CX) < MUL) return false;

        // ── 트랜섬 (수평 바) ───────────────────────────────────────────────
        if (Mathf.Abs(v - TRANS) < TRSH) return false;

        return true;
    }

    // ── 씬의 GlassWindow_Light 에 쿠키 할당 ─────────────────────────────────

    [MenuItem("Tools/Fix Window Light Position")]
    public static void FixLightPosition()
    {
        var go = GameObject.Find("GlassWindow_Light");
        if (go == null) { Debug.LogError("[WindowCookie] GlassWindow_Light not found"); return; }

        go.transform.SetParent(null, true);

        // 통로 내부 배치 (창문을 통해 빛이 이미 들어온 위치 시뮬레이션)
        // Euler(70, 270, 0) → forward=(-0.34, -0.94, 0) : 주로 아래, 약간 창문 방향
        // 바닥 도달: Y=5에서 5/0.94≈5.3유닛 이동 → X=2-0.34*5.3≈0.2 (통로 중심)
        go.transform.position = new Vector3(2f, 5f, -64.67f);
        go.transform.rotation = Quaternion.Euler(70f, 270f, 0f);

        var light = go.GetComponent<Light>();
        if (light != null)
        {
            light.spotAngle      = 45f;
            light.innerSpotAngle = 25f;
            light.range          = 8f;
            light.intensity      = 18000f;
        }

        EditorUtility.SetDirty(go);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log($"[WindowCookie] 라이트 재배치 완료 | pos={go.transform.position} | fwd={go.transform.forward}");
    }

    private static void AssignToLight(string texPath)
    {
        var cookie = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
        if (cookie == null)
        {
            Debug.LogWarning("[WindowCookie] 텍스처 로드 실패: " + texPath);
            return;
        }

        var lightGO = GameObject.Find("GlassWindow_Light");
        if (lightGO == null)
        {
            Debug.LogWarning("[WindowCookie] GlassWindow_Light GO를 찾을 수 없습니다.");
            return;
        }

        var light = lightGO.GetComponent<Light>();
        if (light == null)
        {
            Debug.LogWarning("[WindowCookie] Light 컴포넌트 없음.");
            return;
        }

        light.cookie = cookie;
        EditorUtility.SetDirty(lightGO);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(lightGO.scene);
        Debug.Log("[WindowCookie] GlassWindow_Light 쿠키 할당 완료.");
    }
}
#endif
