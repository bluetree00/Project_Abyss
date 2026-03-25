using Abyss.Monster;
using UnityEngine;

/// <summary>
/// BlackKnight 보스 패턴 SO.
/// 3가지 공격의 데이터를 담으며, MonsterConfigSO.specialState0 슬롯에 할당한다.
///
/// ━━ 공격 구성 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  1) OverheadSlash  — 기본 근접 강타 (항상 사용 가능, 우선순위 최하)
///  2) SpinSlash      — 360° 회전 참격  (HP ≤ hpThresholdForSpin, 쿨다운)
///  3) ChargeAttack   — 돌진 공격       (중거리, 쿨다운, 우선순위 중간)
///
/// Selector 우선순위: SpinSlash → ChargeAttack → OverheadSlash
/// </summary>
[CreateAssetMenu(fileName = "BlackKnightPatternData",
                 menuName  = "Abyss/Monster/Special/BlackKnightPatternData")]
public class BlackKnightPatternData : SpecialStateDataBase
{
    // ── 공통 ─────────────────────────────────────────────
    [Header("공통")]
    [Tooltip("패턴 상태에서 공격 후 Chase 복귀 전 대기 시간 (초)")]
    public float postAttackDelay = 0.4f;

    [Tooltip("패턴 완료 후 추격 텀 최솟값 (초). 이 시간 동안 기본 공격/추격만 한다.")]
    public float patternBreakDurationMin = 2.5f;
    [Tooltip("패턴 완료 후 추격 텀 최댓값 (초). Min~Max 사이에서 랜덤 선택.")]
    public float patternBreakDurationMax = 5f;

    // ── 패턴 선택 가중치 ──────────────────────────────────
    [Header("패턴 선택 가중치 (높을수록 자주 선택됨)")]
    [Tooltip("SpinSlash 가중치. HP 조건 충족 시에만 후보에 오르므로 높게 설정 권장.")]
    public float spinWeight    = 8f;
    public float leapWeight    = 3f;
    public float rainWeight    = 3f;
    public float scatterWeight = 2f;
    public float chargeWeight  = 2f;
    public float overheadWeight = 2f;

    [Tooltip("직전 사용 패턴에 페널티를 적용하는 지속 시간 (초). 이 시간 안에 같은 패턴이 다시 선택될 확률을 낮춤.")]
    public float patternRepeatPenaltyDuration = 20f;
    [Tooltip("페널티 구간 동안 적용할 가중치 배율. 0 = 완전 차단, 0.1 = 10% 확률로만 재선택.")]
    public float patternRepeatPenaltyMult = 0.1f;

    [Tooltip("OverheadSlash 발동 전 평타 모드(비특수 상태)로 최소 유지해야 하는 시간 (초). " +
             "이 시간 동안 기본 공격이 진행된 뒤에야 OverheadSlash 를 발동한다.")]
    public float minBasicAttackDuration = 3f;

    // ── 사운드 ────────────────────────────────────────────
    [Header("사운드 (Hovl Studio Skill SFX)")]
    [Tooltip("기본 강타 (OverheadSlash) 시작 사운드")]
    public AudioClip overheadSfx;
    [Tooltip("회전 참격 (SpinSlash) 시작 사운드")]
    public AudioClip spinSfx;
    [Tooltip("돌진 (ChargeAttack) 시작 사운드")]
    public AudioClip chargeSfx;
    [Tooltip("낙하 공격 (RainAttack) 시작 사운드")]
    public AudioClip rainSfx;
    [Tooltip("부채꼴 투사체 (ScatterShot) 발사 사운드")]
    public AudioClip scatterSfx;
    [Tooltip("도약 (LeapSlam) 점프 사운드")]
    public AudioClip leapJumpSfx;
    [Tooltip("도약 (LeapSlam) 착지 사운드")]
    public AudioClip leapLandSfx;

    // ── Attack 1: Overhead Slash ─────────────────────────
    [Header("Attack 1 — OverheadSlash (기본 강타)")]
    [Tooltip("Animator 상태 이름")]
    public string overheadAnimState = "Attack02";
    [Tooltip("공격 애니메이션 지속 시간 (초)")]
    public float  overheadDuration  = 1.2f;
    [Tooltip("히트 판정 시작 시간 (Enter 후 몇 초 뒤)")]
    public float  overheadHitTime   = 0.5f;
    [Tooltip("히트 데미지 배율 (attackPower 기준)")]
    public float  overheadDamageMul = 1.2f;
    [Tooltip("히트 범위 반경 (m)")]
    public float  overheadRadius    = 7.5f;
    [Tooltip("쿨다운 (초) — 이 시간 동안 TryGetSpecialState 가 null 반환 → 기본 공격 허용")]
    public float  overheadCooldown  = 5f;
    [Tooltip("넉백 Y 방향값")]
    public float  overheadKnockbackY = 0.3f;

    // ── Attack 2: Spin Slash ─────────────────────────────
    [Header("Attack 2 — SpinSlash (360° 연속 회전 참격)")]
    public string spinAnimState      = "Attack02";
    [Tooltip("전체 지속 시간 (초).")]
    public float  spinDuration       = 10f;
    [Tooltip("차지 구간 비율 (0~1). 앞부분 이 비율만큼 차지, 뒤도 동일.")]
    public float  spinChargeRatio    = 0.2f;
    [Tooltip("마무리 구간 비율 (0~1).")]
    public float  spinWindDownRatio  = 0.2f;
    [Tooltip("쿨다운 (초)")]
    public float  spinCooldown       = 6f;
    [Tooltip("발동 HP 임계값 목록 (내림차순 정렬). 각 구간에서 한 번씩 발동.")]
    public float[] spinHpThresholds  = { 0.85f, 0.55f, 0.25f };
    [Tooltip("히트 범위 반경 (m) — 회전 공격이므로 넓게 설정")]
    public float  spinRadius         = 3.5f;
    [Tooltip("회전 중 히트 1회당 데미지 배율 (attackPower 기준). 연속 히트이므로 낮게 설정.")]
    public float  spinDamageMul      = 0.4f;
    [Tooltip("회전 중 히트 간격 (초)")]
    public float  spinHitInterval    = 0.55f;
    [Tooltip("히트 섬광 지속 시간 (초)")]
    public float  spinFlashDuration  = 0.15f;
    [Tooltip("차지 구간 맥동 시작 Hz")]
    public float  spinChargeFreqMin  = 4f;
    [Tooltip("차지 구간 맥동 종료 Hz")]
    public float  spinChargeFreqMax  = 18f;
    [Tooltip("스핀 구간 맥동 시작 Hz")]
    public float  spinSpinFreqMin    = 6f;
    [Tooltip("스핀 구간 맥동 종료 Hz")]
    public float  spinSpinFreqMax    = 24f;
    [Tooltip("스핀 반지름 1단계 배율")]
    public float  spinRadiusMul1     = 1.5f;
    [Tooltip("스핀 반지름 2단계 배율")]
    public float  spinRadiusMul2     = 1.9f;
    [Tooltip("스핀 반지름 3단계 배율")]
    public float  spinRadiusMul3     = 2.4f;
    [Tooltip("넉백 Y 방향값")]
    public float  spinKnockbackY     = 0.4f;
    [Tooltip("넉백 힘 배율")]
    public float  spinKnockbackMul   = 1.5f;

    // ── Attack 3: Charge Attack ───────────────────────────
    [Header("Attack 3 — ChargeAttack (돌진)")]
    public string chargeAnimState   = "Attack03";
    public float  chargeDuration    = 1.4f;
    [Tooltip("쿨다운 (초)")]
    public float  chargeCooldown    = 8f;
    [Tooltip("돌진 발동 최소 거리 (m)")]
    public float  chargeMinDist     = 4f;
    [Tooltip("돌진 발동 최대 거리 (m)")]
    public float  chargeMaxDist     = 9f;
    [Tooltip("돌진 이동 속도")]
    public float  chargeSpeed       = 14f;
    [Tooltip("돌진 데미지 배율")]
    public float  chargeDamageMul   = 1.5f;
    [Tooltip("돌진 히트 범위")]
    public float  chargeRadius      = 2f;
    [Tooltip("바람업 비율 (0~1). 이 비율 동안 경고 후 돌진 시작.")]
    public float  chargeWindUpRatio = 0.3f;
    [Tooltip("넉백 Y 방향값")]
    public float  chargeKnockbackY  = 0.5f;
    [Tooltip("넉백 힘 배율")]
    public float  chargeKnockbackMul = 2.5f;

    // ── RainAttack 방어막 ─────────────────────────────────
    [Header("RainAttack 방어막 (무적 시각 효과)")]
    [Tooltip("방어막 구체 반지름 (m). BKShieldBarrier 프로시저럴 구체에 적용.")]
    public float barrierRadius = 2.5f;
    [Tooltip("방어막 기본 색상 (RGBA — 알파가 투명도)")]
    public Color barrierColor = new Color(0.3f, 0.7f, 1f, 0.22f);
    [Tooltip("방어막 박동 최고 색상")]
    public Color barrierPeakColor = new Color(0.5f, 0.9f, 1f, 0.38f);
    [Tooltip("박동 주기 (초)")]
    public float barrierPulsePeriod = 1.2f;

    // ── Attack 4: Rain Attack ─────────────────────────────
    [Header("Attack 4 — RainAttack (낙하 공격)")]
    [Tooltip("보스 애니메이션 상태 이름 (방패 치기 등)")]
    public string rainAnimState      = "Attack02";
    public float  rainCooldown       = 14f;
    [Tooltip("반복 라운드 수")]
    public int    rainRounds         = 2;
    [Tooltip("라운드당 낙하 횟수")]
    public int    rainDropsPerRound  = 3;
    [Tooltip("경고 원 표시 시간 (초) — 모든 페이즈 동일")]
    public float  rainWarningTime    = 0.9f;
    [Tooltip("경고 시간 중 플레이어 추적 비율 (0~1). 나머지는 고정 구간.")]
    public float  rainTrackRatio     = 0.70f;
    [Tooltip("같은 라운드 내 연속 낙하 간격 (초)")]
    public float  rainDropInterval   = 0.05f;
    [Tooltip("라운드/섹션 사이 대기 시간 (초)")]
    public float  rainRoundDelay     = 1.2f;
    [Tooltip("낙하 피해 범위 반경 (m)")]
    public float  rainHitRadius      = 2.0f;
    public float  rainDamageMul      = 1.0f;
    [Tooltip("운석 스폰 높이 (m)")]
    public float  rainMeteorSpawnHeight = 12f;
    [Tooltip("메인 패턴 완료 후 ChaseState 복귀 전 대기 시간 (초)")]
    public float  rainCooldownDelay  = 3.0f;
    [Tooltip("방어막 페이드아웃 시간 (초)")]
    public float  rainBarrierFadeDuration = 1.5f;
    [Tooltip("착지 Hit VFX 표시 시간 (초)")]
    public float  rainHitVfxDuration = 3.0f;
    [Tooltip("낙하물 VFX 프리팹 (Meteor.prefab 등) — 비워두면 스킵")]
    public GameObject rainMeteorVfxPrefab;
    [Tooltip("착지 VFX 프리팹 (Meteor hit.prefab 등) — 비워두면 스킵")]
    public GameObject rainHitVfxPrefab;

    [Header("Attack 4-2 — RainAttack Phase 1 (랜덤 동시 낙하)")]
    [Tooltip("플레이어 기준 랜덤 낙하 최소 반경 (m)")]
    public float  rainRandomRadiusMin   = 2f;
    [Tooltip("플레이어 기준 랜덤 낙하 최대 반경 (m)")]
    public float  rainRandomRadius      = 5f;
    [Tooltip("1페이즈에서 한 번에 떨어지는 운석 개수")]
    public int    rainPhase1Count       = 3;
    [Tooltip("2페이즈에서 한 번에 떨어지는 운석 개수 (1페이즈보다 많게)")]
    public int    rainPhase2Count       = 5;
    [Tooltip("개별 운석 낙하 딜레이 최대값 (초) — 0~이 값 사이 랜덤으로 스태거")]
    public float  rainRandomStaggerSpread = 1.5f;
    [Tooltip("랜덤 낙하 발동 최소 간격 (초)")]
    public float  rainRandomMinInterval = 1.2f;
    [Tooltip("랜덤 낙하 발동 최대 간격 (초)")]
    public float  rainRandomMaxInterval = 2.5f;
    [Tooltip("동시 패턴 낙하 드롭 간격 (초) — +/X 패턴 전용")]
    public float  rainConcurrentInterval = 1.8f;

    [Header("Attack 4-3 — RainAttack Phase 2 (+/X 패턴 동시 낙하)")]
    [Tooltip("각 팔 방향당 운석 개수 (팔 × 4방향 동시 낙하)")]
    public int    rainPatternCount   = 2;
    [Tooltip("팔 방향 운석 간 간격 (m)")]
    public float  rainPatternSpacing = 3f;
    [Tooltip("첫 번째 운석까지의 보스 기준 거리 (m)")]
    public float  rainPatternRadius  = 3f;

    // ── Attack 5: Scatter Shot ───────────────────────────
    [Header("Attack 5 — ScatterShot (부채꼴 투사체)")]
    public string scatterAnimState    = "Attack01";
    public float  scatterCooldown     = 10f;
    [Tooltip("부채꼴 내 투사체 개수")]
    public int    scatterCount        = 7;
    [Tooltip("부채꼴 전체 각도 (도)")]
    public float  scatterFanAngle     = 60f;
    [Tooltip("2회차 발사 시 전체 부채꼴 회전 오프셋 (도) — 엇갈리도록")]
    public float  scatterAngleOffset  = 15f;
    public float  scatterSpeed        = 12f;
    public float  scatterRange        = 18f;
    public float  scatterDamageMul    = 0.8f;
    [Tooltip("1 → 2회차 발사 사이 딜레이 (초)")]
    public float  scatterShotDelay    = 0.9f;
    [Tooltip("투사체 충돌 판정 반경 (m). 기본값 0.25")]
    public float      scatterHitRadius       = 0.25f;
    [Tooltip("투사체 프리팹 (BKBossProjectile 컴포넌트 필요) — 비워두면 스킵")]
    public GameObject scatterProjectilePrefab;

    // ── Attack 6: Leap Slam ──────────────────────────────
    [Header("Attack 6 — LeapSlam (점프 내려찍기)")]
    public string leapAnimState      = "Attack03";
    public float  leapCooldown       = 18f;
    [Tooltip("경고 원이 플레이어를 추적하는 시간 (초)")]
    public float  leapTrackDuration  = 2.5f;
    [Tooltip("원 고정 후 착지까지 시간 (초)")]
    public float  leapLockDuration   = 1.2f;
    [Tooltip("착지 피해 반경 (m)")]
    public float  leapSlamRadius     = 5.0f;
    public float  leapDamageMul      = 2.5f;
    [Tooltip("보스가 사라지는 점프 높이 (m)")]
    public float  leapJumpHeight     = 20f;
    [Tooltip("도약 올라가는 시간 (초)")]
    public float  leapUpDuration     = 0.3f;
    [Tooltip("착지 후 경직 시간 (초)")]
    public float  leapRecoveryDuration = 0.5f;
    [Tooltip("착지 VFX 표시 시간 (초)")]
    public float  leapVfxDuration    = 2.0f;
    [Tooltip("착지 넉백 Y 방향값")]
    public float  leapKnockbackY     = 0.5f;
    [Tooltip("착지 넉백 힘 배율")]
    public float  leapKnockbackMul   = 2.0f;
    [Tooltip("착지 충격파 VFX 프리팹 (Energy explosion.prefab 등) — 비워두면 스킵")]
    public GameObject leapSlamVfxPrefab;

    // BlackKnightBoss 는 BT 로 직접 상태를 관리하므로 CreateState 를 사용하지 않는다.
    public override SpecialStateBase CreateState() => null;
}
