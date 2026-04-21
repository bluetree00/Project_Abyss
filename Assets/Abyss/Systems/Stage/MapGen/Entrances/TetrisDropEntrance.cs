using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// TetrisDrop. 수직 드롭 + row별 순차 + 착지 바운스.
/// "딱딱 맞춰지는" 정돈된 모임 연출.
/// scatter_range를 dropHeight로 재해석.
/// </summary>
public sealed class TetrisDropEntrance : IMapEntrance
{
    private const float DefaultDropHeight = 15f;
    private const float RowDelay = 0.04f;
    private const float FallDuration = 0.35f;
    private const float BounceTime = 0.1f;
    private const float BounceAmp = 0.15f;

    public async UniTask PlayAsync(
        IReadOnlyList<MapBuilder.PlacedBlock> blocks,
        MapEntranceContext ctx,
        CancellationToken ct)
    {
        if (blocks == null || blocks.Count == 0) return;

        int n = blocks.Count;
        float dropHeight = ctx.ScatterRange > 0f ? ctx.ScatterRange : DefaultDropHeight;

        // row 범위(cell.y) 계산 → 순차 delay 기준
        int minY = int.MaxValue, maxY = int.MinValue;
        for (int i = 0; i < n; i++)
        {
            int y = blocks[i].cell.y;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        var starts = new Vector3[n];
        var targets = new Vector3[n];
        var delays = new float[n];

        for (int i = 0; i < n; i++)
        {
            var b = blocks[i];
            if (b.instance == null) continue;

            targets[i] = b.targetPosition;
            starts[i] = b.targetPosition + new Vector3(0f, dropHeight, 0f);
            // 뒤쪽(cell.y 큰) row부터 떨어지게
            delays[i] = (maxY - b.cell.y) * RowDelay;

            b.instance.transform.position = starts[i];
            b.instance.transform.rotation = Quaternion.Euler(0f, b.targetRotationY, 0f);
            MapEntranceUtil.SetCollidersEnabled(b.instance, false);
        }

        float maxDelay = (maxY - minY) * RowDelay;
        float totalDuration = maxDelay + FallDuration + BounceTime;

        float t = 0f;
        while (t < totalDuration)
        {
            ct.ThrowIfCancellationRequested();
            t += Time.deltaTime;

            for (int i = 0; i < n; i++)
            {
                var b = blocks[i];
                if (b.instance == null) continue;

                float localT = t - delays[i];
                if (localT < 0f) continue;

                if (localT < FallDuration)
                {
                    // 낙하: ease-in (가속)
                    float k = localT / FallDuration;
                    float eased = k * k;
                    b.instance.transform.position = Vector3.Lerp(starts[i], targets[i], eased);
                    b.instance.transform.localScale = Vector3.one;
                }
                else if (localT < FallDuration + BounceTime)
                {
                    // 착지 바운스
                    float k = (localT - FallDuration) / BounceTime;
                    float wave = Mathf.Sin(k * Mathf.PI);
                    float squashY = 1f - BounceAmp * wave;
                    float stretchXZ = 1f + BounceAmp * 0.3f * wave;

                    b.instance.transform.position = targets[i];
                    b.instance.transform.localScale = new Vector3(stretchXZ, squashY, stretchXZ);
                }
                else
                {
                    b.instance.transform.position = targets[i];
                    b.instance.transform.localScale = Vector3.one;
                }
            }

            await UniTask.Yield(ct);
        }

        // 최종 고정 + 콜라이더 활성화
        for (int i = 0; i < n; i++)
        {
            var b = blocks[i];
            if (b.instance == null) continue;
            b.instance.transform.position = targets[i];
            b.instance.transform.rotation = Quaternion.Euler(0f, b.targetRotationY, 0f);
            b.instance.transform.localScale = Vector3.one;
            MapEntranceUtil.SetCollidersEnabled(b.instance, true);
        }
    }
}
