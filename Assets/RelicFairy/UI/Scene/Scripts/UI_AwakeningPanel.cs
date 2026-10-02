using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 기억의 제단 (구 「유물의 각성」 팝업).
/// BaseCamp(WorldAwakeningAltar)와 로비(Btn_Awakening) 양쪽에서 열린다.
///
/// <para><b>파는 것</b> — 「가능성의 확장」 노드. 남은 영구 스탯은 최대 체력 하나뿐이고 영구 공격력은 0이다.</para>
///
/// <para><b>화면 구조(09-29 개편)</b> — 해금 탭은 <b>가운데 제단에서 갈래 다섯이 자라는 트리</b>다(<see cref="AltarTreeView"/>).
/// 산 노드에서 뻗은 선 끝이 곧 다음 목표이고, 오른쪽 상세 패널(<see cref="AltarDetailPanel"/>)에서 산다.
/// 예전의 5열 목록 · 아래 행동 바는 해금 탭에서 걷었다(행동 바는 업적 탭의 [모두 받기]로만 쓴다).</para>
///
/// <para><b>조건은 자물쇠가 아니라 할인이다.</b> 모든 노드는 정수만으로 열린다(자물쇠 예외 둘: 챕터 4 · 심연).</para>
/// </summary>
public class UI_AwakeningPanel : UI_Popup
{
    // ── Constants ──────────────────────────────────────────────────────────
    /// <summary>
    /// ESC로 닫힌다. 화면 우상단에 「ESC 닫기」 안내를 띄우면서 이 값을 안 올려서,
    /// ESC가 <see cref="UIManager.TryCloseTopPopupOnEscape"/>에 <b>소비만 되고 아무 일도 안 났다</b>.
    /// 닫기 버튼과 같은 경로(ClosePopupUI)라 저장·정리 흐름도 동일하다.
    /// </summary>
    public override bool CloseOnEscape => true;

    /// <summary>
    /// 화면 전체를 덮는 모달이므로 <b>게임플레이를 막는다</b>.
    /// 이걸 켜지 않으면 (1) 뒤에서 플레이어가 계속 움직이고,
    /// (2) <see cref="UIInputGate.Blocked"/>가 false로 남아 F키로 폴링하는 월드 상호작용이
    /// 팝업 위에서 그대로 발동하며, (3) 이 화면의 연출이 전제하는 timeScale=0이 성립하지 않는다.
    /// 상점·재련소·정제소·서약·룬선택·유물정보가 모두 같은 규약이다.
    /// </summary>
    public override bool BlocksGameplay => true;

    private const float PanelWidth   = 1720f;   // 트리가 가로로 넓다(예전 5열 판 폭 그대로)
    private const float TreeTop      = 226f;    // 헤더 가로줄(Rule, 위에서 219) 바로 아래
    private const float TreeSide     = 36f;
    private const float TreeBottom   = 24f;
    private const float DetailWidth  = 380f;

    // ── 직렬화 필드 ────────────────────────────────────────────────────────
    [Header("헤더")]
    [SerializeField] private TMP_Text essenceText;

    [Tooltip("다음 해금까지의 진행바. 미배선이면 코드가 헤더 아래에 만들어 붙인다.")]
    [SerializeField] private TMP_Text nextGoalText;
    [SerializeField] private Image    nextGoalFill;

    [Header("상위 탭 (업적 / 해금)")]
    [SerializeField] private Button   achievementTab;
    [SerializeField] private TMP_Text achievementTabLabel;
    [SerializeField] private Image    achievementTabUnderline;
    [SerializeField] private Image    achievementBadge;
    [SerializeField] private Button   unlockTab;
    [SerializeField] private TMP_Text unlockTabLabel;
    [SerializeField] private Image    unlockTabUnderline;

    [Header("모드별 루트")]
    [SerializeField] private GameObject unlockRoot;
    [SerializeField] private GameObject achievementRoot;
    [SerializeField] private AchievementListView achievementList;

    [Header("예전 5열 목록 (09-29 트리 개편으로 숨김)")]
    [SerializeField] private Transform[] branchColumns;
    [SerializeField] private AltarNodeRowView rowTemplate;

    [Header("하단 행동 바 (업적 탭)")]
    [SerializeField] private TMP_Text actionName;
    [SerializeField] private TMP_Text actionDesc;
    [SerializeField] private TMP_Text actionCondition;
    [SerializeField] private Button   actionButton;
    [SerializeField] private Image    actionButtonImage;
    [SerializeField] private TMP_Text actionButtonLabel;
    [SerializeField] private TMP_Text actionButtonSub;

    [Header("닫기")]
    [SerializeField] private Button closeButton;

    [Header("조회 전용 (로비에서 열릴 때)")]
    [Tooltip("켜면 해금·수령이 동작하지 않는다. 로비에서 정수를 쓸 수 있으면 거점으로 돌아올 이유가 사라진다.")]
    [SerializeField] private bool readOnly;

    // ── 비공개 필드 ────────────────────────────────────────────────────────
    private static readonly Color BannerFill = new(0.09f, 0.08f, 0.07f, 0.94f);
    private const float BannerY = -18f;

    private RectTransform _banner;
    private CanvasGroup   _bannerGroup;
    private TMP_Text      _bannerTitle, _bannerDesc;

    private readonly AltarTreePresenter _presenter = new();
    private AltarTreeView    _tree;
    private AltarDetailPanel _detail;
    private CancellationTokenSource _fxCts;
    private bool _achievementMode;
    private bool _openPlayed;
    private bool _refreshing;   // 갱신 중 자동 초점은 소리 없이
    private readonly Dictionary<string, AltarNodeVisual> _before = new(40);

    /// <summary>패널이 사라졌음을 알린다 — 제단(WorldAwakeningAltar)의 중복 오픈 가드 해제용.</summary>
    public event Action OnClosed;

    // ── Properties ────────────────────────────────────────────────────────

    /// <summary>
    /// 로비 등 <b>거점 밖</b>에서 열 때 켠다.
    /// <para>팝업 핸들은 <c>Init()</c>이 끝난 뒤에 돌아오므로 여기서 다시 그려야 한다 —
    /// 값만 바꾸면 이미 연결된 행동이 그대로 남는다.</para>
    /// </summary>
    public bool ReadOnly
    {
        get => readOnly;
        set
        {
            if (readOnly == value) return;
            readOnly = value;
            if (achievementList != null) { achievementList.ReadOnly = value; achievementList.Refresh(); }
            Refresh();
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public override void Init()
    {
        base.Init();

        // 판 폭을 먼저 정한다 — 아래 창 맞춤(UIWindowFitter)은 붙는 순간의 판 크기를 기준으로 잡는다.
        if (transform.Find("Panel_Main") is RectTransform widen) widen.sizeDelta = new Vector2(PanelWidth, widen.sizeDelta.y);

        // 컨텐츠 화면 크기 규칙을 다른 화면과 맞춘다. 이 화면만 맞춤 장치가 없어
        // 프리팹 크기(1560×1000) 그대로 열렸고, 재련소·상점(세로 94%)과 크기가 달랐다.
        if (transform.Find("Panel_Main") is RectTransform panel &&
            panel.GetComponent<UIWindowFitter>() == null)
        {
            panel.gameObject.AddComponent<UIWindowFitter>()
                 .Configure(maxScale: UIWindowFitter.ContentScreen);
        }

        // 판 테두리가 없어 모서리가 뚝 끊긴 평판이었다 — 전 화면 공통 글래스(둥근 모서리 + 금 가는 선, 09-28 UI 톤 통일).
        if (transform.Find("Panel_Main") is RectTransform main && main.TryGetComponent<Image>(out var mainImg))
            UITheme.StylePanel(mainImg, mainImg.color, UITheme.GoldLine, 18f);

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(OnCloseClicked);

            // 「ESC 닫기」 안내(Txt_CloseHint)는 버튼의 <b>자식</b>이다(09-09 프리팹). 형제였을 땐 이 탐색이 비어
            // 프리팹 글자(어두운 4E4A61)가 판 위에 묻혀 있었다.
            var label = closeButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text      = "닫기  ESC";
                label.alignment = TextAlignmentOptions.Center;
                label.color     = AltarPalette.TextPrimary;
            }

            // 와이어프레임(09-09): 우상단 닫기는 글자만이 아니라 <b>둥근 판 버튼</b>이다. 아트 없이 코드 판으로.
            if (closeButton.TryGetComponent<Image>(out var closeImg))
            {
                closeImg.sprite = UIProceduralSprites.RoundedRect(radius: 8f, feather: 2f);
                closeImg.type   = Image.Type.Sliced;
                closeImg.color  = new Color(0.16f, 0.16f, 0.18f, 1f);
            }
        }
        if (actionButton != null) actionButton.onClick.AddListener(OnActionClicked);

        if (achievementTab != null) achievementTab.onClick.AddListener(() => { PlayClickSfx(); SetMode(true); });
        if (unlockTab      != null) unlockTab.onClick.AddListener(() => { PlayClickSfx(); SetMode(false); });

        if (achievementList != null)
        {
            achievementList.ReadOnly = readOnly;
            achievementList.OnClaimed += HandleClaimed;
        }

        BuildTree();

        // 납품 하단바(853×106)를 ActionBar 바탕에 깐다 — 합성본(업적3)의 「받아갈 것이 N개 있다」 띠.
        // 띠가 판 폭을 따라 늘어나므로 가로 9-slice(좌우 24, SkinArtImporter)로 둥근 끝만 지킨다.
        if (transform.Find("Panel_Main/ActionBar") is RectTransform actionBar &&
            actionBar.TryGetComponent<Image>(out var actionBarImage))
            ShopUIStyle.Skin(actionBarImage, UISkin.Achievement?.bottomBar, sliced: true);

        // 폐기된 각성 6계열에 쓴 정수를 되돌린다(1회성). 제단을 여는 시점엔 비용표 로드가 끝나 있다.
        // 10-02 재설계로 뺀 유물 노드 4개(파츠 선택지 +1 · 코어 2·3종 · 이어받기)에 쓴 정수도 돌려준다(1회성).
        int refunded = readOnly ? 0 : MemoryAltarService.TryRefundLegacyAwakening() + MemoryAltarService.TryRefundRemovedNodes();
        if (refunded > 0)
            SaveAsync().Forget();

        // 받아갈 것이 있으면 업적을 먼저 연다 — 받는 쪽을 먼저 보여야
        // 「받았다 → 이제 열 수 있다」가 한 화면에서 이어진다.
        SetMode(WaitingAchievements() > 0);
    }

    protected override void OnDestroy()
    {
        if (achievementList != null)
            achievementList.OnClaimed -= HandleClaimed;
        _fxCts?.Cancel();
        _fxCts?.Dispose();

        base.OnDestroy();
        OnClosed?.Invoke();
    }

    // ── Private Methods ───────────────────────────────────────────────────

    /// <summary>
    /// 해금 탭을 트리로 — 예전 5열 목록은 숨기고, 헤더 가로줄 아래 영역에 트리(왼쪽)와 상세 패널(오른쪽)을 세운다.
    /// </summary>
    private void BuildTree()
    {
        if (branchColumns != null && branchColumns.Length > 0 && branchColumns[0] != null && branchColumns[0].parent != null)
            branchColumns[0].parent.gameObject.SetActive(false);
        if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);
        if (unlockRoot == null || !(unlockRoot.transform is RectTransform root)) return;

        var areaGo = new GameObject("AltarTreeArea", typeof(RectTransform));
        var area = (RectTransform)areaGo.transform;
        area.SetParent(root, false);
        area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
        area.offsetMin = new Vector2(TreeSide, TreeBottom);
        area.offsetMax = new Vector2(-TreeSide, -TreeTop);

        _detail = AltarDetailPanel.Create(area, DetailWidth, 0f);
        _tree   = AltarTreeView.Create(area, DetailWidth + 20f);
        _tree.transform.SetAsFirstSibling();

        _tree.NodeFocused   += OnTreeFocused;
        _tree.NodeSubmitted += OnTreeSubmitted;
        _detail.ActionClicked += OnDetailAction;
    }

    private void SetMode(bool achievements)
    {
        _achievementMode = achievements;
        if (transform.Find("Panel_Main/ActionBar") is RectTransform bar) bar.gameObject.SetActive(achievements);
        if (achievements) LayoutActionBar();
        if (unlockRoot)      unlockRoot.SetActive(!achievements);
        if (achievementRoot) achievementRoot.SetActive(achievements);

        if (achievementTabUnderline) achievementTabUnderline.gameObject.SetActive(achievements);
        if (unlockTabUnderline)      unlockTabUnderline.gameObject.SetActive(!achievements);
        if (achievementTabLabel)     achievementTabLabel.color = achievements ? AltarPalette.TextPrimary : AltarPalette.TextDim;
        if (unlockTabLabel)          unlockTabLabel.color      = achievements ? AltarPalette.TextDim : AltarPalette.TextPrimary;

        if (achievements) achievementList?.Refresh();
        Refresh();

        // 트리를 처음 보는 순간 한 번 — 제단 문양이 그려지고 산 길이 자란다.
        if (!achievements && !_openPlayed && _tree != null)
        {
            _openPlayed = true;
            PlayOpenAsync().Forget();
        }
    }

    private void Refresh()
    {
        int essence = MemoryAltarService.Essence;
        CurrencyCounter.Apply(essenceText, essence);
        // 정수 글자가 창 윗변에 붙어 있다 — 차감 숫자가 위로 뜨면 테두리에 걸린다(09-29 실측). 아래로 띄운다.
        if (essenceText != null && essenceText.TryGetComponent<CurrencyCounter>(out var counter)) counter.PlaceDeltaBelow();

        RefreshNextGoalBar(essence);
        RefreshBadge();

        if (_achievementMode) { RefreshAchievementAction(); return; }
        if (_tree == null) return;

        var models = _presenter.Build();
        _tree.Apply(models, _presenter.Center(essence), instant: true);
        _refreshing = true;
        if (_tree.FocusedNode == null) _tree.Focus(_presenter.AutoFocus(), select: true);
        _refreshing = false;
        _detail.Show(_presenter.Detail(_tree.FocusedNode, essence, readOnly));
    }

    /// <summary>
    /// 헤더의 <b>다음 해금 진행바</b>. 업적을 수령하면 이 막대가 차오르고, 다 차면 문구가 바뀐다 —
    /// 정수를 받는 곳과 쓰는 곳을 같은 화면에 둔 이유가 여기서 눈에 보인다.
    /// 해금 탭에선 트리 가운데 제단 문양이 같은 말을 하므로 숨긴다(09-29).
    /// </summary>
    private void RefreshNextGoalBar(int essence)
    {
        EnsureNextGoalBar();
        if (nextGoalText == null) return;

        nextGoalText.gameObject.SetActive(_achievementMode);
        if (nextGoalFill != null && nextGoalFill.transform.parent != null)
            nextGoalFill.transform.parent.gameObject.SetActive(_achievementMode);
        if (!_achievementMode) return;

        var next = MemoryAltarService.GetNextGoal();
        if (next == null)
        {
            nextGoalText.text = "열 수 있는 것을 모두 열었다";
            nextGoalText.color = AltarPalette.TextFaint;
            if (nextGoalFill) SetFill(nextGoalFill, 1f);
            return;
        }

        var goal = next.Value;
        int left = Mathf.Max(0, goal.Cost - essence);
        bool ready = left <= 0;

        nextGoalText.text  = ready
            ? $"다음 · 「{goal.Node.DisplayName}」        해금 가능"
            : $"다음 · 「{goal.Node.DisplayName}」        -{left:N0}";
        nextGoalText.color = ready ? AltarPalette.Gold : AltarPalette.TextDim;
        if (nextGoalFill) SetFill(nextGoalFill, goal.Cost > 0 ? (float)essence / goal.Cost : 1f);
    }

    private static void SetFill(Image fill, float ratio01)
    {
        var rt = fill.rectTransform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(Mathf.Clamp01(ratio01), 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>프리팹에 아직 없으면 정수 표시 아래에 만들어 붙인다(아트 배선 전에도 동작하게).</summary>
    private void EnsureNextGoalBar()
    {
        if (nextGoalText != null || essenceText == null) return;

        var anchor = essenceText.rectTransform;
        var parent = anchor.parent;

        var label = new GameObject("Txt_NextGoal", typeof(RectTransform)).GetComponent<RectTransform>();
        label.SetParent(parent, false);
        CopyAnchors(anchor, label, yOffset: -56f, height: 22f);   // 16px 줄 높이(20.6)가 들어가야 자동 축소가 안 걸린다
        nextGoalText = label.gameObject.AddComponent<TextMeshProUGUI>();
        nextGoalText.fontSize = 16f;
        // 글자가 판 밖으로 나가지 않게 — 최대는 설계 크기로 묶으므로 커지지 않고, 안 들어갈 때만 줄어든다.
        nextGoalText.enableAutoSizing = true;
        nextGoalText.fontSizeMax = 16f;
        nextGoalText.fontSizeMin = 10f;
        nextGoalText.alignment = TextAlignmentOptions.Right;
        nextGoalText.color = AltarPalette.TextDim;
        nextGoalText.raycastTarget = false;

        var track = new GameObject("Bar_NextGoal", typeof(RectTransform)).GetComponent<RectTransform>();
        track.SetParent(parent, false);
        CopyAnchors(anchor, track, yOffset: -80f, height: 10f);
        var trackImg = track.gameObject.AddComponent<Image>();
        trackImg.color = AltarPalette.AccentLocked;
        trackImg.raycastTarget = false;

        var fill = new GameObject("Fill", typeof(RectTransform)).GetComponent<RectTransform>();
        fill.SetParent(track, false);
        nextGoalFill = fill.gameObject.AddComponent<Image>();
        nextGoalFill.color = AltarPalette.Essence;
        nextGoalFill.raycastTarget = false;
        SetFill(nextGoalFill, 0f);
    }

    private static void CopyAnchors(RectTransform src, RectTransform dst, float yOffset, float height)
    {
        dst.anchorMin = src.anchorMin;
        dst.anchorMax = src.anchorMax;
        dst.pivot     = src.pivot;
        dst.anchoredPosition = src.anchoredPosition + new Vector2(0f, yOffset);

        // 세로로 늘린 앵커에서 sizeDelta.y는 높이가 아니라 <b>앵커 높이에 더해지는 값</b>이다.
        // 기준으로 삼는 Txt_Essence가 양축 비율 앵커(세로 스팬 0.054 × 부모 1000 = 54)라,
        // 예전처럼 sizeDelta.y = height로 대입하면 10짜리 막대가 64, 20짜리 라벨이 74가 됐다.
        dst.sizeDelta = new Vector2(src.sizeDelta.x, dst.sizeDelta.y);   // 가로는 기준과 같게
        dst.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
    }

    /// <summary>미수령 업적이 있으면 탭에 ●를 켠다 — 이 점 하나가 업적을 열게 만든다.</summary>
    private void RefreshBadge()
    {
        int waiting = WaitingAchievements();
        if (achievementBadge) achievementBadge.gameObject.SetActive(waiting > 0);
        if (achievementTabLabel)
            achievementTabLabel.text = waiting > 0 ? $"업적  {waiting}" : "업적";
    }

    /// <summary>
    /// 업적 탭의 하단 행동 바 — <b>[모두 받기] 주 버튼</b>.
    /// 수령이 무엇을 여는지도 함께 말한다 — 정수를 받는 곳과 쓰는 곳을 같은 화면에 둔 이유가
    /// 이 한 줄에서 성립한다("받자마자 쓸 수 있다").
    /// </summary>
    private void RefreshAchievementAction()
    {
        int waiting = WaitingAchievements();
        if (waiting <= 0)
        {
            SetActionText("받아갈 것이 없다", "달성한 업적은 여기서 수령한다", "");
            SetActionButton(false, "—", "");
            return;
        }

        int gain = achievementList != null ? achievementList.PendingEssence() : 0;
        SetActionText($"받아갈 것이 {waiting}개 있다", BuildClaimLead(gain), "");
        SetActionButton(true, $"모두 받기 {waiting}", gain > 0 ? $"+{gain:N0}" : "");
    }

    /// <summary>수령 후 무엇이 열리는지 한 줄 — 다음 해금까지 남는 정수를 계산해 붙인다.</summary>
    private static string BuildClaimLead(int gain)
    {
        var next = MemoryAltarService.GetNextGoal();
        if (next == null || gain <= 0) return "수령한 정수는 그대로 「해금」에서 쓸 수 있다";

        var goal  = next.Value;
        int after = MemoryAltarService.Essence + gain;
        int left  = goal.Cost - after;
        return left <= 0
            ? $"수령하면 +{gain:N0} — 「{goal.Node.DisplayName}」을 바로 열 수 있다"
            : $"수령하면 +{gain:N0} — 「{goal.Node.DisplayName}」 해금까지 {left:N0} 남는다";
    }

    private void SetActionText(string title, string desc, string cond)
    {
        if (actionName)      actionName.text = title;
        if (actionDesc)      actionDesc.text = desc;
        if (actionCondition) actionCondition.text = cond;
    }

    /// <summary>업적 탭 하단 행동 바 = 의뢰서 02(띠 126 · 두 줄 + 큰 버튼).</summary>
    private void LayoutActionBar()
    {
        if (!(transform.Find("Panel_Main/ActionBar") is RectTransform bar)) return;
        static void Anc(Component c, float x0, float y0, float x1, float y1)
        {
            if (c == null) return;
            var rt = (RectTransform)c.transform;
            rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        bar.anchorMin = new Vector2(0f, 0f); bar.anchorMax = new Vector2(1f, 0f);
        bar.anchoredPosition = new Vector2(0f, 26f); bar.sizeDelta = new Vector2(-88f, 126f);
        Anc(actionName,      0.0177f, 0.587f, 0.643f, 0.841f);
        Anc(actionDesc,      0.0177f, 0.381f, 0.643f, 0.571f);
        Anc(actionCondition, 0.0177f, 0.175f, 0.643f, 0.349f);
        Anc(actionButton,    0.799f,  0.222f, 0.982f, 0.778f);
        if (actionName) actionName.fontSize = 22f;
        if (actionDesc) actionDesc.color = AltarPalette.TextPrimary;
        if (actionName)      actionName.alignment      = TextAlignmentOptions.MidlineLeft;
        if (actionDesc)      actionDesc.alignment      = TextAlignmentOptions.MidlineLeft;
        if (actionCondition) actionCondition.alignment = TextAlignmentOptions.MidlineLeft;
    }

    private void SetActionButton(bool enabled, string label, string sub)
    {
        bool usable = enabled && !readOnly;

        if (actionButton)      actionButton.interactable = usable;
        if (actionButtonImage) actionButtonImage.color = usable ? AltarPalette.Gold : AltarPalette.BtnQuiet;
        UIAffordGlow.Set(actionButtonImage, usable);   // 지금 받을 수 있으면 은은한 불(09-29)
        if (actionButtonLabel)
        {
            actionButtonLabel.text  = readOnly && enabled ? "제단에서 받기" : label;
            actionButtonLabel.color = usable ? AltarPalette.OnGold : AltarPalette.TextDim;
        }
        if (actionButtonSub)
        {
            actionButtonSub.gameObject.SetActive(!string.IsNullOrEmpty(sub));
            actionButtonSub.text = sub;
        }
    }

    /// <summary>
    /// 해금 한 번 — 규칙 검사 · 소리 · 저장 · 연출. 고행자의 인장은 이미 열려 있으면 <b>착용 스위치</b>다.
    /// </summary>
    private void PurchaseNode(MemoryAltarNode node)
    {
        if (readOnly || node == null) return;

        if (node.Id == MemoryAltarCatalog.SigilAscetic && MemoryAltarService.IsUnlocked(node.Id))
        {
            AsceticSigilService.Toggle();
            ShopUIStyle.PlaySfx("shop_click");
            SaveAndRefreshAsync().Forget();
            return;
        }
        if (MemoryAltarService.IsUnlocked(node.Id)) return;

        // 연출은 「해금 전 → 뒤」를 비교해 새로 열린 것만 움직인다 — 사기 전 화면 상태를 떠 둔다.
        _before.Clear();
        foreach (var kv in _presenter.Build()) _before[kv.Key] = kv.Value.Visual;

        // 되돌릴 수 없는 메타 진행이다 — 열렸는지 아닌지가 소리와 움직임으로도 남아야 한다.
        if (!MemoryAltarService.TryUnlock(node))
        {
            ShopUIStyle.PlaySfx("shop_reject");
            Refresh();
            return;
        }
        SaveRefreshAndCelebrateAsync(node).Forget();
    }

    private async UniTaskVoid SaveAndRefreshAsync()
    {
        await SaveAsync();
        Refresh();
    }

    /// <summary>
    /// 저장 · 갱신 뒤 트리 연출(각인 → 점화 → 빛실 → 개화) · 배너. 연출 중에 또 사면 앞 연출은 끝 상태로 바로 넘어간다.
    /// </summary>
    private async UniTaskVoid SaveRefreshAndCelebrateAsync(MemoryAltarNode node)
    {
        _fxCts?.Cancel(); _fxCts?.Dispose();
        _fxCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        var ct = _fxCts.Token;

        var before = new Dictionary<string, AltarNodeVisual>(_before);
        await SaveAsync();
        if (this == null) return;
        Refresh();
        try
        {
            await _tree.PlayUnlockAsync(node, before, ct);

            // 새로 산 노드에서 바로 살 수 있는 자식이 돋았으면 초점을 옮긴다 — 다음 목표가 손끝에 온다.
            foreach (var child in MemoryAltarCatalog.Children(node))
                if (MemoryAltarService.GetState(child).CanBuy) { _tree.Focus(child, select: true); break; }

            await ShowUnlockBannerAsync(node, ct);
        }
        catch (OperationCanceledException) { }
    }

    private async UniTaskVoid PlayOpenAsync()
    {
        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            await _tree.PlayOpenAsync(ct);

            // 새 시기 고리가 처음 드러났다(붕괴 · 엔딩 뒤 첫 방문) — 고리가 그려지고 그 띠의 노드가 스며든다(10-02). 봉인기는 처음부터라 연출 없음.
            var data = BackendGameData.Instance?.Data;
            int era  = MemoryAltarCatalog.RevealedEra;
            int seen = Mathf.Max(1, data?.GetRecord(MemoryAltarCatalog.Rec.AltarEraSeen) ?? 1);
            if (data != null && era > seen)
            {
                for (int e = seen + 1; e <= era; e++) await _tree.PlayEraRevealAsync(e, ct);
                data.SetRecordMax(MemoryAltarCatalog.Rec.AltarEraSeen, era);
                if (!readOnly) SaveAsync().Forget();
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 「무엇이 열렸는가」 배너. 이름 한 줄 + <b>변화 한 줄</b>(A → B)이다.
    /// <para>화면 위쪽 가운데에 잠깐 떴다가 사라진다 — 확인 버튼을 요구하지 않는다.
    /// 해금은 이미 끝난 일이고, 배너는 통보지 질문이 아니다.</para>
    /// </summary>
    private async UniTask ShowUnlockBannerAsync(MemoryAltarNode node, CancellationToken ct)
    {
        if (node == null) return;
        EnsureBanner();
        if (_banner == null) return;

        if (_bannerTitle != null) _bannerTitle.text = $"열렸다 — {node.DisplayName}";
        if (_bannerDesc  != null) _bannerDesc.text  = node.Description;

        _banner.gameObject.SetActive(true);
        var group = _bannerGroup;
        var rt    = _banner;

        const float In = 0.22f, Hold = 1.9f, Out = 0.45f;
        try
        {
            float t = 0f;
            while (t < In)                                  // 살짝 내려오며 떠오른다
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / In);
                group.alpha = k;
                rt.anchoredPosition = new Vector2(0f, Mathf.Lerp(BannerY + 18f, BannerY, k));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            group.alpha = 1f;
            rt.anchoredPosition = new Vector2(0f, BannerY);

            await UniTask.Delay(TimeSpan.FromSeconds(Hold), DelayType.UnscaledDeltaTime,
                                PlayerLoopTiming.Update, ct);

            t = 0f;
            while (t < Out)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = 1f - Mathf.Clamp01(t / Out);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_banner != null) _banner.gameObject.SetActive(false);
        }
    }

    /// <summary>배너를 1회 만든다. 프리팹에 없는 요소라 런타임에 세운다. 공통 글래스 판(09-29).</summary>
    private void EnsureBanner()
    {
        if (_banner != null) return;

        var parent = unlockRoot != null ? unlockRoot.transform : transform;
        var go = new GameObject("UnlockBanner", typeof(RectTransform));
        _banner = (RectTransform)go.transform;
        _banner.SetParent(parent, false);
        _banner.anchorMin = new Vector2(0.5f, 1f);
        _banner.anchorMax = new Vector2(0.5f, 1f);
        _banner.pivot     = new Vector2(0.5f, 1f);
        _banner.sizeDelta = new Vector2(560f, 76f);
        _banner.anchoredPosition = new Vector2(0f, BannerY);

        var bg = go.AddComponent<Image>();
        UITheme.StylePanel(bg, BannerFill, UITheme.GoldLine, 12f);
        bg.raycastTarget = false;                 // 통보일 뿐 — 아래 조작을 막으면 안 된다
        _bannerGroup     = go.AddComponent<CanvasGroup>();
        _bannerGroup.blocksRaycasts = false;
        _bannerGroup.interactable   = false;

        _bannerTitle = MakeBannerText("Title", 22f, FontStyles.Bold,   AltarPalette.Gold,   new Vector2(0f, -12f), 28f);
        _bannerDesc  = MakeBannerText("Desc",  16f, FontStyles.Normal, AltarPalette.Essence, new Vector2(0f, -42f), 24f);

        go.SetActive(false);
    }

    private TMP_Text MakeBannerText(string name, float size, FontStyles style, Color color,
                                    Vector2 pos, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_banner, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(14f, rt.offsetMin.y);
        rt.offsetMax = new Vector2(-14f, rt.offsetMax.y);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);

        var txt = go.AddComponent<TextMeshProUGUI>();
        txt.fontSize      = size;
        // 글자가 판 밖으로 나가지 않게 — 최대는 설계 크기로 묶으므로 커지지 않고, 안 들어갈 때만 줄어든다.
        txt.enableAutoSizing = true;
        txt.fontSizeMax      = size;
        txt.fontSizeMin      = Mathf.Max(9f, size * 0.55f);
        txt.fontStyle     = style;
        txt.color         = color;
        txt.alignment     = TextAlignmentOptions.Center;
        txt.raycastTarget = false;
        txt.textWrappingMode = TextWrappingModes.NoWrap;
        txt.overflowMode  = TextOverflowModes.Ellipsis;
        return txt;
    }

    private async UniTask SaveAsync()
    {
        try
        {
            if (BackendGameData.Instance != null)
                await BackendGameData.Instance.SaveAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogWarning($"[MemoryAltar] 저장 실패: {e.Message}");
        }
    }

    private static int WaitingAchievements() => Managers.Quest?.WaitingAchievementCount() ?? 0;

    private static void PlayClickSfx() => Managers.Sound?.PlayUiAsync(SoundKey.Sfx.UiButton).Forget();

    private void HandleClaimed() => Refresh();

    // ── Event Handlers ────────────────────────────────────────────────────

    private void OnCloseClicked()
    {
        PlayClickSfx();
        ClosePopupUI();
    }

    /// <summary>하단 행동 바 = 업적 탭의 [모두 받기]. 스태거 연출·저장은 목록 뷰가 소유하므로 위임한다.</summary>
    private void OnActionClicked()
    {
        if (readOnly) return;
        if (_achievementMode) achievementList?.ClaimAll();
    }

    private void OnTreeFocused(MemoryAltarNode node)
    {
        if (!_refreshing) ShopUIStyle.PlaySfx("shop_select");
        _detail?.Show(_presenter.Detail(node, MemoryAltarService.Essence, readOnly));
    }

    private void OnTreeSubmitted(MemoryAltarNode node) => PurchaseNode(node);

    private void OnDetailAction() => PurchaseNode(_tree != null ? _tree.FocusedNode : null);
}
