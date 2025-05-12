using UnityEngine;

[CreateAssetMenu(menuName = "Weapon/Animation/HeavyAttackAnimationSet")]
public class HeavyAttackAnimationSetSO : ScriptableObject
{
    [Header("모으기 애니메이션")]
    public AnimationClip chargeClip;

    [Header("강공격 실행 애니메이션")]
    public AnimationClip attackClip;

    [Header("강공격 종료 애니메이션")]
    public AnimationClip endClip;

    [Header("변경할 기존 공격 애니메이션 이름")]
    public string[] HeavyAttackAnimations;

    // 확장 예시
    [Header("모으기 시간")]
    public float chargeDuration = 1.5f;

    [Header("강공격 임팩트 세기")]
    public float attackPower = 100f;
}
