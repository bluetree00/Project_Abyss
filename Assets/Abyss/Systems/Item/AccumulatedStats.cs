/// <summary>
/// 아이템 패시브 효과를 합산한 결과.
/// PlayerRuntimeStats의 아이템 레이어에 직접 반영.
/// </summary>
public struct AccumulatedStats
{
    // ── 가산 (Flat) ─────────────────────────────────────────
    public int MeleeDamage;
    public int RangedDamage;
    public int Defense;
    public int MaxHP;
    public int Luck;

    // ── 배율/퍼센트 ─────────────────────────────────────────
    public float MoveSpeed;                   // m/s 가산
    public float AttackSpeed;                 // 초당 공격 가산
    public float SkillCooldownReduction;      // 0.1 = 10% 감소
    public float ActiveItemCooldownReduction;
    public float RollCooldown;                // 음수 = 감소
    public float RollDistance;                // m 가산
    public float RangedRange;                 // m 가산
    public float HealingReceived;             // 0.05 = +5%
    public float DebuffResistance;            // 0.1 = +10%
    public float AllDamageFlat;               // 모든 공격력 Flat 가산
    public float AllDamagePercent;            // 모든 공격력 % 가산
    public float AllStatsPercent;             // 모든 스탯 % 가산
    public float DamageReduction;             // 방패 등 피해 감소%
    public float AllElementBonus;             // 모든 원소 보너스%
    public float DebuffDuration;              // 디버프 지속시간 가산 (방)
    public float SpecialRoomChance;           // 특수방 등장 확률 가산
    public float ShopRoomChance;              // 상점방 등장 확률 가산
    public float HighGradeItemChance;         // 고등급 아이템 확률 가산
    public int ConsumableSlotBonus;           // 소모품 슬롯 보너스
    public float Lifesteal;                   // 흡혈 비율 (패시브, OnHit과 별도)
    public float GoldGainRate;                // 골드 획득 배율 (0.1 = +10%)
    public float SkillDamagePercent;          // 스킬 데미지 % 가산 (0.1 = +10%)
    public int ProjectilePierceBonus;         // 투사체 관통 +개수
    public int ProjectileCountBonus;          // 투사체 추가 개수 (멀티샷)

    /// <summary>모든 값을 0으로 초기화.</summary>
    public void Clear()
    {
        this = default;
    }
}
