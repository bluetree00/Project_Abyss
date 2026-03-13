using UnityEngine;

/// <summary>
/// 피격 상태.
/// - 피격 애니메이션 재생 후 Chase(타겟 있음) 또는 Idle로 복귀
/// </summary>
public class LeeGetHitState : LeeMonsterStateBase
{
    private readonly float _duration;
    private float _elapsed;

    /// <param name="duration">피격 경직 시간(초). 기본 0.6초.</param>
    public LeeGetHitState(float duration = 0.6f)
    {
        _duration = duration;
    }

    protected override void OnEnter()
    {
        _elapsed = 0f;
        Controller.StopMoving();
        Controller.animator.CrossFade(LeeFSM?.AnimGetHit ?? "GetHit", 0.05f);
    }

    protected override MonsterController.MonsterState OnUpdate()
    {
        _elapsed += Time.deltaTime;

        if (_elapsed >= _duration)
        {
            var next = Controller.HasDetectedTarget
                ? MonsterController.MonsterState.Chase
                : MonsterController.MonsterState.Idle;
            StateChanger.RequestStateChange(next);
            return next;
        }

        return MonsterController.MonsterState.GetHit;
    }
}
