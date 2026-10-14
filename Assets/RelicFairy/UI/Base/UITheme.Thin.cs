using TMPro;
using UnityEngine;

/// <summary>톤 v2 굵기 — 큰 숫자 · 제목 Light(300) / 본문 Regular(400) / 강조 · 라벨 Medium(500). 600 이상은 쓰지 않는다.</summary>
public enum UIWeight { Light, Regular, Medium }

public static partial class UITheme
{
    /// <summary>
    /// 톤 v2 「얇은 금선」(10-07) — 의뢰서 §3 색은 그대로, 굵기만 덜어낸다.
    /// 설계 = 바탕화면 기획/런결과창·UI톤v2_시안_20261007/README.md §1. 이행한 화면은 이 값만 쓴다.
    /// </summary>
    public static class Thin
    {
        // ── 색 ──
        public static readonly Color Gold      = new(1f, 0.80f, 0.302f, 1f);          // #FFCC4D
        public static readonly Color Ink       = new(0.925f, 0.902f, 0.847f, 1f);     // #ECE6D8
        public static readonly Color GlassFill = new(0.063f, 0.059f, 0.086f, 0.55f);  // (16,15,22) 55%
        public static readonly Color Backdrop  = new(0.039f, 0.035f, 0.055f, 0.94f);  // 결과창 바탕
        public static readonly Color LineIdle   = WithA(Ink, 0.18f);
        public static readonly Color LineGold   = WithA(Gold, 0.30f);
        public static readonly Color LineActive = WithA(Gold, 0.55f);
        public static readonly Color LineReady  = WithA(Gold, 0.85f);
        public static readonly Color TickGold   = WithA(Gold, 0.90f);
        public static readonly Color Danger     = new(0.871f, 0.376f, 0.329f, 1f);
        public static readonly Color HpFill     = new(0.408f, 0.714f, 0.486f, 1f);    // 채도 낮춘 초록
        public static readonly Color HpLow      = new(0.839f, 0.420f, 0.361f, 1f);

        // ── 글자 크기 단계(1080 기준 px) — 이 밖의 크기를 만들지 않는다 ──
        public const float Caption = 13f, Small = 15f, Body = 17f, Lead = 20f, Title = 24f, H2 = 32f, H1 = 48f, Display = 76f;

        // ── 선 ──
        public const float Stroke  = 1f;   // 1080 기준 px
        public const float TickLen = 8f;   // 모서리 눈금

        public static Color WithA(Color c, float a) => new(c.r, c.g, c.b, a);
        public static Color InkA(float a) => WithA(Ink, a);

        /// <summary>CanvasScaler(1920×1080 · Match 0.5)가 이 화면에서 쓰는 배율 — 캔버스가 아직 한 번도 안 돌았어도 같은 값.</summary>
        public static float CanvasScale(float screenW, float screenH)
        {
            float lw = Mathf.Log(Mathf.Max(1f, screenW) / 1920f, 2f);
            float lh = Mathf.Log(Mathf.Max(1f, screenH) / 1080f, 2f);
            return Mathf.Pow(2f, Mathf.Lerp(lw, lh, 0.5f));
        }

        /// <summary>
        /// 선 굵기(캔버스 단위) — 화면이 작아 배율이 1보다 작으면 늘려서 <b>최소 1 물리 픽셀</b>을 지킨다(720p에서 1px 선이 사라짐 방지).
        /// </summary>
        public static float PixelStroke(float canvasScale) => Mathf.Max(Stroke, 1f / Mathf.Max(0.01f, canvasScale));
    }

    /// <summary>톤 v2 글꼴. 아직 안 올라왔으면(Addressables 로드 전 · 미등록) 기본 글꼴 — null을 돌려주지 않는다.</summary>
    public static TMP_FontAsset Font(UIWeight weight)
    {
        var f = UISkin.ThemeFonts != null ? UISkin.ThemeFonts.Get(weight) : null;
        return f != null ? f : TMP_Settings.defaultFontAsset;
    }

    /// <summary>톤 v2 글자 — 굵기 글꼴 · 가짜 굵게 없음 · 크기(자동 크기 상한도 함께) · 색 · 선택 그림자(월드 위 HUD).</summary>
    public static void StyleThinText(TMP_Text t, UIWeight weight, float size, Color color, bool shadow = false)
    {
        if (t == null) return;
        var f = Font(weight);
        if (f != null && t.font != f) t.font = f;
        t.fontStyle = FontStyles.Normal;
        t.fontSize  = size;
        if (t.enableAutoSizing) t.fontSizeMax = size;
        t.color = color;
        if (shadow) TMPOutlineHelper.ApplySoftShadow(t);
    }
}
