using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// M / M0 / M1 … 토큰 처리 — 확정 몬스터 스포너를 배치한다.
/// ApplyMonsterSpawnerPlan이 먼저 그리드를 수정하므로, ctx.Grid[cell] == MonsterSpawn인 셀만 처리.
/// </summary>
[TokenHandler("M", TokenCategory.Spawn, "몬스터 스포너 (확정) — max 제한과 무관하게 항상 배치", isPrefix: true, phase: TokenPhase.PreBuild,
    csvExample: "M  / Mc3  / Mr2  / Me1  / Mc3r1e1\n(등급: c=Common r=Rare e=Elite, 숫자=마릿수)")]
public sealed class MonsterSpawnHandler : ITokenHandler
{
    public void Execute(TokenContext ctx) => MonsterSpawnUtil.Execute(ctx);
}

/// <summary>
/// m / m0 / m1 … 토큰 처리 — 후보 몬스터 스포너를 배치한다.
/// ApplyMonsterSpawnerPlan이 선택된 후보는 MonsterSpawn으로, 나머지는 Floor로 치환하므로
/// ctx.Grid[cell] == MonsterSpawn인 셀(선택된 후보)만 실제로 스포너를 배치한다.
/// </summary>
[TokenHandler("m", TokenCategory.Spawn, "몬스터 스포너 (후보) — max_active_spawners 초과분은 자동 비활성", isPrefix: true, phase: TokenPhase.PreBuild,
    csvExample: "m  / mc3  / mr2  / me1  / mc3r1e1\n(max 한도 내에서만 랜덤 선택됨)")]
public sealed class MonsterSpawnCandidateHandler : ITokenHandler
{
    public void Execute(TokenContext ctx) => MonsterSpawnUtil.Execute(ctx);
}

internal static class MonsterSpawnUtil
{
    public static void Execute(TokenContext ctx)
    {
        // ApplyMonsterSpawnerPlan이 비활성화한 후보 셀은 Floor로 치환됨 → 스킵
        if (ctx.Grid == null || ctx.Grid[ctx.Cell.x, ctx.Cell.y] != TileType.MonsterSpawn) return;

        if (ctx.ActivePalette == null)
        {
            Debug.LogWarning($"[MonsterSpawnHandler] ActivePalette 미할당 — 스포너 미배치 ({ctx.Cell})");
            return;
        }

        var def = ctx.ActivePalette.Pick(TileType.MonsterSpawn);
        if (def == null || def.prefab == null)
        {
            Debug.LogWarning($"[MonsterSpawnHandler] 팔레트에 MonsterSpawn BlockDef 없음 — 미배치 ({ctx.Cell})");
            return;
        }

        var go = Object.Instantiate(def.prefab, ctx.WorldPos, Quaternion.identity, ctx.Parent);
        go.name = $"MonsterSpawner_{ctx.Cell.x}_{ctx.Cell.y}";

        var spawner = go.GetComponent<MonsterSpawner>();
        if (spawner == null) return;

        // 내부 필드 경계 주입 — 스폰이 게이트/복도로 새지 않게 방 안으로 제한
        if (ctx.FieldBounds.HasValue)
            spawner.SetFieldBounds(ctx.FieldBounds.Value);

        // 웨이브 설정 주입
        if (ctx.SpawnInfos != null && ctx.SpawnInfos.TryGetValue(ctx.Cell, out var info))
        {
            if (info.waves != null && info.waves.Length >= 2)
                spawner.ConfigureWaves(info.waves);
            else
                spawner.Configure(info.maxGrade, info.totalCount);
        }

        // 입장 연출 전까지 비활성화 — Start() 호출을 연출 종료 이후로 지연
        spawner.enabled = false;
        ctx.DeferredSpawners?.Add(spawner);
    }
}
