using UnityEngine;

/// <summary>
/// CV 토큰 처리 — 월드 서약 제단(WorldCovenantPickup)을 배치한다.
/// 플레이어가 범위 안에서 F를 누르면 CovenantChoiceUI 3지선다가 열려 서약을 획득한다.
/// 이벤트방의 "작은 안전방 + 서약 제단" 구성을 위한 토큰.
/// </summary>
[TokenHandler("CV", TokenCategory.Special, "서약 제단 — WorldCovenantPickup 스폰(F 상호작용 → 서약 3지선다)",
    csvExample: "CV\n(셀은 Floor로 깔리고 그 위에 서약 제단 오브가 스폰됨)")]
public sealed class CovenantAltarHandler : ITokenHandler
{
    public void Execute(TokenContext ctx)
    {
        var pos = new Vector3(ctx.WorldPos.x, ctx.BaseY, ctx.WorldPos.z);
        var pickup = WorldCovenantPickup.SpawnAt(pos);
        if (pickup != null)
            pickup.name = $"CovenantAltar_{ctx.Cell.x}_{ctx.Cell.y}";
    }
}
