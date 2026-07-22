using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 보스방 클리어 시 아레나 바깥으로 뻗어나가는 '이어지는 길'을 깐다.
///
/// 기존 ChapterGate(허공의 사각 포탈)를 대체하는 연출. 포탈로 순간이동하는 대신
/// 벽이 열리고 길이 한 칸씩 뻗어나가 다음 방으로 걸어 나가는 흐름을 만든다.
/// 길 끝에는 기존 ChapterGate를 그대로 세워 전환 판정을 재사용한다(전환 로직 무수정).
///
/// 출구 방향 결정 순서:
///   1) 프리팹의 Exit 마커(이름이 "Exit"로 시작하는 자식) — 커스텀 아레나 규약과 동일
///   2) 폴백: PlayerSpawn 반대 방향(= 보스 뒤편)으로 바닥 경계까지 전진
/// 마커가 없어도 Ch1·Ch2·Ch4처럼 '남쪽 입구 → 북쪽 보스' 구조면 폴백만으로 올바르게 잡힌다.
/// </summary>
public sealed class BossExitPath : MonoBehaviour
{
    // ── Constants ──────────────────────────────────────────────
    private const float TileSize      = 1f;    // CorridorBridgeSpawner의 blockCellSize와 동일 규약
    private const int   PathWidth     = 5;     // 타일 수 (복도 너비와 동일)
    private const float PathLength    = 18f;   // 아레나 밖으로 뻗는 길이(m)
    private const float RevealStep    = 0.045f; // 한 행이 드러나는 간격(초) — 길이 뻗어나가는 연출
    private const float TileRiseHeight = 0.8f;  // 타일이 아래에서 솟아오르는 높이
    private const float TileRiseTime   = 0.25f;

    /// <summary>바닥 레이어 — MapBuilder가 방 바닥에 쓰는 값과 동일해야 한다(플레이어 접지 판정·카메라가 이 레이어를 본다).
    /// CreatePrimitive는 Default(0)로 생성되므로 반드시 덮어써야 길 위에서 접지가 성립한다.</summary>
    private const int   GroundLayer   = 3;

    /// <summary>폴백 타일 색 — 아레나 바닥 머티리얼을 못 구했을 때만 사용.</summary>
    private static readonly Color FallbackTileColor = new Color(0.32f, 0.30f, 0.28f, 1f);

    // ── Private fields ─────────────────────────────────────────
    private readonly List<Transform> _tiles = new List<Transform>();

    // ── Public Methods ─────────────────────────────────────────

    /// <summary>
    /// 보스방 클리어 시 호출. 아레나에서 출구 방향을 산출해 길을 깔고 끝에 챕터 게이트를 세운다.
    /// arena가 null이면(격자 폴백 방) 기존 동작대로 roomCenter에 게이트만 세운다.
    /// </summary>
    public static void Spawn(Vector3 roomCenter, Transform arena, CorridorStyleSO style)
    {
        if (arena == null)
        {
            // 커스텀 아레나가 아닌 방 — 길을 깔 기준 형상이 없다. 기존 게이트로 폴백.
            Debug.Log("[BossExitPath] 아레나 없음 — 챕터 게이트만 스폰(폴백)");
            ChapterGate.Spawn(roomCenter);
            return;
        }

        var go = new GameObject("@BossExitPath");
        go.transform.position = roomCenter;
        go.AddComponent<BossExitPath>().Build(roomCenter, arena, style);
    }

    // ── Private Methods ────────────────────────────────────────

    private void Build(Vector3 roomCenter, Transform arena, CorridorStyleSO style)
    {
        if (!ResolveExit(roomCenter, arena, out Vector3 exitPos, out Vector3 dir))
        {
            Debug.LogWarning("[BossExitPath] 출구 방향 산출 실패 — 챕터 게이트만 스폰(폴백)");
            ChapterGate.Spawn(roomCenter);
            return;
        }

        OpenWallAt(exitPos, dir, arena);

        Material tileMat = style?.floorTilePrefab != null ? null : SampleFloorMaterial(arena);
        BuildPathAsync(exitPos, dir, style, tileMat, this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>
    /// 출구 지점과 방향을 정한다. Exit 마커 우선, 없으면 PlayerSpawn 반대편 바닥 경계.
    /// </summary>
    private bool ResolveExit(Vector3 roomCenter, Transform arena, out Vector3 exitPos, out Vector3 dir)
    {
        exitPos = roomCenter;
        dir     = Vector3.forward;

        // 1) Exit 마커 — 커스텀 아레나 규약(GameRunBootstrapper.CollectArenaExitSlots와 동일)
        var all = arena.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            var t = all[i];
            if (t == arena || !t.name.StartsWith("Exit", StringComparison.OrdinalIgnoreCase)) continue;

            exitPos = t.position;
            dir     = Flatten(t.forward);
            return dir.sqrMagnitude > 0.01f;
        }

        // 2) 폴백 — 입구(PlayerSpawn) 반대 방향 = 보스 뒤편.
        var spawn = arena.Find("PlayerSpawn");
        if (spawn == null)
        {
            Debug.LogWarning("[BossExitPath] Exit 마커도 PlayerSpawn도 없음 — 방향을 정할 수 없다");
            return false;
        }

        dir = Flatten(roomCenter - spawn.position);
        if (dir.sqrMagnitude < 0.01f) return false;

        // 바닥 경계까지 전진해 벽 위치를 출구로 삼는다.
        exitPos = ProjectToFloorEdge(arena, roomCenter, dir);
        return true;
    }

    /// <summary>아레나 바닥 렌더러 경계에서 dir 방향 끝점을 구한다. 바닥을 못 찾으면 방 중앙에서 고정 거리.</summary>
    private static Vector3 ProjectToFloorEdge(Transform arena, Vector3 roomCenter, Vector3 dir)
    {
        var floor = arena.Find("Floor");
        var rend  = floor != null ? floor.GetComponent<Renderer>() : null;
        if (rend == null)
        {
            Debug.LogWarning("[BossExitPath] Floor 렌더러 없음 — 방 중앙에서 15m 지점을 출구로 사용");
            return roomCenter + dir * 15f;
        }

        var b = rend.bounds;
        // dir 축으로 경계까지의 거리(사각 바닥 가정 — 지배적인 축만 사용)
        float reach = Mathf.Abs(dir.z) >= Mathf.Abs(dir.x) ? b.extents.z : b.extents.x;
        return new Vector3(b.center.x, b.min.y, b.center.z) + dir * reach;
    }

    /// <summary>
    /// 출구 지점을 막고 있는 벽 오브젝트를 비활성화해 길을 낸다.
    /// 바닥은 제외하고, 아레나 자식이면서 출구 폭 안에 걸린 콜라이더만 끈다.
    /// </summary>
    private static void OpenWallAt(Vector3 exitPos, Vector3 dir, Transform arena)
    {
        var half = new Vector3(PathWidth * 0.5f, 3f, 2.5f);
        var hits = Physics.OverlapBox(exitPos + Vector3.up * 1.5f, half, Quaternion.LookRotation(dir));
        int opened = 0;

        for (int i = 0; i < hits.Length; i++)
        {
            var t = hits[i].transform;
            if (!t.IsChildOf(arena)) continue;                 // 아레나 밖 오브젝트는 건드리지 않는다
            if (t.name.StartsWith("Floor", StringComparison.OrdinalIgnoreCase)) continue;
            if (t.GetComponentInParent<PlayerController>() != null) continue;

            t.gameObject.SetActive(false);
            opened++;
        }

        if (opened > 0) Debug.Log($"[BossExitPath] 출구 벽 {opened}개 개방");
        else Debug.LogWarning("[BossExitPath] 출구에서 벽을 찾지 못함 — 이미 열린 구조이거나 위치가 어긋났을 수 있다");
    }

    /// <summary>아레나 바닥 머티리얼을 그대로 빌려 길의 톤을 방과 일치시킨다(폴백 타일용).</summary>
    private static Material SampleFloorMaterial(Transform arena)
    {
        var floor = arena.Find("Floor");
        var rend  = floor != null ? floor.GetComponent<Renderer>() : null;
        return rend != null ? rend.sharedMaterial : null;
    }

    /// <summary>길을 한 행씩 깔며 드러낸다 — 출구에서 바깥으로 뻗어나가는 연출.</summary>
    private async UniTaskVoid BuildPathAsync(
        Vector3 exitPos, Vector3 dir, CorridorStyleSO style, Material fallbackMat, CancellationToken ct)
    {
        var right = Vector3.Cross(Vector3.up, dir).normalized;
        int rows   = Mathf.CeilToInt(PathLength / TileSize);
        int half   = PathWidth / 2;
        var rot    = Quaternion.LookRotation(dir);

        try
        {
            for (int i = 0; i < rows; i++)
            {
                var rowCenter = exitPos + dir * ((i + 0.5f) * TileSize);

                for (int lane = -half; lane <= half; lane++)
                {
                    var pos  = rowCenter + right * (lane * TileSize);
                    var tile = CreateTile(pos, rot, style, fallbackMat);
                    if (tile != null) _tiles.Add(tile);
                }

                SpawnEdgeDeco(rowCenter, right, half, i, style);

                await UniTask.Delay(TimeSpan.FromSeconds(RevealStep), cancellationToken: ct);
            }

            PlaceGateAtEnd(exitPos + dir * PathLength, dir);
        }
        catch (OperationCanceledException)
        {
            // 방 정리 중 취소 — 이미 깔린 타일은 방과 함께 파괴된다.
        }
    }

    private Transform CreateTile(Vector3 pos, Quaternion rot, CorridorStyleSO style, Material fallbackMat)
    {
        GameObject go;

        if (style?.floorTilePrefab != null)
        {
            go = Instantiate(style.floorTilePrefab, pos, rot, transform);
        }
        else
        {
            // 테마 코리더 스타일이 없는 챕터(Ch2~4) — 아레나 바닥 머티리얼을 빌려 톤을 맞춘다.
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "PathTile";
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(pos + Vector3.down * 0.1f, rot);
            go.transform.localScale = new Vector3(TileSize, 0.2f, TileSize);

            if (go.TryGetComponent<Renderer>(out var rend))
            {
                if (fallbackMat != null) rend.sharedMaterial = fallbackMat;
                else RuntimePrimitiveMaterial.Apply(rend, FallbackTileColor);
            }
        }

        // 프리팹 경로/프리미티브 경로 모두 Ground로 통일 — 안 하면 길 위에서 접지 판정이 실패한다.
        SetLayerRecursive(go, GroundLayer);

        RiseInAsync(go.transform, this.GetCancellationTokenOnDestroy()).Forget();
        return go.transform;
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    /// <summary>타일이 아래에서 솟아오르며 자리잡는다 — 길이 만들어지는 느낌.</summary>
    private static async UniTaskVoid RiseInAsync(Transform tile, CancellationToken ct)
    {
        if (tile == null) return;

        Vector3 to   = tile.position;
        Vector3 from = to + Vector3.down * TileRiseHeight;
        tile.position = from;

        float t = 0f;
        try
        {
            while (t < TileRiseTime)
            {
                if (tile == null) return;
                ct.ThrowIfCancellationRequested();
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / TileRiseTime);
                tile.position = Vector3.Lerp(from, to, 1f - (1f - k) * (1f - k)); // ease-out
                await UniTask.Yield();
            }
            if (tile != null) tile.position = to;
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>길 양옆 장식 — 스타일에 프리팹이 있을 때만. 간격은 스타일의 edgeObjectSpacing을 따른다.</summary>
    private void SpawnEdgeDeco(Vector3 rowCenter, Vector3 right, int half, int rowIndex, CorridorStyleSO style)
    {
        if (style == null) return;

        int spacing = Mathf.Max(1, Mathf.RoundToInt(style.edgeObjectSpacing / TileSize));
        if (rowIndex % spacing != 0) return;

        float offset = (half + 0.5f) * TileSize;
        PlaceEdge(style.leftEdgePrefabs,  rowCenter + right * offset);
        PlaceEdge(style.rightEdgePrefabs, rowCenter - right * offset);
    }

    private void PlaceEdge(GameObject[] pool, Vector3 pos)
    {
        if (pool == null || pool.Length == 0) return;
        var prefab = pool[UnityEngine.Random.Range(0, pool.Length)];
        if (prefab != null) Instantiate(prefab, pos, Quaternion.identity, transform);
    }

    /// <summary>길 끝에 챕터 게이트를 세운다 — 전환 판정은 기존 ChapterGate가 그대로 담당.</summary>
    private void PlaceGateAtEnd(Vector3 endPos, Vector3 dir)
    {
        ChapterGate.Spawn(endPos);

        // 도착 지점 강조 — 일반 방 게이트가 쓰는 포털 VFX를 그대로 재사용해 톤을 맞춘다.
        var portal = GameRunBootstrapper.Instance?.GatePortalPrefab;
        if (portal != null)
            Instantiate(portal, endPos + Vector3.up * 1.5f, Quaternion.LookRotation(-dir), transform);
    }

    private static Vector3 Flatten(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero;
    }
}
