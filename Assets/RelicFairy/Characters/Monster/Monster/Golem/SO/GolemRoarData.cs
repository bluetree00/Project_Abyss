using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 골렘 포효 특수 상태 데이터 SO.
/// Create > Lee/Monster/Special/GolemRoarData 로 .asset 생성 후
/// GolemConfigSO 의 roar 슬롯에 드래그하여 참조한다.
/// </summary>
[CreateAssetMenu(fileName = "GolemRoarData", menuName = "Lee/Monster/Special/GolemRoarData")]
public class GolemRoarData : SpecialStateDataBase
{
    [Tooltip("HP 비율이 이 이하로 떨어지면 포효 상태 진입 (0~1)")]
    [Range(0f, 1f)]
    public float hpThreshold = 0.5f;

    [Tooltip("포효 지속 시간 (초)")]
    public float roarDuration = 2.0f;

    [Tooltip("포효 애니메이션 상태 이름 (Animator State 이름과 일치)")]
    public string roarStateName = "Roar";

    [Header("포효 충격파 (주변 날려버리기)")]
    [Tooltip("충격파 반경 (m)")]
    public float knockbackRadius = 6f;

    [Tooltip("날려버리는 힘 배율 (TakeDamage knockbackMultiplier)")]
    public float knockbackForce = 5f;

    [Tooltip("충격파로 주는 데미지")]
    public float knockbackDamage = 5f;

    [Header("포효 후 분노 추격")]
    [Tooltip("포효 종료 후 빠른 추격 지속 시간 (초)")]
    public float rageChaseDuration = 6f;

    [Tooltip("분노 추격 중 이동 속도 배율")]
    public float rageSpeedMultiplier = 1.8f;

    public override SpecialStateBase CreateState() => new GolemRoarState(this);
}
