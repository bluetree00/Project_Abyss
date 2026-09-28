using UnityEngine;

/// <summary>
/// 플레이어가 적에게서 받는 이동 계열 상태이상 — 슬로우 · 빙결 · 얼음 누적 · 넉백.
/// PlayerController가 소유하고 Update 앞부분에서 <see cref="Tick"/>한다.
///
/// 슬로우는 <b>자기 채널(<see cref="SlowScale"/>)</b>에만 쓴다. 예전엔 공격·스킬의 이동 잠금과 같은
/// <c>MoveScale</c> 하나를 덮어써서 ① 공격 중 슬로우가 끝나면 잠금이 풀려 공격하며 걸어 다니고,
/// ② 슬로우 중 공격이 끝나면 슬로우가 사라지고, ③ 공격 중 슬로우가 걸리면 그 배율로 움직일 수 있었다.
/// 이제 실제 이동 배율은 PlayerController가 두 채널을 곱해서 낸다.
/// </summary>
public sealed class PlayerStatusEffects
{
    // ── Constants ─────────────────────────────────────────────────
    public const float  IceStageWindowDuration = 3f;
    private const float IceStage1SoundVolume  = 0.3f;

    // ── Private ───────────────────────────────────────────────────
    private readonly PlayerController _owner;
    private readonly AudioClip        _freezeLoopSfx;

    private float _slowTimer;

    private float       _freezeTimer;
    private AudioSource _freezeLoopAudioSource;

    private int   _iceStage;       // 0=기본, 1=경고(화면이펙트 활성)
    private float _iceStageTimer;

    private float _knockbackTimer;

    // ── Properties ────────────────────────────────────────────────
    /// <summary>슬로우 배율(평소 1). 이동 잠금 채널과 곱해져 최종 이동 배율이 된다.</summary>
    public float SlowScale { get; private set; } = 1f;
    public bool  IsFrozen    => _freezeTimer > 0f;
    public bool  IsKnockback => _knockbackTimer > 0f;

    public PlayerStatusEffects(PlayerController owner, AudioClip freezeLoopSfx)
    {
        _owner         = owner;
        _freezeLoopSfx = freezeLoopSfx;
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>
    /// 매 프레임 상태이상 타이머를 진행한다. 빙결 중이면 true — 호출자는 그 프레임의 입력·행동을 멈춘다.
    /// (빙결이 이번 프레임에 끝나도 이번 프레임까지는 true — 기존 동작 그대로)
    /// </summary>
    public bool Tick(float dt, float unscaledDt)
    {
        _knockbackTimer = Mathf.Max(0f, _knockbackTimer - dt);
        if (_slowTimer > 0f)
        {
            _slowTimer = Mathf.Max(0f, _slowTimer - unscaledDt);
            if (_slowTimer <= 0f)
                SlowScale = 1f;
        }
        if (_iceStage == 1)
        {
            _iceStageTimer -= dt;
            if (_iceStageTimer <= 0f)
                _iceStage = 0;
        }
        if (_freezeTimer > 0f)
        {
            _freezeTimer = Mathf.Max(0f, _freezeTimer - unscaledDt);
            if (_freezeTimer <= 0f) StopFreezeLoopSfx();
            return true;
        }
        return false;
    }

    /// <summary>이동 속도를 scale 배율로 duration초 동안 감소시킨다. 종료 시 자동으로 1로 복구.</summary>
    public void ApplySlow(float scale, float duration)
    {
        SlowScale  = Mathf.Clamp01(scale);
        _slowTimer = Mathf.Max(_slowTimer, duration);
    }

    /// <summary>슬로우 상태를 즉시 해제하고 부착된 VFX를 제거한다.</summary>
    public void ClearSlow()
    {
        _slowTimer = 0f;
        SlowScale  = 1f;
        var fx = _owner.transform.Find("StatusEffect_Slow");
        if (fx != null) Object.Destroy(fx.gameObject);
        var screenFx = _owner.transform.Find("StatusEffectScreen_Slow");
        if (screenFx != null) Object.Destroy(screenFx.gameObject);
    }

    /// <summary>빙결: duration초 동안 이동·행동·입력을 완전히 차단한다. 연속 피격 시 남은 시간을 연장.</summary>
    public void ApplyFreeze(float duration)
    {
        _freezeTimer = Mathf.Max(_freezeTimer, duration);
        if (_freezeLoopAudioSource == null || !_freezeLoopAudioSource.isPlaying)
        {
            StopFreezeLoopSfx();
            _freezeLoopAudioSource = Managers.Sound?.PlayLoopingEffectAt(_freezeLoopSfx, _owner.transform.position);
        }
    }

    /// <summary>
    /// 얼음 공격 1회 처리. 반환값: 1=1단계(화면이펙트), 2=빙결 발동, 0=이미 빙결 중(연장만).
    /// </summary>
    public int AddIceStack(float fullDuration)
    {
        if (IsFrozen)
        {
            ApplyFreeze(fullDuration * 0.5f);
            return 2;
        }

        if (_iceStage == 1 && _iceStageTimer > 0f)
        {
            _iceStage      = 0;
            _iceStageTimer = 0f;
            ApplyFreeze(fullDuration * 0.5f);
            return 2;
        }

        _iceStage      = 1;
        _iceStageTimer = IceStageWindowDuration;
        Managers.Sound?.PlayEffect(_freezeLoopSfx, IceStage1SoundVolume);
        return 1;
    }

    public void StopFreezeLoopSfx()
    {
        if (_freezeLoopAudioSource == null) return;
        Managers.Sound?.StopLoopingEffect(_freezeLoopAudioSource);
        _freezeLoopAudioSource = null;
    }

    /// <summary>외부 힘(넉백)을 가하고 일정 시간 동안 수평 이동 잠금을 스킵한다.</summary>
    public void ApplyKnockback(Vector3 force, float duration)
    {
        _owner.Rigid?.AddForce(force, ForceMode.Impulse);
        _knockbackTimer = duration;
    }
}
