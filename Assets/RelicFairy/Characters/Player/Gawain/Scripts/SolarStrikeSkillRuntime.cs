using UnityEngine;

/// <summary>
/// 가웨인 Q스킬 — 태양의 강타 런타임 (로그 스텁).
///
/// 컨셉: 전방 내리찍기. 강화 구간 시 피해+100%, 범위+80%.
/// 실제 피해/VFX는 추후 구현 예정.
/// </summary>
public class SolarStrikeSkillRuntime : ISkillRuntime
{
    private const string AnimName    = "QSkill_SolarStrike";
    private const float AnimDuration = 1.1f;

    private readonly SolarTimer _solarTimer;
    private float _elapsed;
    private bool  _hitLogged;
    private bool  _empowered;

    public SolarStrikeSkillRuntime(SolarTimer solarTimer)
    {
        _solarTimer = solarTimer;
    }

    public void OnEnter(SkillExecutionContext ctx)
    {
        _elapsed   = 0f;
        _hitLogged = false;
        _empowered = _solarTimer?.IsEmpowered ?? false;

        ctx.Animator?.CrossFade(AnimName, 0.1f);

        Debug.Log($"[SolarStrike] Q스킬 발동 — empowered={_empowered}, phase={_solarTimer?.CurrentPhase}, timer={_solarTimer?.PhaseTimer:F1}s");
    }

    public void OnUpdate(SkillExecutionContext ctx)
    {
        _elapsed += Time.deltaTime;

        if (!_hitLogged && _elapsed >= 0.5f)
        {
            _hitLogged = true;
            Debug.Log($"[SolarStrike] 타격 판정 시점 — empowered={_empowered}");
        }

        if (_elapsed >= AnimDuration)
            ctx.RequestEnd?.Invoke();
    }

    public void OnExit(SkillExecutionContext ctx) { }
}
