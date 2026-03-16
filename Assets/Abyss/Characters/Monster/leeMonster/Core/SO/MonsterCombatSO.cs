using UnityEngine;

/// <summary>
/// 공격 타이밍·판정 레이어 설정 SO.
/// 수치(데미지 등)는 MonsterStatSO, 공격 타이밍만 여기서 관리.
/// </summary>
[CreateAssetMenu(fileName = "MonsterCombat", menuName = "Lee/Monster/CombatSO")]
public class MonsterCombatSO : ScriptableObject
{
    [Tooltip("공격 애니메이션 시작 후 실제 데미지 판정까지 지연 시간 (s). " +
             "애니메이션 이벤트(OnAnimAttackHit)로 데미지를 주고 싶으면 0으로 설정 후 " +
             "LeeMonsterAnimEventReceiver를 활용.")]
    public float damageApplyDelay = 0.4f;

    [Tooltip("공격 히트 판정 대상 레이어 (Player 레이어 설정)")]
    public LayerMask targetLayer;
}
