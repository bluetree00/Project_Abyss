using UnityEngine;

/// <summary>
/// B 토큰 처리 — ActivePalette에서 BossSpawn BlockDef를 꺼내 보스 스포너를 배치한다.
/// MapBuilder는 BossSpawn 셀에 Floor만 깔고, 실제 스포너는 이 핸들러가 담당한다.
/// </summary>
[TokenHandler("B", TokenCategory.Spawn, "보스 스포너 — ActivePalette의 BossSpawn BlockDef 사용",
    csvExample: "B\n(방당 1개, 바닥은 MapBuilder가 별도 처리)")]
public sealed class BossSpawnHandler : ITokenHandler
{
    public void Execute(TokenContext ctx)
    {
        if (ctx.ActivePalette == null)
        {
            Debug.LogWarning("[BossSpawnHandler] ActivePalette 미할당 — 보스 스포너 미배치");
            return;
        }

        var def = ctx.ActivePalette.Pick(TileType.BossSpawn, ctx.Rng);
        if (def == null || def.prefab == null)
        {
            Debug.LogWarning($"[BossSpawnHandler] 팔레트에 BossSpawn BlockDef 없음 — 미배치 (palette={ctx.ActivePalette.name})");
            return;
        }

        var go = Object.Instantiate(def.prefab, ctx.WorldPos, Quaternion.identity, ctx.Parent);
        go.name = $"BossSpawner_{ctx.Cell.x}_{ctx.Cell.y}";
    }
}
