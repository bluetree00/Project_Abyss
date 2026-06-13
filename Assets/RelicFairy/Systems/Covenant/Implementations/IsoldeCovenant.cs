using UnityEngine;

/// <summary>
/// 이졸데의 서약 — 서로 다른 힘이 교차할 때 폭발 (전투 리듬형: 교차 리듬)
///
/// [선택]   스킬↔일반 공격 교차 N회 누적 시 다음 스킬이 광역 폭발 동반.
/// [강화]   교차 카운트 단축 + 광역 범위 증가.
/// [각성]   광역 폭발 2회 연속 발동.
///
/// 데이터 인덱스: [0]교차카운트 [1]범위증가비 [2]연속발동 [3]피해배율
/// ⚠️ P1 근사: 스킬 광역화를 스킬 사용 시 플레이어 중심 AoE로 구현. 실제 스킬 효과 변환은 P2.
/// </summary>
public sealed class IsoldeCovenant : CovenantBase
{
    private const int V_CROSS  = 0;
    private const int V_RANGE  = 1;
    private const int V_REPEAT = 2;
    private const int V_MULT   = 3;

    public override string CovenantId => CovenantFactory.Isolde;
    public override CovenantCategory Category => CovenantCategory.CombatRhythm;

    public override string DisplayName         => "이졸데의 서약";
    public override string LoreText            => "이졸데 — 서로 다른 힘이 교차할 때 폭발하는 서약을 전달했다";
    public override string BasicDescription    => "스킬↔일반 공격 교차 3회 누적 시 다음 스킬이 광역 폭발을 동반.";
    public override string EnhancedDescription => "교차 카운트 2로 단축. 광역 범위 +50% 확대.";
    public override string EvolvedDescription  => "광역 폭발이 2회 연속 발동.";

    private int  _cross;
    private bool _lastWasSkill;
    private bool _empowered;

    private int   CrossNeeded => Mathf.Max(1, VI(V_CROSS, 3));
    private float Range       => V(V_RANGE, 0f);
    private int   Repeat      => Mathf.Max(1, VI(V_REPEAT, 1));
    private float Mult        => V(V_MULT, 1f);

    public override void OnSkillUse(SkillType skill)
    {
        // 강화된 상태면 이번 스킬에 광역 폭발 동반
        if (_empowered)
        {
            _empowered = false;
            float radius = 3f * (1f + Range);
            for (int i = 0; i < Repeat; i++) DealAoe(PlayerPos, radius, Mult);
            Vfx("VFX_FireExplosion", PlayerPos);
        }

        // 교차 카운트: 직전이 일반공격이었으면 교차 성립
        if (!_lastWasSkill) _cross++;
        _lastWasSkill = true;
        if (_cross >= CrossNeeded) { _empowered = true; _cross = 0; }
    }

    public override void OnAttackHit(GameObject target, float dmg)
    {
        if (_lastWasSkill) _cross++;
        _lastWasSkill = false;
        if (_cross >= CrossNeeded) { _empowered = true; _cross = 0; }
    }

    public override void OnRoomEnter() { _cross = 0; _empowered = false; _lastWasSkill = false; }
}
