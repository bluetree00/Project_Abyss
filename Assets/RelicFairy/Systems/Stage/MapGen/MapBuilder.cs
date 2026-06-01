using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TileType[,] 그리드 + BlockPalette → 블록 인스턴스 생성.
/// 연출은 MapPresenter에 위임.
/// </summary>
public class MapBuilder
{
    /// <summary>생성된 블록 정보.</summary>
    public struct PlacedBlock
    {
        public GameObject instance;
        public Vector3 targetPosition;
        public float targetRotationY;
        public TileType tileType;
        public Vector2Int cell;
    }

    /// <summary>
    /// 그리드 기반으로 블록을 인스턴스화.
    /// </summary>
    /// <param name="shopStallPrefab">상점 매대 프리팹(Block_ShopStall). null이면 임시 큐브로 폴백.</param>
    public static List<PlacedBlock> Build(
        TileType[,] grid,
        BlockPalette palette,
        Transform parent,
        float cellSize = 1f,
        float baseY = 0f,
        GameObject shopStallPrefab = null,
        int wallLayers = 1)
    {
        int w = grid.GetLength(0);
        int h = grid.GetLength(1);
        int buffCount = 0;
        var result = new List<PlacedBlock>(w * h);
        // 맵 중앙이 (0, baseY, 0)에 오도록 오프셋 계산
        var offset = new Vector3((w - 1) * 0.5f * cellSize, 0f, (h - 1) * 0.5f * cellSize);
        var gridCenter = new Vector3(0f, baseY, 0f);

        for (int x = 0; x < w; x++)
        {
            for (int z = 0; z < h; z++)
            {
                var type = grid[x, z];
                if (type == TileType.Empty) continue;

                bool isBuffTile = type == TileType.BuffBox || type == TileType.BuffPedestal;
                bool isShopTile = type == TileType.ShopStall
                                  || type == TileType.ShopStallWeapon
                                  || type == TileType.ShopStallItem;
                bool isMonsterSpawnTile = type == TileType.MonsterSpawn || type == TileType.MonsterSpawnCandidate;
                bool isBossSpawnTile   = type == TileType.BossSpawn;
                bool isOverlayTile = isBuffTile || isShopTile || isMonsterSpawnTile || isBossSpawnTile;

                // 오버레이 타일(버프/상점/몬스터스폰): 바닥 블록을 먼저 깔고 그 위에 기능 오브젝트 배치
                var renderType = isOverlayTile ? TileType.Floor : type;
                var blockDef = palette.Pick(renderType);
                if (blockDef == null)
                {
                    blockDef = palette.Pick(TileType.Floor);
                    if (blockDef == null) continue;
                }

                var localPos = new Vector3(x * cellSize - offset.x, baseY, z * cellSize - offset.z);
                var worldPos = parent.TransformPoint(localPos);
                float rotY = CalcRotation(blockDef.facingRule, localPos, gridCenter);

                var go = Object.Instantiate(blockDef.prefab, worldPos, Quaternion.Euler(0, rotY, 0), parent);
                go.name = $"Block_{x}_{z}_{renderType}";
                // 벽은 Wall(8) 레이어로 — 리치 등 공중 보스의 SphereCast 충돌 감지에 사용.
                // 나머지 블록(바닥·버프·상점 등)은 Ground(3) 레이어.
                SetLayerRecursive(go, renderType == TileType.Wall ? 8 : 3);

                result.Add(new PlacedBlock
                {
                    instance = go,
                    targetPosition = worldPos,
                    targetRotationY = rotY,
                    tileType = renderType,
                    cell = new Vector2Int(x, z),
                });

                // 벽 블록 수직 반복 — 같은 프리팹을 위로 쌓아 자연스러운 높이 연출.
                // GPU 인스턴싱(URP 기본)으로 동일 메시+머티리얼은 자동 배칭되어 드로우콜 증가가 적다.
                if (renderType == TileType.Wall && wallLayers > 1)
                {
                    for (int layer = 1; layer < wallLayers; layer++)
                    {
                        var layerWorld = worldPos + new Vector3(0f, layer * cellSize, 0f);
                        var layerGO    = Object.Instantiate(blockDef.prefab, layerWorld, Quaternion.Euler(0, rotY, 0), parent);
                        layerGO.name   = $"Block_{x}_{z}_Wall_L{layer}";
                        SetLayerRecursive(layerGO, 8); // Wall layer
                        result.Add(new PlacedBlock
                        {
                            instance        = layerGO,
                            targetPosition  = layerWorld,
                            targetRotationY = rotY,
                            tileType        = TileType.Wall,
                            cell            = new Vector2Int(x, z),
                        });
                    }
                }

                // 버프 타일: 전용 프리팹이 있으면 사용, 없으면 기본 트리거 오브젝트 생성
                if (isBuffTile)
                {
                    var buffDef = palette.Pick(type);
                    PlacedBlock buffBlock;

                    if (buffDef != null)
                    {
                        var buffGo = Object.Instantiate(buffDef.prefab, worldPos, Quaternion.identity, parent);
                        buffGo.name = $"Buff_{x}_{z}_{type}";
                        AttachBuffInteraction(buffGo, type);
                        buffBlock = new PlacedBlock
                        {
                            instance = buffGo,
                            targetPosition = worldPos,
                            targetRotationY = 0f,
                            tileType = type,
                            cell = new Vector2Int(x, z),
                        };
                        Debug.Log($"[MapBuilder] 버프 타일 배치 (프리팹): {type} at ({x},{z}) pos={worldPos}");
                        buffCount++;
                    }
                    else
                    {
                        buffBlock = CreateDefaultBuffObject(x, z, type, worldPos, cellSize, parent);
                        Debug.Log($"[MapBuilder] 버프 타일 배치 (임시큐브): {type} at ({x},{z}) pos={worldPos}");
                        buffCount++;
                    }

                    result.Add(buffBlock);
                }
                // 상점 타일: shopStallPrefab(Block_ShopStall)을 인스턴스화하고
                // TileType에 따라 ShopStallInteraction.category를 자동 부여.
                // shopStallPrefab이 null인 경우에만 팔레트/임시큐브로 폴백.
                else if (isShopTile)
                {
                    PlacedBlock shopBlock;
                    var category = ResolveCategory(type);

                    if (shopStallPrefab != null)
                    {
                        var shopGo = Object.Instantiate(shopStallPrefab, worldPos, Quaternion.identity, parent);
                        shopGo.name = $"ShopStall_{x}_{z}_{type}";
                        AttachShopStallInteraction(shopGo, cellSize, category);
                        shopBlock = new PlacedBlock
                        {
                            instance = shopGo,
                            targetPosition = worldPos,
                            targetRotationY = 0f,
                            tileType = type,
                            cell = new Vector2Int(x, z),
                        };
                        Debug.Log($"[MapBuilder] 상점 타일 배치 (Block_ShopStall): ({x},{z}) {type} cat={category} pos={worldPos}");
                    }
                    else
                    {
                        // 폴백 1: 팔레트에 등록된 별도 프리팹
                        var shopDef = palette.Pick(type);
                        if (shopDef != null && shopDef.prefab != null)
                        {
                            var shopGo = Object.Instantiate(shopDef.prefab, worldPos, Quaternion.identity, parent);
                            shopGo.name = $"ShopStall_{x}_{z}_{type}";
                            AttachShopStallInteraction(shopGo, cellSize, category);
                            shopBlock = new PlacedBlock
                            {
                                instance = shopGo,
                                targetPosition = worldPos,
                                targetRotationY = 0f,
                                tileType = type,
                                cell = new Vector2Int(x, z),
                            };
                            Debug.Log($"[MapBuilder] 상점 타일 배치 (팔레트 프리팹): ({x},{z}) {type} cat={category} pos={worldPos}");
                        }
                        else
                        {
                            // 폴백 2: 임시 큐브
                            shopBlock = CreateDefaultShopStallObject(x, z, type, worldPos, cellSize, parent, category);
                            Debug.LogWarning($"[MapBuilder] 상점 타일 배치 (임시큐브 폴백 — Block_ShopStall 프리팹 미할당): ({x},{z}) {type} cat={category}");
                        }
                    }

                    result.Add(shopBlock);
                }
                // MonsterSpawn: 바닥만 깔고 스포너 배치는 MonsterSpawnHandler(TokenParser PreBuild)에 위임
                // BossSpawn: 바닥만 깔고 스포너 배치는 BossSpawnHandler(TokenParser PostBuild)에 위임
            }
        }

        if (buffCount > 0)
            Debug.Log($"[MapBuilder] 맵 빌드 완료: 총 블록 {result.Count}개, 버프 타일 {buffCount}개");

        return result;
    }

    /// <summary>전용 프리팹이 없을 때 기본 버프 오브젝트 생성.</summary>
    private static PlacedBlock CreateDefaultBuffObject(
        int x, int z, TileType type, Vector3 pos, float cellSize, Transform parent)
    {
        bool isPedestal = type == TileType.BuffPedestal;

        var go = new GameObject($"Buff_{x}_{z}_{type}");
        go.transform.SetParent(parent, false);
        go.transform.position = pos + Vector3.up * (isPedestal ? 0.05f : 0.5f);

        // 트리거 콜라이더
        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = isPedestal
            ? new Vector3(cellSize * 0.8f, 0.3f, cellSize * 0.8f)
            : new Vector3(cellSize * 0.5f, cellSize * 0.8f, cellSize * 0.5f);

        // 시각 표시용 큐브 (임시 — 추후 프리팹으로 교체)
        var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.transform.SetParent(go.transform, false);
        visual.transform.localScale = isPedestal
            ? new Vector3(cellSize * 0.7f, 0.1f, cellSize * 0.7f)
            : new Vector3(cellSize * 0.4f, cellSize * 0.4f, cellSize * 0.4f);

        // 콜라이더 제거 (시각용만)
        var visualCol = visual.GetComponent<Collider>();
        if (visualCol != null) Object.Destroy(visualCol);

        // 색상: 발판=파랑, 상자=노랑
        var renderer = visual.GetComponent<Renderer>();
        if (renderer != null)
            renderer.material.color = isPedestal
                ? new Color(0.3f, 0.5f, 1f, 0.8f)
                : new Color(1f, 0.85f, 0.2f, 0.8f);

        AttachBuffInteraction(go, type);

        return new PlacedBlock
        {
            instance = go,
            targetPosition = go.transform.position,
            targetRotationY = 0f,
            tileType = type,
            cell = new Vector2Int(x, z),
        };
    }

    private static void AttachBuffInteraction(GameObject go, TileType type)
    {
        var interaction = go.GetComponent<BuffTileInteraction>();
        if (interaction == null)
            interaction = go.AddComponent<BuffTileInteraction>();

        interaction.Setup(type == TileType.BuffPedestal);

        // 트리거 콜라이더 보장
        var col = go.GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    /// <summary>
    /// 인스턴스에 ShopStallInteraction 컴포넌트 보장 + 카테고리 주입 + 트리거 콜라이더 보장.
    /// 프리팹에 컴포넌트가 이미 부착돼 있으면 그대로 사용하고, 없으면 안전망으로 AddComponent.
    /// </summary>
    private static void AttachShopStallInteraction(GameObject go, float cellSize, ShopCategory category)
    {
        if (!go.TryGetComponent<ShopStallInteraction>(out var interaction))
            interaction = go.AddComponent<ShopStallInteraction>();

        interaction.SetCategory(category);

        // 트리거 콜라이더 보장 (없으면 추가)
        if (!go.TryGetComponent<Collider>(out var col))
        {
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(cellSize * 1.2f, cellSize * 1.5f, cellSize * 1.2f);
            box.center = new Vector3(0f, cellSize * 0.5f, 0f);
        }
        else
        {
            col.isTrigger = true;
        }
    }

    /// <summary>TileType → ShopCategory 매핑 (레거시 ShopStall은 Item 폴백).</summary>
    private static ShopCategory ResolveCategory(TileType type)
    {
        switch (type)
        {
            case TileType.ShopStallWeapon: return ShopCategory.Weapon;
            case TileType.ShopStallItem:   return ShopCategory.Item;
            case TileType.ShopStall:       return ShopCategory.Item; // 레거시 호환
            default:                       return ShopCategory.Item;
        }
    }

    /// <summary>상점 진열대 프리팹이 모두 없을 때 기본 시각 오브젝트 생성 (최후 폴백).</summary>
    private static PlacedBlock CreateDefaultShopStallObject(
        int x, int z, TileType type, Vector3 pos, float cellSize, Transform parent, ShopCategory category)
    {
        var go = new GameObject($"ShopStall_{x}_{z}_{type}");
        go.transform.SetParent(parent, false);
        go.transform.position = pos + Vector3.up * 0.5f;

        // 트리거 콜라이더 (상호작용 범위)
        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(cellSize * 1.4f, cellSize * 1.6f, cellSize * 1.4f);

        // 시각용 큐브 (진열대)
        var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.transform.SetParent(go.transform, false);
        visual.transform.localScale = new Vector3(cellSize * 0.7f, cellSize * 0.6f, cellSize * 0.7f);
        visual.transform.localPosition = new Vector3(0f, -cellSize * 0.2f, 0f);

        var visualCol = visual.GetComponent<Collider>();
        if (visualCol != null) Object.Destroy(visualCol);

        var renderer = visual.GetComponent<Renderer>();
        if (renderer != null)
            renderer.material.color = new Color(0.9f, 0.6f, 0.2f, 1f); // 나무색 진열대

        var interaction = go.AddComponent<ShopStallInteraction>();
        interaction.SetCategory(category);

        return new PlacedBlock
        {
            instance = go,
            targetPosition = go.transform.position,
            targetRotationY = 0f,
            tileType = type,
            cell = new Vector2Int(x, z),
        };
    }

    /// <summary>
    /// 그리드 비어있지 않은 모든 칸 위에 천장 타일을 배치한다.
    /// palette에 Ceiling BlockDef가 있으면 사용, 없으면 Floor 타일을 X축 180° 뒤집어 폴백.
    /// ceilingHeight == 0이면 아무것도 하지 않는다.
    /// </summary>
    public static void BuildCeiling(
        TileType[,] grid,
        BlockPalette palette,
        Transform parent,
        float cellSize,
        float baseY,
        float ceilingHeight)
    {
        if (ceilingHeight <= 0f) return;

        int w = grid.GetLength(0);
        int h = grid.GetLength(1);
        var offset = new Vector3((w - 1) * 0.5f * cellSize, 0f, (h - 1) * 0.5f * cellSize);
        float ceilingY = baseY + ceilingHeight;

        var ceilingDef = palette?.Pick(TileType.Ceiling);
        var floorDef   = palette?.Pick(TileType.Floor);
        var useDef     = ceilingDef ?? floorDef;
        if (useDef?.prefab == null) return;

        bool flip = ceilingDef == null; // 전용 천장 프리팹 없으면 바닥 타일 뒤집기

        for (int x = 0; x < w; x++)
        {
            for (int z = 0; z < h; z++)
            {
                if (grid[x, z] == TileType.Empty) continue;

                var localPos = new Vector3(x * cellSize - offset.x, ceilingY, z * cellSize - offset.z);
                var worldPos = parent.TransformPoint(localPos);
                var rot      = flip ? Quaternion.Euler(180f, 0f, 0f) : Quaternion.identity;

                var go = Object.Instantiate(useDef.prefab, worldPos, rot, parent);
                go.name = $"Ceiling_{x}_{z}";
                SetLayerRecursive(go, 3);
            }
        }
    }

    /// <summary>
    /// Floor에 인접한 Wall 타일의 안쪽 면에 벽 조명 프리팹을 배치한다.
    /// 방 중앙 조명(centerLightPrefab)도 선택적으로 배치.
    /// </summary>
    /// <param name="spacing">몇 칸마다 조명 1개를 배치할지 (낮을수록 조밀).</param>
    /// <param name="heightRatio">벽 높이 중 어느 위치에 배치할지 (0=하단, 1=상단). 0.4 권장.</param>
    public static void BuildRoomLights(
        TileType[,]        grid,
        Transform          parent,
        float              cellSize,
        float              baseY,
        int                wallLayers,
        RoomLightingConfig cfg)
    {
        if (cfg == null || (cfg.wallLightPrefab == null && cfg.centerLightPrefab == null)) return;

        int w      = grid.GetLength(0);
        int h      = grid.GetLength(1);
        var offset = new Vector3((w - 1) * 0.5f * cellSize, 0f, (h - 1) * 0.5f * cellSize);
        float lightY = baseY + wallLayers * cellSize * cfg.wallLightHeightRatio;

        // 이웃 방향 (4방향)
        int[] dx = { -1, 1, 0, 0 };
        int[] dz = {  0, 0,-1, 1 };

        int counter = 0;

        if (cfg.wallLightPrefab != null)
        {
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (grid[x, z] != TileType.Wall) continue;

                    // Floor에 인접한 방향(안쪽) 탐색
                    bool      hasInner  = false;
                    Vector3   inwardDir = Vector3.zero;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + dx[d], nz = z + dz[d];
                        if (nx < 0 || nx >= w || nz < 0 || nz >= h) continue;
                        var t = grid[nx, nz];
                        if (t != TileType.Wall && t != TileType.Empty)
                        {
                            hasInner  = true;
                            inwardDir = new Vector3(dx[d], 0f, dz[d]);
                            break;
                        }
                    }

                    if (!hasInner) continue;

                    if (counter++ % cfg.wallLightSpacing != 0) continue;

                    var wallLocal  = new Vector3(x * cellSize - offset.x, lightY, z * cellSize - offset.z);
                    var lightLocal = wallLocal + inwardDir * (cellSize * 0.45f);
                    var worldPos   = parent.TransformPoint(lightLocal);
                    var rot        = Quaternion.LookRotation(inwardDir);

                    Object.Instantiate(cfg.wallLightPrefab, worldPos, rot, parent);
                }
            }
        }

        // 방 중심 천장 조명
        if (cfg.centerLightPrefab != null)
        {
            float ceilingY = baseY + wallLayers * cellSize;
            Object.Instantiate(cfg.centerLightPrefab,
                parent.TransformPoint(new Vector3(0f, ceilingY - cellSize * 0.3f, 0f)),
                Quaternion.identity, parent);
        }
    }

    /// <summary>
    /// 그리드 Floor 영역과 동일한 크기의 투명 바닥 콜라이더 생성.
    /// 외곽 벽 밖(방 경계 이탈)에서는 존재하지 않으므로, 플레이어가 벽을 뚫고 나가면
    /// SafeFloor 없이 바로 낙하 → FallRecoveryController가 _lastSafe로 복구.
    /// </summary>
    public static GameObject CreateSafeFloor(int width, int height, float cellSize, float baseY, Transform parent)
    {
        var go = new GameObject("SafeFloor");
        go.layer = 3; // Ground (TagManager layer 3)
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, baseY - 0.05f, 0f);

        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(width * cellSize, 0.1f, height * cellSize);

        return go;
    }

    private static float CalcRotation(FacingRule rule, Vector3 pos, Vector3 center)
    {
        switch (rule)
        {
            case FacingRule.FaceCenter:
                var dir = center - pos;
                if (dir.sqrMagnitude < 0.001f) return 0f;
                return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

            case FacingRule.FaceOutward:
                var outDir = pos - center;
                if (outDir.sqrMagnitude < 0.001f) return 0f;
                return Mathf.Atan2(outDir.x, outDir.z) * Mathf.Rad2Deg;

            case FacingRule.Random:
                return Random.Range(0, 4) * 90f;

            default:
                return 0f;
        }
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }
}
