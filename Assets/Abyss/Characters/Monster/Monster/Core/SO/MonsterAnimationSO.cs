using UnityEngine;

/// <summary>
/// 몬스터 애니메이션 설정 SO.
/// 어느 몬스터가 들어와도 이 SO의 값만 바꾸면 애니메이터 파라미터/상태 이름이 연동됨.
/// </summary>
[CreateAssetMenu(fileName = "MonsterAnimation", menuName = "Lee/Monster/AnimationSO")]
public class MonsterAnimationSO : ScriptableObject
{
    [Header("Animator Override Controller")]
    [Tooltip("Addressables에 등록된 AnimatorOverrideController(.overrideController) 주소.\n" +
             "비어있으면 프리팹 Animator에 붙은 Controller를 그대로 사용.\n" +
             "Override Controller의 Base는 Bat.controller로 설정할 것.")]
    public string animatorControllerAddress;

    [Header("CrossFade 상태 이름")]
    [Tooltip("기본 대기 상태 이름 (Animator State 이름과 일치해야 함)")]
    public string idleStateName        = "Idle_Normal";

    [Tooltip("배회 이동 상태 이름")]
    public string patrolStateName      = "MoveBlend";

    [Tooltip("추격 이동 상태 이름")]
    public string chaseStateName       = "MoveBlend";

    [Tooltip("공격 준비(대기) 상태 이름")]
    public string attackReadyStateName = "AttackReady";

    [Header("Trigger 파라미터 이름")]
    [Tooltip("공격 트리거")]
    public string attackTrigger  = "Attack01";

    [Tooltip("피격 트리거")]
    public string getHitTrigger  = "GetHit";

    [Tooltip("사망 트리거")]
    public string dieTrigger     = "Die";

    [Tooltip("플레이어 감지 트리거 (SenseSomething 등)")]
    public string detectTrigger  = "SenseSomething";

    [Header("블렌드 파라미터 이름 (선택)")]
    [Tooltip("이동 속도 Float 파라미터. 사용 안 하면 비워두기.")]
    public string speedParam     = "";

    [Header("전환 설정")]
    [Tooltip("CrossFade 전환 시간 (s)")]
    public float crossFadeDuration = 0.15f;
}
