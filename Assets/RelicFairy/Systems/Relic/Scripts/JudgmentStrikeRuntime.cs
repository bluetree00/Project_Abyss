using UnityEngine;

/// <summary>
/// 랜슬롯 고유 스킬 — 심판의 일격. 광란(Frenzy) 상태 전용·광란당 1회(LancelotMadnessRelic 게이팅).
/// 전방 콘(±45°, 반경6)을 <b>연타로 몰아치다 마지막에 강력한 일격</b>으로 마무리한다.
/// 실제 판정은 LancelotMadnessRelic.PerformJudgmentStrike가 담당(스택/낙인 슬롯 소유).
///
/// ── 타이밍은 '이펙트'에 맞춘다(실측) ─────────────────────────────
///   • 이펙트 Effect_36_MadnessSlash : 본체 슬래시 <b>~1.75초</b> (잔광은 3.5초까지 남지만 그건 여운)
///
/// 타격은 이펙트가 도는 <b>내내 이어지다가 이펙트가 끝날 때 막타로 닫힌다</b>.
/// 앞쪽에 연타를 몰아넣고 뒤를 비우면 "때리는 건 벌써 끝났는데 이펙트만 남아 도는" 위화감이 난다.
///
/// ── 애니는 '시퀀스'다 ────────────────────────────────────────────
/// 베기 한 클립은 연타 창(1.7초)보다 짧아 그냥 두면 중간에 마지막 포즈로 굳는다.
/// 그래서 유물 데이터(<see cref="RelicClassSO"/>.qSkillClipSequence)가 준 참격 단계들을
/// <b>순서대로 이어 붙여</b> 창을 채우고, 막타 직전에 마지막 참격을 처음부터 다시 건다.
///
/// 이어 붙이는 시점은 상수 주기가 아니라 <b>현재 클립이 실제로 끝났는지</b>(normalizedTime)로 판단한다.
/// 유물·무기마다 클립 길이와 상태 speed가 달라, 상수로 잡으면 앞부분만 반복되며 끊긴다.
///
/// 매 타가 콘을 새로 질의하므로 도중에 들어온 적도 맞는다.
/// </summary>
public sealed class JudgmentStrikeRuntime : ISkillRuntime
{
    private const float  AnimBlend  = 0.08f;
    private const float  ReplayAt   = 0.94f;   // 현재 참격이 이만큼 진행되면 다음 참격으로 넘긴다
    private const float  MinStepGap = 0.05f;   // CrossFade가 애니메이터에 반영되기 전 중복 전환 방지

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
    private bool  _finisherAnimPlayed;

    // ── 애니 시퀀스 상태 ──
    private RelicClassSO _relicClass;   // Q 상태 이름의 출처(없으면 기본 상태 1개)
    private int   _stepCount;
    private int   _stepIndex;
    private float _lastStepAt;

    public JudgmentStrikeRuntime(LancelotMadnessRelic relic) { _relic = relic; }

    /// <summary>i번째 타의 발생 시각. 연타는 등간격, 마지막(마무리)만 텀을 두고 뒤에 떨어진다.</summary>
    private static float HitTimeAt(int i)
        => i < FlurryHits ? FirstHitTime + HitInterval * i : FinisherHitAt;

    public void OnEnter(SkillExecutionContext ctx)
    {
        _elapsed = 0f; _hitsDone = 0; _finisherAnimPlayed = false;

        // Q 모션의 주인은 무기가 아니라 유물이다 — 상태 이름을 유물 데이터에서 읽는다.
        _relicClass = ctx.Controller != null ? ctx.Controller.RelicClass : null;
        _stepCount  = _relicClass != null ? _relicClass.QSkillStepCount : 1;
        _stepIndex  = 0;

        ctx.RotateToMouse();
        ctx.SetMoveScale(0f);
        PlayStep(ctx, 0);
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
    /// <summary>현재 참격이 끝나면 다음 참격으로 넘겨 연타 내내 캐릭터가 계속 베게 한다. 막타 직전엔 마지막 참격을 한 번 더.</summary>
    private void TickAnimation(SkillExecutionContext ctx)
    {
        var anim = ctx.Animator;
        if (anim == null) return;

        if (!_finisherAnimPlayed && _elapsed >= FinisherAnimAt)
        {
            _finisherAnimPlayed = true;
            PlayStep(ctx, _stepCount - 1);   // 마무리 = 시퀀스의 마지막 참격을 처음부터
            return;
        }
        if (_finisherAnimPlayed) return;

        // CrossFade 직후 몇 프레임은 아직 이전 상태가 현재 상태로 보고된다 — 그때 또 넘기면 첫 참격이 겹쳐 튄다.
        if (_elapsed - _lastStepAt < MinStepGap || anim.IsInTransition(0)) return;

        // 클립 길이·상태 speed는 유물/무기마다 다르므로 상수 주기 대신 '이 참격이 끝났는가'로 판단한다.
        var state = anim.GetCurrentAnimatorStateInfo(0);
        if (!state.IsName(StateName(_stepIndex))) return;   // 피격 등으로 끊겼으면 애니에 관여하지 않는다
        if (state.normalizedTime < ReplayAt) return;

        PlayStep(ctx, _stepIndex + 1);
    }

    /// <summary>시퀀스 index번째 참격을 처음부터 재생. 시퀀스 끝에 닿으면 앞으로 돌아 계속 몰아친다.</summary>
    private void PlayStep(SkillExecutionContext ctx, int index)
    {
        _stepIndex  = _stepCount > 0 ? ((index % _stepCount) + _stepCount) % _stepCount : 0;
        _lastStepAt = _elapsed;
        ctx.Animator?.CrossFade(StateName(_stepIndex), AnimBlend, 0, 0f);
    }

    private string StateName(int step)
        => _relicClass != null ? _relicClass.QSkillStateAt(step) : RelicClassSO.DefaultQSkillState;
}
