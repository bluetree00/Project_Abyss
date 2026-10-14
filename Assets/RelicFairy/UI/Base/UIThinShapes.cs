using System.Collections.Generic;
using UnityEngine;

/// <summary>얇은 테 모양.</summary>
public enum ThinShape { Square, Diamond }

/// <summary>
/// 톤 v2 얇은 테 스프라이트(10-07) — 그림 파일 없이 코드로 굽는다. 크기별로 한 번만 굽고 캐시한다.
/// 화면 크기의 2배 해상도로 굽고 가장자리는 1px 안티에일리어싱 — 큰 화면에서도 선이 번지지 않고,
/// 회전한 Image와 달리 마름모 변에 계단이 생기지 않는다. Q 마름모 · 결과창 테 · HUD 칸이 같은 부품을 쓴다.
/// </summary>
public static class UIThinShapes
{
    public const int Oversample = 2;

    private static readonly Dictionary<string, Sprite> s_cache = new();
    private static Sprite s_solid;

    /// <summary>흰 4×4 — Filled 이미지용(스프라이트 없는 Filled는 fillAmount를 무시하고 전체를 덮는다).</summary>
    public static Sprite Solid => s_solid != null ? s_solid
        : (s_solid = UI_RuneSelectPopup.MakeProcSprite("ThinSolid", 4, 4, (u, v) => 1f));

    /// <summary>윤곽선. w · h = 화면 크기(1080 기준 px), stroke = 선 굵기(px). 선은 스프라이트 안쪽에 온전히 들어간다.</summary>
    public static Sprite Outline(ThinShape shape, float w, float h, float stroke = UITheme.Thin.Stroke)
    {
        int tw = Px(w), th = Px(h);
        float s = stroke * Oversample;
        string key = $"O{(int)shape}_{tw}x{th}_{s:0.##}";
        if (s_cache.TryGetValue(key, out var hit) && hit != null) return hit;

        float hx = tw * 0.5f - s * 0.5f - 0.5f;   // 선 가운데가 지나는 반폭
        float hy = th * 0.5f - s * 0.5f - 0.5f;
        var sp = UI_RuneSelectPopup.MakeProcSprite("Thin" + key, tw, th, (u, v) =>
        {
            float x = (u - 0.5f) * tw, y = (v - 0.5f) * th;
            float d = shape == ThinShape.Diamond ? DiamondDist(x, y, hx, hy) : RectDist(x, y, hx, hy);
            return Mathf.Clamp01(s * 0.5f + 0.5f - Mathf.Abs(d));
        });
        s_cache[key] = sp;
        return sp;
    }

    /// <summary>채운 모양(가장자리 안티에일리어싱) — 마름모 칸 바탕 · 쿨다운 부채꼴 · 번쩍.</summary>
    public static Sprite Fill(ThinShape shape, float w, float h)
    {
        int tw = Px(w), th = Px(h);
        string key = $"F{(int)shape}_{tw}x{th}";
        if (s_cache.TryGetValue(key, out var hit) && hit != null) return hit;

        float hx = tw * 0.5f - 0.5f, hy = th * 0.5f - 0.5f;
        var sp = UI_RuneSelectPopup.MakeProcSprite("Thin" + key, tw, th, (u, v) =>
        {
            float x = (u - 0.5f) * tw, y = (v - 0.5f) * th;
            float d = shape == ThinShape.Diamond ? DiamondDist(x, y, hx, hy) : RectDist(x, y, hx, hy);
            return Mathf.Clamp01(0.5f - d);
        });
        s_cache[key] = sp;
        return sp;
    }

    /// <summary>원 테(10-08 명조형 HUD) — d = 지름(화면 px), stroke = 선 굵기. 쿨다운 · 게이지 고리는 Radial360 채움으로 쓴다.</summary>
    public static Sprite Ring(float d, float stroke = UITheme.Thin.Stroke)
    {
        int t = Px(d);
        float s = stroke * Oversample;
        string key = $"R{t}_{s:0.##}";
        if (s_cache.TryGetValue(key, out var hit) && hit != null) return hit;

        float r = t * 0.5f - s * 0.5f - 0.5f;   // 선 가운데가 지나는 반지름
        var sp = UI_RuneSelectPopup.MakeProcSprite("Thin" + key, t, t, (u, v) =>
        {
            float x = (u - 0.5f) * t, y = (v - 0.5f) * t;
            return Mathf.Clamp01(s * 0.5f + 0.5f - Mathf.Abs(Mathf.Sqrt(x * x + y * y) - r));
        });
        s_cache[key] = sp;
        return sp;
    }

    /// <summary>원판(가장자리 안티에일리어싱) — 원형 칸 바탕 · 쿨다운 부채꼴.</summary>
    public static Sprite Disc(float d)
    {
        int t = Px(d);
        string key = $"D{t}";
        if (s_cache.TryGetValue(key, out var hit) && hit != null) return hit;

        float r = t * 0.5f - 0.5f;
        var sp = UI_RuneSelectPopup.MakeProcSprite("Thin" + key, t, t, (u, v) =>
        {
            float x = (u - 0.5f) * t, y = (v - 0.5f) * t;
            return Mathf.Clamp01(r + 0.5f - Mathf.Sqrt(x * x + y * y));
        });
        s_cache[key] = sp;
        return sp;
    }

    /// <summary>네 모서리 L 눈금 — w · h는 눈금이 놓일 바깥 상자(테보다 사방 4px 크게 준다).</summary>
    public static Sprite Ticks(float w, float h, float len = UITheme.Thin.TickLen, float stroke = UITheme.Thin.Stroke)
    {
        int tw = Px(w), th = Px(h);
        float s = stroke * Oversample, l = len * Oversample;
        string key = $"T{tw}x{th}_{l:0.##}_{s:0.##}";
        if (s_cache.TryGetValue(key, out var hit) && hit != null) return hit;

        var sp = UI_RuneSelectPopup.MakeProcSprite("Thin" + key, tw, th, (u, v) =>
        {
            float px = u * tw, py = v * th;
            float ex = Mathf.Min(px, tw - px);   // 가까운 세로 가장자리까지
            float ey = Mathf.Min(py, th - py);   // 가까운 가로 가장자리까지
            if (ex > l || ey > l) return 0f;
            return Mathf.Max(Mathf.Clamp01(s + 0.5f - ex), Mathf.Clamp01(s + 0.5f - ey));
        });
        s_cache[key] = sp;
        return sp;
    }

    private static int Px(float v) => Mathf.Max(4, Mathf.RoundToInt(v * Oversample));

    /// <summary>축 정렬 사각형 테두리까지 부호 거리(안 = 음수).</summary>
    private static float RectDist(float x, float y, float hx, float hy)
    {
        float dx = Mathf.Abs(x) - hx, dy = Mathf.Abs(y) - hy;
        float outside = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude;
        float inside  = Mathf.Min(Mathf.Max(dx, dy), 0f);
        return outside + inside;
    }

    /// <summary>마름모(반대각선 rx · ry) 테두리까지 근사 부호 거리.</summary>
    private static float DiamondDist(float x, float y, float rx, float ry)
    {
        float f = Mathf.Abs(x) / rx + Mathf.Abs(y) / ry - 1f;
        return f / Mathf.Sqrt(1f / (rx * rx) + 1f / (ry * ry));
    }
}
