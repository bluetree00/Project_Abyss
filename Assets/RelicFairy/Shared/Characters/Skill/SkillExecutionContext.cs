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

    /// <summary>
    /// 스킬 적중 1회를 <b>주 피해 파이프라인</b>으로 처리한다.
    /// IDamageable.TakeDamage를 직접 부르면 크리티컬·아이템·서약·패시브·타격감이 전부 스킵되므로
    /// 스킬은 반드시 이 경로를 쓴다(ActionType이 자동으로 Q/E/R로 전달된다).
    /// </summary>
    public float DealDamage(UnityEngine.GameObject target, float damage, float knockback)
    {
        if (target == null || Controller == null) return 0f;

        return CombatDamage.Deal(new CombatDamage.Request
        {
            Target              = target,
            BaseDamage          = damage,
            Owner               = Controller.gameObject,
            ActionType          = ActionType,
            KnockbackMultiplier = knockback,
            HitPoint            = target.transform.position + UnityEngine.Vector3.up * 1f,
            SourcePosition      = Controller.transform.position,
        });
    }

    /// <summary>IDamageable 대상 오버로드 — 스킬 Behavior들이 IDamageable 목록을 들고 있어 편의 제공.</summary>
    public float DealDamage(IDamageable target, float damage, float knockback)
    {
        var go = (target as UnityEngine.Component)?.gameObject;
        return go != null ? DealDamage(go, damage, knockback) : 0f;
    }
}
