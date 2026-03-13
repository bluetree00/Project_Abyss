using UnityEngine;

/// <summary>
/// 공통 AttackReady 상태.
/// - 이동 정지 후 공격 어빌리티 선택 및 애니메이션 오버라이드
/// - 쿨타임 대기 중 타깃 방향 회전
/// - 쿨타임 종료 → Attack 전환
/// - 범위 이탈 → Chase 복귀
///
/// [기존 개선] AttackStyle / AttackPurpose를 생성자로 주입받아 하드코딩 제거.
///   new LeeAttackReadyState()                                        → Melee / Normal01 (기본값)
///   new LeeAttackReadyState(Define.AttackStyle.Ranged, ...)          → 원거리 공격 몬스터에 재사용
/// </summary>
public class LeeAttackReadyState : LeeMonsterStateBase
{
    private readonly Define.AttackStyle   _style;
    private readonly Define.AttackPurpose _purpose;
    private AttackAbilitySet _attackAbilitySet;

    /// <param name="style">공격 스타일. 기본값: Melee.</param>
    /// <param name="purpose">공격 의도. 기본값: Normal01.</param>
    public LeeAttackReadyState(
        Define.AttackStyle   style   = Define.AttackStyle.Melee,
        Define.AttackPurpose purpose = Define.AttackPurpose.Normal01)
    {
        _style   = style;
        _purpose = purpose;
    }

    protected override void OnInit()
    {
        var ability = Controller.AbilitySet.GetAbility<IMonsterAbility>(Define.MonsterAbilityType.Attack);
        _attackAbilitySet = ability as AttackAbilitySet;

        if (_attackAbilitySet == null)
            Debug.LogError($"[{Controller.name}] AttackAbilitySet이 없습니다. AbilitySetSO를 확인하세요.");
    }

    protected override void OnEnter()
    {
        Controller.StopMoving();
        Controller.SetAttackReadyTime(0f); // 쿨타임 초기화 → 즉시 공격

        var selectedAttack = _attackAbilitySet?.SelectAttackAbility(_style, _purpose);
        if (selectedAttack != null)
        {
            Controller.SetCurrentAttackAbility(selectedAttack);
            // NormalAttackAbility.OnAttackEnd 필터가 올바르게 동작하도록 purpose 동기화
            Controller.currentAttackPurpose = selectedAttack.Purpose;

            if (selectedAttack is IAnimClipProvider clipProvider)
                Controller.OverrideAnimationClip("Attack", clipProvider.GetAttackAnimationClip());
        }

        Controller.animator.CrossFade(LeeFSM?.AnimAttackReady ?? "AttackReady", 0.1f);
    }

    protected override MonsterController.MonsterState OnUpdate()
    {
        if (Controller.playerTarget == null)
        {
            StateChanger.RequestStateChange(MonsterController.MonsterState.Patrol);
            return MonsterController.MonsterState.Patrol;
        }

        float dist = Vector3.Distance(Controller.transform.position, Controller.playerTarget.position);
        bool inRange = dist <= Controller.MyStat.attack_range;
        Controller.SetInAttackRange(inRange);

        // 범위 이탈 → Chase
        if (!inRange)
        {
            StateChanger.RequestStateChange(MonsterController.MonsterState.Chase);
            return MonsterController.MonsterState.Chase;
        }

        // 쿨타임 종료 → Attack
        if (Controller.AttackReadyTime <= 0f)
        {
            StateChanger.RequestStateChange(MonsterController.MonsterState.Attack);
            return MonsterController.MonsterState.Attack;
        }

        // 대기 중 타깃 방향 회전
        Vector3 dir = Controller.playerTarget.position - Controller.transform.position;
        dir.y = 0f;
        if (dir != Vector3.zero)
        {
            Controller.transform.rotation = Quaternion.Slerp(
                Controller.transform.rotation,
                Quaternion.LookRotation(dir),
                Time.deltaTime * 10f);
        }

        return MonsterController.MonsterState.AttackReady;
    }
}
