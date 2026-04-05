using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// ForestGuardian 보스 전용 블랙보드.
/// 폼(Form) 상태, 패턴 카운트, 폼 전환 로직을 담당한다.
/// </summary>
public class ForestGuardianBlackboard
{
    public enum BossForm { Liche, Spider, Thorn }

    public BossForm CurrentForm = BossForm.Liche;
    public int PatternCountSinceFormChange = 0;
    public bool FormChangePending = false;

    // 폼 전환 가중치 테이블 [현재폼] → [(다음폼, 가중치)]
    // Liche(0): Spider 50%, Thorn 50%
    // Spider(1): Liche 30%, Thorn 70%
    // Thorn(2): Spider 70%, Liche 30%
    private static readonly (BossForm next, float weight)[][] TransitionTable =
    {
        new[] { (BossForm.Spider, 0.5f), (BossForm.Thorn,  0.5f) },
        new[] { (BossForm.Liche,  0.3f), (BossForm.Thorn,  0.7f) },
        new[] { (BossForm.Spider, 0.7f), (BossForm.Liche,  0.3f) },
    };

    /// <summary>현재 폼 기반 가중치 랜덤으로 다음 폼을 결정한다.</summary>
    public BossForm RollNextForm()
    {
        var table = TransitionTable[(int)CurrentForm];
        float total = 0f;
        foreach (var (_, w) in table) total += w;
        float roll = Random.Range(0f, total);
        float acc = 0f;
        foreach (var (next, w) in table)
        {
            acc += w;
            if (roll <= acc) return next;
        }
        return table[table.Length - 1].next;
    }

    /// <summary>패턴 1회 실행 시 호출. HP 80% 이하이고 2패턴 이상이면 폼 전환 플래그 세팅.</summary>
    public void OnPatternExecuted(float bossHpRatio)
    {
        PatternCountSinceFormChange++;
        if (PatternCountSinceFormChange >= 2 && bossHpRatio <= 0.8f)
            FormChangePending = true;
    }

    /// <summary>폼 전환 완료 시 호출.</summary>
    public void OnFormChanged(BossForm next)
    {
        CurrentForm = next;
        PatternCountSinceFormChange = 0;
        FormChangePending = false;
    }

    /// <summary>보스 풀 재사용(OnEnable) 시 초기화.</summary>
    public void Reset()
    {
        CurrentForm = BossForm.Liche;
        PatternCountSinceFormChange = 0;
        FormChangePending = false;
    }
}
}
