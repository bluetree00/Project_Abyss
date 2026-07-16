using UnityEngine;

/// <summary>
/// 랜슬롯 고유 스킬 — 심판의 일격. 광란(Frenzy) 상태 전용·광란당 1회(LancelotMadnessRelic 게이팅).
/// 전방 콘(±45°, 반경6)을 <b>연타로 몰아치다 마지막에 강력한 일격</b>으로 마무리한다.
/// 실제 판정은 LancelotMadnessRelic.PerformJudgmentStrike가 담당(스택/낙인 슬롯 소유).
///
/// ── 타이밍은 '이펙트'에 맞춘다(실측) ─────────────────────────────
///   • 이펙트 Effect_36_MadnessSlash : 본체 슬래시 <b>~1.75초</b> (잔광은 3.5초까지 남지만 그건 여운)
///   • 애니메이션 QSkill_01          : 클립 1.0초 × 상태 speed 2 = <b>실제 0.5초</b>
///
/// 타격은 이펙트가 도는 <b>내내 이어지다가 이펙트가 끝날 때 막타로 닫힌다</b>.
/// 앞쪽에 연타를 몰아넣고 뒤를 비우면 "때리는 건 벌써 끝났는데 이펙트만 남아 도는" 위화감이 난다.
///
/// 애니가 0.5초뿐이라 그냥 두면 중간에 마지막 포즈로 굳는다 → <b>연타 동안 베기 모션을 이어 붙이고</b>,
/// 막타 직전에 한 번 더 걸어 스윙 중간에 판정이 꽂히게 한다.
///
/// 매 타가 콘을 새로 질의하므로 도중에 들어온 적도 맞는다.
/// </summary>
public sealed class JudgmentStrikeRuntime : ISkillRuntime
{
    private const string AnimName  = "QSkill_01";
    private const float  AnimBlend = 0.08f;
    private const float  AnimPlayback = 0.5f;    // 실측 재생 길이 — 이 주기로 베기를 이어 붙인다

    // ── 이펙트에 타이밍을 맞춘다(실측) ─────────────────────────────
    // Effect_36_MadnessSlash 는 스폰 후 <b>1.5초 지점에 파티클 4개가 동시에 터지는 '강조 버스트'</b>가 있다
    // (PS0/1/2/4 : startDelay 1.5s). 이게 이 이펙트의 클라이맥스다.
    // 막타는 이 버스트에 얹혀야 한다 — 그보다 먼저 때리면 "때린 건 끝났는데 이펙트만 나중에 터지는" 꼴이 된다.
    // 첫 참격이 0.16초에 뜨므로 그 버스트는 0.16 + 1.5 = 약 1.66초부터 시작해 이후로 이어진다.
    private const float  SkillDuration = 2.75f;

    private const float  FirstHitTime = 0.16f;   // 첫 타(선딜)
    private const float  HitInterval  = 0.09f;   // 연타 간격 — 짧게 유지(타당 피해는 그만큼 잘게)
    private const int    FlurryHits   = 20;      // 0.16 ~ 1.87초. 막타를 늦춘 만큼 연타로 채워 빈 구간을 없앤다

    private const float  FinisherAnimAt = 2.10f; // 마무리 베기 모션
    private const float  FinisherHitAt  = 2.40f; // 막타 — 강조 버스트가 한창일 때 꽂는다

    private const int    HitCount = FlurryHits + 1;   // 연타 + 마무리

    private readonly LancelotMadnessRelic _relic;
    private float _elapsed;
    private int   _hitsDone;
    private int   _animsPlayed;
    private bool  _finisherAnimPlayed;

    public JudgmentStrikeRuntime(LancelotMadnessRelic relic) { _relic = relic; }

    /// <summary>i번째 타의 발생 시각. 연타는 등간격, 마지막(마무리)만 텀을 두고 뒤에 떨어진다.</summary>
    private static float HitTimeAt(int i)
        => i < FlurryHits ? FirstHitTime + HitInterval * i : FinisherHitAt;

    public void OnEnter(SkillExecutionContext ctx)
    {
        _elapsed = 0f; _hitsDone = 0; _animsPlayed = 1; _finisherAnimPlayed = false;
        ctx.RotateToMouse();
        ctx.SetMoveScale(0f);
        ctx.Animator?.CrossFade(AnimName, AnimBlend);
    }

    public void OnUpdate(SkillExecutionContext ctx)
    {
        _elapsed += Time.deltaTime;

        TickAnimation(ctx);

        // 프레임이 길어져도(스파이크·히트스톱) 타를 흘리지 않도록 밀린 만큼 while로 소화한다.
        while (_hitsDone < HitCount && _elapsed >= HitTimeAt(_hitsDone))
        {
            _relic?.PerformJudgmentStrike(ctx.PlayerTransform, _hitsDone, HitCount);
            _hitsDone++;
        }

        if (_elapsed >= SkillDuration) ctx.RequestEnd?.Invoke();
    }

    public void OnExit(SkillExecutionContext ctx)
    {
        // 스킬이 중간에 끊겨도(피격·사망) 남은 타는 버린다 — 종료 후 유령 판정 방지.
        _hitsDone = HitCount;
        ctx.SetMoveScale(1f);
    }

    // ── Private Methods ───────────────────────────────────────────
    /// <summary>0.5초짜리 베기를 이어 붙여 연타 내내 캐릭터가 계속 베게 한다. 막타 직전엔 한 번 더.</summary>
    private void TickAnimation(SkillExecutionContext ctx)
    {
        if (ctx.Animator == null) return;

        if (!_finisherAnimPlayed && _elapsed >= FinisherAnimAt)
        {
            _finisherAnimPlayed = true;
            ctx.Animator.CrossFade(AnimName, AnimBlend, 0, 0f);   // 처음부터 다시
            return;
        }

        if (!_finisherAnimPlayed && _elapsed >= _animsPlayed * AnimPlayback)
        {
            _animsPlayed++;
            ctx.Animator.CrossFade(AnimName, AnimBlend, 0, 0f);
        }
    }
}
