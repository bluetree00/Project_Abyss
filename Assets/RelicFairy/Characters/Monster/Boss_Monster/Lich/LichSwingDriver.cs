using System;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>낫 휘두름 동작 — 클립별 방향이 다르다(판정·이펙트 모양을 여기에 맞춘다).</summary>
public enum LichSwing
{
    RightToLeft,   // ScytheCombo1 (Anim_Fly_Attack_01) — 수평, 오른쪽 → 왼쪽
    LeftToRight,   // ScytheCombo2 (Anim_Fly_Attack_02) — 수평, 왼쪽 → 오른쪽
    Overhead,      // ScytheCombo3 (Anim_Fly_Attack_03) — 머리 위에서 앞으로 내려찍기
}

/// <summary>시전 동작 — 손을 들어 모았다가(충전 자세) 내뻗는(방출) 클립. 박자 상수는 <see cref="LichSwingDriver"/>의 표.</summary>
public enum LichCast
{
    MagicBolt,
    ArcaneOrb,
    ElementalBarrage,
    DeathRayStart,
    ScytheThrow,
    Phase2Entry,
}

/// <summary>
/// 낫 휘두름을 판정 순간에 맞춘다(09-19 사용자 지시 — 「휘두르는 모션이 제대로 느껴지게, 클립 속도를 특정 타이밍 조절」).
/// 클립에 애니메이션 이벤트가 없어 접촉 프레임(무기 각속도 최고점)을 상수로 두고,
/// 판정까지 남은 시간에 접촉이 오도록 클립을 틀고 속도를 나눈다:
/// 느린 준비(늘어진 당김) → 접촉 직전 빠른 베기 → 접촉 순간 멈칫 → 보통 속도로 회복.
/// 다른 동작(휘청·전환)이 끼어들면 스스로 손을 떼고 속도를 1로 돌린다.
/// 시전 박자(<see cref="PlayBeat"/>) — 큰 마법도 같은 방식: 충전 자세까지 들어 올려 <b>그 자세로 버티다가</b>
/// 판정 순간에 맞춰 빠르게 내뻗고, 방출 순간 멈칫(09-19 사용자 지시 — 「애니메이션 속도를 특정 부분 조절해 연출을 살려」).
/// </summary>
public sealed class LichSwingDriver
{
    // ── Constants ─────────────────────────────────────────────
    private const float StrikeClip   = 0.15f;   // 접촉 직전 이만큼(클립 초)은 빠르게 돈다
    private const float StrikeSpeed  = 1.8f;
    private const float HoldSpeed    = 0.04f;   // 접촉 순간 멈칫(리치 몸만)
    private const float MinWindSpeed = 0.4f;
    private const float MaxWindSpeed = 1.4f;
    private const float FadeSeconds  = 0.08f;
    private const float ChargeSpeed  = 0.06f;   // 충전 자세로 버티는 동안(아주 느리게 — 완전 정지는 굳어 보인다)
    private const float MaxBeatWind  = 2.2f;

    private enum Stage { Idle, Delay, Windup, Charge, Strike, Hold }

    // ── Private ───────────────────────────────────────────────
    private readonly Animator _animator;
    private Stage  _stage;
    private string _stateName;
    private int    _stateHash;
    private float  _contact;     // 클립 속 접촉 시각(초)
    private float  _clipTime;    // 지금 클립 시각(추정)
    private float  _windSpeed;
    private float  _startClip;
    private float  _delay;
    private float  _holdLeft;
    private float  _holdSeconds;
    private bool   _beat;          // 시전 박자(충전 자세에서 버틴다)
    private float  _chargeClip;
    private float  _chargeLeft;
    private float  _chargeSpeed = ChargeSpeed;
    private float  _strikeSpeed = StrikeSpeed;

    // ── Properties ────────────────────────────────────────────
    public bool IsActive => _stage != Stage.Idle;

    /// <summary>빠른 베기 구간이 시작됐다(접촉 약 0.08초 전) — 휙 소리 자리.</summary>
    public event Action StrikeStarted;

    public LichSwingDriver(Animator animator) => _animator = animator;

    // ── Public Methods ────────────────────────────────────────
    /// <summary>
    /// <paramref name="contactIn"/>초 뒤(게임 시간)에 <paramref name="swing"/>의 접촉 프레임이 오게 한다.
    /// 시간이 넉넉하면 준비를 늘이고(최소 속도 아래로는 늦게 시작), 모자라면 클립 중간부터 튼다.
    /// </summary>
    public void Play(LichSwing swing, float contactIn, float holdSeconds = 0.07f)
    {
        if (_animator == null) return;
        Cancel();

        (_stateName, _contact) = Clip(swing);
        _stateHash   = Animator.StringToHash(_stateName);
        _holdSeconds = Mathf.Max(0f, holdSeconds);
        _startClip   = 0f;
        _delay       = 0f;
        _beat        = false;
        _strikeSpeed = StrikeSpeed;

        float strikeReal = StrikeClip / StrikeSpeed;
        float windClip   = _contact - StrikeClip;
        float windReal   = contactIn - strikeReal;

        if (windReal <= 0.01f)
        {
            // 준비할 틈이 없다 — 베기 구간 안에서 시작한다.
            _windSpeed = StrikeSpeed;
            _startClip = Mathf.Max(0f, _contact - Mathf.Max(0f, contactIn) * StrikeSpeed);
        }
        else
        {
            _windSpeed = windClip / windReal;
            if (_windSpeed > MaxWindSpeed)
            {
                _windSpeed = MaxWindSpeed;
                _startClip = windClip - windReal * MaxWindSpeed;   // 앞부분을 건너뛴다
            }
            else if (_windSpeed < MinWindSpeed)
            {
                _windSpeed = MinWindSpeed;
                _delay     = windReal - windClip / MinWindSpeed;   // 지금 자세를 조금 더 유지
            }
        }

        if (_delay > 0f) _stage = Stage.Delay;
        else             Begin();
    }

    /// <summary>
    /// 시전 박자 — <paramref name="releaseIn"/>초 뒤(게임 시간)에 <paramref name="cast"/>의 방출 프레임이 오게 한다.
    /// 보통 속도로 충전 자세까지 → 남은 시간 동안 그 자세로 버틴다 → 방출까지 <paramref name="snapSpeed"/>배로 내뻗는다 → 멈칫.
    /// 시간이 모자라면 준비를 빠르게(최대 <see cref="MaxBeatWind"/>배), 그래도 모자라면 충전 자세에서 바로 시작한다.
    /// </summary>
    public void PlayBeat(LichCast cast, float releaseIn, float snapSpeed = 2f, float holdSeconds = 0.1f)
    {
        if (_animator == null) return;
        Cancel();

        (_stateName, _chargeClip, _contact) = Beat(cast);
        _stateHash   = Animator.StringToHash(_stateName);
        _holdSeconds = Mathf.Max(0f, holdSeconds);
        _strikeSpeed = Mathf.Max(0.5f, snapSpeed);
        _beat        = true;
        _startClip   = 0f;
        _delay       = 0f;
        _windSpeed   = 1f;
        _chargeLeft  = 0f;

        float snapReal = Mathf.Max(0f, _contact - _chargeClip) / _strikeSpeed;
        float budget   = releaseIn - snapReal;   // 준비 + 버티기에 쓸 수 있는 시간
        if (budget >= _chargeClip)
        {
            _chargeLeft = budget - _chargeClip;
        }
        else if (budget > 0.05f)
        {
            _windSpeed = Mathf.Min(MaxBeatWind, _chargeClip / budget);
            _startClip = Mathf.Max(0f, _chargeClip - budget * _windSpeed);
        }
        else
        {
            _startClip = _chargeClip;
        }
        Begin();
    }

    /// <summary>손을 뗀다 — 애니메이터 속도를 1로.</summary>
    public void Cancel()
    {
        if (_stage == Stage.Idle) return;
        _stage = Stage.Idle;
        if (_animator != null) _animator.speed = 1f;
    }

    /// <summary>리치 Update에서 매 프레임.</summary>
    public void Tick(float dt)
    {
        if (_stage == Stage.Idle) return;

        if (_stage == Stage.Delay)
        {
            _delay -= dt;
            if (_delay <= 0f) Begin();
            return;
        }

        // 다른 동작이 끼어들었으면(휘청 · 전환 컷신) 그쪽 속도를 건드리지 않는다.
        if (!IsOnState())
        {
            Cancel();
            return;
        }

        switch (_stage)
        {
            case Stage.Windup:
                _clipTime += dt * _windSpeed;
                if (_beat)
                {
                    if (_clipTime >= _chargeClip) EnterCharge();
                }
                else if (_clipTime >= _contact - StrikeClip) EnterStrike();
                break;

            case Stage.Charge:
                _clipTime   += dt * _chargeSpeed;
                _chargeLeft -= dt;
                if (_chargeLeft <= 0f) EnterStrike();
                break;

            case Stage.Strike:
                _clipTime += dt * _strikeSpeed;
                if (_clipTime >= _contact)
                {
                    _stage           = Stage.Hold;
                    _holdLeft        = _holdSeconds;
                    _animator.speed  = HoldSpeed;
                }
                break;

            case Stage.Hold:
                _holdLeft -= dt;
                if (_holdLeft <= 0f) Cancel();   // 회복은 보통 속도
                break;
        }
    }

    // ── Private Methods ───────────────────────────────────────
    private static (string state, float contact) Clip(LichSwing swing) => swing switch
    {
        LichSwing.RightToLeft => ("ScytheCombo1", 0.50f),
        LichSwing.LeftToRight => ("ScytheCombo2", 0.77f),
        _                     => ("ScytheCombo3", 0.38f),
    };

    /// <summary>
    /// 시전 클립 박자 — (상태, 충전 자세 시각, 방출 시각) 클립 초.
    /// LichClipBeatProbe(RelicFairy/Boss/Lich/Measure Cast Clip Beats) 09-19 실측(왼손 높이 · 속도 곡선):
    /// ArcaneOrb · ElementalBarrage — 1.3초까지 손을 낮게 모았다가 1.4~1.6초에 머리 위로 치켜든다(2.08 m, 초속 12 m) → 모은 자세에서 버티고 치켜드는 순간이 방출.
    /// MagicBolt — 1.27초에 가장 높이(1.73 m) 들었다가 1.8초까지 내리민다 → 든 자세에서 버티고 내리미는 끝이 방출.
    /// DeathRayStart — 0.47초에 두 팔을 가장 높이(1.86 m) → 그 자세로 버티고 1.0초(내리긋는 한가운데)가 방출.
    /// ScytheThrow — 0.40초 뒤로 젖힘 → 0.48초 던짐(초속 41.8 m).
    /// Phase2Entry — 0.1~0.55초에 오른팔을 머리 위(2.68 m)로 치켜든다 → 치켜든 자세에서 버틴다(전환 컷신의 힘 모으기).
    /// </summary>
    private static (string state, float charge, float release) Beat(LichCast cast) => cast switch
    {
        LichCast.MagicBolt        => ("MagicBolt",        1.27f, 1.80f),
        LichCast.ArcaneOrb        => ("ArcaneOrb",        1.30f, 1.62f),
        LichCast.ElementalBarrage => ("ElementalBarrage", 1.30f, 1.70f),
        LichCast.DeathRayStart    => ("DeathRayStart",    0.47f, 1.00f),
        LichCast.Phase2Entry      => ("Phase2Entry",      0.55f, 0.60f),
        _                         => ("ScytheThrow",      0.40f, 0.48f),
    };

    private void Begin()
    {
        _animator.CrossFadeInFixedTime(_stateName, FadeSeconds, 0, _startClip);
        _clipTime = _startClip;
        if (_beat)
        {
            if (_clipTime < _chargeClip)
            {
                _stage          = Stage.Windup;
                _animator.speed = _windSpeed;
            }
            else if (_chargeLeft > 0f) EnterCharge();
            else                       EnterStrike();
            return;
        }
        if (_clipTime >= _contact - StrikeClip)
        {
            EnterStrike();
        }
        else
        {
            _stage          = Stage.Windup;
            _animator.speed = _windSpeed;
        }
    }

    private void EnterCharge()
    {
        // 버티는 동안 클립이 방출까지 남은 길이의 40% 이상 흐르지 않게(긴 채널링이면 더 느리게).
        _chargeSpeed    = Mathf.Min(ChargeSpeed, 0.4f * Mathf.Max(0.01f, _contact - _clipTime) / Mathf.Max(0.01f, _chargeLeft));
        _stage          = Stage.Charge;
        _animator.speed = _chargeSpeed;
    }

    private void EnterStrike()
    {
        _stage          = Stage.Strike;
        _animator.speed = _strikeSpeed;
        if (!_beat) StrikeStarted?.Invoke();   // 휙 소리는 낫만
    }

    private bool IsOnState()
    {
        if (_animator.GetCurrentAnimatorStateInfo(0).shortNameHash == _stateHash) return true;
        return _animator.IsInTransition(0) && _animator.GetNextAnimatorStateInfo(0).shortNameHash == _stateHash;
    }
}
}
