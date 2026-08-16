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

        if (closeButton  != null) closeButton.onClick.AddListener(OnCloseClicked);
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
            : $"다음 · 「{goal.Node.DisplayName}」        −{left:N0}";
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
        CopyAnchors(anchor, label, yOffset: -56f, height: 20f);
        nextGoalText = label.gameObject.AddComponent<TextMeshProUGUI>();
        nextGoalText.fontSize = 15f;
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
        dst.sizeDelta = new Vector2(src.sizeDelta.x, height);
        dst.anchoredPosition = src.anchoredPosition + new Vector2(0f, yOffset);
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

        if (state.Unlocked)                  SetActionButton(false, "해금됨", "");
        else if (state.BlockedByRequirement) SetActionButton(false, "선행 조건 필요", node.ConditionLabel);
        else if (state.CanBuy)               SetActionButton(true,  $"{state.Cost:N0} ◆ 해금", "");
        else                                 SetActionButton(false, "정수 부족", $"{state.Cost - essence:N0} 모자람");
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

    private static void PlayClickSfx() => Managers.Sound?.PlayEffectAsync(SoundKey.Sfx.UiButton).Forget();

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

        // 되돌릴 수 없는 메타 진행이다 — 열렸는지 아닌지가 소리와 움직임으로도 남아야 한다.
        if (!MemoryAltarService.TryUnlock(node))
        {
            ShopUIStyle.PlaySfx("shop_reject");
            Refresh();
            return;
        }

        ShopUIStyle.PlaySfx("enhance_success");
        PunchActionButtonAsync().Forget();
        SaveAndRefreshAsync().Forget();
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
