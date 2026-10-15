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
    private const float ChipMidY = 132f, RuleY1 = 214f, StreakY = 236f, RuleY2 = 340f, MatY = 360f;

    // 원거리 왼쪽 판 — 파츠 5행(704×52) · 고른 파츠 판
    private const float PrY0 = 62f, PrH = 52f, PrGap = 6f, PrW = 704f;
    private const float PdY = 352f, PdH = 248f;

    // 결과 연출 길이(설계 §1-2 — 결과 무게에 비례 · 카메라 · 화면 흔들림 없음)
    private const float V3SuccessDur   = 0.40f;   // 평범한 성공 — 「+N」 펀치
    private const float V3FailDur      = 0.50f;   // 실패 — 붉게 가라앉았다 돌아온다
    private const float V3MilestoneDur = 1.50f;   // 이정표 — 형 열림 · 진화 · 승급
    private const float V3JackpotDur   = 1.00f;   // 잭팟(재료 환불) — 평범한 성공과 이정표 사이
    private const float V3PunchAmp     = 0.14f;

    /// <summary>잭팟 확률 상한 — CrucibleRoomController.JackpotMaxChance(비공개 0.5)와 같은 값. 게이지 끝 · 「최대 50%」 표시용.</summary>
    private const float JackpotCapShown = 0.5f;
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
    private UIThinFrame _v3EnhanceFrame;
    private Transform   _rowLineTop;
    private float _v3StreakShown, _v3StreakTarget;

    // 판정 전(망치가 닿기 전) 붙잡아 둘 값 — 결과가 새지 않게(HoldBeforeState와 같은 규칙)
    private int    _v3StreakBefore;
    private float  _v3JackpotBefore;
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

        LayoutV3Tab(_tabWeaponImg, "무기 강화", 260f, 144f);     // 탭 폭 = 글자 + 양옆 여유 — 준비 ◆가 글자 끝과 떨어지게
        LayoutV3Tab(_tabRangedImg, "원거리 파츠", 412f, 160f);
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
        _cardBasePos[1] = Vector2.zero;   // Place가 anchoredPosition을 0으로 굳혔다(흔들림 복원 기준)
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
        else        RefreshV3Melee(0);
    }

    /// <summary>근거리 — 지금 값으로 그린다. heldRefund = 잭팟 환불이 아직 재화 칸에 안 돌아온 몫(연출 중).</summary>
    private void RefreshV3Melee(int heldRefund)
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
            _rateLabel.text = "성공 확률";
            _successText.text = "—";
            HideChips(0);
            _v3StreakGroup.gameObject.SetActive(false);
            _v3MatRow.gameObject.SetActive(false);
            _v3Fail.text = "근접 무기가 없다";
            ButtonV3("강화하기", 0, 0);
            RenderPathMelee(null, 0, 0);
            _v3Next.text = "";
            return;
        }
        int max  = _controller.MaxAt(m);
        bool can = _controller.CanEnhance(m);
        RenderMeleeV3(w, w.enhanceLevel, max, can ? _controller.SuccessChanceAt(m) : 0f, _controller.Streak, _controller.JackpotChance,
                      can ? _controller.CostAt(m) : 0, _controller.FuelAmount - heldRefund);
        RenderPathMelee(w, w.enhanceLevel, max);
        _v3Next.text = NextLineV3();
    }

    /// <summary>근거리 두 판 — 단계 L 기준. 판정 전(HoldBeforeState)에는 강화 전 값으로, 판정 순간(V3Reveal)엔 지금 값으로 부른다.</summary>
    private void RenderMeleeV3(WeaponData w, int L, int max, float chance, int streak, float jackpot, int need, int have)
    {
        bool maxed = L >= max;

        // 왼쪽 — 초점 · 바뀌는 수치
        _v3FocalCap.text = maxed ? "강화 단계" : "다음 강화";
        _v3Focal.text = maxed ? $"+{L} <size=50%><color={HexMute}>최대</color></size>"
                              : $"+{L} <color={HexMute}>→</color> <color={HexUp}>+{L + 1}</color>";
        _v3Focal.color = UITheme.Thin.Ink;
        FillStatRows(w, L, max);

        // 오른쪽 — 숫자 하나 · 원인 칩 · 연속 · 재료 · 실패 · 버튼
        _rateLabel.text   = maxed ? "강화 단계" : "성공 확률";
        _successText.text = maxed ? "MAX" : $"{chance * 100f:F0}%";
        _successText.color = UITheme.Thin.Ink;
        HideChips(maxed ? 0 : FillRateChips());
        UITooltipTrigger.Attach(_successText.gameObject, "성공 확률", maxed ? "최대 강화 — 더 굴릴 것이 없다" : RateTip(chance), UITheme.Thin.Gold);
        StreakV3(streak, jackpot, maxed);
        MaterialV3(need, have, maxed);

        int drop = maxed || _controller.Table == null ? 0 : _controller.Table.DropAt(L);
        _v3Fail.text = maxed ? MaxedLine(w)
                     : drop > 0 ? $"<color={HexDown}>실패하면 −{drop} 하락 · 재료도 사라진다</color>"
                                : "실패해도 단계는 그대로 · 재료만 사라진다";
        ButtonV3(maxed ? "최대 강화" : "강화하기", maxed ? 0 : need, have);
    }

    /// <summary>바뀌는 수치 2~3줄 — 공격은 늘, 스킬 단계 · 형 · 마스터리는 이번 한 번이 그 문턱을 넘을 때만. 강화가 바꾸는 수치는 실제로 이것뿐이다.</summary>
    private void FillStatRows(WeaponData w, int L, int max)
    {
        int n = 0;
        var t = _controller.Table;
        float a0 = WeaponEnhanceService.EffectiveAttackAt(w, t, L);
        if (L >= max) SetStat(n++, "공격", $"{a0:F1}", $"<color={HexMute}>최대</color>");
        else
        {
            float a1 = WeaponEnhanceService.EffectiveAttackAt(w, t, L + 1);
            SetStat(n++, "공격", Arrow($"{a0:F1}", $"{a1:F1}"), $"▲{a1 - a0:F1}");

            int evo = EvolveAt();
            if (evo > 0 && L + 1 == evo && CurrentBranches.Count > 0)
                SetStat(n++, "형", $"<color={HexMute}>잠김 →</color> <color={HexForm}>깨어남</color>", $"<color={HexForm}>◆</color>");

            var ms = SkillTierResolver.Milestones(w);
            int t0 = TierAt(ms, L), t1 = TierAt(ms, L + 1);
            if (t1 > t0 && n < StatRows) SetStat(n++, "스킬 단계", Arrow($"{t0}", $"{t1}단계"), "▲1");
        }

        // 마스터리(진화 뒤 기본 상한 위) — 스킬 피해 · 재사용
        if (n < StatRows && t != null && w.evolutionStage > 0)
        {
            int m0 = t.MasteryLevel(w.rarity, w.evolutionStage, L);
            int m1 = L >= max ? m0 : t.MasteryLevel(w.rarity, w.evolutionStage, L + 1);
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
        return string.IsNullOrEmpty(mastery) ? "최대 강화에 닿았다" : mastery;
    }

    /// <summary>성공률 원인 칩 — 「▲ +8 인장」 · 「▲ +15 화로」 · 「▼ −10 저주」(로스트아크 「기본 + 추가」). 쓴 칩 수.</summary>
    private int FillRateChips()
    {
        int k = 0;
        float sig = CrucibleRoomController.SigilBonus, ev = _controller.EventSuccessBonus;
        int count = (sig > 0f ? 1 : 0) + (Mathf.Approximately(ev, 0f) ? 0 : 1);
        float y0 = ChipMidY - (count * 38f - 6f) * 0.5f;   // 칩 묶음을 숫자 세로 가운데에
        if (sig > 0f) RateChip(k++, y0, $"▲ +{sig * 100f:F0} 인장", UpGreen);
        if (ev > 0f)  RateChip(k++, y0, $"▲ +{ev * 100f:F0} 화로", UpGreen);
        if (ev < 0f)  RateChip(k++, y0, $"▼ −{-ev * 100f:F0} 저주", DownRed);
        return k;
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

    /// <summary>숫자 호버 — 「기본 55% · 인장 +8%p · 화로 +15%p」.</summary>
    private string RateTip(float chance)
    {
        float sig = CrucibleRoomController.SigilBonus, ev = _controller.EventSuccessBonus;
        var sb = new StringBuilder();
        sb.Append("기본 ").Append((Mathf.Clamp01(chance - sig - ev) * 100f).ToString("F0")).Append('%');
        if (sig > 0f) sb.Append("\n대장장이의 인장 +").Append((sig * 100f).ToString("F0")).Append("%p");
        if (ev > 0f)  sb.Append("\n화로 +").Append((ev * 100f).ToString("F0")).Append("%p");
        if (ev < 0f)  sb.Append("\n저주 ").Append((ev * 100f).ToString("F0")).Append("%p");
        return sb.ToString();
    }

    /// <summary>
    /// 연속 게이지 — 지금 있는 연속(Streak) 상태만 보인다: 연속 성공이 잭팟(재료 환불) 확률을 올리고, 실패하면 0부터.
    /// 로스트아크 장인의 기운처럼 「쌓이는 것」이 보여야 덜 억울하다(리서치 B-1) — 새 천장 기능은 만들지 않는다.
    /// </summary>
    private void StreakV3(int streak, float jackpot, bool hidden)
    {
        _v3StreakGroup.gameObject.SetActive(!hidden);
        if (hidden) return;
        float bounty = _controller.EventJackpotBonus;
        _v3StreakHead.text = $"<color={HexMute}>연속 성공</color>  {streak}회";
        _v3StreakVal.text  = $"잭팟 {jackpot * 100f:F0}% <color={HexMute}>· 재료 환불</color>"
                           + (bounty > 0f ? $"  <color={HexUp}>▲ +{bounty * 100f:F0} 화로</color>" : "");
        _v3StreakRule.text = $"성공이 이어질수록 잭팟이 오른다 · 실패하면 처음부터 · 최대 {JackpotCapShown * 100f:F0}%";
        _v3StreakTarget = Mathf.Clamp01(jackpot / JackpotCapShown);
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
        _v3PathCap.text = "단계";
        _v3PipGlow.enabled = false;
        int n = w == null || max <= 0 ? 0 : Mathf.Min(max, MaxTicks);
        var table = _controller.Table;
        int evoAt = EvolveAt();
        var tiers = w != null ? SkillTierResolver.Milestones(w) : System.Array.Empty<int>();
        float span = n > 0 ? (PipX1 - PipX0) / n : 0f;
        for (int j = 0; j < MaxTicks; j++)
        {
            bool on = j < n;
            _v3Pip[j].gameObject.SetActive(on);
            _v3PipLabel[j].gameObject.SetActive(on);
            if (!on) continue;
            int lv = n == max ? j + 1 : Mathf.RoundToInt((j + 1) * (float)max / n);
            bool evo = lv == evoAt;
            int skill = System.Array.IndexOf(tiers, lv);
            bool danger = lv < max && table != null && table.DropAt(lv) > 0;
            SetPip(j, PipX0 + span * (j + 0.5f), span, lv <= L, lv == L + 1, evo || skill >= 0, danger, $"+{lv}");
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
    /// 원거리 — 근거리와 같은 뼈대(10-15 사용자 「원거리는 좀 더 보기 편한 가독성과 레이아웃 · 파츠 강화도 확률」):
    /// 오른쪽 = 성공 확률 96 · 원인 칩(인장 · 화로 · 저주) · 연속 · 잭팟 · 재료 · 실패 규칙 · [강화하기 · 비용].
    /// 지금 조합의 총합(몇 발 · 관통 · 폭발 · 유도)은 왼쪽 목록 머리 한 줄로 옮겼다 — 예전 오른쪽 표 5줄은 파츠가 없으면 「없음」만 다섯 줄이었다.
    /// </summary>
    private void RefreshV3Ranged()
    {
        SetRowCount(0);
        RenderPathRanged();
        _v3Next.text = NextLineV3();
        _successText.color = UITheme.Thin.Ink;
        if (_slotSummary != null) _slotSummary.text = RangedComboLine();

        var def   = SelectedPartDef();
        int have  = _controller.FuelAmount;
        if (def == null)
        {
            _rateLabel.text   = "성공 확률";
            _successText.text = "—";
            UITooltipTrigger.Attach(_successText.gameObject, "성공 확률", "왼쪽 목록에서 올릴 파츠를 고르면 확률이 나온다", UITheme.Thin.Gold);
            HideChips(0);
            StreakV3(0, 0f, hidden: true);
            MaterialV3(0, have, hidden: true);
            _v3Fail.text = "왼쪽 목록에서 올릴 파츠를 고른다";
            if (_enhanceBtn != null) _enhanceBtn.interactable = false;
            ButtonV3("파츠를 고른다", 0, have);
            Halo(_enhanceBtn, false);
        }
        else
        {
            int lv = RangedPartsState.Current?.LevelOf(def.part_id) ?? 0;
            bool maxed = def.max_level > 0 && lv >= def.max_level;
            int cost = maxed ? 0 : _controller.PartCostAt(def.part_id);
            float chance = maxed ? 0f : _controller.PartSuccessChanceAt(def.part_id);
            int drop = maxed ? 0 : _controller.PartDropAt(def.part_id);
            _rateLabel.text   = maxed ? "파츠 단계" : "성공 확률";
            _successText.text = maxed ? "MAX" : $"{chance * 100f:F0}%";
            HideChips(maxed ? 0 : FillRateChips());
            UITooltipTrigger.Attach(_successText.gameObject, "성공 확률", maxed ? "최대 — 더 굴릴 것이 없다" : RateTip(chance), UITheme.Thin.Gold);
            StreakV3(_controller.Streak, _controller.JackpotChance, maxed);
            MaterialV3(cost, have, hidden: maxed);
            _v3Fail.text = maxed ? "이 파츠는 최대 — 더 올릴 수 없다"
                         : drop > 0 ? $"<color={HexDown}>실패하면 −{drop} 하락 · 재료도 사라진다</color>"
                                    : "실패해도 단계는 그대로 · 재료만 사라진다";
            bool afford = !maxed && have >= cost;
            if (_enhanceBtn != null) _enhanceBtn.interactable = afford;
            ButtonV3(maxed ? "최대" : lv <= 0 ? "켜기" : "강화하기", cost, have);
            Halo(_enhanceBtn, afford && !Choosing);   // 후광은 한 화면 한 곳
        }
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

    /// <summary>파츠 강화 — 판정 한 박자 전까지 오른쪽 판 · 초점을 강화 전 값으로(결과가 새지 않게 · 근거리 V3HoldBefore와 같은 뜻).</summary>
    private void V3HoldBeforePart(EnhanceResult r)
    {
        if (!_v3Ready) return;
        _successText.text = $"{r.chance * 100f:F0}%";
        StreakV3(_v3StreakBefore, _v3JackpotBefore, false);
        if (_v3PartFocal != null)
            _v3PartFocal.text = $"Lv {r.beforeLevel} <color={HexMute}>→</color> <color={HexUp}>{r.beforeLevel + 1}</color>";
    }

    /// <summary>파츠 판정 한 박자 — 타격음 · 초점이 한 번 부풀었다 가라앉는다(망치는 근거리 무대에만 있어 소리와 초점이 충격을 말한다).</summary>
    private async UniTask PartStrikeAsync()
    {
        ShopUIStyle.PlaySfx("crucible_hit");
        await PunchFocalAsync(_v3PartFocal, UITheme.Thin.Gold, PartStrikeDur, 0.06f);
        await Hold(PartStrikeHold);
    }

    /// <summary>고른(또는 손을 올린) 파츠의 초점 「Lv 3 → 4」 — RefreshRangedCard가 부른다(호버에도 따라간다).</summary>
    private void RefreshPartFocalV3()
    {
        if (!_v3Ready || _v3PartFocal == null) return;
        var def = HoveredPartDef() ?? SelectedPartDef();
        if (def == null) { _v3PartFocal.text = ""; return; }
        int lv = RangedPartsState.Current?.LevelOf(def.part_id) ?? 0;
        bool maxed = def.max_level > 0 && lv >= def.max_level;
        _v3PartFocal.text = maxed ? $"Lv {lv} <size=50%><color={HexMute}>최대</color></size>"
                                  : $"Lv {lv} <color={HexMute}>→</color> <color={HexUp}>{lv + 1}</color>";
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

        bool enhanceFirst = _controller.CanEnhance(m) && _controller.FuelAmount >= _controller.CostAt(m);
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
                body  = $"<color={HexMute}>잠김</color> · +{Mathf.Max(0, EvolveAt())}에서 {pair}{Iga(fb?.DisplayName)} 깨어난다";
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
                string c = afford ? $"강화재료 {cost}" : $"<color={HexDown}>강화재료 {cost}</color>";
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
        bool lit = act && afford && click;
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

    /// <summary>강화 누르기 직전 — 판정 전 화면에 붙잡아 둘 값(연속 · 잭팟 · 「다음」 · 형 칸).</summary>
    private void V3SnapshotBefore()
    {
        if (!_v3Ready || _controller == null) return;
        _v3StreakBefore  = _controller.Streak;
        _v3JackpotBefore = _controller.JackpotChance;
        _v3NextBefore    = _v3Next != null ? _v3Next.text : null;
        _v3FormBeforeOn  = _evolveBtn != null && _evolveBtn.gameObject.activeSelf;
        _v3FormBeforeText = _evolveLabel != null ? _evolveLabel.text : null;
    }

    /// <summary>망치가 닿기 전 — v3 두 판을 강화 전 값으로(결과가 새지 않게).</summary>
    private void V3HoldBefore(EnhanceResult r, int max)
    {
        var w = _controller.GetSlot(PlayerWeaponManager.Slot0);
        if (w == null) return;
        int have = _controller.FuelAmount - (_controller.LastJackpot ? _controller.LastRefund : 0);
        RenderMeleeV3(w, r.beforeLevel, max, r.chance, _v3StreakBefore, _v3JackpotBefore, r.spent, have);
        RenderPathMelee(w, r.beforeLevel, max);
        if (_v3NextBefore != null) _v3Next.text = _v3NextBefore;
        if (_evolveBtn != null)
        {
            _evolveBtn.gameObject.SetActive(_v3FormBeforeOn);
            if (_evolveLabel != null && _v3FormBeforeText != null) _evolveLabel.text = _v3FormBeforeText;
        }
    }

    /// <summary>망치가 닿는 순간 — 지금 값으로(잭팟 환불은 재화 칸과 같이 늦게 돌아온다).</summary>
    private void V3Reveal()
    {
        if (!_v3Ready || _controller == null) return;
        RefreshV3Melee(_controller.LastJackpot ? _controller.LastRefund : 0);
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

    /// <summary>상시 — 연속 게이지가 목표를 따라 차고(실패면 비고), 다음 단계 점이 숨쉬고, 화면 크기가 바뀌면 판을 다시 맞춘다. 할당 없음.</summary>
    private void TickV3(float dt, float time)
    {
        if (_v3StreakFill != null && !Mathf.Approximately(_v3StreakShown, _v3StreakTarget))
        {
            _v3StreakShown = Mathf.MoveTowards(_v3StreakShown, _v3StreakTarget, dt);
            _v3StreakFill.anchorMax = new Vector2(_v3StreakShown, 1f);
        }
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
