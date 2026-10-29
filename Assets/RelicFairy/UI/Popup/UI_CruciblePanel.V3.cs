using System.Text;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 재련소 v3 「한눈에」(10-14 · 구현설계/정제소·재련소_v3_벤치마킹설계_20261014.md §1-2) — 로스트아크 재련 · 원신 무기 돌파.
/// 창 1560×880(1920×1080 캔버스) = 머리 64 · 왼쪽 판 760 · 오른쪽 판 680 · 아래 72. 한 눈의 초점은 둘(리서치 B-2-1).
///  · 머리 — 「재련소」 · 탭 [무기 강화] [원거리 파츠](글자 + 금 밑줄 — 제단 탭과 같은 결) · 재화 · 나가기.
///  · 왼쪽 판(무엇이 좋아지나) — 모루 위 무기 320 · 이름 + 「+2 / 6」 · 초점 「+2 → +3」 72 · 바뀌는 수치(지금 → 다음 · 초록 ▲) ·
///    단계 점(성장 길 · 불꽃 막대 대신) · 「다음」 한 줄 · 형 칸 한 줄(누르면 4b 형 화면 — <see cref="ShowEvolvePanel"/>).
///  · 오른쪽 판(될까 · 얼마) — 성공 확률 96 한 숫자 + 원인 칩(▲ +15 화로) · 연속 게이지(잭팟) · 재료 보유/필요 · 실패 한 줄 · [강화하기 · 비용] 84.
///  · 아래 — 대장장이 말풍선(왼쪽 · 작게) · 단축키 안내(오른쪽).
/// 원거리 파츠 탭도 같은 두 판 — 왼쪽 = 파츠 목록 + 고른 파츠(초점 「Lv 3 → 4」), 오른쪽 = 발사 미리보기 96 · 표 · 재료 · [강화하기].
///
/// 구운 프리팹(1600×1000)은 그대로 두고 Bind 때 한 번 다시 앉힌다(v2 · 형 UI와 같은 길). 쓰는 부품은 v3 판으로 옮기고,
/// 대신하는 옛 부품(성장 길 · 불꽃 막대 · 표 3줄 · 화로 칩 · 형 칸 묶음)은 꺼 둔 보관함에 둔다 — 갱신 코드는 그대로 돈다.
/// 덮개 창(진화 · 단련 = 4b)은 창(_window · 1600×1000 비율) 좌표 그대로다. 남색 판은 제단 · 상점과 같은 Artsystack 판(UISkin.Achievement).
/// </summary>
public sealed partial class UI_CruciblePanel
{
    // ── v3 판(1560×880) ──
    private const float V3W = 1560f, V3H = 880f, V3HeadH = 64f;
    private const float V3BoardY = 80f, V3BoardH = 720f;
    private const float V3LeftX = 40f, V3LeftW = 760f, V3RightX = 840f, V3RightW = 680f;
    private const float V3BarY = 812f, V3BarH = 60f, V3BarW = V3W - V3LeftX * 2f;
    private const float LPad = 28f, RPad = 40f, RInner = V3RightW - RPad * 2f;   // 오른쪽 판 안쪽 폭 600

    // 글자(1080 기준 px) — 본문 최소 16 · 표 18 · 줄 20 · 제목 24 · 큰 숫자 72 / 96. 기울임 없음.
    private const float FMin = 16f, FTable = 18f, FLead = 20f, FTitle = 24f, FFocal = 72f, FBig = 96f;

    // 왼쪽 판(근거리) — 모루 위 무기 320 · 초점 열(무기 무리의 세로 가운데에 맞춘다)
    private const float WpX = 28f, WpY = 92f, WpWH = 320f;
    private const float AnX = 83f, AnY = 398f, AnW = 210f, AnH = 132f;
    private const float StrikeV3X = 203f, StrikeV3Y = 412f;    // 망치가 닿는 점(모루 윗면) — 불꽃 층을 여기로 옮긴다
    private const float EmberShiftX = -250f;                   // 불씨(무대 x 180~780) → 무기 열(0~372) 안
    private const float FcX = 372f, FcW = 360f, FcCapY = 168f, FcY = 200f, FcH = 112f;   // 72의 줄 높이 ≈ 104 — 칸 112(10-14 실측: 96이면 66까지 줄었다)
    private const float StatY0 = 318f, StatH = 44f;
    private const int   StatRows = 3;

    // 단계 점 · 「다음」 · 형 칸 — 판 위 y(근거리 548 · 원거리 604). 칸 안은 위 기준 고정 px
    private const float PathMeleeY = 548f, PathRangedY = 604f, PathH = 164f;
    private const float PipX0 = 108f, PipX1 = 732f, PipCy = 16f, PipLabelY = 34f, PipD = 14f, PipDia = 16f;

    // 오른쪽 판(근거리) — 숫자 구역 · 연속 게이지 · 재료 줄 · 실패 한 줄 y(버튼은 바닥 596)
    private const float ChipMidY = 132f, RuleY1 = 214f, StreakY = 236f, RuleY2 = 438f, MatY = 448f;
    // 10-19 ⑰ 가진 증강 · 켠 파츠 세로 목록 — 머리 아래 한 줄 26px · 6줄(넘치면 마지막 줄 「+N개 더」 · 손을 올리면 전부)
    private const int   OwnedRowsMax = 6;
    private const float OwnedRowsY = StreakY + 36f, OwnedRowH = 26f, OwnedNameW = 230f;

    // 원거리 왼쪽 판 — 파츠 5행(704×52) · 고른 파츠 판
    private const float PrY0 = 62f, PrH = 52f, PrGap = 6f, PrW = 704f;
    private const float PdY = 352f, PdH = 248f;

    // 결과 연출 길이(설계 §1-2 — 결과 무게에 비례 · 카메라 · 화면 흔들림 없음)
    private const float V3SuccessDur   = 0.40f;   // 평범한 성공 — 「+N」 펀치
    private const float V3MilestoneDur = 1.50f;   // 이정표 — 형 열림 · 진화 · 승급
    private const float V3PunchAmp     = 0.14f;

    private const float PartStrikeDur  = 0.18f;   // 파츠 판정 — 초점 펀치(10-15)
    private const float PartStrikeHold = 0.22f;   // 펀치 뒤 공개까지 — 합 0.4초 「두근」

    private static readonly Color V3Base    = new(0.055f, 0.051f, 0.086f, 1f);     // (14,13,22) — 남색 판 아래 불투명 바탕
    private static readonly Color V3BoardC  = new(0.075f, 0.071f, 0.118f, 0.92f);
    private static readonly Color V3Card    = new(0.090f, 0.086f, 0.140f, 1f);
    private static readonly Color V3Bubble  = new(0.090f, 0.085f, 0.140f, 0.96f);
    private static readonly Color V3Detail  = new(0.10f, 0.095f, 0.15f, 0.55f);
    private static readonly Color UpGreen   = new(0.478f, 0.831f, 0.431f, 1f);     // #7AD46E
    private static readonly Regex SizeTag   = new("</?size[^>]*>", RegexOptions.Compiled);

    private bool _v3Ready;
    private int  _v3Tab = -1;
    private RectTransform _v3Root, _v3Park, _v3Host, _v3Path, _v3MeleeRight, _v3StreakGroup, _v3StreakFill, _v3MatRow, _v3MatChipBox;
    private CanvasGroup   _v3PathGroup;
    private Vector2       _v3Area;

    // 왼쪽
    private TMP_Text _v3FocalCap, _v3Focal, _v3PartFocal, _v3PathCap, _v3Next, _v3Hint;
    private readonly TMP_Text[] _v3StatLabel = new TMP_Text[StatRows], _v3StatValue = new TMP_Text[StatRows], _v3StatDelta = new TMP_Text[StatRows];
    private readonly Image[]    _v3StatLine  = new Image[StatRows + 1];
    private readonly Image[]    _v3Pip       = new Image[MaxTicks];
    private readonly TMP_Text[] _v3PipLabel  = new TMP_Text[MaxTicks];
    private Image _v3PipGlow, _v3RowGlow;
    private Sprite _pipDisc, _pipRing, _pipDia, _pipDiaLine;

    // 오른쪽
    private readonly RectTransform[] _v3ChipBox = new RectTransform[3];
    private readonly TMP_Text[]      _v3Chip    = new TMP_Text[3];
    private TMP_Text _v3StreakHead, _v3StreakVal, _v3StreakRule, _v3MatValue, _v3MatChip, _v3Fail;
    private readonly TMP_Text[] _ownedName = new TMP_Text[OwnedRowsMax];
    private readonly TMP_Text[] _ownedEffect = new TMP_Text[OwnedRowsMax];
    private TMP_Text _v3EvolveSub;      // 버튼 아래 한 줄(10-18) — 진화 예고 · 진화 미루고 무형검 강화
    private Button   _v3EvolveSubBtn;
    private bool     _v3SubOpensForm;   // 버튼 아래 줄 = 형 단련 입구(10-22)
    private UIThinFrame _v3EnhanceFrame;
    private Transform   _rowLineTop;

    // 판정 전(망치가 닿기 전) 붙잡아 둘 값 — 결과가 새지 않게(HoldBeforeState와 같은 규칙)
    private string _v3NextBefore, _v3FormBeforeText;
    private bool   _v3FormBeforeOn;

    private static Sprite BoardArt => UISkin.Achievement != null ? UISkin.Achievement.AltarDetailPanel : null;
    private static Sprite FrameArt => UISkin.Achievement != null ? UISkin.Achievement.AltarWindow : null;

    // ── 짓기(Bind 때 한 번 — EnsureFormUi · ApplyUxChrome 뒤) ──

    private void ApplyV3Layout()
    {
        if (_v3Ready || _window == null || _stageArea == null) return;
        var info = _infoGroup != null ? (RectTransform)_infoGroup.transform : FindDeep("InfoPanel") as RectTransform;
        if (info == null) return;
        EnsureRateBreak();   // v2 줄(「기본 55% · ▲ …」)을 먼저 만들어 둔다 — 아래에서 보관함으로 옮기고 원인 칩이 대신한다

        // 창 바탕 · 청동 테는 걷는다. 창 rect(1600×1000 비율 · 화면 맞춤)는 덮개 창 좌표라 그대로 두고, v3 판이 그 가운데 앉는다.
        if (_window.TryGetComponent<Image>(out var winImg)) winImg.color = Color.clear;
        _v3Park = ShopUIStyle.MakeRect(_window, "V3Parked").GetComponent<RectTransform>();
        _v3Park.gameObject.SetActive(false);
        Park(_window.Find("Backdrop"));
        Park(_track);   // 성장 길 → 단계 점 · 「다음」 한 줄(갱신은 Form.cs가 그대로 한다 — 「다음」 글을 v3가 읽어 간다)

        BuildV3Root();
        LayoutV3Header();
        LayoutV3Boards();
        LayoutV3Melee();
        LayoutV3Ranged();
        LayoutV3Path();
        LayoutV3Right(info);
        LayoutV3Bottom(_bandGroup != null ? (RectTransform)_bandGroup.transform : FindDeep("DialogueBand") as RectTransform);

        _v3Ready = true;
        ApplyTabV3(_tabWeaponImg, _activeTab == 0);
        ApplyTabV3(_tabRangedImg, _activeTab == 1);
        if (_enhanceBtn != null) _enhanceBtn.gameObject.SetActive(true);   // 두 탭 공용 — 파츠 탭에선 고른 파츠를 올린다
    }

    /// <summary>창 안 가운데 1560×880 — 불투명 남색 바탕 + 제단 창 판(InfoBox 남색 재색칠) + 1px 금선 · 모서리 눈금. 맨 뒤 형제(덮개 창이 위로).</summary>
    private void BuildV3Root()
    {
        var root = ShopUIStyle.MakeImage(_window, "V3", V3Base, raycast: true);
        _v3Root = root.rectTransform;
        _v3Root.SetSiblingIndex(0);
        _v3Root.anchorMin = _v3Root.anchorMax = _v3Root.pivot = new Vector2(0.5f, 0.5f);
        _v3Root.anchoredPosition = Vector2.zero;
        _v3Root.sizeDelta = new Vector2(V3W, V3H);
        root.gameObject.AddComponent<UIFontScaleExempt>();   // 창 맞춤(ScaleFonts)이 v3 글자를 다시 키우지 않게 — 판은 FitV3가 통째로 맞춘다

        var art = FrameArt;
        if (art != null)
        {
            var a = ShopUIStyle.MakeImage(_v3Root, "V3Art", Color.white);
            ShopUIStyle.Stretch(a.rectTransform);
            ShopUIStyle.Skin(a, art, sliced: true);
        }
        var edge = UIThinBox.Create(_v3Root, "V3Edge", Vector2.zero, Vector2.one, 0f, UITheme.Thin.LineActive, UITheme.Thin.TickGold, Color.clear);
        edge.SetSiblingIndex(art != null ? 1 : 0);

        // 모서리 마름모 — 장식은 지우지 않고 작게(유물 컨셉 · 10-08)
        int k = 0;
        for (int i = _window.childCount - 1; i >= 0 && k < 4; i--)
        {
            var d = _window.GetChild(i);
            if (d.name != "Diamond" || d is not RectTransform drt) continue;
            drt.SetParent(_v3Root, false);
            const float s = 22f, o = 7f;
            V3Place(drt, k % 2 == 0 ? -o : V3W - s + o, k < 2 ? -o : V3H - s + o, s, s);
            k++;
        }

        _v3Host = (RectTransform)transform;
        FitV3();
    }

    private void LayoutV3Header()
    {
        if (_window.Find("Anvil") is RectTransform anvil)
        {
            anvil.SetParent(_v3Root, false);
            V3Place(anvil, V3LeftX, 17f, 46f, 30f);
        }
        var brand = _window.Find("Title")?.GetComponent<TMP_Text>();
        if (brand != null)
        {
            brand.transform.SetParent(_v3Root, false);
            V3Place(brand.rectTransform, 96f, 0f, 150f, V3HeadH);
            brand.alignment        = TextAlignmentOptions.MidlineLeft;
            brand.enableAutoSizing = false;
            brand.fontStyle        = FontStyles.Normal;
            brand.fontSize         = UITheme.Thin.H2;   // 화면 이름 「재련소」 한 줄만 DNF 32(v2 그대로) — 판 안 제목은 24
        }
        Park(_window.Find("Subtitle"));

        LayoutV3Tab(_tabWeaponImg, "무기 재련", 260f, 144f);     // 탭 폭 = 글자 + 양옆 여유 — 준비 ◆가 글자 끝과 떨어지게(10-19 「강화」 → 「재련」)
        LayoutV3Tab(_tabRangedImg, "파츠 재련", 412f, 160f);
        LayoutV3Pill(_matPill, 1100f, _fuelText, _skin?.MaterialIcon, MatTint);
        LayoutV3Pill(_orePill, 1262f, _oreText, _skin?.OreIcon, OreTint);

        if (_window.Find("Exit") is RectTransform exit)
        {
            exit.SetParent(_v3Root, false);
            V3Place(exit, 1424f, 14f, 96f, 36f);
            if (exit.TryGetComponent<Image>(out var ei)) { ei.sprite = null; ei.color = Color.clear; }
            if (exit.Find("Fill")?.GetComponent<Image>() is Image fill)
            {
                fill.sprite = null;
                fill.color  = UITheme.Thin.GlassFill;
                ShopUIStyle.Stretch(fill.rectTransform, 1f);
            }
            UIThinBox.Create(exit, "V3Edge", Vector2.zero, Vector2.one, 0f, UITheme.Thin.LineIdle, Color.clear, Color.clear);
            V3Text(exit.GetComponentInChildren<TMP_Text>(true), UIWeight.Medium, FTable, UITheme.Thin.InkA(0.85f));
        }

        var rule = ShopUIStyle.MakeImage(_v3Root, "V3HeadRule", UITheme.Thin.WithA(UITheme.Thin.Gold, 0.22f));
        V3Place(rule.rectTransform, V3LeftX, V3HeadH, V3W - V3LeftX * 2f, 1f);
    }

    /// <summary>머리 탭 — 판 그림 없이 글자 20 + 고른 탭만 금 밑줄(제단 탭 밑줄 그림을 같이 쓴다). 탭 그림의 구운 글자는 48px 높이에서 9px로 줄어 버렸다.</summary>
    private void LayoutV3Tab(Image tab, string label, float x, float w)
    {
        if (tab == null) return;
        tab.transform.SetParent(_v3Root, false);
        V3Place(tab.rectTransform, x, 0f, w, V3HeadH);
        tab.sprite = null;
        tab.type   = Image.Type.Simple;
        tab.color  = Color.clear;
        tab.raycastTarget = true;
        if (tab.transform.Find("L")?.GetComponent<TMP_Text>() is TMP_Text lbl)
        {
            lbl.text = label;
            V3Text(lbl, UIWeight.Medium, FLead, UITheme.Thin.Ink);
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.textWrappingMode = TextWrappingModes.NoWrap;
            ShopUIStyle.Stretch(lbl.rectTransform);
        }
        if (tab.transform.Find("Underline") is RectTransform line && line.TryGetComponent<Image>(out var li))
        {
            var art = UISkin.Achievement != null ? UISkin.Achievement.AltarTabUnderline : null;
            line.anchorMin = line.anchorMax = new Vector2(0.5f, 0f);
            line.pivot = new Vector2(0.5f, 0f);
            line.anchoredPosition = new Vector2(0f, 4f);
            if (art != null)
            {
                li.sprite = UISpriteMaster.Neutral(art);
                li.type   = Image.Type.Simple;
                li.preserveAspect = true;
                line.sizeDelta = new Vector2(104f, 15f);
            }
            else
            {
                li.sprite = null;
                line.sizeDelta = new Vector2(w * 0.6f, 2f);
            }
            li.color = UITheme.Thin.Gold;
        }
        if (tab.transform.Find("ReadyMark")?.GetComponent<TMP_Text>() is TMP_Text mark)
        {
            V3Text(mark, UIWeight.Medium, FMin, UITheme.Thin.Gold);
            mark.rectTransform.anchoredPosition = ReadyMarkPos;
        }
    }

    /// <summary>탭 고름 — 글자 밝기 + 금 밑줄. 판은 투명(누르는 면만 남긴다).</summary>
    private static void ApplyTabV3(Image tab, bool active)
    {
        if (tab == null) return;
        tab.sprite = null;
        tab.color  = Color.clear;
        if (tab.transform.Find("L")?.GetComponent<TMP_Text>() is TMP_Text lbl)
        {
            lbl.gameObject.SetActive(true);
            lbl.color = active ? UITheme.Thin.Ink : UITheme.Thin.InkA(0.55f);
        }
        var line = tab.transform.Find("Underline");
        if (line != null) line.gameObject.SetActive(active);
    }

    /// <summary>재화 칸 — HUD와 같은 조각 그대로, 머리 높이에 맞춰 150×40.</summary>
    private void LayoutV3Pill(GameObject pill, float x, TMP_Text value, Sprite icon, Color tint)
    {
        if (pill == null) return;
        var rt = (RectTransform)pill.transform;
        rt.SetParent(_v3Root, false);
        V3Place(rt, x, 12f, 150f, 40f);
        if (rt.TryGetComponent<Image>(out var bg) && bg.sprite != null) ShopUIStyle.Skin(bg, bg.sprite, sliced: true);   // 칸이 줄어든 만큼 9-slice 경계 다시 재기
        if (rt.Find("Frame")?.GetComponent<Image>() is Image frame && frame.sprite != null) ShopUIStyle.Skin(frame, frame.sprite, sliced: true, tint: tint);
        if (rt.Find("Icon") is RectTransform ic)
        {
            PlaceIn(ic, 12f, 8f, 24f, 24f, 150f, 40f);
            if (icon != null && ic.TryGetComponent<Image>(out var ii)) ii.preserveAspect = true;
        }
        if (value != null)
        {
            PlaceIn(value.rectTransform, 44f, 2f, 96f, 36f, 150f, 40f);
            V3Text(value, UIWeight.Medium, FLead, Color.white);
            value.alignment = TextAlignmentOptions.MidlineRight;
            value.textWrappingMode = TextWrappingModes.NoWrap;
        }
    }

    /// <summary>왼쪽 판 = 무대 칸(StageArea)을 옮겨 남색 판을 깐다. 두 탭 무대의 옛 마법진 · 청동 테는 끈다.</summary>
    private void LayoutV3Boards()
    {
        _stageArea.SetParent(_v3Root, false);
        V3Place(_stageArea, V3LeftX, V3BoardY, V3LeftW, V3BoardH);
        var bg = ShopUIStyle.MakeImage(_stageArea, "V3BoardBg", V3BoardC);
        bg.transform.SetAsFirstSibling();
        ShopUIStyle.Stretch(bg.rectTransform);
        V3Panel(bg, BoardArt);

        foreach (var s in new[] { _meleeStage, _rangedStage })
        {
            if (s == null) continue;
            if (s.TryGetComponent<Image>(out var si)) { si.sprite = null; si.color = Color.clear; }
            var ink = s.Find("Ink");
            if (ink != null) ink.gameObject.SetActive(false);
        }

        if (_stageResult != null)
        {
            // 판정 도장 — 무기 그림 위 · 그림 테(320) 안쪽 288 안에서만(긴 문구는 20까지 줄어든다 · 10-14 실측)
            SP(_stageResult.transform, WpX + 16f, 150f, WpWH - 32f, 80f);
            V3Text(_stageResult, UIWeight.Medium, 42f, UITheme.Thin.Ink, autosize: true, min: FLead);
            _stageResult.alignment = TextAlignmentOptions.Center;
            _stageResult.textWrappingMode = TextWrappingModes.NoWrap;
            TMPOutlineHelper.ApplySoftShadow(_stageResult);
        }
    }

    private void LayoutV3Melee()
    {
        var s = _meleeStage;
        if (s == null) return;

        SP(_forgeGlow != null ? _forgeGlow.transform : null, -120f, 330f, 616f, 480f);
        // 불씨 — 무기 열(모루 아래까지) 안에서만 오른다(초점 · 수치 · 단계 점 글자 위를 지나가지 않게)
        if (_emberRoot != null)
        {
            var mask = ShopUIStyle.MakeRect(s, "V3EmberMask", typeof(RectMask2D)).GetComponent<RectTransform>();
            mask.SetSiblingIndex(_emberRoot.GetSiblingIndex());
            SP(mask, 0f, 0f, FcX, PathMeleeY - 8f);
            _emberRoot.SetParent(mask, false);
            _emberRoot.anchorMin = _emberRoot.anchorMax = _emberRoot.pivot = new Vector2(0f, 1f);
            _emberRoot.sizeDelta = new Vector2(V3LeftW, V3BoardH);
            // 불씨는 무대 바닥(StageHNow + 8)에서 생긴다 — 층을 올려 그 바닥이 마스크 아래 끝(모루 밑)에 오게 한다
            _emberRoot.anchoredPosition = new Vector2(EmberShiftX, StageHNow + 8f - (PathMeleeY - 8f));
        }

        // 이름 + 「+2 / 6」 · 부제
        var name = _cardName[0];
        if (name != null)
        {
            SP(name.transform, LPad, 18f, 472f, 36f);
            V3Text(name, UIWeight.Medium, FTitle, UITheme.Thin.Ink, autosize: true, min: FTable);
            name.alignment = TextAlignmentOptions.MidlineLeft;
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;
        }
        var lvl = _cardLevel[0];
        if (lvl != null)
        {
            var tag = lvl.transform.parent;
            lvl.transform.SetParent(s, false);
            SP(lvl.transform, 500f, 18f, 232f, 36f);
            V3Text(lvl, UIWeight.Regular, FTitle, UITheme.Thin.Ink);
            lvl.alignment = TextAlignmentOptions.MidlineRight;
            lvl.textWrappingMode = TextWrappingModes.NoWrap;
            if (tag != null && tag.name == "LevelTag") Park(tag);   // 단계 방패 그림 — 단계는 이름 줄 오른쪽 글자가 말한다
        }
        if (_focusTypeText != null)
        {
            SP(_focusTypeText.transform, LPad, 54f, 472f, 28f);
            V3Text(_focusTypeText, UIWeight.Regular, FTable, UITheme.Thin.InkA(0.6f));
            _focusTypeText.alignment = TextAlignmentOptions.MidlineLeft;
        }

        // 모루 위 무기 320 — 빛 · 빛살 · 판정 빛은 무기 칸 가운데를 중심으로
        float cx = WpX + WpWH * 0.5f, cy = WpY + WpWH * 0.5f;
        SP(_rays != null ? _rays.transform : null, cx - 272f, cy - 272f, 544f, 544f);
        SP(_weaponGlow != null ? _weaponGlow.transform : null, cx - 200f, cy - 200f, 400f, 400f);
        SP(_cardBg[0] != null ? _cardBg[0].transform : null, WpX, WpY, WpWH, WpWH);
        SP(s.Find("StageAnvil"), AnX, AnY, AnW, AnH);
        if (_hammer != null)
        {
            var pv = new Vector2((StrikeV3X + HammerPivotX - StrikeX) / V3LeftW, 1f - StrikeV3Y / V3BoardH);
            _hammer.anchorMin = _hammer.anchorMax = pv;
        }
        if (_sparkRoot != null)
        {
            // 불꽃은 (StrikeX, StrikeYNow) 무대 px로 터진다(Form.cs 단련 연출도) — 층을 옮겨 그 점이 v3 모루에 오게 한다
            _sparkRoot.anchorMin = _sparkRoot.anchorMax = _sparkRoot.pivot = new Vector2(0f, 1f);
            _sparkRoot.sizeDelta = new Vector2(V3LeftW, V3BoardH);
            _sparkRoot.anchoredPosition = new Vector2(StrikeV3X - StrikeX, -(StrikeV3Y - StrikeYNow));
        }
        SP(_stageFlash != null ? _stageFlash.transform : null, cx - 400f, cy - 320f, 800f, 640f);
        if (_jackpotBanner != null)
        {
            SP(_jackpotBanner.transform, LPad, 8f, V3LeftW - LPad * 2f, 80f);
            V3Text(_jackpotBanner, UIWeight.Light, 48f, JackpotInk);
            _jackpotBanner.alignment = TextAlignmentOptions.Center;
        }
        Park(_zoneBg);                  // 위험 배지 → 오른쪽 판 실패 한 줄
        Park(s.Find("AtkPlate"));       // 공격 줄 → 바뀌는 수치 줄
        Park(_cardAtk[0]);
        Park(s.Find("Gauge"));          // 불꽃 막대 · 눈금 → 단계 점

        // 초점 「+2 → +3」 72 · 바뀌는 수치
        _v3FocalCap = NewText(s, "V3FocalCap", FTable, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.6f));
        SP(_v3FocalCap.transform, FcX, FcCapY, FcW, 28f);
        _v3Focal = NewText(s, "V3Focal", FFocal, UIWeight.Light, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
        _v3Focal.enableAutoSizing = true;
        _v3Focal.fontSizeMax = FFocal;
        _v3Focal.fontSizeMin = 56f;   // 「+12 → +13」처럼 긴 단계만 칸 안에서 줄어든다
        SP(_v3Focal.transform, FcX, FcY, FcW, FcH);
        _v3Focal.rectTransform.pivot = new Vector2(0f, 0.5f);   // 펀치가 왼쪽 끝을 지킨다

        for (int i = 0; i <= StatRows; i++)
        {
            _v3StatLine[i] = ShopUIStyle.MakeImage(s, "V3StatLine", UITheme.Thin.InkA(0.10f));
            SP(_v3StatLine[i].transform, FcX, StatY0 + i * StatH, FcW, 1f);
        }
        for (int i = 0; i < StatRows; i++)
        {
            float y = StatY0 + i * StatH;
            _v3StatLabel[i] = NewText(s, "V3StatLabel", FTable, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.6f));
            SP(_v3StatLabel[i].transform, FcX, y, 100f, StatH);
            _v3StatValue[i] = NewText(s, "V3StatValue", FLead, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
            _v3StatValue[i].enableAutoSizing = true;
            _v3StatValue[i].fontSizeMax = FLead;
            SP(_v3StatValue[i].transform, FcX + 100f, y, 180f, StatH);
            _v3StatDelta[i] = NewText(s, "V3StatDelta", FTable, UIWeight.Medium, TextAlignmentOptions.MidlineRight, UpGreen);
            _v3StatDelta[i].enableAutoSizing = true;
            _v3StatDelta[i].fontSizeMax = FTable;
            SP(_v3StatDelta[i].transform, FcX + 280f, y, 80f, StatH);
        }
    }

    private void LayoutV3Ranged()
    {
        var s = _rangedStage;
        if (s == null) return;

        if (_dockTypeText != null)
        {
            SP(_dockTypeText.transform, LPad, 16f, 360f, 36f);
            V3Text(_dockTypeText, UIWeight.Medium, FTitle, UITheme.Thin.Ink);
            _dockTypeText.alignment = TextAlignmentOptions.MidlineLeft;
            _dockTypeText.text = "원거리 파츠";
        }
        if (_slotSummary != null)
        {
            SP(_slotSummary.transform, 388f, 16f, 344f, 36f);
            V3Text(_slotSummary, UIWeight.Regular, FTable, UITheme.Thin.InkA(0.6f));
            _slotSummary.alignment = TextAlignmentOptions.MidlineRight;
        }

        // 파츠 5행 — 704×52 한 줄(표시 · 이름 · Lv · 효과 · [강화 · 재료 N]). 납품 행 그림은 걷고 1px 테(StylePartRowV3)
        _partRowArt = false;
        for (int i = 0; i < PartRows; i++)
        {
            var row = _partsSlots[i];
            if (row == null) continue;
            row.sprite = null;
            row.type   = Image.Type.Simple;
            SP(row.transform, LPad, PrY0 + i * (PrH + PrGap), PrW, PrH);
            if (_partsFrames[i] != null) { _partsFrames[i].sprite = null; _partsFrames[i].color = Color.clear; }
            UIThinBox.Create(row.rectTransform, "V3RowEdge", Vector2.zero, Vector2.one, 0f, UITheme.Thin.LineIdle, Color.clear, Color.clear);

            RowP(_slotKind[i], 12f, 12f, 26f, 28f);
            V3Text(_slotKind[i], UIWeight.Regular, FTable, UITheme.Thin.Ink);
            RowP(_slotName[i], 42f, 0f, 196f, PrH);
            V3Text(_slotName[i], UIWeight.Medium, FLead, UITheme.Thin.Ink);
            if (_slotName[i] != null) { _slotName[i].alignment = TextAlignmentOptions.MidlineLeft; _slotName[i].overflowMode = TextOverflowModes.Ellipsis; _slotName[i].textWrappingMode = TextWrappingModes.NoWrap; }
            RowP(_slotLv[i], 242f, 0f, 64f, PrH);
            V3Text(_slotLv[i], UIWeight.Regular, FTable, UITheme.Thin.InkA(0.75f));
            if (_slotLv[i] != null) _slotLv[i].alignment = TextAlignmentOptions.MidlineLeft;
            RowP(_slotVal[i], 312f, 0f, 380f, PrH);   // 10-15 262 → 380 — 줄마다 있던 [강화 · 재료 N] 버튼을 걷어 「지금 → 다음」이 「…」 없이 든다
            V3Text(_slotVal[i], UIWeight.Regular, FMin, UITheme.Thin.InkA(0.7f));
            if (_slotVal[i] != null) { _slotVal[i].alignment = TextAlignmentOptions.MidlineLeft; _slotVal[i].textWrappingMode = TextWrappingModes.NoWrap; _slotVal[i].overflowMode = TextOverflowModes.Ellipsis; }
            RowP(row.transform.Find("LvBar"), 42f, 45f, 196f, 3f);   // 트랙(구운 경로에선 _slotBar가 트랙일 수 있어 이름으로 찾는다)
            if (_slotBtn[i] != null) _slotBtn[i].gameObject.SetActive(false);   // 10-15 — 강화 입구는 오른쪽 [강화하기] 하나(두 곳이면 어느 쪽인지 헷갈렸다)
            V3Text(_partsMark[i], UIWeight.Regular, FMin, UITheme.Thin.InkA(0.5f));
        }

        // 고른 파츠 판 — 왼쪽 열(이름 · 종류 · 효과 · 자람 · 짝) · 오른쪽 열(초점 「Lv 3 → 4」 72 · 단계 · 설명)
        var pd = _cardBg[1];
        if (pd == null) return;
        SP(pd.transform, LPad, PdY, PrW, PdH);
        pd.sprite = null;
        pd.color  = V3Detail;
        SetBoxLine(pd.transform.Find("ThinEdge") as RectTransform, UITheme.Thin.LineIdle);
        DP(_cardName[1], 20f, 14f, 332f, 34f);
        V3Text(_cardName[1], UIWeight.Medium, FTitle, UITheme.Thin.Ink);
        DP(_partKindText, 20f, 48f, 332f, 26f);
        V3Text(_partKindText, UIWeight.Regular, FTable, UITheme.Thin.InkA(0.6f));
        // 효과 줄은 두 줄까지(10-15 겹침 점검 — 「지금 2발 · 갈래 피해 50% → Lv4에 …」가 한 줄로 오른쪽 초점 밑까지 뻗었다)
        DP(_cardAtk[1], 20f, 80f, 332f, 54f);
        V3Text(_cardAtk[1], UIWeight.Medium, FLead, UITheme.Thin.Ink, autosize: true);
        DP(_partGrowthText, 20f, 136f, 332f, 52f);   // 두 줄(18 × 1.45 = 26 · 2줄 52)
        V3Text(_partGrowthText, UIWeight.Regular, FTable, UITheme.Thin.InkA(0.6f));
        DP(_partSynergyText, 20f, 190f, 332f, 54f);
        V3Text(_partSynergyText, UIWeight.Regular, FTable, UITheme.Thin.InkA(0.85f));
        DP(_cardLevel[1], 368f, 118f, 316f, 26f);
        V3Text(_cardLevel[1], UIWeight.Regular, FTable, UITheme.Thin.InkA(0.75f));
        if (_cardGaugeFill[1] != null) DP(_cardGaugeFill[1].parent, 368f, 148f, 316f, 4f);
        DP(_partDescText, 368f, 158f, 316f, 84f);
        V3Text(_partDescText, UIWeight.Regular, FTable, UITheme.Thin.Ink, autosize: true);
        foreach (var t in new[] { _cardName[1], _partKindText, _cardLevel[1] })
            if (t != null) { t.alignment = TextAlignmentOptions.MidlineLeft; t.textWrappingMode = TextWrappingModes.NoWrap; }
        foreach (var t in new[] { _cardAtk[1], _partGrowthText, _partSynergyText, _partDescText })
            if (t != null) { t.alignment = TextAlignmentOptions.TopLeft; t.textWrappingMode = TextWrappingModes.Normal; }
        _v3PartFocal = NewText(pd.transform, "V3PartFocal", FFocal, UIWeight.Light, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
        _v3PartFocal.enableAutoSizing = true;
        _v3PartFocal.fontSizeMax = FFocal;
        _v3PartFocal.fontSizeMin = 48f;
        DP(_v3PartFocal, 368f, 2f, 316f, 112f);
        _v3PartFocal.rectTransform.pivot = new Vector2(0f, 0.5f);
        Park(_partCostText);   // 비용 · 보유 → 오른쪽 판 재료 줄
    }

    /// <summary>단계 점 · 「다음」 한 줄 · 형 칸 한 줄 — 두 탭 공용 칸(판정 도장 아래 형제). 칸 안은 위 기준 고정 px라 칸을 옮겨도 눌리지 않는다.</summary>
    private void LayoutV3Path()
    {
        _v3Path = ShopUIStyle.MakeRect(_stageArea, "V3Path", typeof(CanvasGroup)).GetComponent<RectTransform>();
        if (_stageResult != null) _v3Path.SetSiblingIndex(_stageResult.transform.GetSiblingIndex());
        _v3PathGroup = _v3Path.GetComponent<CanvasGroup>();
        PlaceTL(_v3Path, 0f, PathMeleeY, V3LeftW, PathH);

        _pipDisc    = UIThinShapes.Disc(PipD);
        _pipRing    = UIThinShapes.Ring(PipD, 1.5f);
        _pipDia     = UIThinShapes.Fill(ThinShape.Diamond, PipDia, PipDia);
        _pipDiaLine = UIThinShapes.Outline(ThinShape.Diamond, PipDia, PipDia, 1.5f);

        _v3PathCap = NewText(_v3Path, "V3PathCap", FTable, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.6f));
        PlaceTL(_v3PathCap.rectTransform, LPad, 2f, 78f, 28f);
        _v3PipGlow = ShopUIStyle.MakeImage(_v3Path, "V3PipGlow", Color.clear);
        _v3PipGlow.sprite = UI_RuneSelectPopup.SoftDot;
        _v3PipGlow.enabled = false;
        for (int j = 0; j < MaxTicks; j++)
        {
            _v3Pip[j] = ShopUIStyle.MakeImage(_v3Path, "V3Pip", Color.white);
            _v3PipLabel[j] = NewText(_v3Path, "V3PipLabel", FMin, UIWeight.Regular, TextAlignmentOptions.Center, UITheme.Thin.InkA(0.5f));
        }
        _v3Next = NewText(_v3Path, "V3Next", FTable, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
        _v3Next.overflowMode = TextOverflowModes.Ellipsis;
        PlaceTL(_v3Next.rectTransform, LPad, 66f, V3LeftW - LPad * 2f, 30f);

        // 형 칸 한 줄 = 진화 버튼을 옮긴 것 — 누르면 4b 형 화면(진화 · 단련 · 승급)을 그대로 연다(ShowEvolvePanel · 배선 그대로)
        if (_evolveBtn == null) return;
        var row = (RectTransform)_evolveBtn.transform;
        row.SetParent(_v3Path, false);
        PlaceTL(row, LPad, 106f, V3LeftW - LPad * 2f, 52f);
        if (_evolveBtn.TryGetComponent<Image>(out var ri)) { ri.sprite = null; ri.color = Color.clear; }
        _v3RowGlow = ShopUIStyle.MakeImage(row, "V3RowGlow", Color.clear);
        _v3RowGlow.sprite = UI_RuneSelectPopup.SoftDot;
        _v3RowGlow.transform.SetAsFirstSibling();
        ShopUIStyle.Stretch(_v3RowGlow.rectTransform, -22f);
        if (_evolveLabel != null)
        {
            ShopUIStyle.Stretch(_evolveLabel.rectTransform);
            V3Text(_evolveLabel, UIWeight.Medium, FLead, FormInk, autosize: true);
            _evolveLabel.margin = new Vector4(18f, 0f, 18f, 0f);
            _evolveLabel.alignment = TextAlignmentOptions.MidlineLeft;
            _evolveLabel.textWrappingMode = TextWrappingModes.NoWrap;
            _evolveLabel.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    /// <summary>오른쪽 판 = 정보창을 옮긴다. 근거리 = 96 숫자 · 원인 칩 · 연속 게이지 · 재료 · 실패 · 버튼 / 원거리 = 96 숫자 · 표 5줄 · 재료 · 확정 · 버튼.</summary>
    private void LayoutV3Right(RectTransform info)
    {
        info.SetParent(_v3Root, false);
        V3Place(info, V3RightX, V3BoardY, V3RightW, V3BoardH);
        if (info.TryGetComponent<Image>(out var bg))
        {
            var edge = info.Find("ThinEdge");   // ThinPanel(10-11)의 1px 청동 — 판 그림이 자기 테를 가지면 걷는다
            if (edge != null) edge.gameObject.SetActive(BoardArt == null);
            V3Panel(bg, BoardArt, addEdge: edge == null);
        }
        Park(_infoTitle);   // 「강화 정보」 — 숫자 위 한 줄(성공 확률 · 발사 미리보기)이 대신한다
        Park(_rateBreak);   // 「기본 55% · ▲ …」 → 원인 칩 · 숫자 호버
        Park(_formBlock);   // 형 칸 묶음(4b) → 왼쪽 판 형 칸 한 줄

        if (_rateLabel != null)
        {
            RP(_rateLabel.transform, RPad, 26f, RInner, 28f);
            V3Text(_rateLabel, UIWeight.Regular, FTable, UITheme.Thin.InkA(0.6f));
            _rateLabel.alignment = TextAlignmentOptions.MidlineLeft;
        }
        if (_successText != null)
        {
            RP(_successText.transform, RPad, 56f, 270f, 152f);   // 96px Noto 줄 높이(≈1.45em)가 144를 4px 넘었다(10-14 재실측)
            V3Text(_successText, UIWeight.Light, FBig, UITheme.Thin.Ink);
            _successText.alignment = TextAlignmentOptions.MidlineLeft;
            _successText.textWrappingMode = TextWrappingModes.NoWrap;
            _successText.raycastTarget = true;   // 호버 = 확률 내역(로스트아크 「기본 + 추가」)
        }

        // 근거리 전용 — 원인 칩 · 연속 게이지
        _v3MeleeRight = ShopUIStyle.MakeRect(info, "V3MeleeRight").GetComponent<RectTransform>();
        ShopUIStyle.Stretch(_v3MeleeRight);
        for (int k = 0; k < _v3Chip.Length; k++) _v3ChipBox[k] = MakeV3Chip(_v3MeleeRight, "V3RateChip", out _v3Chip[k]);
        var d1 = ShopUIStyle.MakeImage(_v3MeleeRight, "V3Rule", UITheme.Thin.InkA(0.12f));
        RP(d1.transform, RPad, RuleY1, RInner, 1f);
        _v3StreakGroup = ShopUIStyle.MakeRect(_v3MeleeRight, "V3Streak").GetComponent<RectTransform>();
        ShopUIStyle.Stretch(_v3StreakGroup);
        _v3StreakHead = NewText(_v3StreakGroup, "Head", FTable, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
        RP(_v3StreakHead.transform, RPad, StreakY, 240f, 28f);
        _v3StreakVal = NewText(_v3StreakGroup, "Value", FTable, UIWeight.Regular, TextAlignmentOptions.MidlineRight, UITheme.Thin.Ink);
        RP(_v3StreakVal.transform, RPad + 240f, StreakY, RInner - 240f, 28f);
        var track = ShopUIStyle.MakeImage(_v3StreakGroup, "Track", UITheme.Thin.InkA(0.10f));
        RP(track.transform, RPad, StreakY + 38f, RInner, 6f);
        var fill = ShopUIStyle.MakeImage(track.transform, "Fill", UITheme.Thin.WithA(UITheme.Thin.Gold, 0.85f));
        _v3StreakFill = fill.rectTransform;
        _v3StreakFill.anchorMin = Vector2.zero;
        _v3StreakFill.anchorMax = new Vector2(0f, 1f);
        _v3StreakFill.offsetMin = _v3StreakFill.offsetMax = Vector2.zero;
        _v3StreakRule = NewText(_v3StreakGroup, "Rule", FMin, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.6f));
        RP(_v3StreakRule.transform, RPad, StreakY + 54f, RInner, 26f);
        var d2 = ShopUIStyle.MakeImage(_v3StreakGroup, "V3Rule", UITheme.Thin.InkA(0.12f));
        RP(d2.transform, RPad, RuleY2, RInner, 1f);

        // 재료 줄 · 실패 한 줄 — 두 탭 공용(자리는 PlaceRightForTab)
        _v3MatRow = ShopUIStyle.MakeRect(info, "V3MatRow").GetComponent<RectTransform>();
        var icon = ShopUIStyle.MakeImage(_v3MatRow, "Icon", MatTint);
        PlaceIn(icon.rectTransform, 0f, 8f, 28f, 28f, RInner, 44f);
        icon.preserveAspect = true;
        ShopUIStyle.Skin(icon, _skin?.MaterialIcon);
        if (_skin?.MaterialIcon == null) icon.enabled = false;
        var ml = NewText(_v3MatRow, "Label", FTable, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.6f));
        ml.text = "강화재료";
        PlaceIn(ml.rectTransform, 36f, 0f, 96f, 44f, RInner, 44f);
        _v3MatChipBox = MakeV3Chip(_v3MatRow, "V3MatChip", out _v3MatChip);
        _v3MatValue = NewText(_v3MatRow, "Value", FLead, UIWeight.Regular, TextAlignmentOptions.MidlineRight, UITheme.Thin.Ink);
        PlaceIn(_v3MatValue.rectTransform, 280f, 0f, RInner - 280f, 44f, RInner, 44f);
        _v3Fail = NewText(info, "V3Fail", FTable, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.75f));
        _v3Fail.enableAutoSizing = true;   // 최대 강화 때 마스터리 줄처럼 긴 글만 16까지 줄인다
        _v3Fail.overflowMode = TextOverflowModes.Ellipsis;

        // 원거리 표(발사 미리보기 5줄) — 값 칸은 RefreshPartInfo가 채운다
        _rowLineTop = info.Find("RowLineTop");
        if (_rowLineTop != null) RP(_rowLineTop, RPad, 196f, RInner, 1f);
        for (int i = 0; i < InfoRows; i++)
        {
            float y = 196f + i * 40f;
            if (info.Find($"RowLabel{i}")?.GetComponent<TMP_Text>() is TMP_Text lab)
            {
                RP(lab.transform, RPad, y, 160f, 40f);
                V3Text(lab, UIWeight.Regular, FTable, UITheme.Thin.InkA(0.6f));
                lab.alignment = TextAlignmentOptions.MidlineLeft;
            }
            var val = RowValue(i);
            if (val != null)
            {
                RP(val.transform, RPad + 160f, y, RInner - 160f, 40f);
                V3Text(val, UIWeight.Regular, FLead, UITheme.Thin.Ink, autosize: true);
                val.alignment = TextAlignmentOptions.MidlineRight;
                val.textWrappingMode = TextWrappingModes.NoWrap;
            }
            var line = info.Find($"RowLine{i}");
            if (line != null) RP(line, RPad, y + 39f, RInner, 1f);
        }
        if (_streakText != null)
        {
            RP(_streakText.transform, RPad, 400f, RInner, 28f);
            V3Text(_streakText, UIWeight.Regular, FMin, UITheme.Thin.InkA(0.6f));
            _streakText.alignment = TextAlignmentOptions.MidlineLeft;
        }
        if (_eventEffectText != null)
        {
            RP(_eventEffectText.transform, RPad, 400f, RInner, 28f);
            V3Text(_eventEffectText, UIWeight.Medium, FMin, UITheme.Thin.Gold);
            _eventEffectText.alignment = TextAlignmentOptions.MidlineRight;
        }
        if (_resultText != null)
        {
            RP(_resultText.transform, RPad, 540f, RInner, 30f);
            V3Text(_resultText, UIWeight.Medium, FTable, UITheme.Thin.Ink, autosize: true);
            _resultText.alignment = TextAlignmentOptions.Center;
            _resultText.textWrappingMode = TextWrappingModes.NoWrap;
        }

        // [강화하기 · 비용] 84 — 납품 버튼 그림(구운 「강화하기」 글자)은 걷고 금 1px 테(준비됨) + 금 글자. 채움 주 버튼은 이것 하나.
        if (_enhanceBtn != null)
        {
            var ert = (RectTransform)_enhanceBtn.transform;
            RP(ert, RPad, 596f, RInner, 84f);
            if (_enhanceBtn.TryGetComponent<Image>(out var eimg))
            {
                eimg.sprite = null;
                eimg.type   = Image.Type.Simple;
                eimg.color  = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.10f);
            }
            _v3EnhanceFrame = UIThinFrame.Create(ert, ThinShape.Square, new Vector2(RInner, 84f));
            _enhanceBtn.onClick.AddListener(_v3EnhanceFrame.PlayPress);
            if (_enhanceLabel != null)
            {
                _enhanceLabel.gameObject.SetActive(true);
                V3Text(_enhanceLabel, UIWeight.Medium, FTitle, UITheme.Thin.Gold);
                _enhanceLabel.textWrappingMode = TextWrappingModes.NoWrap;
            }
            if (_enhanceCost != null)
            {
                V3Text(_enhanceCost, UIWeight.Medium, FTitle, CostInk);
                _enhanceCost.alignment = TextAlignmentOptions.MidlineLeft;
                _enhanceCost.textWrappingMode = TextWrappingModes.NoWrap;
            }
        }

        // 버튼 아래 한 줄(10-18) — 진화 전 무형검 = 「◆ +10에서 다른 검으로 진화」 예고 ·
        // 진화할 수 있으면 = 진화를 미루고 무형검 그대로 강화하는 작은 길(누르는 글 · 지금 규칙상 상한까지)
        _v3EvolveSub = NewText(info, "V3EvolveSub", FMin, UIWeight.Regular, TextAlignmentOptions.Center, UITheme.Thin.InkA(0.85f));
        _v3EvolveSub.textWrappingMode = TextWrappingModes.NoWrap;
        _v3EvolveSub.overflowMode = TextOverflowModes.Ellipsis;
        RP(_v3EvolveSub.transform, RPad, 686f, RInner, 28f);
        _v3EvolveSubBtn = _v3EvolveSub.gameObject.AddComponent<Button>();
        _v3EvolveSubBtn.targetGraphic = _v3EvolveSub;
        var cb = _v3EvolveSubBtn.colors;   // 글자 링크 — 평소 옅게, 손을 올리면 또렷하게(예고 글은 누르지 않아도 같은 옅기)
        cb.normalColor = cb.selectedColor = cb.disabledColor = new Color(1f, 1f, 1f, 0.7f);
        cb.highlightedColor = Color.white;
        cb.pressedColor = new Color(1f, 1f, 1f, 0.85f);
        cb.fadeDuration = 0.08f;
        _v3EvolveSubBtn.colors = cb;
        _v3EvolveSubBtn.onClick.AddListener(OnEvolveSubClicked);
        _v3EvolveSub.gameObject.SetActive(false);
        PlaceRightForTab(false);
    }

    /// <summary>아래 60 — 왼쪽 = 대장장이 초상 68 + 말풍선(20px 두 줄 · 타자기 — UI_CruciblePanel.Speech.cs · 10-15), 오른쪽 = 단축키 안내.</summary>
    private void LayoutV3Bottom(RectTransform band)
    {
        if (band == null) return;
        band.SetParent(_v3Root, false);
        V3Place(band, V3LeftX, V3BarY, V3BarW, V3BarH);
        if (band.TryGetComponent<Image>(out var bi)) { bi.sprite = null; bi.color = Color.clear; }
        var edge = band.Find("ThinEdge");
        if (edge != null) edge.gameObject.SetActive(false);
        if (band.Find("Portrait") is RectTransform por) LayoutV3Portrait(por);
        Park(band.Find("Speaker"));
        if (_eventBanner != null) Park(_eventBanner.transform);   // 화로 칩 → 오른쪽 판 원인 칩(성공률 · 재료 · 잭팟 옆)

        // 말풍선 — 꼬리(마름모 반쪽)를 판 뒤에 깔아 초상 쪽으로 낸다. 꼬리 · 판이 한 묶음으로 옅어진다(_sayGroup)
        var say = ShopUIStyle.MakeRect(band, "V3Say").GetComponent<RectTransform>();
        ShopUIStyle.Stretch(say);
        _sayGroup = say.gameObject.AddComponent<CanvasGroup>();
        _sayGroup.blocksRaycasts = false;
        var tail = ShopUIStyle.MakeImage(say, "V3BubbleTail", V3Bubble);
        tail.sprite = UIThinShapes.Fill(ThinShape.Diamond, 14f, 14f);
        BP(tail.rectTransform, SayBubbleX - 7f, (V3BarH - 14f) * 0.5f, 14f, 14f);
        var tailLine = ShopUIStyle.MakeImage(say, "V3BubbleTailLine", UITheme.Thin.LineIdle);
        tailLine.sprite = UIThinShapes.Outline(ThinShape.Diamond, 14f, 14f);
        BP(tailLine.rectTransform, SayBubbleX - 7f, (V3BarH - 14f) * 0.5f, 14f, 14f);
        var bubble = ShopUIStyle.MakeImage(say, "V3Bubble", V3Bubble);
        BP(bubble.rectTransform, SayBubbleX, 0f, SayBubbleW, V3BarH);
        UIThinBox.Create(bubble.rectTransform, "Line", Vector2.zero, Vector2.one, 0f, UITheme.Thin.LineIdle, Color.clear, Color.clear);
        if (_dialogText != null)
        {
            _dialogText.transform.SetParent(bubble.transform, false);
            ShopUIStyle.Stretch(_dialogText.rectTransform);
            V3Text(_dialogText, UIWeight.Regular, FLead, UITheme.Thin.InkA(0.88f), autosize: true);   // 20 — 두 줄이면 16까지만 줄어든다
            _dialogText.margin = new Vector4(16f, 3f, 16f, 3f);
            _dialogText.alignment = TextAlignmentOptions.MidlineLeft;
            _dialogText.textWrappingMode = TextWrappingModes.Normal;
            _dialogText.overflowMode = TextOverflowModes.Ellipsis;
        }

        // 단축키 — 지금 있는 것만(Esc = 나가기 · 연출 중 다시 누름 = 건너뛰고 이어서 · QueueWhileAnimating)
        _v3Hint = NewText(band, "V3Hint", FMin, UIWeight.Regular, TextAlignmentOptions.MidlineRight, UITheme.Thin.InkA(0.55f));
        _v3Hint.text = $"<color=#ECE6D8>Esc</color>  나가기    ·    연출 중 다시 누르면 건너뛰고 한 번 더";
        BP(_v3Hint.rectTransform, 780f, 0f, V3BarW - 780f, V3BarH);
    }

    // ── 갱신(RefreshAll 맨 끝 — Form.cs가 「다음」 글을 고친 뒤) ──

    private void RefreshV3()
    {
        if (!_v3Ready || _controller == null) return;
        bool ranged = _activeTab == 1;
        if (_v3Tab != _activeTab)
        {
            _v3Tab = _activeTab;
            PlaceTL(_v3Path, 0f, ranged ? PathRangedY : PathMeleeY, V3LeftW, PathH);
            PlaceRightForTab(ranged);
        }
        if (_v3MeleeRight != null) _v3MeleeRight.gameObject.SetActive(true);   // 10-15 — 파츠도 확률 · 연속 · 잭팟(칩 · 연속 게이지 공용)
        // 옛 정보창 두 줄(「2번 키로 쏠 때만」 · 화로 이벤트 글) — 칩 · 연속 게이지 · 재료 칩이 대신한다
        if (_streakText != null) _streakText.gameObject.SetActive(false);
        if (_eventEffectText != null) _eventEffectText.gameObject.SetActive(false);
        if (ranged) RefreshV3Ranged();
        else        RefreshV3Melee();
    }

    /// <summary>근거리 — 지금 값으로 그린다.</summary>
    private void RefreshV3Melee()
    {
        int m = PlayerWeaponManager.Slot0;
        var w = _controller.GetSlot(m);
        SetRowCount(0);
        if (w == null)
        {
            _v3FocalCap.text = "";
            _v3Focal.text = "—";
            for (int i = 0; i < StatRows; i++) SetStat(i, null, null, null);
            foreach (var line in _v3StatLine) line.gameObject.SetActive(false);
            _rateLabel.text = "재련 비용";
            _successText.text = "—";
            HideChips(0);
            _v3StreakGroup.gameObject.SetActive(false);
            _v3MatRow.gameObject.SetActive(false);
            _v3Fail.text = "근접 무기가 없다";
            ButtonV3("재련", 0, 0);
            EvolveSubV3(null, false);
            RenderPathMelee(null, 0, 0);
            _v3Next.text = "";
            return;
        }
        int max  = _controller.MaxAt(m);
        bool can = _controller.CanReforge(false, out string why);
        RenderMeleeV3(w, w.enhanceLevel, max, can, why, _controller.ReforgeCost(false), _controller.FuelAmount);
        RenderPathMelee(w, w.enhanceLevel, max);
        _v3Next.text = NextLineV3();
    }

    /// <summary>
    /// 근거리 두 판 — 단계 L 기준. 판정 전(HoldBeforeState)에는 재련 전 값으로, 판정 순간(V3Reveal)엔 지금 값으로 부른다.
    /// 10-18 재련 개편 — 확률 · 연속 · 잭팟 · 하락이 없다: 오른쪽 숫자 = 재련 비용(원인 칩 = 이번 방문 n번째 · 단계 띠 · 반값) ·
    /// 연속 자리 = 가진 증강 목록 · 실패 줄 = 「실패 없음 · 증강 하나를 고른다」 · 버튼 [재련 · 비용].
    /// </summary>
    private void RenderMeleeV3(WeaponData w, int L, int max, bool can, string why, int cost, int have)
    {
        bool maxed = L >= max;
        int to = NextReforgeLevel(L, max);

        // 왼쪽 — 초점 · 바뀌는 수치(+3 한 번에)
        _v3FocalCap.text = maxed ? "재련 횟수" : "다음 재련";   // 10-19 — 「+N」(강화 단계)이 아니라 재련 횟수
        _v3Focal.text = maxed ? $"{Reforges(L)}회 <size=50%><color={HexMute}>최대</color></size>"
                              : $"{Reforges(L)}회 <color={HexMute}>→</color> <color={HexUp}>{Reforges(to)}회</color>";
        _v3Focal.color = UITheme.Thin.Ink;
        FillStatRows(w, L, max, to);

        // 10-18 — 무형검이 진화 단계에 닿았으면 오른쪽 판의 주인공은 진화다(큰 버튼 = [진화 — 무기 고르기])
        if (EvolveFirstAt(L)) { RenderEvolveFirstV3(w, L, maxed, to, cost, have); return; }

        // 오른쪽 — 비용 하나 · 원인 칩 · 가진 증강 · 재료 · 규칙 한 줄 · 버튼
        _rateLabel.text    = maxed ? "재련 횟수" : "재련 비용";
        _successText.text  = maxed ? "MAX" : cost.ToString();
        _successText.color = !maxed && have < cost ? DownRed : UITheme.Thin.Ink;
        HideChips(maxed ? 0 : FillCostChips(false));
        UITooltipTrigger.Attach(_successText.gameObject, "재련 비용", maxed ? "최대 재련 — 진화해야 더 오른다" : CostTip(false), UITheme.Thin.Gold);
        OwnedListV3(false, w);
        MaterialV3(cost, have, maxed);
        _v3Fail.text = maxed ? MaxedLine(w)
                     : !can && !string.IsNullOrEmpty(why) && have >= cost ? $"<color={HexDown}>{why}</color>"
                     : "실패 없음 · 공격 확정 상승 · 증강 하나를 고른다";
        if (_enhanceBtn != null) _enhanceBtn.interactable = !maxed && can;
        ButtonV3(maxed ? "최대 재련" : "재련", maxed ? 0 : cost, have);
        // 10-18 — 진화 전 무형검: 버튼 아래 예고 한 줄 · 10-22 형이 있으면 그 자리에 「◆ 형 단련」 입구(사용자 「단련은 누르기 힘든 레이아웃 — 확실한 버튼이나 설명」)
        string pre = PreEvolveHint(L);
        if (pre == null && TemperSubLine(out string temper, out bool afford)) { EvolveSubV3(temper, afford); _v3SubOpensForm = true; }
        else { EvolveSubV3(pre, false); _v3SubOpensForm = false; }
    }

    /// <summary>강화 단계 → 재련 횟수(10-19 · <see cref="WeaponReforgeService.CountAt"/>).</summary>
    private static int Reforges(int level) => WeaponReforgeService.CountAt(level);

    /// <summary>재련 한 번 뒤 단계 — 다음 3의 배수(옛 세이브가 3의 배수가 아니면 거기까지) · 상한까지.</summary>
    private static int NextReforgeLevel(int L, int max)
        => Mathf.Min((Mathf.Max(0, L) / WeaponReforgeService.MeleeStep + 1) * WeaponReforgeService.MeleeStep, max);

    /// <summary>재련 비용 원인 칩 — 「이번 방문 2번째」 · 「진화 뒤 ×3」 · 「▲ 반값 화로」. 쓴 칩 수.</summary>
    private int FillCostChips(bool ranged)
    {
        int nth = _controller.ReforgeCountThisVisit + 1;
        float band = ranged ? WeaponReforgeService.RangedBand((RangedPartsState.Current?.ReforgeCount ?? 0) + 1)
                            : WeaponReforgeService.MeleeBand(_controller.GetSlot(PlayerWeaponManager.Slot0));
        bool half = _controller.IsDiscountEvent;
        int count = 1 + (band > 1f ? 1 : 0) + (half ? 1 : 0);
        float y0 = ChipMidY - (count * 38f - 6f) * 0.5f;
        int k = 0;
        RateChip(k++, y0, $"이번 방문 {nth}번째", UITheme.Thin.InkA(0.7f));
        if (band > 1f) RateChip(k++, y0, $"▼ ×{band:0} {(ranged ? "후반" : "진화 뒤")}", DownRed);
        if (half)      RateChip(k++, y0, "▲ 반값 화로", UpGreen);
        return k;
    }

    /// <summary>비용 호버 — 규칙 전부(방문 안에서 오름 · 방을 나가면 처음 값 · 단계 띠 · 반값).</summary>
    private static string CostTip(bool ranged)
        => "이번 방문 1번째 5 · 2번째 10 · 3번째부터 15\n방을 나가면 처음 값으로 돌아간다 · 근접과 원거리가 같은 횟수를 나눠 쓴다\n"
         + (ranged ? "원거리 재련 5~6번째 ×3 · 7번째부터 ×6" : "진화 뒤(+12~) ×3 · +18부터 ×6")
         + "\n반값 화로 ×0.5";

    /// <summary>
    /// 연속 성공 자리(10-18부터 비어 있다) = 지금 무기에 붙은 것 — 근접 = 가진 증강(무형검 환영 몫 표시) · 원거리 = 켠 파츠.
    /// 10-19(사용자 「재련 수와 재련 때 골랐던 리스트」): 재련 기록이 있으면 <b>재련 n번째마다 고른 것</b>(근접 = 증강 · 그때 단계 · 원거리 = 파츠 · 오른 Lv) —
    /// 기록이 없는 옛 세이브 · 테스트 구성만 아래 옛 목록.
    /// 10-19 ⑰(사용자 「현재 보유 증강을 얻은 순차적으로 보유한 리스트로 보기 편하게」): 머리 「가진 증강 n」 아래 <b>한 줄에 하나</b> —
    /// 번호 · 이름 · 단계 | 효과 요약(넘치면 …) · 손을 올리면 전문. 순서 = 처음 얻은 순서(<see cref="WeaponData.AugmentList"/> — 단계가 올라도 제자리).
    /// </summary>
    private void OwnedListV3(bool ranged, WeaponData w)
    {
        _v3StreakGroup.gameObject.SetActive(true);
        if (_v3StreakFill != null && _v3StreakFill.parent != null) _v3StreakFill.parent.gameObject.SetActive(false);   // 연속 막대는 쓰지 않는다
        EnsureOwnedRows();
        s_ownedRows.Clear();
        string headName;
        bool history;
        if (ranged)
        {
            var state = RangedPartsState.Current;
            var all = Managers.WeaponParts?.All;
            history = state != null && state.Log.Count > 0 && Managers.WeaponParts != null;
            if (history)
                foreach (var e in state.Log)
                {
                    var def = Managers.WeaponParts.GetById(e.partId);
                    if (def == null) continue;
                    s_ownedRows.Add(($"{def.part_name} <color={HexMute}>Lv{e.level}</color>", PartValueText(def, def.ValueAt(e.level)), def.description ?? ""));
                }
            else if (state != null && all != null)
                foreach (var def in all)
                {
                    int lv = state.LevelOf(def.part_id);
                    if (lv <= 0) continue;
                    s_ownedRows.Add(($"{def.part_name} <color={HexMute}>Lv{lv}</color>", PartValueText(def, def.ValueAt(lv)), def.description ?? ""));
                }
            headName = history ? "재련 기록" : "켠 파츠";
            _v3StreakHead.text = $"<color={HexMute}>{headName}</color>  {s_ownedRows.Count}{(history ? "회" : "")}";
        }
        else
        {
            history = w != null && !string.IsNullOrEmpty(w.reforgeLog);
            if (w != null && (history || !string.IsNullOrEmpty(w.augments)))
                foreach (var (id, rank) in history ? w.ReforgeLogList() : w.AugmentList())
                {
                    var d = MeleeAugmentCatalog.Get(id);
                    if (d == null) continue;
                    string name = d.IsLegend || d.IsGrowth ? d.Name : $"{d.Name} {RomanRank(rank)}";   // 전설 · 자람(10-26 재련으로 자람)은 단계가 없다
                    if (MeleeAugmentCatalog.RunsAsPhantom(d, w)) name += " <color=#C9D3E6>(환영)</color>";
                    string eff = d.IsGrowth ? d.TextFor(w) : d.Text(rank);
                    s_ownedRows.Add((name, eff, eff));
                }
            headName = history ? "재련 기록" : "가진 증강";
            _v3StreakHead.text = $"<color={HexMute}>{headName}</color>  {s_ownedRows.Count}{(history ? "회" : "")}";
        }
        _v3StreakVal.text  = $"<color={HexMute}>{(history ? "재련마다 고른 것 · 이번 런" : "얻은 순서 · 이번 런")}</color>";

        int n = s_ownedRows.Count;
        _v3StreakRule.gameObject.SetActive(n == 0);
        if (n == 0)
        {
            _v3StreakRule.text = $"<color={HexMute}>아직 없다 — 재련하면 카드를 고른다</color>";
            _v3StreakRule.alignment = TextAlignmentOptions.TopLeft;
            RP(_v3StreakRule.transform, RPad, OwnedRowsY, RInner, OwnedRowH);
        }
        for (int i = 0; i < OwnedRowsMax; i++)
        {
            bool show = i < n;
            _ownedName[i].gameObject.SetActive(show);
            _ownedEffect[i].gameObject.SetActive(show);
            if (!show) continue;
            bool overflowRow = i == OwnedRowsMax - 1 && n > OwnedRowsMax;
            if (overflowRow)
            {
                // 넘치면 마지막 줄 = 「+N개 더」 — 손을 올리면 남은 것 전부
                var rest = new StringBuilder();
                for (int k = i; k < n; k++) rest.Append(k > i ? "\n" : "").Append(k + 1).Append(". ").Append(StripTags(s_ownedRows[k].name)).Append(" — ").Append(s_ownedRows[k].tip);
                _ownedName[i].text   = $"<color={HexMute}>+{n - i}개 더</color>";
                _ownedEffect[i].text = $"<color={HexMute}>손을 올리면 전부</color>";
                UITooltipTrigger.Attach(_ownedName[i].gameObject, headName, rest.ToString(), UITheme.Thin.Gold);
                continue;
            }
            var row = s_ownedRows[i];
            _ownedName[i].text   = history ? $"<color={HexMute}>재련 {i + 1}</color>  {row.name}" : $"<color={HexMute}>{i + 1}</color>  {row.name}";
            _ownedEffect[i].text = UIKeywordInk.Values(row.effect);
            UITooltipTrigger.Attach(_ownedName[i].gameObject, StripTags(row.name), row.tip, UITheme.Thin.Gold);
        }
    }

    private static readonly System.Collections.Generic.List<(string name, string effect, string tip)> s_ownedRows = new(12);

    /// <summary>목록 줄(이름 칸 · 효과 칸)을 처음 한 번 짓는다 — 이름 칸이 손 올림(전문 풀이)을 받는다.</summary>
    private void EnsureOwnedRows()
    {
        if (_ownedName[0] != null) return;
        for (int i = 0; i < OwnedRowsMax; i++)
        {
            float y = OwnedRowsY + i * OwnedRowH;
            var nm = NewText(_v3StreakGroup, $"Owned{i}", FMin, UIWeight.Medium, TextAlignmentOptions.MidlineLeft, UITheme.Thin.Ink);
            nm.textWrappingMode = TextWrappingModes.NoWrap;
            nm.overflowMode     = TextOverflowModes.Ellipsis;
            nm.raycastTarget    = true;   // 손 올림 = 효과 전문
            RP(nm.transform, RPad, y, OwnedNameW, OwnedRowH);
            var ef = NewText(_v3StreakGroup, $"OwnedEffect{i}", FMin, UIWeight.Regular, TextAlignmentOptions.MidlineLeft, UITheme.Thin.InkA(0.72f));
            ef.textWrappingMode = TextWrappingModes.NoWrap;
            ef.overflowMode     = TextOverflowModes.Ellipsis;
            RP(ef.transform, RPad + OwnedNameW + 12f, y, RInner - OwnedNameW - 12f, OwnedRowH);
            _ownedName[i] = nm;
            _ownedEffect[i] = ef;
        }
    }

    private static string RomanRank(int rank) => rank switch { 2 => "Ⅱ", 3 => "Ⅲ", _ => "Ⅰ" };

    /// <summary>풍선 머리용 — 색 태그를 걷은 글.</summary>
    private static string StripTags(string s) => Regex.Replace(s ?? "", "<[^>]+>", "");

    /// <summary>근접 칸 무기가 강화 단계 L에서 진화할 수 있나(갈래 하나라도 열림 · 무형검) — 이때 큰 버튼은 [진화 — 무기 고르기](10-18 · v3만).</summary>
    private bool EvolveFirstAt(int level)
    {
        if (!_v3Ready) return false;
        foreach (var b in CurrentBranches) if (WeaponEvolutionSO.IsUnlocked(b, level)) return true;
        return false;
    }

    /// <summary>단계 L에서 열린 갈래 중 가장 싼 진화 비용(강화재료) — 지금 데이터는 모두 0(무료).</summary>
    private int EvolveCostAt(int level)
    {
        int cost = int.MaxValue;
        foreach (var b in CurrentBranches) if (WeaponEvolutionSO.IsUnlocked(b, level)) cost = Mathf.Min(cost, Mathf.Max(0, b.cost));
        return cost == int.MaxValue ? 0 : cost;
    }

    /// <summary>
    /// 10-18 사용자 「무형검에서 다른 검으로 형 진화를 자연스럽게 — 강화하다 강화 버튼에서 선택하는 걸로 가야 해. 지금은 계속 무형검을 강화하게 되는 UX」.
    /// 무형검이 진화 단계(+10)에 닿으면 오른쪽 판이 진화를 말한다 — 숫자 = 진화 비용(무료) · 실패 줄 = 실패 없음 · 이어지는 것 ·
    /// 큰 버튼 = [진화 — 무기 고르기](금 테 · 후광 · 누르면 갈래 카드 → 확정 → 진화 연출 → 새 검의 [강화하기]).
    /// 지금 규칙은 진화를 미뤄도 막지 않는다(무형검 상한 +12) — 무형검 그대로 강화는 버튼 아래 작은 글로만 남긴다.
    /// </summary>
    private void RenderEvolveFirstV3(WeaponData w, int L, bool maxed, int to, int need, int have)
    {
        int cost = EvolveCostAt(L);
        _rateLabel.text    = cost > 0 ? "진화 비용 · 강화재료" : "진화 비용";
        _successText.text  = cost > 0 ? cost.ToString() : "무료";
        _successText.color = cost > 0 && have < cost ? DownRed : UITheme.Thin.Ink;
        HideChips(0);
        UITooltipTrigger.Attach(_successText.gameObject, "진화", "카드에서 고른 검으로 바뀐다\n실패 없음 · 되돌릴 수 없다", UITheme.Thin.Gold);
        OwnedListV3(false, w);
        MaterialV3(cost, have, hidden: cost <= 0);
        _v3Fail.text = $"실패 없음 · 재련 {Reforges(L)}회와 증강은 그대로 이어진다";
        if (_enhanceBtn != null) _enhanceBtn.interactable = true;   // 재료가 모자란 갈래는 고르기 창의 확정 버튼이 막는다(PickEvolveCard)
        ButtonV3("진화 — 무기 고르기", 0, have);
        Halo(_enhanceBtn, !Choosing);   // 후광은 한 화면 한 곳 — 형 칸 한 줄은 이때 켜지 않는다(RefreshFormRowV3)

        if (maxed) { EvolveSubV3($"{w.displayName} 최대 재련 {Reforges(L)}회 — 진화해야 더 오른다", false); return; }
        string c = have >= need ? $"강화재료 {need}" : $"강화재료 <color={HexDown}>{need}</color>";
        EvolveSubV3($"진화 미루고 {w.displayName} 재련 {Reforges(to)}회째 · {c}  <color={HexMute}>▶</color>", have >= need);
    }

    /// <summary>진화 전 무기의 버튼 아래 예고 — 「◆ +10에서 다른 검으로 진화 — 빠른 검 또는 묵직한 검」. 갈래가 없거나 이미 열렸으면 null.</summary>
    private string PreEvolveHint(int L)
    {
        int at = EvolveAt();
        if (at <= 0 || L >= at) return null;
        var sb = new StringBuilder();
        foreach (var b in CurrentBranches)
        {
            if (b == null || !b.IsValid) continue;
            if (sb.Length > 0) sb.Append(" 또는 ");
            sb.Append(Persona(b.target).tag);
        }
        return $"<color={HexForm}>◆</color>  재련 {Reforges(at)}회에서 다른 검으로 진화 — {sb}";
    }

    /// <summary>버튼 아래 한 줄 — 글이 비면 감춘다. clickable이면 누르는 글(진화 미루고 무형검 강화 · <see cref="OnKeepEnhanceClicked"/>).</summary>
    /// <summary>버튼 아래 줄을 눌렀을 때 — 형 단련 입구면 형 화면(<see cref="ShowEvolvePanel"/>), 아니면 진화 미루고 재련(10-22).</summary>
    private void OnEvolveSubClicked()
    {
        if (_v3SubOpensForm) ShowEvolvePanel();
        else OnKeepEnhanceClicked();
    }

    /// <summary>형 단련을 할 수 있는 무기면 버튼 아래 입구 글 — 「◆ 형 단련 Ⅱ로 · 강화재료 12 — 누르면 변주 둘 중 하나를 고른다 ▶」.</summary>
    private bool TemperSubLine(out string text, out bool afford)
    {
        text = null; afford = false;
        int m = PlayerWeaponManager.Slot0;
        var w = _controller.GetSlot(m);
        if (w == null || CurrentFormUi(w) != FormUi.Temper || !_controller.CanTemperForm(m, out int tier, out int cost)) return false;
        int have = _controller.FuelAmount;
        afford = have >= cost;
        text = afford
            ? $"<color=#FFCC4D>◆ 형 단련 {RomanTier(tier)}로</color> · 강화재료 {cost} — 누르면 변주 둘 중 하나를 고른다  ▶"
            : $"◆ 형 단련 {RomanTier(tier)} — 강화재료 <color={HexDown}>{cost}</color> 필요(지금 {have})";
        return true;
    }

    private void EvolveSubV3(string text, bool clickable)
    {
        if (_v3EvolveSub == null) return;
        bool on = !string.IsNullOrEmpty(text);
        _v3EvolveSub.gameObject.SetActive(on);
        if (!on) return;
        _v3EvolveSub.text = text;
        _v3EvolveSub.raycastTarget = clickable;
        _v3EvolveSubBtn.interactable = clickable;
    }

    /// <summary>바뀌는 수치 2~3줄 — 공격은 늘, 스킬 단계 · 형 · 마스터리는 이번 한 번이 그 문턱을 넘을 때만. 강화가 바꾸는 수치는 실제로 이것뿐이다.</summary>
    private void FillStatRows(WeaponData w, int L, int max, int to)
    {
        int n = 0;
        var t = _controller.Table;
        float a0 = WeaponEnhanceService.EffectiveAttackAt(w, t, L);
        if (L >= max) SetStat(n++, "공격", $"{a0:F1}", $"<color={HexMute}>최대</color>");
        else
        {
            float a1 = WeaponEnhanceService.EffectiveAttackAt(w, t, to);
            SetStat(n++, "공격", Arrow($"{a0:F1}", $"{a1:F1}"), $"▲{a1 - a0:F1}");

            int evo = EvolveAt();
            if (evo > 0 && L < evo && to >= evo && CurrentBranches.Count > 0)
                SetStat(n++, "형", $"<color={HexMute}>잠김 →</color> <color={HexForm}>깨어남</color>", $"<color={HexForm}>◆</color>");

            var ms = SkillTierResolver.Milestones(w);
            int t0 = TierAt(ms, L), t1 = TierAt(ms, to);
            if (t1 > t0 && n < StatRows) SetStat(n++, "스킬 단계", Arrow($"{t0}", $"{t1}단계"), "▲1");
        }

        // 마스터리(진화 뒤 기본 상한 위) — 스킬 피해 · 재사용
        if (n < StatRows && t != null && w.evolutionStage > 0)
        {
            int m0 = t.MasteryLevel(w.rarity, w.evolutionStage, L);
            int m1 = L >= max ? m0 : t.MasteryLevel(w.rarity, w.evolutionStage, to);
            if (m1 > 0)
            {
                float d0 = t.MasterySkillDamage(m0) * 100f, d1 = t.MasterySkillDamage(m1) * 100f;
                SetStat(n++, "스킬 피해", m1 > m0 ? Arrow($"+{d0:0}%", $"+{d1:0}%") : $"+{d0:0}%", m1 > m0 ? $"▲{d1 - d0:0}%p" : "");
                var player = _controller.Run?.Player;
                var slot   = w.skillE != null ? SkillType.E : SkillType.R;
                float s0 = SkillCooldownPreview.Seconds(player, w, slot, m0), s1 = SkillCooldownPreview.Seconds(player, w, slot, m1);
                if (n < StatRows && s0 > 0f)
                    SetStat(n++, slot == SkillType.E ? "E 재사용" : "R 재사용", m1 > m0 ? Arrow($"{s0:0.0#}", $"{s1:0.0#}초") : $"{s0:0.0#}초",
                            m1 > m0 ? $"−{s0 - s1:0.0#}초" : "");
            }
        }
        for (int i = n; i < StatRows; i++) SetStat(i, null, null, null);
        for (int i = 0; i <= StatRows; i++) _v3StatLine[i].gameObject.SetActive(i <= n && n > 0);
    }

    private void SetStat(int i, string label, string value, string delta)
    {
        bool on = label != null;
        _v3StatLabel[i].gameObject.SetActive(on);
        _v3StatValue[i].gameObject.SetActive(on);
        _v3StatDelta[i].gameObject.SetActive(on);
        if (!on) return;
        _v3StatLabel[i].text = label;
        _v3StatValue[i].text = value;
        _v3StatDelta[i].text = delta ?? "";
    }

    private static string Arrow(string now, string next) => $"{now} <color={HexMute}>→</color> <color={HexUp}>{next}</color>";

    private static int TierAt(int[] ms, int level)
    {
        int n = 0;
        for (int i = 0; i < ms.Length; i++) if (ms[i] <= level) n++;
        return Mathf.Clamp(SkillTierResolver.MinTier + n, SkillTierResolver.MinTier, SkillTierResolver.MaxTier);
    }

    /// <summary>최대 강화일 때 실패 줄 자리 — 다음 길(진화 · 마스터리).</summary>
    private string MaxedLine(WeaponData w)
    {
        if (w.CanEvolve) return $"<color={HexForm}>진화하면 다음 구간이 열린다</color>";
        string mastery = MasteryText(w);
        return string.IsNullOrEmpty(mastery) ? "최대 재련에 닿았다" : mastery;
    }

    /// <summary>숫자 오른쪽에 위에서부터 한 줄씩(높이 32 · 간격 6).</summary>
    private void RateChip(int k, float y0, string label, Color color)
    {
        float w = SetV3Chip(_v3ChipBox[k], _v3Chip[k], label, color);
        RP(_v3ChipBox[k], 318f, y0 + k * 38f, w, 32f);
    }

    private void HideChips(int from)
    {
        for (int i = from; i < _v3ChipBox.Length; i++) if (_v3ChipBox[i] != null) _v3ChipBox[i].gameObject.SetActive(false);
    }

    /// <summary>재료 줄 — 보유/필요(모자라면 보유만 빨강 · 리서치 B-2-2) · 반값 화로면 칩.</summary>
    private void MaterialV3(int need, int have, bool hidden)
    {
        _v3MatRow.gameObject.SetActive(!hidden);
        if (hidden) return;
        string h = have < need ? $"<color={HexDown}>{have}</color>" : have.ToString();
        _v3MatValue.text = $"<color={HexMute}>보유</color> {h}  <color={HexMute}>/  필요</color> {need}";
        if (_controller.IsDiscountEvent)
        {
            float w = SetV3Chip(_v3MatChipBox, _v3MatChip, "▲ 반값 화로", UpGreen);
            PlaceIn(_v3MatChipBox, 132f, 6f, w, 32f, RInner, 44f);
        }
        else _v3MatChipBox.gameObject.SetActive(false);
    }

    /// <summary>[강화하기 · 비용] — 비용이 있으면 글자는 왼쪽 56%에 오른쪽 맞춤 · 비용(아이콘 + 수)은 그 오른쪽, 없으면 가운데 한 줄.</summary>
    private void ButtonV3(string label, int cost, int have)
    {
        if (_enhanceBtn == null) return;
        bool withCost = cost > 0;
        const float h = 84f;
        if (_enhanceLabel != null)
        {
            _enhanceLabel.text = withCost ? $"{label}  <color={HexMute}>·</color>" : label;
            if (withCost)
            {
                PlaceIn(_enhanceLabel.rectTransform, 0f, 0f, RInner * 0.56f, h, RInner, h);
                _enhanceLabel.alignment = TextAlignmentOptions.MidlineRight;
            }
            else
            {
                ShopUIStyle.Stretch(_enhanceLabel.rectTransform);
                _enhanceLabel.alignment = TextAlignmentOptions.Center;
            }
        }
        SetEnhanceCost(cost, have >= cost);
        if (withCost && _enhanceCost != null)
        {
            PlaceIn(_enhanceCost.rectTransform, RInner * 0.56f + 48f, 0f, 150f, h, RInner, h);
            if (_enhanceCost.transform.Find("CostIcon") is RectTransform icon) PlaceIn(icon, -36f, (h - 28f) * 0.5f, 28f, 28f, 150f, h);
        }
        _v3EnhanceFrame?.SetState(_enhanceBtn.interactable ? ThinFrameState.Ready : ThinFrameState.Idle, animate: true);
    }

    /// <summary>근거리 단계 점 — +1 … +상한(15개까지). 지난 칸 = 채운 점 · 다음 칸 = 금 테 + 숨쉬는 빛 · 진화(◆) · 스킬 단계(◇) = 마름모(형 글자 보라).</summary>
    private void RenderPathMelee(WeaponData w, int L, int max)
    {
        _v3PathCap.text = "재련";
        _v3PipGlow.enabled = false;
        int step = WeaponReforgeService.MeleeStep;   // 10-18 — 재련 한 번 = 한 칸(+3)
        int n = w == null || max <= 0 ? 0 : Mathf.Min((max + step - 1) / step, MaxTicks);
        int evoAt = EvolveAt();
        var tiers = w != null ? SkillTierResolver.Milestones(w) : System.Array.Empty<int>();
        float span = n > 0 ? (PipX1 - PipX0) / n : 0f;
        for (int j = 0; j < MaxTicks; j++)
        {
            bool on = j < n;
            _v3Pip[j].gameObject.SetActive(on);
            _v3PipLabel[j].gameObject.SetActive(on);
            if (!on) continue;
            int lv = Mathf.Min((j + 1) * step, max);
            bool evo = evoAt > lv - step && evoAt <= lv;                      // 이 칸(재련 한 번) 안에서 진화 단계를 넘는다
            int skill = -1;
            for (int s = 0; s < tiers.Length; s++) if (tiers[s] > lv - step && tiers[s] <= lv) { skill = s; break; }
            SetPip(j, PipX0 + span * (j + 0.5f), span, lv <= L, lv > L && lv - step <= L, evo || skill >= 0, false, $"{j + 1}");   // 10-19 — 칸 = 재련 n번째
            TickTip(_v3PipLabel[j], w, lv, evo, skill);
        }
    }

    /// <summary>원거리 단계 점 — 파츠 레벨 합 1 … 마지막 스킬 단계 문턱(◇ = 스킬 단계).</summary>
    private void RenderPathRanged()
    {
        _v3PathCap.text = "파츠 합";
        _v3PipGlow.enabled = false;
        var rw = _controller.GetSlot(PlayerWeaponManager.Slot1);
        bool ok = rw != null && SkillTierResolver.IsRanged(rw);
        var ms = ok ? SkillTierResolver.Milestones(rw) : System.Array.Empty<int>();
        int n = !ok ? 0 : Mathf.Clamp(ms.Length > 0 ? ms[ms.Length - 1] : 10, 1, MaxTicks);
        int total = RangedPartsState.Current?.TotalLevel ?? 0;
        float span = n > 0 ? (PipX1 - PipX0) / n : 0f;
        for (int j = 0; j < MaxTicks; j++)
        {
            bool on = j < n;
            _v3Pip[j].gameObject.SetActive(on);
            _v3PipLabel[j].gameObject.SetActive(on);
            if (!on) continue;
            int lv = j + 1;
            int skill = System.Array.IndexOf(ms, lv);
            SetPip(j, PipX0 + span * (j + 0.5f), span, lv <= total, lv == total + 1, skill >= 0, false, lv.ToString());
            TickTip(_v3PipLabel[j], rw, lv, false, skill);
        }
    }

    private void SetPip(int j, float cx, float span, bool reached, bool next, bool mark, bool danger, string label)
    {
        var pip = _v3Pip[j];
        float d = mark ? PipDia : PipD;
        pip.sprite = mark ? (reached ? _pipDia : _pipDiaLine) : (reached ? _pipDisc : _pipRing);
        pip.color  = reached ? (mark ? FormInk : UITheme.Thin.InkA(0.85f))
                   : next    ? UITheme.Thin.Gold
                   : mark    ? UITheme.Thin.WithA(FormInk, 0.7f) : UITheme.Thin.InkA(0.35f);
        PlaceTL(pip.rectTransform, cx - d * 0.5f, PipCy - d * 0.5f, d, d);

        var t = _v3PipLabel[j];
        t.text  = label;
        t.color = reached ? UITheme.Thin.InkA(0.85f) : mark ? FormInk : danger ? DownRed : next ? UITheme.Thin.Ink : UITheme.Thin.InkA(0.5f);
        float lw = Mathf.Min(span, 64f);
        PlaceTL(t.rectTransform, cx - lw * 0.5f, PipLabelY, lw, 24f);

        if (!next) return;
        _v3PipGlow.enabled = true;
        PlaceTL(_v3PipGlow.rectTransform, cx - 17f, PipCy - 17f, 34f, 34f);
    }

    /// <summary>「다음」 한 줄 — 성장 길(Form.cs)이 쓴 「다음」 글을 그대로(크기 태그만 걷는다 · 본문 최소 16).</summary>
    private string NextLineV3()
    {
        string s = _next1 != null ? SizeTag.Replace(_next1.text ?? string.Empty, string.Empty) : string.Empty;
        return string.IsNullOrEmpty(s) ? string.Empty : $"<color={HexMute}>다음</color>   {s}";
    }

    /// <summary>
    /// 원거리 — 근거리와 같은 뼈대(10-15 사용자 「원거리는 좀 더 보기 편한 가독성과 레이아웃」):
    /// 오른쪽 = 재련 비용 96 · 원인 칩(이번 방문 n번째 · 후반 띠 · 반값) · 켠 파츠 · 재료 · 규칙 한 줄 · [재련 · 비용](10-18 재련 개편).
    /// 지금 조합의 총합(몇 발 · 관통 · 폭발 · 유도)은 왼쪽 목록 머리 한 줄로 옮겼다 — 예전 오른쪽 표 5줄은 파츠가 없으면 「없음」만 다섯 줄이었다.
    /// </summary>
    private void RefreshV3Ranged()
    {
        SetRowCount(0);
        EvolveSubV3(null, false);   // 버튼 아래 진화 한 줄(10-18)은 근거리 무기 몫
        RenderPathRanged();
        _v3Next.text = NextLineV3();
        _successText.color = UITheme.Thin.Ink;
        if (_slotSummary != null) _slotSummary.text = RangedComboLine();

        int have  = _controller.FuelAmount;
        // 10-18 재련 개편 — 고를 파츠를 미리 정하지 않는다: 재련하면 카드 3장(새 파츠 = Lv1 · 가진 파츠 = 다음 눈금) 중 하나
        bool can  = _controller.CanReforge(true, out string why);
        int  cost = _controller.ReforgeCost(true);
        _rateLabel.text    = "재련 비용";
        _successText.text  = cost.ToString();
        _successText.color = have < cost ? DownRed : UITheme.Thin.Ink;
        HideChips(FillCostChips(true));
        UITooltipTrigger.Attach(_successText.gameObject, "재련 비용", CostTip(true), UITheme.Thin.Gold);
        OwnedListV3(true, null);
        MaterialV3(cost, have, hidden: false);
        _v3Fail.text = can ? $"실패 없음 · 파츠 카드 {RangedPartsDraft.CardCount}장 중 하나를 고른다"
                     : have >= cost && !string.IsNullOrEmpty(why) ? $"<color={HexDown}>{why}</color>" : "재료가 모자란다";
        if (_enhanceBtn != null) _enhanceBtn.interactable = can;
        ButtonV3("재련", cost, have);
        Halo(_enhanceBtn, can && !Choosing);   // 후광은 한 화면 한 곳
        RefreshPartFocalV3();
    }

    /// <summary>지금 켠 파츠를 모두 합친 발사 — 실제 발사 퍼널(RangedParts.Apply)을 그대로 돌린다(표시용 수식을 따로 두지 않는다).</summary>
    private static string RangedComboLine()
    {
        var req = ProjectileRequest.Create("preview", Vector3.zero, Vector3.forward, 100f, null, RangedParts.RangedSlot);
        RangedParts.Apply(ref req);
        var sb = new StringBuilder($"<color={HexMute}>지금 조합</color>  {Mathf.Max(1, req.count)}발");
        if (req.pierce > 0) sb.Append(" · 관통 ").Append(req.pierce);
        if (req.explodeRadius > 0f) sb.Append(" · 폭발 ").Append(req.explodeRadius.ToString("0.#")).Append("m");
        if (req.homingStrength > 0f) sb.Append(" · 유도");
        if (!Mathf.Approximately(req.damageMult, 1f)) sb.Append(" · 피해 ×").Append(req.damageMult.ToString("0.00"));
        return sb.ToString();
    }

    /// <summary>파츠 판정 한 박자 — 타격음 · 초점이 한 번 부풀었다 가라앉는다(망치는 근거리 무대에만 있어 소리와 초점이 충격을 말한다).</summary>
    private async UniTask PartStrikeAsync()
    {
        ShopUIStyle.PlaySfx("crucible_hit");
        await PunchFocalAsync(_v3PartFocal, UITheme.Thin.Gold, PartStrikeDur, 0.06f);
        await Hold(PartStrikeHold);
    }

    /// <summary>
    /// 손을 올린 파츠의 초점 「Lv 4 → 7」(카드로 나오면 오르는 다음 눈금 — 10-19 ⑰: 예전 「Lv 4 → 5」는 재련과 맞지 않았다) — RefreshRangedCard가 부른다.
    /// 손을 올리지 않았으면 「카드 3장」(재련 = 무작위 파츠 카드에서 고른다).
    /// </summary>
    private void RefreshPartFocalV3()
    {
        if (!_v3Ready || _v3PartFocal == null) return;
        var def = HoveredPartDef();
        if (def == null) { _v3PartFocal.text = $"카드 {RangedPartsDraft.CardCount}장"; return; }
        int lv  = RangedPartsState.Current?.LevelOf(def.part_id) ?? 0;
        int cap = RangedPartsDraft.CapOf(def);
        _v3PartFocal.text = lv >= cap ? $"Lv {lv} <size=50%><color={HexMute}>최대</color></size>"
                                      : $"Lv {lv} <color={HexMute}>→</color> <color={HexUp}>{RangedPartsDraft.NextLevel(lv, cap)}</color>";
    }

    /// <summary>원거리 파츠 행(v3) — 고른 행 = 금 테 + 옅은 금 바탕 · 켠 행 = 잉크 테 · 꺼진 행 = 더 옅게.</summary>
    private void StylePartRowV3(int i, bool selected, bool on)
    {
        var row = _partsSlots[i];
        row.color = selected ? UITheme.Thin.WithA(UITheme.Thin.Gold, 0.08f) : UITheme.Thin.InkA(on ? 0.04f : 0.015f);
        if (_partsFrames[i] != null) _partsFrames[i].color = Color.clear;
        SetBoxLine(row.transform.Find("V3RowEdge") as RectTransform,
                   selected ? UITheme.Thin.LineReady : on ? UITheme.Thin.LineIdle : UITheme.Thin.InkA(0.10f));
    }

    /// <summary>
    /// 형 칸 한 줄 — 「형  칼바람 Ⅰ — 단련 Ⅱ · 강화재료 12  ▶」(설계의 ▸는 글꼴에 없어 □가 된다 — ▶). 누르면 4b 형 화면을 <see cref="ShowEvolvePanel"/>로 연다
    /// (무형검 = 진화 고르기(잠겼으면 미리 보기) · 형 = 단련 고르기 · 마지막 형태 = 전설 승급). 형 완성은 요약만(누르지 않는다).
    /// </summary>
    private void RefreshFormRowV3()
    {
        if (_evolveBtn == null) return;
        int m = PlayerWeaponManager.Slot0;
        var w = _controller.GetSlot(m);
        var st = CurrentFormUi(w);
        bool promote = (st == FormUi.None || st == FormUi.Complete) && IsPromoteRoute(w) && _controller.CanPromote(m);
        bool show = _activeTab == 0 && w != null && (st != FormUi.None || promote);
        _evolveBtn.gameObject.SetActive(show);
        if (!show) { Halo(_evolveBtn, false); return; }

        bool enhanceFirst = _controller.CanReforge(false, out _);   // [재련]을 지금 누를 수 있나(10-18 재련 개편 — 무기 · 상한 · 재료 · 카드 대기)
        var (fa, fb) = BranchForms();
        string pair = $"{fa?.DisplayName ?? "?"} 또는 {fb?.DisplayName ?? "?"}";
        string body;
        bool click, act, afford = true;
        if (promote)
        {
            body   = "<color=#FFCC4D>◆ 승급</color> — 전설을 고른다";
            click  = act = true;
            afford = CanAffordNextForm(true);
        }
        else switch (st)
        {
            case FormUi.Locked:
                body  = $"<color={HexMute}>잠김</color> · 재련 {Reforges(EvolveAt())}회에서 {pair}{Iga(fb?.DisplayName)} 깨어난다";   // 10-19 — 「+N」 대신 재련 횟수
                click = true;    // 미리 보기 — 진화 카드는 잠긴 채로 열린다(FillEvolveCard)
                act   = false;
                break;
            case FormUi.Ready:
                body   = $"<color=#FFCC4D>깨어날 수 있다</color> — {pair}";
                click  = act = true;
                afford = CanAffordNextForm(false);
                break;
            case FormUi.Temper:
            {
                _controller.CanTemperForm(m, out int tier, out int cost);
                afford = _controller.FuelAmount >= cost;
                string c = afford ? $"강화재료 {cost}" : $"<color={HexDown}>강화재료 {cost}</color> 필요(지금 {_controller.FuelAmount})";   // 10-22 모자라면 이유까지
                body  = $"{w.form.DisplayName} {RomanTier(w.formTier)} — 단련 {RomanTier(tier)} · {c}";
                click = afford;   // 단련은 재료가 모자라면 꺼진다(v2 · 시안 ③ 규칙 그대로)
                act   = true;
                break;
            }
            default:
                body  = $"{w.form.DisplayName} {RomanTier(w.formTier)} · {VariantName(w, 2) ?? "—"} · {VariantName(w, 3) ?? "—"}";
                click = act = false;
                break;
        }
        if (_evolveLabel != null)
        {
            _evolveLabel.text  = $"<color={HexMute}>형</color>   {body}" + (click ? $"   <color={HexMute}>▶</color>" : "");
            _evolveLabel.color = st == FormUi.Locked && !promote ? UITheme.Thin.InkA(0.85f) : FormInk;
        }
        _evolveBtn.interactable = click;
        // 10-18 — 진화할 수 있으면 오른쪽 큰 버튼이 [진화 — 무기 고르기]다(금 테 · 후광은 그쪽). 형 칸은 같은 창을 여는 둘째 입구로만
        bool lit = act && afford && click && !(st == FormUi.Ready && !promote && EvolveFirstAt(w.enhanceLevel));
        SetBoxLine(_evolveEdge, !click ? UITheme.Thin.LineIdle : lit && !enhanceFirst ? UITheme.Thin.LineReady : UITheme.Thin.LineActive);
        Halo(_evolveBtn, lit && !enhanceFirst && !Choosing);   // 후광은 한 화면 한 곳 — [강화하기]를 누를 수 있으면 그쪽
    }

    /// <summary>재료 줄 · 실패 한 줄 자리 — 근거리는 연속 게이지 아래, 원거리는 표 아래.</summary>
    private void PlaceRightForTab(bool ranged)
    {
        // 10-15 — 원거리도 근거리와 같은 자리(표 5줄을 걷었다 · 파츠 확률 · 연속 게이지가 그 자리)
        if (_v3MatRow != null) RP(_v3MatRow, RPad, MatY, RInner, 44f);
        if (_v3Fail != null) RP(_v3Fail.transform, RPad, MatY + 56f, RInner, 30f);
    }

    // ── 결과 연출(v3) ──

    /// <summary>강화 누르기 직전 — 판정 전 화면에 붙잡아 둘 값(「다음」 · 형 칸).</summary>
    private void V3SnapshotBefore()
    {
        if (!_v3Ready || _controller == null) return;
        _v3NextBefore    = _v3Next != null ? _v3Next.text : null;
        _v3FormBeforeOn  = _evolveBtn != null && _evolveBtn.gameObject.activeSelf;
        _v3FormBeforeText = _evolveLabel != null ? _evolveLabel.text : null;
    }

    /// <summary>망치가 닿기 전 — v3 두 판을 강화 전 값으로(결과가 새지 않게).</summary>
    private void V3HoldBefore(EnhanceResult r, int max)
    {
        var w = _controller.GetSlot(PlayerWeaponManager.Slot0);
        if (w == null) return;
        RenderMeleeV3(w, r.beforeLevel, max, true, null, r.spent, _controller.FuelAmount);
        RenderPathMelee(w, r.beforeLevel, max);
        if (_v3NextBefore != null) _v3Next.text = _v3NextBefore;
        if (_evolveBtn != null)
        {
            _evolveBtn.gameObject.SetActive(_v3FormBeforeOn);
            if (_evolveLabel != null && _v3FormBeforeText != null) _evolveLabel.text = _v3FormBeforeText;
        }
    }

    /// <summary>망치가 닿는 순간 — 지금 값으로.</summary>
    private void V3Reveal()
    {
        if (!_v3Ready || _controller == null) return;
        RefreshV3Melee();
        RefreshEvolveButton();
    }

    /// <summary>초점 숫자 펀치 — 「+N」(태그 없는 글자)만 flash 색에서 제 색으로 · 크기 1 → 1+amp → 1. amp가 음수면 가라앉는다(실패).</summary>
    private async UniTask PunchFocalAsync(TMP_Text t, Color flash, float dur, float amp)
    {
        if (t == null || !t.gameObject.activeInHierarchy) { await Hold(dur); return; }
        var rt = t.rectTransform;
        var baseC = t.color;
        float e = 0f;
        while (e < dur)
        {
            if (_skipAnim) break;
            e += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(e / dur);
            rt.localScale = Vector3.one * (1f + amp * Mathf.Sin(k * Mathf.PI));
            t.color = Color.Lerp(flash, baseC, k * k);
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
        }
        rt.localScale = Vector3.one;
        t.color = baseC;
    }

    /// <summary>이정표(형 열림 · 진화 · 승급) — 형 칸 뒤 금빛이 두 번 부풀었다 가라앉는다(1.5초 · 흔들림 없음).</summary>
    private async UniTask MilestoneGlowAsync(float dur)
    {
        if (_v3RowGlow == null || _evolveBtn == null || !_evolveBtn.gameObject.activeInHierarchy) { await Hold(dur); return; }
        float e = 0f;
        try
        {
            while (e < dur)
            {
                if (_skipAnim) break;
                e += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(e / dur);
                float a = 0.5f - 0.5f * Mathf.Cos(k * Mathf.PI * 4f);   // 두 번
                _v3RowGlow.color = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.45f * a * (1f - 0.4f * k));
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        finally { if (_v3RowGlow != null) _v3RowGlow.color = Color.clear; }
    }

    /// <summary>이번 성공이 형이 깨어나는 단계(+5)에 닿았나 — 이정표 연출.</summary>
    private bool IsFormMilestone(EnhanceResult r)
    {
        int at = EvolveAt();
        return at > 0 && CurrentBranches.Count > 0 && r.beforeLevel < at && r.afterLevel >= at;
    }

    private string FormAwakeLine()
    {
        var (fa, fb) = BranchForms();
        return $"형이 깨어난다 — {fa?.DisplayName ?? "?"} 또는 {fb?.DisplayName ?? "?"}";
    }

    private TMP_Text FocalFor(int slot) => slot == RangedCard ? _v3PartFocal : _v3Focal;

    /// <summary>판정 도장 크기 — v2 64/72/60/32 → v3 42/48/40/24(초점 숫자 72가 주인공).</summary>
    private static float StampV3(float size) => Mathf.Clamp(Mathf.Round(size * 0.66f), FTitle, 48f);

    /// <summary>상시 — 다음 단계 점이 숨쉬고, 화면 크기가 바뀌면 판을 다시 맞춘다. 할당 없음. (연속 게이지는 10-18 재련 개편으로 없다)</summary>
    private void TickV3(float dt, float time)
    {
        if (_v3PipGlow != null && _v3PipGlow.enabled)
        {
            var c = UITheme.Thin.Gold;
            c.a = 0.18f + 0.22f * (0.5f + 0.5f * Mathf.Sin(time * 3.2f));
            _v3PipGlow.color = c;
        }
        if (_v3Host != null && _v3Host.rect.size != _v3Area) FitV3();
    }

    /// <summary>판 배율 — 1920×1080에서 1. 세로가 아주 짧은 화면(초광폭 등)에서만 줄인다(키우지 않는다).</summary>
    private void FitV3()
    {
        if (_v3Root == null || _v3Host == null) return;
        var size = _v3Host.rect.size;
        _v3Area = size;
        if (size.x < 400f || size.y < 300f) return;   // 아직 캔버스가 안 돌았다
        float s = Mathf.Min(1f, size.x * 0.98f / V3W, size.y * 0.98f / V3H);
        _v3Root.localScale = new Vector3(s, s, 1f);
    }

    // ── 도우미 ──

    private static void V3Place(RectTransform rt, float x, float y, float w, float h) => PlaceIn(rt, x, y, w, h, V3W, V3H);

    /// <summary>왼쪽 판(760×720) 좌표.</summary>
    private static void SP(Transform t, float x, float y, float w, float h)
    {
        if (t is RectTransform rt) PlaceIn(rt, x, y, w, h, V3LeftW, V3BoardH);
    }

    /// <summary>오른쪽 판(680×720) 좌표.</summary>
    private static void RP(Transform t, float x, float y, float w, float h)
    {
        if (t is RectTransform rt) PlaceIn(rt, x, y, w, h, V3RightW, V3BoardH);
    }

    /// <summary>파츠 행(704×52) 좌표.</summary>
    private static void RowP(Component c, float x, float y, float w, float h)
    {
        if (c != null && c.transform is RectTransform rt) PlaceIn(rt, x, y, w, h, PrW, PrH);
    }

    /// <summary>고른 파츠 판(704×238) 좌표.</summary>
    private static void DP(Component c, float x, float y, float w, float h)
    {
        if (c != null && c.transform is RectTransform rt) PlaceIn(rt, x, y, w, h, PrW, PdH);
    }

    /// <summary>아래 띠(1480×60) 좌표.</summary>
    private static void BP(RectTransform rt, float x, float y, float w, float h) => PlaceIn(rt, x, y, w, h, V3BarW, V3BarH);

    /// <summary>v3 글자 — 톤 v2 굵기 글꼴 · 가짜 굵게 없음 · 자동 크기도 16 아래로 내려가지 않는다(본문 최소 16).</summary>
    private static void V3Text(TMP_Text t, UIWeight weight, float size, Color color, bool autosize = false, float min = FMin)
    {
        if (t == null) return;
        t.enableAutoSizing = autosize;
        UITheme.StyleThinText(t, weight, size, color);
        t.fontSizeMax = size;
        t.fontSizeMin = Mathf.Min(size, Mathf.Max(FMin, min));
        t.richText = true;
    }

    private static TMP_Text NewText(Transform parent, string name, float size, UIWeight weight, TextAlignmentOptions align, Color color)
    {
        var t = ThinText(parent, name, size, weight, align, color);
        t.richText = true;
        t.fontSizeMax = size;
        t.fontSizeMin = Mathf.Min(size, FMin);
        return t;
    }

    /// <summary>얇은 칩 — 1px 테 + 옅은 바탕 + 글자 16. 색은 SetV3Chip이 칠한다.</summary>
    private static RectTransform MakeV3Chip(Transform parent, string name, out TMP_Text text)
    {
        var box = ShopUIStyle.MakeRect(parent, name).GetComponent<RectTransform>();
        UIThinBox.Create(box, "Box", Vector2.zero, Vector2.one, 0f, Color.white, Color.clear, Color.white);
        text = ThinText(box, "Text", FMin, UIWeight.Medium, TextAlignmentOptions.Center, Color.white);
        ShopUIStyle.Stretch(text.rectTransform);
        box.gameObject.SetActive(false);
        return box;
    }

    /// <summary>칩 글 · 색을 칠하고 폭(글자 + 24)을 돌려준다. 자리는 부르는 쪽이 잡는다.</summary>
    private static float SetV3Chip(RectTransform box, TMP_Text text, string label, Color color)
    {
        if (box == null || text == null) return 0f;
        box.gameObject.SetActive(true);
        text.text  = label;
        text.color = color;
        foreach (var img in box.GetComponentsInChildren<Image>(true))
            img.color = img.name == "Fill" ? UITheme.Thin.WithA(color, 0.08f) : UITheme.Thin.WithA(color, 0.55f);
        return text.GetPreferredValues(label).x + 24f;
    }

    /// <summary>
    /// 남색 판 — 불투명 바탕 위에 제단 상세 판과 같은 그림(description_box 남색 재색칠 · 9-slice)을 덮는다.
    /// 그림이 자기 가는 테를 가져 금 테는 그림이 없을 때만(제단 AltarDetailPanel 규칙). 바깥 창은 BuildV3Root가 금선을 둔다.
    /// </summary>
    private static void V3Panel(Image host, Sprite art, bool addEdge = true)
    {
        host.sprite = null;
        host.type   = Image.Type.Simple;
        host.color  = V3BoardC;
        if (art != null)
        {
            var img = ShopUIStyle.MakeImage(host.transform, "V3Art", Color.white);
            img.transform.SetAsFirstSibling();
            ShopUIStyle.Stretch(img.rectTransform);
            ShopUIStyle.Skin(img, art, sliced: true);
            return;
        }
        if (addEdge) UIThinBox.Create(host.rectTransform, "V3Edge", Vector2.zero, Vector2.one, 0f, UITheme.Thin.LineGold, Color.clear, Color.clear);
    }

    /// <summary>v3가 대신하는 옛 부품 — 꺼 둔 보관함으로(갱신 코드는 그대로 돈다 · 보관함이 꺼져 있어 SetActive(true)도 보이지 않는다).</summary>
    private void Park(Component c)
    {
        if (c != null && _v3Park != null) c.transform.SetParent(_v3Park, false);
    }
}
