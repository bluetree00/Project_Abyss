using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 제한 시간이 있는 퀘스트(TimeLimit&gt;0)의 카운트다운을 관리하고, 만료 시 QuestManager.FailQuest로 실패 처리한다.
/// 등록 시 데드라인 설정, 완료/취소/실패 시 해제. 트래커가 남은 시간 표시를 위해 TryGetRemaining을 조회한다.
/// @UIRoot에 배치(전역). unscaled time 기준.
/// </summary>
public sealed class QuestTimerService : MonoBehaviour
{
    public static QuestTimerService Instance { get; private set; }

    private readonly Dictionary<Quest, float> _deadlines    = new Dictionary<Quest, float>();
    private readonly List<Quest>              _expiredBuffer = new List<Quest>();
    private QuestManager _quest;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        _quest = Managers.Quest;
        if (_quest != null)
        {
            _quest.onQuestRegistered += HandleRegistered;
            _quest.onQuestCompleted  += HandleEnded;
            _quest.onQuestCanceled   += HandleEnded;
            _quest.onQuestFailed     += HandleEnded;
        }
    }

    private void OnDisable()
    {
        if (_quest != null)
        {
            _quest.onQuestRegistered -= HandleRegistered;
            _quest.onQuestCompleted  -= HandleEnded;
            _quest.onQuestCanceled   -= HandleEnded;
            _quest.onQuestFailed     -= HandleEnded;
            _quest = null;
        }
        _deadlines.Clear();
    }

    private void Update()
    {
        if (_deadlines.Count == 0) return;

        float now = Time.unscaledTime;
        _expiredBuffer.Clear();
        foreach (var kv in _deadlines)
            if (now >= kv.Value) _expiredBuffer.Add(kv.Key);

        for (int i = 0; i < _expiredBuffer.Count; i++)
        {
            var q = _expiredBuffer[i];
            _deadlines.Remove(q);
            _quest?.FailQuest(q);   // onQuestFailed → 트래커 실패 연출
        }
    }

    /// <summary>남은 시간(초)을 반환. 제한시간 퀘스트가 아니면 false.</summary>
    public bool TryGetRemaining(Quest quest, out float remaining)
    {
        if (quest != null && _deadlines.TryGetValue(quest, out float dl))
        {
            remaining = Mathf.Max(0f, dl - Time.unscaledTime);
            return true;
        }
        remaining = 0f;
        return false;
    }

    private void HandleRegistered(Quest quest)
    {
        if (quest != null && quest.TimeLimit > 0f)
            _deadlines[quest] = Time.unscaledTime + quest.TimeLimit;
    }

    private void HandleEnded(Quest quest)
    {
        if (quest != null) _deadlines.Remove(quest);
    }
}
