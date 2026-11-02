using UnityEngine;

/// <summary>
/// 데미지 정보 패킷. Pre 단계에서 ref로 전달하여 효과가 수정 가능.
/// </summary>
public struct DamagePacket
{
    public float BaseDamage;
    public float FinalDamage;
    public GameObject Attacker;
    public GameObject Target;
    public bool Negated;           // true면 데미지 0으로 처리
    public bool IsCrit;            // 크리티컬 발생 여부

    // 11-02 Q29 한 타 보너스 장부 — 출처별 (곱 − 1). 피해는 예전처럼 FinalDamage에 곱하고(총합 · 적중 효과 그대로),
    // 받는 쪽이 숫자만 출처별 「추가 피해」로 떼어 띄운다(DamageBonusSplit).
    public float BonusRune, BonusAugment, BonusLegendary, BonusItem;

    public DamagePacket(float damage, GameObject attacker = null, GameObject target = null)
    {
        BaseDamage = damage;
        FinalDamage = damage;
        Attacker = attacker;
        Target = target;
        Negated = false;
        IsCrit = false;
        BonusRune = BonusAugment = BonusLegendary = BonusItem = 0f;
    }

    /// <summary>장부에 적힌 보너스를 모두 곱한 배율(1 = 보너스 없음).</summary>
    public float BonusProduct => (1f + BonusRune) * (1f + BonusAugment) * (1f + BonusLegendary) * (1f + BonusItem);

    /// <summary>한 타 보너스를 곱하고 출처를 적는다 — 룬 · 증강 · 전설의 사전 보정은 FinalDamage에 직접 곱하지 말고 이것을 쓴다(11-02 Q29).</summary>
    public void AddBonus(BonusSource src, float frac)
    {
        FinalDamage *= 1f + frac;
        NoteBonus(src, frac);
    }

    /// <summary>다른 곳에서 이미 곱한 보너스의 출처만 적는다(④ 저스트가드처럼 FinalDamage를 읽은 뒤의 배율).</summary>
    public void NoteBonus(BonusSource src, float frac)
    {
        if (frac <= 0f) return;   // 깎는 배율은 주 피해 몫
        switch (src)
        {
            case BonusSource.Rune:      BonusRune      = Stack(BonusRune, frac);      break;
            case BonusSource.Augment:   BonusAugment   = Stack(BonusAugment, frac);   break;
            case BonusSource.Legendary: BonusLegendary = Stack(BonusLegendary, frac); break;
            default:                    BonusItem      = Stack(BonusItem, frac);      break;
        }
    }

    private static float Stack(float bonus, float frac) => (1f + bonus) * (1f + frac) - 1f;
}

/// <summary>
/// 데미지 적중 확정 후 결과. Post 단계에서 읽기 전용으로 전달.
/// </summary>
public struct DamageReport
{
    public float DamageDealt;
    public GameObject Attacker;
    public GameObject Target;
    public bool WasKill;
    public bool IsCrit;            // 치명타 여부 (룬 OnCrit 라우팅에 사용)
    public Vector3 HitPosition;
    public WeaponActionType ActionType;   // 공격 종류(스킬 특화 파츠가 심판의 일격 등 식별에 사용)
    public bool IsRanged;          // 원거리 명중(화살 · 볼트) — 사격 계열 발동이 센다
    public float ProcCoef;         // 발동 계수(10-14 A1) — 확률 · 상태 지속 · 포이즈에 곱한다. 0 = 옛 경로(1로 본다)
    public bool IsHeavy;           // 강타(발동 계수 ≥ 문턱 · 10-14 A2) — 강타 발동 룬이 센다
}
