using UnityEngine;

/// <summary>
/// 공격 상태 (몸통박치기 등 근거리 공격).
///
/// 흐름:
///  Enter → 공격 애니메이션 트리거 → damageApplyDelay 후 DealDamageToPlayer()
///       → 1/attackRate 초 후 쿨다운 종료
///       → 여전히 사정거리 안이면 AttackReady, 아니면 Chase
///
/// 데미지 타이밍은 두 가지를 지원:
///  1) 타이머 방식 (기본) : MonsterCombatSO.damageApplyDelay
///  2) 애니메이션 이벤트 방식 : LeeMonsterBase.OnAnimAttackHit() 가 직접 호출
///     (이 경우 damageApplyDelay = 0 으로 설정해 타이머가 즉시 비활성화)
/// </summary>
public class LeeAttackState : ILeeMonsterState
{
    private float _cooldownTimer;   // 다음 상태 전환까지 남은 시간
    private float _damageTimer;     // 데미지 적용까지 남은 시간
    private bool  _damageDealt;     // 이 공격에서 데미지를 이미 줬는지

    public void Enter(LeeMonsterContext ctx)
    {
        ctx.Agent.ResetPath();

        _cooldownTimer = 1f / Mathf.Max(0.01f, ctx.Stat.attackRate);
        _damageTimer   = ctx.Combat.damageApplyDelay;
        _damageDealt   = false;

        ctx.Runtime.AttackHitDealt = false;

        // 공격 애니메이션 (CrossFade로 직접 전환 — AnyState 트리거 불필요)
        if (ctx.Animator != null && !string.IsNullOrEmpty(ctx.Animation.attackTrigger))
            ctx.Animator.CrossFade(ctx.Animation.attackTrigger, 0.05f, 0, 0f);

        // 공격 시 플레이어 방향 바라보기
        FacePlayer(ctx);
    }

    public void Update(LeeMonsterContext ctx)
    {
        // 타이머 방식 데미지 적용
        if (!_damageDealt && _damageTimer > 0f)
        {
            _damageTimer -= Time.deltaTime;
            if (_damageTimer <= 0f)
            {
                _damageDealt = true;
                ctx.Monster.DealDamageToPlayer();
            }
        }

        // 공격 쿨다운 경과 → 다음 상태 결정
        _cooldownTimer -= Time.deltaTime;
        if (_cooldownTimer > 0f) return;

        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState(LeeMonsterStateType.Patrol);
            return;
        }

        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        ctx.Monster.ChangeState(
            dist <= ctx.Stat.attackRange
                ? LeeMonsterStateType.AttackReady
                : LeeMonsterStateType.Chase);
    }

    public void Exit(LeeMonsterContext ctx) { }

    // ── 헬퍼 ──────────────────────────────────────────────

    private static void FacePlayer(LeeMonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }
}
