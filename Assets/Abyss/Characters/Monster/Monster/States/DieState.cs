using UnityEngine;
using Cysharp.Threading.Tasks;


namespace Abyss.Monster
{
/// <summary>
/// 사망 상태.
/// - NavMeshAgent 비활성화, 콜라이더 비활성화
/// - 사망 애니메이션 재생
/// - 3초 후 ObjectPoolerManager에 반환
/// </summary>
public class DieState : IMonsterState
{
    private const float DespawnDelay = 3f;

    public virtual void Enter(MonsterContext ctx)
    {
        ctx.Runtime.IsDead = true;
        ctx.Monster.HideWorldHPBar();

        // 이동 중지
        ctx.Agent.enabled = false;

        // Rigidbody 속도 초기화 후 kinematic 전환 (사망 시 밀려남 방지)
        var rb = ctx.Monster.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        // 콜라이더 비활성화 (루트 + 자식 포함, 더 이상 피격되지 않도록)
        foreach (var col in ctx.Monster.GetComponentsInChildren<Collider>())
            col.enabled = false;

        // 사망 애니메이션 즉시 전환
        if (ctx.Animator != null && !string.IsNullOrEmpty(ctx.Animation.dieTrigger))
            ctx.Animator.CrossFade(ctx.Animation.dieTrigger, 0.1f, 0, 0f);

        // 지연 파괴
        DespawnAsync(ctx.Monster).Forget();
    }

    public virtual void Update(MonsterContext ctx) { }

    public virtual void Exit(MonsterContext ctx) { }

    // ── 비동기 파괴 ────────────────────────────────────────

    private static async UniTaskVoid DespawnAsync(MonsterBase monster)
    {
        await UniTask.Delay(
            System.TimeSpan.FromSeconds(DespawnDelay),
            cancellationToken: monster.destroyCancellationToken);

        if (monster != null)
            Managers.ObjectPooler.Despawn(monster.gameObject);
    }
}
}
