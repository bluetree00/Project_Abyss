using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Abyss.Monster;

/// <summary>
/// 원거리 발사체 공격 SO.
/// Execute() 호출 시 projectilePrefab 을 인스턴스화하고
/// MonsterProjectile 컴포넌트에 발사 파라미터를 주입한다.
/// </summary>
[CreateAssetMenu(fileName = "RangedAttack", menuName = "Lee/Monster/AttackShape/Ranged")]
public class MonsterRangedAttackSO : MonsterAttackShapeSO
{
    [Tooltip("발사체 프리팹 (MonsterProjectile 컴포넌트 필수). vfxProjectilePrefab이 있으면 이 필드는 무시됨.")]
    public MonsterProjectile projectilePrefab;

    [Tooltip("발사체 이동 속도 (m/s).")]
    public float projectileSpeed = 10f;

    [Tooltip("발사체 최대 비행 거리 (m). 초과 시 자동 소멸.")]
    public float projectileMaxRange = 20f;

    [Tooltip("발사 높이 오프셋. 발사 위치 = 몬스터 위치 + (0, launchHeightOffset, 0).")]
    public float launchHeightOffset = 1f;

    [Header("VFX 투사체 (physics 발사체 대체)")]
    [Tooltip("설정 시 projectilePrefab 대신 VFX 이펙트가 직선으로 날아가며 데미지를 입힘.")]
    public GameObject vfxProjectilePrefab;
    [Tooltip("VFX 투사체 스케일 배율.")]
    public float vfxProjectileScale = 1f;
    [Tooltip("VFX가 플레이어에게 이 거리 이하로 접근하면 히트 판정. (기본 0.6m)")]
    public float vfxProjectileHitRadius = 0.6f;

    [Header("VFX 임팩트 (선택)")]
    [Tooltip("VFX 투사체 명중 시 스폰할 임팩트 이펙트 프리팹.")]
    public GameObject vfxHitEffectPrefab;
    [Tooltip("임팩트 이펙트 스케일 배율.")]
    public float vfxHitEffectScale = 1f;

    [Header("Multi-Projectile")]
    [Tooltip("한 번의 공격에 발사할 투사체 수. 1이면 단발.")]
    public int projectileCount = 1;
    [Tooltip("발사 후 Idle로 복귀하기 전 대기 시간(초). 공격 애니메이션의 나머지 재생 길이에 맞게 설정.")]
    public float postFireWait = 0f;
    [Tooltip("Idle 유지 시간(초). 다음 Attack 애니메이션 시작까지 Idle을 유지하는 간격.")]
    public float projectileInterval = 0.15f;
    [Tooltip("true면 발사와 발사 사이에 IdleBattle → Attack 애니메이션을 다시 재생.")]
    public bool returnToIdleBetweenShots = false;

    [Header("Debuff (Optional)")]
    [Tooltip("피격 시 슬로우 배율 (0=미적용, 0.5=50% 감속).")]
    public float slowScale;

    [Tooltip("슬로우 지속 시간 (초).")]
    public float slowDuration;

    public override void Execute(MonsterContext ctx, int damage, float knockbackForce)
    {
        if (ctx.Runtime?.PlayerTarget == null) return;

        SpawnVFX(ctx.Transform);

        int count = Mathf.Max(1, projectileCount);
        if (count == 1)
        {
            FireSingle(ctx, damage, knockbackForce);
            return;
        }

        FireSequenceAsync(ctx, damage, knockbackForce, count).Forget();
    }

    private async UniTaskVoid FireSequenceAsync(MonsterContext ctx, int damage, float knockbackForce, int count)
    {
        var token = ctx.Monster.destroyCancellationToken;

        ctx.Runtime.IsExecutingAttackSequence = true;
        try
        {
            // 첫 발사: AttackState에서 이미 Attack 애니메이션이 재생 중이므로 바로 발사
            FireSingle(ctx, damage, knockbackForce);

            for (int i = 1; i < count; i++)
            {
                if (ctx.Monster == null || !ctx.Monster.gameObject.activeInHierarchy) return;

                // 발사 후 공격 애니메이션 나머지 재생 대기
                if (postFireWait > 0f)
                    await UniTask.Delay(TimeSpan.FromSeconds(postFireWait), cancellationToken: token);

                if (ctx.Monster == null || !ctx.Monster.gameObject.activeInHierarchy) return;

                // Idle 복귀
                if (returnToIdleBetweenShots && ctx.Animator != null
                    && !string.IsNullOrEmpty(ctx.Animation.attackReadyStateName))
                {
                    ctx.Animator.CrossFade(ctx.Animation.attackReadyStateName, ctx.Animation.crossFadeDuration);
                }

                // Idle 유지 시간 대기
                if (projectileInterval > 0f)
                    await UniTask.Delay(TimeSpan.FromSeconds(projectileInterval), cancellationToken: token);

                if (ctx.Monster == null || !ctx.Monster.gameObject.activeInHierarchy) return;
                if (ctx.Runtime?.PlayerTarget == null) return;

                // 플레이어 방향 재조준
                FacePlayer(ctx);

                // Attack 애니메이션 처음부터 재생
                if (returnToIdleBetweenShots && ctx.Animator != null
                    && !string.IsNullOrEmpty(ctx.Animation.attackTrigger))
                {
                    ctx.Animator.CrossFade(ctx.Animation.attackTrigger, ctx.Animation.crossFadeDuration, 0, 0f);
                }

                // 발사 타이밍까지 대기
                await UniTask.Delay(TimeSpan.FromSeconds(ctx.Combat.damageApplyDelay), cancellationToken: token);

                if (ctx.Monster == null || !ctx.Monster.gameObject.activeInHierarchy) return;
                if (ctx.Runtime?.PlayerTarget == null) return;

                FireSingle(ctx, damage, knockbackForce);
            }
        }
        finally
        {
            if (ctx.Runtime != null)
                ctx.Runtime.IsExecutingAttackSequence = false;
        }
    }

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime?.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }

    private void FireSingle(MonsterContext ctx, int damage, float knockbackForce)
    {
        Vector3 origin    = ctx.Transform.position + Vector3.up * launchHeightOffset;
        Vector3 targetPos = ctx.Runtime.PlayerTarget.position + Vector3.up * launchHeightOffset;
        Vector3 direction = (targetPos - origin).normalized;

        if (vfxProjectilePrefab != null)
        {
            var go = UnityEngine.Object.Instantiate(vfxProjectilePrefab, origin, Quaternion.LookRotation(direction));
            go.transform.localScale = Vector3.one * Mathf.Max(0.001f, vfxProjectileScale);
            ApplyHierarchyScaling(go);

            if (!go.TryGetComponent<MonsterVfxProjectile>(out var vfxProj))
                vfxProj = go.AddComponent<MonsterVfxProjectile>();

            vfxProj.Init(
                direction, projectileSpeed, projectileMaxRange,
                damage, knockbackForce, slowScale, slowDuration,
                vfxProjectileHitRadius, ctx.Runtime.PlayerTarget,
                vfxHitEffectPrefab, vfxHitEffectScale);
            return;
        }

        if (projectilePrefab == null) return;
        var proj = UnityEngine.Object.Instantiate(projectilePrefab, origin, Quaternion.LookRotation(direction));
        proj.Init(direction, projectileSpeed, projectileMaxRange, damage, knockbackForce, slowScale, slowDuration);
    }
}
