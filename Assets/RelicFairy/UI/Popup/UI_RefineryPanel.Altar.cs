using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 정제소 v4 제단 화면(설계 정제소_v4_제단화면·연출, 10-14) — 정제소를 열면 처음부터 「아발론의 밤」 전체 화면이다.
/// 원신 기원 화면처럼 비는 <b>장소</b> 하나: 가운데 응축 제단(빛살 · 이중 금 고리 · 속성 보석 여섯이 천천히 돔 · 원석이 떠 숨 쉼),
/// 왼쪽 「나올 수 있는 등급」 세 줄, 오른쪽 「이번 정제」 비용, 아래 [정제하기]. 누르면 같은 하늘에서 연출이 이어진다(<c>UI_RefineryPanel.Gacha.cs</c>).
///
/// <para>무대 캔버스(<c>UI_RefineryPanel.Wish.cs</c>)의 한 묶음(<see cref="_wIdle"/>)이다 — 예전 1170×828 창(동굴 그림 · 보석 링 · 확률판)은
/// 숨기고(<see cref="HideBakedWindow"/>) 값 계산 · 버튼 처리는 그대로 쓴다. 제단 화면에서 빈 곳 누르기는 닫지 않는다(실수 닫힘) — [닫기 · ESC].</para>
/// </summary>
public sealed partial class UI_RefineryPanel
{
    // ── 배치(1080 화면 px · 화면 가운데 기준) ──
    private const float AltarY       =  150f;   // 제단 = 착지 = 공개 룬 자리(LandY)
    private const float HaloPx       =  640f;
    private const float AltarRaysPx  = 1000f;
    private const float RingOuterPx  =  500f;
    private const float RingInnerPx  =  340f;
    private const float OrbitR       =  250f;   // 보석이 바깥 고리 위를 돈다
    private const float GlyphW       =   64f;   // 육각 각인 비율 0.835
    private const float GlyphH       =   76f;
    private const float IdleOrePx    =  120f;
    private const float ColW         =  400f;
    private const float ColLeftX     = -820f;   // 왼쪽 열 왼끝(글은 모두 왼쪽 정렬)
    private const float ColRightX    =  420f;
    private const float ColTopY      =  300f;   // 열 머리 줄
    private const float RowPitch     =   64f;
    private const float MeltY        =  -36f;   // 「재료 녹이기」 버튼(이 방 이벤트 줄 아래)
    private const float MeltH        =   52f;
    private const float SpinBigW     =  520f;
    private const float SpinBigH     =   88f;
    private const float SpinBigY     = -360f;
    private const float EdgeIdleA    =  0.55f;  // 제단 화면의 가장자리 어둡힘(누르면 1까지)
    private const float EdgeIdleS    =  1.15f;
    private const float AltarSpin    =    5f;   // 초당 도 — 보석 궤도
    private const float DashSpin     =    2f;
    private const float AltarRaysSpin =   3f;

    private static readonly Color AltarHaloC = new(0.62f, 0.52f, 1f, 1f);

    // ── Static ──
    private static Sprite s_dashRing;

    // ── Private ──
    private CanvasGroup _wIdle, _wAltar;   // 글 · 열 · 버튼 / 가운데 제단(연출 동안에도 남아 충전 · 룰렛 · 착지 표적이 된다)
    private Image _iHalo, _iRays, _iDash, _iRingIn, _iOre, _iOreGlow, _iSpin, _iSpinIcon, _iCostIcon, _iGemGlow, _iMelt;
    private Image[] _iMeltLine;
    private Image[] _iGemImgs;
    private Color[] _iGemBase;
    private Image[] _iSpinLine;
    private RectTransform[] _iGems;
    private TMP_Text _iSub, _iOwn, _iSpinLabel, _iSpinCost, _iCost, _iNext, _iMode, _iBoost, _iHint, _iEvent, _iPity, _iMeltLabel, _iMeltNote;
    private readonly OddsRow[] _iOdds = new OddsRow[3];

    /// <summary>「나올 수 있는 등급」 한 줄 — ◆ · 등급 이름 · 큰 % · 막대(바탕 선 + 채움).</summary>
    private struct OddsRow
    {
        public Image    Gem, Track, Fill;
        public TMP_Text Name, Pct;
    }

    /// <summary>점선 고리 — 금 점선이 천천히 돌아 「제단이 살아 있다」가 보인다(민무늬 고리는 돌아도 안 보인다). 지름 512 · 선 3텍셀 · 점 60개.</summary>
    private static Sprite DashRing => s_dashRing != null ? s_dashRing
        : (s_dashRing = UI_RuneSelectPopup.MakeProcSprite("Refinery_DashRing", 512, 512, (u, v) =>
          {
              float dx = (u - 0.5f) * 512f, dy = (v - 0.5f) * 512f;
              float r = Mathf.Sqrt(dx * dx + dy * dy);
              float band = Mathf.Clamp01(1.5f - Mathf.Abs(r - 250f));
              const float period = 2f * Mathf.PI * 250f / 60f;   // 점 하나 + 틈 = 약 26텍셀
              float f = Mathf.Repeat(Mathf.Atan2(dy, dx) / (2f * Mathf.PI) * 60f, 1f);
              float dash = Mathf.Clamp01(period * 0.3f - Mathf.Abs(f - 0.3f) * period + 0.5f);
              return band * dash;
          }));

    // ── 짓기 ──

    /// <summary>제단 화면 묶음 — 무대를 지을 때(<see cref="EnsureWish"/>) 밤하늘 · 가장자리 바로 위에 한 번 짓는다.</summary>
    private void BuildIdleStage()
    {
        // 가운데 제단은 따로 한 묶음 — 누르면 글 · 열 · 버튼은 스미고 제단은 남아 충전 · 보석 룰렛을 보여 준다(v5)
        var altar = ShopUIStyle.MakeRect(_wish, "Altar", typeof(CanvasGroup)).GetComponent<RectTransform>();
        ShopUIStyle.Stretch(altar);
        _wAltar = altar.GetComponent<CanvasGroup>();
        _wAltar.blocksRaycasts = false;
        BuildAltarCore(altar);

        var idle = ShopUIStyle.MakeRect(_wish, "Idle", typeof(CanvasGroup)).GetComponent<RectTransform>();
        ShopUIStyle.Stretch(idle);
        _wIdle = idle.GetComponent<CanvasGroup>();

        // 왼쪽 위 — 이름 · 한 줄 설명 · 금선
        var title = WText(idle, "Title", UIWeight.Light, UITheme.Thin.H1, UITheme.Thin.Ink, TextAlignmentOptions.MidlineLeft);
        Corner(title.rectTransform, false, 72f, -44f, 600f, 64f);
        title.text = "정제소";
        _iSub = WText(idle, "Sub", UIWeight.Regular, UITheme.Thin.Body, UITheme.Thin.InkA(0.62f), TextAlignmentOptions.MidlineLeft);
        Corner(_iSub.rectTransform, false, 74f, -112f, 900f, 28f);
        var rule = WImg(idle, "TitleRule", null);
        rule.color = UITheme.Thin.LineGold;
        Corner(rule.rectTransform, false, 74f, -152f, 360f, 1f);

        // 오른쪽 위 — 보유 원석(아이콘은 글 오른쪽) · [닫기 · ESC]
        var ownIcon = WImg(idle, "OwnIcon", OreIconSprite());
        ownIcon.preserveAspect = true;
        ownIcon.color   = Color.white;
        ownIcon.enabled = ownIcon.sprite != null;
        Corner(ownIcon.rectTransform, true, -72f, -50f, 32f, 32f);
        _iOwn = WText(idle, "Own", UIWeight.Medium, UITheme.Thin.Title, UITheme.Thin.Ink, TextAlignmentOptions.MidlineRight);
        Corner(_iOwn.rectTransform, true, ownIcon.enabled ? -114f : -72f, -46f, 320f, 40f);

        var close = ShopUIStyle.MakeImage(idle, "Close", ButtonFill, raycast: true);
        Corner(close.rectTransform, true, -72f, -104f, 168f, 44f);
        SetLine(StyleFlatButton(close), UITheme.Thin.InkA(0.3f));
        var closeLabel = WText(close.transform, "Label", UIWeight.Regular, UITheme.Thin.Body, UITheme.Thin.InkA(0.85f), TextAlignmentOptions.Center);
        ShopUIStyle.Stretch(closeLabel.rectTransform);
        closeLabel.text = "닫기 · ESC";
        AddClick(close.gameObject, () => { if (!_busy) ClosePopupUI(); });

        BuildOddsColumn(idle);
        BuildCostColumn(idle);
        BuildSpinBig(idle);
    }

    /// <summary>가운데 제단 — 빛무리 · 빛살 · 점선 바깥 고리 · 안 고리 · 속성 보석 여섯 · 떠 있는 원석.</summary>
    private void BuildAltarCore(RectTransform idle)
    {
        var at = new Vector2(0f, AltarY);
        _iHalo   = WImgAt(idle, "AltarHalo", UI_RuneSelectPopup.SoftDot, at, HaloPx);
        _iHalo.color = UITheme.Thin.WithA(AltarHaloC, 0.20f);
        _iRays   = WImgAt(idle, "AltarRays", SoftRays, at, AltarRaysPx);
        _iRays.color = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.10f);
        _iDash   = WImgAt(idle, "RingOuter", DashRing, at, RingOuterPx);
        _iDash.color = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.55f);
        _iRingIn = WImgAt(idle, "RingInner", UIProceduralSprites.Ring(0.005f, 512), at, RingInnerPx);
        _iRingIn.color = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.30f);

        // 룰렛이 멎은 보석(맨 위) 뒤 빛 — 연출에서만 켠다
        _iGemGlow = WImgAt(idle, "GemGlow", UI_RuneSelectPopup.SoftDot, new Vector2(0f, AltarY + OrbitR), 170f);
        _iGemGlow.enabled = false;

        var order = ElementDef.Order;
        _iGems    = new RectTransform[order.Count];
        _iGemImgs = new Image[order.Count];
        _iGemBase = new Color[order.Count];
        for (int i = 0; i < order.Count; i++)
        {
            var glyph = UISkin.Refinery != null ? UISkin.Refinery.Glyph(i) : null;
            var g = WImg(idle, $"Gem_{order[i]}", glyph);
            g.preserveAspect = true;
            // 각인 그림이 없으면(스킨 미로드) 속성 색 점으로 — 흰 네모를 그리지 않는다
            if (glyph == null) g.sprite = UI_RuneSelectPopup.SoftDot;
            g.color = glyph != null ? UITheme.Thin.WithA(Color.white, 0.92f) : ElementDef.IdColor(order[i], UITheme.Thin.Ink);
            _iGems[i]    = g.rectTransform;
            _iGemImgs[i] = g;
            _iGemBase[i] = g.color;
            Put(_iGems[i], 0f, AltarY, GlyphW, GlyphH);
        }

        _iOreGlow = WImgAt(idle, "OreGlow", UI_RuneSelectPopup.SoftDot, at, 260f);
        _iOreGlow.color = UITheme.Thin.WithA(Silver, 0.32f);
        _iOre = WImgAt(idle, "Ore", OreIconSprite(), at, IdleOrePx);
        _iOre.preserveAspect = true;
        _iOre.color   = Color.white;
        _iOre.enabled = _iOre.sprite != null;
    }

    /// <summary>왼쪽 열 「나올 수 있는 등급」 — Rare · Epic · Legend 세 줄(◆ · 이름 · 큰 % · 막대) + 해금 몫 한 줄.</summary>
    private void BuildOddsColumn(RectTransform idle)
    {
        var cap = WText(idle, "OddsCaption", UIWeight.Medium, UITheme.Thin.Lead, UITheme.Thin.InkA(0.6f), TextAlignmentOptions.MidlineLeft);
        PutLeft(cap.rectTransform, ColLeftX, ColTopY, ColW, 30f);
        cap.text = "나올 수 있는 등급";

        for (int i = 0; i < _iOdds.Length; i++)
        {
            int tier = i + 1;
            float y = ColTopY - 62f - i * RowPitch;
            var row = new OddsRow();
            row.Gem = WImg(idle, $"OddsGem{tier}", null);
            row.Gem.color = GradeColor(tier);
            Put(row.Gem.rectTransform, ColLeftX + 9f, y, 13f, 13f);
            row.Gem.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            row.Name = WText(idle, $"OddsName{tier}", UIWeight.Medium, UITheme.Thin.Lead, GradeColor(tier), TextAlignmentOptions.MidlineLeft);
            PutLeft(row.Name.rectTransform, ColLeftX + 32f, y, 200f, 30f);
            row.Name.text = GradeName(tier);
            row.Pct = WText(idle, $"OddsPct{tier}", UIWeight.Light, UITheme.Thin.H2, UITheme.Thin.Ink, TextAlignmentOptions.MidlineRight);
            PutLeft(row.Pct.rectTransform, ColLeftX + ColW - 140f, y + 2f, 140f, 44f);
            row.Track = WImg(idle, $"OddsTrack{tier}", null);
            row.Track.color = UITheme.Thin.InkA(0.12f);
            PutLeft(row.Track.rectTransform, ColLeftX, y - 26f, ColW, 2f);
            row.Fill = WImg(idle, $"OddsFill{tier}", null);
            row.Fill.color = GradeColor(tier);
            PutLeft(row.Fill.rectTransform, ColLeftX, y - 26f, 0f, 2f);
            _iOdds[i] = row;
        }

        _iBoost = WText(idle, "OddsBoost", UIWeight.Regular, UITheme.Thin.Body, UITheme.Thin.Gold, TextAlignmentOptions.MidlineLeft);
        PutLeft(_iBoost.rectTransform, ColLeftX, ColTopY - 62f - 3f * RowPitch, ColW, 28f);
        // 천장(10-15) — Legend 없이 10번이면 다음은 Legend. 몇 번 남았는지 · 확정이면 금빛
        _iPity = WText(idle, "OddsPity", UIWeight.Regular, UITheme.Thin.Body, UITheme.Thin.InkA(0.6f), TextAlignmentOptions.MidlineLeft);
        PutLeft(_iPity.rectTransform, ColLeftX, ColTopY - 62f - 3f * RowPitch - 34f, ColW, 28f);
    }

    /// <summary>오른쪽 열 「이번 정제」 — 원석 아이콘 + 비용(큰 숫자) · 다음 비용 · 정제 방식(한 장 / 두 장 중 하나).</summary>
    private void BuildCostColumn(RectTransform idle)
    {
        var cap = WText(idle, "CostCaption", UIWeight.Medium, UITheme.Thin.Lead, UITheme.Thin.InkA(0.6f), TextAlignmentOptions.MidlineLeft);
        PutLeft(cap.rectTransform, ColRightX, ColTopY, ColW, 30f);
        cap.text = "이번 정제";

        _iCostIcon = WImg(idle, "CostIcon", OreIconSprite());
        _iCostIcon.preserveAspect = true;
        _iCostIcon.color   = Color.white;
        _iCostIcon.enabled = _iCostIcon.sprite != null;
        Put(_iCostIcon.rectTransform, ColRightX + 22f, ColTopY - 66f, 44f, 44f);
        _iCost = WText(idle, "Cost", UIWeight.Light, UITheme.Thin.H1, UITheme.Thin.Ink, TextAlignmentOptions.MidlineLeft);
        PutLeft(_iCost.rectTransform, ColRightX + (_iCostIcon.enabled ? 58f : 0f), ColTopY - 64f, 300f, 64f);

        _iNext = WText(idle, "NextCost", UIWeight.Regular, UITheme.Thin.Body, UITheme.Thin.InkA(0.6f), TextAlignmentOptions.MidlineLeft);
        PutLeft(_iNext.rectTransform, ColRightX, ColTopY - 126f, ColW, 28f);
        _iMode = WText(idle, "Mode", UIWeight.Regular, UITheme.Thin.Body, UITheme.Thin.InkA(0.85f), TextAlignmentOptions.MidlineLeft);
        PutLeft(_iMode.rectTransform, ColRightX, ColTopY - 62f - 2f * RowPitch, ColW, 28f);
        // 이 방 돌발 이벤트(10-15) — 「◆ 반값 정제 — 원석 절반 · 이 방에서만」(금 = 지금 걸려 있다)
        _iEvent = WText(idle, "RoomEvent", UIWeight.Medium, UITheme.Thin.Body, UITheme.Thin.Gold, TextAlignmentOptions.MidlineLeft);
        PutLeft(_iEvent.rectTransform, ColRightX, ColTopY - 62f - 3f * RowPitch, ColW, 28f);

        // 재료 녹이기(10-17) — 원석이 모자라고 강화재료로 채울 수 있을 때만 보인다. 누르면 이번 정제에 모자란 만큼만 녹인다.
        // 기본은 누를 때만(재련소가 다음에 올 때를 위해 아껴 둘 수 있게) — 그래서 금 테 · 불 없이 차분한 선.
        _iMelt = ShopUIStyle.MakeImage(idle, "Melt", ButtonFill, raycast: true);
        PutLeft(_iMelt.rectTransform, ColRightX, MeltY, ColW, MeltH);
        _iMeltLine = StyleFlatButton(_iMelt);
        SetLine(_iMeltLine, UITheme.Thin.InkA(0.35f));
        _iMeltLabel = WText(_iMelt.transform, "Label", UIWeight.Regular, UITheme.Thin.Body, UITheme.Thin.Ink, TextAlignmentOptions.Center);
        ShopUIStyle.Stretch(_iMeltLabel.rectTransform);
        AddClick(_iMelt.gameObject, OnMeltClicked);
        _iMeltNote = WText(idle, "MeltNote", UIWeight.Regular, UITheme.Thin.Small, UITheme.Thin.InkA(0.55f), TextAlignmentOptions.MidlineLeft);
        PutLeft(_iMeltNote.rectTransform, ColRightX, MeltY - MeltH * 0.5f - 22f, ColW, 26f);
    }

    /// <summary>아래 가운데 큰 [정제하기 ◆ N] — 평판 + 1px 금(창 버튼과 같은 모양) · 아래 안내 줄.</summary>
    private void BuildSpinBig(RectTransform idle)
    {
        _iSpin = ShopUIStyle.MakeImage(idle, "SpinBig", ButtonFill, raycast: true);
        Put(_iSpin.rectTransform, 0f, SpinBigY, SpinBigW, SpinBigH);
        _iSpinLine = StyleFlatButton(_iSpin);
        _iSpinLabel = WText(_iSpin.transform, "Label", UIWeight.Medium, 30f, UITheme.Thin.Gold, TextAlignmentOptions.Center);
        Put(_iSpinLabel.rectTransform, -60f, 0f, 300f, SpinBigH);
        _iSpinLabel.text = "정제하기";
        _iSpinIcon = WImg(_iSpin.transform, "CostIcon", OreIconSprite());
        _iSpinIcon.preserveAspect = true;
        _iSpinIcon.enabled = _iSpinIcon.sprite != null;
        Put(_iSpinIcon.rectTransform, 140f, 0f, 30f, 30f);
        _iSpinCost = WText(_iSpin.transform, "CostNum", UIWeight.Regular, 28f, UITheme.Thin.Ink, TextAlignmentOptions.MidlineLeft);
        PutLeft(_iSpinCost.rectTransform, _iSpinIcon.enabled ? 164f : 120f, 0f, 96f, SpinBigH);
        AddClick(_iSpin.gameObject, OnSpinClicked);

        _iHint = WText(idle, "Hint", UIWeight.Regular, UITheme.Thin.Body, UITheme.Thin.InkA(0.62f), TextAlignmentOptions.Center);
        Put(_iHint.rectTransform, 0f, SpinBigY - SpinBigH * 0.5f - 30f, 900f, 28f);
    }

    // ── 상태 ──

    /// <summary>예전 창(동굴 그림 · 보석 링 · 확률판 · 작은 버튼)을 숨긴다 — 값과 버튼 처리는 그대로 쓰고 보이는 것은 제단 화면이 맡는다.</summary>
    private void HideBakedWindow()
    {
        var frame = _root != null ? _root.parent : null;
        if (frame != null && frame != transform) frame.gameObject.SetActive(false);
    }

    /// <summary>
    /// 제단 화면 — 밤하늘은 늘 켜져 있고(연출은 같은 하늘이 짙어지는 것), 공개 카드 · 별빛 · 섬광 · 끝 버튼을 걷고 제단 묶음을 켠다.
    /// 열 때 · 끝 상태에서 빈 곳을 누를 때(<see cref="ApplyIdleLook"/>).
    /// </summary>
    private void ShowIdleStage()
    {
        EnsureWish();
        _wish.gameObject.SetActive(true);
        _wish.SetAsLastSibling();
        _inputOpenAt = -1f;
        ShowEndButtons(false);
        _wMain.Root.gameObject.SetActive(false);
        _wAlt.Root.gameObject.SetActive(false);
        _wCaption.alpha = 0f;
        _wSkip.alpha    = 0f;
        _wHint.text     = "";
        SetStarVisible(false);
        foreach (var img in new[] { _wRays, _wRaysGold, _wOre, _wOreGlow, _wBeam, _wShock, _wShock2, _wGoldRing, _wBurst, _wFlash, _iGemGlow })
            SetA(img, Color.white, 0f);
        foreach (var s in _wSparks) SetA(s, Color.white, 0f);

        _wNight.alpha = 1f;
        SetA(_wEdge, Color.black, EdgeIdleA);
        _wEdge.rectTransform.localScale = Vector3.one * EdgeIdleS;
        _wIdle.alpha          = 1f;
        _wIdle.blocksRaycasts = true;
        _iOre.enabled     = _iOre.sprite != null;
        _iOreGlow.enabled = true;
        _wAltar.alpha     = 1f;
        for (int i = 0; i < _iGems.Length; i++)
        {
            _iGems[i].localScale = Vector3.one;
            _iGemImgs[i].color   = _iGemBase[i];
        }
        _iHalo.color = UITheme.Thin.WithA(AltarHaloC, 0.20f);
        _iRays.color = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.10f);
        _iSpin.transform.localScale = Vector3.one;
    }

    /// <summary>제단 화면 값 — 등급 확률 세 줄 · 해금 몫 · 보유 원석 · 비용 · 다음 비용 · 정제 방식 · 안내(<see cref="Refresh"/>가 부른다).</summary>
    private void RefreshIdleStage(float rare, float epic, float leg)
    {
        if (_wIdle == null || _svc == null) return;
        float[] odds = { rare, epic, leg };
        for (int i = 0; i < _iOdds.Length; i++)
        {
            _iOdds[i].Pct.text = Pct(odds[i]);
            _iOdds[i].Fill.rectTransform.sizeDelta = new Vector2(ColW * Mathf.Clamp01(odds[i]), 2f);
        }
        _iBoost.text = RefineryService.QualityUnlockEpic > 0f
            ? $"정제 등급 상승 +{RefineryService.QualityUnlockEpic * 100f:F0}%p" : string.Empty;

        CurrencyCounter.Apply(_iOwn, _svc.OreOwned, "원석 ");
        _iCost.text  = _iCostIcon.enabled ? _svc.CurrentCost.ToString() : $"원석 {_svc.CurrentCost}";
        _iCost.color = _svc.CanAfford ? UITheme.Thin.Ink : UITheme.Thin.Danger;
        _iNext.text  = _svc.NextCost > _svc.CurrentCost ? $"다음 정제부터 원석 {_svc.NextCost}" : $"다음 정제도 원석 {_svc.NextCost}";
        bool pick    = _svc.TwoCards;
        _iMode.text  = pick ? "룬 두 장 중 하나를 고른다" : "룬 하나가 나온다";
        _iMode.color = pick ? UITheme.Thin.Gold : UITheme.Thin.InkA(0.85f);
        _iSub.text   = pick ? "원석을 바치면 밤하늘이 룬 두 장을 내린다 — 하나를 고른다" : "원석을 바치면 밤하늘이 룬 하나를 내린다";

        int left = RefineryService.PityLimit - _svc.PityCount;
        _iPity.text  = _svc.PityReady ? "◆ 이번 정제 Legend 확정" : $"Legend 확정까지 {left}회";
        _iPity.color = _svc.PityReady ? UITheme.Thin.Gold : UITheme.Thin.InkA(0.6f);
        var ev = _svc.RoomEvent;
        bool spent = ev == RefineryEvent.HighPurity && !_svc.PurityReady;   // 고순도는 한 번 쓰면 끝
        _iEvent.text  = ev == RefineryEvent.None ? string.Empty
                      : spent ? $"{RefineryService.EventTitle(ev)} — 이미 썼다"
                      : $"◆ {RefineryService.EventTitle(ev)} — {RefineryService.EventLine(ev)} · 이 방에서만";
        _iEvent.color = spent ? UITheme.Thin.InkA(0.5f) : UITheme.Thin.Gold;

        int melt = _svc.MeltForNext;
        _iMelt.gameObject.SetActive(melt > 0);
        _iMeltNote.gameObject.SetActive(melt > 0);
        if (melt > 0)
        {
            _iMeltLabel.text = $"강화재료 {melt} → 원석 {melt}  녹이기";
            _iMeltNote.text  = $"재련소의 강화재료를 원석으로 1 : 1 · 남은 강화재료 {_svc.MaterialOwned}";
        }
    }

    /// <summary>[정제하기] 큰 버튼 — 비용 숫자는 모자라면 빨강, 잠금 중엔 흐림(창 버튼과 같은 규칙).</summary>
    private void RefreshSpinBig(bool afford, bool ready)
    {
        if (_iSpin == null || _svc == null) return;
        _iSpinLabel.color = ready ? UITheme.Thin.Gold : UITheme.Thin.InkA(0.45f);
        _iSpinCost.text   = _iSpinIcon.enabled ? _svc.CurrentCost.ToString() : $"원석 {_svc.CurrentCost}";
        _iSpinCost.color  = afford ? UITheme.Thin.Ink : UITheme.Thin.Danger;
        if (_iSpinIcon.enabled) _iSpinIcon.color = new Color(1f, 1f, 1f, ready ? 1f : 0.5f);
        SetLine(_iSpinLine, ready ? UITheme.Thin.LineReady : UITheme.Thin.LineIdle);
        UIAffordGlow.Set(_iSpin, ready);
    }

    /// <summary>제단의 잔잔한 움직임 — 빛살 · 점선 고리 · 안 고리가 천천히 돌고, 보석이 고리 위를 돌고, 원석이 위아래로 숨 쉰다(실시간).</summary>
    private void ApplyIdleMotion(float now)
    {
        if (_wAltar == null || _plan.Tier > 0) return;   // 연출 · 끝 상태에선 시간표(ApplyAltar)가 제단을 맡는다
        _iRays.rectTransform.localRotation   = Quaternion.Euler(0f, 0f, AltarRaysSpin * now);
        _iDash.rectTransform.localRotation   = Quaternion.Euler(0f, 0f, -DashSpin * now);
        _iRingIn.color = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.26f + 0.08f * Mathf.Sin(now * 1.1f));
        for (int i = 0; i < _iGems.Length; i++)
        {
            float a = (GemAngles[i < GemAngles.Length ? i : 0] + AltarSpin * now) * Mathf.Deg2Rad;
            _iGems[i].anchoredPosition = new Vector2(OrbitR * Mathf.Cos(a), AltarY + OrbitR * Mathf.Sin(a));
        }
        float bob = Mathf.Sin(now * 1.6f);
        _iOre.rectTransform.anchoredPosition     = new Vector2(0f, AltarY + 6f * bob);
        _iOreGlow.rectTransform.anchoredPosition = new Vector2(0f, AltarY + 6f * bob);
        _iOreGlow.color = UITheme.Thin.WithA(Silver, 0.30f + 0.08f * bob);
    }

    // ── 작은 도우미 ──

    /// <summary>화면 모서리 기준 자리 — 왼쪽 위(<paramref name="right"/> false) 또는 오른쪽 위. 피벗도 그 모서리라 화면 비율이 달라도 모서리에 붙는다.</summary>
    private static void Corner(RectTransform rt, bool right, float x, float y, float w, float h)
    {
        var a = new Vector2(right ? 1f : 0f, 1f);
        rt.anchorMin = rt.anchorMax = rt.pivot = a;
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta        = new Vector2(w, h);
    }

    /// <summary>왼끝 기준 자리(화면 가운데 기준 좌표) — 막대 채움이 왼쪽에서 자라고, 열 글이 같은 왼끝에 선다.</summary>
    private static void PutLeft(RectTransform rt, float left, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = Half;
        rt.pivot            = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(left, y);
        rt.sizeDelta        = new Vector2(w, h);
    }
}
