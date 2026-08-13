using UnityEngine;

/// <summary>
/// NS 토큰 — 서비스 NPC(상인·재련공·정제사)가 설 자리.
///
/// 예전에는 위치를 CSV가 정하지 못했다. <c>ResolveNpcPlacement</c>가
/// ①<see cref="ShopNpcAnchor"/> → ②매대 중심 → ③<b>방 중심</b> 순으로 폴백하는데,
/// 재련소·정제소 격자에는 매대 타일조차 없어 <b>항상 ③</b>으로 떨어졌다.
/// 그래서 NPC가 텅 빈 방 정중앙에 홀로 섰다(플레이어 스폰에서 8칸 남쪽).
/// 이 토큰이 ①을 CSV로 만들어 준다 — 컨트롤러 코드는 그대로 두고 앵커만 심는다.
///
/// 방향: 기본은 방 중심을 본다. 접미사로 고정할 수 있다 — <c>NSn/NSs/NSe/NSw</c>(북/남/동/서).
/// 방 회전(heading)·미러는 부모 트랜스폼에 걸리므로 접미사 방향은 방 기준으로 유지된다.
/// </summary>
[TokenHandler("NS", TokenCategory.Special,
    "서비스 NPC 자리 — 상인/재련공/정제사가 여기 선다. 기본은 방 중심을 봄",
    isPrefix: true, phase: TokenPhase.PostBuild,
    csvExample: "NS  / NSn  / NSs  / NSe  / NSw\n(n=북 s=남 e=동 w=서. 접미사 없으면 방 중심을 향함)")]
public sealed class ServiceNpcAnchorHandler : ITokenHandler
{
    public void Execute(TokenContext ctx)
    {
        var pos = new Vector3(ctx.WorldPos.x, ctx.BaseY, ctx.WorldPos.z);

        var go = new GameObject($"ServiceNpcAnchor_{ctx.Cell.x}_{ctx.Cell.y}");
        go.transform.SetParent(ctx.Parent, false);
        go.transform.position = pos;
        go.transform.rotation = ResolveRotation(ctx, pos);
        go.AddComponent<ShopNpcAnchor>();
    }

    /// <summary>접미사(n/s/e/w)가 있으면 그 방향, 없으면 방 중심을 본다.
    /// 방 중심조차 같은 지점이면(중앙 배치) 회전을 건드리지 않는다.</summary>
    private static Quaternion ResolveRotation(TokenContext ctx, Vector3 pos)
    {
        string tok = ctx.RawToken ?? string.Empty;
        if (tok.Length > 2)
        {
            switch (char.ToLowerInvariant(tok[2]))
            {
                case 'n': return Quaternion.LookRotation(Vector3.forward);
                case 's': return Quaternion.LookRotation(Vector3.back);
                case 'e': return Quaternion.LookRotation(Vector3.right);
                case 'w': return Quaternion.LookRotation(Vector3.left);
            }
        }

        // 방 원점 = 격자 중심(MapBuilder는 중심 기준으로 블록을 깐다).
        Vector3 center = ctx.Parent != null ? ctx.Parent.position : pos;
        Vector3 dir = center - pos;
        dir.y = 0f;
        return dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir.normalized) : Quaternion.identity;
    }
}

/// <summary>
/// NC 토큰 — 판매대·작업대 자리. <c>decorPrefabs[0]</c>이 여기 놓인다.
/// 앵커가 하나도 없으면 컨트롤러가 기존 "NPC 정면 탐색"으로 폴백한다.
/// </summary>
[TokenHandler("NC", TokenCategory.Special,
    "판매대/작업대 자리 — 서비스 방 decorPrefabs[0]이 배치됨",
    phase: TokenPhase.PostBuild,
    csvExample: "NC\n(NS와 함께 쓴다. 보통 NPC와 플레이어 사이에 둔다)")]
public sealed class ServiceCounterAnchorHandler : ITokenHandler
{
    public void Execute(TokenContext ctx) =>
        ServiceAnchorUtil.Spawn(ctx, ServiceDecorAnchor.Slot.Counter, "ServiceCounterAnchor");
}

/// <summary>
/// NP 토큰 — 배경 소품 자리(여러 개 가능). <c>decorPrefabs[1]</c> 이후가 배치 순서대로 채운다.
/// </summary>
[TokenHandler("NP", TokenCategory.Special,
    "배경 소품 자리 — 서비스 방 decorPrefabs[1] 이후가 순서대로 배치됨",
    phase: TokenPhase.PostBuild,
    csvExample: "NP\n(여러 개 놓을 수 있다. 셀 순서대로 소품이 채워진다)")]
public sealed class ServicePropAnchorHandler : ITokenHandler
{
    public void Execute(TokenContext ctx) =>
        ServiceAnchorUtil.Spawn(ctx, ServiceDecorAnchor.Slot.Prop, "ServicePropAnchor");
}

internal static class ServiceAnchorUtil
{
    public static void Spawn(TokenContext ctx, ServiceDecorAnchor.Slot kind, string namePrefix)
    {
        var go = new GameObject($"{namePrefix}_{ctx.Cell.x}_{ctx.Cell.y}");
        go.transform.SetParent(ctx.Parent, false);
        go.transform.position = new Vector3(ctx.WorldPos.x, ctx.BaseY, ctx.WorldPos.z);
        go.AddComponent<ServiceDecorAnchor>().SetKind(kind);
    }
}
