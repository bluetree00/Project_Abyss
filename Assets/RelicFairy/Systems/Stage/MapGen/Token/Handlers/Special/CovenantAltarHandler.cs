using UnityEngine;

/// <summary>
/// CV 토큰 처리 — 월드 조립 서약 제단(WorldCovenantAltar)을 배치한다.
/// 플레이어가 범위 안에서 F를 누르면 조립 서약 팝업(원인×효과)이 열려 서약을 벼려낸다.
/// (사전제작 3지선다 WorldCovenantPickup는 폐기 — 조립 서약으로 대체.)
/// </summary>
[TokenHandler("CV", TokenCategory.Special, "서약 제단 — WorldCovenantAltar 스폰(F 상호작용 → 조립 서약)",
    csvExample: "CV\n(셀은 Floor로 깔리고 그 위에 조립 서약 제단 오브가 스폰됨)")]
public sealed class CovenantAltarHandler : ITokenHandler
{
    public void Execute(TokenContext ctx)
    {
        var pos = new Vector3(ctx.WorldPos.x, ctx.BaseY, ctx.WorldPos.z);
        var altar = WorldCovenantAltar.SpawnAt(pos);
        if (altar != null)
            altar.name = $"CovenantAltar_{ctx.Cell.x}_{ctx.Cell.y}";
    }
}
