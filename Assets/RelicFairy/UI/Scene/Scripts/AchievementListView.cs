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

    // ── 분류 필터 ────────────────────────────────────────────────────────
    //
    // 21개가 한 목록이면 「진행 중」만 14개가 쏟아져 3단 정렬만으로는 훑기 어렵다.
    // CSV의 category는 전부 「Record」라 데이터에 분류가 없으므로 <b>대상 키에서 유도</b>한다 —
    // 새 컬럼을 만들면 CSV·생성기·서버를 다 건드려야 하는데, 키만 보면 분류가 이미 결정돼 있다.
    public enum AchCategory { All, Reach, Combat, Build, Explore, Feat }

    private static readonly (AchCategory Cat, string Label)[] Categories =
    {
        (AchCategory.All,     "전체"),
        (AchCategory.Reach,   "도달"),
        (AchCategory.Combat,  "전투"),
        (AchCategory.Build,   "빌드"),
        (AchCategory.Explore, "탐색"),
        (AchCategory.Feat,    "기행"),
    };

    private AchCategory _filter = AchCategory.All;
    private readonly List<Image>    _chipBg    = new();
    private readonly List<TMP_Text> _chipLabel = new();
    private bool _chipsBuilt;

    private bool PassFilter(Quest q) => _filter == AchCategory.All || CategoryOf(q) == _filter;

    /// <summary>
    /// 목록 위 분류 칩. 프리팹에 자리가 없어도 코드가 만들어 붙인다 — 아트 배선 전에도 동작해야 한다.
    /// 「수령 가능」 머리 위에 놓아 3단 정렬을 가리지 않는다.
    /// </summary>
    private void EnsureChips()
    {
        if (_chipsBuilt) return;
        _chipsBuilt = true;

        var anchor = claimableHeaderRow != null ? claimableHeaderRow.transform.parent
                   : (claimableRows != null ? claimableRows.parent : null);
        if (anchor == null) return;

        var bar = new GameObject("ChipBar", typeof(RectTransform)).GetComponent<RectTransform>();
        bar.SetParent(anchor, false);
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot     = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, 36f);
        bar.anchoredPosition = new Vector2(0f, 4f);
        bar.SetAsFirstSibling();

        float x = 0f;
        for (int i = 0; i < Categories.Length; i++)
        {
            var (cat, label) = Categories[i];
            float w = i == 0 ? 96f : 76f;

            var chip = new GameObject($"Chip_{cat}", typeof(RectTransform)).GetComponent<RectTransform>();
            chip.SetParent(bar, false);
            chip.anchorMin = chip.anchorMax = new Vector2(0f, 0.5f);
            chip.pivot     = new Vector2(0f, 0.5f);
            chip.sizeDelta = new Vector2(w, 32f);   // 의뢰서 03: 분류 칩 높이 32
            chip.anchoredPosition = new Vector2(x, 0f);
            x += w + 8f;

            var img = chip.gameObject.AddComponent<Image>();
            img.color = AltarPalette.TabOff;
            _chipBg.Add(img);

            var txt = new GameObject("Label", typeof(RectTransform)).GetComponent<RectTransform>();
            txt.SetParent(chip, false);
            txt.anchorMin = Vector2.zero; txt.anchorMax = Vector2.one;
            txt.offsetMin = Vector2.zero; txt.offsetMax = Vector2.zero;
            var tmp = txt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 16f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            _chipLabel.Add(tmp);

            var btn = chip.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            var picked = cat;
            btn.onClick.AddListener(() => { _filter = picked; Refresh(); });
        }
    }

    /// <summary>칩 색과 개수를 갱신한다. 「전체」에만 총 개수를 붙여 목록 규모를 알린다.</summary>
    private void RefreshChips()
    {
        EnsureChips();
        if (_chipBg.Count == 0) return;

        var mgr = Managers.Quest;
        int total = 0;
        if (mgr != null)
        {
            total = mgr.ActiveAchievements.Count + mgr.CompletedAchievements.Count;
        }

        for (int i = 0; i < _chipBg.Count && i < Categories.Length; i++)
        {
            bool on = Categories[i].Cat == _filter;
            _chipBg[i].color    = on ? AltarPalette.TabOn : AltarPalette.TabOff;
            _chipLabel[i].color = on ? AltarPalette.Gold  : AltarPalette.TextDim;
            _chipLabel[i].text  = i == 0 ? $"{Categories[i].Label} {total}" : Categories[i].Label;
        }
    }

    private static readonly string[] ReachKeys   = { "maxChapter", "maxDepth", "clears" };
    private static readonly string[] CombatKeys  = { "kills", "eliteKills", "bossKills" };
    private static readonly string[] BuildKeys   = { "maxEnhance", "refineCount" };
    private static readonly string[] FeatKeys    = { "noPotionClear", "noSpecialClear", "flawlessChapter" };

    /// <summary>대상 키 → 분류. 어디에도 안 걸리면 「탐색」으로 떨어뜨려 목록에서 사라지지 않게 한다.</summary>
    private static AchCategory CategoryOf(Quest q)
    {
        if (HasAnyTarget(q, ReachKeys))  return AchCategory.Reach;
        if (HasAnyTarget(q, CombatKeys)) return AchCategory.Combat;
        if (HasAnyTarget(q, BuildKeys))  return AchCategory.Build;
        if (HasAnyTarget(q, FeatKeys))   return AchCategory.Feat;
        return AchCategory.Explore;   // roomClears · shopUses · 미상
    }

    private static bool HasAnyTarget(Quest q, string[] keys)
    {
        var groups = q?.TaskGroups;
        if (groups == null) return false;

        foreach (var g in groups)
            foreach (var t in g.Tasks)
            {
                if (t == null) continue;
                foreach (var k in keys)
                    if (t.ContainsTarget(k)) return true;
            }
        return false;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    private void Awake()
    {
        if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);

        if (claimAllButton != null)
        {
            claimAllButton.onClick.AddListener(() => ClaimAllAsync().Forget());

            // [모두 받기] 아트. 스킨이 없으면 기존 색 버튼 그대로 동작한다.
            var art = UISkin.Achievement?.claimAllButton;
            if (art != null && claimAllButton.TryGetComponent<Image>(out var img))
            {
                img.sprite = art;
                img.type   = Image.Type.Sliced;
                img.color  = Color.white;
            }
        }

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

    /// <summary>
    /// 외부(하단 행동 바)에서 [모두 받기]를 부르는 통로.
    /// 스태거 연출·저장·중복 실행 가드가 전부 이 뷰에 있으므로 로직을 복제하지 않고 위임한다.
    /// </summary>
    public void ClaimAll() => ClaimAllAsync().Forget();

    /// <summary>수령 시 받게 될 정수 합. 하단 버튼이 "얼마를 받는지"를 미리 말하는 데 쓴다.</summary>
    public int PendingEssence()
    {
        var mgr = Managers.Quest;
        if (mgr == null) return 0;

        int sum = 0;
        foreach (var a in mgr.ActiveAchievements)
            if (a.IsCompltable) sum += EssenceOf(a);
        return sum;
    }

    public void Refresh()
    {
        var mgr = Managers.Quest;
        if (mgr == null || !mgr.IsInitialized) { ShowEmpty("업적 데이터를 불러오지 못했다."); return; }

        var claimable = new List<Quest>();
        var running   = new List<Quest>();
        var claimed   = new List<Quest>();

        // 분류 필터는 3단 정렬 <b>안쪽</b>에 건다 — 정렬 구조는 그대로 두고 보이는 것만 줄인다.
        foreach (var a in mgr.ActiveAchievements)
        {
            if (!PassFilter(a)) continue;
            if (a.IsCompltable) claimable.Add(a);
            else                running.Add(a);
        }
        foreach (var a in mgr.CompletedAchievements)
            if (PassFilter(a)) claimed.Add(a);

        RefreshChips();

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
