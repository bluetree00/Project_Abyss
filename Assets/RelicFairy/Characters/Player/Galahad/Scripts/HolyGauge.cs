using System;
using UnityEngine;

/// <summary>
/// 갈라하드 고유 메커닉: 신성 게이지 (0~100).
///
/// 충전:
///   피격 시 +15  (GalahadFathersGuiltPassive에서 호출)
///   성배의 빛 사용 시 +20
///
/// 만충(100) 달성 시:
///   5초간 AttackMultiplier 2.5× + 피해감소 30% 적용 (무적 아님)
///   이후 BurstCooldown(30초) 동안 재충전 불가
/// </summary>
public class HolyGauge : MonoBehaviour
{
    // ── 상수 ─────────────────────────────────────────────────────────────────
    public const float MaxGauge        = 100f;
    public const float BurstDuration   = 5f;
    public const float BurstCooldown   = 30f;

    private const float BurstAttackMult = 2.5f;
    private const float BurstDamageReduction = 0.30f;

    // ── 이벤트 ───────────────────────────────────────────────────────────────
    /// <summary>게이지 값이 변경될 때마다 발생. (현재값 0~100)</summary>
    public event Action<float> OnGaugeChanged;
    /// <summary>만충 발동 시 발생.</summary>
    public event Action OnBurstActivated;
    /// <summary>만충 효과 종료 시 발생.</summary>
    public event Action OnBurstEnded;

    // ── 상태 ─────────────────────────────────────────────────────────────────
    public float Current      { get; private set; }
    public bool  IsBursting   { get; private set; }
    public bool  IsOnCooldown { get; private set; }
    public float BurstTimer   { get; private set; }
    public float CooldownTimer { get; private set; }

    /// <summary>게이지 30 이상이면 스킬 강화 조건 충족.</summary>
    public bool IsSkillEmpowered => Current >= 30f && !IsBursting && !IsOnCooldown;

    private PlayerRuntimeStats _stats;

    // ── 초기화 ───────────────────────────────────────────────────────────────

    public void Initialize(PlayerRuntimeStats stats)
    {
        _stats = stats;
        Current = 0f;
        IsBursting = false;
        IsOnCooldown = false;
    }

    // ── Unity Lifecycle ───────────────────────────────────────────────────────

    private void Update()
    {
        if (IsBursting)
        {
            BurstTimer -= Time.deltaTime;
            if (BurstTimer <= 0f)
                EndBurst();
        }
        else if (IsOnCooldown)
        {
            CooldownTimer -= Time.deltaTime;
            if (CooldownTimer <= 0f)
                IsOnCooldown = false;
        }
    }

    // ── 공개 API ─────────────────────────────────────────────────────────────

    /// <summary>게이지를 amount만큼 충전한다. 만충 조건이면 Burst 발동.</summary>
    public void AddGauge(float amount)
    {
        if (amount <= 0f || IsBursting || IsOnCooldown) return;

        Current = Mathf.Min(MaxGauge, Current + amount);
        OnGaugeChanged?.Invoke(Current);

        if (Current >= MaxGauge)
            ActivateBurst();
    }

    /// <summary>성배의 빛 사용 시 +20 충전. 강화 발동 여부도 함께 반환.</summary>
    public bool ConsumeForSkill()
    {
        bool empowered = IsSkillEmpowered;
        AddGauge(20f);
        return empowered;
    }

    // ── 내부 ─────────────────────────────────────────────────────────────────

    private void ActivateBurst()
    {
        Current = MaxGauge;
        IsBursting = true;
        BurstTimer = BurstDuration;

        _stats?.SetCharacterAttackMultiplier(BurstAttackMult, BurstAttackMult);
        _stats?.SetCharacterDefenseBonus(1f, BurstDamageReduction);

        OnBurstActivated?.Invoke();
    }

    private void EndBurst()
    {
        IsBursting = false;
        IsOnCooldown = true;
        CooldownTimer = BurstCooldown;
        Current = 0f;

        _stats?.SetCharacterAttackMultiplier(1f, 1f);
        _stats?.SetCharacterDefenseBonus(1f, 0f);

        OnGaugeChanged?.Invoke(Current);
        OnBurstEnded?.Invoke();
    }
}
