namespace RelicFairy
{
    /// <summary>
    /// 시너지 시스템이 활성화한 행동 역학 플래그 모음.
    /// PlayerRuntimeStats.SynergyMechanics에 보관, 전투·스킬·이동 시스템이 읽는다.
    /// </summary>
    public class SynergyMechanicsState
    {
        // ── ATK 존 ──────────────────────────────────
        /// <summary>ATK 각성: 3연타마다 강타 자동 발동 (카운트 트리거).</summary>
        public bool ChargingStrikeEnabled;
        /// <summary>강타 발동까지 남은 타수.</summary>
        public int  ChargingStrikeCounter;
        /// <summary>강타 발동 주기 (기본 3).</summary>
        public float ChargingStrikePeriod;
        /// <summary>강타 피해 배율.</summary>
        public float ChargingStrikeDamageMultiplier;

        /// <summary>ATK 완성: 강타 시 충격파 + 스턴.</summary>
        public bool  ShockwaveBurstEnabled;
        /// <summary>스턴 지속 시간(초).</summary>
        public float ShockwaveBurstStunDuration;
        /// <summary>충격파 피해 배율.</summary>
        public float ShockwaveBurstDamageMultiplier;
        /// <summary>충격파 범위(단위: 픽셀/월드 유닛).</summary>
        public float ShockwaveBurstRadius;

        // ── MAG 존 ──────────────────────────────────
        /// <summary>MAG 각성: 스킬 후 N회 공격에 마법 잔향 부여.</summary>
        public bool  MagicEchoEnabled;
        /// <summary>잔향 남은 횟수.</summary>
        public int   MagicEchoRemaining;
        /// <summary>잔향 충전 횟수 (기본 3).</summary>
        public float MagicEchoChargeCount;
        /// <summary>잔향 추가 피해 비율.</summary>
        public float MagicEchoDamageBonus;

        /// <summary>MAG 완성: 잔향 공격 시 연쇄 발동 + 자동 조준.</summary>
        public bool  SkillEchoChainEnabled;
        /// <summary>연쇄 피해 배율.</summary>
        public float SkillEchoChainDamageMultiplier;
        /// <summary>자동 조준 범위.</summary>
        public float SkillEchoChainRange;

        // ── DEF 존 ──────────────────────────────────
        /// <summary>DEF 각성: 피격 피해의 일부를 쉴드로 전환.</summary>
        public bool  ShieldAccumulateEnabled;
        /// <summary>쉴드 전환 비율.</summary>
        public float ShieldAccumulateRate;
        /// <summary>현재 누적 쉴드 값.</summary>
        public float ShieldCurrentValue;
        /// <summary>쉴드 상한 (MaxHp 비율).</summary>
        public float ShieldCapRatio;

        /// <summary>DEF 완성: 쉴드 만충 시 무적 + 폭발 반격.</summary>
        public bool  ShieldBurstEnabled;
        /// <summary>무적 지속 시간(초).</summary>
        public float ShieldBurstInvincibleDuration;
        /// <summary>폭발 피해 배율.</summary>
        public float ShieldBurstDamageMultiplier;
        /// <summary>쉴드 만충 폭발 트리거 여부 (한 프레임 플래그).</summary>
        public bool  ShieldBurstTriggered;

        // ── SPD 존 ──────────────────────────────────
        /// <summary>SPD 각성: 이동 중 회피율 추가.</summary>
        public bool  DodgeOnMoveEnabled;
        /// <summary>이동 중 추가 회피율.</summary>
        public float DodgeOnMoveBonus;

        /// <summary>SPD 완성: 이동 직후 첫 공격 방어 무시 + 넉백.</summary>
        public bool  MoveAttackPenetrateEnabled;
        /// <summary>이동 후 방어 무시 공격 준비 상태 (이동 종료 시 true, 첫 공격 후 false).</summary>
        public bool  MoveAttackReady;

        // ── HP 존 ──────────────────────────────────
        /// <summary>HP 각성: HP 비율 기반 피해 경감.</summary>
        public bool  LowHpDamageReduceEnabled;
        /// <summary>최대 피해 경감률 (HP 0일 때 기준).</summary>
        public float LowHpDamageReduceMax;
        /// <summary>경감 활성화 HP 임계 비율 (기본 0.5).</summary>
        public float LowHpThreshold;

        /// <summary>DEF 완성: 치사 피해 1회 무효 + 무적 + 회복.</summary>
        public bool  DeathSaveEnabled;
        /// <summary>런당 1회 사용 여부 추적.</summary>
        public bool  DeathSaveUsed;
        /// <summary>무적 지속 시간(초).</summary>
        public float DeathSaveInvincibleDuration;
        /// <summary>즉시 회복 비율.</summary>
        public float DeathSaveHealRatio;

        // ── LUCK 존 ──────────────────────────────────
        /// <summary>LUCK 각성: 공격마다 2배/빗나감 도박.</summary>
        public bool  GambleDiceEnabled;
        /// <summary>2배 피해 확률.</summary>
        public float GambleDiceDoubleChance;
        /// <summary>빗나감 확률.</summary>
        public float GambleDiceMissChance;

        /// <summary>LUCK 완성: 치명타 시 연쇄 발동 + 황금구간 빗나감 제거.</summary>
        public bool  CritChainEnabled;
        /// <summary>연쇄 발동 확률.</summary>
        public float CritChainChance;
        /// <summary>황금구간 진입 여부 (연속 2배 성공 시 true, 빗나감 제거).</summary>
        public bool  GoldenStreakActive;

        // ── CENTER 보너스 ──────────────────────────────────
        /// <summary>CENTER 셀 2개 이상 점유 시 활성화.</summary>
        public bool CenterBonusEnabled;

        // ── 런타임 상태 초기화 ──────────────────────────────────
        public void Reset()
        {
            ChargingStrikeEnabled     = false;
            ChargingStrikeCounter     = 0;
            ChargingStrikePeriod      = 3f;
            ChargingStrikeDamageMultiplier = 2f;

            ShockwaveBurstEnabled     = false;
            ShockwaveBurstStunDuration = 0.5f;
            ShockwaveBurstDamageMultiplier = 1.5f;
            ShockwaveBurstRadius      = 60f;

            MagicEchoEnabled          = false;
            MagicEchoRemaining        = 0;
            MagicEchoChargeCount      = 3f;
            MagicEchoDamageBonus      = 0.3f;

            SkillEchoChainEnabled     = false;
            SkillEchoChainDamageMultiplier = 0.5f;
            SkillEchoChainRange       = 8f;

            ShieldAccumulateEnabled   = false;
            ShieldAccumulateRate      = 0.2f;
            ShieldCurrentValue        = 0f;
            ShieldCapRatio            = 0.3f;

            ShieldBurstEnabled        = false;
            ShieldBurstInvincibleDuration = 0.5f;
            ShieldBurstDamageMultiplier = 1.5f;
            ShieldBurstTriggered      = false;

            DodgeOnMoveEnabled        = false;
            DodgeOnMoveBonus          = 0.1f;

            MoveAttackPenetrateEnabled = false;
            MoveAttackReady           = false;

            LowHpDamageReduceEnabled  = false;
            LowHpDamageReduceMax      = 0.4f;
            LowHpThreshold            = 0.5f;

            DeathSaveEnabled          = false;
            DeathSaveUsed             = false;
            DeathSaveInvincibleDuration = 1.5f;
            DeathSaveHealRatio        = 0.3f;

            GambleDiceEnabled         = false;
            GambleDiceDoubleChance    = 0.15f;
            GambleDiceMissChance      = 0.15f;

            CritChainEnabled          = false;
            CritChainChance           = 0.5f;
            GoldenStreakActive        = false;

            CenterBonusEnabled = false;
        }
    }
}
