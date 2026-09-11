using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 아트 없이 코드로 굽는 작은 UI 스프라이트 모음 — 둥근 판·둥근 테두리·원·고리·체크.
///
/// <para>디자이너 아트가 오기 전에도 와이어프레임 모습이 나와야 하고, 폰트 화이트리스트에 없는
/// 기호(✔ 같은 것)는 글자로 못 그린다. 전부 한 번만 구워 정적으로 캐싱한다(96×96·64×64라 부담 없음).</para>
/// </summary>
public static class UIProceduralSprites
{
    private static readonly Dictionary<string, Sprite> s_cache = new();

    /// <summary>둥근 사각 판(흰색 · 알파만). 가장자리 <paramref name="feather"/>px 소프트. 9-slice 경계 = 반경 + 소프트.</summary>
    public static Sprite RoundedRect(float radius = 28f, float feather = 14f, int size = 96)
        => Get($"rr_{radius}_{feather}_{size}", () => Bake(size, radius + feather,
            (x, y) =>
            {
                float d = RoundedSdf(x, y, size, radius, feather);
                return Mathf.Clamp01(1f - d / feather);
            }));

    /// <summary>둥근 사각 <b>테두리</b>(흰색). 선 굵기 <paramref name="stroke"/>px, 안쪽은 투명. 9-slice 경계 = 반경 + 4.</summary>
    public static Sprite RoundedOutline(float radius = 20f, float stroke = 2f, int size = 96)
        => Get($"ro_{radius}_{stroke}_{size}", () => Bake(size, radius + 4f,
            (x, y) =>
            {
                float d = RoundedSdf(x, y, size, radius, 4f);        // 바깥 경계 기준 거리
                float band = Mathf.Abs(d + stroke * 0.5f) - stroke * 0.5f;   // 선 중심에서의 거리
                return Mathf.Clamp01(1f - band);                      // 1px 안티에일리어싱
            }));

    /// <summary>꽉 찬 원(흰색).</summary>
    public static Sprite Circle(int size = 64)
        => Get($"circle_{size}", () => Bake(size, 0f, (x, y) =>
        {
            float r = size * 0.5f - 1f;
            float d = new Vector2(x + 0.5f - size * 0.5f, y + 0.5f - size * 0.5f).magnitude - r;
            return Mathf.Clamp01(1f - d);
        }));

    /// <summary>고리(흰색). 선 굵기는 지름의 <paramref name="thickness01"/> 비율.</summary>
    public static Sprite Ring(float thickness01 = 0.12f, int size = 64)
        => Get($"ring_{thickness01}_{size}", () => Bake(size, 0f, (x, y) =>
        {
            float r  = size * 0.5f - 1f;
            float t  = size * thickness01;
            float d  = new Vector2(x + 0.5f - size * 0.5f, y + 0.5f - size * 0.5f).magnitude;
            float band = Mathf.Abs(d - (r - t * 0.5f)) - t * 0.5f;
            return Mathf.Clamp01(1f - band);
        }));

    /// <summary>체크 표시(흰색 · 배경 투명). 폰트에 ✔가 없어 그림으로 그린다. 원 안에 얹는 용도.</summary>
    public static Sprite Check(int size = 64)
        => Get($"check_{size}", () => Bake(size, 0f, (x, y) =>
        {
            // 두 선분: (0.28,0.52)→(0.44,0.36)  /  (0.44,0.36)→(0.74,0.68)   — y는 위가 1
            var p  = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
            var a  = new Vector2(0.28f, 0.52f); var b = new Vector2(0.44f, 0.36f); var c = new Vector2(0.74f, 0.68f);
            float d = Mathf.Min(SegDist(p, a, b), SegDist(p, b, c)) * size;
            float half = size * 0.065f;                                 // 선 굵기 ≈ 13% 지름
            return Mathf.Clamp01(1f - (d - half));
        }));

    // ── 내부 ────────────────────────────────────────────────────────────

    private static float RoundedSdf(int x, int y, int size, float radius, float inset)
    {
        float half = size * 0.5f - inset;
        float cx = Mathf.Abs(x + 0.5f - size * 0.5f) - (half - radius);
        float cy = Mathf.Abs(y + 0.5f - size * 0.5f) - (half - radius);
        return new Vector2(Mathf.Max(cx, 0f), Mathf.Max(cy, 0f)).magnitude - radius;
    }

    private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return (p - (a + ab * t)).magnitude;
    }

    private static Sprite Get(string key, System.Func<Sprite> bake)
    {
        if (s_cache.TryGetValue(key, out var s) && s != null) return s;
        s = bake(); s.name = "Procedural/" + key; s_cache[key] = s; return s;
    }

    private static Sprite Bake(int size, float border, System.Func<int, int, float> alphaAt)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Procedural",
        };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alphaAt(x, y) * 255f));
        tex.SetPixels32(px);
        tex.Apply(false, false);
        var b = Vector4.one * border;
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                             SpriteMeshType.FullRect, border > 0f ? b : Vector4.zero);
    }
}
