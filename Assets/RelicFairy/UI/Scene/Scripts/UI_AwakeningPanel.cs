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
/// <para><b>파는 것</b> — 각성 6계열 영구 스탯을 폐기하고 「가능성의 확장」 18노드를 판다.
/// 남은 영구 스탯은 최대 체력 하나뿐이고 영구 공격력은 0이다.</para>
///
/// <para><b>화면 구조</b> — 갈래 탭을 쓰지 않고 <b>네 갈래를 한 화면에</b> 편다.
/// 탭으로 나누면 18노드 중 8개만 보여 "무엇을 목표로 잡을까"를 화면에서 정할 수 없고,
/// 그래서 「다음 목표」를 따로 띄워야 했다 — 그건 구조 결함의 반창고였다.
/// 대신 행에서 버튼을 걷어내고 <b>화면 아래 한 곳</b>에서만 산다. 아무것도 안 고르면 그 자리가 다음 목표를 말한다.</para>
///
/// <para><b>조건은 자물쇠가 아니라 할인이다.</b> 모든 노드는 정수만으로 열린다.</para>
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

    [Header("갈래 열 (출발·등장·존속·심연 순)")]
    [SerializeField] private Transform[] branchColumns;   // 4개
    [SerializeField] private TMP_Text[]  branchTitles;    // 4개
    [SerializeField] private TMP_Text[]  branchQuestions; // 4개
    [SerializeField] private TMP_Text[]  branchCounts;    // 4개
    [SerializeField] private AltarNodeRowView rowTemplate;

    [Header("하단 행동 바")]
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
    private static readonly AltarBranch[] Branches =
        { AltarBranch.Start, AltarBranch.Appear, AltarBranch.Endure, AltarBranch.Abyss };

    private static readonly string[] BranchQuestions =
        { "무엇으로 시작하는가", "무엇이 나올 수 있는가", "얼마나 버틸 수 있는가", "얼마나 깊이 갈 수 있는가" };

    private static readonly Color BannerFill = new(0.09f, 0.08f, 0.07f, 0.94f);
    private const float BannerY = -18f;
    private const float RowSpacing = 22f;   // 열 VLG spacing — 와이어프레임 행 간격

    private RectTransform _banner;
    private CanvasGroup   _bannerGroup;
    private TMP_Text      _bannerTitle, _bannerDesc;

    private readonly List<List<AltarNodeRowView>> _rows = new();
    private readonly List<List<MemoryAltarNode>>   _branchNodes = new();
    private MemoryAltarNode _selected;
    private bool _achievementMode;

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

        // 컨텐츠 화면 크기 규칙을 다른 화면과 맞춘다. 이 화면만 맞춤 장치가 없어
        // 프리팹 크기(1560×1000) 그대로 열렸고, 재련소·상점(세로 94%)과 크기가 달랐다.
        if (transform.Find("Panel_Main") is RectTransform panel &&
            panel.GetComponent<UIWindowFitter>() == null)
        {
            panel.gameObject.AddComponent<UIWindowFitter>()
                 .Configure(maxScale: UIWindowFitter.ContentScreen);
        }

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

        if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);

        if (achievementList != null)
        {
            achievementList.ReadOnly = readOnly;
            achievementList.OnClaimed += HandleClaimed;
        }

        BuildColumns();

        // 납품 하단바(853×106)를 ActionBar 바탕에 깐다 — 합성본(업적3)의 「받아갈 것이 N개 있다」 띠.
        // 띠가 판 폭을 따라 늘어나므로 가로 9-slice(좌우 24, SkinArtImporter)로 둥근 끝만 지킨다.
        if (transform.Find("Panel_Main/ActionBar") is RectTransform actionBar &&
            actionBar.TryGetComponent<Image>(out var actionBarImage))
            ShopUIStyle.Skin(actionBarImage, UISkin.Achievement?.bottomBar, sliced: true);

        // 폐기된 각성 6계열에 쓴 정수를 되돌린다(1회성). 제단을 여는 시점엔 비용표 로드가 끝나 있다.
        if (!readOnly && MemoryAltarService.TryRefundLegacyAwakening() > 0)
            SaveAsync().Forget();

        // 받아갈 것이 있으면 업적을 먼저 연다 — 받는 쪽을 먼저 보여야
        // 「받았다 → 이제 열 수 있다」가 한 화면에서 이어진다.
        SetMode(WaitingAchievements() > 0);
    }

    protected override void OnDestroy()
    {
        if (achievementList != null)
            achievementList.OnClaimed -= HandleClaimed;

        base.OnDestroy();
        OnClosed?.Invoke();
    }

    // ── Private Methods ───────────────────────────────────────────────────

    /// <summary>열마다 노드를 한 번만 만든다. 갈래를 오갈 일이 없으니 재생성도 없다.</summary>
    private void BuildColumns()
    {
        if (rowTemplate == null || branchColumns == null) return;

        for (int c = 0; c < Branches.Length; c++)
        {
            var list = new List<AltarNodeRowView>();
            _rows.Add(list);

            // 열이 비어 있어도 자리를 채운다 — 아래 continue로 건너뛰면 _rows와 인덱스가 어긋나
            // 갈래 진척(RefreshBranchCounts)이 옆 열의 숫자를 쓴다.
            var nodes = MemoryAltarCatalog.GetBranch(Branches[c]);
            _branchNodes.Add(nodes);

            if (branchTitles    != null && c < branchTitles.Length    && branchTitles[c])
                branchTitles[c].text = MemoryAltarCatalog.BranchLabel(Branches[c]);
            if (branchQuestions != null && c < branchQuestions.Length && branchQuestions[c])
                branchQuestions[c].text = BranchQuestions[c];

            if (c >= branchColumns.Length || branchColumns[c] == null) continue;

            foreach (var node in nodes)
            {
                var row = Instantiate(rowTemplate, branchColumns[c]);
                row.gameObject.SetActive(true);

                var captured = node;
                row.SetOnSelect(() => Select(captured));
                list.Add(row);
            }
        }
    }

    /// <summary>
    /// 열 머리의 진척 표시(2/5). 사슬이라 <b>어디까지 왔는지</b>가 갈래마다 하나의 숫자로 떨어진다.
    /// 다 열면 숫자 대신 ✦ — 목록의 끝이 아니라 갈래의 완성으로 읽힌다.
    /// </summary>
    private void RefreshBranchCounts()
    {
        if (branchCounts == null) return;

        for (int c = 0; c < _branchNodes.Count && c < branchCounts.Length; c++)
        {
            if (branchCounts[c] == null) continue;

            var nodes = _branchNodes[c];
            int done = 0;
            for (int i = 0; i < nodes.Count; i++)
                if (MemoryAltarService.IsUnlocked(nodes[i].Id)) done++;

            bool complete = done >= nodes.Count && nodes.Count > 0;
            branchCounts[c].text  = complete ? "◆" : $"{done}/{nodes.Count}";
            branchCounts[c].color = complete ? AltarPalette.Essence : AltarPalette.TextDim;
        }
    }

    private void SetMode(bool achievements)
    {
        _achievementMode = achievements;
        LayoutActionBar(achievements);
        if (unlockRoot)      unlockRoot.SetActive(!achievements);
        if (achievementRoot) achievementRoot.SetActive(achievements);

        if (achievementTabUnderline) achievementTabUnderline.gameObject.SetActive(achievements);
        if (unlockTabUnderline)      unlockTabUnderline.gameObject.SetActive(!achievements);
        if (achievementTabLabel)     achievementTabLabel.color = achievements ? AltarPalette.TextPrimary : AltarPalette.TextDim;
        if (unlockTabLabel)          unlockTabLabel.color      = achievements ? AltarPalette.TextDim : AltarPalette.TextPrimary;

        if (achievements) achievementList?.Refresh();
        Refresh();
    }

    private void Select(MemoryAltarNode node)
    {
        _selected = node;
        Refresh();
    }

    private void Refresh()
    {
        int essence = MemoryAltarService.Essence;
        CurrencyCounter.Apply(essenceText, essence);

        RefreshNextGoalBar(essence);
        RefreshBadge();

        if (!_achievementMode)
        {
            for (int c = 0; c < _rows.Count && c < Branches.Length; c++)
            {
                var nodes = MemoryAltarCatalog.GetBranch(Branches[c]);
                var list  = _rows[c];
                for (int i = 0; i < list.Count && i < nodes.Count; i++)
                    list[i].Refresh(MemoryAltarService.GetState(nodes[i]), essence, nodes[i] == _selected);
            }

            RefreshBranchCounts();
        }

        RefreshActionBar(essence);
    }

    /// <summary>
    /// 헤더의 <b>다음 해금 진행바</b>. 업적을 수령하면 이 막대가 차오르고, 다 차면 문구가 바뀐다 —
    /// 정수를 받는 곳과 쓰는 곳을 같은 화면에 둔 이유가 여기서 눈에 보인다.
    ///
    /// 정수 숫자만으로는 "받아서 뭐가 되는가"를 알 수 없어, 수령의 결과가 화면에서 사라져 있었다.
    /// </summary>
    private void RefreshNextGoalBar(int essence)
    {
        EnsureNextGoalBar();
        if (nextGoalText == null) return;

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
        // 그 판이 헤더 밴드를 가로질러 「등장」 열 머리를 통째로 덮었다.
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
    /// 화면의 <b>유일한 행동 지점</b>. 고른 것이 없으면 「다음 목표」를 대신 말한다 —
    /// 그래야 헤더에 목표 줄을 따로 붙일 필요가 없다.
    /// </summary>
    private void RefreshActionBar(int essence)
    {
        if (_achievementMode) { RefreshAchievementAction(); return; }

        var state = _selected != null
            ? MemoryAltarService.GetState(_selected)
            : MemoryAltarService.GetNextGoal() ?? default;

        if (state.Node == null)
        {
            SetActionText("전부 열었다", "이제 남은 것은 깊이뿐이다", "");
            // 「닫기」라고 써 두면 눌리지 않는 버튼이 고장으로 읽힌다 — 업적 탭의 빈 상태와 같은 표기로 맞춘다.
            SetActionButton(false, "—", "");
            return;
        }

        var node = state.Node;
        bool isGoal = _selected == null;

        SetActionText(isGoal ? $"다음 「{node.DisplayName}」" : node.DisplayName,
                      node.Description,
                      BuildActionCondition(state, node));

        if (state.Unlocked)                  RefreshUnlockedAction(node);
        else if (state.BlockedByChain)
        {
            var prev = MemoryAltarCatalog.PreviousInBranch(node);
            SetActionButton(false, "아직 차례가 아니다", prev != null ? $"앞의 「{prev.DisplayName}」을 먼저 연다" : "");
        }
        else if (state.BlockedByRequirement) SetActionButton(false, "선행 조건 필요", node.ConditionLabel);
        else if (state.CanBuy)               SetActionButton(true,  $"{state.Cost:N0} ◆ 해금", "");
        else                                 SetActionButton(false, "정수 부족", $"{state.Cost - essence:N0} 모자람");
    }

    /// <summary>
    /// 이미 열린 노드의 행동 바. 대부분은 「해금됨」으로 끝나지만, <b>계속 조작할 것이 있는</b>
    /// 두 노드는 여기서 그 조작을 받는다 — 열고 나서도 화면이 죽지 않는다.
    ///
    /// <para>「고행자의 인장」은 해금이 곧 적용이 아니다. 자발적 난이도라 켜고 끌 수 있어야 하고,
    /// 그 스위치를 둘 자리로는 이 노드 자신이 가장 자연스럽다(새 화면이 필요 없다).</para>
    ///
    /// <para>「파츠 영구 계승」은 자동이라 조작이 없지만, <b>지금 무엇을 물고 있는지</b>는 보여야 한다.
    /// 안 보이면 다음 런이 왜 달라졌는지 설명되지 않는다.</para>
    /// </summary>
    private void RefreshUnlockedAction(MemoryAltarNode node)
    {
        if (node.Id == MemoryAltarCatalog.SigilAscetic)
        {
            bool on = AsceticSigilService.Active;
            SetActionButton(true, on ? "인장 해제" : "인장 착용",
                            on ? "지금 착용 중 · 보상 -1개 / 정수 ×1.6" : "켜면 다음 런부터 적용된다");
            return;
        }

        if (node.Id == MemoryAltarCatalog.PartsInherit)
        {
            string name = PartInheritanceService.InheritedPartName;
            SetActionButton(false, "해금됨",
                            string.IsNullOrEmpty(name)
                                ? "런을 마치면 마지막에 고른 파츠가 계승된다"
                                : $"현재 계승 · {name}");
            return;
        }

        SetActionButton(false, "해금됨", "");
    }

    /// <summary>
    /// 업적 탭의 하단 행동 바 — <b>[모두 받기] 주 버튼</b>.
    ///
    /// 예전엔 여기에 항상 비활성인 「목록에서 받기」를 띄웠다. 1472×126을 차지하면서 눌리지 않아
    /// "고장난 버튼"으로 읽혔고, 정작 주 행동은 목록 안 작은 버튼에만 있었다.
    /// 해금 탭의 구매 버튼과 같은 자리라 조작 위치도 일관된다.
    ///
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

    private static string BuildActionCondition(AltarNodeState state, MemoryAltarNode node)
    {
        if (state.Unlocked)     return "";
        if (!node.HasCondition) return "조건 없음 — 언제든 열 수 있다";

        if (state.ConditionMet)
            return $"■ {node.ConditionLabel} — 할인가 적용 중";

        return node.ConditionRequired
            ? $"선행 조건 · {node.ConditionLabel}  {state.Progress}/{state.Target}"
            : $"조건 · {node.ConditionLabel}  {state.Progress}/{state.Target} → 달성하면 {node.DiscountCost:N0}";
    }

    private void SetActionText(string title, string desc, string cond)
    {
        if (actionName)      actionName.text = title;
        if (actionDesc)      actionDesc.text = desc;
        if (actionCondition) actionCondition.text = cond;
    }

    /// <summary>
    /// 하단 행동 바의 두 얼굴. 업적 탭 = 의뢰서 02(띠 126 · 두 줄 + 큰 버튼). 해금 탭 = 와이어프레임(09-09):
    /// 얇은 띠 66 · 「이름  변화」 한 줄 + 조건 한 줄 · 오른쪽 작은 주황 버튼 176×46. 열 폭(0.049~0.958)과 맞춘다.
    /// </summary>
    private void LayoutActionBar(bool achievements)
    {
        if (!(transform.Find("Panel_Main/ActionBar") is RectTransform bar)) return;
        static void Anc(Component c, float x0, float y0, float x1, float y1)
        {
            if (c == null) return;
            var rt = (RectTransform)c.transform;
            rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        if (achievements)
        {
            bar.anchorMin = new Vector2(0f, 0f); bar.anchorMax = new Vector2(1f, 0f);
            bar.anchoredPosition = new Vector2(0f, 26f); bar.sizeDelta = new Vector2(-88f, 126f);
            Anc(actionName,      0.0177f, 0.587f, 0.643f, 0.841f);
            Anc(actionDesc,      0.0177f, 0.381f, 0.643f, 0.571f);
            Anc(actionCondition, 0.0177f, 0.175f, 0.643f, 0.349f);
            Anc(actionButton,    0.799f,  0.222f, 0.982f, 0.778f);
            if (actionName) actionName.fontSize = 22f;
            if (actionDesc) actionDesc.color = AltarPalette.TextPrimary;
        }
        else
        {
            bar.anchorMin = new Vector2(0.049f, 0f); bar.anchorMax = new Vector2(0.958f, 0f);
            bar.anchoredPosition = new Vector2(0f, 65f); bar.sizeDelta = new Vector2(0f, 66f);
            Anc(actionName,      0.016f, 0.50f, 0.34f, 0.95f);
            Anc(actionDesc,      0.34f,  0.50f, 0.84f, 0.95f);
            Anc(actionCondition, 0.016f, 0.05f, 0.84f, 0.48f);
            Anc(actionButton,    0.868f, 0.14f, 0.984f, 0.86f);
            if (actionName) actionName.fontSize = 20f;
            if (actionDesc) actionDesc.color = AltarPalette.Gold;   // 「변화」는 이 화면의 값이다
        }
        if (actionName)      actionName.alignment      = TextAlignmentOptions.MidlineLeft;
        if (actionDesc)      actionDesc.alignment      = TextAlignmentOptions.MidlineLeft;
        if (actionCondition) actionCondition.alignment = TextAlignmentOptions.MidlineLeft;
    }

    private void SetActionButton(bool enabled, string label, string sub)
    {
        bool usable = enabled && !readOnly;

        if (actionButton)      actionButton.interactable = usable;
        if (actionButtonImage) actionButtonImage.color = usable ? AltarPalette.Gold : AltarPalette.BtnQuiet;
        if (actionButtonLabel)
        {
            actionButtonLabel.text  = readOnly && enabled ? "제단에서 해금" : label;
            actionButtonLabel.color = usable ? AltarPalette.OnGold : AltarPalette.TextDim;
        }
        // 해금 탭의 얇은 바(66px)엔 버튼 부제가 들어갈 자리가 없다 — 조건 줄 끝에 붙인다.
        bool subOnButton = _achievementMode && !string.IsNullOrEmpty(sub);
        if (actionButtonSub)
        {
            actionButtonSub.gameObject.SetActive(subOnButton);
            actionButtonSub.text = sub;
        }
        if (!_achievementMode && !string.IsNullOrEmpty(sub) && actionCondition)
            actionCondition.text = string.IsNullOrEmpty(actionCondition.text) ? sub : actionCondition.text + "   ·   " + sub;
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

    private void OnActionClicked()
    {
        if (readOnly) return;

        // 업적 탭에서는 이 버튼이 [모두 받기]다. 스태거 연출·저장은 목록 뷰가 소유하므로 위임한다.
        if (_achievementMode) { achievementList?.ClaimAll(); return; }

        // 고른 것이 없으면 하단 바가 「다음 목표」를 가리키고 있으므로 그것을 산다.
        var node = _selected ?? MemoryAltarService.GetNextGoal()?.Node;
        if (node == null) return;

        // 이미 열린 「고행자의 인장」에서는 이 버튼이 <b>착용 스위치</b>다(구매가 아니다).
        if (node.Id == MemoryAltarCatalog.SigilAscetic && MemoryAltarService.IsUnlocked(node.Id))
        {
            AsceticSigilService.Toggle();
            ShopUIStyle.PlaySfx("shop_click");
            SaveAndRefreshAsync().Forget();
            return;
        }

        // 되돌릴 수 없는 메타 진행이다 — 열렸는지 아닌지가 소리와 움직임으로도 남아야 한다.
        if (!MemoryAltarService.TryUnlock(node))
        {
            ShopUIStyle.PlaySfx("shop_reject");
            Refresh();
            return;
        }

        ShopUIStyle.PlaySfx("enhance_success");
        PunchActionButtonAsync().Forget();
        // 갱신이 먼저다 — 행이 다시 그려진 뒤에 그 행을 밝혀야 연출이 결과 위에 얹힌다.
        SaveRefreshAndCelebrateAsync(node).Forget();
    }

    /// <summary>해금 직후 행동 버튼을 한 번 튕긴다(팝업은 timeScale=0이라 unscaled 트윈뿐이다).</summary>
    private async UniTaskVoid PunchActionButtonAsync()
    {
        if (actionButton == null) return;
        try
        {
            await UIJuice.PunchAsync((RectTransform)actionButton.transform, 0.12f, 0.25f,
                                     this.GetCancellationTokenOnDestroy());
        }
        catch (OperationCanceledException) { }
    }

    private async UniTaskVoid SaveAndRefreshAsync()
    {
        await SaveAsync();
        Refresh();
    }

    /// <summary>
    /// 저장·갱신 뒤 <b>무엇이 열렸는지</b>를 화면에 남긴다 — 기획 「순차해금 개편」 §7-1 해금 순간(1.2초).
    ///
    /// <para>0.00 정수 카운터 감소 + 판 미세 진동 → 0.25 산 칸 점화 → 0.55 빛이 사슬을 타고 내려감 →
    /// 0.90 다음 칸이 차례로 승격(축약 행에서 자라며 테두리 점등·◆ 이동) → 1.20 하단 행동 바 교체 강조 + 배너.</para>
    ///
    /// <para>핵심은 0.55~0.90이다 — "이걸 샀더니 다음이 열렸다"가 몸으로 읽히는 구간. 갱신(Refresh)은 먼저 해 두고,
    /// 다음 칸만 잠깐 옛 높이로 되돌려 자라는 모습을 보여준다.</para>
    /// </summary>
    private async UniTaskVoid SaveRefreshAndCelebrateAsync(MemoryAltarNode node)
    {
        var nextNode = NextInBranch(node);
        var nextRow  = FindRow(nextNode);
        float nextFrom = nextRow != null ? nextRow.CurrentHeight : 0f;   // Refresh 전(축약 행) 높이

        await SaveAsync();
        Refresh();

        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            if (nextRow != null && nextFrom > 0f) nextRow.SetHeightImmediate(nextFrom);

            ShakePanelAsync(ct).Forget();                                            // 0.00
            await UniTask.Delay(250, DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, ct);

            var row = FindRow(node);
            if (row != null) row.PlayUnlockAsync(ct).Forget();                       // 0.25 점화
            await UniTask.Delay(300, DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, ct);

            if (row != null && nextRow != null)                                      // 0.55 빛 흐름 — 표식 중심 사이
                await row.PlayFlowDownAsync(row.CurrentHeight * 0.5f + RowSpacing + nextFrom * 0.5f, ct);
            if (nextRow != null) await nextRow.PlayBecomeTurnAsync(nextFrom, ct);   // 0.90 승격

            FlashActionBarAsync(ct).Forget();                                        // 1.20 행동 바 교체 강조
            await ShowUnlockBannerAsync(node, ct);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>같은 갈래에서 바로 다음 칸. 마지막이면 null.</summary>
    private MemoryAltarNode NextInBranch(MemoryAltarNode node)
    {
        if (node == null) return null;
        for (int c = 0; c < _branchNodes.Count; c++)
        {
            int i = _branchNodes[c].IndexOf(node);
            if (i >= 0) return i + 1 < _branchNodes[c].Count ? _branchNodes[c][i + 1] : null;
        }
        return null;
    }

    /// <summary>기획 §7-1 0.00 「화면 미세 진동」 — 판을 ±2px로 0.15초 흔든다(unscaled).</summary>
    private async UniTaskVoid ShakePanelAsync(CancellationToken ct)
    {
        if (!(transform.Find("Panel_Main") is RectTransform panel)) return;
        var basePos = panel.anchoredPosition;
        const float Dur = 0.15f;
        float t = 0f;
        try
        {
            while (t < Dur)
            {
                t += Time.unscaledDeltaTime;
                float k = 1f - Mathf.Clamp01(t / Dur);
                panel.anchoredPosition = basePos + new Vector2(
                    Mathf.Sin(t * 90f) * 2f * k, Mathf.Cos(t * 70f) * 1.5f * k);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally { if (panel != null) panel.anchoredPosition = basePos; }
    }

    /// <summary>기획 §7-1 1.20 — 하단 행동 바가 새 「다음 목표」로 바뀌었음을 한 번 밝혀 알린다.</summary>
    private async UniTaskVoid FlashActionBarAsync(CancellationToken ct)
    {
        if (!(transform.Find("Panel_Main/ActionBar") is RectTransform bar) ||
            !bar.TryGetComponent<Image>(out var img)) return;
        var baseCol = img.color;
        var lit     = Color.Lerp(baseCol, Color.white, 0.35f);
        const float Dur = 0.45f;
        float t = 0f;
        try
        {
            while (t < Dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / Dur);
                img.color = Color.Lerp(lit, baseCol, k);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally { if (img != null) img.color = baseCol; }
    }

    /// <summary>
    /// 「무엇이 열렸는가」 배너. 이름 한 줄 + <b>변화 한 줄</b>(A → B)이다.
    ///
    /// <para>이름만 띄우면 "샀다"까지만 전해진다. 정작 알아야 할 것은 <b>내 다음 런이 어떻게 달라지는가</b>라
    /// 노드가 이미 갖고 있는 변화 문구를 그대로 얹는다.</para>
    ///
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

            await UniTask.Delay(System.TimeSpan.FromSeconds(Hold), DelayType.UnscaledDeltaTime,
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

    /// <summary>배너를 1회 만든다. 프리팹에 없는 요소라 런타임에 세운다.</summary>
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
        bg.color         = BannerFill;
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

    /// <summary>노드에 대응하는 행을 찾는다. 열·행 목록이 카탈로그 순서와 1:1이라 인덱스로 떨어진다.</summary>
    private AltarNodeRowView FindRow(MemoryAltarNode node)
    {
        if (node == null) return null;
        for (int c = 0; c < _branchNodes.Count && c < _rows.Count; c++)
        {
            int i = _branchNodes[c].IndexOf(node);
            if (i >= 0 && i < _rows[c].Count) return _rows[c][i];
        }
        return null;
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
}
