using System;
using UnityEngine;

public enum SkillType { Q, E, R }

/// <summary>
/// Q/E/R 스킬 쿨다운 추적기.
/// PlayerController.Update()에서 Tick()을 호출해야 한다.
/// </summary>
public class SkillCooldownTracker
{
    private readonly float[] _remaining = new float[3];  // [Q, E, R]
    private readonly float[] _total     = new float[3];

    /// <summary>스킬 쿨다운 상태 변경 시 발생. (SkillType, remaining, total)</summary>
    public event Action<SkillType, float, float> OnCooldownChanged;

    // ── 외부 API ─────────────────────────────────────────────────────────────

    /// <summary>매 프레임 호출. deltaTime은 unscaledDeltaTime 권장.</summary>
    public void Tick(float deltaTime)
    {
        for (int i = 0; i < _remaining.Length; i++)
        {
            if (_remaining[i] <= 0f) continue;

            _remaining[i] = Mathf.Max(0f, _remaining[i] - deltaTime);
            OnCooldownChanged?.Invoke((SkillType)i, _remaining[i], _total[i]);
        }
    }

    /// <summary>해당 스킬이 사용 가능한지 반환.</summary>
    public bool IsReady(SkillType skill) => _remaining[(int)skill] <= 0f;

    /// <summary>
    /// 쿨다운 시작. duration이 0 이하면 아무것도 하지 않는다.
    /// cooldownReduction: 0.1 = 10% 감소 (SkillCooldownReduction StatType 값)
    /// </summary>
    public void StartCooldown(SkillType skill, float duration, float cooldownReduction = 0f)
    {
        if (duration <= 0f) return;

        float reduction = Mathf.Clamp01(cooldownReduction);
        float actual    = duration * (1f - reduction);

        int idx = (int)skill;
        _remaining[idx] = actual;
        _total[idx]     = actual;
        OnCooldownChanged?.Invoke(skill, actual, actual);
    }

    /// <summary>특정 스킬 쿨다운 즉시 초기화 (아이템/버프 등으로 리셋할 때).</summary>
    public void ResetCooldown(SkillType skill)
    {
        int idx = (int)skill;
        _remaining[idx] = 0f;
        _total[idx]     = 0f;
        OnCooldownChanged?.Invoke(skill, 0f, 0f);
    }

    /// <summary>현재 남은 쿨다운 시간 (0이면 사용 가능).</summary>
    public float GetRemaining(SkillType skill) => _remaining[(int)skill];

    /// <summary>쿨다운 진행률 0~1 (0 = 완료, 1 = 방금 시작).</summary>
    public float GetProgress(SkillType skill)
    {
        float total = _total[(int)skill];
        if (total <= 0f) return 0f;
        return _remaining[(int)skill] / total;
    }
}
