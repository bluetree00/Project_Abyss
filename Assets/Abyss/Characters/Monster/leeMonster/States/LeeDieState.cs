using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 공통 Die 상태.
/// - 이동 정지 + NavMeshAgent 비활성
/// - "Die" 애니메이션 재생
/// - destroyDelay 후 게임오브젝트 파괴 (0이면 파괴 안 함 → 풀 반환 등 외부에서 처리)
///
/// [기존 개선] SlimeDieState/BatDieState는 Enter가 완전히 비어 있었음.
/// </summary>
public class LeeDieState : LeeMonsterStateBase
{
    private readonly float _destroyDelay;
    private bool _dying;

    /// <param name="destroyDelay">사망 후 오브젝트 파괴까지 대기 시간(초). 0이면 자동 파괴 안 함.</param>
    public LeeDieState(float destroyDelay = 2f)
    {
        _destroyDelay = destroyDelay;
    }

    protected override void OnEnter()
    {
        _dying = true;

        Controller.StopMoving();

        if (Controller.agent != null)
            Controller.agent.enabled = false;

        Controller.animator.CrossFade(LeeFSM?.AnimDie ?? "Die", 0.1f);

        if (_destroyDelay > 0f)
            DestroyAfterDelay().Forget();
    }

    protected override MonsterController.MonsterState OnUpdate()
    {
        // 종결 상태 — 전환 없음
        return MonsterController.MonsterState.Die;
    }

    private async UniTaskVoid DestroyAfterDelay()
    {
        await UniTask.WaitForSeconds(_destroyDelay);

        if (Controller != null)
            Object.Destroy(Controller.gameObject);
    }
}
