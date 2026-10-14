using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 색이 구워져 납품된 스프라이트에서 <b>무채색 원본</b>(명암만 남긴 흰 계열)을 만든다 — 코드가 색을 입히려고.
/// 의뢰서 §3은 「색 틴트 항목은 화이트 마스터 — 코드가 색을 입힌다」인데, 체력바 초록 · 룬/정제소의 청색 베벨은
/// 색이 구워진 채로 왔다. 곱하기 틴트로는 채도를 뺄 수 없어 원본부터 무채색으로 만든다(09-28 UI 톤 통일).
///
/// <para>텍스처가 읽기 불가여도 되도록 GPU로 복사(Blit)한 뒤 읽는다. 원본마다 한 번 만들어 캐시한다.
/// 밝기는 가장 밝은 곳이 1이 되게 늘린다 — 틴트 색이 그대로 최대 밝기가 된다.</para>
/// </summary>
public static class UISpriteMaster
{
    // ── Static ───────────────────────────────────────────────
    private static readonly Dictionary<Sprite, Sprite> s_cache   = new();
    private static readonly HashSet<Sprite>            s_masters = new();
    private static readonly Dictionary<Sprite, Sprite> s_thin    = new();
    private static readonly Dictionary<Sprite, Sprite> s_thinKeep = new();

    // ── Public Methods ───────────────────────────────────────

    /// <summary>무채색 원본. null이면 null, 이미 원본이면 그대로 돌려준다.</summary>
    public static Sprite Neutral(Sprite src)
    {
        if (src == null || s_masters.Contains(src)) return src;
        if (s_cache.TryGetValue(src, out var hit) && hit != null) return hit;

        var master = Build(src);
        if (master == null) return src;   // 만들지 못하면 원본 그대로(색은 틀려도 화면은 산다)
        s_cache[src] = master;
        s_masters.Add(master);
        return master;
    }

    /// <summary>
    /// 얇게 깎은 무채색 원본(10-08 톤 v2) — 납품 장식 테의 선을 1px 침식해(알파 3×3 최소) 장식은 남기고 굵기만 덜어낸다.
    /// 사용자 「유물 컨셉 UI는 남겨야 해 · 테두리도 조금은 사용하되 얇게」. 무채색이라 코드가 금 · 잉크로 틴트한다. null이면 null.
    /// <paramref name="keepColor"/>면 색은 그대로 두고 선만 깎는다(유물칸처럼 색 자체가 컨셉인 그림).
    /// </summary>
    public static Sprite Thinned(Sprite src, bool keepColor = false)
    {
        if (src == null) return null;
        var cache = keepColor ? s_thinKeep : s_thin;
        if (cache.TryGetValue(src, out var hit) && hit != null) return hit;

        var thin = Build(src, erode: true, neutral: !keepColor);
        if (thin == null) return keepColor ? src : Neutral(src);   // 만들지 못하면 굵은 원본(화면은 산다)
        cache[src] = thin;
        s_masters.Add(thin);
        return thin;
    }

    // ── Private Methods ──────────────────────────────────────

    private static Sprite Build(Sprite src, bool erode = false, bool neutral = true)
    {
        var tex = src.texture;
        if (tex == null) return null;

        Rect r;
        try { r = src.textureRect; }   // 촘촘히 묶인(tight) 아틀라스면 예외 — 그땐 rect
        catch { r = src.rect; }
        int x = Mathf.FloorToInt(r.x), y = Mathf.FloorToInt(r.y);
        int w = Mathf.Max(1, Mathf.RoundToInt(r.width)), h = Mathf.Max(1, Mathf.RoundToInt(r.height));

        // sRGB 렌더 타깃 — 선형 색공간에서도 읽어 온 바이트가 원본과 같게(읽을 때 디코드, 쓸 때 다시 인코드).
        var rt   = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active;
        Texture2D copy;
        try
        {
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            copy = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            copy.ReadPixels(new Rect(x, y, w, h), 0, 0, false);
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
        }

        var px = copy.GetPixels32();
        if (neutral) ToNeutral(px);
        if (erode) Erode(px, w, h);
        copy.SetPixels32(px);
        copy.wrapMode   = TextureWrapMode.Clamp;
        copy.filterMode = FilterMode.Bilinear;
        copy.name       = src.name + (erode ? (neutral ? " (Thin)" : " (Thin Color)") : " (Neutral)");
        copy.hideFlags  = HideFlags.HideAndDontSave;
        copy.Apply(false, true);

        var pivot  = new Vector2(src.pivot.x / src.rect.width, src.pivot.y / src.rect.height);
        var master = Sprite.Create(copy, new Rect(0f, 0f, w, h), pivot, src.pixelsPerUnit,
                                   0, SpriteMeshType.FullRect, src.border);
        master.name      = copy.name;
        master.hideFlags = HideFlags.HideAndDontSave;
        return master;
    }

    /// <summary>명암만 남긴 흰 계열 — 가장 밝은 곳이 1(틴트 색이 그대로 최대 밝기).</summary>
    private static void ToNeutral(Color32[] px)
    {
        float max = MaxLuma(px, 128);
        // 불투명 화소가 하나도 없는 옅은 그림(Artsystack icon_slot_active — 최대 알파 0.31)은 밝기가 안 올라 고리가 흐렸다(10-13) — 반투명 화소로 잡는다
        if (max <= 0.01f) max = MaxLuma(px, 11);
        float k = max > 0.01f ? 1f / max : 1f;
        for (int i = 0; i < px.Length; i++)
        {
            byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(Luma(px[i]) * k * 255f), 0, 255);
            px[i] = new Color32(v, v, v, px[i].a);
        }
    }

    private static float MaxLuma(Color32[] px, byte minAlpha)
    {
        float max = 0f;
        for (int i = 0; i < px.Length; i++)
            if (px[i].a >= minAlpha) max = Mathf.Max(max, Luma(px[i]));
        return max;
    }

    private static float Luma(Color32 c) => (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;

    /// <summary>알파 3×3 최소(1px 침식) — 선 양쪽이 1px씩 얇아진다. 바깥은 투명으로 본다.</summary>
    private static void Erode(Color32[] px, int w, int h)
    {
        var a = new byte[px.Length];
        for (int i = 0; i < px.Length; i++) a[i] = px[i].a;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                byte m = 255;
                for (int dy = -1; dy <= 1 && m > 0; dy++)
                {
                    int sy = y + dy;
                    if (sy < 0 || sy >= h) { m = 0; break; }
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int sx = x + dx;
                        if (sx < 0 || sx >= w) { m = 0; break; }
                        byte v = a[sy * w + sx];
                        if (v < m) m = v;
                    }
                }
                px[y * w + x].a = m;
            }
        }
    }
}
