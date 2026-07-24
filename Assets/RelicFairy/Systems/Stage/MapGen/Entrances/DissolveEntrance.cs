using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 대각선(cell.x + cell.y) 방향으로 한 줄씩 동시 디졸브 등장.
/// 같은 대각선의 타일은 동시에 나타나고, 대각선 간격으로 파도가 이동한다.
/// </summary>
public sealed class DissolveEntrance : IMapEntrance
{
    /// <summary>
    /// 파도(대각선 순차 등장) 전체에 허용하는 시간 상한(초).
    ///
    /// 예전에는 <c>대각선 그룹 수 × staggerInterval</c>이 그대로 총 시간이라 <b>방이 클수록 선형으로 길어졌다</b>
    /// (40×40 방이면 대각선 80줄 × 0.045 ≈ 3.6초). 방 진입마다 그만큼 기다려야 해서 답답했다.
    /// 간격을 무작정 줄이면 작은 방에서는 파도가 사라지므로, <b>총량에 상한</b>을 두고
    /// 큰 방에서만 간격이 자동으로 촘촘해지게 한다.
    /// </summary>
    private const float MaxWaveSeconds = 1.0f;

    private readonly float _staggerInterval;
    private readonly float _duration;

    public DissolveEntrance(float staggerInterval = 0.045f, float duration = 0.28f)
    {
        _staggerInterval = staggerInterval;
        _duration        = duration;
    }

    public async UniTask PlayAsync(
        IReadOnlyList<MapBuilder.PlacedBlock> blocks,
        MapEntranceContext ctx,
        CancellationToken ct)
    {
        if (blocks == null || blocks.Count == 0) return;

        // 대각선(x+z) 기준으로 그룹화
        var groups = new SortedDictionary<int, List<(GameObject tile, Renderer[] rs)>>();

        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            if (b.instance == null) continue;

            b.instance.transform.position = b.targetPosition;
            b.instance.transform.rotation = Quaternion.Euler(0f, b.targetRotationY, 0f);
            MapEntranceUtil.SetCollidersEnabled(b.instance, true);

            var rs = b.instance.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) continue;   // MonsterSpawner 등 Renderer 없는 기능 오브젝트 스킵

            foreach (var r in rs) r.enabled = false;   // 선로드 전까지 숨김

            int diagKey = b.cell.x + b.cell.y;
            if (!groups.TryGetValue(diagKey, out var list))
            {
                list = new List<(GameObject, Renderer[])>();
                groups[diagKey] = list;
            }
            list.Add((b.instance, rs));
        }

        try
        {
            // 머티리얼 선로드 — 이후 타일별 캐시 히트로 pop-in 없음
            await DissolveEffect.WarmupAsync(ct);

            // 방 크기와 무관하게 파도 총량을 MaxWaveSeconds 안에 가둔다(작은 방은 원래 간격 유지).
            float stagger = groups.Count > 0
                ? Mathf.Min(_staggerInterval, MaxWaveSeconds / groups.Count)
                : _staggerInterval;

            foreach (var kvp in groups)
            {
                ct.ThrowIfCancellationRequested();

                // 같은 대각선의 타일 전부 동시에 등장
                foreach (var (tile, rs) in kvp.Value)
                {
                    foreach (var r in rs)
                        if (r != null) r.enabled = true;

                    DissolveEffect.PlayAppearAsync(tile, _duration, ct).Forget();
                }

                await UniTask.Delay(
                    System.TimeSpan.FromSeconds(stagger), cancellationToken: ct);
            }

            // 마지막 대각선의 디졸브가 끝날 때까지 대기
            await UniTask.Delay(
                System.TimeSpan.FromSeconds(_duration), cancellationToken: ct);
        }
        catch (System.OperationCanceledException)
        {
            foreach (var kvp in groups)
                foreach (var (_, rs) in kvp.Value)
                    foreach (var r in rs)
                        if (r != null) r.enabled = true;
        }
    }
}
