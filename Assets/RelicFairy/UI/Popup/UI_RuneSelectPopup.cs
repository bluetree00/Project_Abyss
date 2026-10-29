using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 룬 선택 팝업 (Canvas_Popup, Addressable "UI_RuneSelectPopup").
///
/// 룬 획득 = <b>후보 N개 중 1개 선택</b>. 예전 단일 알림 팝업(09-21 폐기)은
/// "고르는" 결정이 없었다 — 이 팝업이 매 방 보상을 결정으로 바꾼다.
///
/// 카드 구성(10-06 개편 — 의뢰서 N3~N9 「모양이 1순위」 · 시안 바탕화면 기획/룬획득·유물기억_팝업개편_시안_20261006):
///  - 맨 위 <b>속성 리본</b> · 등급 글자 + 속성 칩
///  - <b>룬 실물 판</b> — 납품 속성 조각으로 모양을 크게(빈 자리는 옅은 테). 뒤집히는 순간 칸이 하나씩 끼워진다.
///  - 이름 / (발동 계열 칩) / 효과 칸(숫자 금색) / <b>룬 수 한 줄</b>(글자만) — 상한을 넘으면 붉게. 경고일 뿐 선택은 막지 않는다.
///  - <b>테 = 등급</b>: 등급색 선(두께 1~2) + 영웅 이상만 서약 카드의 보석 테두리(얇게 깎아 0.5배) + 전설만 바깥 빛. 뒷면의 문장 색도 등급이다.
///  - 카드 줄 오른쪽 = <b>지금 룬판</b>(<see cref="RuneBoardMini"/>). 후보는 3장 고정이라(10-01) 네 번째 자리를 판이 쓴다.
///  - 고른 카드 = 금 1px 테 + 모서리 눈금(<see cref="UIThinFrame"/> 켜짐 연출) + 1.03배 — 표시는 이것 하나다(10-11 v2).
///
/// 10-11 v2(비교 리서치 B §2): 창 맞춤 상한 1.3(화면 88% → 약 55%) · 글자 Noto(가짜 굵게 · 기울임 없음).
/// 구운 프리팹이라 창 · 제목 · 버튼은 <see cref="ApplyThinChrome"/>가 쓰는 곳에서 덮어쓴다(재굽기 없이 반영).
///
/// 공개 연출: 카드가 뒷면으로 내려오고 → 낮은 등급부터 뒤집힌다 → 전설은 한 번 멈췄다가 연다 →
/// 고른 카드는 보관함 카운터로 날아간다. 전부 unscaled UI 트윈이고(팝업 중 timeScale 0), 아무 입력이나 들어오면 끝 상태로 넘어간다.
/// </summary>
public sealed class UI_RuneSelectPopup : UI_Popup
{
    public override bool BlocksGameplay => true;
    public override bool CloseOnEscape  => false;   // 보상 결정이라 실수로 닫히면 안 된다 — 선택/넘기기로만 종료

    // ── 레이아웃 ──
    // 배경 아트는 9슬라이스가 아니라 통짜로 늘어난다(Skin(window, background) — sliced 아님).
    // 따라서 크기는 원본 비율 2081:1241(=1.6774)을 지켜야 왜곡되지 않는다.
    // 의뢰서 「정제소_룬획득」 06: 창 1100×620 · 카드 300×380 × 3 · 선택 300×56 / 넘기기 180×56.
    // 카드는 효과 4행(16px 줄바꿈 허용)을 담느라 420으로 40 늘렸다 — 창 620 안에 제목 60 + 카드 420 + 버튼 56이 들어간다.
    private const float WindowW = 1250f;   // 10-19 「넓게 써야 여유롭다」 → 「옆으로 너무 늘면 안 좋다」 → 10-20 「테두리끼리 붙으면 애매 — 여유 있게 · 배경을 키워도 좋다」: 1250×740(배율 상한 1.3 · 1080p 85% × 89% · 카드 폭 267 그대로)
    private const float WindowH = 740f;
    private const float CardW   = 300f;   // 카드 폭 상한 — 후보가 많으면 이 아래로 줄어든다
    private const float CardH   = 480f;   // 의뢰서 380 + 효과 4행분 40 · 10-19 +60(세로로 넓힌 창 — 효과 칸 · 폭 266과 9:16 틀 비율)
    private const int   CardSlots = 3;    // 룬 선택지는 3장 고정(10-01) — 카드 폭은 늘 3장 자리 기준(2장이어도 늘어나지 않고 가운데)
    private const float CardGap = 36f;   // 카드 사이 · 카드–미니 판(10-20 24 → 36 — 테두리끼리 붙지 않게)
    private const float SkipGuardSec = 0.4f;   // 공개 연출 중에 누른 클릭을 뗀 직후 — 넘기기로 읽지 않는 시간(10-18)
    private const float CardSideMargin = 56f;   // 카드 열 좌우 여백(창 안쪽 · 10-20 40 → 56 — 돌 틀 안쪽 선에서 떨어지게)
    // 「지금 룬판」 — 카드 줄 오른쪽 끝, 카드와 같은 높이. 13열 판이 칸 13.7px로 들어가는 폭이다(카드는 240으로 준다).
    private const float BoardPanelW = 228f;
    // 「셋 중 하나」 팝업 창 맞춤 상한(10-11 v2) — 컨텐츠 화면 상한(3)이면 1.64배로 커져 화면 88%를 덮었다. 1.3 = 약 55%(유물 진화와 같다).
    private const float FitMaxScale = 1.3f;
    // [선택] · [넘기기] — 목업 px(창 아래 가운데 기준). 선택 220×44(리서치 B §2 P3) · 넘기기도 같은 높이 · 두 버튼 사이 23.
    private static readonly Vector2 ConfirmBtnSize = new(220f, 44f);
    private static readonly Vector2 SkipBtnSize    = new(180f, 44f);
    private const float BtnBottom = 52f;   // 10-20 44 → 52 — 아래 돌 틀에서 떨어지게
    private const float BtnGap    = 23f;

    // 글자(10-11 v2 — Noto · 가짜 굵게 없음, 목업 px): 제목 300 24 · 이름 500 22 · 효과 400 18 · 버튼 500 20 · 보조 17 · 라벨 15.
    private const float TitleSize  = 24f;
    private const string TitleText = "룬 획득 — 하나를 고른다";
    private const float NameSize   = 22f;
    private const float NameH      = 32f;    // Noto 줄 높이(≈1.45em) — DNF에 맞춘 28이면 자동 크기가 글자를 줄인다
    private const float EffectSize = 18f;
    private const float EffectRowH = 26f;
    private const float ButtonSize = 20f;
    private const float SubSize    = 17f;
    private const float LabelSize  = 15f;

    // 등급 확률 막대 — 창 좌하단, [선택]/[넘기기] 좌측 여백에 앉힌다.
    private const float OddsBarW      = 236f;
    private const float OddsBarH      = 78f;
    private const float OddsBarMargin = 8f;
    private const float WindowFrameCorner = 36f;   // 힉스필드 창 틀 9칸 모서리(목업 px · 10-19)
    private const float InteriorInset     = 18f;   // 10-29 창 안쪽 그림 — 틀의 돌 테(≈21) 밑으로 조금 들어가 이음매가 안 보이게
    private static readonly Color ShowcaseTint = new(0.78f, 0.78f, 0.82f, 1f);
    private const float CardFrameScale    = 3.4f;  // 힉스필드 등급 카드 틀 — 그림 px ÷ 이 값 = 카드 목업 px(테 얇게 · 10-19)
    private const float CardY   = 30f;    // 카드 위 100 / 아래 160(버튼 줄 + 여백) — 창 중심 기준(10-20 740 · 480)

    // 카드 앞면 배치(카드 위에서부터 — 10-06 개편: 리본 / 등급 · 속성 / 룬 실물 판 / 이름 / (계열) / 효과 칸 / 배지)
    private const float RaysFrac        = 1.15f;  // 전설 광선 = 판 폭의 배수
    private const float RibbonY         = 9f;     // 속성 리본(카드 맨 위 한 줄)
    private const float RibbonH         = 8f;
    private const float HeadY           = 22f;    // 등급 글자 · 속성 칩 줄
    private const float ChipH           = 24f;
    private const float PanelY          = 52f;    // 룬 실물 판 — 카드의 3분의 1 남짓(의뢰서 「모양이 1순위」)
    private const float PanelH          = 146f;
    private const float PanelInset      = 12f;
    private const float CountRowH       = 18f;
    private const float PanelRefCardW   = 240f;   // 모양 판 높이 기준 카드 폭(3장) — 이보다 좁으면 판을 비례해 낮춘다(10-19)    // 판 아래 「N칸」 글자 자리
    private const float CellMax         = 46f;    // 한 칸 상한 — 1칸 룬이 판을 다 먹지 않게
    private const float CellGap         = 5f;
    private const float NameY           = 206f;
    private const float NameIcon        = 34f;    // 이름 왼쪽 룬 그림(10-10)
    private const float NameIconGap     = 6f;
    private const float FamilyY         = 238f;   // 발동 계열 칩(있을 때만)
    private const float FamilyRowH      = 28f;
    private const float EffectsTop      = 240f;
    private const float BadgeH          = 24f;
    private const float BadgeBottom     = 12f;
    private const float MinEffectFont   = 12f;    // 넘칠 때 줄이는 하한
    private const float AssembleStagger = 0.035f; // 뒤집히는 순간 칸이 하나씩 끼워진다
    private const float AssembleDur     = 0.14f;
    private const float HaloPad         = 8f;     // 전설 바깥 빛이 카드 밖으로 번지는 폭(10-11 v2 「후광 8px 안팎」)
    private const float HaloRingEdge    = 24f;    // 고리 스프라이트 경계 px — HaloPad로 줄여 그린다(pixelsPerUnitMultiplier)
    private const float HaloAlpha       = 0.5f;   // 전설만(10-11 v2 — 희귀 0.55 · 영웅 0.70 후광은 걷었다)
    // 서약 보석 테두리 조각(모서리 53×88 · 장식바 187×52)을 0.5배로, 영웅 이상만 — 선은 얇게 깎은 사본(10-11 v2 「장식은 남기되 얇게」).
    private const float GemCornerW  = 26.5f;
    private const float GemBarW     = 93.5f;
    private const float GemOutset   = 3f;     // 모서리가 카드 모서리를 살짝 감싼다
    private const float SelectGap   = 4f;     // 고른 표시 금 테는 카드 밖 4px — 등급색 선과 겹치지 않게

    // ── 공개 연출(시안 타임라인) ──
    private const float VeilInDur      = 0.25f;
    private const float DealDelay      = 0.12f;
    private const float DealStagger    = 0.07f;
    private const float DealDur        = 0.32f;
    private const float DealRise       = 60f;    // 위에서 내려온다
    private const float FlipLead       = 0.26f;  // 마지막 카드가 내려앉은 뒤 첫 뒤집기까지
    private const float FlipStagger    = 0.12f;
    private const float FlipHalf       = 0.11f;
    private const float BriefStagger   = 0.04f;
    private const float LegendHold     = 0.55f;
    private const float LegendLift     = 1.07f;   // 더 키우면 위 제목 줄을 덮는다(카드 위 여백 68)
    private const float LegendRise     = 0f;
    private const float LegendVeil     = 0.92f;
    private const float LegendDim      = 0.45f;
    private const float ShakeDur       = 0.30f;
    private const float ShakeAmp       = 3f;
    private const int   SparkCount     = 24;
    private const float SparkDur       = 0.8f;
    private const float ScreenFlashDur = 0.25f;
    private const float BurstDur       = 0.45f;
    private const float FlyDur         = 0.28f;
    // 10-19 등급별 공개 강조(틀 선 · 보석 빛 · 영웅 떨림)
    private const float EdgeFlareDur    = 0.55f;
    private const float CornerFlareDur  = 0.5f;
    private const float AnticipateDur   = 0.22f;  // 영웅 — 뒤집기 전 떨림(흔들림은 「살짝」 · 1.5px)
    private const float AnticipateAmp   = 1.5f;
    private const float EpicHold        = 0.12f;  // 영웅이 뒤집힌 뒤 다음 카드까지 조금 더
    private const int   EpicSparkCount  = 8;
    private const float FrameLineArtPx  = 38f;    // 등급 카드 틀 그림에서 안쪽 빛 선까지(그림 px — 네 등급 같음)
    private const float RaysTurnSec    = 14f;
    private const float BobPeriod      = 3.2f;
    private const float BobAmp         = 4f;

    // 청 수정 동굴 바탕을 인디고로 누른다 — 다른 화면(인디고 글래스)과 온도를 맞추고 그림은 남긴다(톤 통일 ④, 09-29).
    private static readonly Color CaveTint      = new(0.66f, 0.62f, 0.80f, 1f);
    private static readonly Color CardBorderArt = new(0.62f, 0.62f, 0.66f, 1f);   // 카드 테 아트 — 고름과 무관하게 한 색(고른 표시는 금 테 하나)
    private static readonly Color NoColor       = new(0.92f, 0.40f, 0.33f, 1f);
    private static readonly Color NoteAmber     = new(0.91f, 0.73f, 0.33f, 1f);   // #E8BA54 — 「알아 둘 것」(막힘이 아님 · 10-19)
    private static readonly Color ChipFill      = new(0.05f, 0.06f, 0.10f, 0.72f);
    private static readonly Color BackFill      = new(0.045f, 0.055f, 0.09f, 1f);
    private static readonly Color LegendGold    = new(1f, 0.84f, 0.47f, 1f);

    // 코드로 그리는 빛 — 부드러운 원·회전 광선·기둥. 아트 없이 한 번 만들어 모든 팝업이 같이 쓴다.
    private static Sprite _softDot, _rays, _beam, _haloRing;

    // ── 상태 ──
    private readonly List<CardView> _cards = new();
    private RectTransform _boardPanel;   // 「지금 룬판」 자리(카드 레이어의 자식)
    private CanvasGroup   _boardGroup;
    private RuneBoardMini _board;
    private List<(RuntimeItemData data, ItemSO so)> _candidates;
    private RunItemInventory _inventory;
    private const float SelectedCardScale   = 1.03f;   // 선택 카드 확대(10-11 v2 1.05 → 1.03 — 금 테가 주인공)
    private const float UnselectedCardAlpha = 0.55f;   // 비선택 카드 감광

    private int _selected = -1;
    private float _cardW = CardW;   // 후보 수에 맞춰 산출된 실제 카드 폭
    private bool _built;
    private bool _skinned;   // 아트 로드 성공 — 선택 피드백을 색 틴트 대신 밝기로 처리
    private bool _closing;   // 선택 확정 후 날아가는 중 — 두 번 눌러도 한 번만 처리
    private bool _themedButtons;   // 공통 베벨 버튼을 입혔다 — [선택] 색을 금/흐린 금으로

    private RectTransform _cardLayer;
    private Image _veil;
    private float _veilAlpha = -1f;   // 구운 막의 본래 알파
    private Image _pillar;
    private RectTransform _fxRoot;

    private UniTaskCompletionSource _interactionTcs;

    [SerializeField] private TMP_Text _counterText;
    [SerializeField] private Image    _confirmBtnImg;
    [SerializeField] private TMP_Text _confirmLabel;
    [SerializeField] private Image    _screenFlash;      // Legendary 전체화면 플래시 오버레이(코드 생성 Image 1장)

    /// <summary>공개 시퀀스 진행 중인가 — 아무 입력이 들어오면 즉시 스냅한다.</summary>
    private bool _revealing;
    private float _revealEndedAt = float.NegativeInfinity;   // 공개 연출이 끝난(또는 건너뛴) 실시간 — 그 누름이 뗀 클릭을 넘기기로 읽지 않는다
    private int _refinerySpent = -1;                          // 정제소에서 열었으면 쓴 원석(10-26부터 넘겨도 안 돌려준다) · 아니면 -1
    private TMP_Text _skipHint;
    private bool _openBoard = true;   // 고른 뒤 룬판을 연다 — 챌린지 다중 보상은 마지막 라운드만(10-19)
    private bool _briefReveal;        // 챌린지 2라운드부터 짧은 공개 연출(10-19)

    /// <summary>넘기면 받는 원석 — 보이는 카드 중 가장 높은 분해 값(10-19 · 옛 +1은 「골라서 분해」보다 늘 손해였다). 정제소 창은 쓴 원석 환불이 따로.</summary>
    public int SkipOre { get; private set; } = ClearRewardTrigger.SkipOreReward;

    /// <summary>보관함이 가득 — 고르면 넘침 칸에 들어가 룬판에서 정리한다. 붉은 「못 얻음」이 아니라 두 길(바꾸기 · 분해)로 말한다(10-19 사용자).</summary>
    private bool StagingFull => _inventory != null && _inventory.IsStagingFull;
    private string ConfirmText => StagingFull ? "룬판에서 바꾸기" : "선택";
    /// <summary>스킵 요청됨 — 진행 중 트윈을 최종 상태로 확정한다(파괴가 아니라 완료).</summary>
    private bool _revealSkipped;

    /// <summary>선택된 룬. 넘겼으면 null.</summary>
    public RuntimeItemData Result { get; private set; }
    /// <summary>넘기기로 종료했는지(넘기기 보상 지급 판단용).</summary>
    public bool Skipped { get; private set; }

    private sealed class CardView
    {
        public Image         Border;
        public Image         Fill;
        public GameObject    Root;
        public RectTransform Rt;
        public CanvasGroup   Group;
        public Vector2       BasePos;
        public ItemRarity    Rarity;
        public GameObject    Face;
        public GameObject    Back;
        public Image         Glow;
        public RectTransform Icon;       // 전설은 이게 떠오른다(Update) — 지금은 모양 묶음
        public Vector2       IconBase;
        public RectTransform Rays;
        public readonly List<RectTransform> Cells = new();   // 뒤집힐 때 하나씩 끼워지는 모양 칸
        public UIThinFrame   Select;     // 고른 표시 — 금 1px 테 + 모서리 눈금(평소 꺼 둔다)
        public bool          RarityArt;  // 힉스필드 등급 카드 틀을 깔았다 — 등급은 틀이 말한다(10-19)
        public float         FrameInset; // 그 틀의 9칸 모서리(목업 px) — 리본이 모서리 장식을 피한다
        public RectTransform EffectBox;  // 효과 칸 · 그 안 줄 묶음 — 세 장 글자 크기 · 칸 높이를 하나로(10-19 P2 ⑤)
        public RectTransform Effects;
    }

    // ── Lifecycle ──

    public override void Init()
    {
        base.Init();
        BuildChrome();
    }

    private void Update()
    {
        // 전설 카드는 문양 뒤 광선이 느리게 돌고 문양이 떠오른다 — 판·보관함의 금빛 광택과 같은 신호.
        float now = Time.unscaledTime;
        for (int i = 0; i < _cards.Count; i++)
        {
            var c = _cards[i];
            if (c.Rays == null) continue;
            c.Rays.localRotation = Quaternion.Euler(0f, 0f, -now * 360f / RaysTurnSec);
            if (c.Icon != null)
                c.Icon.anchoredPosition = c.IconBase + new Vector2(0f, Mathf.Sin(now * Mathf.PI * 2f / BobPeriod) * BobAmp);
        }

        // 아무 입력 = 스냅. 시퀀스가 끝나야 고를 수 있는 게 아니라, 언제든 끊고 바로 고를 수 있어야 한다.
        // (카드 클릭·[선택]·[넘기기]는 각 핸들러가 SkipReveal을 부른다)
        if (_revealing)
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.F) || Input.GetMouseButtonDown(0))
                SkipReveal();
            return;
        }
        // 10-19 — 1 2 3(4) 키로 고르고 Enter · F로 확정(고른 카드가 있을 때만 · 연출을 건너뛴 누름을 뗀 직후는 아니게)
        if (_closing || _cards.Count == 0) return;
        for (int i = 0; i < _cards.Count && i < 4; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) { SetSelected(i); return; }
        if (_selected >= 0 && Time.unscaledTime - _revealEndedAt >= SkipGuardSec
            && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.F)))
            OnConfirmClicked();
    }

    // ── Public API ──

    /// <summary>버튼 클릭 즉시 resolve — 애니메이션을 기다리지 않는다.</summary>
    public UniTask WaitForInteractionAsync(CancellationToken ct)
    {
        _interactionTcs = new UniTaskCompletionSource();
        return _interactionTcs.Task.AttachExternalCancellation(ct);
    }

    /// <summary>
    /// 후보를 주입해 카드를 구성한다. ShowPopupUIAndGetAsync 직후 호출.
    /// <paramref name="refinerySpent"/> ≥ 0 = 정제소에서 연 창(넘겨도 쓴 원석은 안 돌려준다 — 10-26 · 방 확률 막대는 숨긴다 — 10-18 분석 P1).
    /// <paramref name="rounds"/> &gt; 1 = 챌린지 다중 보상의 <paramref name="round"/>번째 — 제목에 「보상 2/3」(10-19 P2 ⑧).
    /// </summary>
    public void Setup(List<(RuntimeItemData data, ItemSO so)> candidates, RunItemInventory inventory, int refinerySpent = -1,
                      int round = 0, int rounds = 0)
    {
        _candidates    = candidates;
        _inventory     = inventory;
        _closing       = false;
        _refinerySpent = refinerySpent;
        if (_windowRoot != null && _windowRoot.Find("Title") is Transform titleT && titleT.TryGetComponent<TMP_Text>(out var title))
            title.text = rounds > 1 ? $"{TitleText}  <color=#9A98A0>보상 {Mathf.Clamp(round, 1, rounds)}/{rounds}</color>" : TitleText;
        _openBoard   = rounds <= 1 || round >= rounds;
        _briefReveal = rounds > 1 && round >= 2;

        if (candidates == null || candidates.Count == 0)
        {
            Debug.LogWarning("[UI_RuneSelectPopup] 후보 없음 — 즉시 닫음");
            Skipped = true;
            _interactionTcs?.TrySetResult();
            ClosePopupUI();
            return;
        }

        // 공개 순서 = 등급 오름차순. 제일 좋은 카드가 마지막에 열려야 상승감이 생긴다(§C-1 Beat 3).
        // 후보 리스트 자체를 정렬하므로 _selected 인덱스와 카드 순서가 항상 일치한다.
        _candidates.Sort((a, b) => a.data.rarity.CompareTo(b.data.rarity));
        SkipOre = ClearRewardTrigger.SkipOreReward;
        foreach (var c in _candidates) SkipOre = Mathf.Max(SkipOre, RuneSalvage.OreValueOf(c.data));

        if (_veil == null) _veil = ShopUIStyle.FindDeep(transform, "Veil")?.GetComponent<Image>();
        if (_veil != null && _veilAlpha < 0f) _veilAlpha = _veil.color.a;

        // 선택 초기화는 카드를 짓기 <b>전에</b> — 뒤에 하면 선택 강조가 숨겨 둔 카드의 투명도·배율을 1로 되돌려
        // 아직 내려오지 않은 카드가 뒷면으로 미리 보인다.
        SetSelected(-1);
        BuildCards();
        RefreshCounter();
        RefreshOdds();   // 열 때마다 — 챕터 · 방 종류가 바뀌었다(창을 다시 쓰면 지난 값이 남는다, 10-10)
        RefreshSkipHint();

        // 선택은 이 시점부터 이미 가능하다 — 시퀀스가 끝나야 고를 수 있는 게 아니다(§C-1 스킵 규칙).
        PlayRevealSequenceAsync().Forget();
    }

    // ── 공개 연출 ──

    /// <summary>
    /// 막 → 뒷면으로 내려옴 → 등급 오름차순 뒤집기(전설은 멈춤·개봉).
    /// 뒷면의 문장 색이 곧 등급이라, 뒤집기 전에 무엇이 섞였는지 이미 안다 — 예전의 등급 굴림(Common→…→Legendary 라벨이
    /// 계단식으로 오르던 것)은 결과를 가리는 척만 해서 걷어냈다.
    /// </summary>
    private async UniTaskVoid PlayRevealSequenceAsync()
    {
        var ct = destroyCancellationToken;
        _revealing     = true;
        _revealSkipped = false;

        try
        {
            Managers.Sound?.PlayUiEvent(SoundEvent.UiOpen);   // 10-29 UI 소리 — 열기(연출 끔 길도 여기를 지난다)
            if (RewardPresentation.IsOff) return;
            bool brief = _briefReveal || RewardPresentation.Mode == RewardPresentationMode.Brief;

            FadeVeilAsync(0f, VeilBase, VeilInDur, ct).Forget();
            FadeBoardAsync(ct).Forget();
            await HoldOrSkip(DealDelay, ct);

            for (int i = 0; i < _cards.Count && !_revealSkipped; i++)
            {
                DealAsync(_cards[i], ct).Forget();
                await HoldOrSkip(brief ? BriefStagger : DealStagger, ct);
            }
            // 마지막 카드가 다 내려앉기를 기다리지 않는다(시안) — 첫 카드는 이미 앉아 있다.
            // 축약 모드는 간격이 짧아 내려오는 트윈과 뒤집기가 겹치므로 착지까지 기다린다.
            await HoldOrSkip(brief ? DealDur : FlipLead, ct);

            bool lastAwaited = false;

            for (int i = 0; i < _cards.Count && !_revealSkipped; i++)
            {
                var card = _cards[i];
                lastAwaited = card.Rarity == ItemRarity.Legendary && !brief;
                if (lastAwaited)
                {
                    await RevealLegendaryAsync(card, ct);
                    continue;
                }
                if (card.Rarity == ItemRarity.Epic && !brief) await AnticipateAsync(card, ct);   // 10-19 영웅 — 뒤집기 전 살짝 떨림
                FlipAsync(card, ct).Forget();
                await HoldOrSkip(brief ? BriefStagger : card.Rarity == ItemRarity.Epic ? FlipStagger + EpicHold : FlipStagger, ct);
            }
            if (!lastAwaited) await HoldOrSkip(FlipHalf * 2f, ct);   // 마지막 카드가 다 펴질 때까지
        }
        catch (OperationCanceledException) { return; }
        finally
        {
            _revealing = false;
            _revealEndedAt = Time.unscaledTime;
            // 선택 확정으로 이미 날아가는 중이면 건드리지 않는다(확정 시점에 한 번 스냅했다).
            if (!_closing)
            {
                // 취소·스킵 어느 경로로 빠져도 카드가 뒷면·반투명·축소 상태로 남지 않게 확정한다.
                SnapToRest();

                // 위 한 줄이 배율·투명도를 되돌리므로, 연출 도중에 고른 카드가 있으면
                // 선택 강조가 함께 지워진다(_selected만 남고 화면에는 표시가 사라진다).
                if (_selected >= 0) ApplySelectionVisual(_selected);
            }
        }
    }

    /// <summary>「지금 룬판」은 막과 함께 떠오른다 — 순간 등장 금지(UI 톤 규약). 스킵하면 <see cref="SnapToRest"/>가 1로 둔다.</summary>
    private async UniTaskVoid FadeBoardAsync(CancellationToken ct)
    {
        if (_boardGroup == null) return;
        try { await TweenAsync(DealDur, t => { if (_boardGroup != null) _boardGroup.alpha = EaseOutCubic(t); }, ct); }
        catch (OperationCanceledException) { }
    }

    /// <summary>뒷면인 채로 위에서 내려앉는다.</summary>
    private async UniTaskVoid DealAsync(CardView card, CancellationToken ct)
    {
        try
        {
            await TweenAsync(DealDur, t =>
            {
                float e = EaseOutCubic(t);
                card.Rt.anchoredPosition = card.BasePos + new Vector2(0f, DealRise * (1f - e));
                card.Rt.localScale       = Vector3.one * Mathf.Lerp(0.86f, 1f, e);
                card.Group.alpha         = Mathf.Clamp01(t * 2f);
            }, ct);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>가로 배율을 접었다 펴서 뒤집는다. 펴질 때 문양 뒤 빛이 한 번 부푼다.</summary>
    private async UniTask FlipAsync(CardView card, CancellationToken ct, float lift = 1f)
    {
        try
        {
            var spec = RewardPresentation.For(card.Rarity);
            Managers.Sound?.PlayUiEvent(SoundEvent.RuneFlip, spec.SfxPitch);   // 등급 피치는 그대로

            await TweenAsync(FlipHalf, t => SetFlipScale(card, 1f - EaseInQuad(t), lift), ct);
            ShowFace(card, true);
            if (card.Glow != null) PulseGlowAsync(card.Glow, ct).Forget();
            PlayRarityAccent(card, ct);
            AssembleShapeAsync(card, ct).Forget();
            await TweenAsync(FlipHalf, t => SetFlipScale(card, EaseOutCubic(t), lift), ct);

            if (spec.PulsePeak > 0f) VolumePulseService.Pulse(spec.PulsePeak, spec.PulseDuration);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 전설 — 나머지가 어두워지고 막이 짙어진다. 전설 카드만 떠오르며 금빛 기둥이 선다(0.55초) →
    /// 뒤집히는 순간 섬광·불티 24개·화면 흔들림 0.3초.
    /// </summary>
    private async UniTask RevealLegendaryAsync(CardView card, CancellationToken ct)
    {
        foreach (var other in _cards)
            if (other != card && other.Group != null) other.Group.alpha = LegendDim;
        if (_boardGroup != null) _boardGroup.alpha = LegendDim;
        FadeVeilAsync(VeilBase, LegendVeil, 0.2f, ct).Forget();
        ShowPillar(card);

        Managers.Sound?.PlayUiEvent(SoundEvent.RuneLegend);
        await TweenAsync(0.3f, t =>
        {
            float e = EaseOutCubic(t);
            card.Rt.anchoredPosition = card.BasePos + new Vector2(0f, LegendRise * e);
            card.Rt.localScale       = Vector3.one * Mathf.Lerp(1f, LegendLift, e);
            if (_pillar != null) _pillar.color = WithAlpha(LegendGold, 0.55f * e);
        }, ct);
        await HoldOrSkip(LegendHold - 0.3f, ct);
        if (_revealSkipped) return;

        await FlipAsync(card, ct, LegendLift);
        if (_revealSkipped) return;

        // 섬광은 카드에서 퍼지는 원형 — 화면 전체를 금색으로 덮으면 다른 카드와 글자가 한꺼번에 바랜다.
        var spec = RewardPresentation.For(ItemRarity.Legendary);
        if (spec.ScreenFlashAlpha > 0f)
        {
            BurstAsync(card, ct).Forget();
            PlayScreenFlashAsync(spec.ScreenFlashAlpha * 0.3f, ct).Forget();
        }
        SparksAsync(card, ct).Forget();
        ShakeAsync(ct).Forget();

        await TweenAsync(ShakeDur + 0.02f, t =>
        {
            float e = EaseOutCubic(t);
            card.Rt.anchoredPosition = card.BasePos + new Vector2(0f, LegendRise * (1f - e));
            card.Rt.localScale       = Vector3.one * Mathf.Lerp(LegendLift, 1f, e);
            if (_pillar != null) _pillar.color = WithAlpha(LegendGold, 0.55f * (1f - e));
        }, ct);

        foreach (var other in _cards)
            if (other.Group != null) other.Group.alpha = 1f;
        if (_boardGroup != null) _boardGroup.alpha = 1f;
        FadeVeilAsync(LegendVeil, VeilBase, 0.2f, ct).Forget();
    }

    /// <summary>카드 뒤 금빛 기둥 — 레이어 맨 뒤라 카드를 덮지 않는다.</summary>
    private void ShowPillar(CardView card)
    {
        if (_pillar == null) return;
        _pillar.rectTransform.anchoredPosition = new Vector2(card.BasePos.x, card.BasePos.y);
        _pillar.rectTransform.sizeDelta        = new Vector2(_cardW * 1.15f, CardH * 1.6f);
        _pillar.color = WithAlpha(LegendGold, 0f);
        _pillar.gameObject.SetActive(true);
    }

    private async UniTaskVoid SparksAsync(CardView card, CancellationToken ct, int count = SparkCount, Color? tint = null, float reach = 200f)
    {
        if (_fxRoot == null) return;
        var col    = tint ?? LegendGold;
        var origin = card.BasePos + new Vector2(0f, CardH * 0.2f);
        var sparks = new RectTransform[count];
        var dirs   = new Vector2[count];
        var imgs   = new Image[count];
        for (int i = 0; i < count; i++)
        {
            var img = ShopUIStyle.MakeImage(_fxRoot, "Spark", col);
            img.sprite = SoftDot;
            imgs[i] = img;
            sparks[i] = img.rectTransform;
            ShopUIStyle.Anchor(sparks[i], new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                               origin, Vector2.one * UnityEngine.Random.Range(8f, 14f));
            float a = UnityEngine.Random.value * Mathf.PI * 2f;
            dirs[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * UnityEngine.Random.Range(60f, reach);
        }

        try
        {
            float t = 0f;
            while (t < 1f && !_revealSkipped)
            {
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / SparkDur);
                float e = EaseOutCubic(t);
                for (int i = 0; i < count; i++)
                {
                    sparks[i].anchoredPosition = origin + dirs[i] * e;
                    sparks[i].localScale       = Vector3.one * Mathf.Lerp(1f, 0.2f, t);
                    imgs[i].color              = WithAlpha(col, 1f - t);
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            for (int i = 0; i < count; i++)
                if (sparks[i] != null) Destroy(sparks[i].gameObject);
        }
    }

    /// <summary>
    /// 10-19 사용자 「등급별 특별 연출」 — 앞면이 서는 순간 등급만큼 한 단계씩 더한다(일반은 없음 · 후광 · 기둥 · 섬광은 전설 하나 그대로):
    /// 희귀 = 틀 안쪽 선이 등급색으로 한 번 켜진다 · 영웅 = 틀 선 + 모서리 보석 빛 + 불티 8 · 전설 = 틀 선 + 보석 빛(불티 · 섬광은 전설 연출이).
    /// </summary>
    private void PlayRarityAccent(CardView card, CancellationToken ct)
    {
        if (card.Rarity < ItemRarity.Rare) return;
        var col = RewardPresentation.FrameColor(card.Rarity);
        EdgeFlareAsync(card, col, card.Rarity >= ItemRarity.Epic ? 0.95f : 0.7f, ct).Forget();
        if (card.Rarity < ItemRarity.Epic) return;
        CornerFlareAsync(card, col, ct).Forget();
        if (card.Rarity == ItemRarity.Epic) SparksAsync(card, ct, EpicSparkCount, col, 130f).Forget();
    }

    /// <summary>틀 안쪽 빛 선을 따라 등급색 선이 가운데서 양끝으로 그어진 뒤 사라진다(얇은 선 2px + 번짐 8px · 카드 마스크 밖 · 카드 위).</summary>
    private async UniTaskVoid EdgeFlareAsync(CardView card, Color col, float peak, CancellationToken ct)
    {
        if (card?.Rt == null) return;
        float ins = card.RarityArt ? FrameLineArtPx / CardFrameScale : 2f;
        var lines = new List<(RectTransform rt, Image img, float a, bool horiz)>(8);
        foreach (var (th, a) in new[] { (8f, 0.28f), (2f, 1f) })
        {
            float o = ins - th * 0.5f;
            lines.Add(EdgeLine(card.Rt, col, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -o), new Vector2(-ins * 2f, th), a, true));
            lines.Add(EdgeLine(card.Rt, col, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f,  o), new Vector2(-ins * 2f, th), a, true));
            lines.Add(EdgeLine(card.Rt, col, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2( o, 0f), new Vector2(th, -ins * 2f), a, false));
            lines.Add(EdgeLine(card.Rt, col, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-o, 0f), new Vector2(th, -ins * 2f), a, false));
        }
        try
        {
            await TweenAsync(EdgeFlareDur, t =>
            {
                float grow = EaseOutCubic(Mathf.Clamp01(t / 0.45f));                 // 앞 45% — 가운데서 양끝으로
                float fade = t < 0.45f ? 1f : 1f - (t - 0.45f) / 0.55f;               // 뒤 55% — 사라짐
                foreach (var l in lines)
                {
                    l.rt.localScale = l.horiz ? new Vector3(grow, 1f, 1f) : new Vector3(1f, grow, 1f);
                    l.img.color     = WithAlpha(col, peak * l.a * fade);
                }
            }, ct);
        }
        catch (OperationCanceledException) { }
        finally { foreach (var l in lines) if (l.rt != null) Destroy(l.rt.gameObject); }
    }

    private static (RectTransform, Image, float, bool) EdgeLine(RectTransform parent, Color col, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size, float a, bool horiz)
    {
        var img = ShopUIStyle.MakeImage(parent, "EdgeFlare", WithAlpha(col, 0f));
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        rt.localScale = horiz ? new Vector3(0f, 1f, 1f) : new Vector3(1f, 0f, 1f);
        return (rt, img, a, horiz);
    }

    /// <summary>네 모서리 보석 자리에서 등급색 빛이 한 번 부푼다(영웅 이상).</summary>
    private async UniTaskVoid CornerFlareAsync(CardView card, Color col, CancellationToken ct)
    {
        if (card?.Rt == null) return;
        float gem  = card.RarityArt ? (card.Rarity == ItemRarity.Legendary ? 45f : 25f) / CardFrameScale : 4f;
        float size = card.Rarity == ItemRarity.Legendary ? 46f : 34f;
        var dots = new Image[4];
        var corners = new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) };
        for (int i = 0; i < 4; i++)
        {
            var c = corners[i];
            var img = ShopUIStyle.MakeImage(card.Rt, "CornerFlare", WithAlpha(col, 0f));
            img.sprite = SoftDot;
            img.raycastTarget = false;
            var off = new Vector2(c.x < 0.5f ? gem : -gem, c.y < 0.5f ? gem : -gem);
            ShopUIStyle.Anchor(img.rectTransform, c, c, new Vector2(0.5f, 0.5f), off, Vector2.one * size);
            dots[i] = img;
        }
        try
        {
            await TweenAsync(CornerFlareDur, t =>
            {
                float s = Mathf.Lerp(0.3f, 1.4f, EaseOutCubic(t));
                float a = Mathf.Sin(t * Mathf.PI);
                foreach (var d in dots)
                {
                    d.rectTransform.localScale = Vector3.one * s;
                    d.color = WithAlpha(col, 0.9f * a);
                }
            }, ct);
        }
        catch (OperationCanceledException) { }
        finally { foreach (var d in dots) if (d != null) Destroy(d.gameObject); }
    }

    /// <summary>영웅 — 뒤집기 전 뒷면이 살짝 떨리며 조금 커진다(1.5px · 1.03배 · 0.22초).</summary>
    private async UniTask AnticipateAsync(CardView card, CancellationToken ct)
    {
        try
        {
            await TweenAsync(AnticipateDur, t =>
            {
                card.Rt.anchoredPosition = card.BasePos + new Vector2(Mathf.Sin(t * 95f) * AnticipateAmp * t, 0f);
                card.Rt.localScale       = Vector3.one * Mathf.Lerp(1f, 1.03f, t);
            }, ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (card.Rt != null) { card.Rt.anchoredPosition = card.BasePos; card.Rt.localScale = Vector3.one; }
        }
    }

    /// <summary>카드 한가운데서 퍼지는 금빛 원 — 0.45초에 걸쳐 커지며 사라진다.</summary>
    private async UniTaskVoid BurstAsync(CardView card, CancellationToken ct)
    {
        if (_fxRoot == null) return;
        var img = ShopUIStyle.MakeImage(_fxRoot, "Burst", WithAlpha(LegendGold, 0.9f));
        img.sprite = SoftDot;
        var rt = img.rectTransform;
        ShopUIStyle.Anchor(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                           card.BasePos + new Vector2(0f, CardH * 0.2f), Vector2.one * _cardW * 3f);
        try
        {
            float t = 0f;
            while (t < 1f && !_revealSkipped)
            {
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / BurstDur);
                rt.localScale = Vector3.one * Mathf.Lerp(0.5f, 1.2f, EaseOutCubic(t));
                img.color     = WithAlpha(LegendGold, 0.9f * (1f - t));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally { if (rt != null) Destroy(rt.gameObject); }
    }

    private async UniTaskVoid ShakeAsync(CancellationToken ct)
    {
        if (_cardLayer == null) return;
        try
        {
            await TweenAsync(ShakeDur, t =>
            {
                float k = ShakeAmp * (1f - t);
                _cardLayer.anchoredPosition = new Vector2(Mathf.Sin(t * 71f) * k, Mathf.Cos(t * 53f) * k * 0.7f);
            }, ct);
        }
        catch (OperationCanceledException) { }
        finally { if (_cardLayer != null) _cardLayer.anchoredPosition = Vector2.zero; }
    }

    private async UniTaskVoid PulseGlowAsync(Image glow, CancellationToken ct)
    {
        var rt = glow.rectTransform;
        var baseCol = glow.color;
        try
        {
            await TweenAsync(0.35f, t =>
            {
                float k = Mathf.Sin(t * Mathf.PI);
                rt.localScale = Vector3.one * (1f + 0.3f * k);
                glow.color    = WithAlpha(baseCol, Mathf.Min(1f, baseCol.a * (1f + 1.2f * k)));
            }, ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (rt != null) rt.localScale = Vector3.one;
            if (glow != null) glow.color = baseCol;
        }
    }

    private async UniTaskVoid FadeVeilAsync(float from, float to, float dur, CancellationToken ct)
    {
        if (_veil == null || _veilAlpha < 0f) return;
        try { await TweenAsync(dur, t => _veil.color = WithAlpha(_veil.color, Mathf.Lerp(from, to, t)), ct); }
        catch (OperationCanceledException) { }
    }

    /// <summary>Legendary 전체화면 금색 플래시. 코드 생성 Image 1장 + 알파 트윈(신규 아트 0).</summary>
    private async UniTaskVoid PlayScreenFlashAsync(float alpha, CancellationToken ct)
    {
        if (_screenFlash == null) return;

        var c = RewardPresentation.FrameColor(ItemRarity.Legendary);
        c.a = alpha;
        _screenFlash.color = c;

        try { await UIJuice.FadeOutAsync(_screenFlash, alpha, ScreenFlashDur, ct); }
        catch (OperationCanceledException) { }
    }

    /// <summary>모든 카드를 앞면·제자리로, 연출 부산물(막·기둥·흔들림·섬광)을 평상시로 되돌린다.</summary>
    private void SnapToRest()
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            var card = _cards[i];
            UIJuice.SnapPopIn(card.Rt, card.Group, card.BasePos);
            ShowFace(card, true);
        }
        if (_veil != null && _veilAlpha >= 0f) _veil.color = WithAlpha(_veil.color, _veilAlpha);
        if (_boardGroup != null) _boardGroup.alpha = 1f;
        if (_pillar != null) _pillar.gameObject.SetActive(false);
        if (_cardLayer != null) _cardLayer.anchoredPosition = Vector2.zero;
        if (_screenFlash != null) _screenFlash.color = WithAlpha(_screenFlash.color, 0f);
    }

    /// <summary>진행 중인 공개 연출을 최종 상태로 스냅한다. 어떤 입력이든 들어오면 호출.</summary>
    private void SkipReveal()
    {
        if (!_revealing) return;
        _revealSkipped = true;
    }

    private float VeilBase => _veilAlpha >= 0f ? _veilAlpha : 0f;

    private static void SetFlipScale(CardView card, float sx, float lift)
        => card.Rt.localScale = new Vector3(Mathf.Max(0.001f, sx) * lift, lift, 1f);

    private static void ShowFace(CardView card, bool faceUp)
    {
        if (card.Face != null) card.Face.SetActive(faceUp);
        if (card.Back != null) card.Back.SetActive(!faceUp);
    }

    /// <summary>스킵되면 그 자리에서 멈춘다(최종 상태는 <see cref="SnapToRest"/>가 잡는다).</summary>
    private async UniTask TweenAsync(float dur, Action<float> step, CancellationToken ct)
    {
        float t = 0f;
        while (t < 1f)
        {
            if (_revealSkipped) return;
            t = Mathf.Min(1f, t + Time.unscaledDeltaTime / Mathf.Max(0.01f, dur));
            step(t);
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }

    private async UniTask HoldOrSkip(float seconds, CancellationToken ct)
    {
        float t = 0f;
        while (t < seconds && !_revealSkipped)
        {
            t += Time.unscaledDeltaTime;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }

    private static float EaseOutCubic(float t) { float p = 1f - t; return 1f - p * p * p; }
    private static float EaseInQuad(float t) => t * t;
    private static Color WithAlpha(Color c, float a) { c.a = a; return c; }

    // ── Build ──

    private void BuildChrome()
    {
        if (_built) return;
        _built = true;
        // 프리팹이 구워져 있으면 <b>짓지 않고 잇기만 한다</b> — 다시 지으면 UI가 두 벌 겹친다.
        if (transform.childCount > 0) { BindBakedHierarchy(); return; }


        ShopUIStyle.Stretch(GetComponent<RectTransform>());

        var veil = ShopUIStyle.MakeImage(transform, "Veil", ShopUIStyle.Veil, raycast: true);
        ShopUIStyle.Stretch(veil.rectTransform);

        // MakeFrame은 inner(채움)를 반환한다 — 위치/크기는 부모(테두리)에 건다.
        var window = ShopUIStyle.MakeFrame(transform, "Window",
            ShopUIStyle.WindowBorder, ShopUIStyle.WindowFill, 2f, raycast: true);
        ShopUIStyle.Anchor((RectTransform)window.transform.parent,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(WindowW, WindowH));
        _windowRoot = window.transform;
        // 화면 맞춤은 빌더가 붙인다 — 프리팹에만 붙이면 재굽기 때 사라진다.
        // 크기를 가진 것은 테두리(부모)라 거기에 붙인다. 상한은 「셋 중 하나」 팝업 값(구운 프리팹은 ApplyThinChrome이 덮어쓴다).
        window.transform.parent.gameObject.AddComponent<UIWindowFitter>()
              .Configure(maxScale: FitMaxScale);

        var skin = UISkin.RuneSelect;
        _skinned = skin != null;
        // 창 바탕 — 전면 일러스트로 교체(9-slice 아님). 미로드면 기존 색 창 유지.
        ShopUIStyle.Skin(window, skin?.background);

        // 타이틀바 — 제목/카운터 뒤에 깔린다. 아트 없으면 표시 안 됨(투명 폴백).
        if (skin?.titleBar != null)
        {
            var titleBar = ShopUIStyle.MakeImage(_windowRoot, "TitleBar", Color.white);
            ShopUIStyle.Skin(titleBar, skin.titleBar, sliced: true);
            ShopUIStyle.Anchor(titleBar.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -16f), new Vector2(WindowW - 48f, 40f));   // 의뢰서 N2 1052×36
        }

        // 제목
        var title = ShopUIStyle.MakeText(_windowRoot, "Title", TitleSize, FontStyles.Normal,
            TextAlignmentOptions.Left, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(title.rectTransform,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(40f, -20f), new Vector2(600f, 32f));
        title.text = TitleText;

        // 보관함/배치 카운터
        _counterText = ShopUIStyle.MakeText(_windowRoot, "Counter", 16f, FontStyles.Normal,
            TextAlignmentOptions.Right, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(_counterText.rectTransform,
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-40f, -22f), new Vector2(420f, 28f));

        BuildFooter();

        // 전체화면 플래시 오버레이 — Legendary 방점 전용. 평소엔 알파 0이라 아무것도 가리지 않는다.
        // 창(window)이 아니라 팝업 루트에 붙여 화면 전체를 덮되, 레이캐스트는 받지 않는다.
        _screenFlash = ShopUIStyle.MakeImage(transform, "ScreenFlash", new Color(1f, 1f, 1f, 0f));
        ShopUIStyle.Stretch(_screenFlash.rectTransform);
        _screenFlash.raycastTarget = false;

        ApplyThinChrome();
    }

    [SerializeField] private Transform _windowRoot;

    private void BuildFooter()
    {
        var skin = UISkin.RuneSelect;

        // [선택]
        var confirm = ShopUIStyle.MakeFrame(_windowRoot, "ConfirmBtn",
            ShopUIStyle.BronzeLine, ShopUIStyle.BandFill, 2f, raycast: true);
        ShopUIStyle.Anchor((RectTransform)confirm.transform.parent,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            // 314×52 = 아트 「선택 버튼@2x」(670×111 · 비율 6.04)를 높이 52에 맞춘 값.
            // 200이면 4.08이라 버튼이 세로로 눌린다(아트 실치수는 335×55).
            new Vector2(-100f, 44f), new Vector2(300f, 56f));   // 의뢰서 N10 선택 300×56
        _confirmBtnImg = confirm;
        ShopUIStyle.Skin(_confirmBtnImg, skin?.confirmButton, sliced: true);
        AddClick(confirm.transform.parent.gameObject, OnConfirmClicked);

        _confirmLabel = ShopUIStyle.MakeText(confirm.transform, "Label", ButtonSize, FontStyles.Normal,
            TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Stretch(_confirmLabel.rectTransform);
        _confirmLabel.text = "선택";

        // [넘기기]
        var skip = ShopUIStyle.MakeFrame(_windowRoot, "SkipBtn",
            ShopUIStyle.CardBorder, ShopUIStyle.CardFill, 2f, raycast: true);
        ShopUIStyle.Anchor((RectTransform)skip.transform.parent,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            // 212×52 = 아트 「넘기기 버튼@2x」(387×95 · 비율 4.07)를 높이 52에 맞춘 값.
            // 두 버튼을 40px 간격으로 묶어 창 정중앙에 오도록 x를 다시 잡았다.
            new Vector2(163f, 44f), new Vector2(180f, 56f));    // 의뢰서 N10 넘기기 180×56
        ShopUIStyle.Skin(skip, skin?.skipButton, sliced: true);
        AddClick(skip.transform.parent.gameObject, OnSkipClicked);

        var skipLbl = ShopUIStyle.MakeText(skip.transform, "Label", 18f, FontStyles.Normal,
            TextAlignmentOptions.Center, ShopUIStyle.TextDim);
        ShopUIStyle.Stretch(skipLbl.rectTransform);
        skipLbl.text = "넘기기";

        // 넘기기의 대가 — 설계서 §3 「넘기면 원석 환원」. 보이지 않으면 넘기기는 순손실로 읽힌다.
        // 의뢰서 N10 「넘기기는 조용하게」 — 흐린 16px 한 줄로 버튼 아래에만 둔다.
        var skipHint = ShopUIStyle.MakeText(_windowRoot, "SkipHint", 16f, FontStyles.Normal,
            TextAlignmentOptions.Center, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(skipHint.rectTransform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(163f, 18f), new Vector2(180f, 22f));
        skipHint.text = $"넘기면 원석 +{ClearRewardTrigger.SkipOreReward}";
        FitSingleLine(skipHint);
        ApplyThemeButtons();

        BuildOddsBar();
    }

    /// <summary>
    /// 이 방의 등급 확률 막대. "확률로 뜬다"는 사실이 화면에 처음 존재하게 만든다.
    ///
    /// 값의 출처는 행운이 아니라 <see cref="RoomRewardTable"/>(방 종류)다. 정예방에 들어가면
    /// 막대가 눈에 띄게 위로 쏠려, 위험을 감수한 대가가 숫자로 보인다.
    /// <see cref="OddsBarView"/>는 이미 구현돼 있었으나 호출처가 0개였다 — 그대로 재사용한다.
    /// </summary>
    private void BuildOddsBar()
    {
        OddsBarView.Create(_windowRoot,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(24f, 30f), new Vector2(OddsBarW, OddsBarH));   // 버튼 줄 왼쪽 빈 자리(선택 버튼은 x≥300)
        RefreshOdds();
    }

    /// <summary>
    /// 방 종류에 맞는 확률을 막대에 싣는다. <b>구운 경로에서도 반드시 한 번 불러야 한다</b> —
    /// 프리팹에 굳은 값은 베이크 당시의 일반방 기준이라, 정예방에 들어가도 일반방 확률을
    /// 그대로 보여주는 <b>거짓 정보</b>가 된다.
    /// </summary>
    private void RefreshOdds()
    {
        var bar = GetComponentInChildren<OddsBarView>(true);
        if (bar == null) return;
        // 정제소에서 연 창 — 방 확률은 정제소 확률이 아니다(거짓 정보). 정제소 화면이 이미 자기 확률을 보여 줬다.
        bar.gameObject.SetActive(_refinerySpent < 0);
        if (_refinerySpent >= 0) return;

        var kind = GameRunBootstrapper.Instance?.Run?.CurrentRoomKind ?? RoomPlanKind.Normal;
        // 굴림은 제단에서 안 연 등급을 한 단계씩 내린다(ClampRarity) — 표시도 같은 규칙으로 접는다(정제소와 동일).
        // 안 접으면 영웅·전설이 잠긴 세이브에서도 「Epic 9% · Legend 2%」가 뜬다. 방 하한(정예 보스 = Epic · 잠금보다 앞섬)도 굴림과 같이 본다(10-23).
        var (rare, epic, legendary) = RoomRewardTable.DisplayOdds(kind, GameRunBootstrapper.Instance?.Run?.CurrentRoomDanger ?? false);   // 10-29 F4
        // 방 보상은 Common부터 — 네 등급을 다 보이고, 무엇이 등급을 올렸는지(챕터 · 시기 · 정예) 머리 오른쪽에(10-10).
        bar.ShowCommon = true;
        bar.SetBoost(RoomRewardTable.DescribeBoost(kind));
        bar.SetOdds(rare, epic, legendary, heated: false);
    }

    /// <summary>넘기기 아래 한 줄 — 방 보상 = 원석 +1 · 정제소 = 환불 없음(10-26 · 구운 프리팹 글을 열 때마다 덮어쓴다).</summary>
    private void RefreshSkipHint()
    {
        if (_skipHint == null) _skipHint = _windowRoot != null ? _windowRoot.Find("SkipHint")?.GetComponent<TMP_Text>() : null;
        if (_skipHint == null) return;
        bool full = _refinerySpent < 0 && StagingFull;
        _skipHint.text = _refinerySpent >= 0 ? "넘기면 두 장 다 버린다 · 원석은 돌아오지 않는다"   // 10-26 「환불 없음」(전액 환불로 공짜 다시 돌리기가 됐다)
                       : full ? $"<color={UIPalette.RuneOreHex}>원석 +{SkipOre}</color> · 고르지 않는다" : $"넘기면 원석 +{SkipOre}";
        FitSingleLine(_skipHint);
        // 10-19 — 보관함 가득이면 두 버튼이 두 길을 말한다: [룬판에서 바꾸기](고르고 판에서 정리) · [분해해서 받기](원석)
        if (_windowRoot != null && _windowRoot.Find("SkipBtn/Fill/Label") is Transform sl && sl.TryGetComponent<TMP_Text>(out var skipLbl))
            skipLbl.text = full ? "분해해서 받기" : "넘기기";
        if (_confirmLabel != null) _confirmLabel.text = ConfirmText;
    }

    private void BuildCards()
    {
        foreach (var c in _cards) if (c.Root != null) Destroy(c.Root);
        _cards.Clear();
        if (_boardPanel != null) Destroy(_boardPanel.gameObject);
        _boardPanel = null; _boardGroup = null; _board = null;
        if (_pillar != null) Destroy(_pillar.gameObject);
        if (_fxRoot != null) Destroy(_fxRoot.gameObject);

        // 창은 화면에 맞춰 커지지만 아래 배치는 전부 목업 px다 — 배율 레이어가 그 차이를 흡수한다.
        // 이게 없으면 넓어진 판에 원래 크기 카드가 떠 있게 된다.
        var layer = UIProportional.EnsureScaledLayer(_windowRoot, "CardLayer", WindowW, WindowH) ?? (RectTransform)_windowRoot;
        _cardLayer = layer;

        int n = _candidates.Count;
        // 「지금 룬판」도 줄의 일부다 — 카드 오른쪽 끝에 카드 높이로 선다. 폭 산출과 중앙 정렬에 함께 넣는다.
        bool hasBoard = Managers.RuneData?.GetZoneMapRows() is { Count: > 0 };
        float boardW  = hasBoard ? BoardPanelW + CardGap : 0f;

        // 카드 폭은 칸 수에 맞춰 줄인다. 300 고정이던 시절엔 3장까지만 창에 들어갔고,
        // 정예방 4지선다(RoomRewardTable)에서 카드가 창 밖으로 밀려났다.
        float avail = WindowW - CardSideMargin * 2f - (Mathf.Max(CardSlots, n) - 1) * CardGap - boardW;
        _cardW = Mathf.Min(CardW, avail / Mathf.Max(CardSlots, n));   // 3장 자리 폭 고정 — 적으면 가운데로 모인다(10-19 「옆으로 늘면 보기 안 좋다」)

        float totalW = n * _cardW + (n - 1) * CardGap + boardW;
        float startX = -totalW * 0.5f + _cardW * 0.5f;

        // 전설 기둥은 카드 뒤(레이어 맨 앞 자식), 불티는 카드 위(맨 뒤 자식).
        _pillar = ShopUIStyle.MakeImage(layer, "LegendPillar", WithAlpha(LegendGold, 0f));
        _pillar.sprite = Beam;
        ShopUIStyle.Anchor(_pillar.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                           new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(_cardW, CardH));
        _pillar.gameObject.SetActive(false);

        bool animate = !RewardPresentation.IsOff;
        for (int i = 0; i < n; i++)
        {
            int idx = i;   // 클로저 캡처
            var (data, so) = _candidates[i];

            var card = ShopUIStyle.MakeFrame(layer, $"Card{i}",
                ShopUIStyle.CardBorder, ShopUIStyle.CardFill, 2f, raycast: true);
            var cardRT = (RectTransform)card.transform.parent;   // 위치/크기는 테두리(outer)에
            ShopUIStyle.Anchor(cardRT,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(startX + i * (_cardW + CardGap), CardY), new Vector2(_cardW, CardH));

            AddClick(cardRT.gameObject, () => OnCardClicked(idx));

            var view = new CardView
            {
                Root    = cardRT.gameObject,                 // outer(테두리)가 루트
                Border  = cardRT.GetComponent<Image>(),
                Fill    = card,
                Rt      = cardRT,
                Group   = cardRT.gameObject.AddComponent<CanvasGroup>(),
                BasePos = cardRT.anchoredPosition,
                Rarity  = data.rarity,
            };
            // 카드 테두리 라인아트 + 채움 — 미로드면 색 박스 유지
            var skin = UISkin.RuneSelect;
            ShopUIStyle.Skin(view.Border, skin?.cardFrame, sliced: true);
            ShopUIStyle.Skin(view.Fill,   skin?.cardFill,  sliced: true);
            ApplyRarityCardFrame(view, skin?.RarityCardFrame(data.rarity));

            BuildFace(card.transform, data, view);
            BuildBack(card.transform, data.rarity, view);
            // 등급 테두리는 앞뒤 공통 — 채움 위, 카드 밖으로 살짝 걸친다(얼굴의 클리핑 밖). 등급 카드 틀이 있으면 틀이 등급을 말한다.
            if (!view.RarityArt) BuildGemFrame(cardRT, data.rarity);
            BuildHalo(cardRT, data.rarity);
            view.Select = BuildSelectFrame(cardRT);
            _cards.Add(view);

            // 연출이 켜져 있으면 숨은 뒷면에서 시작한다(공개는 PlayRevealSequenceAsync가 담당).
            ShowFace(view, !animate);
            if (animate)
            {
                view.Group.alpha   = 0f;
                view.Rt.localScale = Vector3.one * 0.86f;
            }
        }

        UnifyEffectRows();

        if (hasBoard)
            BuildBoardPanel(layer, startX - _cardW * 0.5f + n * (_cardW + CardGap) + BoardPanelW * 0.5f, animate);

        _fxRoot = ShopUIStyle.MakeRect(layer, "RevealFx").GetComponent<RectTransform>();
        ShopUIStyle.Stretch(_fxRoot);
    }

    /// <summary>
    /// 「지금 룬판」 자리 — 카드와 같은 줄 · 같은 높이의 어두운 판에 미니 판을 짓는다(<see cref="RuneBoardMini"/>).
    /// 카드 레이어의 자식이라 창 배율을 같이 받고, 카드를 다시 지을 때 같이 지워진다.
    /// </summary>
    private void BuildBoardPanel(RectTransform layer, float x, bool animate)
    {
        var fill = ShopUIStyle.MakeFrame(layer, "BoardPanel", WithAlpha(ShopUIStyle.CardBorder, 0.9f), ChipFill, 1f);
        _boardPanel = (RectTransform)fill.transform.parent;
        ShopUIStyle.Anchor(_boardPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                           new Vector2(x, CardY), new Vector2(BoardPanelW, CardH));
        _boardGroup = _boardPanel.gameObject.AddComponent<CanvasGroup>();
        _boardGroup.alpha = animate ? 0f : 1f;
        _board = RuneBoardMini.Build(_boardPanel, BoardPanelW, _inventory);
    }

    /// <summary>
    /// 등급 보석 테두리 — 서약 카드의 철·블루·보라·골드 조각을 그대로 쓴다(새 아트 0).
    /// 좌상단 모서리 하나를 축 반전해 네 귀퉁이로, 장식바는 위·아래 가운데에 걸터앉힌다.
    /// 조각이 없으면 예전 색 막대 테두리로 떨어진다.
    /// 10-11 v2: 영웅 이상만 · 0.5배 · 선을 1텍셀 깎은 사본(색은 그대로) — 「유물 콘셉트는 남기되 얇게」. 일반 · 희귀는 등급색 선 하나.
    /// </summary>
    private void BuildGemFrame(RectTransform cardRT, ItemRarity rarity)
    {
        // 테 = 등급(10-06) — 등급색 선은 늘 두르고, 보석 조각이 있으면 그 위에 얹는다.
        BuildRarityFrame(cardRT, rarity);
        if (rarity < ItemRarity.Epic) return;
        var skin   = UISkin.RuneSelect;
        var corner = UISpriteMaster.Thinned(skin?.RarityCorner(rarity), keepColor: true);
        var bar    = UISpriteMaster.Thinned(skin?.RarityBar(rarity), keepColor: true);
        if (corner == null || bar == null) return;

        float k  = _cardW / CardW;
        float cw = GemCornerW * k;
        float ch = cw * corner.rect.height / Mathf.Max(1f, corner.rect.width);
        float bw = GemBarW * k;
        float bh = bw * bar.rect.height / Mathf.Max(1f, bar.rect.width);
        float o  = GemOutset;

        // 모서리 — 피벗을 조각의 바깥 모서리(0,1)에 두면 반전해도 바깥선이 카드 가장자리에 붙는다.
        var outer = new Vector2(0f, 1f);
        GemPiece(cardRT, "Gem_TL", corner, new Vector2(0f, 1f), outer, new Vector2(-o,  o), new Vector2(cw, ch), new Vector2( 1f,  1f));
        GemPiece(cardRT, "Gem_TR", corner, new Vector2(1f, 1f), outer, new Vector2( o,  o), new Vector2(cw, ch), new Vector2(-1f,  1f));
        GemPiece(cardRT, "Gem_BL", corner, new Vector2(0f, 0f), outer, new Vector2(-o, -o), new Vector2(cw, ch), new Vector2( 1f, -1f));
        GemPiece(cardRT, "Gem_BR", corner, new Vector2(1f, 0f), outer, new Vector2( o, -o), new Vector2(cw, ch), new Vector2(-1f, -1f));

        // 장식바 — 보석이 테두리 선에 앉도록 가운데를 가장자리 살짝 안쪽에 둔다.
        var mid = new Vector2(0.5f, 0.5f);
        GemPiece(cardRT, "Gem_BarT", bar, new Vector2(0.5f, 1f), mid, new Vector2(0f, -bh * 0.12f), new Vector2(bw, bh), new Vector2(1f,  1f));
        GemPiece(cardRT, "Gem_BarB", bar, new Vector2(0.5f, 0f), mid, new Vector2(0f,  bh * 0.12f), new Vector2(bw, bh), new Vector2(1f, -1f));
    }

    /// <summary>
    /// 10-19 사용자 「룬 선택 레이아웃 배경인 세로 팝업도 등급별로」 — 힉스필드 등급 카드 틀(흑요석 돌 · 일반 은 / 희귀 사파이어 / 영웅 자수정 / 전설 금 세공)을
    /// 카드 채움에 9칸으로 깐다. 바깥 2px 테 선은 끈다. 모서리는 그림 px ÷ <see cref="CardFrameScale"/>(얇게 — 일반 ≈ 16 · 전설 ≈ 40).
    /// </summary>
    private static void ApplyRarityCardFrame(CardView view, Sprite art)
    {
        if (art == null || view?.Fill == null) return;
        view.Fill.sprite = art;
        view.Fill.type   = Image.Type.Sliced;
        view.Fill.color  = Color.white;
        view.Fill.pixelsPerUnitMultiplier = CardFrameScale;
        if (view.Border != null) view.Border.color = Color.clear;
        view.RarityArt  = true;
        view.FrameInset = art.border.x / CardFrameScale;
    }

    private static void GemPiece(RectTransform parent, string name, Sprite art, Vector2 anchor, Vector2 pivot,
                                 Vector2 pos, Vector2 size, Vector2 flip)
    {
        var img = ShopUIStyle.MakeImage(parent, name, Color.white);
        img.sprite = art;
        img.preserveAspect = true;
        ShopUIStyle.Anchor(img.rectTransform, anchor, anchor, pivot, pos, size);
        img.rectTransform.localScale = new Vector3(flip.x, flip.y, 1f);
    }

    /// <summary>등급 색 프레임 4조각(상·하·좌·우). 두께는 1 / 1 / 1.5 / 2(10-11 v2 — 예전 1.5~4.5는 화면에서 4~7px 띠였다).</summary>
    private static void BuildRarityFrame(RectTransform cardRT, ItemRarity rarity)
    {
        float th = rarity switch { ItemRarity.Legendary => 2f, ItemRarity.Epic => 1.5f, _ => 1f };
        var col  = RewardPresentation.FrameColor(rarity);

        // (anchorMin, anchorMax, pivot, pos, size) — 두께 방향만 고정하고 나머지는 늘린다.
        var specs = new[]
        {
            (new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0f),  new Vector2(0f, th)),  // 상
            (new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0f),  new Vector2(0f, th)),  // 하
            (new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(0f, 0f),  new Vector2(th, 0f)),  // 좌
            (new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(0f, 0f),  new Vector2(th, 0f)),  // 우
        };

        for (int i = 0; i < specs.Length; i++)
        {
            var bar = ShopUIStyle.MakeImage(cardRT, $"RarityBar{i}", col);
            ShopUIStyle.Anchor(bar.rectTransform, specs[i].Item1, specs[i].Item2, specs[i].Item3,
                               specs[i].Item4, specs[i].Item5);
        }
    }

    /// <summary>뒷면 — 등급색 빛 위에 마름모 문장, 아래에 등급 이름. 뒤집기 전에 이미 등급을 알린다.</summary>
    private void BuildBack(Transform card, ItemRarity rarity, CardView view)
    {
        // 바탕은 카드 채움(어두운 카드 아트)이 그대로 맡는다 — 사각 판을 덮으면 아트의 깎인 모서리가 가려진다.
        var back = ShopUIStyle.MakeRect(card, "Back").GetComponent<RectTransform>();
        ShopUIStyle.Stretch(back);
        view.Back = back.gameObject;

        var col = ShopUIStyle.Rarity(rarity);
        float cy = -CardH * 0.46f;

        var glow = ShopUIStyle.MakeImage(back.transform, "BackGlow", WithAlpha(col, 0.32f));
        glow.sprite = SoftDot;
        ShopUIStyle.Anchor(glow.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                           new Vector2(0f, cy), Vector2.one * _cardW * 0.95f);

        float s = _cardW * 0.34f;
        var sigil = ShopUIStyle.MakeFrame(back.transform, "Sigil", col, BackFill, Mathf.Max(3f, s * 0.07f));
        var sigilRT = (RectTransform)sigil.transform.parent;
        ShopUIStyle.Anchor(sigilRT, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                           new Vector2(0f, cy), new Vector2(s, s));
        sigilRT.localRotation = Quaternion.Euler(0f, 0f, 45f);
        var core = ShopUIStyle.MakeImage(sigil.transform, "Core", WithAlpha(col, 0.85f));
        ShopUIStyle.Stretch(core.rectTransform, s * 0.24f);

        var word = ShopUIStyle.MakeText(back.transform, "RarityWord", SubSize, FontStyles.Normal,
            TextAlignmentOptions.Center, col);
        ShopUIStyle.Anchor(word.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                           new Vector2(0f, CardH * 0.15f), new Vector2(_cardW - 24f, 26f));
        UITheme.StyleThinText(word, UIWeight.Medium, SubSize, col, shadow: true);
        word.characterSpacing = 18f;
        word.text = rarity.ToString().ToUpperInvariant();
        FitSingleLine(word);
    }

    /// <summary>
    /// 앞면(10-06 개편 — 사용자 「레어도 · 룬 색상 · 룬 모양 · 텍스트 가시성」 · 의뢰서 N3~N9 「모양이 1순위」):
    /// 속성 리본 / 등급 글자 · 속성 칩 / <b>룬 실물 판</b>(속성 조각으로 모양을 크게) / 이름 / (계열 칩) / 효과 칸 / 룬 수 한 줄.
    /// 규칙: 테 = 등급(<see cref="BuildRarityFrame"/> · <see cref="BuildGemFrame"/> · <see cref="BuildHalo"/>) · 리본 · 칸 색 = 속성.
    /// 예전 가운데 문양(효과 개념 그림)은 속성과 색이 어긋나(「어둠」 칩인데 파란 보석) 앞면에서 뺐다.
    /// 10-11 v2: 글자 Noto(이름 500 22 · 효과 400 18 · 라벨 500 15) · 테 두른 상자는 카드 테 + 룬 판 + 효과 칸 셋.
    /// </summary>
    private void BuildFace(Transform card, RuntimeItemData data, CardView view)
    {
        var face = ShopUIStyle.MakeRect(card, "Face", typeof(RectMask2D)).GetComponent<RectTransform>();
        ShopUIStyle.Stretch(face);
        view.Face = face.gameObject;

        var skin      = UISkin.RuneSelect;
        var rarityCol = ShopUIStyle.Rarity(data.rarity);
        var elemCol   = ElementDef.IdColor(data.element, ShopUIStyle.TextDim);
        int elemIdx   = ElementIndex(data.element);
        bool legend   = data.rarity == ItemRarity.Legendary;
        bool family   = BuildFamilyRules.OfItem(data) != BuildFamily.None;
        var top       = new Vector2(0.5f, 1f);

        // 속성 리본 — 카드 맨 위 한 줄
        var ribbonArt = skin?.ElementRibbon(elemIdx);
        var ribbon = ShopUIStyle.MakeImage(face, "ElementRibbon", ribbonArt != null ? Color.white : elemCol);
        if (ribbonArt != null) ribbon.sprite = ribbonArt;
        if (view.RarityArt)   // 등급 카드 틀 — 돌 테 · 안쪽 빛 선 아래, 모서리 장식(전설 금 세공) 사이로(10-19)
            ShopUIStyle.Anchor(ribbon.rectTransform, top, top, top, new Vector2(0f, -(RibbonY + 4f)),
                               new Vector2(_cardW - 2f * Mathf.Max(14f, view.FrameInset + 2f), RibbonH));
        else
            ShopUIStyle.Anchor(ribbon.rectTransform, top, top, top, new Vector2(0f, -RibbonY), new Vector2(_cardW - 28f, RibbonH));

        BuildHeader(face, data, rarityCol, elemCol);
        bool canPlace = BuildShapePanel(face, data, view, rarityCol, elemCol, elemIdx, legend);

        // 이름
        var nameText = ShopUIStyle.MakeText(face, "Name", NameSize, FontStyles.Normal,
            TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(nameText.rectTransform, top, top, top, new Vector2(0f, -NameYNow), new Vector2(_cardW - 24f, NameH));
        UITheme.StyleThinText(nameText, UIWeight.Medium, NameSize, ShopUIStyle.TextPrimary, shadow: true);
        nameText.text = data.displayName ?? data.itemId;
        FitSingleLine(nameText);
        PlaceRuneIcon(face, nameText, data);

        if (family) BuildFamilyChip(face, data);

        // 효과 칸 — 납품 「효과 칸」 판 위에 줄. 바닥은 배지 위.
        float fxTop = EffectsTopNow + (family ? FamilyRowH : 0f);
        float fxH   = CardH - fxTop - BadgeBottom - BadgeH - 8f;
        var box = ShopUIStyle.MakeImage(face, "EffectBox", ChipFill);
        ShopUIStyle.Skin(box, skin?.effectBox, sliced: true);
        ShopUIStyle.Anchor(box.rectTransform, top, top, top, new Vector2(0f, -fxTop), new Vector2(_cardW - 24f, fxH));
        var fxRoot = ShopUIStyle.MakeRect(box.transform, "Effects").GetComponent<RectTransform>();
        ShopUIStyle.Stretch(fxRoot, 8f);
        var vlg = fxRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childControlHeight = true;  vlg.childForceExpandHeight = false;   // 줄바꿈된 효과 행이 자기 높이로 서게(2026-09-09)
        vlg.spacing = 2f;
        BuildEffectRows(fxRoot, data);
        view.EffectBox = box.rectTransform;
        view.Effects   = fxRoot;

        // 룬 수 — 넘칠 때(룬 상한 · 같은 룬 상한)만 아래 붉은 한 줄. 경고일 뿐 선택은 막지 않는다(10-08 · 10-10 칸 → 룬 개수).
        // 10-19 P2 ③: 세 카드에 같은 「룬 n + 1 / 20」이 늘 서 있었다(카운터 · 미니 판까지 다섯 번 · 「+1」이 판에 놓는다는 뜻으로 읽힘) —
        // 평소 수는 오른쪽 「지금 룬판 · 룬 n / 20」이 한 번 말한다. 비는 자리는 발동 칩이 전체 글(「한 방형 · 확률 ×0.7」)로 쓴다.
        TMP_Text badgeText = null;
        if (!canPlace)
        {
            var bottom = new Vector2(0.5f, 0f);
            badgeText = ShopUIStyle.MakeText(face, "PlaceBadge", LabelSize, FontStyles.Normal, TextAlignmentOptions.Center, NoteAmber);
            ShopUIStyle.Anchor(badgeText.rectTransform, bottom, bottom, bottom,
                               new Vector2(0f, BadgeBottom), new Vector2(_cardW - 40f, BadgeH));
            UITheme.StyleThinText(badgeText, UIWeight.Regular, LabelSize, NoteAmber, shadow: true);
            badgeText.text = FullBadge(data);   // 10-19 — 붉은 「넘침 · 판에 못 놓음」은 못 얻는 것처럼 읽혔다 → 얻고 바꿔 끼우는 길
            FitSingleLine(badgeText);
        }
        BuildProcChip(face, data, badgeText);

        // 전설 광택 — 판·보관함과 같은 금빛 줄기가 카드를 가로지른다.
        if (legend)
            face.gameObject.AddComponent<StagingSlotShimmer>()
                .Configure(StagingSlotShimmer.LegendaryTint, 0.22f, 3.6f, _cardW, phase: 0f);
    }

    /// <summary>
    /// 머리줄 — 왼쪽 등급 글자(등급색 · 등급 표기는 이 한 곳), 오른쪽 속성 = 블록 타일 + 이름.
    /// 10-11 v2: 속성 칩의 테 · 채움을 걷었다(카드 안 테 두른 상자 5 → 3) — 타일 그림이 속성색을 맡는다.
    /// </summary>
    private void BuildHeader(RectTransform face, RuntimeItemData data, Color rarityCol, Color elemCol)
    {
        var tl = new Vector2(0f, 1f);
        var word = ShopUIStyle.MakeText(face, "RarityWord", SubSize, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, rarityCol);
        ShopUIStyle.Anchor(word.rectTransform, tl, tl, tl, new Vector2(14f, -HeadY), new Vector2(_cardW * 0.6f, ChipH + 2f));
        UITheme.StyleThinText(word, UIWeight.Medium, SubSize, rarityCol, shadow: true);
        word.text = RewardPresentation.RarityLabel(data.rarity);
        FitSingleLine(word);

        var elem = ElementDef.GetById(data.element);
        if (elem == null) return;
        const float pad = 9f, tile = 14f;
        var chip = MakeChip(face, "ElementChip", elem.Name, UITheme.Thin.Ink, Color.clear, out float ew, tile + 4f);
        if (chip.Find("Fill") is Transform fill && fill.TryGetComponent<Image>(out var fillImg))
            fillImg.color = Color.clear;
        var tileArt = RuneArt.GetBlockTile(data.element);
        var dot = ShopUIStyle.MakeImage(chip, "Tile", tileArt != null ? Color.white : elemCol);
        if (tileArt != null) { dot.sprite = tileArt; dot.preserveAspect = true; }
        ShopUIStyle.Anchor(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                           new Vector2(pad, 0f), Vector2.one * tile);
        chip.anchoredPosition = new Vector2(_cardW * 0.5f - 12f - ew * 0.5f, -HeadY);
        // 좁은 카드(4장 · 174px)에서 등급 글자가 칩에 가렸다(「Commor」 · 10-19) — 글자 폭을 칩 앞까지
        word.rectTransform.sizeDelta = new Vector2(Mathf.Min(_cardW * 0.6f, _cardW - 14f - 12f - ew - 6f), ChipH + 2f);
    }

    /// <summary>
    /// 이름 왼쪽에 룬 그림을 작게 — 사용자 10-10 「어떤 룬인지 조그만 크기라도 보여주면」. 보관함 · 룬판 오른쪽 카드와 같은 그림
    /// (<see cref="RuneArt.ResolveRuneIcon"/>). 그림 + 이름을 한 묶음으로 가운데 맞춘다 — 이름이 길면 말줄임.
    /// </summary>
    private void PlaceRuneIcon(RectTransform face, TMP_Text nameText, RuntimeItemData data)
    {
        var art = RuneArt.ResolveRuneIcon(data) ?? data.icon;
        if (art == null || nameText == null) return;

        var top   = new Vector2(0.5f, 1f);
        float maxW = _cardW - 24f - NameIcon - NameIconGap;
        float tw   = Mathf.Min(maxW, nameText.GetPreferredValues(nameText.text, maxW, NameH).x + 2f);
        float left = -(NameIcon + NameIconGap + tw) * 0.5f;

        var nrt = nameText.rectTransform;
        nrt.sizeDelta        = new Vector2(tw, NameH);
        nrt.anchoredPosition = new Vector2(left + NameIcon + NameIconGap + tw * 0.5f, -NameYNow);

        var icon = ShopUIStyle.MakeImage(face, "RuneIcon", Color.white);
        icon.sprite         = art;
        icon.preserveAspect = true;
        icon.raycastTarget  = false;
        ShopUIStyle.Anchor(icon.rectTransform, top, top, new Vector2(0.5f, 0.5f),
                           new Vector2(left + NameIcon * 0.5f, -NameYNow - NameH * 0.5f), Vector2.one * NameIcon);
    }

    /// <summary>
    /// 룬 실물 판 — 판 · 보관함과 같은 속성 타일을 판에 놓일 모양 그대로 크게 세운다(빈 자리는 옅은 테로 모양 윤곽).
    /// 뒤에 속성색 빛 · 전설만 등급색 빛(10-11 v2 — 등급색은 등급 글자 · 테에만). 룬 수 상한을 넘으면 칸을 어둡게 하고 false.
    /// </summary>
    private bool BuildShapePanel(RectTransform face, RuntimeItemData data, CardView view,
                                 Color rarityCol, Color elemCol, int elemIdx, bool legend)
    {
        var skin = UISkin.RuneSelect;
        var top  = new Vector2(0.5f, 1f);
        var mid  = new Vector2(0.5f, 0.5f);
        float pw = _cardW - 24f;

        var panel = ShopUIStyle.MakeImage(face, "ShapePanel", ChipFill);
        if (skin?.showcaseBackdrop != null) { panel.sprite = skin.showcaseBackdrop; panel.color = ShowcaseTint; }   // 10-29 돌 제단 홈 — 눌러 룬이 앞선다
        else if (skin?.runeTile != null) { panel.sprite = skin.runeTile; panel.color = Color.white; }
        ShopUIStyle.Anchor(panel.rectTransform, top, top, top, new Vector2(0f, -PanelY), new Vector2(pw, PanelHNow));
        var prt = panel.rectTransform;

        if (legend)
        {
            var rays = ShopUIStyle.MakeImage(prt, "Rays", WithAlpha(LegendGold, 0.32f));
            rays.sprite = Rays;
            ShopUIStyle.Anchor(rays.rectTransform, mid, mid, mid, Vector2.zero, Vector2.one * pw * RaysFrac);
            view.Rays = rays.rectTransform;
        }
        // 판 안 등급색 빛 — 전설만(뒤집힐 때 한 번 부푼다). 희귀 · 영웅의 하늘 · 보라 빛은 카드 안 강조색 면적의 큰 몫이었다.
        if (legend)
        {
            var glow = ShopUIStyle.MakeImage(prt, "Glow", WithAlpha(rarityCol, 0.42f));
            glow.sprite = SoftDot;
            ShopUIStyle.Anchor(glow.rectTransform, mid, mid, mid, Vector2.zero, new Vector2(pw * 0.95f, PanelHNow * 1.1f));
            view.Glow = glow;
        }

        var shape = ShopUIStyle.MakeRect(prt, "Shape").GetComponent<RectTransform>();
        ShopUIStyle.Anchor(shape, mid, mid, mid, Vector2.zero, new Vector2(pw, PanelHNow));
        view.Icon = shape;
        view.IconBase = shape.anchoredPosition;

        var entry   = Managers.RuneData?.GetShape(data.shapeId);
        var offsets = entry != null ? RuneDataManager.ParseCellOffsets(entry) : null;
        if (offsets == null || offsets.Length == 0) return true;   // 판정 불가 → 막지 않는다

        // 룬 수 상한(10-08 · 10-10 칸 → 룬 개수) — 판 자리 판정은 뺐다(사용자: 판을 완벽히 배치하지 않는 한 맞히기 어렵다). 넘치면 칸을 어둡게.
        bool canPlace = RuneBlockBudget.Fits(RuneBlockBudget.Used) && RuneBlockBudget.FitsSame(data);   // + 같은 룬 상한(10-14)

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var o in offsets)
        {
            if (o.x < minX) minX = o.x;  if (o.x > maxX) maxX = o.x;
            if (o.y < minY) minY = o.y;  if (o.y > maxY) maxY = o.y;
        }
        int cols = maxX - minX + 1, rows = maxY - minY + 1;
        float cell = Mathf.Min(CellMax,
            (pw - PanelInset * 2f - (cols - 1) * CellGap) / cols,
            (PanelHNow - PanelInset * 2f - CountRowH - (rows - 1) * CellGap) / rows);
        float w = cols * cell + (cols - 1) * CellGap, h = rows * cell + (rows - 1) * CellGap;
        var origin = new Vector2(-w * 0.5f + cell * 0.5f, h * 0.5f - cell * 0.5f + CountRowH * 0.5f);

        var eg = ShopUIStyle.MakeImage(shape, "ElementGlow", WithAlpha(elemCol, 0.38f));   // 판이 가득이어도 흐리게 하지 않는다 — 얻을 수 있다(10-19)
        eg.sprite = SoftDot;
        ShopUIStyle.Anchor(eg.rectTransform, mid, mid, mid, new Vector2(0f, CountRowH * 0.5f), new Vector2(w * 1.7f + 24f, h * 1.7f + 24f));

        var filled = new HashSet<Vector2Int>();
        foreach (var o in offsets) filled.Add(new Vector2Int(o.x - minX, maxY - o.y));   // 위에서부터 줄(판과 같은 방향)
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (filled.Contains(new Vector2Int(c, r))) continue;
                var slot = ShopUIStyle.MakeFrame(shape, $"Slot{c}_{r}", new Color(1f, 1f, 1f, 0.14f), new Color(1f, 1f, 1f, 0.02f), 1.5f);
                ShopUIStyle.Anchor((RectTransform)slot.transform.parent, mid, mid, mid,
                                   origin + new Vector2(c * (cell + CellGap), -r * (cell + CellGap)), Vector2.one * cell);
            }

        // 판 · 보관함과 같은 속성 타일(문양 있는 칸) — 납품 「룬조각」(무늬 없는 색 칸)은 타일이 없을 때만(10-10 사용자 「룬 그리드처럼」).
        var piece = RuneArt.GetBlockTile(data.element) ?? skin?.ElementPiece(elemIdx);
        var tint  = piece != null ? Color.white : elemCol;
        foreach (var p in filled)
        {
            var img = ShopUIStyle.MakeImage(shape, $"C{p.x}_{p.y}", tint);
            if (piece != null) { img.sprite = piece; img.preserveAspect = true; }
            ShopUIStyle.Anchor(img.rectTransform, mid, mid, mid,
                               origin + new Vector2(p.x * (cell + CellGap), -p.y * (cell + CellGap)), Vector2.one * cell);
            view.Cells.Add(img.rectTransform);
        }

        var count = ShopUIStyle.MakeText(prt, "CellCount", LabelSize, FontStyles.Normal, TextAlignmentOptions.MidlineRight, ShopUIStyle.TextDim);
        var br = new Vector2(1f, 0f);
        ShopUIStyle.Anchor(count.rectTransform, br, br, br, new Vector2(-10f, 2f), new Vector2(80f, CountRowH + 4f));
        UITheme.StyleThinText(count, UIWeight.Medium, LabelSize, ShopUIStyle.TextDim, shadow: true);
        count.text = $"{offsets.Length}칸";
        return canPlace;
    }

    /// <summary>발동 계열 기여 — 「기동 +1 → 3/4」. 고르는 순간 빌드에 무엇이 되는지(09-29 빌드 컨셉). 이름 아래 한 줄.</summary>
    private void BuildFamilyChip(RectTransform face, RuntimeItemData data)
    {
        var fam = BuildFamilyRules.OfItem(data, out bool statOnly);
        int now = (BuildImprint.Halves(fam) + (statOnly ? 1 : 2)) / 2;   // 스탯 룬은 절반(10-14 R0)
        int next = BuildFamilyRules.NextThreshold(now);
        bool stageUp = BuildFamilyRules.StageOf(now) > BuildImprint.Stage(fam);
        string tail = stageUp ? $"  <color=#E8BA54>{BuildFamilyRules.StageOf(now)}단계!</color>"
                    : next > 0 ? $"<color=#8A8594>/{next}</color>" : "";
        var famCol = BuildFamilyRules.ColorOf(fam);
        // 10-19 P2 ⑥: 테 · 채움을 걷고 마름모 점 + 글자(속성 칩 v2와 같은 말 — 카드 안 테 두른 상자 5 → 3)
        var fc = MakeChip(face, "FamilyChip", $"{BuildFamilyRules.Label(fam)} {(statOnly ? "+0.5" : "+1")} → {now}{tail}", famCol, Color.clear, out _, DotLead);
        ClearChipFill(fc);
        AddChipDot(fc, famCol, 9f);
        fc.anchoredPosition = new Vector2(0f, -FamilyYNow);
    }

    // ── 발동 계수 칩(10-14 빌드 시너지 A1 · 83 UI 요청 4) ──
    // 카드 맨 아래 줄 왼쪽(룬 수 글은 오른쪽으로 비킨다) — 효과 칸 바로 밑이라 「위 효과가 어떤 타격을 키우나」로 읽힌다. 계열 칩 줄은 계열 칩 몫으로 둔다.
    //  · 한 방형 — 확률 · 지속에 그 적중의 발동 계수를 곱하는 효과(OnHitProc.Roll · Coef: 화상 · 빙결 · 추가 타격 · 순간이동 · 석화 · 기절)
    //             · 강타(계수가 문턱 이상)일 때만 터지는 발동(OnHeavyHit · AfterHeavyHit)
    //  · 연타형 — 타수를 세는 발동(OnHitCount · SameTarget) · 걸 때마다 쌓이는 독(불 아닌 PoisonOnHit — 최대 8중첩)
    // 계수가 곱해지는 룬은 지금 든 무기 평타 계수를 「확률 ×0.7」로 붙인다(ProcCoefficient.Of). 폭이 모자라면 짧게 → 이름만.
    private const float ProcChipSize = 16f;
    private const float ProcChipPad  = 9f;
    private const float ProcChipGap  = 8f;
    private const float ProcChipEdge = 12f;
    private const float DotSize      = 6f;    // 칩 마름모 점(10-19 P2 ⑥ — 테 대신)
    private const float DotLead      = DotSize + 6f;

    private enum ProcDetail { None, Coef, Heavy, Count, Stack }

    /// <summary>룬 하나의 타격 성격 — 효과 칸 순서대로 처음 걸리는 것. 해당 없으면 false(칩 없음).</summary>
    private static bool ProcStyleOf(RuntimeItemData data, out bool heavy, out ProcDetail detail)
    {
        heavy = false; detail = ProcDetail.None;
        if (data?.effects == null) return false;
        foreach (var s in data.effects)
        {
            if (s == null) continue;
            switch (s.trigger)
            {
                case "OnHeavyHit": case "AfterHeavyHit": heavy = true;  detail = ProcDetail.Heavy; return true;
                case "OnHitCount": case "SameTarget":    heavy = false; detail = ProcDetail.Count; return true;
            }
            switch (s.effectType)
            {
                case "Freeze": case "Stun": case "Petrify": case "ExtraAttack": case "TeleportSwap":
                    heavy = true; detail = ProcDetail.Coef; return true;
                case "PoisonOnHit":   // 불 룬 = 화상(그 타격 피해 비례 · 가장 센 하나) · 그 밖 = 독(걸 때마다 +1중첩) — OnHitEffects
                    heavy = data.element == "FIRE"; detail = ProcDetail.Coef; return true;
            }
        }
        return false;
    }

    /// <summary>지금 든 무기 평타의 발동 계수(없으면 -1).</summary>
    private static float CurrentBasicCoef()
    {
        var wd = GameRunBootstrapper.Instance?.Run?.Player?.WeaponManager?.CurrentWeaponData;
        if (wd == null) return -1f;
        return ProcCoefficient.Of(wd.weaponType, WeaponActionType.GroundLight, SkillTierResolver.IsRanged(wd));
    }

    /// <summary>
    /// 판이 가득(룬 상한 · 같은 룬 상한)일 때 카드 아래 한 줄 — 「못 얻는다」가 아니라 「얻고, 판의 하나를 빼면 놓인다」(10-19 사용자).
    /// 고르면 보관함으로 가고, 룬판에서 하나를 빼거나 분해하면 그 자리에 놓을 수 있다.
    /// </summary>
    private static string FullBadge(RuntimeItemData data) => RuneBlockBudget.FitsSame(data)
        ? $"룬판 {RuneBlockBudget.Used}/{RuneBlockBudget.Max} — 하나를 빼면 놓인다"
        : $"같은 룬 {RuneBlockBudget.SameExcept(data)}/{RuneBlockBudget.MaxSame} — 하나를 빼면 놓인다";

    /// <summary>좁은 카드(4장)용 짧은 꼴 — 발동 칩 자리를 낸다.</summary>
    private static string ShortBadge(RuntimeItemData data) => RuneBlockBudget.FitsSame(data)
        ? $"룬판 {RuneBlockBudget.Used}/{RuneBlockBudget.Max}"
        : $"같은 룬 {RuneBlockBudget.SameExcept(data)}/{RuneBlockBudget.MaxSame}";

    /// <summary>맨 아래 줄 왼쪽 칩. 칩이 서면 룬 수 글을 오른쪽 끝으로 옮긴다. 폭이 모자라면 짧은 글 → 그래도 모자라면 칩을 내지 않는다.</summary>
    private void BuildProcChip(RectTransform face, RuntimeItemData data, TMP_Text badge)
    {
        if (!ProcStyleOf(data, out bool heavy, out var detail)) return;
        string name = heavy ? "한 방형" : "연타형";
        float coef  = detail == ProcDetail.Coef ? CurrentBasicCoef() : -1f;
        string tail = detail switch
        {
            ProcDetail.Coef  => coef > 0f ? $"확률 ×{coef:0.#}" : null,
            ProcDetail.Heavy => "강타 때",
            ProcDetail.Count => "타수로",
            _                => null,
        };
        string full = tail != null ? $"{name} · {tail}" : name;
        string mid  = detail == ProcDetail.Coef && coef > 0f ? $"{name} ×{coef:0.#}" : name;

        var col  = BuildFamilyRules.ColorOf(heavy ? BuildFamily.Crit : BuildFamily.Combo);

        // 10-19 P2 ⑥: 테 · 채움을 걷고 마름모 점 + 글자
        var fill = ShopUIStyle.MakeFrame(face, "ProcChip", Color.clear, Color.clear, 1f);
        var rt   = (RectTransform)fill.transform.parent;
        var label = ShopUIStyle.MakeText(fill.transform, "Label", ProcChipSize, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, col);
        UITheme.StyleThinText(label, UIWeight.Medium, ProcChipSize, col);
        label.enableAutoSizing = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        ShopUIStyle.Stretch(label.rectTransform);
        label.rectTransform.offsetMin = new Vector2(ProcChipPad + DotLead, 0f);
        AddChipDot(rt, col, ProcChipPad);

        // 룬 수 글은 긴 꼴(「룬 0 + 1 / 20」) → 좁은 카드(4장 · 174px)면 짧은 꼴(「룬 1/20」 — 고른 뒤 수)로 줄여 칩 자리를 낸다(10-14 SYN_6b)
        string badgeText = badge != null ? badge.text : null;
        string[] badges  = badge != null ? new[] { badgeText, ShortBadge(data) } : new string[] { null };
        string pick = null; float w = 0f, badgeW = 0f;
        foreach (var bt in badges)
        {
            badgeW = bt != null ? Mathf.Ceil(badge.GetPreferredValues(bt).x) + 2f : 0f;
            float avail = _cardW - ProcChipEdge * 2f - badgeW - ProcChipGap;
            foreach (var cand in new[] { full, mid, name })
            {
                w = Mathf.Ceil(label.GetPreferredValues(cand, 999f, ChipH).x + ProcChipPad * 2f + DotLead);
                if (w <= avail) { pick = cand; break; }
            }
            if (pick != null) { badgeText = bt; break; }
        }
        if (pick == null) { Destroy(rt.gameObject); return; }
        label.text = pick;
        if (badge != null) badge.text = badgeText;

        var bl = new Vector2(0f, 0f);
        ShopUIStyle.Anchor(rt, bl, bl, bl, new Vector2(ProcChipEdge, BadgeBottom), new Vector2(w, ChipH));

        if (badge == null) return;
        var br = new Vector2(1f, 0f);
        ShopUIStyle.Anchor(badge.rectTransform, br, br, br, new Vector2(-ProcChipEdge, BadgeBottom), new Vector2(badgeW, BadgeH));
        badge.alignment = TextAlignmentOptions.MidlineRight;
    }

    /// <summary>
    /// 등급 바깥 빛 — <b>전설만</b>(10-11 v2 「후광은 한 화면 한 곳 · 최고 등급 하나」). 고를 때 더 밝히지 않는다(고른 표시는 금 테).
    /// 카드의 첫 자식이라 카드 몸체가 안쪽을 덮고 가장자리 밖으로만 번진다. 고리 경계(24px)를 8px로 줄여 그린다.
    /// </summary>
    private static void BuildHalo(RectTransform cardRT, ItemRarity rarity)
    {
        if (rarity != ItemRarity.Legendary) return;
        var img = ShopUIStyle.MakeImage(cardRT, "RarityHalo", WithAlpha(RewardPresentation.FrameColor(rarity), HaloAlpha));
        img.sprite = HaloRing;
        img.type   = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = HaloRingEdge / HaloPad;
        ShopUIStyle.Stretch(img.rectTransform, -HaloPad);
        img.transform.SetAsFirstSibling();
    }

    /// <summary>
    /// 고른 표시 — 카드 밖 4px에 금 85% 1px 테 + 모서리 눈금 8px(<see cref="UIThinFrame"/> Ready). 평소엔 꺼 두고, 고르는 순간 켜며
    /// 테가 시계 방향으로 그려진다(0.25초). 후광 다음 · 카드 몸체 앞에 깐다 — 영웅 이상 보석 모서리가 테 위에 앉는다.
    /// </summary>
    private UIThinFrame BuildSelectFrame(RectTransform cardRT)
    {
        var frame = UIThinFrame.Create(cardRT, ThinShape.Square, new Vector2(_cardW, CardH) + Vector2.one * (SelectGap * 2f));
        frame.transform.SetSiblingIndex(cardRT.Find("RarityHalo") != null ? 1 : 0);
        frame.gameObject.SetActive(false);
        return frame;
    }

    /// <summary>ElementDef.Order 안 자리(납품 리본 · 조각 배열 순서) — 없으면 -1.</summary>
    private static int ElementIndex(string elementId)
    {
        var order = ElementDef.Order;
        for (int i = 0; i < order.Count; i++)
            if (order[i] == elementId) return i;
        return -1;
    }

    /// <summary>칩 채움을 투명으로 — 테 없는 칩(속성 · 계열).</summary>
    private static void ClearChipFill(RectTransform chip)
    {
        if (chip != null && chip.Find("Fill") is Transform fill && fill.TryGetComponent<Image>(out var img)) img.color = Color.clear;
    }

    /// <summary>칩 왼쪽 마름모 점(<see cref="DotSize"/> · 칩 색) — 테 대신 「무엇의 칩인지」를 색으로 말한다(10-19 P2 ⑥).</summary>
    private static void AddChipDot(RectTransform chip, Color col, float leftPad)
    {
        var dot = ShopUIStyle.MakeImage(chip, "Dot", col);
        dot.raycastTarget = false;
        var lm = new Vector2(0f, 0.5f);
        ShopUIStyle.Anchor(dot.rectTransform, lm, lm, new Vector2(0.5f, 0.5f), new Vector2(leftPad + DotSize * 0.5f, 0f), Vector2.one * DotSize);
        dot.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
    }

    /// <summary>
    /// 효과 줄 — 세 장이 한 몸(10-19 P2 ⑤): 글자 크기는 가장 많이 줄어든 카드에 맞춰 하나로(한 줄 안에 18 · 15 · 12px가 섞였다) ·
    /// 효과 칸 높이는 가장 긴 효과에 맞춰 세 장 같게(Common 카드는 칸의 약 75%가 비어 있었다). 칸 아래는 카드 바탕.
    /// </summary>
    /// <summary>좁은 카드(4장)는 모양 판을 폭에 비례해 낮추고 그만큼 이름 · 계열 · 효과 칸을 올린다 — 4장에서 긴 효과가 하한 12px에서도 칸을 넘었다(10-19).</summary>
    private float PanelShrink   => Mathf.Max(0f, PanelH * (1f - _cardW / PanelRefCardW));
    private float PanelHNow     => PanelH - PanelShrink;
    private float NameYNow      => NameY - PanelShrink;
    private float FamilyYNow    => FamilyY - PanelShrink;
    private float EffectsTopNow => EffectsTop - PanelShrink;

    private void UnifyEffectRows()
    {
        // 앞면이 연출 때문에 꺼져 있으면(뒷면에서 시작) 글 높이를 못 잰다 — 한 줄 높이로 재 칸을 줄여 여러 줄 효과가 겹쳤다(10-19 실측) → 재는 동안만 켠다
        var hidden = new List<GameObject>(_cards.Count);
        foreach (var c in _cards)
            if (c.Face != null && !c.Face.activeSelf) { c.Face.SetActive(true); hidden.Add(c.Face); }
        try { UnifyEffectRowsActive(); }
        finally { foreach (var f in hidden) if (f != null) f.SetActive(false); }
    }

    /// <summary>효과 글의 실제 높이(메시 기준) — 레이아웃 선호 높이는 줄바꿈 · 글꼴 태그(수치 Noto 500) 때문에 한 줄쯤 모자랐다(10-19 실측).</summary>
    private static float RenderedHeight(EffectRowWidget row)
    {
        row.Label.ForceMeshUpdate();
        return row.Label.GetRenderedValues(false).y;
    }

    /// <summary>효과 줄 묶음의 실제 높이 — 줄마다 max(줄 높이, 글 높이) + 「+N개 더」 + 간격 2.</summary>
    private static float RenderedRowsHeight(RectTransform effects)
    {
        float sum = 0f;
        int n = 0;
        foreach (var row in effects.GetComponentsInChildren<EffectRowWidget>(true))
        {
            if (row.Label == null) continue;
            sum += Mathf.Max(EffectRowH, RenderedHeight(row) + 2f);
            n++;
        }
        if (effects.Find("MoreEffects") != null) { sum += EffectRowH; n++; }
        return sum + Mathf.Max(0, n - 1) * 2f;
    }

    private void UnifyEffectRowsActive()
    {
        float font = EffectSize;
        foreach (var c in _cards)
            if (c.Effects != null)
                foreach (var row in c.Effects.GetComponentsInChildren<EffectRowWidget>(true))
                    if (row.Label != null) font = Mathf.Min(font, row.Label.fontSize);
        for (; ; font -= 1f)   // 하나로 맞춘 크기에서 어느 카드든 칸(안쪽 8px씩)을 넘으면 1px씩 — 좁은 4장에서 긴 효과가 몇 px 넘쳤다(10-19)
        {
            bool fits = true;
            foreach (var c in _cards)
            {
                if (c.Effects == null) continue;
                foreach (var row in c.Effects.GetComponentsInChildren<EffectRowWidget>(true))
                    if (row.Label != null) row.Label.fontSize = font;
                LayoutRebuilder.ForceRebuildLayoutImmediate(c.Effects);
                float need = Mathf.Max(LayoutUtility.GetPreferredHeight(c.Effects), RenderedRowsHeight(c.Effects));
                if (c.EffectBox != null && need + 16f > c.EffectBox.sizeDelta.y + 0.5f) fits = false;
            }
            if (fits || font <= MinEffectFont) break;
        }
        // 줄마다 실제 글 높이를 최소 높이로 — 줄 칸이 글보다 낮으면 가운데 맞춤 글이 위아래로 넘친다
        foreach (var c in _cards)
        {
            if (c.Effects == null) continue;
            foreach (var row in c.Effects.GetComponentsInChildren<EffectRowWidget>(true))
                if (row.Label != null && row.TryGetComponent<LayoutElement>(out var le))
                    le.minHeight = Mathf.Max(EffectRowH, RenderedHeight(row) + 2f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(c.Effects);
        }
        // 10-29 칸 높이는 줄이지 않는다 — 가장 긴 효과에 맞춰 줄이면 효과 한 줄짜리(Common) 세 장 모두 칸 아래가 빈 카드 바닥으로 남았다.
        // 칸이 배지 자리 위까지 그대로 서서 세 장 바닥 줄이 맞는다.
    }

    private static RectTransform MakeChip(RectTransform parent, string name, string text, Color textCol, Color lineCol,
                                          out float width, float leading = 0f)
    {
        const float pad = 9f;   // 글자 = 라벨 Noto 500 15(10-11 v2) — 칩 높이 24에 한 줄로 들어간다
        var fill = ShopUIStyle.MakeFrame(parent, name, lineCol, ChipFill, 1f);
        var rt = (RectTransform)fill.transform.parent;

        var label = ShopUIStyle.MakeText(fill.transform, "Label", LabelSize, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, textCol);
        UITheme.StyleThinText(label, UIWeight.Medium, LabelSize, textCol);
        label.enableAutoSizing = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.text = text;
        float tw = label.GetPreferredValues(text, 999f, ChipH).x;
        width = Mathf.Ceil(tw + pad * 2f + leading);

        ShopUIStyle.Anchor(rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                           Vector2.zero, new Vector2(width, ChipH));
        ShopUIStyle.Stretch(label.rectTransform);
        label.rectTransform.offsetMin = new Vector2(pad + leading, 0f);
        return rt;
    }

    /// <summary>
    /// 한 줄 라벨의 넘침을 말줄임으로 가둔다.
    ///
    /// 카드 안의 값(룬 이름·배지)은 전부 <b>길이가 가변</b>인데 박스는 1줄 높이로 고정돼 있다.
    /// 기본 설정에서는 긴 이름이 줄바꿈되며 두 번째 줄이 박스를 뚫고 아래 요소 위로 겹쳤다.
    /// </summary>
    private static void FitSingleLine(TMP_Text t)
    {
        if (t == null) return;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode     = TextOverflowModes.Ellipsis;
    }

    /// <summary>효과 목록 박스에 들어가는 최대 행 수.</summary>
    private const int MaxEffectRows = 4;

    private void BuildEffectRows(RectTransform parent, RuntimeItemData data)
    {
        if (data.effects == null) return;

        var style = EffectRowStyle.Default;

        style.wrap = true;   // 카드 폭(≈300px)에 '▲ 불 존에 놓으면 그 존의 시너지 효과 +20%'가 한 줄로 안 들어간다 — 실측 스크린샷에서 상자 밖으로 흘렀음

        style.fontAsset       = UITheme.Font(UIWeight.Regular);   // 10-11 v2 — 본문 Noto 400(DNF 굵은 글꼴 → 굵기 비 0.17에서 0.10대로)
        style.fontSize        = EffectSize;   // 10-06 16 → 17 · 10-11 18
        style.iconSize        = 20f;
        style.rowHeight       = EffectRowH;   // Noto 줄 높이
        style.usePrefixArrows = true;
        style.inkValues       = true;   // 10-19 P2 ④ — 수치 원색 금(#FFD54A) → Noto 500 · 조용한 잉크(룬판 정보판과 같은 말 · 금은 「준비 · 지금」 전용)

        // 효과가 많은 룬은 행이 박스를 넘어 아래 배지·카드 밖까지 밀고 나갔다(레이아웃이 뭉개진 주범).
        // 박스에 들어가는 만큼만 그리고, 잘린 개수는 마지막 줄에 알린다.
        int shown = 0, hidden = 0;
        foreach (var slot in data.effects)
        {
            if (string.IsNullOrEmpty(slot.effectType)) continue;
            if (shown >= MaxEffectRows) { hidden++; continue; }
            EffectRowWidget.Create(parent, style, slot);
            shown++;
        }

        if (hidden > 0 && shown > 0)
        {
            // 마지막 행을 "+N개 더"로 대체 — 잘렸다는 사실이 화면에 보여야 한다.
            var last = parent.GetChild(parent.childCount - 1);
            if (last != null) Destroy(last.gameObject);

            var more = ShopUIStyle.MakeText(parent, "MoreEffects", LabelSize, FontStyles.Normal,   // 기울임 없음(10-11 v2)
                TextAlignmentOptions.Center, ShopUIStyle.TextDim);
            UITheme.StyleThinText(more, UIWeight.Regular, LabelSize, ShopUIStyle.TextDim);
            more.text = $"+{hidden + 1}개 더";
            FitSingleLine(more);
            var le = more.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = style.rowHeight;
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
        FitEffectRows(parent, style.fontSize);
    }

    /// <summary>
    /// 효과 줄이 칸을 넘으면 글자를 1px씩 줄여 칸 안에 가둔다(하한 <see cref="MinEffectFont"/>) —
    /// 줄바꿈된 긴 조건부 효과 두 줄이 아래 모양 칸 · 배지 위로 흘렀다(09-29 사용자).
    /// </summary>
    private static void FitEffectRows(RectTransform parent, float startSize)
    {
        float avail = parent.rect.height;
        if (avail <= 1f) return;
        for (float fs = startSize - 1f; fs >= MinEffectFont && LayoutUtility.GetPreferredHeight(parent) > avail + 0.5f; fs -= 1f)
        {
            foreach (var row in parent.GetComponentsInChildren<EffectRowWidget>(true))
                if (row.Label != null) row.Label.fontSize = fs;
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
        }
    }

    /// <summary>뒤집힌 순간 모양 칸이 하나씩 끼워진다(작게 → 살짝 넘침 → 제자리). 스킵되면 그 자리에서 1로.</summary>
    private async UniTaskVoid AssembleShapeAsync(CardView card, CancellationToken ct)
    {
        var cells = card.Cells;
        if (cells.Count == 0) return;
        foreach (var c in cells) if (c != null) c.localScale = Vector3.zero;
        float total = AssembleDur + AssembleStagger * (cells.Count - 1);
        try
        {
            await TweenAsync(total, t =>
            {
                float now = t * total;
                for (int i = 0; i < cells.Count; i++)
                    if (cells[i] != null)
                        cells[i].localScale = Vector3.one * EaseOutBack(Mathf.Clamp01((now - i * AssembleStagger) / AssembleDur));
            }, ct);
        }
        catch (OperationCanceledException) { }
        finally { foreach (var c in cells) if (c != null) c.localScale = Vector3.one; }
    }

    private static float EaseOutBack(float t) { const float c1 = 1.70158f, c3 = c1 + 1f; float p = t - 1f; return 1f + c3 * p * p * p + c1 * p * p; }

    /// <summary>등급 바깥 빛 고리 — 가운데가 빈 9-slice(경계 24px = <see cref="HaloPad"/>). 카드 가장자리에서 바깥으로 옅어진다.
    /// 되살아난 기억 창(<see cref="UI_RelicPartDraftPopup"/>)도 같이 쓴다.</summary>
    internal static Sprite HaloRing
    {
        get
        {
            if (_haloRing != null) return _haloRing;
            const int S = 64, B = 24;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                name = "RuneSelect_HaloRing", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Max(B - (x + 0.5f), (x + 0.5f) - (S - B)));
                    float dy = Mathf.Max(0f, Mathf.Max(B - (y + 0.5f), (y + 0.5f) - (S - B)));
                    float d  = Mathf.Sqrt(dx * dx + dy * dy) / B;   // 0 = 카드 가장자리 · 1 = 바깥 끝
                    float a  = d <= 0f ? 0f : Mathf.Pow(Mathf.Clamp01(1f - d), 2f);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _haloRing = Sprite.Create(tex, new Rect(0f, 0f, S, S), new Vector2(0.5f, 0.5f), 100f, 0,
                                      SpriteMeshType.FullRect, new Vector4(B, B, B, B));
            _haloRing.name = "RuneSelect_HaloRing";
            _haloRing.hideFlags = HideFlags.HideAndDontSave;
            return _haloRing;
        }
    }

    // ── 코드로 그리는 빛 ── (재련소 무대 연출도 같은 스프라이트를 쓴다 — internal)

    internal static Sprite SoftDot => _softDot != null ? _softDot
        : (_softDot = MakeProcSprite("RuneSelect_SoftDot", 64, 64, (u, v) =>
          {
              float d = Mathf.Clamp01(new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f);
              float a = 1f - d;
              return a * a;
          }));

    internal static Sprite Rays => _rays != null ? _rays
        : (_rays = MakeProcSprite("RuneSelect_Rays", 256, 256, (u, v) =>
          {
              float dx = u - 0.5f, dy = v - 0.5f;
              float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
              float ang = Mathf.Repeat(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg, 22f);
              float stripe = Mathf.Clamp01(1f - Mathf.Abs(ang - 3f) / 3.5f);
              return stripe * (1f - Smooth(0.25f, 0.95f, r));
          }));

    private static Sprite Beam => _beam != null ? _beam
        : (_beam = MakeProcSprite("RuneSelect_Beam", 32, 64, (u, v) =>
          {
              float x = (u - 0.5f) / 0.2f;
              return Mathf.Exp(-x * x) * Smooth(0f, 0.25f, v) * (1f - Smooth(0.75f, 1f, v));
          }));

    private static float Smooth(float e0, float e1, float x)
    {
        float t = Mathf.InverseLerp(e0, e1, x);
        return t * t * (3f - 2f * t);
    }

    /// <summary>흰색 + 알파 텍스처 한 장. 저장·언로드 대상이 아니다(HideAndDontSave) — 씬이 바뀌어도 살아 있다.</summary>
    internal static Sprite MakeProcSprite(string spriteName, int w, int h, Func<float, float, float> alpha)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            name = spriteName, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float a = Mathf.Clamp01(alpha((x + 0.5f) / w, (y + 0.5f) / h));
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);

        var sprite = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = spriteName;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    // ── 선택 상태 ──

    /// <summary>카드 누름 — 고른 카드를 다시 누르면 확정(10-19 · 두 번 고르기는 미니 판이 놓일 칸을 미리 보여 주려고 남긴다).</summary>
    private void OnCardClicked(int index)
    {
        if (index == _selected && !_revealing && Time.unscaledTime - _revealEndedAt >= SkipGuardSec) { OnConfirmClicked(); return; }
        SetSelected(index);
    }

    private void SetSelected(int index)
    {
        SkipReveal();
        Managers.Sound?.PlayUiEvent(SoundEvent.UiSelect);
        _selected = index;
        ApplySelectionVisual(index);
    }

    /// <summary>
    /// 선택 강조(금 테 · 배율 · 명암)만 다시 입힌다 — 소리도, 상태 변경도 하지 않는다.
    /// 공개 연출의 마무리(<see cref="SnapToRest"/>)가 카드 배율·투명도를 되돌리므로,
    /// 연출 도중에 고른 카드가 있으면 연출이 끝난 뒤 이걸 한 번 더 불러야 강조가 살아남는다.
    /// 10-11 v2: 고른 표시는 금 85% 1px 테 + 모서리 눈금 + 1.03배 하나 — 테 아트 밝힘 · 채움 색 · 후광 키움(×1.4)은 걷었다(두 겹 표시 금지).
    /// </summary>
    private void ApplySelectionVisual(int index)
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            bool on = (i == index);
            // 테 아트 · 채움은 고름과 무관하게 한 색 — 고른 표시가 금 테 하나로 읽히게.
            if (_cards[i].Border != null)
                _cards[i].Border.color = _cards[i].RarityArt ? Color.clear : _skinned ? CardBorderArt : ShopUIStyle.CardBorder;
            if (_cards[i].Fill != null)
                _cards[i].Fill.color = _skinned || _cards[i].RarityArt ? Color.white : ShopUIStyle.CardFill;

            // 등급 테두리가 모든 카드에 깔려 있어, 같은 등급 후보가 늘어서면 네 장이 똑같이 빛나 보였다.
            // 등급색과 충돌하지 않는 금 테 · 크기 · 명암으로 표시한다.
            if (_cards[i].Rt != null)
                _cards[i].Rt.localScale = Vector3.one * (on ? SelectedCardScale : 1f);
            if (_cards[i].Group != null)
                _cards[i].Group.alpha = (index < 0 || on) ? 1f : UnselectedCardAlpha;

            var select = _cards[i].Select;
            if (select == null) continue;
            if (on)
            {
                select.gameObject.SetActive(true);
                select.SetState(ThinFrameState.Ready, animate: true);   // 이미 Ready면 그대로(연출이 다시 돌지 않는다)
            }
            else
            {
                select.SetState(ThinFrameState.Idle, animate: false);
                select.gameObject.SetActive(false);
            }
        }

        bool hasSel = index >= 0;
        // 고른 룬이 들어갈 수 있는 빈 칸만 판에서 밝힌다 — 「이걸 고르면 판 어디로 가는가」.
        _board?.Highlight(hasSel && _candidates != null && index < _candidates.Count ? _candidates[index].data : null);
        if (_confirmLabel != null)
        {
            _confirmLabel.color = hasSel ? ShopUIStyle.TextPrimary : ShopUIStyle.TextDim;
            if (hasSel) _confirmLabel.text = ConfirmText;   // 미선택 안내를 띄웠다면 되돌린다
        }
        if (_confirmBtnImg != null)
            _confirmBtnImg.color = _themedButtons ? (hasSel ? UITheme.CtaTint : UITheme.CtaTintOff)
                                 : _skinned ? (hasSel ? Color.white : new Color(1f, 1f, 1f, 0.5f))
                                            : (hasSel ? ShopUIStyle.GoldPillBg : ShopUIStyle.BandFill);
        UIAffordGlow.Set(_confirmBtnImg, hasSel);   // 고르면 확정 버튼에 은은한 불(09-29)
    }

    /// <summary>
    /// [선택][넘기기]를 전 화면 공통 베벨로(금 · 먹빛). 납품 아트는 밝은 청색 SF 베벨이라 갈색·금 UI 사이에서 혼자 튀었고
    /// (청색은 무기 전용 — 의뢰서 §3), 모서리를 깎은 아트 뒤로 코드 빌더의 네모 테두리가 비쳤다(09-28 UI 톤 통일).
    /// </summary>
    private void ApplyThemeButtons()
    {
        // 바탕 일러스트(창 채움) — 빌드 · 구운 경로 둘 다 여기를 지난다.
        if (transform.Find("Window/Fill") is Transform fill && fill.TryGetComponent<Image>(out var bgImg) && bgImg.sprite != null)
            bgImg.color = CaveTint;

        var skipOuter = ShopUIStyle.FindDeep(transform, "SkipBtn");
        var skipInner = skipOuter != null ? skipOuter.Find("Fill") : null;
        if (!UITheme.StyleFrameButton(_confirmBtnImg, UITheme.CtaTintOff)) return;
        if (skipInner != null && skipInner.TryGetComponent<Image>(out var skipImg))
            UITheme.StyleFrameButton(skipImg, UITheme.SecondaryTint);
        _themedButtons = true;

        foreach (var btn in new[] { _confirmBtnImg != null ? _confirmBtnImg.transform : null, skipInner })
        {
            var lbl = btn != null ? btn.Find("Label") : null;
            if (lbl != null && lbl.TryGetComponent<TMP_Text>(out var t)) TMPOutlineHelper.ApplySoftShadow(t);
        }
    }

    /// <summary>
    /// 창 · 제목 · 카운터 · 버튼을 톤 v2(10-11, 비교 리서치 B §2)로 — 빌드 · 구운 경로 둘 다 마지막에 한 번.
    /// 구운 프리팹은 맞춤 상한 · 글꼴 · 버튼 크기가 굳어 있어 빌더만 고치면 게임에 반영되지 않는다 — 그래서 쓰는 곳에서 덮어쓴다(재굽기 불필요).
    /// ① 창 맞춤 상한 1.64 → 1.3(화면 88% → 약 55%) ② 글자 Noto · 가짜 굵게 없음 ③ [선택] 220×44 · [넘기기] 같은 높이로 가운데 묶음.
    /// </summary>
    private void ApplyThinChrome()
    {
        if (_windowRoot == null) return;
        var windowRT = _windowRoot.parent as RectTransform;
        ApplyWindowFrame(windowRT);
        if (windowRT != null && windowRT.TryGetComponent<UIWindowFitter>(out var fitter))
        {
            fitter.Configure(maxScale: FitMaxScale);   // 구운 값은 컨텐츠 화면 상한(3) — 여백 0.94 · 글꼴 배율 켬은 기본값 그대로
            fitter.SetBaseSize(new Vector2(WindowW, WindowH));   // 구운 창은 1100×620 — 넓힌 목업으로(10-19)
            fitter.Fit();
        }
        // 구운 자식은 옛 창(1100×620) 비율 앵커 — 넓힌 창에서 늘어나지 않게 머리 · 확률 막대를 목업 px로 다시 놓는다(10-19)
        // 10-20 — 돌 틀 안쪽 선(목업 ≈ 21)에서 15~20px 안으로(제목 띠 양끝 · 윗선이 틀에 닿았다)
        PlaceTopLeft(_windowRoot.Find("TitleBar") as RectTransform, 40f, 30f, new Vector2(WindowW - 80f, 38f));
        PlaceTopLeft(_windowRoot.Find("Title") as RectTransform, 62f, 33f, new Vector2(600f, 32f));
        PlaceTopLeft(_windowRoot.Find("Counter") as RectTransform, WindowW - 62f - 420f, 35f, new Vector2(420f, 28f));
        if (GetComponentInChildren<OddsBarView>(true) is OddsBarView odds && odds.transform.parent == _windowRoot)
            PlaceBottomLeft((RectTransform)odds.transform, 44f, 42f, new Vector2(OddsBarW, OddsBarH));
        // 창 글자는 맞춤 배율만큼 이미 커져 있다(UIProportional.ScaleFonts) — 목업 크기에 같은 배율을 곱한다.
        float k = windowRT != null && windowRT.TryGetComponent<UIFontScaleMemo>(out var memo) ? memo.Applied : 1f;

        // 창 바로 아래 자식만 찾는다 — 확률 막대(OddsBarView)도 「Title」 자식을 가진다.
        if (_windowRoot.Find("Title") is Transform titleT && titleT.TryGetComponent<TMP_Text>(out var title))
        {
            UITheme.StyleThinText(title, UIWeight.Light, TitleSize * k, ShopUIStyle.TextPrimary, shadow: true);
            title.enableAutoSizing = false;   // Noto 줄 높이(≈1.45em)가 칸(32)을 넘어 자동 크기가 글자를 줄인다 — 고정 문구라 끈다
            title.alignment        = TextAlignmentOptions.MidlineLeft;
        }
        if (_counterText != null)
        {
            UITheme.StyleThinText(_counterText, UIWeight.Regular, SubSize * k, ShopUIStyle.TextDim, shadow: true);
            _counterText.alignment = TextAlignmentOptions.MidlineRight;
        }

        // 버튼 — 목업 px를 비율 앵커로(구운 계층과 같은 방식이라 창 맞춤을 그대로 따라간다).
        float left = -(ConfirmBtnSize.x + BtnGap + SkipBtnSize.x) * 0.5f;
        float confirmX = left + ConfirmBtnSize.x * 0.5f;
        float skipX    = left + ConfirmBtnSize.x + BtnGap + SkipBtnSize.x * 0.5f;
        var skipOuter = _windowRoot.Find("SkipBtn") as RectTransform;
        var hintRT    = _windowRoot.Find("SkipHint") as RectTransform;
        PlaceBottom(_windowRoot.Find("ConfirmBtn") as RectTransform, confirmX, BtnBottom, ConfirmBtnSize);
        PlaceBottom(skipOuter, skipX, BtnBottom, SkipBtnSize);
        PlaceBottom(hintRT,    skipX, 26f, new Vector2(SkipBtnSize.x, 22f));

        if (_confirmLabel != null)
            UITheme.StyleThinText(_confirmLabel, UIWeight.Medium, ButtonSize * k, _confirmLabel.color, shadow: true);
        if (skipOuter != null && skipOuter.Find("Fill/Label") is Transform skipLblT && skipLblT.TryGetComponent<TMP_Text>(out var skipLbl))
            UITheme.StyleThinText(skipLbl, UIWeight.Medium, SubSize * k, ShopUIStyle.TextDim, shadow: true);
        if (hintRT != null && hintRT.TryGetComponent<TMP_Text>(out var hint))
            UITheme.StyleThinText(hint, UIWeight.Regular, LabelSize * k, ShopUIStyle.TextDim, shadow: true);
    }

    /// <summary>
    /// 10-19 사용자 「룬 획득 팝업 틀도 힉스필드로 톤에 맞게」 — 흑요석 돌 · 은금 상감 · 보라/청록 안쪽 빛 9칸 틀(<see cref="RuneSelectSkinSO.windowFrame"/>).
    /// 있으면 납품 통짜 일러스트 대신 창 채움에 깔고 바깥 2px 테 선은 끈다(틀이 테를 맡는다). 구운 프리팹도 여기서 덮어쓴다.
    /// 모서리 <see cref="WindowFrameCorner"/> — 돌 테 ≈ 21 · 안쪽 빛 선 안쪽에 확률 막대(x 24)가 선다.
    /// </summary>
    private void ApplyWindowFrame(RectTransform windowRT)
    {
        var art = UISkin.RuneSelect?.windowFrame;
        if (art == null || !_windowRoot.TryGetComponent<Image>(out var fill)) return;
        fill.sprite = art;
        fill.type   = Image.Type.Sliced;
        fill.color  = Color.white;
        fill.pixelsPerUnitMultiplier = art.border.x > 0f ? art.border.x / WindowFrameCorner : 1f;
        if (windowRT != null && windowRT.TryGetComponent<Image>(out var line)) line.color = Color.clear;
        ApplyWindowInterior();
    }

    /// <summary>
    /// 10-29 사용자 「룬 획득 팝업 — 배경의 아쉬운 건 힉스필드로」 — 틀 9칸 안쪽이 무늬 없는 남색 판이었다.
    /// 흑요석 판 그림(<see cref="RuneSelectSkinSO.windowInterior"/>)을 틀 안쪽에 맨 뒤로 깐다(구운 프리팹도 여기서 · 두 번 깔지 않는다).
    /// </summary>
    private void ApplyWindowInterior()
    {
        var art = UISkin.RuneSelect?.windowInterior;
        if (art == null || _windowRoot == null || _windowRoot.Find("InteriorArt") != null) return;
        var img = ShopUIStyle.MakeImage(_windowRoot, "InteriorArt", Color.white);
        img.sprite = art;
        img.type = Image.Type.Simple;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(InteriorInset, InteriorInset);
        rt.offsetMax = new Vector2(-InteriorInset, -InteriorInset);
        rt.SetAsFirstSibling();
    }

    /// <summary>창 목업 왼쪽 위 기준 사각형(왼쪽 · 위 여백 · 크기)을 비율 앵커로 — 창 맞춤을 그대로 따라간다.</summary>
    private static void PlaceTopLeft(RectTransform rt, float left, float top, Vector2 size)
    {
        if (rt == null) return;
        rt.anchorMin = new Vector2(left / WindowW, 1f - (top + size.y) / WindowH);
        rt.anchorMax = new Vector2((left + size.x) / WindowW, 1f - top / WindowH);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    /// <summary>창 목업 왼쪽 아래 기준 사각형(왼쪽 · 아래 여백 · 크기)을 비율 앵커로.</summary>
    private static void PlaceBottomLeft(RectTransform rt, float left, float bottom, Vector2 size)
    {
        if (rt == null) return;
        rt.anchorMin = new Vector2(left / WindowW, bottom / WindowH);
        rt.anchorMax = new Vector2((left + size.x) / WindowW, (bottom + size.y) / WindowH);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    /// <summary>창(목업 <see cref="WindowW"/>×<see cref="WindowH"/>) 아래 가운데 기준 사각형 — 가운데 x · 바닥 y · 크기를 비율 앵커로 굳힌다.</summary>
    private static void PlaceBottom(RectTransform rt, float centerX, float bottom, Vector2 size)
    {
        if (rt == null) return;
        rt.anchorMin = new Vector2(0.5f + (centerX - size.x * 0.5f) / WindowW, bottom / WindowH);
        rt.anchorMax = new Vector2(0.5f + (centerX + size.x * 0.5f) / WindowW, (bottom + size.y) / WindowH);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private void RefreshCounter()
    {
        if (_counterText == null) return;
        int staged = _inventory?.StagingItems?.Count ?? 0;
        int cap    = RunItemInventory.StagingCapacity;
        // 10-19 P2 ③ ⑦: 「배치 n」은 오른쪽 미니 판 「룬 n / 20」과 같은 수라 뺐다 · 가득이면 고르기 전에 알린다(고르면 넘침 칸으로 받는다)
        _counterText.text = staged >= cap ? $"보관함 <color=#E8BA54>{staged}/{cap} 가득</color>" : $"보관함 {staged}/{cap}";
    }

    // ── 버튼 ──

    private void OnConfirmClicked()
    {
        if (_closing) return;
        if (_revealing)
        {
            // 연출 중 확정 — 여기서 바로 끝 상태로 세운다. 시퀀스의 마무리(SnapToRest)를 기다리면
            // 한 프레임 뒤에 날아가던 카드를 제자리로 되돌려 버린다.
            SkipReveal();
            SnapToRest();
            if (_selected >= 0) ApplySelectionVisual(_selected);
        }

        if (_selected < 0 || _candidates == null || _selected >= _candidates.Count)
        {
            // 미선택 — 예전엔 조용히 return이라 버튼이 죽은 것으로 읽혔다. 무엇이 빠졌는지 버튼이 직접 말한다.
            ShopUIStyle.PlaySfx("shop_reject");
            if (_confirmLabel != null) _confirmLabel.text = "카드를 먼저 고른다";
            return;
        }

        Managers.Sound?.PlayUiEvent(SoundEvent.RuneTake);
        var item = _candidates[_selected].data;
        Result  = item;
        Skipped = false;
        _closing = true;

        // 보관함이 가득이어도 받는다 — 넘친 칸에 들어가고, 룬판에서 놓거나 분해해야 닫힌다(10-01 「보류」 폐지).
        // 10-19: 가득일 때의 노란 공지는 걷었다 — 버튼이 이미 [룬판에서 바꾸기]라 말했고, 열리는 룬판이 「놓거나 하나를 분해」를 말한다.
        bool added = _inventory != null && _inventory.AddToStagingOverflow(item);

        ConfirmSequenceAsync(item, added).Forget();
    }

    /// <summary>고른 카드가 보관함 카운터로 날아가 +1 된 뒤 닫힌다. 연출이 꺼져 있거나 중간에 끊겨도 닫기는 반드시 한다.</summary>
    private async UniTaskVoid ConfirmSequenceAsync(RuntimeItemData item, bool added)
    {
        try
        {
            if (!RewardPresentation.IsOff && _selected >= 0 && _selected < _cards.Count)
                await FlyToCounterAsync(_cards[_selected], added, destroyCancellationToken);
        }
        catch (OperationCanceledException) { }
        finally { FinishConfirm(item, added); }
    }

    private async UniTask FlyToCounterAsync(CardView card, bool added, CancellationToken ct)
    {
        if (card?.Rt == null || _counterText == null) return;

        for (int i = 0; i < _cards.Count; i++)
            if (_cards[i] != card && _cards[i].Group != null) _cards[i].Group.alpha = LegendDim;

        Vector3 from = card.Rt.position, to = _counterText.rectTransform.position;
        float s0 = card.Rt.localScale.x;
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Min(1f, t + Time.unscaledDeltaTime / FlyDur);
            float e = t * t;   // 빨려 들어가듯 가속
            card.Rt.position   = Vector3.Lerp(from, to, e);
            card.Rt.localScale = Vector3.one * Mathf.Lerp(s0, 0.12f, e);
            card.Group.alpha   = 1f - e * 0.9f;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        card.Group.alpha = 0f;

        if (!added) return;
        RefreshCounter();
        var col = _counterText.color;
        _counterText.color = LegendGold;
        await UIJuice.PunchAsync(_counterText.rectTransform, 0.18f, 0.22f, ct);
        _counterText.color = col;
    }

    private void FinishConfirm(RuntimeItemData item, bool added)
    {
        // 닫기(ClosePopupUI)를 resolve(TrySetResult)보다 먼저 — 순서가 뒤집히면 이벤트방에서 시간이 고착된다.
        // TrySetResult는 대기자(ClearRewardTrigger의 다중 라운드 3지선다)를 동기로 이어 곧바로 다음 라운드
        // 팝업을 push한다. 그 뒤에 ClosePopupUI를 부르면 이 팝업은 더 이상 스택 최상단이 아니라 닫기가
        // 무시되고("Close Popup Failed!"), 살아있는 좀비로 남아 BlocksGameplay가 계속 걸린 채 timeScale=0이
        // 영구 고착된다(이벤트방 Gold/Platinum 2라운드 이상 보상에서 재현). 그리드는 다음 라운드 팝업보다
        // 밑에 깔리도록 resolve 전에 연다.
        if (this != null) ClosePopupUI();

        // 챌린지 다중 보상은 마지막 라운드에만 룬판을 연다(10-19) — 넘쳤으면 정리해야 하니 늘 연다
        if (_openBoard || (_inventory != null && _inventory.IsOverCapacity))
        {
            if (UI_GridPanel.Instance == null)
                Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            if (UI_GridPanel.Instance != null)
                UI_GridPanel.Instance.ShowWithNewItem(item);
        }

        _interactionTcs?.TrySetResult();
    }

    private void OnSkipClicked()
    {
        if (_closing) return;
        // 연출 중 누름(또는 연출 중에 눌러 막 끝난 뒤 뗀 클릭)은 연출만 끝 상태로 — 카드를 보기도 전에 보상을 버리지 않는다(10-18 분석 P1).
        if (_revealing || Time.unscaledTime - _revealEndedAt < SkipGuardSec)
        {
            SkipReveal();
            return;
        }
        Managers.Sound?.PlayUiEvent(SoundEvent.UiClose);   // 넘기기
        Result  = null;
        Skipped = true;
        _closing = true;
        // 닫기를 resolve보다 먼저 — FinishConfirm과 동일 이유(다중 라운드 좀비 팝업 → timeScale 고착 방지).
        ClosePopupUI();
        _interactionTcs?.TrySetResult();
    }

    // ── Helpers ──

    /// <summary>
    /// 구워진 프리팹을 잇는다 — <b>계층·좌표·아트는 프리팹이 갖고, 코드는 배선만 한다.</b>
    /// 직렬화되지 않는 것(코드가 붙인 클릭 리스너·런타임 목록)만 되살린다.
    /// 이름으로 찾는다 — 빌더가 붙이던 이름 그대로 프리팹에 굳어 있다.
    /// </summary>
    private void BindBakedHierarchy()
    {
        // 아트 적용 여부는 구운 프리팹에 남은 <b>스프라이트 자체</b>로 판정한다.
        // 빌드 경로에서만 세우던 값이라 잇지 않으면 영영 false로 남고,
        // 아트가 멀쩡히 있는데도 무지 배경용 어두운 색(BandFill·GoldPillBg)이
        // 스프라이트에 곱해져 「선택」 버튼과 카드 테두리가 검게 보인다.
        _skinned = _confirmBtnImg != null && _confirmBtnImg.sprite != null;
        ApplyThemeButtons();
        // 창을 1.3으로 다시 맞춘 <b>뒤에</b> 확률 막대를 짓는다 — 막대는 처음 지을 때의 크기(px)로 한 번만 배치한다.
        ApplyThinChrome();

        RefreshOdds();

        BindClick("ConfirmBtn", OnConfirmClicked);
        BindClick("SkipBtn",    OnSkipClicked);
    }

    private void BindClick(string name, Action onClick)
    {
        var t = ShopUIStyle.FindDeep(transform, name);
        if (t != null) AddClick(t.gameObject, onClick);
        else Debug.LogWarning($"[UI_RuneSelectPopup] 배선 실패 — 「{name}」을 못 찾았다. 버튼이 죽는다.");
    }

    private static void AddClick(GameObject go, Action onClick)
    {
        var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
        ShopUIStyle.ApplyButtonColors(btn);
        btn.onClick.AddListener(() => onClick?.Invoke());
    }

    private static int CellCount(int shapeId)
    {
        var entry = Managers.RuneData?.GetShape(shapeId);
        if (entry == null) return 0;
        var offsets = RuneDataManager.ParseCellOffsets(entry);
        return offsets?.Length ?? 0;
    }
}
