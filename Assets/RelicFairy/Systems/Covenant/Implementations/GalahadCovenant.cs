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

    public override string CovenantId => CovenantFactory.Galahad;

    // ── 런타임 상태 ──────────────────────────────────────
    private float _blockCooldown;
    private float _holyBurstTimer;

    private float BlockCooldownMax => V(V_BLOCK_COOLDOWN,  10f);
    private float HolyBurstTime    => V(V_HOLY_BURST_TIME, 3f);
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
        if (!IsEvolved || _holyBurstTimer <= 0f) return;

        // TODO: 신성 폭발 추가 발동 (범위 피해 VFX + 데미지)
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
