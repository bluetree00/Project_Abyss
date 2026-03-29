using System.Collections.Generic;
using UnityEngine;


namespace Abyss.Monster
{
/// <summary>
/// 보스 전용 Config ScriptableObject.
/// MonsterConfigSO를 상속하므로 MonsterBase.InitAsync()가 그대로 로드할 수 있다.
///
/// ━━ 패턴 엔트리 구조 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  각 엔트리의 conditions 에 조건 키 문자열을 입력한다.
///  보스 코드(BuildConditions)에서 키 → ICondition 인스턴스로 변환한다.
///  조건 파라미터 수치는 아래 condXxx 필드에서 읽어온다 (JSON 덮어쓰기 가능).
///
///  기본 제공 조건 키:
///   Phase2        — HP ≤ condPhase2HpThreshold
///   Dist_Close    — 플레이어 거리 ≤ condDistClose
///   Dist_Far      — 플레이어 거리 ≥ condDistFar
///   AfterBackstep — 직전 패턴 태그 == "backstep"
///   AfterSidestep — 직전 패턴 태그 == "sidestep"
///   TimePressure  — NormalModeTimer ≥ condTimePressureSecs
/// </summary>
[CreateAssetMenu(fileName = "BossConfig", menuName = "Abyss/Boss/BossConfig")]
public class BossConfigSO : MonsterConfigSO
{
    // ── 패턴 브레이크 ─────────────────────────────────────
    [Header("패턴 브레이크 (패턴 완료 후 추격 텀)")]
    [Tooltip("패턴 완료 후 다음 패턴까지 최소 대기 시간 (초)")]
    public float patternBreakDurationMin = 2.5f;
    [Tooltip("패턴 완료 후 다음 패턴까지 최대 대기 시간 (초)")]
    public float patternBreakDurationMax = 5f;

    // ── 연속 패턴 페널티 ───────────────────────────────────
    [Header("연속 패턴 페널티")]
    [Tooltip("직전 사용 패턴에 페널티를 적용하는 지속 시간 (초)")]
    public float patternRepeatPenaltyDuration = 20f;
    [Tooltip("페널티 구간 동안 적용할 가중치 배율. 0 = 완전 차단")]
    public float patternRepeatPenaltyMult = 0.1f;

    // ── 조건 파라미터 (JSON으로 덮어쓰기 가능) ──────────────
    [Header("조건 파라미터")]
    [Tooltip("'Phase2' 조건: HP 비율이 이 값 이하이면 참")]
    public float condPhase2HpThreshold = 0.4f;
    [Tooltip("'Dist_Close' 조건: 플레이어 거리 ≤ 이 값")]
    public float condDistClose         = 3.5f;
    [Tooltip("'Dist_Far' 조건: 플레이어 거리 ≥ 이 값")]
    public float condDistFar           = 5.0f;
    [Tooltip("'TimePressure' 조건: 패턴 미사용 경과 시간 ≥ 이 값 (초)")]
    public float condTimePressureSecs  = 8.0f;

    // ── 패턴 엔트리 목록 ────────────────────────────────────
    [Header("패턴 엔트리 (위에서 아래 = 우선순위 높음)")]
    public List<BossPatternEntry> patternEntries = new();
}

/// <summary>
/// 조건과 패턴을 연결하는 하나의 엔트리.
/// conditions 의 모든 조건이 참일 때 patterns 에서 패턴을 선택해 실행한다.
/// </summary>
[System.Serializable]
public class BossPatternEntry
{
    [Tooltip("AND 조건 키 목록. 비어있으면 항상 참 (fallback 엔트리).\n" +
             "Phase2 / Dist_Close / Dist_Far / AfterBackstep / AfterSidestep / TimePressure")]
    public List<string> conditions = new();

    /// <summary>
    /// BuildConditions() 이후 유효한 ICondition 배열.
    /// 직렬화되지 않으므로 OnInitialized에서 반드시 채워야 한다.
    /// </summary>
    [System.NonSerialized] public ICondition[] BuiltConditions;

    [Tooltip("조건 만족 시 실행할 패턴 목록")]
    public List<BossPatternSO> patterns = new();

    [Tooltip("패턴 선택 방식")]
    public PatternSelectionMode selectionMode = PatternSelectionMode.WeightedRandom;

    [Tooltip("true면 패턴 브레이크 쿨다운을 무시하고 강제 실행 (페이즈 인터럽트용).\n" +
             "실행 중인 패턴이 있으면 해당 패턴 종료 직후 즉시 실행한다.\n" +
             "Force 엔트리는 각 패턴의 CanForceInterrupt() 로 발동 여부를 판정한다.")]
    public bool forceExecute = false;

    /// <summary>
    /// 모든 conditions 를 AND 평가한다.
    /// conditions 가 비어있으면 항상 true (fallback 엔트리).
    /// </summary>
    public bool EvaluateConditions(BossPatternContext ctx)
    {
        if (BuiltConditions == null || BuiltConditions.Length == 0) return true;
        foreach (var c in BuiltConditions)
            if (!c.Evaluate(ctx)) return false;
        return true;
    }
}

/// <summary>엔트리 내 패턴 선택 방식.</summary>
public enum PatternSelectionMode
{
    /// <summary>가중치 기반 확률 선택. 반복 페널티 적용.</summary>
    WeightedRandom,
    /// <summary>목록 순서대로 순환 선택.</summary>
    Sequential,
    /// <summary>균등 확률 무작위 선택.</summary>
    Random,
}
}
