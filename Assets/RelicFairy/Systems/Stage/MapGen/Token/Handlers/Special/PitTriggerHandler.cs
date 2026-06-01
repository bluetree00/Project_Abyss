using UnityEngine;

/// <summary>
/// Pt 토큰 처리 — 구멍 위치에 즉시 낙사 감지 트리거를 배치한다.
/// FallRecoveryController의 Y 임계값 방식보다 즉각적으로 낙사를 감지한다.
/// MapDataLoader에서 "Pt" → TileType.Empty로 매핑되므로 바닥 블록은 생성되지 않는다.
/// </summary>
[TokenHandler("Pt", TokenCategory.Special,
    "낙사 즉시 트리거 — 구멍 진입 즉시 FallRecoveryController 발동",
    phase: TokenPhase.PostBuild,
    csvExample: "Pt")]
public sealed class PitTriggerHandler : ITokenHandler
{
    public void Execute(TokenContext ctx)
    {
        var go = new GameObject($"PitTrigger_{ctx.Cell.x}_{ctx.Cell.y}");
        go.transform.SetParent(ctx.Parent, false);

        // 바닥 레벨(baseY)에 배치 — 플레이어가 구멍으로 한 발만 딛어도 감지
        go.transform.position = new Vector3(ctx.WorldPos.x, ctx.BaseY, ctx.WorldPos.z);

        var col = go.AddComponent<BoxCollider>();
        col.size   = new Vector3(ctx.CellSize * 0.85f, 0.3f, ctx.CellSize * 0.85f);
        col.center = Vector3.zero;
        col.isTrigger = true;

        go.AddComponent<PitTrigger>();
    }
}
