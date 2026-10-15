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

    /// <summary>
    /// 스킬 타이머용 한 프레임 시간 — 플레이어 애니메이터와 같은 시계. 저스트 회피 슬로모(게임 시간 0.35배) 동안 플레이어 애니메이터는
    /// 실제 시간(UnscaledTime)으로 도는데 타이머를 게임 시간으로 세면 피해가 동작보다 늦게 들어갔다
    /// (실측 장비 형/21: 최후의 일격 내려찍기 평소 0.62초 → 슬로모 중 1.42초, 10-09).
    /// </summary>
    public float DeltaTime => Animator != null && Animator.updateMode == AnimatorUpdateMode.UnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
    public Rigidbody Rigidbody => Controller.Rigid;
    public Transform HandTransform => Controller.HandTransform;
    public WeaponEffectHandler EffectHandler => Controller.EffectHandler;
    public PlayerRuntimeStats RuntimeStats => Controller.RuntimeStats;

    /// <summary>스킬 단계(1~3). 정적 tier가 아니라 강화(근접)·파츠(원거리)에서 파생 — <see cref="SkillTierResolver"/>.</summary>
    public int SkillTier => SkillTierResolver.Resolve(WeaponData);

    /// <summary>이 스킬을 쏜 무기 슬롯. 투사체 퍼널(<see cref="CombatSpawner"/>)의 파츠 스코프 게이트에 쓴다.</summary>
    public int SourceSlot => Controller?.WeaponManager?.CurrentSlotIndex ?? 0;

    /// <summary>마우스 쪽으로 돈다. 장비 스킬(E · R)은 조준 보조(마우스 콘 15° 안 가장 가까운 적 — 10-08 A1), 유물 Q는 마우스 그대로.</summary>
    public void RotateToMouse()
    {
        if (Slot == SkillType.Q) Controller.RotateTowardsMousePosition();
        else                     Controller.RotateTowardsSkillAim();
    }

    /// <summary>후딜에 들어갔는가(마지막 피해가 끝났다) — <see cref="EnterRecovery"/>.</summary>
    public bool InRecovery { get; private set; }

    /// <summary>
    /// 후딜 진입(10-08): 마지막 피해가 끝나 남은 건 마무리 동작뿐이다. 회피로 끊을 수 있게 열고(A2 · 결정 S2 「피해 뒤만」),
    /// 시전 스탠스(경직 면역)를 푼다(A4). 각 스킬의 끝(End) 단계 첫 프레임에 부른다 — 여러 번 불러도 된다.
    /// </summary>
    public void EnterRecovery()
    {
        if (InRecovery || Controller == null) return;
        InRecovery = true;
        Controller.SkillRecovering = true;
        Controller.EndStance(this);
        if (Slot != SkillType.Q) Controller.TrailVfx?.EndSkillTrail();   // 마지막 타가 끝났다 — 트레일도 끈다(A7)
    }

    /// <summary>
    /// 지금 겨냥한 정면(수평). <see cref="RotateToMouse"/>는 회전을 요청만 하고 다음 물리 스텝에 적용되므로, 그 직후
    /// <c>PlayerTransform.forward</c>는 옛 방향이다 — 성광베기 돌진 · 최후의 일격 판정 · 궁수의 집중 화살이 한 박자 전 방향으로 나갔다(10-06 B1~B3).
    /// 회전을 요청한 프레임의 방향은 이것을 쓴다.
    /// </summary>
    public UnityEngine.Vector3 AimForward => Controller.AimForward;

    /// <summary>
    /// 돌진 끝점 — <paramref name="dir"/>로 <paramref name="distance"/>만큼 가되 첫 벽 앞에서 멈춘다(적은 꿰뚫고 지나간다 · <see cref="PlayerController.ClearDistanceAhead"/>).
    /// 끝점을 정해 두고 Lerp로 MovePosition하면 한 프레임에 1~2 m씩 건너뛰어 벽을 뚫었다(10-14 사용자 「무형검 R · 대시 스킬이 벽을 뚫는다」).
    /// </summary>
    public UnityEngine.Vector3 DashEndBeforeWall(UnityEngine.Vector3 start, UnityEngine.Vector3 dir, float distance)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f || distance <= 0f) return start;
        dir.Normalize();
        return start + dir * Controller.ClearDistanceAhead(start, dir, distance);
    }
    public void SetMoveScale(float s) => Controller.SetMoveScale(s);

    /// <summary>캐릭터 공격력을 반영한 최종 데미지 계산</summary>
    public float CalculateDamage(float baseDamage)
    {
        if (WeaponData == null) return baseDamage;
        var kind = WeaponData.weaponType.GetAttackStatKind();
        return DamageFormula.Calculate(baseDamage, RuntimeStats.GetEffectiveAttack(kind)) * MeleeAugments.SkillDamageMult(WeaponData, Slot);   // 환영 베기 강화 증강(10-15)
    }

    /// <summary>
    /// 스킬 적중 1회를 <b>주 피해 파이프라인</b>으로 처리한다.
    /// IDamageable.TakeDamage를 직접 부르면 크리티컬·아이템·서약·패시브·타격감이 전부 스킵되므로
    /// 스킬은 반드시 이 경로를 쓴다(ActionType이 자동으로 Q/E/R로 전달된다).
    /// </summary>
    /// <param name="skipHitVfx">스킬이 자기 타격 이펙트를 직접 띄우면 true — 공용 히트 VFX(HitEffect_02)가 한 번 더 떠 한 타에 둘이 보였다(10-06 버그 목록).</param>
    public float DealDamage(UnityEngine.GameObject target, float damage, float knockback, bool isFinisher = false, bool skipHitVfx = false)
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
            IsFinisher          = isFinisher,
            SkipHitVfx          = skipHitVfx,
        });
    }

    /// <summary>IDamageable 대상 오버로드 — 스킬 Behavior들이 IDamageable 목록을 들고 있어 편의 제공.</summary>
    public float DealDamage(IDamageable target, float damage, float knockback, bool isFinisher = false, bool skipHitVfx = false)
    {
        // 파괴된 대상(죽는 순간 Destroy하는 DK 떠다니는 검 · 무덤 기둥 등)은 Unity null인데 `?.`는 C# null만 거른다 — .gameObject에서 예외가 나
        // 스킬 상태가 끝나지 않아 굳고 앞 대상들은 매 프레임 다시 맞았다(10-09 출시 점검 P0)
        if (target is not UnityEngine.Component c || c == null) return 0f;
        return DealDamage(c.gameObject, damage, knockback, isFinisher, skipHitVfx);
    }
}
