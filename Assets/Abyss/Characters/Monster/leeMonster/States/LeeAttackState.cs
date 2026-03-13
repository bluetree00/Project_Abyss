using UnityEngine;

/// <summary>
/// 공통 Attack 상태.
/// - Enter 시 CurrentAttackAbility.Execute() 호출
/// - IsAttacking이 false가 되면 (애니메이션 이벤트로 해제) Chase로 복귀
/// - 타임아웃: 이벤트가 미발동돼도 maxDuration 후 강제 탈출
///   (BatAnimationEventReceiver가 자식에 있거나 AnimationEvent 미설정 시 안전장치)
/// </summary>
public class LeeAttackState : LeeMonsterStateBase
{
    private readonly float _maxDuration;
    private float _elapsed;

    /// <param name="maxDuration">이 시간(초) 안에 OnAttackEnd 이벤트가 안 오면 강제로 Chase 복귀. 기본 3초.</param>
    public LeeAttackState(float maxDuration = 3f)
    {
        _maxDuration = maxDuration;
    }

    protected override void OnEnter()
    {
        _elapsed = 0f;
        Controller.SetAttack(true);
        // AnimAttack 이름으로 직접 CrossFade (Execute의 clip.name이 state 이름과 다를 수 있으므로)
        Controller.animator.CrossFade(LeeFSM?.AnimAttack ?? "Attack", 0.1f);
        Controller.CurrentAttackAbility?.Execute(); // 데미지/이펙트 처리
    }

    protected override void OnExit()
    {
        Controller.SetAttack(false);
        // 이벤트 구독 성공 여부와 관계없이 쿨타임을 보장
        // NormalAttackAbility.OnAttackEnd가 이미 설정했더라도 덮어쓰지 않도록 0일 때만 설정
        if (Controller.AttackReadyTime <= 0f && Controller.MyStat != null)
            Controller.SetAttackReadyTime(Controller.MyStat.attack_cooldown);
    }

    protected override MonsterController.MonsterState OnUpdate()
    {
        _elapsed += Time.deltaTime;

        // 정상 종료: 애니메이션 이벤트(OnAttackEnd)가 IsAttacking을 false로 설정
        if (!Controller.IsAttacking)
        {
            StateChanger.RequestStateChange(MonsterController.MonsterState.Chase);
            return MonsterController.MonsterState.Chase;
        }

        // 안전장치: 이벤트 미발동 시 타임아웃으로 강제 탈출
        if (_elapsed >= _maxDuration)
        {
            Debug.LogWarning($"[LeeAttackState] {Controller.name} 타임아웃 강제 탈출. " +
                             "BatAnimationEventReceiver가 루트에 붙어 있는지, " +
                             "Attack 애니메이션에 OnAttackEnd 이벤트가 설정됐는지 확인하세요.");
            Controller.SetAttack(false);
            StateChanger.RequestStateChange(MonsterController.MonsterState.Chase);
            return MonsterController.MonsterState.Chase;
        }

        return MonsterController.MonsterState.Attack;
    }
}
