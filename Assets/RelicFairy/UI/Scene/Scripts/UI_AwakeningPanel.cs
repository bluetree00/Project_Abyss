using System;
using System.Collections.Generic;
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
    // ── 직렬화 필드 ────────────────────────────────────────────────────────
    [Header("헤더")]
    [SerializeField] private TMP_Text essenceText;

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

    private readonly List<List<AltarNodeRowView>> _rows = new();
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

        if (closeButton  != null) closeButton.onClick.AddListener(ClosePopupUI);
        if (actionButton != null) actionButton.onClick.AddListener(OnActionClicked);

        if (achievementTab != null) achievementTab.onClick.AddListener(() => SetMode(true));
        if (unlockTab      != null) unlockTab.onClick.AddListener(() => SetMode(false));

        if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);

        if (achievementList != null)
        {
            achievementList.ReadOnly = readOnly;
            achievementList.OnClaimed += HandleClaimed;
        }

        BuildColumns();

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

            if (branchTitles    != null && c < branchTitles.Length    && branchTitles[c])
                branchTitles[c].text = MemoryAltarCatalog.BranchLabel(Branches[c]);
            if (branchQuestions != null && c < branchQuestions.Length && branchQuestions[c])
                branchQuestions[c].text = BranchQuestions[c];

            if (c >= branchColumns.Length || branchColumns[c] == null) continue;

            var nodes = MemoryAltarCatalog.GetBranch(Branches[c]);
            if (branchCounts != null && c < branchCounts.Length && branchCounts[c])
                branchCounts[c].text = nodes.Count.ToString();

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

    private void SetMode(bool achievements)
    {
        _achievementMode = achievements;

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
        if (essenceText) essenceText.text = $"{essence:N0}";

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
        }

        RefreshActionBar(essence);
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
            SetActionButton(false, "닫기", "");
            return;
        }

        var node = state.Node;
        bool isGoal = _selected == null;

        SetActionText(isGoal ? $"다음 「{node.DisplayName}」" : node.DisplayName,
                      node.Description,
                      BuildActionCondition(state, node));

        if (state.Unlocked)                  SetActionButton(false, "해금됨", "");
        else if (state.BlockedByRequirement) SetActionButton(false, "선행 조건 필요", node.ConditionLabel);
        else if (state.CanBuy)               SetActionButton(true,  $"{state.Cost:N0} ◆ 해금", "");
        else                                 SetActionButton(false, "정수 부족", $"{state.Cost - essence:N0} 모자람");
    }

    private void RefreshAchievementAction()
    {
        int waiting = WaitingAchievements();
        if (waiting <= 0)
        {
            SetActionText("받아갈 것이 없다", "달성한 업적은 여기서 수령한다", "");
            SetActionButton(false, "—", "");
            return;
        }

        SetActionText($"받아갈 것이 {waiting}개 있다",
                      "수령한 정수는 그대로 「해금」에서 쓸 수 있다", "");
        SetActionButton(false, "목록에서 받기", "");
    }

    private static string BuildActionCondition(AltarNodeState state, MemoryAltarNode node)
    {
        if (state.Unlocked)     return "";
        if (!node.HasCondition) return "조건 없음 — 언제든 열 수 있다";

        if (state.ConditionMet)
            return $"✔ {node.ConditionLabel} — 할인가 적용 중";

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
        if (actionButtonSub)
        {
            actionButtonSub.gameObject.SetActive(!string.IsNullOrEmpty(sub));
            actionButtonSub.text = sub;
        }
    }

    private static int WaitingAchievements() => Managers.Quest?.WaitingAchievementCount() ?? 0;

    private void HandleClaimed() => Refresh();

    // ── Event Handlers ────────────────────────────────────────────────────

    private void OnActionClicked()
    {
        if (readOnly || _achievementMode) return;

        // 고른 것이 없으면 하단 바가 「다음 목표」를 가리키고 있으므로 그것을 산다.
        var node = _selected ?? MemoryAltarService.GetNextGoal()?.Node;
        if (node == null) return;

        if (!MemoryAltarService.TryUnlock(node)) { Refresh(); return; }

        SaveAndRefreshAsync().Forget();
    }

    private async UniTaskVoid SaveAndRefreshAsync()
    {
        await SaveAsync();
        Refresh();
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
