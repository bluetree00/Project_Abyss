using System;
using System.Threading;
using Cysharp.Threading.Tasks;
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

    /// <summary>
    /// 기대한 참격 상태가 아닐 때 이만큼 지나면 <b>포기하지 않고 다시 건다</b>.
    /// 예전엔 상태가 한 번 어긋나면 매 프레임 return이라 연타 구간 내내 애니가 멈췄고,
    /// 막타만 PlayStep을 직접 불러 "마지막 하나만 재생되는" 증상이 났다.
    /// </summary>
    private const float  AnimRecoverAfter = 0.35f;

    /// <summary>
    /// 시전 중 피해 감소. 광기 40스택이 이미 '받는 피해 +30%'를 올려둔 상태라,
    /// 이 값은 강화가 아니라 그 페널티를 원점으로 되돌리는 몫이다.
    /// </summary>
    private const float  StanceDefense = 0.30f;

    /// <summary>Q 전용 검 어드레서블 키. 없으면 장착 무기를 숨기기만 한다(맨손 참격 &gt; 활로 후려치기).</summary>
    private const string QSwordKey = "Relic/Lancelot/QSword";

    /// <summary>
    /// 마무리 강타 전용 상태. 연타(RelicQ_Slash*)와 <b>다른 모션</b>이라야 "한 방"으로 읽힌다.
    /// 유물 데이터의 단독 모션 상태(RelicQ_Main)를 쓴다 — 예전 QSkill_01은 무기 R 스킬이 덮어쓰는 공용 상태라
    /// 활을 들면 조준 대기 자세로, 대검을 들면 대검 R 모션으로 마무리가 나갔다.
    /// </summary>
    private string FinisherState => _relicClass != null ? _relicClass.QSkillMainState : RelicClassSO.DefaultQSkillState;

    // ── 마무리 연출 ──────────────────────────────────────────────
    /// <summary>마무리 클립이 이 지점(정규화 시간)에 닿으면 포즈를 멈춰 세운다 — 이펙트가 도는 동안 자세가 풀리지 않게.</summary>
    private const float HoldPoseAtNormalized = 0.96f;
    /// <summary>막타 순간 세계를 멈춘다(실시간 초). 플레이어 애니메이터도 Normal 갱신이라 함께 멈춰 '한 컷'이 된다.</summary>
    private const float TimeStopRealSeconds = 0.45f;
    private const float TimeStopScale       = 0.03f;
    private static readonly object TimeStopOwner = new object();

    // ── 이펙트에 타이밍을 맞춘다(실측) ─────────────────────────────
    // Effect_36_MadnessSlash 는 스폰 후 <b>1.5초 지점에 파티클 4개가 동시에 터지는 '강조 버스트'</b>가 있다
    // (PS0/1/2/4 : startDelay 1.5s). 이게 이 이펙트의 클라이맥스다.
    // 막타는 이 버스트에 얹혀야 한다 — 그보다 먼저 때리면 "때린 건 끝났는데 이펙트만 나중에 터지는" 꼴이 된다.
    // 첫 참격이 0.16초에 뜨므로 그 버스트는 0.16 + 1.5 = 약 1.66초부터 시작해 이후로 이어진다.
    // 마무리 뒤 포즈를 잡고 버티는 시간까지 포함한다 — 막타 이펙트 본체(2.5초)가 도는 동안 걷기로 풀리면 김이 샌다.
    private const float  SkillDuration = 3.6f;

    private const float  FirstHitTime = 0.16f;   // 첫 타(선딜)

    // ── 타수는 '읽히는가'가 정한다 ──────────────────────────────
    // 예전엔 20타 × 0.085초(초당 11.7타)였다. 사람이 두세 자리 숫자를 읽는 데 0.25초쯤 걸리므로
    // 초당 4개가 상한인데 12개가 쏟아졌다 — 어떤 표시 기법으로도 개별 틱이 읽히지 않는 간격이었다.
    // 10타 × 0.17초로 낮춰 타당 피해를 2배로 키우고(2.75% → 5.5%) 근사치라도 잡히게 한다.
    private const float  HitInterval  = 0.17f;
    private const int    FlurryHits   = 10;      // 0.16 ~ 1.69초. 막타(2.40초)까지 0.7초 여유

    private const float  FinisherAnimAt = 1.75f; // 마무리 베기 모션(RelicQ_Main = SPAttack01 1.45s → 막타가 45% 지점에 얹힌다)
    private const float  FinisherHitAt  = 2.40f; // 막타 — 강조 버스트가 한창일 때 꽂는다

    private const int    HitCount = FlurryHits + 1;   // 연타 + 마무리

    private readonly LancelotMadnessRelic _relic;

    // ── Q 전용 검 ──
    // Q는 유물 스킬이라 모션이 검 참격인데, 손에 들린 오브젝트는 장착 무기 그대로다.
    // 활을 든 채 검을 휘두르면 활로 후려치는 그림이 나온다 — 시전 동안만 바꿔 낀다.
    private GameObject _hiddenWeapon;   // 숨긴 장착 무기(복구 대상)
    private GameObject _qSword;         // 띄운 전용 검

    private float _elapsed;
    private int   _hitsDone;
    private bool  _finisherAnimPlayed;
    private bool  _holdingPose;

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
        _elapsed = 0f; _hitsDone = 0; _finisherAnimPlayed = false; _holdingPose = false;

        // Q 모션의 주인은 무기가 아니라 유물이다 — 상태 이름을 유물 데이터에서 읽는다.
        _relicClass = ctx.Controller != null ? ctx.Controller.RelicClass : null;
        _stepCount  = _relicClass != null ? _relicClass.QSkillStepCount : 1;
        _stepIndex  = 0;

        ctx.RotateToMouse();
        ctx.SetMoveScale(0f);

        // 2.75초를 제자리에서 버티는 스킬이라 스탠스가 없으면 연타 도중 날아가 끊긴다.
        // 면역은 '넘어짐'에만 걸리고 피해는 그대로 받는다.
        ctx.Controller?.BeginStance(this, StanceDefense);
        SwapToQSword(ctx);

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
            // 막타 — 세계를 한 컷 멈춘다. 연타 사이의 일반 히트스톱(우선순위 10)보다 위(SlowMotion 100)라 겹쳐도 이쪽이 이긴다.
            if (_hitsDone == HitCount) BeginTimeStop(ctx);

            // 연타 애니는 <b>타격에 맞춰</b> 넘긴다. 예전엔 클립이 끝나야(normalizedTime 0.94) 넘겨서
            // 1.5초에 참격이 서너 번뿐이라 "반복은 되는데 연타로 안 보이는" 상태였다.
            // 한 타 = 한 참격이면 0.17초마다 칼이 바뀌어 몰아치는 것으로 읽힌다.
            // 막타는 별도 모션이 따로 들어가므로 여기서 제외한다.
            if (!_finisherAnimPlayed && _hitsDone < FlurryHits) PlayStep(ctx, _stepIndex + 1);
        }

        if (_elapsed >= SkillDuration) ctx.RequestEnd?.Invoke();
    }

    public void OnExit(SkillExecutionContext ctx)
    {
        // 스킬이 중간에 끊겨도(피격·사망) 남은 타는 버린다 — 종료 후 유령 판정 방지.
        _hitsDone = HitCount;
        ctx.SetMoveScale(1f);

        // 중단·사망 경로도 이 함수를 지나므로 스탠스·무기·포즈 홀드·시간 정지가 새지 않는다.
        ctx.Controller?.EndStance(this);
        RestoreWeapon();
        if (_holdingPose && ctx.Animator != null) ctx.Animator.speed = 1f;
        _holdingPose = false;
        TimeScaleArbiter.Release(TimeStopOwner);
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

            // 마무리는 <b>연타와 다른 모션</b>이어야 한 방임이 읽힌다.
            // 예전엔 시퀀스의 마지막 참격을 한 번 더 걸어서, 앞선 연타와 구분이 안 됐다.
            anim.CrossFade(FinisherState, AnimBlend, 0, 0f);
            _lastStepAt = _elapsed;
            return;
        }
        if (_finisherAnimPlayed)
        {
            // 마무리 클립이 끝자락에 닿으면 자세를 그대로 세운다. 스킬 종료(OnExit)에서 되돌린다.
            if (!_holdingPose && !anim.IsInTransition(0))
            {
                var fin = anim.GetCurrentAnimatorStateInfo(0);
                if (fin.IsName(FinisherState) && fin.normalizedTime >= HoldPoseAtNormalized)
                {
                    anim.speed    = 0f;
                    _holdingPose  = true;
                }
            }
            return;
        }

        // 연타 참격 전환은 타격 시점(OnUpdate)이 소유한다. 여기서는 <b>복구만</b> 한다 —
        // 피격 등으로 다른 상태가 끼어들어 참격이 끊겼을 때 되살리는 역할이다.
        if (_elapsed - _lastStepAt < AnimRecoverAfter || anim.IsInTransition(0)) return;

        var state = anim.GetCurrentAnimatorStateInfo(0);
        if (!state.IsName(StateName(_stepIndex))) PlayStep(ctx, _stepIndex);
    }

    /// <summary>막타 순간의 시간 정지. 실시간으로 잰 뒤 풀며, 스킬이 먼저 끊기면 OnExit이 푼다.</summary>
    private void BeginTimeStop(SkillExecutionContext ctx)
    {
        TimeScaleArbiter.Acquire(TimeStopOwner, TimeStopScale, TimeScaleArbiter.Priority.SlowMotion);
        var ct = ctx.Controller != null ? ctx.Controller.GetCancellationTokenOnDestroy() : CancellationToken.None;
        ReleaseTimeStopAsync(ct).Forget();
    }

    private static async UniTaskVoid ReleaseTimeStopAsync(CancellationToken ct)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(TimeStopRealSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            TimeScaleArbiter.Release(TimeStopOwner);
        }
    }

    /// <summary>시퀀스 index번째 참격을 처음부터 재생. 시퀀스 끝에 닿으면 앞으로 돌아 계속 몰아친다.</summary>
    private void PlayStep(SkillExecutionContext ctx, int index)
    {
        _stepIndex  = _stepCount > 0 ? ((index % _stepCount) + _stepCount) % _stepCount : 0;
        _lastStepAt = _elapsed;
        ctx.Animator?.CrossFade(StateName(_stepIndex), AnimBlend, 0, 0f);
    }

    /// <summary>장착 무기를 숨기고 전용 검을 오른손 소켓에 붙인다. 에셋이 없으면 숨기기까지만 한다.</summary>
    private void SwapToQSword(SkillExecutionContext ctx)
    {
        var ctrl = ctx.Controller;
        if (ctrl == null) return;

        var equipped = ctrl.WeaponManager?.CurrentWeaponInstance;
        if (equipped != null && equipped.activeSelf)
        {
            equipped.SetActive(false);
            _hiddenWeapon = equipped;
        }

        // 검 참격 모션은 오른손 기준이라 WeaponMount에 붙인다(활은 왼손이라 여기가 아니다).
        var socket = ctrl.handTransform;
        if (socket == null) return;

        SpawnQSwordAsync(socket).Forget();
    }

    /// <summary>
    /// 전용 검 스폰. 로드가 끝났을 때 스킬이 이미 끝났으면 즉시 버린다 —
    /// 안 그러면 검이 손에 남아 다음 전투 내내 따라다닌다.
    /// </summary>
    private async UniTaskVoid SpawnQSwordAsync(Transform socket)
    {
        GameObject go;
        try
        {
            // 키가 없으면 매니저가 경고만 남기고 null을 준다(예외·에러 로그 없음).
            // 전용 검은 아직 수급 전이라 '없는 게 정상'인 경로다.
            go = await Managers.AddressableManager.InstantiateAsync(QSwordKey, socket);
        }
        catch (System.OperationCanceledException) { return; }

        if (go == null) return;   // 미수급 — 무기를 숨긴 것만으로도 "활로 후려치기"는 사라진다

        // 스킬이 이미 끝났거나 무기가 복구된 뒤라면 방금 만든 검은 쓸 데가 없다.
        if (_hiddenWeapon == null && _qSword == null)
        {
            Managers.AddressableManager?.ReleaseInstance(go);
            return;
        }

        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        _qSword = go;
    }

    /// <summary>전용 검 회수 + 장착 무기 복구. 중단·사망 경로에서도 반드시 지나야 한다.</summary>
    private void RestoreWeapon()
    {
        if (_qSword != null)
        {
            Managers.AddressableManager?.ReleaseInstance(_qSword);
            _qSword = null;
        }

        if (_hiddenWeapon != null)
        {
            _hiddenWeapon.SetActive(true);
            _hiddenWeapon = null;
        }
    }

    private string StateName(int step)
        => _relicClass != null ? _relicClass.QSkillStateAt(step) : RelicClassSO.DefaultQSkillState;
}
