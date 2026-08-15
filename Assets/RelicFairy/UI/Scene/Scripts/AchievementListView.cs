using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 기억의 제단 「업적」 탭. 달성은 런 중에 일어나고 <b>수령은 여기서</b> 한다.
///
/// <para><b>3단 정렬 — 수령 가능 → 진행 중 → 받음.</b> 열자마자 할 일이 최상단에 있다.
/// 「진행 중」은 <b>목표에 가까운 순</b>으로 세운다 — 완성이 가까울수록 동기가 커지므로,
/// 정의 순서대로 두면 가장 강한 동기가 목록 아래에 묻힌다.</para>
///
/// <para><b>「받음」은 접어 둔다.</b> 성취는 목록에 남아야 하지만, 늘 펼쳐져 있으면
/// 할 일을 가리는 잡음이 된다. 개수만 보여주고 눌러서 편다.</para>
/// </summary>
public class AchievementListView : MonoBehaviour
{
    // ── 직렬화 필드 ──────────────────────────────────────────────────────
    [Header("구간 머리")]
    [Tooltip("「수령 가능」 머리 + [모두 받기] 를 담은 줄. 0건이면 <b>줄 자체를</b> 끈다 — " +
             "글자만 숨기면 빈 46px가 남아 목록이 아래로 밀린다.")]
    [SerializeField] private GameObject claimableHeaderRow;
    [SerializeField] private TMP_Text claimableHeader;
    [SerializeField] private GameObject progressHeaderRow;
    [SerializeField] private TMP_Text progressHeader;
    [SerializeField] private GameObject claimedHeaderRow;
    [SerializeField] private Button   claimedHeaderButton;
    [SerializeField] private TMP_Text claimedHeader;

    [Header("모두 받기")]
    [SerializeField] private Button   claimAllButton;
    [SerializeField] private TMP_Text claimAllLabel;

    [Header("구간별 행 컨테이너")]
    [SerializeField] private Transform          claimableRows;
    [SerializeField] private Transform          progressRows;
    [SerializeField] private Transform          claimedRows;
    [SerializeField] private AchievementRowView rowTemplate;

    [Header("빈 상태")]
    [SerializeField] private TMP_Text emptyText;

    // ── 상수 ─────────────────────────────────────────────────────────────
    /// <summary>[모두 받기] 연쇄 간격. 한 프레임에 끝내면 "우르르 쏟아지는" 순간이 사라진다.</summary>
    private const int ClaimStaggerMs = 150;

    // ── 비공개 필드 ──────────────────────────────────────────────────────
    private readonly List<AchievementRowView> _claimable = new();
    private readonly List<AchievementRowView> _progress  = new();
    private readonly List<AchievementRowView> _claimed   = new();
    private bool _claimedExpanded;
    private bool _busy;
    private CancellationTokenSource _cts;

    /// <summary>수령이 일어나 정수가 변했다 — 패널이 헤더를 다시 그려야 한다.</summary>
    public event Action OnClaimed;

    /// <summary>조회 전용(로비·ESC 책자). 수령 버튼이 동작하지 않는다.</summary>
    public bool ReadOnly { get; set; }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    private void Awake()
    {
        if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);

        if (claimAllButton != null)
            claimAllButton.onClick.AddListener(() => ClaimAllAsync().Forget());

        if (claimedHeaderButton != null)
            claimedHeaderButton.onClick.AddListener(() => { _claimedExpanded = !_claimedExpanded; Refresh(); });
    }

    private void OnDisable()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _busy = false;
    }

    // ── Public Methods ───────────────────────────────────────────────────

    public void Refresh()
    {
        var mgr = Managers.Quest;
        if (mgr == null || !mgr.IsInitialized) { ShowEmpty("업적 데이터를 불러오지 못했다."); return; }

        var claimable = new List<Quest>();
        var running   = new List<Quest>();
        var claimed   = new List<Quest>();

        foreach (var a in mgr.ActiveAchievements)
        {
            if (a.IsCompltable) claimable.Add(a);
            else                running.Add(a);
        }
        foreach (var a in mgr.CompletedAchievements) claimed.Add(a);

        // 목표에 가까운 것이 위 — 근접도가 곧 동기다.
        running.Sort((x, y) => ProgressOf(y).CompareTo(ProgressOf(x)));

        if (claimable.Count + running.Count + claimed.Count == 0)
        {
            ShowEmpty("아직 업적이 없다.");
            return;
        }
        if (emptyText) emptyText.gameObject.SetActive(false);

        RefreshHeaders(claimable.Count, running.Count, claimed.Count);

        FillSection(_claimable, claimableRows, claimable, isClaimable: true,  isClaimed: false);
        FillSection(_progress,  progressRows,  running,   isClaimable: false, isClaimed: false);
        FillSection(_claimed,   claimedRows,
                    _claimedExpanded ? claimed : new List<Quest>(), isClaimable: false, isClaimed: true);
    }

    /// <summary>구간 하나를 채운다. 행은 재사용하고 남는 것은 끈다 — 여닫을 때마다 Instantiate 하면 GC가 튄다.</summary>
    private void FillSection(List<AchievementRowView> pool, Transform parent,
                             List<Quest> quests, bool isClaimable, bool isClaimed)
    {
        if (parent == null || rowTemplate == null) return;

        for (int i = 0; i < quests.Count; i++)
        {
            while (pool.Count <= i)
            {
                var created = Instantiate(rowTemplate, parent);
                created.gameObject.SetActive(true);
                pool.Add(created);
            }

            var q = quests[i];
            var row = pool[i];
            row.gameObject.SetActive(true);
            row.Refresh(BuildState(q, isClaimable, isClaimed));
            row.SetOnClaim(isClaimable && !ReadOnly ? () => ClaimOneAsync(q).Forget() : null);
        }

        for (int i = quests.Count; i < pool.Count; i++)
            pool[i].gameObject.SetActive(false);
    }

    // ── Private Methods ──────────────────────────────────────────────────

    private void RefreshHeaders(int claimableCount, int runningCount, int claimedCount)
    {
        // 빈 구간은 <b>머리째</b> 접는다. 글자만 숨기면 그 줄의 높이가 남아 목록이 통째로 밀린다.
        if (claimableHeaderRow) claimableHeaderRow.SetActive(claimableCount > 0);
        if (claimableHeader)    claimableHeader.text = $"수령 가능  {claimableCount}";

        if (progressHeaderRow)  progressHeaderRow.SetActive(runningCount > 0);
        if (progressHeader)     progressHeader.text = $"진행 중  {runningCount}";

        if (claimedHeaderRow)   claimedHeaderRow.SetActive(claimedCount > 0);
        if (claimedHeader)      claimedHeader.text  = $"받음  {claimedCount}   {(_claimedExpanded ? "▲" : "▼")}";

        bool showClaimAll = claimableCount > 0 && !ReadOnly;
        if (claimAllButton) claimAllButton.gameObject.SetActive(showClaimAll);
        if (claimAllLabel)  claimAllLabel.text = $"모두 받기 {claimableCount}";
    }


    private static AchievementRowState BuildState(Quest quest, bool claimable, bool claimed)
    {
        return new AchievementRowState
        {
            Quest         = quest,
            DisplayName   = quest.DisplayName,
            ConditionText = BuildConditionText(quest, claimable || claimed),
            Reward        = EssenceOf(quest),
            Progress01    = claimable || claimed ? 1f : ProgressOf(quest),
            Claimable     = claimable,
            Claimed       = claimed,
        };
    }

    /// <summary>조건 문구에 <b>진척 숫자</b>를 붙인다 — 얼마나 남았는지 안 보이면 목표가 되지 않는다.</summary>
    private static string BuildConditionText(Quest quest, bool done)
    {
        string desc = quest.Description;
        if (done) return desc;

        var group = quest.CurrentTaskGroup;
        if (group == null || group.Tasks.Count == 0) return desc;

        int cur = 0, need = 0;
        foreach (var t in group.Tasks) { cur += t.CurrentSuccess; need += t.NeedSuccessToComplete; }

        return need > 0 ? $"{desc}   {Mathf.Min(cur, need)} / {need}" : desc;
    }

    private static float ProgressOf(Quest quest)
    {
        var group = quest.CurrentTaskGroup;
        if (group == null || group.Tasks.Count == 0) return 0f;

        int cur = 0, need = 0;
        foreach (var t in group.Tasks) { cur += t.CurrentSuccess; need += t.NeedSuccessToComplete; }
        return need > 0 ? Mathf.Clamp01((float)cur / need) : 0f;
    }

    private static int EssenceOf(Quest quest)
    {
        int sum = 0;
        var rewards = quest.Rewards;
        if (rewards == null) return 0;

        foreach (var r in rewards)
            if (r is EssenceReward e) sum += e.EssenceAmount;
        return sum;
    }

    private void ShowEmpty(string message)
    {
        if (emptyText)
        {
            emptyText.gameObject.SetActive(true);
            emptyText.text = message;
        }
        foreach (var row in _claimable) row.gameObject.SetActive(false);
        foreach (var row in _progress)  row.gameObject.SetActive(false);
        foreach (var row in _claimed)   row.gameObject.SetActive(false);
        if (claimAllButton) claimAllButton.gameObject.SetActive(false);
    }

    // ── Event Handlers ───────────────────────────────────────────────────

    private async UniTaskVoid ClaimOneAsync(Quest quest)
    {
        if (_busy || ReadOnly || quest == null || !quest.IsCompltable) return;
        _busy = true;

        quest.Complete();          // 보상 지급은 Complete 안에서 일어난다
        OnClaimed?.Invoke();
        Refresh();

        await SaveAsync();
        _busy = false;
    }

    /// <summary>
    /// 수령 가능한 것을 <b>0.15초 간격으로 연쇄</b> 수령한다.
    /// <c>QuestManager.CompleteWaitingAchievements()</c>를 쓰지 않는 이유는 그쪽이 한 프레임에 끝나
    /// 이 시스템의 최대 즐거움 지점(우르르 쏟아짐)이 사라지기 때문이다.
    /// </summary>
    private async UniTaskVoid ClaimAllAsync()
    {
        if (_busy || ReadOnly) return;
        _busy = true;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        var ct = _cts.Token;

        try
        {
            var mgr = Managers.Quest;
            if (mgr != null)
            {
                // 스냅샷을 뜬 뒤 돈다 — 수령이 목록을 바꾸므로 순회 중 수정이 된다.
                var pending = new List<Quest>();
                foreach (var a in mgr.ActiveAchievements)
                    if (a.IsCompltable) pending.Add(a);

                foreach (var q in pending)
                {
                    if (ct.IsCancellationRequested) break;
                    if (!q.IsCompltable) continue;

                    q.Complete();
                    OnClaimed?.Invoke();
                    Refresh();

                    await UniTask.Delay(ClaimStaggerMs, ignoreTimeScale: true, cancellationToken: ct);
                }
            }

            await SaveAsync();
        }
        catch (OperationCanceledException) { }
        finally { _busy = false; }
    }

    private static async UniTask SaveAsync()
    {
        try
        {
            if (BackendGameData.Instance != null)
                await BackendGameData.Instance.SaveAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogWarning($"[Achievement] 저장 실패: {e.Message}");
        }
    }
}
