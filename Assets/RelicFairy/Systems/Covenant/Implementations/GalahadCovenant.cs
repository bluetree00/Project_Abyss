using UnityEngine;

/// <summary>
/// 갤러해드의 서약 — 성배 탐색의 순결 서약
///
/// [Basic]    10초마다 피해 1회 완전 무효
/// [Enhanced] 쿨타임 7초
/// [Evolved]  무효 발동 직후 3초간 모든 공격에 신성 폭발 부가 (범위 피해)
/// </summary>
public sealed class GalahadCovenant : CovenantBase
{
    private const int V_BLOCK_COOLDOWN  = 0;
    private const int V_HOLY_BURST_TIME = 1;
    private const int V_BURST_RADIUS    = 2;
    private const int V_BURST_MULT      = 3;

    public override string CovenantId => CovenantFactory.Galahad;

    // ── 런타임 상태 ──────────────────────────────────────
    private float _blockCooldown;
    private float _holyBurstTimer;
    private bool  _bursting; // DealAoe 재진입(폭발→피해→ModifyOutgoing) 가드

    private float BlockCooldownMax => V(V_BLOCK_COOLDOWN,  10f);
    private float HolyBurstTime    => V(V_HOLY_BURST_TIME, 3f);
    private float BurstRadius      => V(V_BURST_RADIUS,    3f);
    private float BurstMult        => V(V_BURST_MULT,      0.5f);
    private bool  IsEvolved        => Stage == CovenantStage.Evolved;
    private bool  CanBlock         => _blockCooldown <= 0f;

    // ── 피해 파이프라인 ──────────────────────────────────
    public override void ModifyIncomingDamage(ref float damage, CombatContext ctx)
    {
        if (!CanBlock) return;

        damage         = 0f;
        _blockCooldown = BlockCooldownMax;

        if (IsEvolved)
            _holyBurstTimer = HolyBurstTime;
    }

    public override void ModifyOutgoingDamage(ref float damage, CombatContext ctx)
    {
        if (!IsEvolved || _holyBurstTimer <= 0f || _bursting) return;

        // 신성 폭발: 명중 지점에 범위 피해 (재진입 가드로 폭발 피해가 다시 폭발을 부르지 않게)
        _bursting = true;
        Vector3 at = ctx.Target != null ? ctx.Target.transform.position : PlayerPos;
        DealAoe(at, BurstRadius, BurstMult, knockback: 0.2f);
        Vfx("VFX_FireExplosion", at); // 임시 VFX (전용 VFX_HolyBurst 대기)
        _bursting = false;
    }

    // ── 이벤트 ──────────────────────────────────────────
    public override void Tick(float deltaTime)
    {
        if (_blockCooldown > 0f)
            _blockCooldown -= deltaTime;

        if (_holyBurstTimer > 0f)
            _holyBurstTimer -= deltaTime;
    }
}
