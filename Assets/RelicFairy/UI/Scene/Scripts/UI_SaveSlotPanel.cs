using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 저장 슬롯 선택 패널.
/// UI_Lobby에 SerializeField로 참조되며, Btn_StartRun 클릭 시 Open/Close.
/// 슬롯 선택 결과:
///   hasSave=true  → 이어하기 (RequestRestoreRun, 슬롯 인덱스 전달)
///   hasSave=false → 새로하기 (RequestStartRun, 스타트 방에서 인게임 선택)
///
/// 10-23 새 세이브 창(사용자 「너무 튀는 레거시 리소스라 깔끔하게 톤에 맞게 · 세이브 데이터를 잘 보여 줄 만한 내용으로 배경이랑 틀」):
///  - 화면 = 힉스필드 배경(달빛 드는 옛 성소 · 가운데 원탁) + 옅은 막 · 위에 「저장 기록」 제목과 금 가는 줄
///  - 카드 3장 = 흑요석 카드 틀(힉스필드 · 9칸) — 내용은 <see cref="UI_SaveSlotCard"/>
///  - 아래 「돌아가기」 · 삭제 확인과 이어하기 차단 안내는 공용 확인 창(<see cref="UI_ConfirmDialog"/>)
/// 목업은 1920×1080 px로 짓고 <see cref="UIWindowFitter"/>가 통째로 배율을 건다. 프리팹의 옛 계층(보라 연기 판 · Yes/No 그림)은 처음 열 때 지운다.
/// 그림은 <see cref="UISkin.SaveSlot"/>(없으면 색 판 — 화면은 그대로 동작).
/// </summary>
public class UI_SaveSlotPanel : UI_Base
{
    // ─────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────

    private const float StageW       = 1920f;
    private const float StageH       = 1080f;
    private const float TitleY       = 68f;
    private const float SubY         = 122f;
    private const float RuleY        = 166f;
    private const float RuleW        = 150f;
    private const float CardY        = 212f;
    private const float CardGap      = 44f;
    private const float BackY        = 928f, BackW = 240f, BackH = 52f;
    private const float EnterStagger = 0.06f;

    private static readonly Color BackdropFill = new(0.035f, 0.035f, 0.060f, 1f);    // 그림이 없을 때 · 그림 둘레
    private static readonly Color ShadeColor   = new(0.010f, 0.012f, 0.025f, 0.40f);  // 그림 위 막 — 카드 글이 먼저 읽히게
    private static readonly Color BackFill     = new(0.040f, 0.045f, 0.075f, 0.80f);

    private static UI_SaveSlotPanel s_open;   // ESC가 닫을, 지금 열린 패널(10-25)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_open = null;

    // ─────────────────────────────────────────────────────────
    // SerializeField
    // ─────────────────────────────────────────────────────────

    [Header("이어하기 임시 차단")]
    [Tooltip("켜면 저장된 슬롯을 눌러도 이어하기 대신 '세밀 작업 중' 안내를 띄운다. 재개하려면 끄면 된다.")]
    [SerializeField] private bool blockResume = false;
    [TextArea, SerializeField]
    private string blockResumeMessage =
        "현재 세밀 작업이 진행 중입니다.\n저장된 게임을 <b>삭제</b>한 뒤 새로 시작해 주세요.";

    // ─────────────────────────────────────────────────────────
    // Events
    // ─────────────────────────────────────────────────────────

    public event System.Action OnClosed;

    // ─────────────────────────────────────────────────────────
    // Private
    // ─────────────────────────────────────────────────────────

    private readonly UI_SaveSlotCard[] _cards = new UI_SaveSlotCard[RunProgressManager.SlotCount];
    private bool _built;
    private bool _opened;   // Open으로 연 적이 있다 — 아니면 닫을 때 페이드 없이 곧바로 끈다
    private CancellationTokenSource _enterCts;

    // ─────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────

    private void OnDestroy()
    {
        CancelEnter();
        if (s_open == this) s_open = null;
    }

    // ─────────────────────────────────────────────────────────
    // Public Methods
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// ESC(10-25 사용자 「모든 UI를 ESC로」) — 열려 있으면 「돌아가기」와 같이 닫는다. 슬롯을 눌러 화면이 덮이는 중(LeaveCovered)이면
    /// 손대지 않는다 — 그때 닫으면 로비가 다시 비쳤다(10-23).
    /// </summary>
    public static bool CloseIfOpen()
    {
        if (s_open == null || !s_open._opened) return false;
        s_open.Close();
        return true;
    }

    /// <summary>패널을 열고 슬롯 카드를 최신 세이브 데이터로 갱신한다. 처음 열 때 짓는다(스킨은 앱 부트에서 미리 올라와 있다).</summary>
    public override void Open()
    {
        EnsureBuilt();
        RefreshAllCards();
        _opened = true;
        s_open  = this;
        UIFader.On(gameObject).Show();   // 순간 등장 → 공통 박자(09-28 UI 톤 통일)
        Managers.Sound?.PlayUiEvent(SoundEvent.UiOpen);   // 10-29 UI 소리 — 열기
        PlayEnter();
    }

    public override void Close()
    {
        CancelEnter();
        // 열어 둔 적이 없으면(로비가 켜질 때의 정리) 곧바로 끈다 — 페이드하면 로비 첫 화면에 슬롯이 잠깐 비친다.
        // 닫는 순간 알린다 — 로비 제목이 슬롯이 걷히는 동안 함께 떠오른다(교차 페이드).
        if (_opened) Managers.Sound?.PlayUiEvent(SoundEvent.UiClose);   // 10-29 UI 소리 — 닫기(로비가 켜질 때의 정리는 조용히)
        if (_opened) UIFader.On(gameObject).Hide();
        else         gameObject.SetActive(false);
        _opened = false;
        if (s_open == this) s_open = null;
        OnClosed?.Invoke();
    }

    // ─────────────────────────────────────────────────────────
    // Private Methods — 짓기
    // ─────────────────────────────────────────────────────────

    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        // 옛 구운 계층(Dim 보라 연기 판 · Panel_Main 카드 · Btn_Back · ConfirmRoot Yes/No)을 걷는다 — 새 창은 늘 여기서 짓는다.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var old = transform.GetChild(i).gameObject;
            old.SetActive(false);
            Destroy(old);
        }

        var skin = UISkin.SaveSlot;
        ShopUIStyle.Stretch((RectTransform)transform);

        // 배경 — 화면을 덮도록 비율을 지켜 키운다(가장자리가 조금 잘린다). 판이 뒤쪽 로비 클릭을 막는다.
        var backdrop = ShopUIStyle.MakeImage(transform, "Backdrop", BackdropFill, raycast: true);
        ShopUIStyle.Stretch(backdrop.rectTransform);
        var art = skin != null ? skin.Background : null;
        if (art != null)
        {
            var pic = ShopUIStyle.MakeImage(backdrop.transform, "Art", Color.white);
            pic.sprite = art;
            var prt = pic.rectTransform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            var fit = pic.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode  = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = art.rect.width / Mathf.Max(1f, art.rect.height);
        }
        var shade = ShopUIStyle.MakeImage(transform, "Shade", ShadeColor);
        ShopUIStyle.Stretch(shade.rectTransform);

        // 무대 — 1920×1080 목업 그대로 짓고 화면 비율에 맞춰 통째로 줄인다(넓은 화면은 1배 · 좁은 화면은 맞춰 줄임).
        var stage = (RectTransform)ShopUIStyle.MakeRect(transform, "Stage").transform;
        var half  = new Vector2(0.5f, 0.5f);
        ShopUIStyle.Anchor(stage, half, half, half, Vector2.zero, new Vector2(StageW, StageH));   // 맞춤이 Awake에서 이 크기를 기준으로 잡는다 — 먼저
        stage.gameObject.AddComponent<UIWindowFitter>().Configure(margin: 1f, scaleFonts: false, maxScale: 1f, scaleTransform: true);

        BuildHeader(stage);

        float total = UI_SaveSlotCard.Width * _cards.Length + CardGap * (_cards.Length - 1);
        float x0    = (StageW - total) * 0.5f;
        var frame   = skin != null ? skin.CardFrame : null;
        for (int i = 0; i < _cards.Length; i++)
            _cards[i] = UI_SaveSlotCard.Create(stage, i, x0 + (UI_SaveSlotCard.Width + CardGap) * i, CardY, frame);

        var back = SaveSlotUI.MakeButton(stage, "Btn_Back", (StageW - BackW) * 0.5f, BackY, BackW, BackH, BackFill, UITheme.Thin.LineIdle,
                                         "돌아가기", 18f, UITheme.Thin.Ink, out _, out _, out _);
        back.onClick.AddListener(Close);
    }

    private static void BuildHeader(RectTransform stage)
    {
        var title = SaveSlotUI.Text(stage, "Title", 0f, TitleY, StageW, 46f, 34f, UIWeight.Medium, UITheme.Thin.Ink, TextAlignmentOptions.Center);
        title.text = "저장 기록";
        title.characterSpacing = 8f;
        var sub = SaveSlotUI.Text(stage, "Sub", 0f, SubY, StageW, 26f, 17f, UIWeight.Light, UITheme.Thin.InkA(0.62f), TextAlignmentOptions.Center);
        sub.text = "이어 할 기록을 고르거나, 빈 칸에서 새로 시작한다";

        // 금 가는 줄 둘 + 가운데 마름모
        float cx = StageW * 0.5f;
        SaveSlotUI.Img(stage, "RuleL", cx - 14f - RuleW, RuleY, RuleW, 1f, UITheme.Thin.LineGold);
        SaveSlotUI.Img(stage, "RuleR", cx + 14f,         RuleY, RuleW, 1f, UITheme.Thin.LineGold);
        SaveSlotUI.Diamond(stage, "RuleGem", cx, RuleY + 0.5f, 7f, UITheme.Thin.WithA(UITheme.Thin.Gold, 0.85f), outline: false);
    }

    // ─────────────────────────────────────────────────────────
    // Private Methods — Cards
    // ─────────────────────────────────────────────────────────

    private void RefreshAllCards()
    {
        var rpm = RunProgressManager.Instance;
        for (int i = 0; i < _cards.Length; i++)
            _cards[i].Setup(ResolveSlotData(rpm, i), i, OnSlotStartClicked, OnSlotDeleteClicked);
    }

    private void RefreshCard(int slotIndex)
    {
        if (!_built || slotIndex < 0 || slotIndex >= _cards.Length) return;
        var rpm = RunProgressManager.Instance;
        _cards[slotIndex].Setup(ResolveSlotData(rpm, slotIndex), slotIndex, OnSlotStartClicked, OnSlotDeleteClicked);
    }

    /// <summary>진행 중 런은 슬롯별 로컬 파일이 단독 권위. 활성 로컬 런이 있으면 그것을, 없으면 빈 슬롯(null).</summary>
    private static RunSaveData ResolveSlotData(RunProgressManager rpm, int i)
    {
        if (rpm == null || i < 0 || i >= RunProgressManager.SlotCount) return null;
        // 프롤로그를 끝냈는데 기록이 없는 슬롯(예전 판의 런 종료가 기록을 지웠다)은 허브 기록을 다시 세운다 —
        // 「새 게임」으로 보이면 누르는 순간 메타가 지워진다(10-09 출시 점검).
        rpm.RepairHubRecord(i);
        var local = rpm.LoadLocalRun(i);
        return local != null && local.hasActiveRun ? local : null;
    }

    /// <summary>카드가 60ms 간격으로 떠오른다(아래 18px에서 · 0.26초) — 상점 · 룬 선택 팝업과 같은 결.</summary>
    private void PlayEnter()
    {
        CancelEnter();
        _enterCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        for (int i = 0; i < _cards.Length; i++)
            _cards[i].PlayEnterAsync(EnterStagger * i, _enterCts.Token).Forget();
    }

    private void CancelEnter()
    {
        if (_enterCts == null) return;
        _enterCts.Cancel();
        _enterCts.Dispose();
        _enterCts = null;
    }

    // ─────────────────────────────────────────────────────────
    // Event Handlers
    // ─────────────────────────────────────────────────────────

    private void OnSlotStartClicked(int slotIndex, bool hasSave)
    {
        // [임시] 이어하기 차단 — 저장된 슬롯을 눌러도 재개하지 않고 안내만 띄운다.
        // 패널은 열어 둔 채라 사용자가 곧바로 삭제 버튼을 누를 수 있다. (blockResume 끄면 원복)
        if (hasSave && blockResume)
        {
            UI_ConfirmDialog.Show("이어하기를 잠시 막아 두었습니다", blockResumeMessage, "확인", null, null);
            return;
        }

        Managers.Sound?.PlayUiEvent(SoundEvent.UiConfirm);   // 10-29 UI 소리 — 이 기록으로 들어간다(이어하기 차단 안내는 위에서 확인 창이 낸다)
        var rpm = RunProgressManager.Instance;
        if (rpm != null) rpm.ActiveSlotIndex = slotIndex;

        // 슬롯 = 독립 세이브. 진행 런뿐 아니라 영구 메타(각성·정수·봉인)와 퀘스트도 이 슬롯 것으로 갈아끼운다.
        // 여기서 안 바꾸면 직전에 고른 슬롯의 성장과 퀘스트 완료가 그대로 따라 들어간다.
        BackendGameData.Instance?.ApplyActiveSlot();
        Managers.Quest?.ReloadForActiveSlot();

        LeaveCovered();

        Managers.Sound.FadeOutBgmAsync().Forget();

        if (hasSave)
            AppBootstrapper.Instance?.RequestRestoreRun(onFailed: Open);
        else
            AppBootstrapper.Instance?.RequestStartRun();
    }

    /// <summary>
    /// 창을 닫지 않고 덮인 채로 떠난다 — 다음 화면(시작 영상 · 로딩 막)이 위에서 덮는다.
    /// 닫으면 그 화면이 덮기 전까지(새 게임 = 영상 준비 ≈0.8초) 로비가 다시 비쳤고, 빌드에선 그 틈에 유니티 기본 배경이 새어 나왔다(10-23).
    /// 덮이는 동안 다시 누르지 못하게 버튼만 막는다. 로비에 돌아오면 UI_Lobby.OnEnable의 Close가 페이드 없이 곧바로 끈다 · 이어하기 실패면 Open이 다시 연다.
    /// </summary>
    private void LeaveCovered()
    {
        CancelEnter();
        _opened = false;
        if (TryGetComponent<CanvasGroup>(out var cg)) cg.interactable = false;
    }

    /// <summary>한 번 더 묻는다(되돌릴 수 없음 — 붉은 결정 버튼). 「삭제」를 눌러야만 지운다.</summary>
    private void OnSlotDeleteClicked(int slotIndex)
    {
        int n = slotIndex + 1;
        string body = $"기록 {n}{(n == 2 ? "를" : "을")} 삭제하시겠습니까?\n" +
                      "진행 중인 런과 이 기록의 성장(심연의 정수 · 해금 · 봉인)이 모두 사라지며 되돌릴 수 없습니다.";
        UI_ConfirmDialog.Show("기록 삭제", body, "삭제", "취소", () => DeleteSlot(slotIndex), danger: true);
    }

    private void DeleteSlot(int slotIndex)
    {
        var rpm = RunProgressManager.Instance;
        if (rpm == null || slotIndex < 0) return;

        // 이 슬롯의 로컬 런 세이브만 폐기(다른 슬롯 무영향). 로컬이 단독 권위.
        rpm.ResetSlot(slotIndex);   // 슬롯 삭제 = 이 슬롯으로 다시 시작하면 초회부터
        RefreshCard(slotIndex);
        if (_enterCts != null && slotIndex < _cards.Length)
            _cards[slotIndex].PlayEnterAsync(0f, _enterCts.Token).Forget();   // 빈 기록으로 바뀌는 것이 보이게 한 번 더 떠오른다
    }
}
