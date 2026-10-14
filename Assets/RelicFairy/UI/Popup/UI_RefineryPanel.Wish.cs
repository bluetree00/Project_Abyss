using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 정제소 v3 「아발론의 밤」 — 원신 기원식 전체 화면 무대(설계 정제소·재련소_v3 1-1, 10-14). 시간표는 <c>UI_RefineryPanel.Gacha.cs</c>.
/// v4(10-14 저녁) — 정제소를 열면 처음부터 이 무대다: 제단 화면 묶음(<c>UI_RefineryPanel.Altar.cs</c>)이 밤하늘 위에 서 있고,
/// 공개는 그림 가운데 · 글은 그 아래 가운데. 빛살은 가운데 짙고 바깥으로 사라지는 그림(<see cref="SoftRays"/>).
///
/// <para>패널 아래 <b>자기 캔버스</b>(덮어쓴 정렬 = 패널 + 1)를 둔다 — 팝업 캔버스가 화면 공간 오버레이라 3D 파티클이 보이지 않으므로
/// 전부 UI 그림이다: 가장자리 어둡힘 · 인디고 하늘(그라데이션) · 절차 별 점 두 겹 · 원석 · 별빛(머리 · 꼬리 · 빛무리) · 등급 색 섬광 ·
/// 빛살 · 공개 카드(실루엣 흰 테 → 채색 360px · 왼쪽 큰 이름 · 속성 · 효과 · ◆) · 끝 버튼. 새 그림 파일은 없다(코드로 굽는다).</para>
///
/// <para>「두 장 중 하나」는 카드 둘을 나란히(높은 쪽이 오른쪽) 공개하고 기존 룬 선택 화면으로 넘긴다.
/// 끝 상태에서 빈 곳을 누르면 공개를 걷고 같은 하늘의 제단 화면으로 돌아간다(룬은 이미 보관함에 있다 — ESC는 예전처럼 정제소를 닫는다).</para>
/// </summary>
public sealed partial class UI_RefineryPanel
{
    // ── 배치(1080 화면 px · 화면 가운데 기준) ──
    private const float LandX       =    0f;   // 착지 = 공개 룬 자리
    private const float LandY       = AltarY;   // v4 — 제단 = 착지 = 공개 자리(원석이 떠오른 곳에 별이 내려와 룬이 선다)
    private const float IconSize    =  300f;   // 공개 룬 그림 — v4 글을 아래로 옮겨 360 → 300
    private const float RimPx       =    4f;   // 실루엣 흰 테 굵기
    private const float InfoWide    =  860f;   // 한 장 — 그림 아래 가운데 글 묶음 폭
    private const float InfoTextX   =    0f;
    private const float InfoTextY   = -185f;   // 묶음 중심(위끝 0 · 아래끝 -370)
    private const float TwinCx      =  260f;   // 두 장 — 카드 중심 ±260
    private const float TwinIcon    =  280f;
    private const float TwinIconY   =  150f;
    private const float TwinWide    =  440f;
    private const float TwinTextY   = -150f;
    private const float GemSide     =   16f;   // ◆ = 16px 정사각을 45° 돌린 것(대각 22.6)
    private const float GemPitch    =   30f;
    private const float BtnW        =  320f;
    private const float BtnH        =   68f;
    private const float BtnY        = -430f;
    private const float BtnDx       =  180f;   // [다시 정제] 왼쪽 · [룬판에 놓기] 오른쪽(다음 행동은 오른쪽)
    private const float BtnFont     =   22f;
    private const float OreFromY    = AltarY;          // 원석이 제단 가운데(제단 화면에 떠 있던 자리)에서
    private const float OreSize     = IdleOrePx;
    private const float StarHeadPx  =   84f;
    private const float StarCorePx  =   26f;
    private const float StarTailW   =   20f;
    private const float StarGlowW   =   70f;
    private const float StarTailMin =  140f;
    private const float StarTailMax =  620f;
    private const float BeamW       =   26f;   // 방출 빛줄기(원석 자리 → 화면 위)
    private const float BeamLen     =  900f;
    private const float RaysPx      = 1500f;
    private const float GoldRaysPx  = 1800f;
    private const float DismissGrace = 0.40f;  // 입력이 열린 직후의 클릭(건너뛰기 연타)은 공개 닫기로 받지 않는다
    private const int   SparkCount  =   16;    // 전설 금빛 불티

    private static readonly Vector2 Land     = new(LandX, LandY);
    private static readonly Vector2 StarFrom = new(860f, 470f);   // 오른쪽 위 — 0.9초(색 확정)에도 화면 안에 보이는 자리
    private static readonly Color SkyHigh  = new(0.020f, 0.024f, 0.075f, 1f);   // 위 — 거의 검은 남색
    private static readonly Color SkyLow   = new(0.075f, 0.070f, 0.200f, 1f);   // 아래 — 인디고
    private static readonly Color Horizon  = new(0.300f, 0.240f, 0.550f, 0.45f); // 지평 보랏빛
    private static readonly Color StarTint = new(0.86f, 0.90f, 1f, 1f);
    private static readonly Color Shade    = new(0.03f, 0.03f, 0.08f, 1f);      // 실루엣 속

    // ── Static ──
    private static Sprite s_edge, s_skyRamp, s_stars, s_starsFaint, s_tail, s_softRays;
    private static readonly Dictionary<Sprite, Sprite> s_silhouettes = new();

    // ── Private ──
    private RectTransform _wish;
    private CanvasGroup   _wNight;
    private Image _wEdge, _wStars, _wStarsFaint, _wRays, _wRaysGold, _wOre, _wOreGlow, _wBeam;
    private Image _wHead, _wCore, _wTail, _wTailGlow, _wPing, _wShock, _wShock2, _wGoldRing, _wBurst, _wFlash;
    private readonly Image[] _wSparks = new Image[SparkCount];
    private TMP_Text _wSkip, _wCaption, _wHint, _wAgainLabel;
    private RectTransform _wButtons;
    private CanvasGroup   _wButtonsGroup;
    private Image   _wAgain, _wPlace;
    private Image[] _wAgainLine;
    private WishCard _wMain, _wAlt;

    /// <summary>공개 카드 한 장의 요소 — 그림(흰 테 실루엣 · 채색) · 빛무리 · 글 묶음(등급 · 이름 · ◆ · 속성 · 효과).</summary>
    private sealed class WishCard
    {
        public RectTransform Root, Text;
        public CanvasGroup   TextGroup;
        public Image  Glow, Rim, Icon, Rule;
        public TMP_Text Grade, Name, Attr, Effect;
        public readonly Image[] Gems = new Image[3];
        public Vector2 TextPos;
        public bool Twin;
        public int  Tier;
    }

    // ── 코드로 굽는 그림(한 번 · 정적 캐시) ──

    /// <summary>화면 가장자리 어둡힘 — 가운데는 비우고 바깥으로 짙어진다. 16:9로 구워 원으로 보인다.</summary>
    private static Sprite EdgeSprite => s_edge != null ? s_edge
        : (s_edge = UI_RuneSelectPopup.MakeProcSprite("Refinery_Edge", 160, 90, (u, v) =>
          {
              float x = (u * 2f - 1f) * (16f / 9f), y = v * 2f - 1f;
              float d = Mathf.InverseLerp(0.45f, 1.75f, Mathf.Sqrt(x * x + y * y));
              return d * d * (3f - 2f * d);
          }));

    /// <summary>하늘 위쪽 짙음 — 아래는 투명, 위로 갈수록 진하다(인디고 바탕 위에 검은 남색을 얹어 그라데이션).</summary>
    private static Sprite SkyRamp => s_skyRamp != null ? s_skyRamp
        : (s_skyRamp = UI_RuneSelectPopup.MakeProcSprite("Refinery_SkyRamp", 4, 128, (u, v) =>
          {
              float k = Mathf.InverseLerp(0.15f, 1f, v);
              return k * k * (3f - 2f * k);
          }));

    /// <summary>밝은 별 — 24텍셀 칸마다 절반쯤 별 하나(드물게 큰 별 + 옅은 빛무리). 화면에 늘이면 2~6px 점.</summary>
    private static Sprite Stars => s_stars != null ? s_stars
        : (s_stars = UI_RuneSelectPopup.MakeProcSprite("Refinery_Stars", 640, 360, (u, v) => StarField(u * 640f, v * 360f, 24f, 0.5f, 0.7f, 1.4f, 0.4f, 11)));

    /// <summary>옅은 잔별 — 9텍셀 칸에 듬성듬성, 작고 흐리다(밝은 별과 다른 박자로 반짝인다).</summary>
    private static Sprite StarsFaint => s_starsFaint != null ? s_starsFaint
        : (s_starsFaint = UI_RuneSelectPopup.MakeProcSprite("Refinery_StarsFaint", 640, 360, (u, v) => StarField(u * 640f, v * 360f, 9f, 0.22f, 0.45f, 0.35f, 0.15f, 29)));

    /// <summary>별빛 꼬리 — 아래 끝(머리 쪽)이 밝고 위로 사라진다. 가로는 가우스로 부드럽게.</summary>
    private static Sprite Tail => s_tail != null ? s_tail
        : (s_tail = UI_RuneSelectPopup.MakeProcSprite("Refinery_StarTail", 32, 128, (u, v) =>
          {
              float x = (u - 0.5f) / 0.22f;
              return Mathf.Exp(-x * x) * Mathf.Pow(1f - v, 1.6f);
          }));

    /// <summary>
    /// 부드러운 빛살 — 가운데 짙고 바깥으로 사라지는 후광(v4). 줄기 18개가 각도 가우스라 가장자리가 부드럽고,
    /// 줄기마다 굵기 · 길이가 조금씩 달라 「무늬」가 아니라 빛으로 읽힌다. 가운데엔 옅은 빛무리를 겹친다.
    /// </summary>
    private static Sprite SoftRays => s_softRays != null ? s_softRays
        : (s_softRays = UI_RuneSelectPopup.MakeProcSprite("Refinery_SoftRays", 384, 384, (u, v) =>
          {
              float dx = u - 0.5f, dy = v - 0.5f;
              float r  = Mathf.Sqrt(dx * dx + dy * dy) * 2f;   // 0 가운데 · 1 가장자리
              const float period = 2f * Mathf.PI / 18f;
              float ang = Mathf.Atan2(dy, dx) + Mathf.PI;
              int   idx = Mathf.FloorToInt(ang / period);
              float phi = Mathf.Repeat(ang, period) / period - 0.5f;   // -0.5 ~ 0.5
              float w   = 0.10f + 0.10f * Hash(idx, 0, 41);
              float len = 0.65f + 0.35f * Hash(idx, 1, 41);
              float stripe = Mathf.Exp(-(phi / w) * (phi / w));
              float fall   = Mathf.Pow(Mathf.Clamp01(1f - r / len), 1.6f);
              float core   = 0.45f * Mathf.Exp(-(r / 0.22f) * (r / 0.22f));
              return stripe * fall * Mathf.Lerp(0.6f, 1f, Hash(idx, 2, 41)) + core;
          }));

    /// <summary>
    /// 절차 별 점 한 픽셀 — 칸(cell)마다 해시로 별이 있을지 · 자리 · 크기 · 밝기를 정한다(자기 칸만 보므로 픽셀당 한 번).
    /// 별 중심은 칸 가운데 절반 안이라 빛무리가 칸 밖으로 잘리지 않는다.
    /// </summary>
    private static float StarField(float px, float py, float cell, float density, float radius, float radiusVar,
                                   float minBright, int seed)
    {
        int cx = Mathf.FloorToInt(px / cell), cy = Mathf.FloorToInt(py / cell);
        if (Hash(cx, cy, seed) > density) return 0f;
        float sx = (cx + 0.25f + 0.5f * Hash(cx, cy, seed + 1)) * cell;
        float sy = (cy + 0.25f + 0.5f * Hash(cx, cy, seed + 2)) * cell;
        float r  = radius + radiusVar * Mathf.Pow(Hash(cx, cy, seed + 3), 3f);
        float b  = Mathf.Lerp(minBright, 1f, Hash(cx, cy, seed + 4));
        float dx = px - sx, dy = py - sy;
        float d2 = (dx * dx + dy * dy) / (r * r);
        float a  = b * Mathf.Exp(-d2);
        // 큰 별만 옅은 빛무리 — 별 지름 2배 안에서 진다. 예전(3배 · 0.25)은 칸 경계에서 잘려 빛무리가 네모로 보였다(10-14 사진 E_1.40 —
        // UI는 선형 색공간에서 섞여 0.1 알파 가장자리도 또렷하다).
        if (r > radius + radiusVar * 0.5f) a += 0.12f * b * Mathf.Exp(-d2 / 4f);
        return a;
    }

    private static float Hash(int x, int y, int k)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + k * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    /// <summary>룬 그림의 흰 실루엣 — 실루엣 단계의 흰 테(뒤에 깔고 Outline으로 둘레만 보인다). 그림마다 한 번 만들어 캐시한다.</summary>
    private static Sprite WhiteSilhouette(Sprite src)
    {
        if (src == null) return null;
        if (s_silhouettes.TryGetValue(src, out var s) && s != null) return s;
        s = CopySprite(src, "Silhouette", c => new Color32(255, 255, 255, c.a));
        s_silhouettes[src] = s;
        return s;
    }

    // ── 짓기 ──

    /// <summary>
    /// 전체 화면 무대를 패널을 열 때 한 번 짓고 꺼 둔다(구운 프리팹에 없는 노드 · 코드 스프라이트는 프리팹에 저장되지 않는다).
    /// 별 그림 굽기(0.2메가픽셀 두 장)는 세션에 한 번 — 정적 캐시.
    /// 그리기 순서 = 형제 순서: 막 → 밤하늘(하늘 · 별) → 가장자리 → 제단 화면 → 빛살 → 원석 → 별빛 → 착지 고리 → 카드 → 빛무리 · 섬광 → 안내 · 버튼.
    /// (v4 — 하늘이 늘 켜져 있어 가장자리 어둡힘을 하늘 위로 올렸다. 예전 순서면 불투명한 하늘에 가려 보이지 않는다)
    /// </summary>
    private void EnsureWish()
    {
        if (_wish != null) return;

        var go = new GameObject("WishOverlay", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        _wish = (RectTransform)go.transform;
        _wish.SetParent(transform, false);
        ShopUIStyle.Stretch(_wish);
        // 패널 바로 위 정렬 — 덮어쓴 정렬이라 자기 레이캐스터가 패널(막 · 버튼)보다 먼저 클릭을 받는다.
        var cv = go.GetComponent<Canvas>();
        cv.overrideSorting = true;
        cv.sortingOrder    = (TryGetComponent<Canvas>(out var own) ? own.sortingOrder : UISortingOrder.PopupStack) + 1;
        cv.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal
                                     | AdditionalCanvasShaderChannels.Tangent;   // TMP SDF 글자가 쓰는 채널

        // 막 — 화면 전체 클릭을 받아 패널로 새지 않게. 끝 상태에선 빈 곳 누르기 = 공개 닫기(제단 화면에선 아무 일 없음).
        var back = WImg(_wish, "Backdrop", null);
        ShopUIStyle.Stretch(back.rectTransform);
        back.raycastTarget = true;
        AddClick(back.gameObject, OnWishBackdrop);

        var night = ShopUIStyle.MakeRect(_wish, "Night", typeof(CanvasGroup)).GetComponent<RectTransform>();
        ShopUIStyle.Stretch(night);
        _wNight = night.GetComponent<CanvasGroup>();
        _wNight.blocksRaycasts = false;
        var sky = WImg(night, "Sky", null);
        ShopUIStyle.Stretch(sky.rectTransform);
        sky.color = SkyLow;
        var top = WImg(night, "SkyTop", SkyRamp);
        ShopUIStyle.Stretch(top.rectTransform);
        top.color = SkyHigh;
        var horizon = WImg(night, "Horizon", UI_RuneSelectPopup.SoftDot);
        ShopUIStyle.Anchor(horizon.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Half, Vector2.zero, new Vector2(2600f, 760f));
        horizon.color = Horizon;
        _wStarsFaint = WImg(night, "StarsFaint", StarsFaint);
        ShopUIStyle.Stretch(_wStarsFaint.rectTransform);
        _wStars = WImg(night, "Stars", Stars);
        ShopUIStyle.Stretch(_wStars.rectTransform);

        _wEdge = WImg(_wish, "Edge", EdgeSprite);
        ShopUIStyle.Stretch(_wEdge.rectTransform);

        BuildIdleStage();

        _wRays     = WImgAt(_wish, "Rays", SoftRays, Land, RaysPx);
        _wRaysGold = WImgAt(_wish, "RaysGold", SoftRays, Land, GoldRaysPx);

        _wOreGlow = WImgAt(_wish, "OreGlow", UI_RuneSelectPopup.SoftDot, new Vector2(LandX, OreFromY), 220f);
        _wOre     = WImgAt(_wish, "Ore", OreIconSprite(), new Vector2(LandX, OreFromY), OreSize);
        _wOre.preserveAspect = true;
        _wBeam    = WImgAt(_wish, "Beam", Tail, new Vector2(LandX, OreFromY), BeamW);
        _wBeam.rectTransform.pivot = new Vector2(0.5f, 0f);   // 아래 끝(밝은 쪽)이 원석 자리 — 위로 뻗는다

        // 별빛 — 꼬리는 아래 끝(머리)을 기준점으로 지나온 쪽(오른쪽 위)으로 뻗는다
        var streak = ShopUIStyle.MakeRect(_wish, "Streak").GetComponent<RectTransform>();
        ShopUIStyle.Stretch(streak);
        Vector2 back2 = StarFrom - Land;
        var tailRot = Quaternion.Euler(0f, 0f, Mathf.Atan2(-back2.x, back2.y) * Mathf.Rad2Deg);
        _wTailGlow = WImgAt(streak, "TailGlow", Tail, StarFrom, StarGlowW);
        _wTail     = WImgAt(streak, "Tail", Tail, StarFrom, StarTailW);
        foreach (var tail in new[] { _wTailGlow, _wTail })
        {
            tail.rectTransform.pivot         = new Vector2(0.5f, 0f);
            tail.rectTransform.localRotation = tailRot;
        }
        _wHead = WImgAt(streak, "Head", UI_RuneSelectPopup.SoftDot, StarFrom, StarHeadPx);
        _wCore = WImgAt(_wHead.transform, "Core", UI_RuneSelectPopup.SoftDot, Vector2.zero, StarCorePx);
        _wPing = WImgAt(streak, "Ping", UIProceduralSprites.Ring(0.025f, 256), StarFrom, 60f);   // 가는 고리 — 0.08은 고비마다 두꺼운 원판으로 보였다(v5 사진 E_3.40)

        _wShock  = WImgAt(_wish, "Shock", UIProceduralSprites.Ring(0.008f, 256), Land, 200f);
        _wShock2 = WImgAt(_wish, "Shock2", UIProceduralSprites.Ring(0.010f, 256), Land, 140f);
        _wGoldRing = WImgAt(_wish, "GoldRing", UIProceduralSprites.Ring(0.008f, 256), Land, 320f);

        _wAlt  = BuildCard("CardAlt");
        _wMain = BuildCard("CardMain");

        _wBurst = WImgAt(_wish, "Burst", UI_RuneSelectPopup.SoftDot, Land, 500f);
        for (int i = 0; i < _wSparks.Length; i++)
            _wSparks[i] = WImgAt(_wish, $"Spark{i}", UI_RuneSelectPopup.SoftDot, Land, 16f);
        _wFlash = WImg(_wish, "Flash", null);
        ShopUIStyle.Stretch(_wFlash.rectTransform);

        _wSkip = WText(_wish, "SkipHint", UIWeight.Regular, 18f, UITheme.Thin.InkA(0.6f), TextAlignmentOptions.MidlineRight);
        ShopUIStyle.Anchor(_wSkip.rectTransform, Vector2.one, Vector2.one, Vector2.one, new Vector2(-48f, -36f), new Vector2(520f, 28f));
        _wCaption = WText(_wish, "TwinCaption", UIWeight.Regular, 22f, UITheme.Thin.InkA(0.8f), TextAlignmentOptions.Center);
        Put(_wCaption.rectTransform, 0f, BtnY, 900f, 32f);
        _wCaption.text = "두 장 중 하나를 고른다";

        BuildEndButtons();
        _wish.gameObject.SetActive(false);
    }

    private WishCard BuildCard(string name)
    {
        var c = new WishCard();
        c.Root = ShopUIStyle.MakeRect(_wish, name).GetComponent<RectTransform>();
        ShopUIStyle.Stretch(c.Root);
        c.Glow = WImg(c.Root, "Glow", UI_RuneSelectPopup.SoftDot);
        c.Rim  = WImg(c.Root, "Rim", null);
        c.Rim.preserveAspect = true;
        var rim = c.Rim.gameObject.AddComponent<Outline>();
        rim.effectColor     = Color.white;
        rim.effectDistance  = new Vector2(RimPx, -RimPx);
        rim.useGraphicAlpha = true;
        c.Icon = WImg(c.Root, "Icon", null);
        c.Icon.preserveAspect = true;

        c.Text = ShopUIStyle.MakeRect(c.Root, "Text", typeof(CanvasGroup)).GetComponent<RectTransform>();
        c.TextGroup = c.Text.GetComponent<CanvasGroup>();
        c.TextGroup.blocksRaycasts = false;
        c.TextGroup.interactable   = false;
        c.Grade  = WText(c.Text, "Grade",  UIWeight.Medium,  20f, UITheme.Thin.Ink, TextAlignmentOptions.MidlineLeft);
        c.Name   = WText(c.Text, "Name",   UIWeight.Regular, UITheme.Thin.H1, UITheme.Thin.Ink, TextAlignmentOptions.MidlineLeft);
        for (int i = 0; i < c.Gems.Length; i++)
        {
            c.Gems[i] = WImg(c.Text, $"Gem{i}", null);
            c.Gems[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        }
        c.Attr   = WText(c.Text, "Attr",   UIWeight.Regular, 20f, UITheme.Thin.InkA(0.72f), TextAlignmentOptions.MidlineLeft);
        c.Rule   = WImg(c.Text, "Rule", null);
        c.Rule.color = UITheme.Thin.LineGold;
        c.Effect = WText(c.Text, "Effect", UIWeight.Regular, 20f, UITheme.Thin.InkA(0.9f), TextAlignmentOptions.TopLeft);
        c.Effect.textWrappingMode = TextWrappingModes.Normal;
        c.Root.gameObject.SetActive(false);
        return c;
    }

    /// <summary>끝 버튼 — [다시 정제 · 원석 N](잉크) · [룬판에 놓기](금 — 지금 누를 것). 평판 + 1px 선(창 버튼과 같은 모양).</summary>
    private void BuildEndButtons()
    {
        _wButtons = ShopUIStyle.MakeRect(_wish, "EndButtons", typeof(CanvasGroup)).GetComponent<RectTransform>();
        ShopUIStyle.Stretch(_wButtons);
        _wButtonsGroup = _wButtons.GetComponent<CanvasGroup>();

        _wAgain = ShopUIStyle.MakeImage(_wButtons, "Again", ButtonFill, raycast: true);
        Put(_wAgain.rectTransform, -BtnDx, BtnY, BtnW, BtnH);
        _wAgainLine  = StyleFlatButton(_wAgain);
        _wAgainLabel = WText(_wAgain.transform, "Label", UIWeight.Medium, BtnFont, UITheme.Thin.Ink, TextAlignmentOptions.Center);
        ShopUIStyle.Stretch(_wAgainLabel.rectTransform);
        _wAgainLabel.richText = true;
        AddClick(_wAgain.gameObject, OnSpinClicked);

        _wPlace = ShopUIStyle.MakeImage(_wButtons, "Place", ButtonFill, raycast: true);
        Put(_wPlace.rectTransform, BtnDx, BtnY, BtnW, BtnH);
        SetLine(StyleFlatButton(_wPlace), UITheme.Thin.LineReady);
        var place = WText(_wPlace.transform, "Label", UIWeight.Medium, BtnFont, UITheme.Thin.Gold, TextAlignmentOptions.Center);
        ShopUIStyle.Stretch(place.rectTransform);
        place.text = "룬판에 놓기";
        AddClick(_wPlace.gameObject, OnPlaceClicked);

        _wHint = WText(_wish, "Hint", UIWeight.Regular, 18f, UITheme.Thin.InkA(0.7f), TextAlignmentOptions.Center);
        Put(_wHint.rectTransform, 0f, BtnY - BtnH * 0.5f - 30f, 900f, 28f);

        _wButtons.gameObject.SetActive(false);
    }

    // ── 한 번 돌리기 준비 ──

    /// <summary>돌리기 전에 — 무대를 켜고 맨 위로, 카드 배치(한 장 / 두 장) · 내용, 끝 버튼 · 안내 걷기.
    /// 제단 화면의 원석은 연출 원석(<c>_wOre</c>)이 같은 자리에서 이어받는다 — 제단 쪽은 끈다.</summary>
    private void PrepareWish()
    {
        _wish.gameObject.SetActive(true);
        _wish.SetAsLastSibling();
        _wIdle.blocksRaycasts = false;
        _iOre.enabled     = false;
        _iOreGlow.enabled = false;
        ShowEndButtons(false);
        _wHint.text = "";
        _wSkip.text = "건너뛰기 · 클릭 · Space";
        _wCaption.gameObject.SetActive(_plan.Twin);
        _wCaption.alpha = 0f;

        LayoutCards(_plan.Twin);
        FillCard(_wMain, _plan.Rune, _plan.MainTier);
        if (_plan.Twin) FillCard(_wAlt, _plan.Alt, _plan.AltTier);
        _wMain.Root.gameObject.SetActive(false);
        _wAlt.Root.gameObject.SetActive(false);

        _wRaysGold.rectTransform.localRotation = Quaternion.identity;
        _wRays.rectTransform.localRotation     = Quaternion.identity;
        if (_wOre.sprite == null) _wOre.sprite = OreIconSprite();   // 스킨이 늦게 올라왔을 때
    }

    /// <summary>한 장이면 그림을 가운데(착지 자리) · 글을 그 아래 가운데에, 두 장이면 카드 둘을 ±260에(높은 쪽 = Main이 오른쪽).</summary>
    private void LayoutCards(bool twin)
    {
        if (!twin)
        {
            LayoutCard(_wMain, LandX, LandY, IconSize, false, InfoTextX, InfoTextY);
            return;
        }
        LayoutCard(_wMain,  TwinCx, TwinIconY, TwinIcon, true,  TwinCx, TwinTextY);
        LayoutCard(_wAlt,  -TwinCx, TwinIconY, TwinIcon, true, -TwinCx, TwinTextY);
    }

    private static void LayoutCard(WishCard c, float ix, float iy, float size, bool twin, float tx, float ty)
    {
        c.Twin = twin;
        Put(c.Glow.rectTransform, ix, iy, size * 1.8f, size * 1.8f);
        Put(c.Rim.rectTransform,  ix, iy, size, size);
        Put(c.Icon.rectTransform, ix, iy, size, size);
        c.Rim.rectTransform.localScale  = Vector3.one;
        c.Icon.rectTransform.localScale = Vector3.one;
        c.TextPos = new Vector2(tx, ty);

        if (!twin)
        {
            // 한 장 — 그림 아래 가운데 글 묶음(v4 — 룬 그림은 캐릭터 그림처럼 크지 않아 왼쪽 글이면 오른쪽이 비었다). 묶음 중심 기준 세로 자리:
            // 등급 · ◆ · 이름(48) · 속성 · 금선 · 효과(위 정렬, 넘치면 16까지 줄임).
            Put(c.Text, tx, ty, InfoWide, 370f);
            var center = TextAlignmentOptions.Center;
            PutText(c.Grade,  0f,  155f, InfoWide,  30f, 20f, center);
            PutText(c.Name,   0f,   69f, InfoWide,  64f, UITheme.Thin.H1, center);
            PutText(c.Attr,   0f,   17f, InfoWide,  28f, 20f, center);
            Put(c.Rule.rectTransform, 0f, -9f, 520f, 1f);
            PutText(c.Effect, 0f, -100f, InfoWide, 150f, 20f, TextAlignmentOptions.Top);
            return;
        }

        // 두 장 — 그림 아래 가운데 정렬(원신 10연 요약처럼 카드마다 이름 · ◆).
        Put(c.Text, tx, ty, TwinWide, 300f);
        var mid = TextAlignmentOptions.Center;
        PutText(c.Grade,  0f,  130f, TwinWide,  26f, 18f, mid);
        PutText(c.Name,   0f,   92f, TwinWide,  46f, 36f, mid);
        PutText(c.Attr,   0f,   20f, TwinWide,  26f, 18f, mid);
        Put(c.Rule.rectTransform, 0f, 0f, 360f, 1f);
        PutText(c.Effect, 0f,  -72f, TwinWide, 120f, 18f, TextAlignmentOptions.Top);
    }

    /// <summary>카드 내용 — 그림 · 흰 실루엣 · 등급 · 이름 · 속성(칸 수 · 존) · 효과 · ◆ 자리(등급 수만큼).</summary>
    private static void FillCard(WishCard c, RuntimeItemData rune, int tier)
    {
        c.Tier = Mathf.Clamp(tier, 1, 3);
        var icon = RuneArt.ResolveRuneIcon(rune);
        if (icon == null && rune != null) icon = RuneArt.GetArt(rune.rarity);
        c.Icon.sprite  = icon;
        c.Icon.enabled = icon != null;
        c.Rim.sprite   = WhiteSilhouette(icon);
        c.Rim.enabled  = c.Rim.sprite != null;

        c.Grade.text  = GradeName(c.Tier);
        c.Grade.color = GradeColor(c.Tier);
        c.Name.text   = rune != null ? rune.displayName : "";
        c.Name.color  = UITheme.Thin.Ink;
        c.Attr.text   = RuneFootText(rune);
        c.Effect.text = RuneEffectText(rune);
        c.TextGroup.alpha = 0f;

        float y = c.Twin ? 52f : 123f;   // 한 장 — 등급 글과 이름 사이 가운데 줄
        for (int i = 0; i < c.Gems.Length; i++)
        {
            var gem = c.Gems[i];
            bool on = i < c.Tier;
            if (gem.gameObject.activeSelf != on) gem.gameObject.SetActive(on);
            if (!on) continue;
            float x = (i - (c.Tier - 1) * 0.5f) * GemPitch;
            Put(gem.rectTransform, x, y, GemSide, GemSide);
            gem.color = UITheme.Thin.InkA(0.16f);
            gem.rectTransform.localScale = Vector3.one;
        }
    }

    // ── 끝 상태 ──

    /// <summary>끝 버튼을 켜고 끈다 — 켤 때 0.18초에 스민다(순간 등장 금지). [룬판에 놓기]는 놓을 룬이 있을 때만.</summary>
    private void ShowEndButtons(bool on)
    {
        if (_wButtons == null) return;
        _wButtons.gameObject.SetActive(on);
        if (!on) return;
        _wPlace.gameObject.SetActive(_pendingRune != null);
        _wSkip.text  = "빈 곳을 누르면 제단으로";
        _wSkip.alpha = 1f;
        _wButtonsGroup.alpha = 0f;
        FadeInAsync(_wButtonsGroup, destroyCancellationToken).Forget();
    }

    /// <summary>[다시 정제 · 원석 N] — 창의 [정제하기]와 같은 값(모자라면 비용만 빨강, 잠금 중엔 흐림).</summary>
    private void RefreshAgainButton(bool afford, bool ready)
    {
        if (_wAgainLabel == null || _svc == null) return;
        int cost = _svc.CurrentCost;
        _wAgainLabel.text = afford
            ? $"다시 정제 · 원석 {cost}"
            : $"다시 정제 · <color=#{ColorUtility.ToHtmlStringRGB(UITheme.Thin.Danger)}>원석 {cost}</color>";
        _wAgainLabel.color = ready ? UITheme.Thin.Ink : UITheme.Thin.InkA(0.45f);
        SetLine(_wAgainLine, ready ? UITheme.Thin.InkA(0.35f) : UITheme.Thin.LineIdle);
    }

    /// <summary>끝 상태에서 빈 곳 — 공개를 걷고 제단 화면으로(원신 「누르면 닫힘」). 입력이 막 열린 직후 0.4초의 클릭은 건너뛰기 연타로 보고 거른다.
    /// 제단 화면에선(입력 열림 기록 없음) 아무 일도 하지 않는다 — 닫기는 [닫기 · ESC].</summary>
    private void OnWishBackdrop()
    {
        EventSystem.current?.SetSelectedGameObject(null);   // 막 버튼이 선택된 채 남아 Submit 키를 받지 않게
        if (_busy || _wish == null || !_wish.gameObject.activeSelf || _inputOpenAt < 0f) return;
        if (Time.unscaledTime - _inputOpenAt < DismissGrace) return;
        ApplyIdleLook();
    }

    // ── 작은 도우미 ──

    private static Image WImg(Transform parent, string name, Sprite sprite)
    {
        var img = ShopUIStyle.MakeImage(parent, name, Color.clear);
        img.sprite        = sprite;
        img.raycastTarget = false;
        return img;
    }

    private static Image WImgAt(Transform parent, string name, Sprite sprite, Vector2 pos, float size)
    {
        var img = WImg(parent, name, sprite);
        Put(img.rectTransform, pos.x, pos.y, size, size);
        return img;
    }

    /// <summary>전체 화면 글 — 톤 v2(Noto · 가짜 굵게 없음 · 부드러운 그림자). 자동 크기 하한은 16(설계 v3 3장).</summary>
    private static TMP_Text WText(Transform parent, string name, UIWeight weight, float size, Color color, TextAlignmentOptions align)
    {
        var t = ShopUIStyle.MakeText(parent, name, size, FontStyles.Normal, align, color);
        UITheme.StyleThinText(t, weight, size, color, shadow: true);
        t.alignment        = align;
        t.fontSizeMin      = Mathf.Min(size, MinFont);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget    = false;
        return t;
    }

    private static void PutText(TMP_Text t, float x, float y, float w, float h, float size, TextAlignmentOptions align)
    {
        Put(t.rectTransform, x, y, w, h);
        t.fontSize    = size;
        t.fontSizeMax = size;
        t.fontSizeMin = Mathf.Min(size, MinFont);
        t.alignment   = align;
    }

    /// <summary>화면(또는 묶음) 가운데 기준 자리 · 크기.</summary>
    private static void Put(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = Half;
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta        = new Vector2(w, h);
    }
}
