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
    private readonly float _staggerInterval;
    private readonly float _duration;

    public DissolveEntrance(float staggerInterval = 0.045f, float duration = 0.32f)
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
                    System.TimeSpan.FromSeconds(_staggerInterval), cancellationToken: ct);
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
