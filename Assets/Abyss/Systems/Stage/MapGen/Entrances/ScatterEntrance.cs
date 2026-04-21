using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Scatter. 사방 랜덤 위치에서 Bezier4 + Perlin 지터로 복귀.
/// 기존 MapPresenter.PlayEntrance 로직을 그대로 이관.
/// </summary>
public sealed class ScatterEntrance : IMapEntrance
{
    private const float TravelJitter = 0.05f;
    private const float CurveBend = 1f;

    public async UniTask PlayAsync(
        IReadOnlyList<MapBuilder.PlacedBlock> blocks,
        MapEntranceContext ctx,
        CancellationToken ct)
    {
        if (blocks == null || blocks.Count == 0) return;

        float scatterRange = ctx.ScatterRange;
        float returnDuration = ctx.ReturnDuration;

        int n = blocks.Count;

        Vector3 center = Vector3.zero;
        for (int i = 0; i < n; i++) center += blocks[i].targetPosition;
        center /= n;

        var starts = new Vector3[n];
        var targets = new Vector3[n];
        var startRots = new float[n];
        var targetRots = new float[n];
        var controlA = new Vector3[n];
        var controlB = new Vector3[n];

        for (int i = 0; i < n; i++)
        {
            var b = blocks[i];
            targets[i] = b.targetPosition;
            targetRots[i] = b.targetRotationY;

            Vector3 scattered = center + RandomInBox(scatterRange);
            starts[i] = scattered;
            startRots[i] = Random.Range(0f, 360f);

            b.instance.transform.position = scattered;
            b.instance.transform.rotation = Quaternion.Euler(0f, startRots[i], 0f);
            MapEntranceUtil.SetCollidersEnabled(b.instance, false);

            Vector3 mid = (starts[i] + targets[i]) * 0.5f;
            Vector3 bend = RandomInsideRadiusXZ(CurveBend);
            bend.y = Random.Range(-CurveBend * 0.25f, CurveBend * 0.25f);
            controlA[i] = mid + bend;
            controlB[i] = mid - bend * 0.6f;
        }

        float t = 0f;
        while (t < returnDuration)
        {
            ct.ThrowIfCancellationRequested();
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / returnDuration);
            float eased = k * k * (3f - 2f * k);
            float noiseT = Time.time;

            for (int i = 0; i < n; i++)
            {
                var b = blocks[i];
                if (b.instance == null) continue;

                Vector3 p = Bezier4(starts[i], controlA[i], controlB[i], targets[i], eased);

                if (TravelJitter > 0f)
                {
                    float jx = (Mathf.PerlinNoise(i * 0.37f, noiseT) - 0.5f) * 2f;
                    float jy = (Mathf.PerlinNoise(i * 0.51f, noiseT) - 0.5f) * 2f;
                    float jz = (Mathf.PerlinNoise(i * 0.73f, noiseT) - 0.5f) * 2f;
                    p.x += jx * TravelJitter;
                    p.y += jy * (TravelJitter * 0.5f);
                    p.z += jz * TravelJitter;
                }

                b.instance.transform.position = p;

                float rotY = Mathf.LerpAngle(startRots[i], targetRots[i], eased);
                b.instance.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
            }

            await UniTask.Yield(ct);
        }

        for (int i = 0; i < n; i++)
        {
            var b = blocks[i];
            if (b.instance == null) continue;
            b.instance.transform.position = targets[i];
            b.instance.transform.rotation = Quaternion.Euler(0f, targetRots[i], 0f);
            MapEntranceUtil.SetCollidersEnabled(b.instance, true);
        }
    }

    private static Vector3 RandomInBox(float range) => new Vector3(
        Random.Range(-range, range),
        Random.Range(-range * 0.4f, range * 0.6f),
        Random.Range(-range, range));

    private static Vector3 RandomInsideRadiusXZ(float r)
    {
        Vector2 v = Random.insideUnitCircle * r;
        return new Vector3(v.x, 0f, v.y);
    }

    private static Vector3 Bezier4(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float u = 1f - t;
        return (u * u * u) * p0
             + (3f * u * u * t) * p1
             + (3f * u * t * t) * p2
             + (t * t * t) * p3;
    }
}
