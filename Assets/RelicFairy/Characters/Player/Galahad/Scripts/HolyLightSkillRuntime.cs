using UnityEngine;

/// <summary>
/// 갈라하드 Q스킬 — 성배의 빛 런타임 (로그 스텁).
///
/// 컨셉: 전방 범위 신성 폭발. 게이지 30+ 시 강화.
/// 실제 피해/VFX는 추후 구현 예정.
/// </summary>
public class HolyLightSkillRuntime : ISkillRuntime
{
    private const string AnimName    = "QSkill_HolyLight";
    private const float AnimDuration = 0.9f;

    private readonly HolyGauge _gauge;
    private float _elapsed;
    private bool  _hitLogged;
    private bool  _empowered;

    public HolyLightSkillRuntime(HolyGauge gauge)
    {
        _gauge = gauge;
    }

    public void OnEnter(SkillExecutionContext ctx)
    {
        _elapsed   = 0f;
        _hitLogged = false;
        _empowered = _gauge?.ConsumeForSkill() ?? false;

        ctx.Animator?.CrossFade(AnimName, 0.1f);

        Debug.Log($"[HolyLight] Q스킬 발동 — empowered={_empowered}, gauge={_gauge?.Current:F0}");
    }

    public void OnUpdate(SkillExecutionContext ctx)
    {
        _elapsed += Time.deltaTime;

        if (!_hitLogged && _elapsed >= 0.4f)
        {
            _hitLogged = true;
            Debug.Log($"[HolyLight] 타격 판정 시점 — empowered={_empowered}");
        }

        if (_elapsed >= AnimDuration)
            ctx.RequestEnd?.Invoke();
    }

    public void OnExit(SkillExecutionContext ctx) { }
}
