using UnityEngine;

/// <summary>
/// MB&lt;종류&gt; 토큰 처리(10-12 정예 보스 「관문지기」) — 정예 보스 스포너를 하나 놓는다.
/// 스폰 표 뽑기 · 수량 배율 · 시기 파도와 무관하게 늘 1마리다: 웨이브를 [정예 × 1]로 잡고(레거시 자동 루프를 막는다)
/// <see cref="MidBossSpawnMarker"/>를 붙여 <see cref="RoomWaveController"/>가 <see cref="MonsterSpawner.SpawnMidBossAsync"/>를 부르게 한다.
/// 웨이브 컨트롤러가 표식을 모르는 판이어도 스포너가 정예 1마리를 뽑아 작은 정예방처럼 끝난다(파손 없음).
/// </summary>
[TokenHandler("MB", TokenCategory.Spawn, "정예 보스 스포너 — 종류 이름이 붙는다(MidBoss 방에 정확히 1개 · M/m 없이)", isPrefix: true, phase: TokenPhase.PreBuild,
    csvExample: "MBBullTank\n(주소 BullTank/BullTank_Ch1~4 — 챕터는 스포너가 고른다)")]
public sealed class MidBossSpawnHandler : ITokenHandler
{
    private static readonly MapDataLoader.WaveCellConfig[] SingleElite =
    {
        new MapDataLoader.WaveCellConfig { grade = MonsterGrade.Elite, count = 1 },
    };

    public void Execute(TokenContext ctx)
    {
        string kind = ctx.RawToken.Length > 2 ? ctx.RawToken.Substring(2) : null;
        if (string.IsNullOrEmpty(kind))
        {
            Debug.LogWarning($"[MidBossSpawnHandler] 종류 없는 MB 토큰 — 미배치 ({ctx.Cell})");
            return;
        }
        if (ctx.ActivePalette == null)
        {
            Debug.LogWarning($"[MidBossSpawnHandler] ActivePalette 미할당 — 정예 보스 스포너 미배치 ({ctx.Cell})");
            return;
        }

        var def = ctx.ActivePalette.Pick(TileType.MonsterSpawn, ctx.Rng);
        if (def == null || def.prefab == null)
        {
            Debug.LogWarning($"[MidBossSpawnHandler] 팔레트에 MonsterSpawn BlockDef 없음 — 미배치 ({ctx.Cell})");
            return;
        }

        var go = Object.Instantiate(def.prefab, ctx.WorldPos, Quaternion.identity, ctx.Parent);
        go.name = $"MidBossSpawner_{kind}_{ctx.Cell.x}_{ctx.Cell.y}";

        if (!go.TryGetComponent<MonsterSpawner>(out var spawner)) return;

        // 내부 필드 경계 주입 — 스폰이 게이트/복도로 새지 않게 방 안으로 제한
        if (ctx.FieldBounds.HasValue)
            spawner.SetFieldBounds(ctx.FieldBounds.Value);

        spawner.ConfigureWaves(SingleElite);
        go.AddComponent<MidBossSpawnMarker>().Init(kind);

        // 입장 연출 전까지 비활성화 — Start() 호출을 연출 종료 이후로 지연(M 토큰과 같다)
        spawner.enabled = false;
        ctx.DeferredSpawners?.Add(spawner);
    }
}
