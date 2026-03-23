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
                 menuName  = "Lee/Monster/Special/BlackKnightPatternData")]
public class BlackKnightPatternData : SpecialStateDataBase
{
    // ── 공통 ─────────────────────────────────────────────
    [Header("공통")]
    [Tooltip("패턴 상태에서 공격 후 Chase 복귀 전 대기 시간 (초)")]
    public float postAttackDelay = 0.4f;

    [Tooltip("패턴 완료 후 다음 패턴 진입까지 최소 대기 시간 (초). " +
             "이 시간 동안 기본 공격 또는 추격만 한다.")]
    public float patternBreakDuration = 2f;

    [Tooltip("OverheadSlash 발동 전 평타 모드(비특수 상태)로 최소 유지해야 하는 시간 (초). " +
             "이 시간 동안 기본 공격이 진행된 뒤에야 OverheadSlash 를 발동한다.")]
    public float minBasicAttackDuration = 3f;

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

    // ── Attack 2: Spin Slash ─────────────────────────────
    [Header("Attack 2 — SpinSlash (360° 연속 회전 참격)")]
    public string spinAnimState      = "Attack02";
    [Tooltip("전체 지속 시간 (초). 앞 20% 차지, 중간 60% 회전, 뒤 20% 마무리.")]
    public float  spinDuration       = 10f;
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

    // BlackKnightBoss 는 BT 로 직접 상태를 관리하므로 CreateState 를 사용하지 않는다.
    public override LeeSpecialStateBase CreateState() => null;
}
