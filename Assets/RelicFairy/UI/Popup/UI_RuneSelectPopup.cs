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
/// 카드 구성(시안 「룬 픽업」 2026-09-19):
///  - <b>룬 문양</b> — 카드 위쪽 43%. 컨셉별 문양(<see cref="RuneArt.ResolveRuneIcon"/>) 뒤에 등급색 빛.
///  - 이름 / 등급·칸수 칩 / 속성 칩 / 효과
///  - 아래에 <b>작은 모양</b>과 <b>배치 가능 배지</b> — 인접 제약 때문에 실제로 못 놓는 룬이 생긴다. 경고일 뿐 선택은 막지 않는다.
///  - 등급 테두리 = 서약 카드의 보석 테두리(철·블루·보라·골드). 뒷면의 문장 색도 등급이다.
///  - 카드 줄 오른쪽 = <b>지금 룬판</b>(<see cref="RuneBoardMini"/>). 후보는 3장 고정이라(10-01) 네 번째 자리를 판이 쓴다.
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

    // 등급 확률 막대 — 창 좌하단, [선택]/[넘기기] 좌측 여백에 앉힌다.
    private const float OddsBarW      = 236f;
    private const float OddsBarH      = 78f;
    private const float OddsBarMargin = 8f;
    private const float CardY   = 32f;    // 카드 위 68 / 아래 132(버튼 줄 56 + 여백) — 창 중심 기준

    // 카드 안 배치(카드 위에서부터, 폭 300 기준 — 좁은 카드는 폭 비율로 줄인다)
    private const float ArtCenterY  = 92f;    // 문양 칸(위 43% = 180) 한가운데
    private const float IconFrac    = 0.48f;  // 문양 = 카드 폭의 48%
    private const float LegendIconFrac = 0.56f;
    private const float GlowFrac    = 0.78f;
    private const float RaysFrac    = 1.30f;
    private const float NameY       = 184f;
    private const float ChipY       = 218f;
    private const float ChipH       = 24f;
    private const float EffectsY    = 250f;
    // 발동 계열 칩이 있으면 칩이 두 줄이 된다 — 효과 칸을 아래로 밀면 모양 칸과 겹치므로(09-29 실측) 위에서 자리를 만든다.
    private const float FamilyLift     = 22f;    // 이름 · 칩 줄을 이만큼 올린다
    private const float FamilyArtLift  = 8f;     // 문양도 조금 올리고
    private const float FamilyIconFrac = 0.88f;  // 조금 줄인다
    private const float FootTop        = CardH - 12f - FootH;   // 모양 칸 윗변(카드 위 기준) — 효과 칸의 바닥
    private const float MinEffectFont  = 12f;    // 넘칠 때 줄이는 하한
    // 09-25 사용자 「룬 획득 팝업에서 블록 모양도 알 수 있어야」: 모양 칸 44 → 76 · 칸 상한 14 → 22.
    // 효과는 1~2줄이 대부분이라 아래가 비어 있었다 — 그 자리를 모양에 준다(효과 4줄 = 82).
    private const float EffectsH    = 82f;
    private const float FootH       = 76f;    // 모양 칸 높이 — 3줄 모양(전설 12칸)이 22px 칸으로 들어간다
    private const float FootW       = 0.5f;   // 모양 칸 폭(카드 폭 비율)
    private const float MiniCellMax = 22f;
    private const float MiniGap     = 3f;
    // 서약 보석 테두리 조각(모서리 53×88 · 장식바 187×52)을 폭 300 카드에 0.8~0.93배로 얹는다.
    private const float GemCornerW  = 42f;
    private const float GemBarW     = 174f;
    private const float GemOutset   = 3f;     // 모서리가 카드 모서리를 살짝 감싼다

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

    private static readonly Color SelectBorder  = new(0.88f, 0.72f, 0.32f, 1f);
    // 청 수정 동굴 바탕을 인디고로 누른다 — 다른 화면(인디고 글래스)과 온도를 맞추고 그림은 남긴다(톤 통일 ④, 09-29).
    private static readonly Color CaveTint      = new(0.66f, 0.62f, 0.80f, 1f);
    private static readonly Color CardSelected  = new(0.20f, 0.17f, 0.10f, 1f);
    private static readonly Color OkColor       = new(0.47f, 0.84f, 0.60f, 1f);
    private static readonly Color NoColor       = new(0.92f, 0.40f, 0.33f, 1f);
    private static readonly Color ChipFill      = new(0.05f, 0.06f, 0.10f, 0.72f);
    private static readonly Color BackFill      = new(0.045f, 0.055f, 0.09f, 1f);
    private static readonly Color LegendGold    = new(1f, 0.84f, 0.47f, 1f);

    // 코드로 그리는 빛 — 부드러운 원·회전 광선·기둥. 아트 없이 한 번 만들어 모든 팝업이 같이 쓴다.
    private static Sprite _softDot, _rays, _beam;

    // ── 상태 ──
    private readonly List<CardView> _cards = new();
    private RectTransform _boardPanel;   // 「지금 룬판」 자리(카드 레이어의 자식)
    private CanvasGroup   _boardGroup;
    private RuneBoardMini _board;
    private List<(RuntimeItemData data, ItemSO so)> _candidates;
    private RunItemInventory _inventory;
    private const float SelectedCardScale   = 1.05f;   // 선택 카드 확대
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
        public RectTransform Icon;
        public Vector2       IconBase;
        public RectTransform Rays;
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
            Managers.Sound?.PlayUiAsync(SoundKey.Sfx.UiButton, 0.55f, spec.SfxPitch).Forget();

            await TweenAsync(FlipHalf, t => SetFlipScale(card, 1f - EaseInQuad(t), lift), ct);
            ShowFace(card, true);
            if (card.Glow != null) PulseGlowAsync(card.Glow, ct).Forget();
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

        Managers.Sound?.PlayUiAsync(SoundKey.Sfx.UiButton, 0.45f, 0.8f).Forget();
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
        // 크기를 가진 것은 테두리(부모)라 거기에 붙인다.
        window.transform.parent.gameObject.AddComponent<UIWindowFitter>()
              .Configure(maxScale: UIWindowFitter.ContentScreen);

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
        var title = ShopUIStyle.MakeText(_windowRoot, "Title", 26f, FontStyles.Bold,
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

        _confirmLabel = ShopUIStyle.MakeText(confirm.transform, "Label", 20f, FontStyles.Bold,
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
    /// </summary>
    private void BuildGemFrame(RectTransform cardRT, ItemRarity rarity)
    {
        var skin   = UISkin.RuneSelect;
        var corner = skin?.RarityCorner(rarity);
        var bar    = skin?.RarityBar(rarity);
        if (corner == null || bar == null) { BuildRarityFrame(cardRT, rarity); return; }

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

    /// <summary>폴백 — 등급 색 프레임 4조각(상·하·좌·우). 두께는 등급이 올라갈수록 두꺼워진다.</summary>
    private static void BuildRarityFrame(RectTransform cardRT, ItemRarity rarity)
    {
        float th = ShopUIStyle.RarityBorder(rarity);
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

        var word = ShopUIStyle.MakeText(back.transform, "RarityWord", 16f, FontStyles.Bold,
            TextAlignmentOptions.Center, col);
        ShopUIStyle.Anchor(word.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                           new Vector2(0f, CardH * 0.15f), new Vector2(_cardW - 24f, 22f));
        word.characterSpacing = 18f;
        word.text = rarity.ToString().ToUpperInvariant();
        FitSingleLine(word);
    }

    /// <summary>앞면 — 문양(빛·광선) / 이름 / 칩 / 효과 / 작은 모양 + 배치 배지.</summary>
    private void BuildFace(Transform card, RuntimeItemData data, CardView view)
    {
        var face = ShopUIStyle.MakeRect(card, "Face", typeof(RectMask2D)).GetComponent<RectTransform>();
        ShopUIStyle.Stretch(face);
        view.Face = face.gameObject;

        var rarityCol = ShopUIStyle.Rarity(data.rarity);
        bool legend = data.rarity == ItemRarity.Legendary;
        bool family = BuildFamilyRules.OfItem(data) != BuildFamily.None;
        float lift  = family ? FamilyLift : 0f;
        float artY  = ArtCenterY - (family ? FamilyArtLift : 0f);
        float iconK = family ? FamilyIconFrac : 1f;

        // 문양 칸 — 전설은 광선이 돈다(Update)
        var artAnchor = new Vector2(0.5f, 1f);
        if (legend)
        {
            var rays = ShopUIStyle.MakeImage(face, "Rays", WithAlpha(LegendGold, 0.32f));
            rays.sprite = Rays;
            ShopUIStyle.Anchor(rays.rectTransform, artAnchor, artAnchor, new Vector2(0.5f, 0.5f),
                               new Vector2(0f, -artY), Vector2.one * _cardW * RaysFrac);
            view.Rays = rays.rectTransform;
        }

        var glow = ShopUIStyle.MakeImage(face, "Glow",
            WithAlpha(rarityCol, data.rarity == ItemRarity.Common ? 0.18f : 0.45f));
        glow.sprite = SoftDot;
        ShopUIStyle.Anchor(glow.rectTransform, artAnchor, artAnchor, new Vector2(0.5f, 0.5f),
                           new Vector2(0f, -artY), Vector2.one * _cardW * GlowFrac * iconK);
        view.Glow = glow;

        var art = RuneArt.ResolveRuneIcon(data);
        if (art != null)
        {
            var icon = ShopUIStyle.MakeImage(face, "RuneIcon", Color.white);
            icon.sprite = art;
            icon.preserveAspect = true;
            ShopUIStyle.Anchor(icon.rectTransform, artAnchor, artAnchor, new Vector2(0.5f, 0.5f),
                               new Vector2(0f, -artY), Vector2.one * _cardW * (legend ? LegendIconFrac : IconFrac) * iconK);
            view.Icon     = icon.rectTransform;
            view.IconBase = icon.rectTransform.anchoredPosition;
        }

        // 이름
        var nameText = ShopUIStyle.MakeText(face, "Name", 22f, FontStyles.Bold,
            TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(nameText.rectTransform, artAnchor, artAnchor, new Vector2(0.5f, 1f),
            new Vector2(0f, -(NameY - lift)), new Vector2(_cardW - 28f, 28f));
        nameText.text = data.displayName ?? data.itemId;
        FitSingleLine(nameText);

        BuildChips(face, data, rarityCol, lift);

        // 효과 목록 — 바닥은 모양 칸 윗변. 칩이 두 줄이면 그 아래에서 시작한다.
        float fxTop = family ? ChipY - lift + ChipH * 2f + 10f : EffectsY;
        float fxH   = Mathf.Min(EffectsH, FootTop - fxTop);
        var fxRoot = ShopUIStyle.MakeRect(face, "Effects").GetComponent<RectTransform>();
        ShopUIStyle.Anchor(fxRoot, artAnchor, artAnchor, new Vector2(0.5f, 1f),
            new Vector2(0f, -fxTop), new Vector2(_cardW - 32f, fxH));
        var vlg = fxRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childControlHeight = true;  vlg.childForceExpandHeight = false;   // 줄바꿈된 효과 행이 자기 높이로 서게(2026-09-09)
        vlg.spacing = 2f;
        BuildEffectRows(fxRoot, data);

        // 작은 모양 + 배치 배지 — 모양은 "놓을 수 있는가"의 근거라 배지 옆에 붙인다.
        var shapeRoot = ShopUIStyle.MakeRect(face, "MiniShape").GetComponent<RectTransform>();
        // 폭은 오른쪽 아래 배지 자리를 뺀 만큼까지 — 좁은 카드(4지선다)에서 5칸 모양이 배지와 겹쳤다.
        float badgeW = Mathf.Min(128f, _cardW * 0.46f);
        float footW  = Mathf.Min(_cardW * FootW, _cardW - 32f - badgeW - 8f);
        ShopUIStyle.Anchor(shapeRoot, Vector2.zero, Vector2.zero, Vector2.zero,
                           new Vector2(16f, 12f), new Vector2(footW, FootH));
        bool canPlace = BuildMiniShape(shapeRoot, data);

        var badgeCol = canPlace ? OkColor : NoColor;
        var badge = ShopUIStyle.MakeFrame(face, "PlaceBadge", WithAlpha(badgeCol, 0.55f),
                                          new Color(badgeCol.r * 0.12f, badgeCol.g * 0.14f, badgeCol.b * 0.12f, 0.85f), 1f);
        ShopUIStyle.Anchor((RectTransform)badge.transform.parent, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                           new Vector2(-16f, 16f), new Vector2(badgeW, 24f));
        var badgeText = ShopUIStyle.MakeText(badge.transform, "Label", 16f, FontStyles.Bold,   // 가독성 하한 16
            TextAlignmentOptions.Center, badgeCol);
        ShopUIStyle.Stretch(badgeText.rectTransform);
        badgeText.text = canPlace ? "놓을 자리 있음" : "놓을 자리 없음";
        FitSingleLine(badgeText);

        // 전설 광택 — 판·보관함과 같은 금빛 줄기가 카드를 가로지른다.
        if (legend)
            face.gameObject.AddComponent<StagingSlotShimmer>()
                .Configure(StagingSlotShimmer.LegendaryTint, 0.22f, 3.6f, _cardW, phase: 0f);
    }

    /// <summary>등급·칸수 칩과 속성 칩(블록 타일 + 이름)을 한 줄 가운데에 세운다. 속성 없는 룬은 등급 칩만.</summary>
    /// <param name="lift">칩 줄을 올리는 만큼(계열 칩이 있어 두 줄일 때).</param>
    private void BuildChips(RectTransform face, RuntimeItemData data, Color rarityCol, float lift)
    {
        int cells = CellCount(data.shapeId);
        string rarityText = RewardPresentation.RarityLabel(data.rarity) + (cells > 0 ? $" · {cells}칸" : string.Empty);

        var elem = ElementDef.GetById(data.element);
        const float pad = 9f, gap = 6f, tile = 14f;

        var r = MakeChip(face, "RarityChip", rarityText, rarityCol, WithAlpha(rarityCol, 0.45f), out float rw);
        float total = rw;
        RectTransform e = null; float ew = 0f;
        if (elem != null)
        {
            var elemCol = ElementDef.IdColor(data.element, ShopUIStyle.TextDim);
            e = MakeChip(face, "ElementChip", elem.Name, elemCol, WithAlpha(elemCol, 0.35f), out ew, tile + 4f);
            var tileArt = RuneArt.GetBlockTile(data.element);
            var dot = ShopUIStyle.MakeImage(e, "Tile", tileArt != null ? Color.white : elemCol);
            if (tileArt != null) { dot.sprite = tileArt; dot.preserveAspect = true; }
            ShopUIStyle.Anchor(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                               new Vector2(pad, 0f), Vector2.one * tile);
            total += gap + ew;
        }

        float x = -total * 0.5f;
        r.anchoredPosition = new Vector2(x + rw * 0.5f, -(ChipY - lift));
        if (e != null) e.anchoredPosition = new Vector2(x + rw + gap + ew * 0.5f, -(ChipY - lift));

        // 발동 계열 기여 — 「기동 +1 → 3/4」. 고르는 순간 빌드에 무엇이 되는지(09-29 빌드 컨셉). 칩 줄 아래 한 줄.
        var fam = BuildFamilyRules.OfItem(data);
        if (fam != BuildFamily.None)
        {
            int now = BuildImprint.Count(fam) + 1;
            int next = BuildFamilyRules.NextThreshold(now);
            bool stageUp = BuildFamilyRules.StageOf(now) > BuildImprint.Stage(fam);
            string tail = stageUp ? $"  <color=#E8BA54>{BuildFamilyRules.StageOf(now)}단계!</color>"
                        : next > 0 ? $"<color=#8A8594>/{next}</color>" : "";
            var famCol = BuildFamilyRules.ColorOf(fam);
            var fc = MakeChip(face, "FamilyChip", $"{BuildFamilyRules.Label(fam)} +1 → {now}{tail}", famCol, WithAlpha(famCol, 0.40f), out _);
            fc.anchoredPosition = new Vector2(0f, -(ChipY - lift + ChipH + 4f));
        }
    }

    private static RectTransform MakeChip(RectTransform parent, string name, string text, Color textCol, Color lineCol,
                                          out float width, float leading = 0f)
    {
        const float pad = 9f, size = 16f;   // 가독성 하한 16 — 칩 높이 24에 한 줄로 들어간다
        var fill = ShopUIStyle.MakeFrame(parent, name, lineCol, ChipFill, 1f);
        var rt = (RectTransform)fill.transform.parent;

        var label = ShopUIStyle.MakeText(fill.transform, "Label", size, FontStyles.Normal, TextAlignmentOptions.Left, textCol);
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

        style.fontSize        = 16f;   // 가독성 하한
        style.iconSize        = 20f;   // 룬의 얼굴은 위의 큰 문양 — 행 아이콘은 기능 표식만
        style.rowHeight       = 22f;
        style.usePrefixArrows = true;

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

            var more = ShopUIStyle.MakeText(parent, "MoreEffects", 13f, FontStyles.Italic,
                TextAlignmentOptions.Center, ShopUIStyle.TextDim);
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

    /// <summary>작은 모양 셀을 그리고(판 위 블록과 같은 속성 타일), 지금 판에 놓을 자리가 있는지 반환한다.</summary>
    private bool BuildMiniShape(RectTransform root, RuntimeItemData data)
    {
        var entry = Managers.RuneData?.GetShape(data.shapeId);
        if (entry == null) return true;   // 판정 불가 → 막지 않는다

        var offsets = RuneDataManager.ParseCellOffsets(entry);
        if (offsets == null || offsets.Length == 0) return true;

        // 속성까지 넘긴다 — 룬은 자기 속성 존(레전드리는 중앙 제외)에만 놓이므로,
        // 모양만 보고 판정하면 "자리 있음"으로 뜬 룬이 막상 판에서는 들어갈 곳이 없다.
        bool canPlace = MerlinRuneBridge.Instance == null
            || MerlinRuneBridge.Instance.CanPlaceShape(offsets, data.element, RuneZoneRule.NoCenter(data));

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var o in offsets)
        {
            if (o.x < minX) minX = o.x;  if (o.x > maxX) maxX = o.x;
            if (o.y < minY) minY = o.y;  if (o.y > maxY) maxY = o.y;
        }
        int cols = maxX - minX + 1, rows = maxY - minY + 1;

        float boxW = root.sizeDelta.x, boxH = root.sizeDelta.y;
        float fitW = (boxW - (cols - 1) * MiniGap) / Mathf.Max(1, cols);
        float fitH = (boxH - (rows - 1) * MiniGap) / Mathf.Max(1, rows);
        float cellSize = Mathf.Min(MiniCellMax, fitW, fitH);

        // 스프라이트/틴트 규칙은 RuneArt.ResolveRuneCell 한곳에서 정한다(네 경로 동일 규칙).
        // 판 위 블록과 <b>같은 속성 타일</b>이 있으면 그걸 쓴다 — 미리보기는 "판에서 어떻게 보일지"다.
        RuneArt.ResolveRuneCell(data.element, data.rarity, new Color(0.7f, 0.7f, 0.75f),
            out Sprite art, out Color tint);
        if (art == null) art = UISkin.RuneSelect?.runeTile;
        var blockTile = RuneArt.GetBlockTile(data.element);
        if (blockTile != null) { art = blockTile; tint = Color.white; }
        Color dimTint = new Color(tint.r * 0.5f, tint.g * 0.5f, tint.b * 0.5f, 0.7f);

        // 왼쪽 아래 기준으로 쌓는다 — 배지와 밑줄을 맞춘다.
        foreach (var o in offsets)
        {
            int col = o.x - minX;
            int row = maxY - o.y;

            var cell = ShopUIStyle.MakeImage(root, $"C{o.x}_{o.y}", canPlace ? tint : dimTint);
            if (art != null) { cell.sprite = art; cell.preserveAspect = true; }
            ShopUIStyle.Anchor(cell.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(col * (cellSize + MiniGap), (rows - 1 - row) * (cellSize + MiniGap)),
                Vector2.one * cellSize);
        }

        return canPlace;
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
        Managers.Sound.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();
        _selected = index;
        ApplySelectionVisual(index);
    }

    /// <summary>
    /// 선택 강조(테두리색·배율·명암)만 다시 입힌다 — 소리도, 상태 변경도 하지 않는다.
    /// 공개 연출의 마무리(<see cref="SnapToRest"/>)가 카드 배율·투명도를 되돌리므로,
    /// 연출 도중에 고른 카드가 있으면 연출이 끝난 뒤 이걸 한 번 더 불러야 강조가 살아남는다.
    /// </summary>
    private void ApplySelectionVisual(int index)
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            bool on = (i == index);
            // 스킨 시: 아트를 물들이지 않도록 선택=흰색·비선택=살짝 어둡게(밝기)로 피드백.
            if (_cards[i].Border != null)
                _cards[i].Border.color = _skinned ? (on ? Color.white : new Color(0.62f, 0.62f, 0.66f))
                                                  : (on ? SelectBorder : ShopUIStyle.CardBorder);
            if (_cards[i].Fill != null)
                _cards[i].Fill.color = _skinned ? Color.white
                                                : (on ? CardSelected : ShopUIStyle.CardFill);

            // 등급 테두리가 모든 카드에 깔려 있어, 같은 등급 후보가 늘어서면 네 장이 똑같이 빛나 보였다.
            // 등급색과 충돌하지 않는 축인 <b>크기와 명암</b>으로 표시한다.
            if (_cards[i].Rt != null)
                _cards[i].Rt.localScale = Vector3.one * (on ? SelectedCardScale : 1f);
            if (_cards[i].Group != null)
                _cards[i].Group.alpha = (index < 0 || on) ? 1f : UnselectedCardAlpha;
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

        Managers.Sound.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();
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
        Managers.Sound.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();
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
