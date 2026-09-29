using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 전 화면 공통 언어 — 의뢰서 §3(`ui-design-commission.md`) 「인디고 글래스 판 · 금 가는 선 · 금 주 버튼 · 양피지 잉크」.
/// 09-28 UI 톤 진단: 화면마다 자기 색(지역 상수 · ShopUIStyle · AltarPalette)을 따로 들고 있어 바탕 온도가 네 갈래,
/// 버튼이 여섯 가지로 갈렸다. 코드로 그리는 화면은 여기 값과 도우미만 쓴다.
///
/// <para>값은 의뢰서 §3을 상점 계열이 먼저 구현한 것 그대로다 — <see cref="ShopUIStyle"/>의 같은 이름 색은 이곳을 가리킨다
/// (상점·룬 획득·재련소 화면은 바뀌지 않는다). 가게 나무판 · 서약 양피지는 「가게 · 문서」 소품이라 이 규칙 밖이다.</para>
/// </summary>
public static class UITheme
{
    // ── 바탕 ─────────────────────────────────────────────
    public static readonly Color Veil     = new(0f, 0f, 0f, 0.80f);
    public static readonly Color Window   = new(0.055f, 0.050f, 0.085f, 0.992f);   // 인디고차콜 창
    public static readonly Color Band     = new(0.10f, 0.085f, 0.135f, 1f);        // 창 안의 판 · 띠
    /// <summary>화면 위에 떠 있는 판(대사 띠 · 알림 · 배지) — 뒤 월드가 살짝 비친다.</summary>
    public static readonly Color Glass    = new(0.045f, 0.040f, 0.070f, 0.86f);

    // ── 선 ───────────────────────────────────────────────
    public static readonly Color Bronze   = new(0.52f, 0.40f, 0.20f, 1f);          // 창 테두리(묵은 청동)
    public static readonly Color GoldLine = new(0.91f, 0.73f, 0.33f, 0.55f);       // 금 가는 선

    // ── 글자 ─────────────────────────────────────────────
    public static readonly Color Gold     = UIPalette.Gold;                        // 제목 · 누를 수 있는 것
    public static readonly Color Ink      = new(0.93f, 0.91f, 0.85f, 1f);          // 양피지 잉크(본문)
    public static readonly Color Mute     = new(0.62f, 0.60f, 0.64f, 1f);          // 보조 설명

    // ── 버튼 틴트(무채색 베벨 위에 곱한다) ─────────────────
    // 베벨 원본은 몸통과 테두리 밝기 차가 작아 틴트가 거의 그대로 몸통 색이 된다 — 0.86 금은 겨자색 파스텔로 떴다(09-28 실측
    // 몸통 200,154,76). 재련소 [강화하기]처럼 짙은 청동 몸통 + 밝은 잉크 글자가 되게 눌러 잡는다.
    /// <summary>주 버튼 — 짙은 청동 금.</summary>
    public static readonly Color CtaTint       = new(0.58f, 0.42f, 0.18f, 1f);
    /// <summary>주 버튼이지만 지금 누를 수 없음.</summary>
    public static readonly Color CtaTintOff    = new(0.32f, 0.30f, 0.28f, 0.85f);
    /// <summary>보조 버튼 — 먹빛.</summary>
    public static readonly Color SecondaryTint = new(0.30f, 0.29f, 0.34f, 1f);
    /// <summary>되돌릴 수 없는 버튼(게임 종료 등) — 가라앉은 진홍.</summary>
    public static readonly Color DangerTint    = new(0.46f, 0.19f, 0.17f, 1f);

    // ── Static ───────────────────────────────────────────
    private static Sprite s_softBand;

    // ── Public Methods ───────────────────────────────────

    /// <summary>
    /// 글자 없는 베벨 버튼 원본(무채색) — 정제소 [돌리기] 아트에서 색을 뺀 것. 룬 획득 · 정제소 · 룬판 · 코드 그림 화면의
    /// 버튼이 모두 이 한 모양을 틴트만 달리해 쓴다. 스킨이 아직 안 들어왔으면 null(호출측은 색 판 폴백).
    /// </summary>
    public static Sprite ButtonBevel
    {
        get
        {
            var spin = UISkin.Refinery?.spinButton;
            return spin != null && spin.Length > 0 ? UISpriteMaster.Neutral(spin[0]) : null;
        }
    }

    /// <summary>
    /// 버튼 한 칸을 공통 모양으로 — 무채색 베벨 + 틴트. 베벨이 없으면 둥근 판(틴트를 어둡게 눌러) 폴백.
    /// 크기는 호출측이 이미 잡아 둔 상태여야 한다(9-slice 배율을 칸 크기로 정한다).
    /// </summary>
    public static void StyleButton(Image img, Color tint)
    {
        if (img == null) return;
        var bevel = ButtonBevel;
        if (bevel != null)
        {
            ShopUIStyle.Skin(img, bevel, sliced: true, tint: tint);
            return;
        }
        img.sprite = UIProceduralSprites.RoundedRect(radius: 10f, feather: 2f);
        img.type   = Image.Type.Sliced;
        img.color  = new Color(tint.r * 0.45f, tint.g * 0.45f, tint.b * 0.45f, tint.a);
    }

    /// <summary>
    /// 판 한 장을 글래스로 — 둥근 모서리 채움 + 금 가는 선(자식 「Edge」, 한 번만 붙는다).
    /// fill은 판 색(<see cref="Window"/> · <see cref="Band"/> · <see cref="Glass"/>), edge는 선 색(기본 <see cref="GoldLine"/>).
    /// </summary>
    public static Image StylePanel(Image img, Color fill, Color? edge = null, float radius = 14f)
    {
        if (img == null) return null;
        img.sprite = UIProceduralSprites.RoundedRect(radius: radius, feather: 2f);
        img.type   = Image.Type.Sliced;
        img.color  = fill;

        var e = img.transform.Find("Edge");
        Image line;
        if (e == null)
        {
            var go = new GameObject("Edge", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(img.transform, false);
            line = go.AddComponent<Image>();
            line.raycastTarget = false;
            var rt = line.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            // 맨 뒤 형제(=맨 위)로 둔다 — 가장자리 1.5px 선이라 안쪽 내용을 가리지 않고, 앞 형제 순서로 찾는 코드를 흔들지 않는다.
        }
        else line = e.GetComponent<Image>();

        line.sprite = UIProceduralSprites.RoundedOutline(radius: radius, stroke: 1.5f);
        line.type   = Image.Type.Sliced;
        line.color  = edge ?? GoldLine;
        return img;
    }

    /// <summary>
    /// ShopUIStyle.MakeFrame(바깥 테두리 + 안쪽 채움)으로 지은 판을 둥글게 — 색은 그대로 둔다.
    /// 네모 판 + 윗단 색 띠가 웹 카드처럼 읽혔다(09-28 UI 톤 진단 D4).
    /// </summary>
    public static void RoundFrame(Image inner, float radius = 12f)
    {
        if (inner == null) return;
        inner.sprite = UIProceduralSprites.RoundedRect(radius: Mathf.Max(2f, radius - 2f), feather: 1.5f);
        inner.type   = Image.Type.Sliced;
        if (inner.transform.parent != null && inner.transform.parent.TryGetComponent<Image>(out var outer))
        {
            outer.sprite = UIProceduralSprites.RoundedRect(radius: radius, feather: 1.5f);
            outer.type   = Image.Type.Sliced;
        }
    }

    /// <summary>
    /// MakeFrame으로 지은 버튼 — 바깥 테두리는 투명(클릭은 그대로 받는다), 안쪽을 공통 베벨로.
    /// 모서리를 깎은 아트 뒤로 코드 빌더의 네모 테두리가 비치던 것도 이것으로 없앤다. 베벨을 입혔으면 true.
    /// </summary>
    public static bool StyleFrameButton(Image inner, Color tint)
    {
        if (inner == null || ButtonBevel == null) return false;
        if (inner.transform.parent != null && inner.transform.parent.TryGetComponent<Image>(out var outer))
            outer.color = Color.clear;
        StyleButton(inner, tint);
        return true;
    }

    /// <summary>
    /// 좌우 끝이 흐려지는 띠 — 대사 · 종료 문구처럼 화면을 가로지르는 글자 뒤에. 판 모서리가 드러나지 않는다.
    /// </summary>
    public static Sprite SoftBand => s_softBand != null ? s_softBand
        : (s_softBand = UI_RuneSelectPopup.MakeProcSprite("UITheme_SoftBand", 128, 32, (u, v) =>
          {
              float x = Edge(Mathf.Abs(u * 2f - 1f), 0.55f, 1f);
              float y = Edge(Mathf.Abs(v * 2f - 1f), 0.45f, 1f);
              return (1f - x) * (1f - y);
          }));

    /// <summary>글자 표준 — 기본 글꼴(DNF) · 색 · 부드러운 그림자(09-27 글자 정본).</summary>
    public static void StyleText(TMP_Text t, Color color)
    {
        if (t == null) return;
        if (TMP_Settings.defaultFontAsset != null && t.font != TMP_Settings.defaultFontAsset)
            t.font = TMP_Settings.defaultFontAsset;
        t.color = color;
        TMPOutlineHelper.ApplySoftShadow(t);
    }

    // ── Private Methods ──────────────────────────────────

    /// <summary>셰이더 smoothstep과 같은 가장자리 함수 — Mathf.SmoothStep(a,b,t)는 보간이라 이 용도로 못 쓴다.</summary>
    private static float Edge(float x, float e0, float e1)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }
}
