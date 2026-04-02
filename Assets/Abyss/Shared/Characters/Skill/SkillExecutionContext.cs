using UnityEngine;

/// <summary>
/// 스킬 실행 시 SkillBehaviorSO.Runtime에 전달되는 컨텍스트.
/// SO가 PlayerController를 직접 참조하지 않도록 중개.
/// </summary>
public class SkillExecutionContext
{
    public PlayerController Controller;
    public AbilityExecution Execution;
    public WeaponData WeaponData;
    public SkillType Slot;
    public WeaponActionType ActionType;

    /// <summary>스킬 종료 요청 — 호출하면 다음 프레임에 ActState.None으로 전환</summary>
    public System.Action RequestEnd;

    // ── 편의 접근자 ──
    public Transform PlayerTransform => Controller.transform;
    public Animator Animator => Controller.Anim;
    public Rigidbody Rigidbody => Controller.Rigid;
    public Transform HandTransform => Controller.handTransform;
    public WeaponEffectHandler EffectHandler => Controller.EffectHandler;
    public PlayerRuntimeStats RuntimeStats => Controller.RuntimeStats;

    public void RotateToMouse() => Controller.RotateTowardsMousePosition();
    public void SetMoveScale(float s) => Controller.SetMoveScale(s);

    /// <summary>캐릭터 공격력을 반영한 최종 데미지 계산</summary>
    public float CalculateDamage(float baseDamage)
    {
        if (WeaponData == null) return baseDamage;
        var kind = WeaponData.weaponType.GetAttackStatKind();
        return DamageFormula.Calculate(baseDamage, RuntimeStats.GetEffectiveAttack(kind));
    }
}
