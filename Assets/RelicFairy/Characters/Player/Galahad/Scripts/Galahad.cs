using UnityEngine;

/// <summary>
/// 유물 1 — 성배의 수호자 (갈라하드).
/// 스타일: 방어형 / 탱커-카운터.
///
/// 패시브 1개 + Q스킬 1개 (기획 원칙).
///   패시브: 빛의 반사 — 받는 피해 10% 경감 + 방어력 비례 반사 피해
///   Q스킬:  성스러운 방패 — 전방 방패 오브젝트 전개 (흡인 + 지속피해 + 전방 120° 피해감소)
/// </summary>
public class Galahad : PlayerController
{
    // ── Constants ─────────────────────────────────────────────
    private const float ShieldFrontalArcCos = 0.5f;  // cos(60°) — 120° 부채꼴 절반
    private const float ShieldFrontalReduce = 0.5f;  // 전방 피해 50% 감소

    // ── Private ───────────────────────────────────────────────
    private float _shieldEnd;

    // ── 초기화 ───────────────────────────────────────────────────────────────

    protected override void InitLayerFSMs()
    {
        RegisterDefaultFSMs();
    }

    protected override bool IsInAttackOrSkillState() =>
        base.IsInAttackOrSkillState()       ||
        actSM.CurrentId == ActState.Charge  ||
        actSM.CurrentId == ActState.HeavyAttack;

    protected override void InitPassives()
    {
        RegisterPassive(new GalahadLightReflectionPassive());

        // 빛의 반사 1단계: 받는 피해 10% 경감을 캐릭터 상시 보너스로 적용.
        // (반사 피해는 패시브 Apply 시점에 별도 처리)
        RuntimeStats.SetCharacterDefenseBonus(defenseMult: 1f, damageReduction: 0.10f);
    }

    protected override void RouteInputsToLayers()
    {
        DefaultRouteInputsToLayers();
    }

    // ── 캐릭터 고유 스킬 ─────────────────────────────────────────────────────

    public override ISkillRuntime CreateCharacterSkillRuntime(SkillType slot)
    {
        if (slot == SkillType.Q) return new HolyShieldSkillRuntime();
        return null;
    }

    public override float GetCharacterSkillCooldown(SkillType slot)
    {
        if (slot == SkillType.Q) return 18f;
        return 0f;
    }

    // ── 성스러운 방패 연동 ────────────────────────────────────────────────────

    /// <summary>HolyShieldSkillRuntime 가 호출. 방패 활성 종료 시각만 기록.
    /// 전방 방향은 매 프레임 transform.forward 로 직접 판정 (방패가 캐릭터에 자식으로 추종됨).</summary>
    public void SetHolyShieldActive(float duration, Vector3 forward)
    {
        _shieldEnd = Time.time + duration;
    }

    /// <summary>피격 시 공격 방향이 갈라하드 현재 전방 120° 부채꼴 안인지 판정.</summary>
    private bool IsShieldBlocking(GameObject attacker)
    {
        if (Time.time >= _shieldEnd || attacker == null) return false;

        Vector3 toAttacker = attacker.transform.position - transform.position;
        toAttacker.y = 0f;
        if (toAttacker.sqrMagnitude < 0.01f) return false;

        return Vector3.Dot(toAttacker.normalized, transform.forward) >= ShieldFrontalArcCos;
    }

    // ── 피격 오버라이드 ───────────────────────────────────────────────────────

    public override void TakeDamage(int dmg, GameObject attacker = null)
    {
        if (IsShieldBlocking(attacker))
            dmg = Mathf.Max(0, Mathf.RoundToInt(dmg * (1f - ShieldFrontalReduce)));

        base.TakeDamage(dmg, attacker);
    }
}
