using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Wall 전용 낙하 연출. 기본 맵 연출이 끝난 뒤 호출되어, 각 벽이 z축(뒤 → 앞) row 순차로
/// 상공에서 자기 최종 위치로 낙하하고 착지 바운스로 자리잡는다.
///
/// 사용 흐름 (GameRunBootstrapper):
///   1) MapBuilder.Build 직후 Prewarm(wallBlocks) — 벽을 즉시 상공에 배치해 main 연출 중
///      최종 위치에 노출되지 않도록 함
///   2) mainBlocks 에 기본 IMapEntrance 실행
///   3) wallBlocks 에 WallDropEntrance.PlayAsync 실행 — row 순차 낙하
///   4) NavMesh 빌드
/// </summary>
public sealed class WallDropEntrance : IMapEntrance
{
    public  const float DropHeight   = 18f;   // 낙하 시작 높이 (카메라 밖 위쪽 충분히)
    private const float FallDuration = 0.55f; // 개별 벽의 낙하 시간 (높이에 비례해 살짝 늘림)
    private const float BounceTime   = 0.22f; // 착지 바운스 시간
    private const float BounceAmp    = 0.25f; // 바운스 진폭
    private const float RowDelay     = 0.035f;// row(z) 간 순차 지연

    /// <summary>
    /// 메인 연출 전에 호출 — 벽들을 상공으로 선배치하고 Collider 비활성화.
    /// 호출 시점 이후 프레임이 렌더되기 전에 최종 위치에서 상공 위치로 이동하므로,
    /// 플레이어는 벽이 처음부터 "위에서 내려오는" 모습만 보게 된다.
    /// </summary>
    public static void Prewarm(IReadOnlyList<MapBuilder.PlacedBlock> walls)
    {
        if (walls == null) return;
        for (int i = 0; i < walls.Count; i++)
        {
            var b = walls[i];
            if (b.instance == null) continue;
            b.instance.transform.position = b.targetPosition + new Vector3(0f, DropHeight, 0f);
            b.instance.transform.rotation = Quaternion.Euler(0f, b.targetRotationY, 0f);
            MapEntranceUtil.SetCollidersEnabled(b.instance, false);
        }
    }

    public async UniTask PlayAsync(
        IReadOnlyList<MapBuilder.PlacedBlock> blocks,
        MapEntranceContext ctx,
        CancellationToken ct)
    {
        if (blocks == null || blocks.Count == 0) return;

        int n = blocks.Count;
        var starts  = new Vector3[n];
        var targets = new Vector3[n];
        var delays  = new float[n];

        // z축(cell.y) 범위 계산 — 뒤(큰 z)부터 앞(작은 z) 순으로 순차 낙하
        int minZ = int.MaxValue, maxZ = int.MinValue;
        for (int i = 0; i < n; i++)
        {
            int z = blocks[i].cell.y;
            if (z < minZ) minZ = z;
            if (z > maxZ) maxZ = z;
        }

        for (int i = 0; i < n; i++)
        {
            var b = blocks[i];
            if (b.instance == null) continue;

            targets[i] = b.targetPosition;
            starts[i]  = targets[i] + new Vector3(0f, DropHeight, 0f);
            delays[i]  = (maxZ - b.cell.y) * RowDelay; // 뒤쪽 row가 먼저 떨어짐

            // 선배치 누락 시 방어 — Prewarm이 호출되지 않았더라도 첫 프레임에 상공으로 올린다
            b.instance.transform.position = starts[i];
            b.instance.transform.rotation = Quaternion.Euler(0f, b.targetRotationY, 0f);
            MapEntranceUtil.SetCollidersEnabled(b.instance, false);
        }

        float maxDelay = (maxZ - minZ) * RowDelay;
        float total    = maxDelay + FallDuration + BounceTime;

        float t = 0f;
        while (t < total)
        {
            ct.ThrowIfCancellationRequested();
            t += Time.deltaTime;

            for (int i = 0; i < n; i++)
            {
                var b = blocks[i];
                if (b.instance == null) continue;

                float localT = t - delays[i];
                if (localT < 0f)
                {
                    // 아직 차례 전 — 상공 대기
                    b.instance.transform.position = starts[i];
                    continue;
                }

                if (localT < FallDuration)
                {
                    // 낙하: ease-in (가속)
                    float k = localT / FallDuration;
                    float eased = k * k;
                    b.instance.transform.position = Vector3.LerpUnclamped(starts[i], targets[i], eased);
                }
                else if (localT < FallDuration + BounceTime)
                {
                    // 착지 바운스 (감쇠 사인)
                    float bt = (localT - FallDuration) / BounceTime;
                    float bounce = Mathf.Sin(bt * Mathf.PI) * BounceAmp * (1f - bt);
                    b.instance.transform.position = targets[i] + new Vector3(0f, -bounce, 0f);
                }
                else
                {
                    b.instance.transform.position = targets[i];
                }
            }

            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }

        // 정착 + Collider 활성화
        for (int i = 0; i < n; i++)
        {
            var b = blocks[i];
            if (b.instance == null) continue;
            b.instance.transform.position = targets[i];
            MapEntranceUtil.SetCollidersEnabled(b.instance, true);
        }
    }
}
