using System.Collections.Generic;
using UnityEngine;


namespace Abyss.Monster
{
/// <summary>
/// 보스 전용 Config ScriptableObject.
/// MonsterConfigSO를 상속하므로 MonsterBase.InitAsync()가 그대로 로드할 수 있다.
///
/// ━━ 패턴 엔트리 구조 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  Inspector에서 + / - 로 엔트리를 조립한다.
///
///  조건 복수 + 패턴 단일:
///   [CondA] [CondB] [CondC] → [PatternX]  (모두 참일 때 PatternX 실행)
///
///  조건 단일 + 패턴 복수:
///   [CondA] → [Pattern1] [Pattern2] [Pattern3]  (CondA 참일 때 패턴 중 선택)
///
///  엔트리 목록은 위에서 아래 순서로 평가되며, 가장 먼저 조건을 만족한 엔트리가 실행된다.
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

    // ── 패턴 엔트리 목록 ────────────────────────────────────
    [Header("패턴 엔트리 (위에서 아래 = 우선순위 높음)")]
    public List<BossPatternEntry> patternEntries = new();
}

/// <summary>
/// 조건과 패턴을 연결하는 하나의 엔트리.
/// conditions 가 모두 참(AND)일 때 patterns 에서 패턴을 선택해 실행한다.
/// </summary>
[System.Serializable]
public class BossPatternEntry
{
    [Tooltip("모든 조건이 참(AND)이어야 패턴이 실행 후보가 된다. 맨 위 엔트리가 최우선.")]
    public List<BossConditionSO> conditions = new();

    [Tooltip("조건 만족 시 실행할 패턴 목록")]
    public List<BossPatternSO> patterns = new();

    [Tooltip("패턴 선택 방식")]
    public PatternSelectionMode selectionMode = PatternSelectionMode.WeightedRandom;

    [Tooltip("true면 패턴 브레이크 쿨다운을 무시하고 강제 실행 (페이즈 인터럽트용).\n" +
             "실행 중인 패턴이 있으면 해당 패턴 종료 직후 즉시 실행한다.")]
    public bool forceExecute = false;
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
