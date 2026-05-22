using UnityEngine;

/// <summary>
/// 헤카테의 서약 — 세 갈래 길의 여신과 맺은 다중 계약
///
/// [Basic]    스킬 사용 시 3가지 효과 중 무작위 1개 추가 발동
/// [Enhanced] 2개 동시 발동
/// [Evolved]  3가지 전부 동시 발동, 대신 스킬 쿨타임 +30%
/// </summary>
public sealed class HecateCovenant : CovenantBase
{
    private const int V_FIRE_COUNT       = 0;
    private const int V_COOLDOWN_PENALTY = 1;

    private const int EffectCount = 3;

    public override string CovenantId => CovenantFactory.Hecate;

    private int   FireCount       => VI(V_FIRE_COUNT,       1);
    private float CooldownPenalty => V(V_COOLDOWN_PENALTY,  0f);
    private bool  IsEvolved       => Stage == CovenantStage.Evolved;

    // ── 메커닉 수정 ──────────────────────────────────────
    public override void ModifySkillEffect(SkillType skill, ref SkillEffectContext ctx)
    {
        // TODO: ctx에 쿨타임 수정 필드 추가 필요 시 SkillEffectContext 확장
    }

    public override bool OverrideSkillCost(SkillType skill, ref SkillCostContext ctx)
    {
        if (!IsEvolved || CooldownPenalty <= 0f) return false;

        // TODO: 쿨타임 +CooldownPenalty 오버라이드
        return false;
    }

    // ── 이벤트 ──────────────────────────────────────────
    public override void OnSkillUse(SkillType skill)
    {
        int count = IsEvolved ? EffectCount : FireCount;

        var indices = GetRandomIndices(count);
        foreach (int idx in indices)
            TriggerEffect(idx);
    }

    // ── 내부 ────────────────────────────────────────────
    private int[] GetRandomIndices(int count)
    {
        // TODO: Fisher-Yates 셔플로 중복 없는 인덱스 반환
        return new int[count];
    }

    private void TriggerEffect(int idx)
    {
        switch (idx)
        {
            case 0: /* TODO: 소형 폭발 */ break;
            case 1: /* TODO: HP 5% 회복 */ break;
            case 2: /* TODO: 이동속도 2초 강화 */ break;
        }
    }
}
