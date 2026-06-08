using UnityEngine;

/// <summary>
/// 니무에의 서약 — 호수의 여인이 멀린에게 전수한 보호 계약
///
/// [Basic]   피격 시 피해의 40%를 흡수하는 보호막 생성 (3초)
/// [Enhanced] 흡수율 70%, 지속 4초
/// [Evolved]  보호막 만료 시 흡수한 피해량을 주변 적에게 폭발로 반사
/// </summary>
public sealed class NimueCovenant : CovenantBase
{
    private const int V_ABSORB_RATIO    = 0;
    private const int V_SHIELD_DURATION = 1;
    private const int V_EXPLODE_RADIUS  = 2;
    private const int V_EXPLODE_MULT    = 3;

    public override string CovenantId => CovenantFactory.Nimue;

    // ── 표시 데이터 ──────────────────────────────────────
    public override string DisplayName         => "니무에의 서약";
    public override string LoreText            => "호수의 여인이 멀린에게 전수한 보호 계약";
    public override string BasicDescription    => "피격 시 피해의 40%를 흡수하는 보호막 생성 (3초)";
    public override string EnhancedDescription => "흡수율 70%, 지속 4초";
    public override string EvolvedDescription  => "보호막 만료 시 흡수량을 주변 적에게 폭발로 반사";

    // ── 런타임 상태 ──────────────────────────────────────
    private float _shieldTimer;
    private float _accumulatedAbsorb;

    private float AbsorbRatio    => V(V_ABSORB_RATIO,    0.40f);
    private float ShieldDuration => V(V_SHIELD_DURATION, 3f);
    private float ExplodeRadius  => V(V_EXPLODE_RADIUS,  4f);
    private float ExplodeMult    => V(V_EXPLODE_MULT,    1f);
    private bool  IsEvolved      => Stage == CovenantStage.Evolved;

    // ── 피해 파이프라인 ──────────────────────────────────
    // 실드 인프라 부재 → 내부 상태로 근사: 보호막 활성 중 들어오는 피해를 AbsorbRatio만큼 차감·누적.
    public override void ModifyIncomingDamage(ref float damage, CombatContext ctx)
    {
        if (_shieldTimer <= 0f) return;
        float absorbed = damage * AbsorbRatio;
        damage             -= absorbed;
        _accumulatedAbsorb += absorbed;
    }

    // ── 이벤트 ──────────────────────────────────────────
    public override void OnTakeDamage(float damage)
    {
        // 피격 시 보호막 생성(이미 활성이면 유지). ModifyIncoming은 OnTakeDamage보다 먼저라 다음 피격부터 흡수.
        if (_shieldTimer > 0f) return;
        _shieldTimer       = ShieldDuration;
        _accumulatedAbsorb = 0f;
    }

    public override void Tick(float deltaTime)
    {
        if (_shieldTimer <= 0f) return;
        _shieldTimer -= deltaTime;

        // 만료: Evolved면 주변 폭발(Tick 내 호출이라 재진입 안전). 흡수 발생 시에만.
        if (_shieldTimer <= 0f && IsEvolved && _accumulatedAbsorb > 0f)
        {
            DealAoe(PlayerPos, ExplodeRadius, ExplodeMult, knockback: 0.3f);
            Vfx("VFX_FireExplosion", PlayerPos); // 임시 VFX (전용 자산 대기)
            _accumulatedAbsorb = 0f;
        }
    }
}
