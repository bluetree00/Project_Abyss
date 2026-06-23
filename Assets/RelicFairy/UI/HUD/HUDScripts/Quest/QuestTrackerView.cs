using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public enum QuestRemoveKind { Instant, Clear, Fail }

/// <summary>
/// 진행 중 퀘스트 추적 위젯 View. 퀘스트별 행을 증분 관리하며, 추가/제거 연출을 op 큐로
/// 순차 처리한다(완료 연출 → 다음 퀘스트 등장이 겹치지 않게). 등장=페이드 인,
/// 완료=금색 강조 후 페이드 아웃, 실패=적색 강조 후 페이드 아웃. 행 위치는 즉시 스냅.
/// </summary>
public sealed class QuestTrackerView : MonoBehaviour
{
    private readonly struct Op
    {
        public readonly bool IsAdd;
        public readonly Quest Quest;
        public readonly QuestRemoveKind RemoveKind;
        public Op(bool isAdd, Quest quest, QuestRemoveKind removeKind)
        {
            IsAdd = isAdd; Quest = quest; RemoveKind = removeKind;
        }
    }

    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform entryRoot;
    [SerializeField] private QuestTrackerEntry entryTemplate;
    [SerializeField] private float rowHeight = 56f;
    [SerializeField] private float stepDelay = 0.18f;   // 연출 사이 "넘어가는" 간격

    private readonly List<QuestTrackerEntry> _active = new List<QuestTrackerEntry>();
    private readonly Stack<QuestTrackerEntry> _pool  = new Stack<QuestTrackerEntry>();
    private readonly Dictionary<Quest, QuestTrackerEntry> _byQuest = new Dictionary<Quest, QuestTrackerEntry>();
    private readonly Queue<Op> _ops = new Queue<Op>();
    private CancellationTokenSource _cts;
    private bool _processing;

    private void Awake()
    {
        if (entryTemplate != null)
            entryTemplate.gameObject.SetActive(false);
    }

    private void OnEnable() => _cts ??= new CancellationTokenSource();

    private void OnDisable()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _ops.Clear();
        _processing = false;
        ClearAll();
    }

    /// <summary>초기 채우기(애니메이션 없음).</summary>
    public void SetQuestsInstant(IReadOnlyList<Quest> quests)
    {
        _ops.Clear();
        ClearAll();

        int count = quests?.Count ?? 0;
        for (int i = 0; i < count; i++)
        {
            var e = AddInternal(quests[i]);
            e?.ShowInstant();
        }

        Reposition();
        UpdateGroupAlpha();
    }

    public void AddQuest(Quest quest)
    {
        if (quest == null || entryTemplate == null) return;
        EnqueueOp(new Op(true, quest, QuestRemoveKind.Instant));
    }

    public void RemoveQuest(Quest quest, QuestRemoveKind kind)
    {
        if (quest == null) return;
        EnqueueOp(new Op(false, quest, kind));
    }

    public void Clear() => ClearAll();

    // ── op 큐 처리 ─────────────────────────────────────────

    private void EnqueueOp(Op op)
    {
        _ops.Enqueue(op);
        if (!_processing)
        {
            EnsureCts();
            ProcessAsync(_cts.Token).Forget();
        }
    }

    private async UniTaskVoid ProcessAsync(CancellationToken ct)
    {
        _processing = true;
        try
        {
            while (_ops.Count > 0)
            {
                var op = _ops.Dequeue();
                if (op.IsAdd) await ProcessAddAsync(op.Quest, ct);
                else          await ProcessRemoveAsync(op.Quest, op.RemoveKind, ct);

                if (_ops.Count > 0 && stepDelay > 0f)
                    await UniTask.Delay((int)(stepDelay * 1000f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            }
        }
        catch (System.OperationCanceledException) { /* 비활성/파괴 시 정상 */ }
        finally
        {
            _processing = false;
        }
    }

    private async UniTask ProcessAddAsync(Quest quest, CancellationToken ct)
    {
        if (quest == null || _byQuest.ContainsKey(quest)) return;

        var entry = AddInternal(quest);
        if (entry == null) return;

        Reposition();
        UpdateGroupAlpha();
        await entry.FadeInAsync(ct);
    }

    private async UniTask ProcessRemoveAsync(Quest quest, QuestRemoveKind kind, CancellationToken ct)
    {
        if (quest == null || !_byQuest.TryGetValue(quest, out var entry)) return;

        switch (kind)
        {
            case QuestRemoveKind.Clear: await entry.PlayClearAsync(ct); break;
            case QuestRemoveKind.Fail:  await entry.PlayFailAsync(ct);  break;
        }

        FinishRemove(quest, entry);
    }

    // ── 내부 ──────────────────────────────────────────────

    private QuestTrackerEntry AddInternal(Quest quest)
    {
        if (quest == null || _byQuest.ContainsKey(quest)) return null;

        var entry = _pool.Count > 0 ? _pool.Pop() : Instantiate(entryTemplate, entryRoot);
        entry.transform.SetAsLastSibling();
        entry.gameObject.SetActive(true);
        entry.Bind(quest);

        _active.Add(entry);
        _byQuest[quest] = entry;
        return entry;
    }

    private void FinishRemove(Quest quest, QuestTrackerEntry entry)
    {
        if (entry == null) return;

        _byQuest.Remove(quest);
        _active.Remove(entry);
        entry.Unbind();
        entry.gameObject.SetActive(false);
        if (!_pool.Contains(entry))
            _pool.Push(entry);

        Reposition();
        UpdateGroupAlpha();
    }

    private void Reposition()
    {
        for (int i = 0; i < _active.Count; i++)
        {
            var rt = (RectTransform)_active[i].transform;
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, -i * rowHeight);
        }
    }

    private void UpdateGroupAlpha()
    {
        if (canvasGroup != null)
            canvasGroup.alpha = _active.Count > 0 ? 1f : 0f;
    }

    private void ClearAll()
    {
        for (int i = 0; i < _active.Count; i++)
        {
            var e = _active[i];
            e.Unbind();
            e.gameObject.SetActive(false);
            if (!_pool.Contains(e))
                _pool.Push(e);
        }
        _active.Clear();
        _byQuest.Clear();

        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
    }

    private void EnsureCts() => _cts ??= new CancellationTokenSource();
}
