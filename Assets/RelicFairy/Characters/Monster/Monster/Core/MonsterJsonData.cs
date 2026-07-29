using System;

/// <summary>
/// 서버(JSON)에서 수신한 몬스터 데이터 역직렬화 구조.
/// JsonUtility.FromJson&lt;MonsterJsonData&gt;(json) 으로 파싱.
///
/// 필드가 0 또는 기본값이면 "미설정"으로 간주하고 SO 기본값을 유지한다.
/// 서버에서 내려준 필드만 덮어쓰도록 null-safe 처리는 ApplyToConfig()에서 담당.
/// </summary>
[Serializable]
public class MonsterJsonData
{
    // ── 기본 정보 ──────────────────────────────────────────
    public string monsterName;
    /// <summary>"Common" | "Rare" | "Elite" | "Boss"</summary>
    public string grade;

    // ── 서브 데이터 ────────────────────────────────────────
    public StatData      stat;
    public DetectionData detection;
    public PatrolData    patrol;
    public CombatData    combat;
    public AnimationData animation;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 중첩 데이터 클래스
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Serializable]
    public class StatData
    {
        public int   maxHp;
        public float defense;
        public float attackPower;
        public float moveSpeed;
        public float attackRange;
        public float attackRadius;
        public float attackRate;
        public float attackDelay;
        public float knockbackForce;
    }

    [Serializable]
    public class DetectionData
    {
        public float detectionRange;
        public float chaseGiveUpRange;
    }

    [Serializable]
    public class PatrolData
    {
        /// <summary>"Horizontal" | "Vertical" | "Random"</summary>
        public string patrolType;
        public float  patrolRange;
        public float  patrolSpeed;
        public float  waypointWaitTime;
    }

    [Serializable]
    public class CombatData
    {
        public float damageApplyDelay;
    }

    [Serializable]
    public class AnimationData
    {
        /// <summary>Addressables에 등록된 AnimatorOverrideController 주소. 비어있으면 덮어쓰지 않음.</summary>
        public string animatorControllerAddress;
        public string idleStateName;
        public string patrolStateName;
        public string chaseStateName;
        public string attackReadyStateName;
        public string attackStateName;
        public string getHitStateName;
        public string dieStateName;
        public string detectStateName;
        public string speedParam;
        public float  speedDampTime;
        public float  crossFadeDuration;
    }


    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // SO 적용
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>
    /// 파싱된 JSON 값을 MonsterConfigSO와 서브 SO에 덮어씁니다.
    /// SO는 공유 에셋이므로 런타임 수치만 갱신(Addressables 릴리즈 전까지 유효).
    /// </summary>
    public void ApplyToConfig(MonsterConfigSO config)
    {
        if (config == null) return;

        // ── 기본 정보
        if (!string.IsNullOrEmpty(monsterName))
            config.monsterName = monsterName;

        if (!string.IsNullOrEmpty(grade) &&
            System.Enum.TryParse<MonsterGrade>(grade, out var parsedGrade))
            config.grade = parsedGrade;

        // ── 스탯
        if (stat != null)
        {
            if (stat.maxHp          > 0) config.stat.maxHp          = stat.maxHp;
            if (stat.defense        > 0) config.stat.defense         = stat.defense;
            if (stat.attackPower    > 0) config.stat.attackPower     = stat.attackPower;
            if (stat.moveSpeed      > 0) config.stat.moveSpeed       = stat.moveSpeed;
            if (stat.attackRange    > 0) config.stat.attackRange     = stat.attackRange;
            if (stat.attackRadius   > 0) config.stat.attackRadius    = stat.attackRadius;
            if (stat.attackRate     > 0) config.stat.attackRate      = stat.attackRate;
            if (stat.attackDelay    > 0) config.stat.attackDelay     = stat.attackDelay;
            if (stat.knockbackForce > 0) config.stat.knockbackForce  = stat.knockbackForce;
        }

        // ── 감지
        if (detection != null)
        {
            if (detection.detectionRange   > 0) config.detection.detectionRange   = detection.detectionRange;
            if (detection.chaseGiveUpRange > 0) config.detection.chaseGiveUpRange = detection.chaseGiveUpRange;
        }

        // ── 배회
        if (patrol != null)
        {
            if (!string.IsNullOrEmpty(patrol.patrolType) &&
                System.Enum.TryParse<PatrolType>(patrol.patrolType, out var pt))
                config.patrol.patrolType = pt;

            if (patrol.patrolRange      > 0) config.patrol.patrolRange      = patrol.patrolRange;
            if (patrol.patrolSpeed      > 0) config.patrol.patrolSpeed      = patrol.patrolSpeed;
            if (patrol.waypointWaitTime > 0) config.patrol.waypointWaitTime = patrol.waypointWaitTime;
        }

        // ── 전투 타이밍
        if (combat != null)
        {
            if (combat.damageApplyDelay > 0) config.combat.damageApplyDelay = combat.damageApplyDelay;
        }

        // ── 애니메이션
        if (animation != null)
        {
            if (!string.IsNullOrEmpty(animation.animatorControllerAddress))
                config.animation.animatorControllerAddress = animation.animatorControllerAddress;
            if (!string.IsNullOrEmpty(animation.idleStateName))
                config.animation.idleStateName        = animation.idleStateName;
            if (!string.IsNullOrEmpty(animation.patrolStateName))
                config.animation.patrolStateName      = animation.patrolStateName;
            if (!string.IsNullOrEmpty(animation.chaseStateName))
                config.animation.chaseStateName       = animation.chaseStateName;
            if (!string.IsNullOrEmpty(animation.attackReadyStateName))
                config.animation.attackReadyStateName = animation.attackReadyStateName;
            if (!string.IsNullOrEmpty(animation.attackStateName))
                config.animation.attackStateName      = animation.attackStateName;
            if (!string.IsNullOrEmpty(animation.getHitStateName))
                config.animation.getHitStateName      = animation.getHitStateName;
            if (!string.IsNullOrEmpty(animation.dieStateName))
                config.animation.dieStateName         = animation.dieStateName;
            if (!string.IsNullOrEmpty(animation.speedParam))
                config.animation.speedParam           = animation.speedParam;
            if (animation.speedDampTime    > 0) config.animation.speedDampTime    = animation.speedDampTime;
            if (animation.crossFadeDuration > 0) config.animation.crossFadeDuration = animation.crossFadeDuration;
        }

    }
}
