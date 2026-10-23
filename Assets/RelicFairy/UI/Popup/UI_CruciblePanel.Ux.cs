using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 재련소 위계 · 가독성 개편(10-11) — 사용자 「눈에 잘 들어오지 않는 최악의 ux」.
/// 근거 = UI4종 개편설계 v2 보강 · 비교 리서치 C §1-4 「고칠 값」. 구운 프리팹은 그대로 두고 Bind 때 한 번 덮어쓴다(재굽기 없이 보인다).
///  · 강조는 세 뜻만 — 금 = 누를 것 · 준비됨 / 초록 = 오르는 값 / 빨강 = 모자람 · 하락. 보라는 진화 · 형 글자에만.
///  · 글자 다섯 단계 — 15 라벨 · 17 보조 · 20 본문 · 24/32 제목 · 48 큰 수. 본문 Noto 400 · 라벨 Noto 500 · 큰 수 Noto 300. DNF는 「재련소」 한 줄.
///  · 후광은 한 화면 한 곳 · 8px(지금 누를 버튼). 채움 버튼은 [강화하기] 하나 — 진화 · 단련 · 파츠 행 버튼은 1px 테 · 글자만.
///  · 판 테 1px 청동 30% · 대사 띠 64px · 글자 20 · 잉크 0.7. 줄인 44px은 무대 · 정보창이 받는다(Form.cs BandGrow).
/// 동작 · 수치 · 버튼 기능은 그대로다. 구운 계층 · 이름은 바꾸지 않는다.
/// 10-14 v3 「한눈에」 배치(UI_CruciblePanel.V3.cs)가 이 위에 판을 다시 앉힌다 — 여기 글자 · 판 규칙은 v3가 쓰지 않는 부품에도 그대로 남는다.
/// </summary>
public sealed partial class UI_CruciblePanel
{
    // ── 강조 세 뜻(리치 텍스트) ──
    private const string HexUp   = "#7AD46E";   // 오르는 값
    private const string HexDown = "#FF7A6A";   // 모자람 · 하락
    private const string HexMute = "#9A98A0";   // 보조(무채)
    private const string HexForm = "#C9A6FF";   // 진화 · 형 글자

    private const float HaloPx   = 8f;          // 후광 번짐 폭(공용 UIAffordGlow 16px → 8px)
    private const float PortraitPx = 48f;       // 대사 띠 초상 칸
    private const float BannerW  = 340f, BannerH = 36f;   // 화로 칩
    private static readonly Vector2 ReadyMarkPos = new(-2f, -6f);   // 탭 준비 ◆ — 탭 오른쪽 위 모서리(글자 끝과 겹치지 않게 · 10-14 v3 실측)

    private static readonly Color DownRed  = new(1f, 0.478f, 0.416f, 1f);        // #FF7A6A
    // 「강화정보 칸」 아트의 안쪽 색 — 아트(2px 주황 테)를 걷고 같은 바탕에 1px 테만 남긴다
    private static readonly Color PanelInk = new(0.094f, 0.086f, 0.078f, 0.96f);
    private static readonly Color ChipFill = new(0.06f, 0.05f, 0.04f, 0.85f);
    private static readonly Color MuteInk  = UITheme.Thin.InkA(0.6f);

    private bool          _uxReady;
    private RectTransform _evolveEdge, _bannerEdge;

    /// <summary>입구(고르기 판)가 덮고 있다 — 그 아래 버튼은 후광을 켜지 않는다(보이지 않는 불도 「한 곳」을 어긴다).</summary>
    private bool Choosing => _chooser != null && _chooser.activeSelf;

    // ── 짓기(Bind 때 한 번) ──

    /// <summary>구운 글자 · 판을 v2 규칙으로 덮어쓴다 — 크기 · 굵기 · 색 · 테만. 계층 · 이름 · 버튼 배선은 그대로.</summary>
    private void ApplyUxChrome()
    {
        if (_uxReady) return;
        _uxReady = true;
        var ink = UITheme.Thin.Ink;

        // 머리줄 — 「재련소」만 DNF(가짜 굵게 없이), 나머지는 Noto
        if (_window != null)
        {
            var brand = _window.Find("Title")?.GetComponent<TMP_Text>();
            if (brand != null)
            {
                brand.fontStyle = FontStyles.Normal;   // DNF Bold에 가짜 굵게를 한 번 더 걸었다(획 굵기비 0.22)
                brand.fontSize  = brand.fontSizeMax = UITheme.Thin.H2;
            }
            Restyle(_window.Find("Subtitle")?.GetComponent<TMP_Text>(), UIWeight.Regular, UITheme.Thin.Small, MuteInk);
            var exit = _window.Find("Exit");
            if (exit != null) Restyle(exit.GetComponentInChildren<TMP_Text>(true), UIWeight.Medium, UITheme.Thin.Body, BronzeInk);
        }
        Restyle(_fuelText, UIWeight.Medium, UITheme.Thin.Title, Color.white);
        Restyle(_oreText,  UIWeight.Medium, UITheme.Thin.Title, Color.white);

        // 정보판 — 2px 주황 테 → 1px 청동 30%. 표 이름 17 · 값 20(Noto 400). 큰 숫자는 EnsureRateBreak(48 · 300)
        ThinPanel(_infoGroup != null ? _infoGroup.GetComponent<Image>() : null);
        Restyle(_infoTitle, UIWeight.Medium, UITheme.Thin.Body, MuteInk);
        Restyle(_rateLabel, UIWeight.Regular, UITheme.Thin.Small, UITheme.Thin.InkA(0.55f));
        if (_rowLabels != null) foreach (var t in _rowLabels) Restyle(t, UIWeight.Regular, UITheme.Thin.Body, MuteInk);
        for (int i = 0; i < InfoRows; i++) Restyle(RowValue(i), UIWeight.Regular, UITheme.Thin.Lead, ink);
        Restyle(_streakText,      UIWeight.Regular, UITheme.Thin.Small, MuteInk);
        Restyle(_eventEffectText, UIWeight.Medium,  UITheme.Thin.Small, ink);
        Restyle(_resultText,      UIWeight.Medium,  UITheme.Thin.Lead,  ink);

        // 진화 · 단련 · 승급 버튼 — 채움 없이 1px 테 · 보라 글자(색은 RefreshEvolveButton)
        if (_evolveBtn != null)
        {
            var host = (RectTransform)_evolveBtn.transform;
            _evolveEdge = host.Find("ThinEdge") as RectTransform;
            if (_evolveEdge == null)
                _evolveEdge = UIThinBox.Create(host, "ThinEdge", Vector2.zero, Vector2.one, 0f, UITheme.Thin.LineIdle, Color.clear, Color.clear);
            Restyle(_evolveLabel, UIWeight.Medium, UITheme.Thin.Lead, FormInk);
        }

        // 무대 — 이름 24 · 부제 17 · 단계 48(300) · 공격 줄 24 · 눈금 15
        Restyle(_cardName[0],    UIWeight.Medium,  UITheme.Thin.Title, ink);
        Restyle(_focusTypeText,  UIWeight.Regular, UITheme.Thin.Body,  MuteInk);
        Restyle(_cardLevel[0],   UIWeight.Light,   UITheme.Thin.H1,    ink);
        Restyle(_cardAtk[0],     UIWeight.Medium,  UITheme.Thin.Title, ink);
        if (_ticks != null) foreach (var t in _ticks) Restyle(t, UIWeight.Medium, UITheme.Thin.Small, TickDim);

        // 원거리 무대 — 상세판도 정보판과 같은 1px 테. 행 강화 버튼은 RefreshRangedParts가 테를 칠한다
        Restyle(_dockTypeText, UIWeight.Medium,  UITheme.Thin.Title, ink);
        Restyle(_slotSummary,  UIWeight.Regular, UITheme.Thin.Body,  MuteInk);
        ThinPanel(_cardBg[1]);
        Restyle(_cardName[1],     UIWeight.Medium,  UITheme.Thin.Title, ink);
        Restyle(_partKindText,    UIWeight.Regular, UITheme.Thin.Body,  MuteInk);
        Restyle(_cardLevel[1],    UIWeight.Medium,  UITheme.Thin.Lead,  ink);
        Restyle(_cardAtk[1],      UIWeight.Medium,  UITheme.Thin.Lead,  ink);
        Restyle(_partGrowthText,  UIWeight.Regular, UITheme.Thin.Body,  MuteInk);
        Restyle(_partDescText,    UIWeight.Regular, UITheme.Thin.Body,  ink);
        Restyle(_partSynergyText, UIWeight.Regular, UITheme.Thin.Body,  UITheme.Thin.InkA(0.85f));
        Restyle(_partCostText,    UIWeight.Regular, UITheme.Thin.Body,  MuteInk);
        for (int i = 0; i < PartRows; i++)
        {
            Restyle(_slotKind[i],   UIWeight.Regular, UITheme.Thin.Body,  ink);
            Restyle(_slotName[i],   UIWeight.Medium,  UITheme.Thin.Lead,  ink);
            Restyle(_slotLv[i],     UIWeight.Regular, UITheme.Thin.Body,  UITheme.Thin.InkA(0.75f));
            Restyle(_slotVal[i],    UIWeight.Regular, UITheme.Thin.Body,  MuteInk);
            Restyle(_partsMark[i],  UIWeight.Regular, UITheme.Thin.Small, MuteInk);
            Restyle(_slotBtnLbl[i], UIWeight.Medium,  UITheme.Thin.Body,  UITheme.Thin.Gold);
        }

        ThinBand();
    }

    /// <summary>
    /// 대사 띠 — 높이 107 → 64 · 글자 27 → 20 · 잉크 0.7(리서치 C §1 P2: 대사가 정보 줄보다 크고 밝았다).
    /// 줄인 판일 때만 자리를 옮긴다(무대 · 정보창이 그만큼 아래로 늘었다). 띠 아트(회색 3px 테)는 걷고 1px 테.
    /// </summary>
    private void ThinBand()
    {
        var band = _bandGroup != null ? (RectTransform)_bandGroup.transform : FindDeep("DialogueBand") as RectTransform;
        if (band == null) return;
        if (_compact) Place(band, LogX, LogY + BandGrow, LogW, BandH);
        float h = _compact ? BandH : LogH;
        if (band.TryGetComponent<Image>(out var bg))
        {
            bg.sprite = null;
            bg.type   = Image.Type.Simple;
            bg.color  = PanelInk;
        }
        UIThinBox.Create(band, "ThinEdge", Vector2.zero, Vector2.one, 0f, UITheme.Thin.LineIdle, Color.clear, Color.clear);

        // 초상 — 88 → 48, 테 2px 청동 → 1px 청동 30%
        if (band.Find("Portrait") is RectTransform por)
        {
            PlaceIn(por, 20f, (h - PortraitPx) * 0.5f, PortraitPx, PortraitPx, LogW, h);
            if (por.TryGetComponent<Image>(out var edge)) edge.color = UITheme.Thin.LineGold;
            if (por.Find("Fill") is RectTransform fill)
            {
                ShopUIStyle.Stretch(fill, UITheme.Thin.Stroke);
                if (fill.Find("Art") is RectTransform art) art.sizeDelta = new Vector2(36f, 23f);
            }
        }

        var who = band.Find("Speaker")?.GetComponent<TMP_Text>();
        Restyle(who, UIWeight.Medium, UITheme.Thin.Small, MuteInk);
        if (who != null) PlaceIn(who.rectTransform, 82f, 0f, 84f, h, LogW, h);
        if (_dialogText != null)
        {
            Restyle(_dialogText, UIWeight.Regular, UITheme.Thin.Lead, UITheme.Thin.InkA(0.7f));
            _dialogText.fontSizeMin = UITheme.Thin.Body;
            PlaceIn(_dialogText.rectTransform, 172f, 4f, LogW - 172f - BannerW - 48f, h - 8f, LogW, h);
        }

        // 화로 칩 — 붉은 상자 → 얇은 선 칩(금 = 화로 이벤트 · 10-18 저주 · 위험은 없앴다). 색은 RefreshBandBadge
        if (_eventBanner != null && _eventBanner.transform is RectTransform chip)
        {
            PlaceIn(chip, LogW - 24f - BannerW, (h - BannerH) * 0.5f, BannerW, BannerH, LogW, h);
            if (chip.TryGetComponent<Image>(out var edge)) edge.color = Color.clear;
            if (chip.Find("Fill") is RectTransform fill)
            {
                ShopUIStyle.Stretch(fill);
                if (fill.TryGetComponent<Image>(out var f)) f.color = Color.clear;
            }
            _bannerEdge = UIThinBox.Create(chip, "ThinEdge", Vector2.zero, Vector2.one, 0f, UITheme.Thin.LineActive, Color.clear, ChipFill);
            Restyle(_eventBannerText, UIWeight.Medium, UITheme.Thin.Small, UITheme.Thin.Gold);
            if (_eventBannerText != null) ShopUIStyle.Stretch(_eventBannerText.rectTransform, 4f);
        }
    }

    // ── 도우미 ──

    /// <summary>톤 v2 글자로 덮어쓴다 — 굵기 글꼴 · 가짜 굵게 없음 · 크기 · 색. 자동 크기가 15 아래로 내려가지 않게 묶는다.</summary>
    private static void Restyle(TMP_Text t, UIWeight weight, float size, Color color)
    {
        if (t == null) return;
        UITheme.StyleThinText(t, weight, size, color);
        t.fontSizeMin = size <= UITheme.Thin.Lead ? Mathf.Min(size, UITheme.Thin.Small) : size * 0.75f;
    }

    /// <summary>납품 판 아트(2px 주황 테)를 걷고 같은 안쪽 색 + 1px 청동 30% 테 — 리서치 C §1 P1. 두 번 불러도 한 벌.</summary>
    private static void ThinPanel(Image panel)
    {
        if (panel == null || panel.transform.Find("ThinEdge") != null) return;
        panel.sprite = null;
        panel.type   = Image.Type.Simple;
        panel.color  = PanelInk;
        UIThinBox.Create(panel.rectTransform, "ThinEdge", Vector2.zero, Vector2.one, 0f, UITheme.Thin.LineGold, Color.clear, Color.clear);
    }

    /// <summary>UIThinBox 네 변의 색만 바꾼다(바탕은 그대로).</summary>
    private static void SetBoxLine(RectTransform box, Color color)
    {
        if (box == null) return;
        for (int i = 0; i < box.childCount; i++)
        {
            var c = box.GetChild(i);
            if (c.name != "Fill" && c.TryGetComponent<Image>(out var img)) img.color = color;
        }
    }

    /// <summary>
    /// 후광 — 한 화면 한 곳 · 8px(지금 누를 버튼). 공용 <see cref="UIAffordGlow"/>(16px)를 그대로 쓰고 번짐 폭만 줄인다(공용 파일은 고치지 않는다).
    /// </summary>
    private static void Halo(Component target, bool on)
    {
        if (target == null) return;
        UIAffordGlow.Set(target, on);
        if (!on) return;
        var g = target.transform.Find("AffordGlow") as RectTransform;
        if (g == null || Mathf.Approximately(g.offsetMax.x, HaloPx)) return;
        g.offsetMin = new Vector2(-HaloPx, -HaloPx);
        g.offsetMax = new Vector2(HaloPx, HaloPx);
        if (g.TryGetComponent<Image>(out var img)) img.sprite = UIProceduralSprites.SoftHalo(radius: 10f, feather: HaloPx);
    }

    /// <summary>탭 「여기도 할 것이 있다」 — 후광 대신 머리 오른쪽 금 ◆(후광은 한 화면 한 곳).</summary>
    private static void TabReady(Image tab, bool on)
    {
        if (tab == null) return;
        UIAffordGlow.Set(tab, false);
        var mark = tab.transform.Find("ReadyMark");
        if (mark == null)
        {
            if (!on) return;
            var t = ThinText(tab.transform, "ReadyMark", FMin, UIWeight.Medium, TextAlignmentOptions.Center, UITheme.Thin.Gold);   // 16 — v3 본문 최소(10-14)
            t.text = "◆";
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.one;
            rt.sizeDelta = new Vector2(22f, 22f);
            rt.anchoredPosition = ReadyMarkPos;
            mark = rt;
        }
        mark.gameObject.SetActive(on);
    }

    /// <summary>화로 한 줄 — 「▲ 화로 · 재료 비용 50%」. 이벤트가 없으면 빈 문자열(저주는 10-18 재련 개편으로 없앴다).</summary>
    private string ForgeEventLine()
    {
        if (_controller == null || !_controller.HasEvent) return string.Empty;
        return $"▲ 화로 · {_controller.EventEffectDesc}";
    }
}
