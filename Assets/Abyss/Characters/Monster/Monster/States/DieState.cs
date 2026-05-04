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

        // 외부 수명주기 구독자(방 클리어 카운터 등)에 사망 통지
        ctx.Monster.RaiseDied();

        // 골드 코인 드롭
        SpawnGoldDrop(ctx);

        // 이동 중지
        ctx.Agent.enabled = false;

        // Rigidbody 속도 초기화 후 kinematic 전환 (사망 시 밀려남 방지)
        // kinematic 상태에서는 velocity 할당이 경고를 내므로 비활성화 상태에서만 초기화.
        var rb = ctx.Monster.GetComponent<Rigidbody>();
        if (rb != null)
        {
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            rb.isKinematic = true;
        }

        // 콜라이더 비활성화 (루트 + 자식 포함, 더 이상 피격되지 않도록)
        foreach (var col in ctx.Monster.GetComponentsInChildren<Collider>())
            col.enabled = false;

        // 사망 애니메이션 즉시 전환 (dieTrigger 미설정 시 dieStateName으로 fallback)
        string dieAnim = !string.IsNullOrEmpty(ctx.Animation.dieTrigger)
            ? ctx.Animation.dieTrigger
            : ctx.Animation.dieStateName;
        if (ctx.Animator != null && !string.IsNullOrEmpty(dieAnim))
        {
            ctx.Animator.speed = 1f;
            ctx.Animator.CrossFade(dieAnim, 0.1f, 0, 0f);
        }

        // 지연 파괴
        DespawnAsync(ctx.Monster).Forget();
    }

    public virtual void Update(MonsterContext ctx) { }

    public virtual void Exit(MonsterContext ctx) { }

    // ── 드롭 ──────────────────────────────────────────────

    private static void SpawnGoldDrop(MonsterContext ctx)
    {
        var config = ctx.Config;
        if (config == null) return;

        var drop = config.drop;
        if (drop == null || drop.coinMax <= 0 || drop.coinValue <= 0) return;

        int min = Mathf.Max(0, drop.coinMin);
        int max = Mathf.Max(min, drop.coinMax);
        int count = UnityEngine.Random.Range(min, max + 1);
        if (count <= 0) return;

        GoldCoinPickup.SpawnDrops(ctx.Monster.transform.position, count, drop.coinValue);
    }

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
