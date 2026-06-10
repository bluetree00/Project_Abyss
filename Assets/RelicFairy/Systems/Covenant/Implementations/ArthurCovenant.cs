using System.Collections.Generic;

/// <summary>
/// 아서의 서약 — 왕국을 위한 불사의 서약
///
/// [Basic]    HP 30% 이하 시 공격력 +35%
/// [Enhanced] 공격력 +55%, 이동속도 +20%
/// [Evolved]  HP 30% 이하 구간에서 5초마다 방어력 무시 충격파 자동 발동
/// </summary>
public sealed class ArthurCovenant : CovenantBase
{
    private const int V_ATK_BONUS          = 0;
    private const int V_MOVE_SPEED_BONUS   = 1;
    private const int V_LOW_HP_THRESHOLD   = 2;
    private const int V_SHOCKWAVE_INTERVAL = 3;
    private const int V_SHOCKWAVE_RADIUS   = 4;
    private const int V_SHOCKWAVE_MULT     = 5;

    public override string CovenantId => CovenantFactory.Arthur;

    // ── 런타임 상태 ──────────────────────────────────────
    private float _shockwaveCooldown;

    private float AtkBonus          => V(V_ATK_BONUS,          35f);
    private float MoveSpeedBonus    => V(V_MOVE_SPEED_BONUS,   0f);
    private float LowHpThreshold    => V(V_LOW_HP_THRESHOLD,   0.30f);
    private float ShockwaveInterval => V(V_SHOCKWAVE_INTERVAL, 5f);
    private float ShockwaveRadius   => V(V_SHOCKWAVE_RADIUS,   4f);
    private float ShockwaveMult     => V(V_SHOCKWAVE_MULT,     2f); // 방어무시 근사 — 큰 배수(IDamageable에 방어무시 인자 없음)

    private bool IsLowHp => Ctx != null &&
        Ctx.RunState.Hp <= Ctx.RunState.MaxHp * LowHpThreshold;

    // ── 스탯 레이어 기여 ─────────────────────────────────
    public override IEnumerable<StatModifier> GetStatModifiers()
    {
        if (!IsLowHp) yield break;

        yield return new StatModifier(StatType.AttackPower, AtkBonus);

        if (MoveSpeedBonus > 0f)
            yield return new StatModifier(StatType.MoveSpeed, MoveSpeedBonus);
    }

    // ── 이벤트 ──────────────────────────────────────────
    public override void Tick(float deltaTime)
    {
        if (Stage < CovenantStage.Evolved || !IsLowHp) return;

        _shockwaveCooldown -= deltaTime;
        if (_shockwaveCooldown > 0f) return;

        _shockwaveCooldown = ShockwaveInterval;
        // 방어무시 충격파 — Tick 내 호출이라 DealAoe 재진입 안전
        DealAoe(PlayerPos, ShockwaveRadius, ShockwaveMult, knockback: 1f);
        Vfx("VFX_LightningStrike", PlayerPos); // 임시 VFX (전용 VFX_Shockwave 대기)
    }
}
