using System.Collections;
using UnityEngine;

/// <summary>
/// 독립 실행형 그룹 운석 낙하.
/// BKDropContext 를 통해 풀에서 VFX/원을 꺼내고 완료 후 반납한다.
/// fire-and-forget: 스폰 후 코루틴이 끝나면 임시 GO 를 자동 정리한다.
/// </summary>
public class BKConcurrentDrop : MonoBehaviour
{
    private const float MeteorSpawnHeight = 12f;

    /// <summary>현재 실행 중인 그룹 낙하 수. BKRainAttackState 가 종료 판정에 사용.</summary>
    public static int ActiveCount { get; private set; }

    /// <summary>보스 재사용(OnEnable) 시 호출하여 잔여 카운트를 초기화한다.</summary>
    public static void ResetActiveCount() => ActiveCount = 0;

    // ─── 단일 위치 편의 오버로드 ──────────────────────────
    public static void Spawn(
        Vector3       targetPos,
        float         warningTime,
        float         fallDuration,
        float         hitRadius,
        int           damage,
        BKDropContext  ctx,
        float         initialDelay = 0f)
        => Spawn(new[] { targetPos }, warningTime, fallDuration,
                 hitRadius, damage, ctx, initialDelay);

    // ─── 여러 위치 동시 낙하 ──────────────────────────────
    public static void Spawn(
        Vector3[]     positions,
        float         warningTime,
        float         fallDuration,
        float         hitRadius,
        int           damage,
        BKDropContext  ctx,
        float         initialDelay = 0f)
    {
        if (positions == null || positions.Length == 0) return;
        var go = new GameObject("[GroupDrop]");
        if (ctx.Parent != null) go.transform.SetParent(ctx.Parent, false);
        ActiveCount++;
        go.AddComponent<BKConcurrentDrop>().StartCoroutine(
            RunDrop(go, positions, warningTime, fallDuration,
                    hitRadius, damage, ctx, initialDelay));
    }

    // ─────────────────────────────────────────────────────
    private static IEnumerator RunDrop(
        GameObject    self,
        Vector3[]     positions,
        float         warningTime,
        float         fallDuration,
        float         hitRadius,
        int           damage,
        BKDropContext  ctx,
        float         initialDelay)
    {
        // 개별 딜레이 (스태거)
        if (initialDelay > 0f)
            yield return new WaitForSeconds(initialDelay);

        // 모든 위치에 경고 원 표시
        foreach (var pos in positions)
        {
            if (ctx.CirclePool != null)
                ctx.CirclePool.Get(pos, hitRadius, warningTime);
            else
                BKGroundCircle.Spawn(pos, hitRadius, warningTime);
        }

        // 경고 시간 70% 대기 후 운석 스폰 시작
        yield return new WaitForSeconds(warningTime * 0.7f);

        for (int i = 0; i < positions.Length; i++)
        {
            var pos      = positions[i];
            var spawnPos = pos + Vector3.up * MeteorSpawnHeight;
            GameObject meteor;

            if (ctx.MeteorPool != null)
            {
                meteor = ctx.MeteorPool.Get(spawnPos, Quaternion.identity);
            }
            else if (ctx.MeteorPrefab != null)
            {
                meteor = Object.Instantiate(ctx.MeteorPrefab, spawnPos, Quaternion.identity);
                HalfAudioVolumes(meteor);
            }
            else continue;

            var falling = meteor.GetComponent<BKFallingObject>()
                       ?? meteor.AddComponent<BKFallingObject>();
            falling.Pool = ctx.MeteorPool;
            falling.Init(pos, fallDuration);
        }

        // 나머지 경고 + 낙하 시간 대기
        yield return new WaitForSeconds(warningTime * 0.3f + fallDuration);

        // 데미지 판정 + 착지 VFX
        var hitVfxList = new System.Collections.Generic.List<GameObject>(positions.Length);
        foreach (var pos in positions)
        {
            var cols = Physics.OverlapSphere(pos, hitRadius);
            foreach (var col in cols)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;
                player.TakeDamage(damage);
            }

            if (ctx.HitVfxPool != null)
            {
                hitVfxList.Add(ctx.HitVfxPool.Get(pos, Quaternion.identity));
            }
            else if (ctx.HitVfxPrefab != null)
            {
                var vfx = Object.Instantiate(ctx.HitVfxPrefab, pos, Quaternion.identity);
                HalfAudioVolumes(vfx);
                Object.Destroy(vfx, 3f);
            }
        }

        // 풀 VFX 재생 대기 후 반납
        if (hitVfxList.Count > 0)
        {
            yield return new WaitForSeconds(3f);
            foreach (var vfx in hitVfxList)
                if (vfx != null) ctx.HitVfxPool?.Return(vfx);
        }

        ActiveCount = Mathf.Max(0, ActiveCount - 1);
        Object.Destroy(self);
    }

    private static void HalfAudioVolumes(GameObject go)
    {
        foreach (var src in go.GetComponentsInChildren<AudioSource>(true))
            src.volume *= 0.5f;
    }
}
