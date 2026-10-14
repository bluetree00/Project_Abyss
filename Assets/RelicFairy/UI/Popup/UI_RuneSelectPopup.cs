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
    private const float WindowW = 1100f;
    private const float WindowH = 620f;
    private const float CardW   = 300f;   // 카드 폭 상한 — 후보가 많으면 이 아래로 줄어든다
    private const float CardH   = 420f;   // 의뢰서 380 + 효과 4행분 40
    private const float CardGap = 24f;
    private const float CardSideMargin = 40f;   // 카드 열 좌우 여백(창 안쪽)
    // 「지금 룬판」 — 카드 줄 오른쪽 끝, 카드와 같은 높이. 13열 판이 칸 13.7px로 들어가는 폭이다(카드는 240으로 준다).
    private const float BoardPanelW = 228f;
    // 「셋 중 하나」 팝업 창 맞춤 상한(10-11 v2) — 컨텐츠 화면 상한(3)이면 1.64배로 커져 화면 88%를 덮었다. 1.3 = 약 55%(유물 진화와 같다).
    private const float FitMaxScale = 1.3f;
    // [선택] · [넘기기] — 목업 px(창 아래 가운데 기준). 선택 220×44(리서치 B §2 P3) · 넘기기도 같은 높이 · 두 버튼 사이 23.
    private static readonly Vector2 ConfirmBtnSize = new(220f, 44f);
    private static readonly Vector2 SkipBtnSize    = new(180f, 44f);
    private const float BtnBottom = 44f;
    private const float BtnGap    = 23f;

    // 글자(10-11 v2 — Noto · 가짜 굵게 없음, 목업 px): 제목 300 24 · 이름 500 22 · 효과 400 18 · 버튼 500 20 · 보조 17 · 라벨 15.
    private const float TitleSize  = 24f;
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
    private const float CardY   = 32f;    // 카드 위 68 / 아래 132(버튼 줄 56 + 여백) — 창 중심 기준

    // 카드 앞면 배치(카드 위에서부터 — 10-06 개편: 리본 / 등급 · 속성 / 룬 실물 판 / 이름 / (계열) / 효과 칸 / 배지)
    private const float RaysFrac        = 1.15f;  // 전설 광선 = 판 폭의 배수
    private const float RibbonY         = 9f;     // 속성 리본(카드 맨 위 한 줄)
    private const float RibbonH         = 8f;
    private const float HeadY           = 22f;    // 등급 글자 · 속성 칩 줄
    private const float ChipH           = 24f;
    private const float PanelY          = 52f;    // 룬 실물 판 — 카드의 3분의 1 남짓(의뢰서 「모양이 1순위」)
    private const float PanelH          = 146f;
    private const float PanelInset      = 12f;
    private const float CountRowH       = 18f;    // 판 아래 「N칸」 글자 자리
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
    private const float RaysTurnSec    = 14f;
    private const float BobPeriod      = 3.2f;
    private const float BobAmp         = 4f;

    // 청 수정 동굴 바탕을 인디고로 누른다 — 다른 화면(인디고 글래스)과 온도를 맞추고 그림은 남긴다(톤 통일 ④, 09-29).
    private static readonly Color CaveTint      = new(0.66f, 0.62f, 0.80f, 1f);
    private static readonly Color CardBorderArt = new(0.62f, 0.62f, 0.66f, 1f);   // 카드 테 아트 — 고름과 무관하게 한 색(고른 표시는 금 테 하나)
    private static readonly Color NoColor       = new(0.92f, 0.40f, 0.33f, 1f);
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
        if (!_revealing) return;
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.F) || Input.GetMouseButtonDown(0))
            SkipReveal();
    }

    // ── Public API ──

    /// <summary>버튼 클릭 즉시 resolve — 애니메이션을 기다리지 않는다.</summary>
    public UniTask WaitForInteractionAsync(CancellationToken ct)
    {
        _interactionTcs = new UniTaskCompletionSource();
        return _interactionTcs.Task.AttachExternalCancellation(ct);
    }

    /// <summary>후보를 주입해 카드를 구성한다. ShowPopupUIAndGetAsync 직후 호출.</summary>
    public void Setup(List<(RuntimeItemData data, ItemSO so)> candidates, RunItemInventory inventory)
    {
        _candidates  = candidates;
        _inventory   = inventory;
        _closing     = false;

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

        if (_veil == null) _veil = ShopUIStyle.FindDeep(transform, "Veil")?.GetComponent<Image>();
        if (_veil != null && _veilAlpha < 0f) _veilAlpha = _veil.color.a;

        // 선택 초기화는 카드를 짓기 <b>전에</b> — 뒤에 하면 선택 강조가 숨겨 둔 카드의 투명도·배율을 1로 되돌려
        // 아직 내려오지 않은 카드가 뒷면으로 미리 보인다.
        SetSelected(-1);
        BuildCards();
        RefreshCounter();
        RefreshOdds();   // 열 때마다 — 챕터 · 방 종류가 바뀌었다(창을 다시 쓰면 지난 값이 남는다, 10-10)

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
            if (RewardPresentation.IsOff) return;
            bool brief = RewardPresentation.Mode == RewardPresentationMode.Brief;

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
                FlipAsync(card, ct).Forget();
                await HoldOrSkip(brief ? BriefStagger : FlipStagger, ct);
            }
            if (!lastAwaited) await HoldOrSkip(FlipHalf * 2f, ct);   // 마지막 카드가 다 펴질 때까지
        }
        catch (OperationCanceledException) { return; }
        finally
        {
            _revealing = false;
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

    private async UniTaskVoid SparksAsync(CardView card, CancellationToken ct)
    {
        if (_fxRoot == null) return;
        var origin = card.BasePos + new Vector2(0f, CardH * 0.2f);
        var sparks = new RectTransform[SparkCount];
        var dirs   = new Vector2[SparkCount];
        var imgs   = new Image[SparkCount];
        for (int i = 0; i < SparkCount; i++)
        {
            var img = ShopUIStyle.MakeImage(_fxRoot, "Spark", LegendGold);
            img.sprite = SoftDot;
            imgs[i] = img;
            sparks[i] = img.rectTransform;
            ShopUIStyle.Anchor(sparks[i], new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                               origin, Vector2.one * UnityEngine.Random.Range(8f, 14f));
            float a = UnityEngine.Random.value * Mathf.PI * 2f;
            dirs[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * UnityEngine.Random.Range(60f, 200f);
        }

        try
        {
            float t = 0f;
            while (t < 1f && !_revealSkipped)
            {
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / SparkDur);
                float e = EaseOutCubic(t);
                for (int i = 0; i < SparkCount; i++)
                {
                    sparks[i].anchoredPosition = origin + dirs[i] * e;
                    sparks[i].localScale       = Vector3.one * Mathf.Lerp(1f, 0.2f, t);
                    imgs[i].color              = WithAlpha(LegendGold, 1f - t);
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            for (int i = 0; i < SparkCount; i++)
                if (sparks[i] != null) Destroy(sparks[i].gameObject);
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
        title.text = "룬 획득 — 하나를 고르세요";

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

        var kind = GameRunBootstrapper.Instance?.Run?.CurrentRoomKind ?? RoomPlanKind.Normal;
        var (rare, epic, legendary) = RoomRewardTable.For(kind).Weights.Normalized();
        // 굴림은 제단에서 안 연 등급을 한 단계씩 내린다(ClampRarity) — 표시도 같은 규칙으로 접는다(정제소와 동일).
        // 안 접으면 영웅·전설이 잠긴 세이브에서도 「Epic 9% · Legend 2%」가 뜬다.
        (rare, epic, legendary) = MemoryAltarService.FoldOdds(rare, epic, legendary);
        // 방 보상은 Common부터 — 네 등급을 다 보이고, 무엇이 등급을 올렸는지(챕터 · 시기 · 정예) 머리 오른쪽에(10-10).
        bar.ShowCommon = true;
        bar.SetBoost(RoomRewardTable.DescribeBoost(kind));
        bar.SetOdds(rare, epic, legendary, heated: false);
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
        float avail = WindowW - CardSideMargin * 2f - (n - 1) * CardGap - boardW;
        _cardW = Mathf.Min(CardW, avail / Mathf.Max(1, n));

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

            AddClick(cardRT.gameObject, () => SetSelected(idx));

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

            BuildFace(card.transform, data, view);
            BuildBack(card.transform, data.rarity, view);
            // 등급 테두리는 앞뒤 공통 — 채움 위, 카드 밖으로 살짝 걸친다(얼굴의 클리핑 밖).
            BuildGemFrame(cardRT, data.rarity);
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
        ShopUIStyle.Anchor(ribbon.rectTransform, top, top, top, new Vector2(0f, -RibbonY), new Vector2(_cardW - 28f, RibbonH));

        BuildHeader(face, data, rarityCol, elemCol);
        bool canPlace = BuildShapePanel(face, data, view, rarityCol, elemCol, elemIdx, legend);

        // 이름
        var nameText = ShopUIStyle.MakeText(face, "Name", NameSize, FontStyles.Normal,
            TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(nameText.rectTransform, top, top, top, new Vector2(0f, -NameY), new Vector2(_cardW - 24f, NameH));
        UITheme.StyleThinText(nameText, UIWeight.Medium, NameSize, ShopUIStyle.TextPrimary, shadow: true);
        nameText.text = data.displayName ?? data.itemId;
        FitSingleLine(nameText);
        PlaceRuneIcon(face, nameText, data);

        if (family) BuildFamilyChip(face, data);

        // 효과 칸 — 납품 「효과 칸」 판 위에 줄. 바닥은 배지 위.
        float fxTop = EffectsTop + (family ? FamilyRowH : 0f);
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

        // 룬 수 — 아래 가운데 글자 한 줄. 「룬 지금 + 1 / 상한」, 넘치면 붉게 — 경고일 뿐 선택은 막지 않는다(10-08 · 10-10 칸 → 룬 개수).
        // 10-11 v2: 초록 칠 바(테 1px)를 걷었다 — 세 카드에 같은 내용이라 초록이 「차이」가 아니라 「공통」을 강조했다. 색은 모자랄 때만.
        var badgeCol  = canPlace ? ShopUIStyle.TextDim : NoColor;
        var bottom    = new Vector2(0.5f, 0f);
        var badgeText = ShopUIStyle.MakeText(face, "PlaceBadge", LabelSize, FontStyles.Normal, TextAlignmentOptions.Center, badgeCol);
        ShopUIStyle.Anchor(badgeText.rectTransform, bottom, bottom, bottom,
                           new Vector2(0f, BadgeBottom), new Vector2(_cardW - 40f, BadgeH));
        UITheme.StyleThinText(badgeText, UIWeight.Regular, LabelSize, badgeCol, shadow: true);
        badgeText.text = RuneBlockBudget.SameLabel(data) ?? RuneBlockBudget.Label();   // 같은 룬이 이미 2개면 그 말이 먼저(10-14)
        FitSingleLine(badgeText);
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
        nrt.anchoredPosition = new Vector2(left + NameIcon + NameIconGap + tw * 0.5f, -NameY);

        var icon = ShopUIStyle.MakeImage(face, "RuneIcon", Color.white);
        icon.sprite         = art;
        icon.preserveAspect = true;
        icon.raycastTarget  = false;
        ShopUIStyle.Anchor(icon.rectTransform, top, top, new Vector2(0.5f, 0.5f),
                           new Vector2(left + NameIcon * 0.5f, -NameY - NameH * 0.5f), Vector2.one * NameIcon);
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
        if (skin?.runeTile != null) { panel.sprite = skin.runeTile; panel.color = Color.white; }
        ShopUIStyle.Anchor(panel.rectTransform, top, top, top, new Vector2(0f, -PanelY), new Vector2(pw, PanelH));
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
            ShopUIStyle.Anchor(glow.rectTransform, mid, mid, mid, Vector2.zero, new Vector2(pw * 0.95f, PanelH * 1.1f));
            view.Glow = glow;
        }

        var shape = ShopUIStyle.MakeRect(prt, "Shape").GetComponent<RectTransform>();
        ShopUIStyle.Anchor(shape, mid, mid, mid, Vector2.zero, new Vector2(pw, PanelH));
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
            (PanelH - PanelInset * 2f - CountRowH - (rows - 1) * CellGap) / rows);
        float w = cols * cell + (cols - 1) * CellGap, h = rows * cell + (rows - 1) * CellGap;
        var origin = new Vector2(-w * 0.5f + cell * 0.5f, h * 0.5f - cell * 0.5f + CountRowH * 0.5f);

        var eg = ShopUIStyle.MakeImage(shape, "ElementGlow", WithAlpha(elemCol, canPlace ? 0.38f : 0.15f));
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
        if (!canPlace) tint = new Color(tint.r * 0.5f, tint.g * 0.5f, tint.b * 0.5f, 0.75f);
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
        var fc = MakeChip(face, "FamilyChip", $"{BuildFamilyRules.Label(fam)} {(statOnly ? "+0.5" : "+1")} → {now}{tail}", famCol, WithAlpha(famCol, 0.40f), out _);
        fc.anchoredPosition = new Vector2(0f, -FamilyY);
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

    /// <summary>룬 수 짧은 꼴 — 「룬 (지금 + 1)/상한」 · 넘치면 「넘침」(RuneBlockBudget.Label과 같은 판정).</summary>
    private static string CompactBudgetLabel()
    {
        int used = RuneBlockBudget.Used;
        string s = $"룬 {used + 1}/{RuneBlockBudget.Max}";
        return RuneBlockBudget.Fits(used) ? s : s + " 넘침";
    }

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

        var fill = ShopUIStyle.MakeFrame(face, "ProcChip", WithAlpha(col, 0.40f), ChipFill, 1f);
        var rt   = (RectTransform)fill.transform.parent;
        var label = ShopUIStyle.MakeText(fill.transform, "Label", ProcChipSize, FontStyles.Normal, TextAlignmentOptions.Center, col);
        UITheme.StyleThinText(label, UIWeight.Medium, ProcChipSize, col);
        label.enableAutoSizing = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        ShopUIStyle.Stretch(label.rectTransform);

        // 룬 수 글은 긴 꼴(「룬 0 + 1 / 20」) → 좁은 카드(4장 · 174px)면 짧은 꼴(「룬 1/20」 — 고른 뒤 수)로 줄여 칩 자리를 낸다(10-14 SYN_6b)
        string badgeText = badge != null ? badge.text : null;
        string[] badges  = badge != null ? new[] { badgeText, RuneBlockBudget.FitsSame(data) ? CompactBudgetLabel() : $"같은 룬 {RuneBlockBudget.SameExcept(data)}/{RuneBlockBudget.MaxSame}" } : new string[] { null };
        string pick = null; float w = 0f, badgeW = 0f;
        foreach (var bt in badges)
        {
            badgeW = bt != null ? Mathf.Ceil(badge.GetPreferredValues(bt).x) + 2f : 0f;
            float avail = _cardW - ProcChipEdge * 2f - badgeW - ProcChipGap;
            foreach (var cand in new[] { full, mid, name })
            {
                w = Mathf.Ceil(label.GetPreferredValues(cand, 999f, ChipH).x + ProcChipPad * 2f);
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

    /// <summary>효과 줄의 수치를 금색으로(10-06) — 위험 줄(▼)은 그대로. 색 태그 안 숫자(#E8BA54 · =80%)는 건드리지 않는다.</summary>
    private static void GildNumbers(TMP_Text label)
    {
        if (label == null || string.IsNullOrEmpty(label.text) || label.text.StartsWith("▼")) return;
        label.text = System.Text.RegularExpressions.Regex.Replace(label.text,
            @"(?<![<#=\w.])([+\-−]?\d+(?:\.\d+)?%?)", "<color=#FFD54A>$1</color>");
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

        // 효과가 많은 룬은 행이 박스를 넘어 아래 배지·카드 밖까지 밀고 나갔다(레이아웃이 뭉개진 주범).
        // 박스에 들어가는 만큼만 그리고, 잘린 개수는 마지막 줄에 알린다.
        int shown = 0, hidden = 0;
        foreach (var slot in data.effects)
        {
            if (string.IsNullOrEmpty(slot.effectType)) continue;
            if (shown >= MaxEffectRows) { hidden++; continue; }
            GildNumbers(EffectRowWidget.Create(parent, style, slot).Label);
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
                _cards[i].Border.color = _skinned ? CardBorderArt : ShopUIStyle.CardBorder;
            if (_cards[i].Fill != null)
                _cards[i].Fill.color = _skinned ? Color.white : ShopUIStyle.CardFill;

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
            if (hasSel) _confirmLabel.text = "선택";   // 미선택 안내를 띄웠다면 되돌린다
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
        if (windowRT != null && windowRT.TryGetComponent<UIWindowFitter>(out var fitter))
        {
            fitter.Configure(maxScale: FitMaxScale);   // 구운 값은 컨텐츠 화면 상한(3) — 여백 0.94 · 글꼴 배율 켬은 기본값 그대로
            fitter.Fit();
        }
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
        PlaceBottom(hintRT,    skipX, 18f, new Vector2(SkipBtnSize.x, 22f));

        if (_confirmLabel != null)
            UITheme.StyleThinText(_confirmLabel, UIWeight.Medium, ButtonSize * k, _confirmLabel.color, shadow: true);
        if (skipOuter != null && skipOuter.Find("Fill/Label") is Transform skipLblT && skipLblT.TryGetComponent<TMP_Text>(out var skipLbl))
            UITheme.StyleThinText(skipLbl, UIWeight.Medium, SubSize * k, ShopUIStyle.TextDim, shadow: true);
        if (hintRT != null && hintRT.TryGetComponent<TMP_Text>(out var hint))
            UITheme.StyleThinText(hint, UIWeight.Regular, LabelSize * k, ShopUIStyle.TextDim, shadow: true);
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
        int placed = _inventory?.PlacedItems?.Count ?? 0;
        _counterText.text = $"보관함 {staged}/{RunItemInventory.StagingCapacity}   ·   배치 {placed}";
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
            if (_confirmLabel != null) _confirmLabel.text = "카드를 고르세요";
            return;
        }

        Managers.Sound?.PlayUiEvent(SoundEvent.RuneTake);
        var item = _candidates[_selected].data;
        Result  = item;
        Skipped = false;
        _closing = true;

        // 보관함이 가득이어도 받는다 — 넘친 칸에 들어가고, 룬판에서 놓거나 분해해야 닫힌다(10-01 「보류」 폐지).
        bool full  = _inventory != null && _inventory.IsStagingFull;
        bool added = _inventory != null && _inventory.AddToStagingOverflow(item);
        if (full)
            ItemEffectVfxHelper.ShowNotice(
                $"<color=#FFCC44>보관함 가득</color> ({RunItemInventory.StagingCapacity}칸) — 룬판에서 하나를 놓거나 분해한다");

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

        if (UI_GridPanel.Instance == null)
            Managers.UI?.ShowOverlayUI<UI_GridPanel>();
        if (UI_GridPanel.Instance != null)
        {
            UI_GridPanel.Instance.ShowWithNewItem(item);
        }

        _interactionTcs?.TrySetResult();
    }

    private void OnSkipClicked()
    {
        if (_closing) return;
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
