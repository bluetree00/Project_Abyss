using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보조무기(원거리) 선택 팝업 — <b>캐러셀</b>.
///
/// 기획 피벗: 주무기는 무형검(근접)이 기본 지급이라 여기서는 보조 원거리만 고른다.
///
/// 목록으로 쌓지 않고 <b>한 번에 무기 하나를 크게</b> 보여주고 좌우로 넘긴다 —
/// 무기 하나에 시선이 집중되고, 넘기는 손맛이 생기며, 큰 아이콘/큰 글씨로 정보가 잘 읽힌다.
/// 잠금 무기(석궁=특전)는 카드에 표시하되 확정만 막는다.
///
/// 슬라이드는 위치가 아니라 알파+스케일 크로스페이드(레이아웃과 안 싸운다). 시간정지 중이라 unscaled.
/// 가독성 머티리얼은 UI_RelicInfoPopup과 동일(얇은 폰트 두께 보정).
///
/// <para>10-11 v2 — 후보가 2~3개면 <b>한 화면에 나란히</b> 놓는다(리서치 C §4 「차이」: 하데스 · RoR2는 고를 후보를 다 늘어놓고 눈으로 비교한다).
/// 카드를 누르면 고르고(금 85% 2px 테 + 1.03배 · 나머지 0.55), [장착하기] 하나로 확정한다. 좌우 화살표 · ← → 키는 고르는 카드를 옮긴다.
/// 4개 이상이면 지금처럼 한 장씩 넘긴다. 나란히 카드는 구운 프리팹에 없어 처음 쓸 때 넘기기 카드(Slider)를 복제해 만든다.</para>
/// </summary>
public class UI_RangedForgePopup : UI_Popup
{
    // ── Constants ─────────────────────────────────────────────
    // 아트가 있으면 패널은 bg.png 비율(1289:1338 = 0.9634)을 지켜야 테두리와 상·하단 문장이 찌그러지지 않는다.
    // 높이는 "내용이 필요로 하는 세로(≈590) + 아트 프레임 여백(위 72 / 아래 52)"로 잡았다 —
    // 목업 비율만 따르면 프레임 여백만큼 내용이 눌려 이름·스탯이 겹친다(FixHeight 주석 참고).
    // 크기는 완성본 목업(전체 샷.png)을 재서 환산했다 — bg 실측 890×924, 패널 747 → 배율 0.826.
    // 세로 예산: 카드 내용(홀더198+이름34+배지20+태그22+스탯104+간격40+여백12 = 430)
    //          + 제목38 + 부제20 + 도트16 + 구분장식37 + 버튼45 + 행간격60 + 프레임여백(77+52) = 775.
    // 모자라면 카드 하단(스탯)이 도트·구분장식 위로 흘러넘친다.
    // 10-19 ⑦ — 고른 뒤에도 공방이 남아 다시 고를 수 있다(WeaponForgeAltar). 구운 프리팹에도 이 글을 덮어쓴다(옛 글은 「무명의 형상」).
    private const string SubText = "주무기 <color=#CFC0A0>무형검</color>은 이미 손에 있다. 곁에 둘 하나를 고른다 — 심연에 들기 전까지 다시 바꿀 수 있다.";
    private const float SkinPanelWidth  = 747f;
    private const float SkinPanelHeight = 775f;
    private const float PanelWidth  = 620f;
    private const float PanelHeight = 640f;   // 카드 내용(아이콘+4줄 스탯)이 눌리지 않을 최소치
    private const float SlideTime   = 0.18f;

    // 아이콘 홀더 — 채움(356) 기준. 테두리·외곽선은 원본 비율대로 조금씩 크다.
    private const float HolderSize        = 198f;   // 목업 240 × 환산 0.826
    private const float HolderFrameScale  = 359f / 356f;
    private const float HolderOutlineScale = 373f / 356f;

    private const float ArrowW = 45f, ArrowH = 110f;   // 목업 55×133 × 환산 0.826
    private const float DividerW = 280f, DividerH = 37f;
    private const float EquipW = 137f, EquipH = 45f;   // 목업 166×55 × 환산 0.826 — 예전 160×52는 17% 컸다

    // 10-11 색 정리(리서치 C §4 「고칠 값」 P1) — 이 화면만 파랑 계열 6색(#8CB8F2 · #9ECCFF …)을 썼다. 다른 화면처럼 금 · 잉크(UITheme.Thin)로.
    // 금 = 누를 것 · 지금 위치(제목 · 확정 · 화살표 · 점) / 잉크 = 이름 · 수치 / 잠김 = 청동. 무기 테마색은 더는 글자에 쓰지 않는다.
    private static readonly Color PanelBg   = new(0.07f, 0.06f, 0.10f, 0.97f);
    private static readonly Color PanelLine = UITheme.Thin.LineActive;
    private static readonly Color Accent    = UITheme.Thin.Gold;

    private static readonly Color TitleColor = UITheme.Thin.Gold;
    private static readonly Color BodyColor  = UITheme.Thin.Ink;
    private static readonly Color SubColor   = UITheme.Thin.InkA(0.6f);
    private static readonly Color LockColor  = new(0.52f, 0.42f, 0.40f, 1f);
    private static readonly Color ArrowOn    = UITheme.Thin.Gold;
    private static readonly Color ArrowOff   = UITheme.Thin.InkA(0.25f);
    private static readonly Color DotOn      = UITheme.Thin.Gold;
    private static readonly Color DotOff     = UITheme.Thin.InkA(0.28f);

    // 글자 단계(톤 v2 · 설계 크기 — 창은 localScale ×1.31로 함께 커진다). 제목만 DNF.
    private const float TitleSize = 24f;          // 설계 30 → 24(화면 ≈ 32, 리서치 C §4 P3)
    private const float NameSize  = 24f;
    private const float BodySize  = 17f;          // 부제 · 태그라인 · 취소
    private const float SmallSize = 15f;          // 잠금 배지 · 「1 / N」
    // 수치표(P1) — 라벨 왼쪽 17 · 잉크 60% / 값 오른쪽 20 · 잉크, 표 폭 360
    private const float StatLabelSize = 17f, StatValueSize = 20f, StatTableW = 360f;
    private const string StatLabelHex = "#ECE6D899";   // Thin.Ink 60%
    private const float DotSize = 14f;                 // 점 10 → 14(P2)

    // 나란히 놓기(10-11 v2) — 2~3개는 카드를 나란히, 그보다 많으면 넘기기. 창(747 · 아트 비율 고정)은 그대로 두고 무대만 나눈다.
    private const int   MaxColumns     = 3;
    private const int   MultiViewerPad = 10;      // 화살표를 판 가장자리 쪽으로 — 카드 폭 확보(넘기기 판은 프리팹 값 53). 무대 ≈ 545 → 카드 ≈ 264
    private const float CardGap        = 16f;
    private const float CardIconSize   = 120f;    // 나란히 카드의 그림 칸(넘기기 198) — 뷰어 세로(≈ 400)에 이름 · 배지 · 태그라인 · 수치표를 다 넣는다
    private const float TagTwoLineH    = 50f;     // 태그라인 두 줄(17 · 줄 높이 ≈ 24.6) — 넘치면 말줄임
    // 3열은 카드 폭 ≈ 171이라 태그라인(약 35자)이 17로는 네 줄이 된다(10-11 실측 폭 159 · 세 줄 칸에서도 세 장 모두 잘림).
    // 3열만 글자를 한 단계 낮추고(배지와 같은 15 — 화면 ≈ 20px) 네 줄을 준다. 그림 칸은 그만큼 줄인다
    private const float CardIconSize3  = 84f;
    private const float TagFourLineH3  = 88f;     // 15 · 줄 높이 ≈ 21.8 × 4
    private const float CardStatInset  = 12f;     // 수치표 좌우 여백(카드가 표 폭 360보다 좁다)
    private const float SelScale       = 1.03f;
    private const float DimAlpha       = 0.55f;
    private const float SelEdgePx      = 2f;      // v2 선 굵기 — 고른 것 2 · 평소 1(1080 기준 px, 창 배율을 나눠 화면에서 맞춘다)
    private const float IdleEdgePx     = 1f;

    // ── Nested ────────────────────────────────────────────────
    public struct Entry
    {
        public WeaponSO Weapon;
        public bool     Locked;
        public string   LockReason;
    }

    /// <summary>나란히 놓인 후보 카드 한 장 — 넘기기 카드(Slider)를 복제한 것과 덧붙인 테.</summary>
    private sealed class CardView
    {
        public RectTransform Root;
        public CanvasGroup   Group;
        public Image         Glow, Icon;
        public TMP_Text      Name, Badge, Tag, Stats;
        public LayoutElement IconBox;
        public Image[]       Edge;   // 상 · 하 · 좌 · 우 — 평소 1px 잉크 18% / 고른 것 2px 금 85%
    }

    // ── Private ───────────────────────────────────────────────
    [SerializeField] private RectTransform _stage;        // 무대(레이아웃 자식, 고정) — 슬라이드로 만지지 않는다
    [SerializeField] private RectTransform _slider;       // 무대 안에서 좌우로 밀려 들어오는 카드 컨테이너(레이아웃 무관)
    [SerializeField] private Image         _stageGlow;    // 무대 배경 — 무기 테마색으로 은은하게 물든다
    [SerializeField] private CanvasGroup   _cardGroup;    // 현재 카드 알파/스케일
    [SerializeField] private Image         _icon;
    [SerializeField] private TMP_Text      _name;
    [SerializeField] private TMP_Text      _badge;
    [SerializeField] private TMP_Text      _tag;
    [SerializeField] private TMP_Text      _stats;
    [SerializeField] private Button        _prev, _next;
    [SerializeField] private RectTransform _dots;
    [SerializeField] private Button        _confirm;
    [SerializeField] private Image         _confirmBg;
    [SerializeField] private TMP_Text      _confirmLabel;

    private WeaponForgeSkinSO _skin;

    private readonly List<Entry> _entries = new();
    private readonly List<Image> _dotImgs = new();
    private readonly Dictionary<Button, TMP_Text> _arrowGlyphs = new();   // SetArrow가 색을 바꾼다(버튼 2개)
    private readonly Dictionary<Button, Image>    _arrowArts   = new();   // 아트 화살표는 글리프 대신 이 이미지를 흐린다
    private readonly StringBuilder _sb = new();
    private static Material s_textMat;
    private TMP_Text _dotCount;   // 「1 / N」(런타임 생성)

    private int _index;
    private int _slideGen;
    private int _slideDir;   // 넘긴 방향(+1 오른쪽 / -1 왼쪽) — 카드가 그쪽에서 밀려 들어온다
    private UniTaskCompletionSource<WeaponSO> _tcs;

    // 나란히 판(10-11 v2) — 처음 쓸 때 만든다(구운 프리팹엔 없다)
    private readonly List<CardView> _cards = new();
    private RectTransform _cardsRoot;
    private RectTransform _panelRt;
    private HorizontalLayoutGroup _viewerRow;
    private int  _viewerPadBase = -1;   // 프리팹의 화살표 들임(넘기기 판) — 처음 볼 때 기억한다
    private bool _multi;
    private CancellationTokenSource _selCts;
    private readonly float[] _fromAlpha = new float[MaxColumns];
    private readonly float[] _fromScale = new float[MaxColumns];
    private readonly Color[] _fromEdge  = new Color[MaxColumns];

    public override bool BlocksGameplay => true;

    // ── Init ──────────────────────────────────────────────────
    public override void Init()
    {
        base.Init();
        _skin = UISkin.WeaponForge;
        BuildLayout();
        ApplyThinStyle();
    }

    /// <summary>← → 키 = 좌우 화살표와 같다 — 나란히 판은 고르는 카드를 옮기고, 넘기기 판은 넘긴다. 시간정지 중에도 입력은 읽힌다.</summary>
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.LeftArrow))       Step(-1);
        else if (Input.GetKeyDown(KeyCode.RightArrow)) Step(+1);
    }

    // ── Public API ────────────────────────────────────────────
    public void Setup(IReadOnlyList<Entry> entries)
    {
        Managers.Sound?.PlayUiEvent(SoundEvent.UiOpen);
        _entries.Clear();
        if (entries != null)
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Weapon != null) _entries.Add(entries[i]);

        _index = 0;
        _tcs = new UniTaskCompletionSource<WeaponSO>();

        // 2~3개는 나란히(10-11 v2) — 처음엔 첫 후보를 골라 둔다(예전처럼 열자마자 [장착하기]가 첫 후보를 단다).
        _multi = _entries.Count >= 2 && _entries.Count <= MaxColumns;
        ApplyViewMode();

        BuildDots();
        // 수치표 폭(360) 여백은 무대 폭을 보고 정한다 — 열자마자 레이아웃이 아직 안 돌았으면 폭이 0이다.
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
        if (_multi) ShowCards();
        else        ShowCard(_index, instant: true);
    }

    public UniTask<WeaponSO> WaitForChoiceAsync() => _tcs.Task;

    /// <summary>
    /// 정상 경로(Complete) 없이 파괴돼도 대기를 끝낸다 — 씬 전환·CloseAllPopupUI 등.
    /// 이게 없으면 _tcs가 영구 미완료라 WaitForChoiceAsync가 무한 대기하고 모루가 영구히 잠긴다.
    /// null = Cancel과 같은 값이라 안전한 기본값이다. Complete가 먼저면 TrySetResult가 무시된다.
    /// </summary>
    protected override void OnDestroy()
    {
        base.OnDestroy();   // 차단 잠금 누수 방지(UI_Popup)
        _tcs?.TrySetResult(null);
        CancelSelectionTween();
    }

    // ── 캐러셀 ────────────────────────────────────────────────
    private void Step(int dir)
    {
        int next = _index + dir;
        if (next < 0 || next >= _entries.Count) return;
        if (_multi) { Select(next); return; }   // 나란히 판 — 고르는 카드만 옮긴다
        Managers.Sound?.PlayUiEvent(SoundEvent.UiSelect);   // 넘기기 판 — 캐러셀 넘김
        _index = next;
        _slideDir = dir;
        ShowCard(_index, instant: false);
    }

    private void ShowCard(int i, bool instant)
    {
        if (i < 0 || i >= _entries.Count) return;
        var e = _entries[i];

        FillCard(e, _icon, _name, _badge, _tag, _stats, _stageGlow, reserveBadge: false);
        FitStatTable();

        // 화살표 활성/비활성 — 양 끝에서 못 넘어감을 색으로 알린다.
        SetArrow(_prev, i > 0);
        SetArrow(_next, i < _entries.Count - 1);
        UpdateDots(i);
        RefreshConfirm(e);

        if (instant)
        {
            _cardGroup.alpha = 1f;
            _slider.localScale = Vector3.one;
            _slider.anchoredPosition = Vector2.zero;
            return;
        }
        SlideInAsync(++_slideGen, _slideDir).Forget();
    }

    /// <summary>
    /// 카드 한 장에 후보 하나를 채운다 — 넘기기 판(한 장)과 나란히 판(카드마다)이 같이 쓴다.
    /// <paramref name="reserveBadge"/>면 잠금이 아니어도 배지 줄 자리를 비워 둔다(나란히 카드끼리 줄 높이를 맞춘다).
    /// </summary>
    private void FillCard(Entry e, Image icon, TMP_Text name, TMP_Text badge, TMP_Text tag, TMP_Text stats, Image glow, bool reserveBadge)
    {
        var w = e.Weapon;

        // 잠금이면 청동, 아니면 금(10-11 — 무기 테마색은 계열이 하나 더 늘어 쓰지 않는다).
        Color accent = e.Locked ? LockColor : Accent;

        icon.sprite  = w.icon;
        icon.enabled = w.icon != null;   // 아이콘이 없는 무기는 흰 네모 대신 빈 홀더로
        icon.color   = w.icon != null ? Color.white : new Color(1f, 1f, 1f, 0.12f);

        name.text  = w.displayName;
        name.color = e.Locked ? LockColor : BodyColor;   // 이름은 잉크 — 금은 누를 것에만

        badge.gameObject.SetActive(e.Locked || reserveBadge);
        badge.text = e.Locked ? "■ " + (string.IsNullOrEmpty(e.LockReason) ? "잠금" : e.LockReason) : string.Empty;

        tag.text   = w.tagline ?? string.Empty;
        tag.color  = e.Locked ? LockColor : SubColor;

        stats.text = e.Locked ? "<size=17><color=#8A6B6B>기억의 제단에서 해금하는 무기</color></size>" : StatBlock(w);

        // ⚠️ 코드로 구운 스프라이트(UIProceduralSprites)는 <b>프리팹에 직렬화되지 않는다</b> —
        //    베이크된 프리팹에서는 null로 되살아나 각진 색판으로 보였다(2026-09-10 게임 화면의 초록 판때기).
        //    그래서 짓는 곳이 아니라 <b>쓰는 곳</b>에서 한 번 붙인다.
        if (glow.sprite == null)
        {
            glow.sprite = UIProceduralSprites.RoundedRect(radius: 36f, feather: 28f, size: 160);
            glow.type   = Image.Type.Sliced;
        }

        // 테마색으로 톤 통일 — 무대 배경 은은하게 물들이고, 확정 버튼도 이 색을 따른다.
        // 0.08은 선형 색공간에서 어두운 판 위에 초록 상자로 보였다(09-28 UI 전수) — 은은하게.
        glow.color = e.Locked
            ? new Color(LockColor.r, LockColor.g, LockColor.b, 0.025f)
            : new Color(accent.r, accent.g, accent.b, 0.035f);
    }

    // ── 나란히 판(10-11 v2) ───────────────────────────────────
    /// <summary>
    /// 나란히 / 넘기기를 고른다. 나란히면 화살표를 판 가장자리 쪽으로 붙여 카드 폭을 벌고, 다 보이니 점 · 「1 / N」은 숨긴다
    /// (리서치 C §4 P2 주 — 다 보이는 화면은 위치 표시가 필요 없다). 넘기기면 프리팹 그대로.
    /// </summary>
    private void ApplyViewMode()
    {
        if (_viewerRow == null && _stage != null && _stage.parent != null) _stage.parent.TryGetComponent(out _viewerRow);
        if (_viewerRow != null)
        {
            if (_viewerPadBase < 0) _viewerPadBase = _viewerRow.padding.left;
            int pad = _multi ? MultiViewerPad : _viewerPadBase;
            _viewerRow.padding = new RectOffset(pad, pad, _viewerRow.padding.top, _viewerRow.padding.bottom);
        }

        if (_multi) EnsureCards(_entries.Count);
        if (_cardsRoot != null) _cardsRoot.gameObject.SetActive(_multi);
        _slider.gameObject.SetActive(!_multi);
        if (_dots != null) _dots.gameObject.SetActive(!_multi);
    }

    /// <summary>후보 수만큼 카드를 갖춘다 — 모자라면 만들고, 남으면 숨긴다. 폭은 앵커로 n등분(사이 16).</summary>
    private void EnsureCards(int n)
    {
        if (_cardsRoot == null)
        {
            _cardsRoot = NewRect("Cards", _stage);
            Stretch(_cardsRoot);
        }
        while (_cards.Count < n) _cards.Add(MakeCard(_cards.Count));

        for (int i = 0; i < _cards.Count; i++)
        {
            bool on = i < n;
            _cards[i].Root.gameObject.SetActive(on);
            if (!on) continue;
            // 앵커로 n등분하고 사이만 오프셋으로 뺀다 — 무대 폭을 몰라도 카드 폭이 같다(i·g/n · (i+1−n)·g/n).
            var rt = _cards[i].Root;
            rt.anchorMin = new Vector2(i / (float)n, 0f);
            rt.anchorMax = new Vector2((i + 1) / (float)n, 1f);
            rt.offsetMin = new Vector2(i * CardGap / n, 0f);
            rt.offsetMax = new Vector2((i + 1 - n) * CardGap / n, 0f);

            // 2열 = 그림 120 · 태그라인 17 두 줄 / 3열 = 그림 84 · 15 네 줄(카드는 한 번 만들어 다시 쓰니 열 수가 바뀔 때마다 고친다)
            bool three = n >= 3;
            var box = _cards[i].IconBox;
            if (box != null)
            {
                float h = three ? CardIconSize3 : CardIconSize;
                box.preferredHeight = h;
                box.minHeight       = Mathf.Min(96f, h);   // 지은 판의 하한 96(BuildStage) — 3열 84는 하한도 같이 내려야 줄어든다(10-11 실측 96 고정 · 아래 넘침 6.5)
            }
            var tag = _cards[i].Tag;
            tag.fontSize = three ? SmallSize : BodySize;
            if (tag.enableAutoSizing) tag.fontSizeMax = tag.fontSize;   // 막 지은 판은 자동 크기가 켜져 있다(최대 = 설계 크기)
            FixHeight(tag.gameObject, three ? TagFourLineH3 : TagTwoLineH);
        }
    }

    /// <summary>
    /// 나란히 카드 한 장 — 넘기기 카드(Slider: 그림 · 이름 · 배지 · 태그라인 · 수치표)를 그대로 복제한다. 구운 프리팹이든 막 지은 판이든
    /// 계층이 같아 따로 짓지 않는다. 다른 점만 고친다 — 그림 칸 120 · 태그라인 두 줄 · 누르면 고름 · 얇은 테.
    /// </summary>
    private CardView MakeCard(int i)
    {
        var go = Instantiate(_slider.gameObject, _cardsRoot, false);
        go.name = "Card" + i;
        go.SetActive(true);
        var rt = (RectTransform)go.transform;
        rt.pivot = new Vector2(0.5f, 0.5f);   // 고른 카드는 가운데를 축으로 1.03배
        rt.localScale = Vector3.one;
        rt.anchoredPosition = Vector2.zero;

        var iconTf = rt.Find("IconBox/Holder/Icon");
        if (iconTf == null) iconTf = rt.Find("IconBox/Icon");
        var c = new CardView
        {
            Root  = rt,
            Glow  = Child<Image>(rt, "Glow"),
            Icon  = iconTf != null ? iconTf.GetComponent<Image>() : null,
            Name  = Child<TMP_Text>(rt, "Name"),
            Badge = Child<TMP_Text>(rt, "Badge"),
            Tag   = Child<TMP_Text>(rt, "Tag"),
            Stats = Child<TMP_Text>(rt, "Stats"),
        };
        if (!go.TryGetComponent(out c.Group)) c.Group = go.AddComponent<CanvasGroup>();
        c.Group.alpha = 1f;

        var box = rt.Find("IconBox");
        if (box != null && box.TryGetComponent<LayoutElement>(out var boxLe)) c.IconBox = boxLe;   // 높이는 EnsureCards가 열 수로 정한다
        c.Tag.textWrappingMode = TextWrappingModes.Normal;   // 카드 폭이 좁다 — 두(3열은 네) 줄까지, 넘치면 말줄임
        c.Tag.overflowMode     = TextOverflowModes.Ellipsis;
        c.Badge.overflowMode   = TextOverflowModes.Ellipsis;
        c.Stats.margin         = new Vector4(CardStatInset, 0f, CardStatInset, 0f);

        // 카드 전체가 누르는 면 — 물든 바탕(Glow)이 카드를 꽉 채운다. 배율 · 색은 이 화면이 직접 굴린다(버튼 전환 · 키보드 이동 없음).
        c.Glow.raycastTarget = true;
        var btn = go.AddComponent<Button>();
        btn.transition    = Selectable.Transition.None;
        btn.navigation    = new Navigation { mode = Navigation.Mode.None };
        btn.targetGraphic = c.Glow;
        int captured = i;
        btn.onClick.AddListener(() => Select(captured));

        c.Edge = BuildCardEdge(rt);
        return c;
    }

    /// <summary>카드 테 네 변 — 카드 안쪽 가장자리에 붙고 레이아웃 · 누름은 받지 않는다. 굵기 · 색은 고름이 바뀔 때 칠한다.</summary>
    private static Image[] BuildCardEdge(RectTransform card)
    {
        var host = NewRect("Edge", card);
        Stretch(host);
        host.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;   // 카드의 세로 레이아웃이 한 줄로 세지 않게
        return new[]
        {
            EdgeSide(host, "Top",    new Vector2(0f, 1f), Vector2.one,         new Vector2(0.5f, 1f)),
            EdgeSide(host, "Bottom", Vector2.zero,        new Vector2(1f, 0f), new Vector2(0.5f, 0f)),
            EdgeSide(host, "Left",   Vector2.zero,        new Vector2(0f, 1f), new Vector2(0f, 0.5f)),
            EdgeSide(host, "Right",  new Vector2(1f, 0f), Vector2.one,         new Vector2(1f, 0.5f)),
        };
    }

    private static Image EdgeSide(RectTransform host, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        var img = NewImage(name, host, UITheme.Thin.LineIdle);
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot     = pivot;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        return img;
    }

    private static void SetEdgeWidth(CardView c, float w)
    {
        c.Edge[0].rectTransform.sizeDelta = new Vector2(0f, w);
        c.Edge[1].rectTransform.sizeDelta = new Vector2(0f, w);
        c.Edge[2].rectTransform.sizeDelta = new Vector2(w, 0f);
        c.Edge[3].rectTransform.sizeDelta = new Vector2(w, 0f);
    }

    private static T Child<T>(Transform root, string path) where T : Component
    {
        var t = root.Find(path);
        return t != null ? t.GetComponent<T>() : null;
    }

    /// <summary>후보를 카드마다 채우고 지금 고른 카드를 바로(연출 없이) 표시한다. 잠긴 후보가 하나라도 있으면 모든 카드가 배지 줄을 둔다.</summary>
    private void ShowCards()
    {
        bool anyLocked = false;
        for (int i = 0; i < _entries.Count; i++) anyLocked |= _entries[i].Locked;

        for (int i = 0; i < _entries.Count; i++)
        {
            var c = _cards[i];
            FillCard(_entries[i], c.Icon, c.Name, c.Badge, c.Tag, c.Stats, c.Glow, reserveBadge: anyLocked);
        }
        ApplySelection(instant: true);
    }

    /// <summary>나란히 판에서 카드를 고른다(카드 클릭 · 좌우 화살표 · ← →). 확정은 [장착하기]만 한다.</summary>
    private void Select(int i)
    {
        if (!_multi || i == _index || i < 0 || i >= _entries.Count) return;
        _index = i;
        Managers.Sound?.PlayUiEvent(SoundEvent.UiSelect);
        ApplySelection(instant: false);
    }

    /// <summary>
    /// 고른 카드 = 금 85% 2px 테 + 1.03배, 나머지 = 알파 0.55 · 잉크 1px 테. 상태가 바뀌는 순간은 0.18초 감속(실시간 — 팝업은 시간정지).
    /// 확정 버튼 · 화살표도 고른 후보에 맞춘다(잠긴 후보를 고르면 [장착하기]가 잠긴다 — 예전과 같다).
    /// </summary>
    private void ApplySelection(bool instant)
    {
        int n = Mathf.Min(_entries.Count, _cards.Count);
        float px = 1f / PanelScale();   // 창이 localScale로 커져 있다 — 나눠야 화면에서 1 · 2px
        for (int i = 0; i < n; i++)
        {
            var c = _cards[i];
            SetEdgeWidth(c, (i == _index ? SelEdgePx : IdleEdgePx) * px);
            _fromAlpha[i] = c.Group.alpha;
            _fromScale[i] = c.Root.localScale.x;
            _fromEdge[i]  = c.Edge[0].color;
        }

        SetArrow(_prev, _index > 0);
        SetArrow(_next, _index < _entries.Count - 1);
        RefreshConfirm(_entries[_index]);

        CancelSelectionTween();
        if (instant) { PaintCards(1f); return; }
        _selCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        SelectionTweenAsync(_selCts.Token).Forget();
    }

    /// <summary>k(0→1)만큼 지금 값에서 목표로 — 고른 카드는 밝게 · 크게 · 금 테, 나머지는 흐리게 · 잉크 테.</summary>
    private void PaintCards(float k)
    {
        int n = Mathf.Min(_entries.Count, _cards.Count);
        for (int i = 0; i < n; i++)
        {
            var c = _cards[i];
            bool on = i == _index;
            c.Group.alpha     = Mathf.Lerp(_fromAlpha[i], on ? 1f : DimAlpha, k);
            c.Root.localScale = Vector3.one * Mathf.Lerp(_fromScale[i], on ? SelScale : 1f, k);
            var edge = Color.Lerp(_fromEdge[i], on ? UITheme.Thin.LineReady : UITheme.Thin.LineIdle, k);
            for (int e = 0; e < c.Edge.Length; e++) c.Edge[e].color = edge;
        }
    }

    private async UniTaskVoid SelectionTweenAsync(CancellationToken ct)
    {
        try { await UIMotion.Tween(UIMotion.Swap, PaintCards, ct); }
        catch (OperationCanceledException) { }
    }

    private void CancelSelectionTween()
    {
        if (_selCts == null) return;
        _selCts.Cancel();
        _selCts.Dispose();
        _selCts = null;
    }

    /// <summary>창 배율(UIWindowFitter가 localScale로 건다) — 선 굵기를 화면 px로 맞출 때 나눈다.</summary>
    private float PanelScale()
    {
        if (_panelRt == null) _panelRt = transform.Find("Panel") as RectTransform;
        return _panelRt != null ? Mathf.Max(0.01f, _panelRt.localScale.x) : 1f;
    }

    /// <summary>
    /// 넘긴 방향에서 카드가 밀려 들어온다 — 오른쪽으로 넘기면 오른쪽에서 슬라이드.
    /// 위치+알파+스케일을 함께 움직여 "물리적으로 넘어왔다"는 방향감을 준다(레이아웃과 안 싸우게
    /// anchoredPosition만 만진다). 시간정지 중이라 unscaled.
    /// </summary>
    private async UniTaskVoid SlideInAsync(int gen, int dir)
    {
        float from = dir >= 0 ? 90f : -90f;   // 오른쪽 넘김 → 오른쪽(+x)에서 진입
        float t = 0f;
        while (t < 1f)
        {
            if (gen != _slideGen) return;
            t += Time.unscaledDeltaTime / SlideTime;
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);   // EaseOutCubic

            _cardGroup.alpha         = e;
            _slider.localScale       = Vector3.one * Mathf.Lerp(0.94f, 1f, e);
            _slider.anchoredPosition = new Vector2(Mathf.Lerp(from, 0f, e), 0f);
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
        }
        _slider.anchoredPosition = Vector2.zero;
    }

    private string StatBlock(WeaponSO w)
    {
        _sb.Clear();
        Row("공격력", w.baseAttack.ToString("0"));
        Row("연사",   w.attackSpeed.ToString("0.0") + " /초");
        Row("사거리", w.attackRange.ToString("0") + " m");
        Row("치명타", w.critChance.ToString("0") + "%");
        return _sb.ToString();
    }

    /// <summary>
    /// 한 줄 = 라벨(왼쪽, 17 · 잉크 60%) + 값(오른쪽, 20 · 잉크). 같은 기준선에 둘을 두려고 라벨 줄 끝에 줄 높이 0을 건다 —
    /// 다음 줄(값)이 같은 높이에서 오른쪽 정렬로 그려지고, 값 줄 끝에서 줄 높이를 되돌린다(TMP line-height 태그).
    /// </summary>
    private void Row(string label, string value)
    {
        if (_sb.Length > 0) _sb.Append('\n');
        _sb.Append("<align=left><size=").Append(StatLabelSize).Append("><color=").Append(StatLabelHex).Append('>')
           .Append(label).Append("</color></size><line-height=0>\n")
           .Append("<align=right>").Append(value).Append("</line-height>");
    }

    /// <summary>수치표를 무대 가운데 360px 폭으로 — 남는 폭은 좌우 여백으로 나눈다(무대가 좁으면 꽉 채운다).</summary>
    private void FitStatTable()
    {
        if (_stats == null) return;
        float m = Mathf.Max(0f, (_stats.rectTransform.rect.width - StatTableW) * 0.5f);
        _stats.margin = new Vector4(m, 0f, m, 0f);
    }

    /// <summary>
    /// 톤 v2 글자(10-11) — 구운 프리팹은 DNF · 가짜 굵게 · 기울임(태그라인)으로 굳어 있어 열 때마다 입힌다.
    /// 제목만 DNF(굵게 없음), 나머지는 Noto 400/500. 글자가 커진 만큼 고정 높이도 늘린다 — 무대가 모자라면 아이콘 칸이 양보한다.
    /// </summary>
    private void ApplyThinStyle()
    {
        var title = transform.Find("Panel/Title")?.GetComponent<TMP_Text>();
        if (title != null)
        {
            title.fontStyle = FontStyles.Normal;
            title.enableAutoSizing = false;
            title.fontSize  = TitleSize;
            title.color     = TitleColor;
        }
        var sub = transform.Find("Panel/Sub")?.GetComponent<TMP_Text>();
        if (sub != null) Thin(sub, UIWeight.Regular, BodySize, SubColor);

        if (_name != null)
        {
            Thin(_name, UIWeight.Medium, NameSize, BodyColor);
            FixHeight(_name.gameObject, 36f);
        }
        if (_badge != null)
        {
            Thin(_badge, UIWeight.Medium, SmallSize, LockColor);
            FixHeight(_badge.gameObject, 22f);
        }
        if (_tag != null)
        {
            Thin(_tag, UIWeight.Regular, BodySize, SubColor);   // 기울임 없앰(P2) · 잉크 60%
            FixHeight(_tag.gameObject, 26f);
        }
        if (_stats != null)
        {
            Thin(_stats, UIWeight.Regular, StatValueSize, BodyColor);
            _stats.lineSpacing = 0f;   // 줄 높이 태그로 짝을 맞춘다 — 줄 간격을 더하면 라벨과 값이 어긋난다
            _stats.richText    = true;
            FixHeight(_stats.gameObject, 120f);   // 4줄 × 20(줄높이 ≈ 29) + 여유
        }
        if (_dots != null && _dots.TryGetComponent<LayoutElement>(out var dle)) dle.preferredHeight = 22f;   // 「1 / N」 15px가 들어갈 높이

        var cancelLabel = transform.Find("Panel/Buttons/Btn_Cancel/L")?.GetComponent<TMP_Text>();
        if (cancelLabel != null) Thin(cancelLabel, UIWeight.Medium, BodySize, BodyColor);
        if (_confirmLabel != null) Thin(_confirmLabel, UIWeight.Medium, 20f, TitleColor);
        foreach (var g in _arrowGlyphs.Values)
            if (g != null) g.fontStyle = FontStyles.Normal;
    }

    private static void Thin(TMP_Text t, UIWeight weight, float size, Color color)
    {
        t.enableAutoSizing = false;   // 단계 밖 크기로 줄지 않게 — 넘치면 말줄임
        UITheme.StyleThinText(t, weight, size, color);
    }

    // ── 상태 표시 ─────────────────────────────────────────────
    private void SetArrow(Button btn, bool on)
    {
        if (btn == null) return;
        btn.interactable = on;

        // 아트 화살표는 알파로, 글리프 화살표는 색으로 비활성을 알린다.
        if (_arrowArts.TryGetValue(btn, out var art) && art != null)
            art.color = on ? Color.white : new Color(1f, 1f, 1f, 0.3f);
        else if (_arrowGlyphs.TryGetValue(btn, out var glyph) && glyph != null)
            glyph.color = on ? ArrowOn : ArrowOff;
    }

    private void UpdateDots(int active)
    {
        for (int i = 0; i < _dotImgs.Count; i++)
            _dotImgs[i].color = i == active ? DotOn : DotOff;
        if (_dotCount != null) _dotCount.text = $"{active + 1} / {_entries.Count}";
    }

    private void RefreshConfirm(Entry e)
    {
        bool ok = !e.Locked;
        if (_confirm != null) { _confirm.interactable = ok; UIAffordGlow.Set(_confirm, ok); }   // 고를 수 있으면 은은한 불(09-29)
        if (_confirmLabel != null)
            _confirmLabel.color = ok ? TitleColor : new Color(TitleColor.r, TitleColor.g, TitleColor.b, 0.35f);
        // 확정 버튼: 아트가 있으면 테마색을 곱하지 않는다(황동 명판이 파랗게 물든다) — 알파로만 잠금을 알린다.
        if (_confirmBg != null)
            _confirmBg.color = _skin?.equipButton != null
                ? (ok ? Color.white : new Color(1f, 1f, 1f, 0.35f))
                : (ok ? UITheme.CtaTint : new Color(0.14f, 0.15f, 0.18f, 1f));
    }

    private void Confirm()
    {
        if (_index < 0 || _index >= _entries.Count)
        {
            Debug.LogWarning($"[RangedForge] 장착 불가 — 표시 중인 후보가 없다(index={_index}, 후보 {_entries.Count}개).");
            return;
        }

        var e = _entries[_index];
        // 잠금 무기(석궁=특전)는 버튼 자체가 비활성이라 여기까지 오지 않는 게 정상이다.
        // 그래도 '눌리지 않는다'는 신고가 오면 원인이 잠금인지 아닌지 바로 갈리도록 남긴다.
        if (e.Locked)
        {
            Debug.LogWarning($"[RangedForge] 장착 불가 — '{e.Weapon?.displayName}'는 잠금({e.LockReason}). 좌우로 넘겨 해금된 무기를 고를 것.");
            return;
        }

        Complete(e.Weapon);
    }

    private void Cancel() => Complete(null);

    private void Complete(WeaponSO result)
    {
        Managers.Sound?.PlayUiEvent(result != null ? SoundEvent.ForgeTake : SoundEvent.UiClose);   // 받기 · 취소
        _tcs?.TrySetResult(result);
        ClosePopupUI();
    }

    // ── 레이아웃 ───────────────────────────────────────────────
    private void BuildLayout()
    {
        // 프리팹이 구워져 있으면 <b>짓지 않고 잇기만 한다</b> — 다시 지으면 UI가 두 벌 겹친다.
        if (transform.childCount > 0) { BindBakedHierarchy(); return; }

        var root = (RectTransform)transform;

        // 프리팹 루트는 화면 한가운데 100×100이다 — 다른 팝업들이 첫 줄에서 루트부터 펴는 이유가 이것이다.
        // 안 펴면 아래 암막이 그 100×100만 덮어 뒤의 월드·HUD가 그대로 살아 있다.
        Stretch(root);

        var dim = NewImage("Dim", root, new Color(0f, 0f, 0f, 0.8f));   // 팝업 공통 암막(ShopUIStyle.Veil과 동일)
        Stretch(dim.rectTransform);
        dim.raycastTarget = true;

        bool skinned = _skin?.panelBackground != null;

        var panel = NewImage("Panel", root, skinned ? Color.white : PanelBg);
        // 세로 레이아웃 그룹이 배치하는 판이라 글꼴은 키우지 않는다 —
        // 상자가 안 커지는데 글자만 키우면 넘친다.
        panel.gameObject.AddComponent<UIWindowFitter>()
             .Configure(scaleFonts: false, maxScale: UIWindowFitter.ContentScreen, scaleTransform: true);
        var prt = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = skinned ? new Vector2(SkinPanelWidth, SkinPanelHeight)
                                : new Vector2(PanelWidth, PanelHeight);

        // 아트에 테두리가 이미 그려져 있다 — Outline을 겹치면 이중 테두리가 된다.
        if (skinned) ShopUIStyle.Skin(panel, _skin.panelBackground);
        else
        {
            var line = panel.gameObject.AddComponent<Outline>();
            line.effectColor = PanelLine;
            line.effectDistance = new Vector2(2f, -2f);
        }

        var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        // 아트 여백은 bg.png의 프레임 안쪽 비율(위 0.100 / 아래 0.067 / 좌우 얇은 금선)에서 뽑았다.
        // 합성본은 제목 위 0.17(775 기준 130)이지만 <b>합성본엔 스탯 4줄이 없다</b> —
        // 코드가 더한 이름·설명·스탯을 담으면 카드가 무대를 97px 넘겨 도트·구분선 위로 흘렀다(2026-09-10 게임 화면).
        // 아트 프레임 안쪽은 위 0.067(52)까지라 96/92는 여전히 프레임 안이다. 그만큼 무대가 넓어진다.
        v.padding = skinned ? new RectOffset(30, 30, 96, 92) : new RectOffset(28, 28, 24, 22);
        v.spacing = 12f;
        v.childControlWidth = true;  v.childForceExpandWidth  = true;
        v.childControlHeight = true; v.childForceExpandHeight = false;
        v.childAlignment = TextAnchor.UpperCenter;

        // 제목 + 안내
        var title = NewText("Title", prt, 30f, TitleColor, FontStyles.Bold, TextAlignmentOptions.Center);
        title.text = "보조 무기 — 원거리";
        var sub = NewText("Sub", prt, 16f, SubColor, FontStyles.Normal, TextAlignmentOptions.Center);
        sub.text = SubText;

        BuildViewer(prt);   // ◀  [카드]  ▶
        BuildDotsRow(prt);
        BuildDivider(prt);
        BuildButtons(prt);
    }

    /// <summary>이름·스탯과 버튼 사이 구분 장식. 아트가 없으면 줄 자체를 만들지 않는다.</summary>
    private void BuildDivider(RectTransform parent)
    {
        if (_skin?.divider == null) return;

        var row = NewRect("Divider", parent);
        FixHeight(row.gameObject, DividerH);

        var img = NewImage("Art", row, Color.white);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(DividerW, DividerH);
        img.raycastTarget = false;
        ShopUIStyle.Skin(img, _skin.divider);
    }

    /// <summary>◀ 화살표 · 카드 무대 · ▶ 화살표 — 가로 3분할.</summary>
    private void BuildViewer(RectTransform parent)
    {
        var row = NewRect("Viewer", parent);
        row.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6f;
        // 합성본에서 화살표는 판 가장자리(0.047)가 아니라 안쪽(0.118)에 선다 — 747 기준 좌우 53.
        h.padding = new RectOffset(53, 53, 0, 0);
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = true;  h.childForceExpandWidth  = false;
        h.childControlHeight = true; h.childForceExpandHeight = true;

        _prev = BuildArrow(row, "◀", -1, _skin?.arrowLeft);

        // 무대 — 레이아웃이 폭을 정하는 자식. 이건 절대 슬라이드로 만지지 않는다.
        var stage = NewRect("Stage", row.transform);
        stage.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        _stage = stage;

        // 슬라이더 — 무대를 꽉 채운 오버레이. 레이아웃 그룹의 자식이 아니라(스트레치) 위치를
        // 마음대로 옮겨도 매 프레임 되돌려지지 않는다. 슬라이드/스케일/알파를 전부 이 위에 건다.
        _slider = NewRect("Slider", stage);
        Stretch(_slider);
        _cardGroup = _slider.gameObject.AddComponent<CanvasGroup>();

        // 무대 배경 — 무기 테마색으로 은은하게 물드는 판(카드보다 뒤).
        _stageGlow = NewImage("Glow", _slider, new Color(1f, 1f, 1f, 0f));
        Stretch(_stageGlow.rectTransform);
        // 각진 사각형이면 알파가 낮아도 '색판'으로 읽힌다(게임 화면에서 초록 판때기로 보였다).
        // 모서리를 둥글리고 가장자리를 흐려 <b>물든 자국</b>처럼 보이게 한다.
        // ⚠️ 9-slice 경계 = 반경 + 소프트다. 기본 96px 스프라이트에 36+28=64를 쓰면 좌우 경계가 서로 겹쳐
        //    슬라이스가 깨진다(가장자리가 안 흐려졌다). 경계 128이 들어가도록 판을 160으로 굽는다.
        _stageGlow.sprite = UIProceduralSprites.RoundedRect(radius: 36f, feather: 28f, size: 160);
        _stageGlow.type   = Image.Type.Sliced;
        _stageGlow.raycastTarget = false;
        // 배경 판이라 세로 레이아웃의 한 칸을 차지하면 안 된다 — 카드 내용이 그만큼 밀려 눌린다.
        _stageGlow.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        BuildCardBody(_slider);

        _next = BuildArrow(row, "▶", +1, _skin?.arrowRight);
    }

    /// <summary>
    /// 구워진 프리팹을 잇는다 — 계층·좌표·아트는 프리팹이 갖고, 코드는 배선만 한다.
    ///
    /// <para>화살표 두 개는 <b>형제 이름이 같다</b>(둘 다 "Arrow"). 순서로 가른다 —
    /// 빌더가 왼쪽(-1)을 먼저 만들므로 프리팹에서도 그 순서로 굳어 있다.</para>
    /// </summary>
    private void BindBakedHierarchy()
    {
        _skin = UISkin.WeaponForge;

        _arrowGlyphs.Clear();
        _arrowArts.Clear();

        var row = _stage != null ? _stage.parent : null;
        int dir = -1;
        if (row != null)
        {
            foreach (Transform child in row)
            {
                if (child.name != "Arrow") continue;
                if (!child.TryGetComponent<Button>(out var arrow)) continue;

                int captured = dir;
                arrow.onClick.RemoveAllListeners();
                arrow.onClick.AddListener(() => Step(captured));

                var glyph = child.Find("G")?.GetComponent<TMP_Text>();
                if (glyph != null) _arrowGlyphs[arrow] = glyph;
                var art = child.Find("Art")?.GetComponent<Image>();
                if (art != null) _arrowArts[arrow] = art;

                if (captured < 0) _prev = arrow; else _next = arrow;
                dir = 1;
            }
        }

        if (_confirm != null)
        {
            _confirm.onClick.RemoveAllListeners();
            _confirm.onClick.AddListener(Confirm);
        }

        var cancel = transform.Find("Panel/Buttons/Btn_Cancel")?.GetComponent<Button>();
        if (cancel != null)
        {
            cancel.onClick.RemoveAllListeners();
            cancel.onClick.AddListener(Cancel);
            FrameCancel(cancel.targetGraphic as Image);
        }

        // 부제 — 구운 글은 옛 이름(「무명의 형상」) · 다시 고르기 안내가 없다(10-19 ⑦)
        foreach (var t in GetComponentsInChildren<TMP_Text>(true))
            if (t.name == "Sub") { t.text = SubText; break; }
    }

    /// <summary>
    /// 취소 버튼에 옅은 둥근 테두리 — 틀 없는 회색 상자라 틀 있는 [장착하기] 옆에서 다른 화면 조각처럼 보였다(09-28 UI 전수).
    /// 코드로 구운 스프라이트는 프리팹에 안 남으므로 쓰는 곳에서 한 번 붙인다.
    /// </summary>
    private static void FrameCancel(Image bg)
    {
        if (bg == null || bg.transform.Find("Frame") != null) return;
        bg.sprite = UIProceduralSprites.RoundedRect(radius: 6f, feather: 1f);
        bg.type   = Image.Type.Sliced;
        var frame = NewImage("Frame", bg.rectTransform, CancelEdge);
        Stretch(frame.rectTransform);
        frame.sprite = UIProceduralSprites.RoundedOutline(radius: 6f, stroke: 1.5f);
        frame.type   = Image.Type.Sliced;
        frame.raycastTarget = false;
    }

    private static readonly Color CancelEdge = new(0.66f, 0.54f, 0.34f, 0.75f);

    /// <summary>
    /// 화살표 버튼. 아트가 있으면 판정면(box)은 투명하게 두고 <b>자식 이미지</b>에 아트를 얹는다 —
    /// box는 레이아웃이 세로로 늘리는 칸이라 거기 아트를 직접 넣으면 화살표가 세로로 늘어난다.
    /// 비활성 표시는 아트면 알파, 아니면 글리프 색으로 한다(SetArrow).
    /// </summary>
    private Button BuildArrow(RectTransform parent, string glyph, int dir, Sprite art)
    {
        var box = NewImage("Arrow", parent, new Color(1f, 1f, 1f, art != null ? 0f : 0.04f));
        var le = box.gameObject.AddComponent<LayoutElement>();
        float w = art != null ? ArrowW + 10f : 52f;
        le.preferredWidth = w; le.minWidth = w;

        var btn = box.gameObject.AddComponent<Button>();
        btn.targetGraphic = box;
        btn.onClick.AddListener(() => Step(dir));

        var t = NewText("G", box.rectTransform, 34f, ArrowOn, FontStyles.Bold, TextAlignmentOptions.Center);
        t.text = glyph;
        Stretch(t.rectTransform);
        _arrowGlyphs[btn] = t;

        if (art != null)
        {
            t.gameObject.SetActive(false);   // 아트에 화살촉이 그려져 있어 글리프는 중복이다

            var img = NewImage("Art", box.rectTransform, Color.white);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(ArrowW, ArrowH);
            img.raycastTarget = false;
            ShopUIStyle.Skin(img, art);
            _arrowArts[btn] = img;
        }

        return btn;
    }

    private void BuildCardBody(RectTransform stage)
    {
        var v = stage.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 10f;
        v.padding = new RectOffset(6, 6, 6, 6);
        v.childAlignment = TextAnchor.MiddleCenter;   // 합성본은 홀더가 무대 세로 중앙(0.54)에 온다
        v.childControlWidth = true;  v.childForceExpandWidth  = true;
        v.childControlHeight = true; v.childForceExpandHeight = false;

        // 무대에 들어갈 세로 예산이 카드 내용보다 작으면 VerticalLayoutGroup이 자식을 최소높이까지
        // 눌러버린다. TMP의 최소높이는 0이라 글자는 그대로 그려지면서 칸만 사라져 서로 겹쳐 보였다
        // (이름·설명·스탯이 한 덩어리로 뭉치던 원인). 그래서 각 줄에 높이를 못 박는다.
        bool hasHolder = _skin?.holderFill != null;
        var iconBox = NewRect("IconBox", stage);
        // 글자 줄은 높이를 못 박지만(눌리면 겹친다) <b>아이콘 칸만은 줄어들 수 있어야 한다</b> —
        // 무대가 좁을 때 누군가는 양보해야 하고, 양보해도 정보가 사라지지 않는 것은 아이콘뿐이다.
        // min < preferred면 VerticalLayoutGroup이 이 칸에서만 모자란 만큼을 뺀다.
        var iconLe = iconBox.gameObject.AddComponent<LayoutElement>();
        iconLe.preferredHeight = hasHolder ? HolderSize : 130f;
        iconLe.minHeight       = 96f;
        iconLe.flexibleHeight  = 0f;

        // 아트 홀더는 정사각형이라 폭 전체를 쓰면 안 된다 — 가운데 정사각 칸을 따로 만든다.
        RectTransform host = iconBox;
        if (hasHolder)
        {
            host = NewRect("Holder", iconBox);
            // 칸을 꽉 채우되 정사각을 지킨다(AspectRatioFitter). 고정 크기로 두면 칸이 줄어도
            // 홀더는 그대로라 아래 글자와 겹친다.
            Stretch(host);
            var fit = host.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode  = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 1f;

            var fill = NewImage("Fill", host, Color.white);
            Stretch(fill.rectTransform);
            fill.raycastTarget = false;
            ShopUIStyle.Skin(fill, _skin.holderFill);
        }

        _icon = NewImage("Icon", host, Color.white);
        _icon.enabled = false;   // 항목이 적용되기 전엔 그리지 않는다 — 빈 Image는 흰 네모로 뜬다
        // 홀더 안에서는 테두리를 먹지 않도록 안쪽으로 들여넣는다.
        if (hasHolder) ShopUIStyle.Stretch(_icon.rectTransform, 22f);
        else           Stretch(_icon.rectTransform);
        _icon.preserveAspect = true;
        // 카드는 표시 전용이다 — 누를 수 있는 건 좌우 화살표와 하단 버튼뿐.
        // 레이캐스트를 켜두면 카드가 조금만 넘쳐도 그 아래 버튼을 덮어 '안 눌리는' 상태가 된다.
        _icon.raycastTarget = false;

        // 테두리·외곽선은 아이콘 위로 지나가야 액자가 된다(원본 비율대로 조금씩 크다).
        // 채움이 없으면 칸 크기가 홀더 기준이 아니라서 겹쳐봐야 어긋난다 — 세트로만 얹는다.
        if (hasHolder)
        {
            AddHolderOverlay(host, "Frame",   _skin.holderFrame,   HolderFrameScale);
            AddHolderOverlay(host, "Outline", _skin.holderOutline, HolderOutlineScale);
        }

        // 이름 + 잠금 배지
        _name = NewText("Name", stage, 26f, TitleColor, FontStyles.Bold, TextAlignmentOptions.Center);
        _name.textWrappingMode = TextWrappingModes.NoWrap;
        _name.overflowMode     = TextOverflowModes.Ellipsis;
        FixHeight(_name.gameObject, 34f);

        _badge = NewText("Badge", stage, 15f, LockColor, FontStyles.Bold, TextAlignmentOptions.Center);
        _badge.textWrappingMode = TextWrappingModes.NoWrap;
        FixHeight(_badge.gameObject, 20f);

        // 한 줄 설명
        _tag = NewText("Tag", stage, 16f, SubColor, FontStyles.Normal, TextAlignmentOptions.Center);   // 글자 하한 · 기울임 없음(10-11)
        _tag.textWrappingMode = TextWrappingModes.NoWrap;
        _tag.overflowMode     = TextOverflowModes.Ellipsis;
        FixHeight(_tag.gameObject, 22f);

        // 스탯 블록(4줄 세로 정렬)
        _stats = NewText("Stats", stage, 16f, BodyColor, FontStyles.Normal, TextAlignmentOptions.Center);
        _stats.lineSpacing = 6f;
        FixHeight(_stats.gameObject, 92f);   // 4줄 × (16 + 줄간격 6) = 88 + 여유
    }

    /// <summary>부모 한가운데 고정 크기 칸을 만든다(레이아웃이 늘리지 못하게).</summary>
    private static void CenterBox(RectTransform rt, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = Vector2.zero;
    }

    /// <summary>홀더 위에 겹치는 테두리/외곽선. 아트가 없으면 만들지 않는다.</summary>
    private static void AddHolderOverlay(RectTransform host, string name, Sprite art, float scale)
    {
        if (art == null) return;

        var img = NewImage(name, host, Color.white);
        // 홀더보다 scale배 크게 — <b>앵커를 0~1 밖으로</b> 벌려 홀더 크기가 바뀌어도 비율이 유지된다.
        // 고정 크기(CenterBox)로 두면 홀더가 줄어들 때 테두리만 옛 크기로 남아 액자가 어긋난다.
        float over = (scale - 1f) * 0.5f;
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(-over, -over);
        rt.anchorMax = new Vector2(1f + over, 1f + over);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        img.raycastTarget = false;
        ShopUIStyle.Skin(img, art);
    }

    /// <summary>레이아웃 그룹이 눌러도 줄지 않도록 높이를 고정한다(min=preferred, flexible=0).</summary>
    private static void FixHeight(GameObject go, float height)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minHeight       = height;
        le.preferredHeight = height;
        le.flexibleHeight  = 0f;
    }

    private void BuildDotsRow(RectTransform parent)
    {
        _dots = NewRect("Dots", parent);
        _dots.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;
        var h = _dots.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = true;  h.childForceExpandWidth  = false;
        h.childControlHeight = true; h.childForceExpandHeight = false;
    }

    private void BuildDots()
    {
        for (int i = 0; i < _dotImgs.Count; i++) Destroy(_dotImgs[i].gameObject);
        _dotImgs.Clear();

        for (int i = 0; i < _entries.Count; i++)
        {
            var dot = NewImage("Dot", _dots, DotOff);
            dot.sprite = UIProceduralSprites.Circle(32);   // 코드로 그린 원 — 넘겨 보는 위치 표시(10 → 14px, P2)
            dot.raycastTarget = false;
            var le = dot.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = DotSize; le.preferredHeight = DotSize;
            le.minWidth = DotSize; le.minHeight = DotSize;
            _dotImgs.Add(dot);
        }
        EnsureDotCount().SetAsLastSibling();
    }

    /// <summary>점 오른쪽 「1 / N」(15 · 잉크 60%) — 구운 프리팹엔 없어 처음 쓸 때 만든다.</summary>
    private RectTransform EnsureDotCount()
    {
        if (_dotCount == null)
        {
            var found = _dots.Find("Count");
            _dotCount = found != null ? found.GetComponent<TMP_Text>() : null;
        }
        if (_dotCount == null)
        {
            _dotCount = NewText("Count", _dots, SmallSize, SubColor, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            _dotCount.textWrappingMode = TextWrappingModes.NoWrap;
            _dotCount.margin = new Vector4(6f, 0f, 0f, 0f);
            var le = _dotCount.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 56f; le.minWidth = 56f; le.preferredHeight = 22f;
            Thin(_dotCount, UIWeight.Regular, SmallSize, SubColor);
        }
        return _dotCount.rectTransform;
    }

    private void BuildButtons(RectTransform parent)
    {
        bool skinned = _skin?.equipButton != null;

        var row = NewRect("Buttons", parent);
        FixHeight(row.gameObject, skinned ? EquipH : 56f);   // 남는 세로를 버튼이 먹어 커지지 않게 고정
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 14f;
        h.childAlignment = TextAnchor.MiddleCenter;
        // 아트 버튼은 원본 비율이 있어 늘리면 구워진 글자가 깨진다 — 폭을 각자 고정한다.
        h.childControlWidth = true;  h.childForceExpandWidth  = !skinned;
        h.childControlHeight = true; h.childForceExpandHeight = true;

        _confirmBg = NewImage("Btn_Confirm", row, skinned ? Color.white : new Color(0.16f, 0.30f, 0.44f, 1f));
        _confirm = _confirmBg.gameObject.AddComponent<Button>();
        _confirm.targetGraphic = _confirmBg;
        _confirm.onClick.AddListener(Confirm);

        _confirmLabel = NewText("L", _confirmBg.rectTransform, 22f, TitleColor, FontStyles.Bold, TextAlignmentOptions.Center);
        _confirmLabel.text = "확정";
        Stretch(_confirmLabel.rectTransform);

        if (skinned)
        {
            ShopUIStyle.Skin(_confirmBg, _skin.equipButton);
            FixWidth(_confirmBg.gameObject, EquipW);
            // 아트에 "장착하기"가 구워져 있다 — 코드 라벨을 남기면 두 벌이 겹친다.
            _confirmLabel.gameObject.SetActive(false);

            if (_skin.equipGlyph != null)
            {
                var glyph = NewImage("Glyph", _confirmBg.rectTransform, Color.white);
                var grt = glyph.rectTransform;
                grt.anchorMin = grt.anchorMax = new Vector2(0f, 0.5f);
                grt.pivot = new Vector2(0f, 0.5f);
                grt.sizeDelta = new Vector2(22f, 22f);
                grt.anchoredPosition = new Vector2(24f, 0f);
                glyph.raycastTarget = false;
                glyph.preserveAspect = true;
                ShopUIStyle.Skin(glyph, _skin.equipGlyph);
            }
        }
        else
        {
            var cl = _confirmBg.gameObject.AddComponent<Outline>();
            cl.effectColor = new Color(Accent.r, Accent.g, Accent.b, 0.55f);
            cl.effectDistance = new Vector2(1.5f, -1.5f);
        }

        // 취소 — 완성본엔 없지만 이 팝업은 ESC로도 못 닫아서(CloseOnEscape=false) 유일한 탈출구다.
        // 전용 아트가 없으므로 주 버튼보다 작고 수수하게 둔다.
        var cancelBg = NewImage("Btn_Cancel", row, new Color(0.16f, 0.16f, 0.19f, 0.92f));
        var cancel = cancelBg.gameObject.AddComponent<Button>();
        cancel.targetGraphic = cancelBg;
        cancel.onClick.AddListener(Cancel);
        if (skinned) FixWidth(cancelBg.gameObject, 110f);
        var t = NewText("L", cancelBg.rectTransform, skinned ? 18f : 22f, BodyColor, FontStyles.Bold, TextAlignmentOptions.Center);
        t.text = "취소";
        Stretch(t.rectTransform);
        FrameCancel(cancelBg);
    }

    /// <summary>레이아웃 그룹이 늘리지 못하게 폭을 고정한다(min=preferred, flexible=0).</summary>
    private static void FixWidth(GameObject go, float width)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minWidth       = width;
        le.preferredWidth = width;
        le.flexibleWidth  = 0f;
    }

    // ── 생성 헬퍼 ─────────────────────────────────────────────
    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    private TMP_Text NewText(string name, Transform parent, float size, Color color,
                             FontStyles style, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.color = color;
        t.fontStyle = style;
        t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        // 글자가 판 밖으로 나가지 않게 하는 안전망 — TMP 기본 넘침은 잘라내지 않고 <b>바깥에 그린다</b>.
        // 최대를 설계 크기로 묶으므로 커지지는 않고, 안 들어갈 때만 줄어든다.
        t.enableAutoSizing = true;
        t.fontSizeMax      = size;
        t.fontSizeMin      = Mathf.Max(9f, size * 0.55f);
        ApplyReadableMaterial(t);
        return t;
    }

    private static void ApplyReadableMaterial(TMP_Text t)
    {
        if (t.font == null) return;
        if (s_textMat == null)
        {
            var src = t.fontSharedMaterial;
            if (src == null) return;
            s_textMat = new Material(src) { name = src.name + " (RangedForge)" };
            s_textMat.EnableKeyword("OUTLINE_ON");
            s_textMat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.07f);
            s_textMat.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.03f, 0.03f, 0.05f, 1f));
            s_textMat.SetFloat(ShaderUtilities.ID_FaceDilate, 0.06f);
        }
        t.fontSharedMaterial = s_textMat;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
