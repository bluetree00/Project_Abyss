using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// 보스용 연출 stub. 보스 좌표(B 타일) 기점으로 충격파가 퍼지며 블록 등장.
///
/// TODO:
///   1. blocks 중 tileType == BossSpawn 위치 추출 (없으면 그리드 중심 폴백)
///   2. 각 블록의 epicenter 대비 XZ 거리 계산 → 거리순 delay
///   3. Drop + Shockwave ripple 결합 (위에서 떨어지되 바깥 링부터 순차)
///   4. 충격파 도달 시 카메라 흔들림/포스트프로세스 펄스/사운드 훅
/// </summary>
public sealed class ShockwaveEntrance : IMapEntrance
{
    // 임시: Scatter 로 폴백
    private readonly ScatterEntrance _fallback = new ScatterEntrance();

    public UniTask PlayAsync(
        IReadOnlyList<MapBuilder.PlacedBlock> blocks,
        MapEntranceContext ctx,
        CancellationToken ct)
        => _fallback.PlayAsync(blocks, ctx, ct);
}
