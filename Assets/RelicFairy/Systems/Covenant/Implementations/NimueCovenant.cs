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

    public override string CovenantId => CovenantFactory.Nimue;

    // ── 표시 데이터 ──────────────────────────────────────
    public override string DisplayName         => "니무에의 서약";
    public override string LoreText            => "호수의 여인이 멀린에게 전수한 보호 계약";
    public override string BasicDescription    => "피격 시 피해의 40%를 흡수하는 보호막 생성 (3초)";
    public override string EnhancedDescription => "흡수율 70%, 지속 4초";
    public override string EvolvedDescription  => "보호막 만료 시 흡수량을 주변 적에게 폭발로 반사";

    // ── 런타임 상태 ──────────────────────────────────────
    private float _shieldAbsorb;
    private float _shieldTimer;
    private float _accumulatedAbsorb;

    private float AbsorbRatio    => V(V_ABSORB_RATIO,    0.40f);
    private float ShieldDuration => V(V_SHIELD_DURATION, 3f);
    private bool  IsEvolved      => Stage == CovenantStage.Evolved;

    // ── 피해 파이프라인 ──────────────────────────────────
    public override void ModifyIncomingDamage(ref float damage, CombatContext ctx)
    {
        // TODO: 보호막 흡수 로직 구현
        // 보호막이 활성화된 경우 damage * AbsorbRatio 만큼 차감, _shieldAbsorb에 누적
    }

    // ── 이벤트 ──────────────────────────────────────────
    public override void OnTakeDamage(float damage)
    {
        // TODO: 보호막 생성 트리거 — 피격 시 ShieldDuration 타이머 시작
    }

    public override void Tick(float deltaTime)
    {
        if (_shieldTimer <= 0f) return;
        _shieldTimer -= deltaTime;

        if (_shieldTimer <= 0f)
        {
            // TODO: 보호막 만료 처리
            // Evolved: _accumulatedAbsorb 만큼 주변 범위 폭발 피해
        }
    }
}
