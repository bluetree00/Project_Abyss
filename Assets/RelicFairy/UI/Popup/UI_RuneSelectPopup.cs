using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 룬 선택 팝업 (Canvas_Popup, Addressable "UI_RuneSelectPopup").
///
/// 룬 획득 = <b>후보 N개 중 1개 선택</b>. 기존 <see cref="UI_ItemAcquisitionPopup"/>은 단일 알림이라
/// "고르는" 결정이 없었다 — 이 팝업이 매 방 보상을 결정으로 바꾼다.
///
/// 카드 구성(설계서 §4):
///  - <b>모양 미리보기</b> — 선택의 핵심. 효과가 좋아도 판에 안 들어가면 무의미하다.
///  - 이름 / 등급·칸수 / 효과
///  - <b>배치 가능 배지</b> — 인접 제약 때문에 실제로 못 놓는 룬이 생긴다. 경고일 뿐 선택은 막지 않는다.
///
/// 속성은 <b>상단 리본색 + 이름/태그</b>로 표시한다 — 어느 존에 놓을지 판단하는 근거이자
/// 디자이너 완성본(0_룬 획득_260723)의 확정 표현. 효과=조각 / 시너지=위치 2층 구조는 그대로다.
/// </summary>
public sealed class UI_RuneSelectPopup : UI_Popup
{
    public override bool BlocksGameplay => true;
    public override bool CloseOnEscape  => false;   // 보상 결정이라 실수로 닫히면 안 된다 — 선택/넘기기로만 종료

    // ── 레이아웃 ──
    // 배경 아트는 9슬라이스가 아니라 통짜로 늘어난다(Skin(window, background) — sliced 아님).
    // 따라서 크기는 원본 비율 2081:1241(=1.6774)을 지켜야 왜곡되지 않는다.
    // 1040×620은 1920 화면의 54%뿐이라 4지선다에서 카드가 222px로 눌렸고, 효과 문구
    // ("스킬 사용 후 4초간 최대 HP +10%" ≈ 200px)가 가용 166px를 넘어 잘렸다.
    // 1400×835로 키우면 비율을 유지한 채 카드가 설계 상한 300px에 도달한다.
    // 의뢰서 「정제소_룬획득」 06: 창 1100×620 · 카드 300×380 × 3 · 선택 300×56 / 넘기기 180×56.
    // 카드는 효과 4행(16px 줄바꿈 허용)을 담느라 420으로 40 늘렸다 — 창 620 안에 제목 60 + 카드 420 + 버튼 56이 들어간다.
    private const float WindowW = 1100f;
    private const float WindowH = 620f;
    private const float CardW   = 300f;   // 카드 폭 상한 — 후보가 많으면 이 아래로 줄어든다
    private const float CardH   = 420f;   // 의뢰서 380 + 효과 4행분 40
    private const float CardGap = 24f;
    private const float CardSideMargin = 40f;   // 카드 열 좌우 여백(창 안쪽)

    // 등급 확률 막대 — 창 좌하단, [선택]/[넘기기] 좌측 여백에 앉힌다.
    // 여백 8 기준으로 막대 상단(-222)이 카드 하단(CardY-CardH/2 = -210)보다 낮아 겹치지 않고,
    // 오른쪽 끝(-272)도 [선택] 버튼 왼쪽 끝(-190)에 닿지 않는다.
    private const float OddsBarW      = 236f;
    private const float OddsBarH      = 78f;
    private const float OddsBarMargin = 8f;
    // 하단 [선택]/[넘기기]와 겹치지 않게 카드를 살짝 올린다.
    // -26이면 막대와의 여유가 6px뿐이라 720p(0.667배)에서 4px로 뭉개져 붙어 보였다 → -20으로 12px 확보.
    // 카드 배치 가능 밴드 = 타이틀바 아래(+339.5) ~ 확률바/버튼 위(-319.5).
    // CardH 560을 0에 두면 위 60 / 아래 40으로 양쪽 여유가 고르게 남는다.
    private const float CardY   = 32f;    // 카드 위 68 / 아래 132(버튼 줄 56 + 여백) — 창 중심 기준

    // 모양 미리보기 셀은 고정 크기가 아니라 <b>박스에 맞춰 확대</b>한다.
    // 고정 22px이던 시절엔 1칸 룬이 점처럼 보여 무슨 모양인지 분간이 안 됐다.
    private const float ShapeBoxH   = 200f;  // 룬 실물(모양 타일) 박스 — 의뢰서 269×203
    // 모양 박스는 좌우 2단이다 — 왼쪽에 룬 고유 아트, 오른쪽에 블록 모양.
    // "이 룬이 무엇인가"와 "판에서 어떤 모양을 먹는가"는 다른 정보라 자리를 나눠 준다.
    // 룬 아트는 "무엇인가"를 알려주고 모양은 "놓을 수 있는가"를 알려준다.
    // 판정에 실제로 쓰이는 건 모양이라, 아트 칸을 줄여 모양 쪽에 자리를 넘긴다.
    private const float MiniGap     = 4f;
    private const float MiniCellMax = 58f;   // 의뢰서 타일 58 — 1~2칸 룬도 이 위로 키우지 않는다
    private const float MiniCellMin = 18f;   // 9칸(3×3)도 박스를 안 넘도록 하한

    // ── 공개 연출 ──
    /// <summary>니어미스 멈칫 길이(초). Legendary 경로에서만 1회.</summary>
    private const float NearMissHold  = 0.08f;
    /// <summary>전체화면 플래시 페이드 길이(초).</summary>
    private const float ScreenFlashDur = 0.25f;

    private static readonly Color CardSelected  = new(0.20f, 0.17f, 0.10f, 1f);
    private static readonly Color SelectBorder  = new(0.88f, 0.72f, 0.32f, 1f);
    private static readonly Color OkColor       = new(0.37f, 0.81f, 0.52f, 1f);
    private static readonly Color NoColor       = new(0.88f, 0.33f, 0.25f, 1f);

    // ── 상태 ──
    private readonly List<CardView> _cards = new();
    private readonly List<GameObject> _lockedRoots = new();   // 잠긴 자리(해금하면 채워질 칸)
    private int _lockedSlots;
    private List<(RuntimeItemData data, ItemSO so)> _candidates;
    private RunItemInventory _inventory;
    private const float SelectedCardScale   = 1.05f;   // 선택 카드 확대
    private const float UnselectedCardAlpha = 0.55f;   // 비선택 카드 감광

    private int _selected = -1;
    private float _cardW = CardW;   // 후보 수에 맞춰 산출된 실제 카드 폭
    private bool _built;
    private bool _skinned;   // 아트 로드 성공 — 선택 피드백을 색 틴트 대신 밝기로 처리

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
        public Image       Border;
        public Image       Fill;
        public GameObject  Root;
        public RectTransform Rt;
        public CanvasGroup Group;
        public Vector2     BasePos;
        public Image[]     RarityFrame;   // 상하좌우 4조각 — 등급 색/두께. 선택 피드백과 충돌하지 않게 분리
        public TMP_Text    MetaText;      // 굴림 리빌 대상(등급 라벨 줄)
        public string      MetaSuffix;    // 등급 라벨 뒤에 붙는 고정부(칸수·속성)
        public ItemRarity  Rarity;
    }

    // ── Lifecycle ──

    public override void Init()
    {
        base.Init();
        BuildChrome();
    }

    private void Update()
    {
        // 아무 입력 = 스냅. 시퀀스가 끝나야 고를 수 있는 게 아니라, 언제든 끊고 바로 고를 수 있어야 한다.
        // (카드 클릭·[선택]·[넘기기]는 각 핸들러가 SkipReveal을 부른다)
        if (!_revealing) return;
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.F) || Input.GetMouseButtonDown(0))
            SkipReveal();
    }

    // ── Public API ──

    /// <summary>버튼 클릭 즉시 resolve — 애니메이션을 기다리지 않는다.</summary>
    public UniTask WaitForInteractionAsync(System.Threading.CancellationToken ct)
    {
        _interactionTcs = new UniTaskCompletionSource();
        return _interactionTcs.Task.AttachExternalCancellation(ct);
    }

    /// <summary>후보를 주입해 카드를 구성한다. ShowPopupUIAndGetAsync 직후 호출.</summary>
    /// <param name="lockedSlots">
    /// 해금하면 <b>실제로 더 열릴 수 있는</b> 칸 수. 빈 자리로 미리 그려 무엇이 늘어나는지 보여준다.
    /// 이 방의 규칙이 애초에 확장 대상이 아니면 0이다 — 없는 확장을 약속하면 안 된다.
    /// </param>
    public void Setup(List<(RuntimeItemData data, ItemSO so)> candidates, RunItemInventory inventory,
                      int lockedSlots = 0)
    {
        _candidates  = candidates;
        _inventory   = inventory;
        _lockedSlots = Mathf.Max(0, lockedSlots);

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

        BuildCards();
        RefreshCounter();
        SetSelected(-1);

        // 선택은 이 시점부터 이미 가능하다 — 시퀀스가 끝나야 고를 수 있는 게 아니다(§C-1 스킵 규칙).
        PlayRevealSequenceAsync().Forget();
    }

    /// <summary>
    /// 카드 순차 공개. 팝업 안이라 <c>timeScale == 0</c>이므로 unscaled UI 트윈과
    /// <see cref="VolumePulseService"/>만 쓴다(§A-5).
    ///
    /// 카드 간격은 <b>스태거</b>다 — 앞 카드가 다 열리기를 기다리지 않고 간격만큼만 두고 다음 카드를
    /// 띄운다. 순차로 기다리면 Common 3장에도 0.5초가 넘게 걸려 §D 검증기준(Common 3장 ≤ 0.35s /
    /// Legendary 포함 ≤ 1.1s)을 넘긴다. 카드는 오름차순이라 <b>마지막(최고 등급)</b>이 항상 가장 길고,
    /// 그 카드의 완료만 기다리면 시퀀스 종료 시점이 정확해진다.
    /// </summary>
    private async UniTaskVoid PlayRevealSequenceAsync()
    {
        var ct = destroyCancellationToken;
        _revealing     = true;
        _revealSkipped = false;

        try
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                var spec = RewardPresentation.For(card.Rarity);
                bool isLast = i == _cards.Count - 1;

                if (_revealSkipped || spec.CardPopDuration <= 0f)
                {
                    RevealCardInstant(card);
                    continue;
                }

                // 마지막 카드만 완료를 기다린다. 앞 카드들은 스태거 간격을 두고 겹쳐 진행한다.
                if (isLast) await PresentCardAsync(card, spec, ct);
                else        PresentCardAsync(card, spec, ct).Forget();

                if (!isLast && !_revealSkipped && spec.CardStagger > 0f)
                    await UIJuice.HoldAsync(spec.CardStagger, ct);
            }
        }
        catch (OperationCanceledException) { return; }
        finally
        {
            // 취소·스킵 어느 경로로 빠져도 카드가 반투명·축소 상태로 남지 않게 확정한다.
            for (int i = 0; i < _cards.Count; i++) RevealCardInstant(_cards[i]);
            _revealing = false;

            // 위 한 줄이 배율·투명도를 되돌리므로, 연출 도중에 고른 카드가 있으면
            // 선택 강조가 함께 지워진다(_selected만 남고 화면에는 표시가 사라진다).
            if (_selected >= 0) ApplySelectionVisual(_selected);
        }
    }

    /// <summary>카드 1장의 공개 — 팝인 → 프레임 플래시 → 굴림 리빌 → (상위 등급) 화면 방점.</summary>
    private async UniTask PresentCardAsync(CardView card, RewardPresentation.TierSpec spec,
                                           System.Threading.CancellationToken ct)
    {
        Managers.Sound?.PlayUiAsync(SoundKey.Sfx.UiButton, 0.55f, spec.SfxPitch).Forget();

        await UIJuice.PopInAsync(card.Rt, card.Group, spec.CardPopDuration,
                                 fromScale: 0.9f, fromYOffset: -18f, rotZ: spec.CardPopRotation, ct);

        if (spec.FlashPulses > 0 && card.Fill != null)
            UIJuice.FlashAsync(card.Fill, RewardPresentation.FrameColor(card.Rarity),
                               0.18f, spec.FlashPulses, ct).Forget();

        // 등급 라벨 굴림 리빌 — 확률·결과는 이미 확정. 표시만 계단식으로 오른다(§C-3-b).
        await RevealRarityLabelAsync(card, spec, ct);

        if (spec.PulsePeak > 0f)
            VolumePulseService.Pulse(spec.PulsePeak, spec.PulseDuration);

        if (spec.ScreenFlashAlpha > 0f)
            PlayScreenFlashAsync(spec.ScreenFlashAlpha, ct).Forget();
    }

    /// <summary>카드를 최종 상태로 즉시 확정(스킵·연출 끔·취소 공통).</summary>
    private void RevealCardInstant(CardView card)
    {
        if (card == null) return;
        UIJuice.SnapPopIn(card.Rt, card.Group, card.BasePos);
        SetMetaLabel(card, card.Rarity);
        ApplyRarityFrame(card, card.Rarity);
    }

    /// <summary>
    /// 등급 라벨을 Common부터 실제 등급까지 계단식으로 올린다. 프레임 색도 라벨과 동기해 함께 오른다.
    /// Legendary만 도달 직전 1회 "멈칫"(니어미스) — 확률·결과 불변, 축약/끔에서는 자동 생략.
    /// </summary>
    private async UniTask RevealRarityLabelAsync(CardView card, RewardPresentation.TierSpec spec,
                                                 System.Threading.CancellationToken ct)
    {
        int steps = (int)card.Rarity;   // Common=0 → 오를 계단 수
        if (spec.RevealDuration <= 0f || steps <= 0 || _revealSkipped)
        {
            SetMetaLabel(card, card.Rarity);
            ApplyRarityFrame(card, card.Rarity);
            return;
        }

        float perStep = spec.RevealDuration / steps;

        for (int r = 0; r <= steps; r++)
        {
            var shown = (ItemRarity)r;
            SetMetaLabel(card, shown);
            ApplyRarityFrame(card, shown);

            if (r == steps) break;

            // 각 단 상승마다 피치가 오른다 — "승급의 첫 신호는 사운드"(§C-3-b).
            Managers.Sound?.PlayUiAsync(SoundKey.Sfx.UiButton, 0.4f,
                                            RewardPresentation.For(shown).SfxPitch).Forget();

            // 니어미스 — ★ 직전 ◆에서 한 번만 멈칫한다.
            bool nearMiss = card.Rarity == ItemRarity.Legendary && r == steps - 1;
            await UIJuice.HoldAsync(nearMiss ? perStep + NearMissHold : perStep, ct);

            if (_revealSkipped)
            {
                SetMetaLabel(card, card.Rarity);
                ApplyRarityFrame(card, card.Rarity);
                return;
            }
        }
    }

    /// <summary>Legendary 전체화면 금색 플래시. 코드 생성 Image 1장 + 알파 트윈(신규 아트 0).</summary>
    private async UniTaskVoid PlayScreenFlashAsync(float alpha, System.Threading.CancellationToken ct)
    {
        if (_screenFlash == null) return;

        var c = RewardPresentation.FrameColor(ItemRarity.Legendary);
        c.a = alpha;
        _screenFlash.color = c;

        try { await UIJuice.FadeOutAsync(_screenFlash, alpha, ScreenFlashDur, ct); }
        catch (OperationCanceledException) { }
    }

    /// <summary>진행 중인 공개 연출을 최종 상태로 스냅한다. 어떤 입력이든 들어오면 호출.</summary>
    private void SkipReveal()
    {
        if (!_revealing) return;
        _revealSkipped = true;
    }

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

        BuildOddsBar();
    }

    /// <summary>
    /// 이 방의 등급 확률 막대. "확률로 뜬다"는 사실이 화면에 처음 존재하게 만든다 —
    /// 그래야 굴림 리빌(§C-3-b)이 "연출"이 아니라 "정보"로 읽힌다.
    ///
    /// 값의 출처는 행운이 아니라 <see cref="RoomRewardTable"/>(방 종류)다. 정예방에 들어가면
    /// 막대가 눈에 띄게 위로 쏠려, 위험을 감수한 대가가 숫자로 보인다.
    /// <see cref="OddsBarView"/>는 이미 구현돼 있었으나 호출처가 0개였다 — 그대로 재사용한다.
    /// </summary>
    private void BuildOddsBar()
    {
        var kind = GameRunBootstrapper.Instance?.Run?.CurrentRoomKind ?? RoomPlanKind.Normal;
        var (rare, epic, legendary) = RoomRewardTable.For(kind).Weights.Normalized();

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
        bar.SetOdds(rare, epic, legendary, heated: false);
    }

    private void BuildCards()
    {
        foreach (var c in _cards) if (c.Root != null) Destroy(c.Root);
        _cards.Clear();
        foreach (var g in _lockedRoots) if (g != null) Destroy(g);
        _lockedRoots.Clear();

        // 창은 화면에 맞춰 커지지만 아래 배치는 전부 목업 px다 — 배율 레이어가 그 차이를 흡수한다.
        // 이게 없으면 넓어진 판에 원래 크기 카드가 떠 있게 된다.
        var layer = UIProportional.EnsureScaledLayer(_windowRoot, "CardLayer", WindowW, WindowH) ?? (RectTransform)_windowRoot;

        int n = _candidates.Count;
        // 잠긴 자리도 줄의 일부다 — 폭 산출과 중앙 정렬에 함께 넣어야 줄이 흐트러지지 않고,
        // 해금 뒤에 카드가 "그 자리로" 들어오는 것으로 보인다.
        int slots = n + _lockedSlots;

        // 카드 폭은 칸 수에 맞춰 줄인다. 300 고정이던 시절엔 3장까지만 창에 들어갔고,
        // 정예방 4지선다(RoomRewardTable)에서 카드가 창 밖으로 밀려났다.
        float avail = WindowW - CardSideMargin * 2f - (slots - 1) * CardGap;
        _cardW = Mathf.Min(CardW, avail / Mathf.Max(1, slots));

        float totalW = slots * _cardW + (slots - 1) * CardGap;
        float startX = -totalW * 0.5f + _cardW * 0.5f;

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

            // 등급 프레임 — 스킨 아트/선택 피드백과 색이 충돌하지 않도록 별도 4조각으로 얹는다.
            // "프레임=희귀도 / 리본·엠블럼=속성" 규칙(통합설계서 §5 P0-8)의 첫 적용처.
            view.RarityFrame = BuildRarityFrame(cardRT, data.rarity);
            _cards.Add(view);

            BuildCardContent(card.transform, data, so, view);

            // 연출이 켜져 있으면 숨은 상태에서 시작한다(공개는 PlayRevealSequenceAsync가 담당).
            var spec = RewardPresentation.For(data.rarity);
            if (spec.CardPopDuration > 0f)
            {
                view.Group.alpha  = 0f;
                view.Rt.localScale = Vector3.one * 0.9f;
            }
        }

        for (int i = 0; i < _lockedSlots; i++)
        {
            int slot = n + i;
            _lockedRoots.Add(UILockedSlot.Build(layer, $"Locked{i}",
                new Vector2(startX + slot * (_cardW + CardGap), CardY),
                new Vector2(_cardW, CardH)));
        }
    }

    /// <summary>등급 색 프레임 4조각(상·하·좌·우). 두께는 등급이 올라갈수록 두꺼워진다.</summary>
    private static Image[] BuildRarityFrame(RectTransform cardRT, ItemRarity rarity)
    {
        float th = ShopUIStyle.RarityBorder(rarity);
        var col  = RewardPresentation.FrameColor(rarity);
        var bars = new Image[4];

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
            bars[i] = bar;
        }
        return bars;
    }

    /// <summary>프레임 색·두께를 표시 등급에 맞춘다(굴림 리빌 중 계단마다 호출).</summary>
    private static void ApplyRarityFrame(CardView card, ItemRarity shown)
    {
        if (card?.RarityFrame == null) return;

        var col = RewardPresentation.FrameColor(shown);
        float th = ShopUIStyle.RarityBorder(shown);

        for (int i = 0; i < card.RarityFrame.Length; i++)
        {
            var bar = card.RarityFrame[i];
            if (bar == null) continue;
            bar.color = col;
            // 0·1 = 가로 막대(높이가 두께), 2·3 = 세로 막대(폭이 두께)
            var sd = bar.rectTransform.sizeDelta;
            bar.rectTransform.sizeDelta = i < 2 ? new Vector2(sd.x, th) : new Vector2(th, sd.y);
        }
    }

    /// <summary>등급 라벨 줄을 표시 등급으로 다시 쓴다(굴림 리빌).</summary>
    private static void SetMetaLabel(CardView card, ItemRarity shown)
    {
        if (card?.MetaText == null) return;
        card.MetaText.color = ShopUIStyle.Rarity(shown);   // 리빌 뒤에도 정색(글로우 알파는 글자용이 아니다)
        card.MetaText.text  = RewardPresentation.RarityLabel(shown) + card.MetaSuffix;
    }

    private void BuildCardContent(Transform card, RuntimeItemData data, ItemSO so, CardView view)
    {
        // 상단 속성 리본 — 완성본의 색 막대. 어느 존에 놓을지 알려주는 근거색이다.
        int elemIdx = ElementIndex(data.element);
        Color ribbonCol = ElementDef.IdColor(data.element, ShopUIStyle.RarityGlow(data.rarity));
        var ribbon = ShopUIStyle.MakeImage(card, "Ribbon", ribbonCol);
        ShopUIStyle.Anchor(ribbon.rectTransform,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -10f), new Vector2(-32f, 14f));
        ShopUIStyle.Skin(ribbon, UISkin.RuneSelect?.ElementRibbon(elemIdx), sliced: true);

        // 속성 엠블럼(룬조각) — 카드 좌상단 배지. 어느 속성 룬인지 한눈에.
        var piece = UISkin.RuneSelect?.ElementPiece(elemIdx);
        if (piece != null)
        {
            var emblem = ShopUIStyle.MakeImage(card, "Emblem", Color.white);
            ShopUIStyle.Skin(emblem, piece);
            emblem.preserveAspect = true;
            ShopUIStyle.Anchor(emblem.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(12f, -28f), new Vector2(34f, 34f));
        }

        // 모양 미리보기
        // 룬 실물 = 모양 타일(의뢰서 N5·N6). 별도 각인석은 두지 않는다 — 같은 룬이 판(룬 그리드)에서 보이는
        // 얼굴(속성 타일)과 여기서 보이는 얼굴이 같아야 한다. 기능 문양은 아래 효과 행의 아이콘이 맡는다.
        var shapeBox = ShopUIStyle.MakeImage(card, "ShapeBox", ShopUIStyle.IconBg);
        ShopUIStyle.Anchor(shapeBox.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -24f), new Vector2(_cardW - 40f, ShapeBoxH));

        bool canPlace = BuildShapePreview(shapeBox.transform, data);

        // 이름
        var name = ShopUIStyle.MakeText(card, "Name", 20f, FontStyles.Bold,
            TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        ShopUIStyle.Anchor(name.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -228f), new Vector2(_cardW - 24f, 26f));
        name.text = data.displayName ?? data.itemId;
        FitSingleLine(name);

        // 등급 · 칸수 · 속성 — 속성명은 속성색으로 표시(어느 존에 놓을지 판단 근거)
        int cells = CellCount(data.shapeId);
        var elem  = ElementDef.GetById(data.element);
        string elemTag = elem != null ? $"  ·  <color={ElementDef.IdHex(data.element)}>{elem.Icon}{elem.Name}</color>" : "";
        var meta = ShopUIStyle.MakeText(card, "Meta", 16f, FontStyles.Normal,
            TextAlignmentOptions.Center, ShopUIStyle.Rarity(data.rarity));   // 글로우(알파 0.26)는 글자에 못 쓴다
        meta.richText = true;
        ShopUIStyle.Anchor(meta.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -254f), new Vector2(_cardW - 24f, 22f));
        // 등급 라벨은 굴림 리빌이 다시 쓴다 — 뒤에 붙는 고정부(칸수·속성)만 따로 보관한다.
        string metaSuffix = (cells > 0 ? $" · {cells}칸" : string.Empty) + elemTag;
        if (view != null)
        {
            view.MetaText   = meta;
            view.MetaSuffix = metaSuffix;
        }
        meta.text = RewardPresentation.RarityLabel(data.rarity) + metaSuffix;
        FitSingleLine(meta);

        // 효과 칸 배경 — 아트 있으면 박스로, 없으면 표시 안 함(투명 폴백)
        var fxSkin = UISkin.RuneSelect?.effectBox;
        if (fxSkin != null)
        {
            var fxBg = ShopUIStyle.MakeImage(card, "EffectBox", Color.white);
            ShopUIStyle.Skin(fxBg, fxSkin, sliced: true);
            ShopUIStyle.Anchor(fxBg.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -280f), new Vector2(_cardW - 28f, 104f));   // 의뢰서 N8 효과 칸(85) + 2줄 여유
        }

        // 효과 목록
        var fxRoot = ShopUIStyle.MakeRect(card, "Effects").GetComponent<RectTransform>();
        ShopUIStyle.Anchor(fxRoot,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -284f), new Vector2(_cardW - 32f, 96f));
        var vlg = fxRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childControlHeight = true;  vlg.childForceExpandHeight = false;   // 줄바꿈된 효과 행이 자기 높이로 서게(2026-09-09)
        vlg.spacing = 2f;
        BuildEffectRows(fxRoot, data);

        // 배치 상태 바 — 완성본의 하단 초록/빨강 막대. 아트 있으면 바로, 없으면 텍스트만.
        var placeSkin = canPlace ? UISkin.RuneSelect?.placeOk : UISkin.RuneSelect?.placeNo;
        Transform badgeParent = card;
        if (placeSkin != null)
        {
            var bar = ShopUIStyle.MakeImage(card, "PlaceBar", Color.white);
            ShopUIStyle.Skin(bar, placeSkin, sliced: true);
            ShopUIStyle.Anchor(bar.rectTransform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 6f), new Vector2(_cardW - 28f, 28f));   // 의뢰서 N9 276×26
            badgeParent = bar.transform;
        }

        var badge = ShopUIStyle.MakeText(badgeParent, "PlaceBadge", 16f, FontStyles.Bold,
            TextAlignmentOptions.Center, placeSkin != null ? Color.white : (canPlace ? OkColor : NoColor));
        if (placeSkin != null)
            ShopUIStyle.Stretch(badge.rectTransform);
        else
            ShopUIStyle.Anchor(badge.rectTransform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 8f), new Vector2(_cardW - 24f, 24f));
        badge.text = canPlace ? "놓을 자리 있음" : "놓을 자리 없음";
        FitSingleLine(badge);
    }

    /// <summary>
    /// 한 줄 라벨의 넘침을 말줄임으로 가둔다.
    ///
    /// 카드 안의 값(룬 이름·등급/칸수/속성 태그·배지)은 전부 <b>길이가 가변</b>인데 박스는 1줄 높이로 고정돼 있다.
    /// 기본 설정에서는 긴 이름이 줄바꿈되며 두 번째 줄이 박스를 뚫고 아래 요소 위로 겹쳤다.
    /// </summary>
    private static void FitSingleLine(TMP_Text t)
    {
        if (t == null) return;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode     = TextOverflowModes.Ellipsis;
    }

    /// <summary>효과 목록 박스(높이 86 · 행 20 + 간격 2)에 들어가는 최대 행 수.</summary>
    private const int MaxEffectRows = 4;   // 효과 칸 96 = 22×4 + 2×3

    private void BuildEffectRows(RectTransform parent, RuntimeItemData data)
    {
        if (data.effects == null) return;

        var style = EffectRowStyle.Default;

        style.wrap = true;   // 카드 폭(≈300px)에 '▲ 불 존에 놓으면 그 존의 시너지 효과 +20%'가 한 줄로 안 들어간다 — 실측 스크린샷에서 상자 밖으로 흘렀음

        style.fontSize        = 16f;   // 가독성 하한
        style.iconSize        = 24f;   // 기능 문양(효과 아이콘)이 룬의 얼굴이다 — 의뢰서 N8 32×32에 가깝게
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
    }

    /// <summary>모양 셀을 그리고, 지금 판에 놓을 자리가 있는지 반환한다.</summary>
    private bool BuildShapePreview(Transform root, RuntimeItemData data)
    {
        var entry = Managers.RuneData?.GetShape(data.shapeId);
        if (entry == null) return true;   // 판정 불가 → 막지 않는다

        var offsets = RuneDataManager.ParseCellOffsets(entry);
        if (offsets == null || offsets.Length == 0) return true;

        // 속성까지 넘긴다 — 룬은 자기 속성 존(레전드리는 중앙 제외)에만 놓이므로,
        // 모양만 보고 판정하면 "자리 있음"으로 뜬 룬이 막상 판에서는 들어갈 곳이 없다.
        bool canPlace = MerlinRuneBridge.Instance == null
            || MerlinRuneBridge.Instance.CanPlaceShape(offsets, data.element, data.rarity == ItemRarity.Legendary);

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var o in offsets)
        {
            if (o.x < minX) minX = o.x;  if (o.x > maxX) maxX = o.x;
            if (o.y < minY) minY = o.y;  if (o.y > maxY) maxY = o.y;
        }
        int cols = maxX - minX + 1, rows = maxY - minY + 1;

        // 박스에 꽉 차도록 셀 크기를 역산 — 1칸 룬은 크게, 큰 모양은 줄여서 항상 형태가 읽히게 한다.
        // 왼쪽 룬 아트 칸을 뺀 나머지가 모양이 쓸 수 있는 폭이다.
        float boxW = _cardW - 40f - 16f;
        float boxH = ShapeBoxH   - 16f;
        float fitW = (boxW - (cols - 1) * MiniGap) / Mathf.Max(1, cols);
        float fitH = (boxH - (rows - 1) * MiniGap) / Mathf.Max(1, rows);
        float cellSize = Mathf.Clamp(Mathf.Min(fitW, fitH), MiniCellMin, MiniCellMax);

        float totalW = cols * (cellSize + MiniGap) - MiniGap;
        float totalH = rows * (cellSize + MiniGap) - MiniGap;
        // 모양은 박스 전체가 아니라 <b>오른쪽 칸</b> 한가운데에 놓는다.
        float startX = -totalW * 0.5f + cellSize * 0.5f;
        float startY =  totalH * 0.5f - cellSize * 0.5f;

        // 스프라이트/틴트 규칙은 RuneArt.ResolveRuneCell 한곳에서 정한다(네 경로 동일 규칙).
        // 각인석이 아예 없을 때만 공용 룬 타일로 한 단계 더 떨어진다 —
        // 룬 타일은 단색 둥근 사각형이라 먼저 잡으면 모든 룬이 색 블록으로 보인다.
        RuneArt.ResolveRuneCell(data.element, data.rarity, new Color(0.7f, 0.7f, 0.75f),
            out Sprite art, out Color tint);
        if (art == null) art = UISkin.RuneSelect?.runeTile;

        // 판 위 블록과 <b>같은 속성 타일</b>로 그린다 — 미리보기의 목적이 "이 룬이 판에서 어떻게
        // 보일지"를 알려주는 것이라, 판과 다른 그림을 보여주면 설명이 되지 않는다.
        // 타일은 이미 속성색으로 그려져 있어 틴트를 곱하지 않는다.
        var blockTile = RuneArt.GetBlockTile(data.element);
        if (blockTile != null) { art = blockTile; tint = Color.white; }
        Color dimTint = new Color(tint.r * 0.5f, tint.g * 0.5f, tint.b * 0.5f, 0.7f);

        foreach (var o in offsets)
        {
            int col = o.x - minX;
            int row = maxY - o.y;

            var cell = ShopUIStyle.MakeImage(root, $"C{o.x}_{o.y}",
                canPlace ? tint : dimTint);
            if (art != null) { cell.sprite = art; cell.preserveAspect = true; }
            ShopUIStyle.Anchor(cell.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(startX + col * (cellSize + MiniGap), startY - row * (cellSize + MiniGap)),
                Vector2.one * cellSize);
        }

        return canPlace;
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
    /// 공개 연출의 마무리(<see cref="RevealCardInstant"/>)가 카드 배율·투명도를 되돌리므로,
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

            // 프레임 밝기(0.62↔1.0)만으로는 선택이 드러나지 않는다 — 그 위에 등급 테두리
            // (RarityFrame)가 더 밝고 두껍게 깔려 있어, 같은 등급 후보가 늘어서면 네 장이
            // 똑같이 빛나 보였다. 등급색과 충돌하지 않는 축인 <b>크기와 명암</b>으로 표시한다.
            if (_cards[i].Rt != null)
                _cards[i].Rt.localScale = Vector3.one * (on ? SelectedCardScale : 1f);
            if (_cards[i].Group != null)
                _cards[i].Group.alpha = (index < 0 || on) ? 1f : UnselectedCardAlpha;
        }

        bool hasSel = index >= 0;
        if (_confirmLabel != null)
        {
            _confirmLabel.color = hasSel ? ShopUIStyle.TextPrimary : ShopUIStyle.TextDim;
            if (hasSel) _confirmLabel.text = "선택";   // 미선택 안내를 띄웠다면 되돌린다
        }
        if (_confirmBtnImg != null)
            _confirmBtnImg.color = _skinned ? (hasSel ? Color.white : new Color(1f, 1f, 1f, 0.5f))
                                            : (hasSel ? ShopUIStyle.GoldPillBg : ShopUIStyle.BandFill);
    }

    private void RefreshCounter()
    {
        if (_counterText == null) return;
        int staged = _inventory?.StagingItems?.Count ?? 0;
        int placed = _inventory?.PlacedItems?.Count ?? 0;
        _counterText.text = $"보관함 {staged}/{RunItemInventory.MaxStagingCapacity}   ·   배치 {placed}";
    }

    // ── 버튼 ──

    private void OnConfirmClicked()
    {
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

        // 보관함 만차 시 조용히 사라지지 않도록, 실패해도 그리드가 '보류'로 들고 간다.
        bool added = _inventory != null && _inventory.AddToStaging(item);
        if (!added)
            ItemEffectVfxHelper.ShowNotice(
                $"<color=#FFCC44>보관함 가득 참</color> ({RunItemInventory.MaxStagingCapacity}칸) — 자리를 비우면 자동으로 추가됩니다");

        // 닫기(ClosePopupUI)를 resolve(TrySetResult)보다 먼저 — 순서가 뒤집히면 이벤트방에서 시간이 고착된다.
        // TrySetResult는 대기자(ClearRewardTrigger의 다중 라운드 3지선다)를 동기로 이어 곧바로 다음 라운드
        // 팝업을 push한다. 그 뒤에 ClosePopupUI를 부르면 이 팝업은 더 이상 스택 최상단이 아니라 닫기가
        // 무시되고("Close Popup Failed!"), 살아있는 좀비로 남아 BlocksGameplay가 계속 걸린 채 timeScale=0이
        // 영구 고착된다(이벤트방 Gold/Platinum 2라운드 이상 보상에서 재현). 그리드는 다음 라운드 팝업보다
        // 밑에 깔리도록 resolve 전에 연다.
        ClosePopupUI();

        if (UI_GridPanel.Instance == null)
            Managers.UI?.ShowOverlayUI<UI_GridPanel>();
        if (UI_GridPanel.Instance != null)
        {
            if (added) UI_GridPanel.Instance.ShowWithNewItem(item);
            else       UI_GridPanel.Instance.ShowWithPendingItem(item);
        }

        _interactionTcs?.TrySetResult();
    }

    private void OnSkipClicked()
    {
        Managers.Sound.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();
        Result  = null;
        Skipped = true;
        // 닫기를 resolve보다 먼저 — OnConfirmClicked와 동일 이유(다중 라운드 좀비 팝업 → timeScale 고착 방지).
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

    /// <summary>ElementDef.Order 내 인덱스(리본 아트 배열 접근용). 미지정/미발견은 -1 → 색 폴백.</summary>
    private static int ElementIndex(string elementId)
    {
        if (string.IsNullOrEmpty(elementId)) return -1;
        var order = ElementDef.Order;
        for (int i = 0; i < order.Count; i++)
            if (order[i] == elementId) return i;
        return -1;
    }

    private static int CellCount(int shapeId)
    {
        var entry = Managers.RuneData?.GetShape(shapeId);
        if (entry == null) return 0;
        var offsets = RuneDataManager.ParseCellOffsets(entry);
        return offsets?.Length ?? 0;
    }
}
