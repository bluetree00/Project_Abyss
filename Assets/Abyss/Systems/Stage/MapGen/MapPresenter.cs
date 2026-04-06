using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 맵 등장 연출: 투명바닥 → Scatter → Return → 바닥 제거.
/// RuntimeMapGeneratorSolo의 연출 로직을 재활용.
/// </summary>
public class MapPresenter
{
    /// <summary>
    /// 블록 등장 연출 실행.
    /// 완료될 때까지 await.
    /// </summary>
    public static async UniTask PlayEntrance(
        List<MapBuilder.PlacedBlock> blocks,
        float scatterRange = 10f,
        float returnDuration = 1.2f,
        float travelJitter = 0.05f,
        float curveBend = 1f)
    {
        if (blocks == null || blocks.Count == 0) return;

        int n = blocks.Count;

        // 그리드 중심 계산
        Vector3 center = Vector3.zero;
        foreach (var b in blocks) center += b.targetPosition;
        center /= n;

        // 1. 모든 블록을 랜덤 위치로 흩뿌리기
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

            // 흩뿌리기 위치
            Vector3 scattered = center + RandomInBox(scatterRange);
            starts[i] = scattered;
            startRots[i] = Random.Range(0f, 360f);

            b.instance.transform.position = scattered;
            b.instance.transform.rotation = Quaternion.Euler(0, startRots[i], 0);

            // 이동 중 콜라이더 비활성화
            SetCollidersEnabled(b.instance, false);

            // 베지어 제어점
            Vector3 mid = (starts[i] + targets[i]) * 0.5f;
            Vector3 bend = RandomInsideRadiusXZ(curveBend);
            bend.y = Random.Range(-curveBend * 0.25f, curveBend * 0.25f);
            controlA[i] = mid + bend;
            controlB[i] = mid - bend * 0.6f;
        }

        // 2. 베지어 곡선 + 노이즈로 복귀 애니메이션
        float t = 0f;
        float jitterTimeScale = 1f;

        while (t < returnDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / returnDuration);
            float eased = k * k * (3f - 2f * k); // SmoothStep

            float noiseT = Time.time * jitterTimeScale;

            for (int i = 0; i < n; i++)
            {
                var b = blocks[i];
                if (b.instance == null) continue;

                // 위치: 베지어4
                Vector3 p = Bezier4(starts[i], controlA[i], controlB[i], targets[i], eased);

                // 흔들림
                if (travelJitter > 0f)
                {
                    float jx = (Mathf.PerlinNoise(i * 0.37f, noiseT) - 0.5f) * 2f;
                    float jy = (Mathf.PerlinNoise(i * 0.51f, noiseT) - 0.5f) * 2f;
                    float jz = (Mathf.PerlinNoise(i * 0.73f, noiseT) - 0.5f) * 2f;
                    p.x += jx * travelJitter;
                    p.y += jy * (travelJitter * 0.5f);
                    p.z += jz * travelJitter;
                }

                b.instance.transform.position = p;

                // 회전 보간
                float rotY = Mathf.LerpAngle(startRots[i], targetRots[i], eased);
                b.instance.transform.rotation = Quaternion.Euler(0, rotY, 0);
            }

            await UniTask.Yield();
        }

        // 3. 최종 위치/회전 고정 + 콜라이더 활성화
        for (int i = 0; i < n; i++)
        {
            var b = blocks[i];
            if (b.instance == null) continue;
            b.instance.transform.position = targets[i];
            b.instance.transform.rotation = Quaternion.Euler(0, targetRots[i], 0);
            SetCollidersEnabled(b.instance, true);
        }
    }

    // ── 콜라이더 제어 ──

    private static void SetCollidersEnabled(GameObject go, bool enabled)
    {
        foreach (var col in go.GetComponentsInChildren<Collider>(true))
            col.enabled = enabled;
    }

    // ── 유틸 (RuntimeMapGeneratorSolo에서 가져옴) ──

    private static Vector3 RandomInBox(float range)
    {
        return new Vector3(
            Random.Range(-range, range),
            Random.Range(-range * 0.4f, range * 0.6f),
            Random.Range(-range, range));
    }

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
