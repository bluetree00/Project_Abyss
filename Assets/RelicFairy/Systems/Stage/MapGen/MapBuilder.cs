using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TileType[,] 그리드 + BlockPalette → 블록 인스턴스 생성.
/// 연출은 MapPresenter에 위임.
/// </summary>
public class MapBuilder
{
    // ── 출구 조명 위계 (웨이파인딩) ──────────────────────────────
    // 출구를 '더 중요해 보이는 조명'으로 승격해 밝기 대비로 경로를 읽히게 한다.
    // 근거: R. Yang, "How to Light a Level"(GDC 2018) — 출구가 여럿이면 균일이 아니라
    //       중요도에 따라 다르게 밝힌다. 값은 인게임에서 튜닝.
    private const float DoorLightIntensityMul = 1.6f;
    private const float DoorLightRangeMul     = 1.35f;
    private const int   DoorLightsPerDoor     = 2;

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
    /// <param name="rng">시드 RNG. 넘기면 블록 배리언트·Random 페이싱이 결정적이 된다(이어하기 재현).
    /// null이면 Unity 전역 Random(레거시 존 빌드·에디터 툴).</param>
    public static List<PlacedBlock> Build(
        TileType[,] grid,
        BlockPalette palette,
        Transform parent,
        float cellSize = 1f,
        float baseY = 0f,
        GameObject shopStallPrefab = null,
        int wallLayers = 1,
        System.Random rng = null)
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
                var blockDef = palette.Pick(renderType, rng);
                if (blockDef == null)
                {
                    blockDef = palette.Pick(TileType.Floor, rng);
                    if (blockDef == null) continue;
                }

                var localPos = new Vector3(x * cellSize - offset.x, baseY, z * cellSize - offset.z);
                var worldPos = parent.TransformPoint(localPos);
                float rotY = CalcRotation(blockDef.facingRule, localPos, gridCenter, rng);

                var go = Object.Instantiate(blockDef.prefab, worldPos, Quaternion.Euler(0, rotY, 0), parent);
                ApplyBlockAdjust(go, blockDef);
                Name(go, $"Block_{x}_{z}_{renderType}");
                // 벽은 Wall(8) 레이어로 — 리치 등 공중 보스의 SphereCast 충돌 감지에 사용.
                // 나머지 블록(바닥·버프·상점 등)은 Ground(3) 레이어.
                SetLayerRecursive(go, renderType == TileType.Wall ? 8 : 3);

                result.Add(new PlacedBlock
                {
                    instance = go,
                    targetPosition = go.transform.position,
                    targetRotationY = rotY,
                    tileType = renderType,
                    cell = new Vector2Int(x, z),
                });

                // 벽 블록 수직 반복 — 같은 프리팹을 위로 쌓아 자연스러운 높이 연출(Stacked 모드).
                // GPU 인스턴싱(URP 기본)으로 동일 메시+머티리얼은 자동 배칭되어 드로우콜 증가가 적다.
                // Single 모드(절벽 등 자연지형)는 셀당 1개만 배치 — 높이는 프리팹 메시가 소유하므로 반복하지 않는다.
                if (renderType == TileType.Wall && palette.WallMode == WallBuildMode.Stacked && wallLayers > 1)
                {
                    for (int layer = 1; layer < wallLayers; layer++)
                    {
                        var layerWorld = worldPos + new Vector3(0f, layer * cellSize, 0f);
                        var layerGO    = Object.Instantiate(blockDef.prefab, layerWorld, Quaternion.Euler(0, rotY, 0), parent);
                        Name(layerGO, $"Block_{x}_{z}_Wall_L{layer}");
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
                    var buffDef = palette.Pick(type, rng);
                    PlacedBlock buffBlock;

                    if (buffDef != null)
                    {
                        var buffGo = Object.Instantiate(buffDef.prefab, worldPos, Quaternion.identity, parent);
                        Name(buffGo, $"Buff_{x}_{z}_{type}");
                        AttachBuffInteraction(buffGo, type);
                        buffBlock = new PlacedBlock
                        {
                            instance = buffGo,
                            targetPosition = worldPos,
                            targetRotationY = 0f,
                            tileType = type,
                            cell = new Vector2Int(x, z),
                        };
                        RFLog.D($"[MapBuilder] 버프 타일 배치 (프리팹): {type} at ({x},{z}) pos={worldPos}");
                        buffCount++;
                    }
                    else
                    {
                        buffBlock = CreateDefaultBuffObject(x, z, type, worldPos, cellSize, parent);
                        RFLog.D($"[MapBuilder] 버프 타일 배치 (임시큐브): {type} at ({x},{z}) pos={worldPos}");
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
                        Name(shopGo, $"ShopStall_{x}_{z}_{type}");
                        AttachShopStallInteraction(shopGo, cellSize, category);
                        shopBlock = new PlacedBlock
                        {
                            instance = shopGo,
                            targetPosition = worldPos,
                            targetRotationY = 0f,
                            tileType = type,
                            cell = new Vector2Int(x, z),
                        };
                        RFLog.D($"[MapBuilder] 상점 타일 배치 (Block_ShopStall): ({x},{z}) {type} cat={category} pos={worldPos}");
                    }
                    else
                    {
                        // 폴백 1: 팔레트에 등록된 별도 프리팹
                        var shopDef = palette.Pick(type, rng);
                        if (shopDef != null && shopDef.prefab != null)
                        {
                            var shopGo = Object.Instantiate(shopDef.prefab, worldPos, Quaternion.identity, parent);
                            Name(shopGo, $"ShopStall_{x}_{z}_{type}");
                            AttachShopStallInteraction(shopGo, cellSize, category);
                            shopBlock = new PlacedBlock
                            {
                                instance = shopGo,
                                targetPosition = worldPos,
                                targetRotationY = 0f,
                                tileType = type,
                                cell = new Vector2Int(x, z),
                            };
                            RFLog.D($"[MapBuilder] 상점 타일 배치 (팔레트 프리팹): ({x},{z}) {type} cat={category} pos={worldPos}");
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
            RFLog.D($"[MapBuilder] 맵 빌드 완료: 총 블록 {result.Count}개, 버프 타일 {buffCount}개");

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

        // 색상: 발판=파랑, 상자=노랑 (빌드 프리미티브 핑크 방지 — URP/Lit 명시 할당)
        var renderer = visual.GetComponent<Renderer>();
        RuntimePrimitiveMaterial.Apply(renderer, isPedestal
            ? new Color(0.3f, 0.5f, 1f, 0.8f)
            : new Color(1f, 0.85f, 0.2f, 0.8f));

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

        // 나무색 진열대 (빌드 프리미티브 핑크 방지 — URP/Lit 명시 할당)
        var renderer = visual.GetComponent<Renderer>();
        RuntimePrimitiveMaterial.Apply(renderer, new Color(0.9f, 0.6f, 0.2f, 1f));

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
        float ceilingHeight,
        System.Random rng = null)
    {
        if (ceilingHeight <= 0f) return;
        // 열린 하늘 테마(숲·심연)는 천장을 덮지 않는다.
        if (palette != null && !palette.HasCeiling) return;

        int w = grid.GetLength(0);
        int h = grid.GetLength(1);
        var offset = new Vector3((w - 1) * 0.5f * cellSize, 0f, (h - 1) * 0.5f * cellSize);
        float ceilingY = baseY + ceilingHeight;

        var ceilingDef = palette?.Pick(TileType.Ceiling, rng);
        var floorDef   = palette?.Pick(TileType.Floor, rng);
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
                Name(go, $"Ceiling_{x}_{z}");
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
    /// <param name="doorCells">
    /// 문(입구/출구) 셀. 넘기면 <b>출구 주변을 최우선으로</b> 밝히고 조명을 승격(강도·범위 증폭)한다.
    /// 근거: 레벨 조명의 1차 역할은 웨이파인딩이며, 출구가 여럿이면 균일하게가 아니라
    /// 중요도에 따라 다르게 밝혀야 한다(R. Yang, "How to Light a Level", GDC 2018).
    /// 균일 배치만 하면 "밝은 곳이 갈 곳"이라는 신호가 성립하지 않는다.
    /// </param>
    public static void BuildRoomLights(
        TileType[,]        grid,
        Transform          parent,
        float              cellSize,
        float              baseY,
        int                wallLayers,
        RoomLightingConfig cfg,
        IReadOnlyCollection<Vector2Int> doorCells = null)
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
        int placed  = 0;
        var lit     = new HashSet<Vector2Int>();   // 이미 조명이 붙은 벽 셀

        // 벽 셀 (x,z)가 방 안쪽(Floor)을 향하는지 판정하고 그 방향을 돌려준다.
        bool TryInward(int x, int z, out Vector3 inwardDir)
        {
            inwardDir = Vector3.zero;
            if (x < 0 || x >= w || z < 0 || z >= h) return false;
            if (grid[x, z] != TileType.Wall) return false;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + dx[d], nz = z + dz[d];
                if (nx < 0 || nx >= w || nz < 0 || nz >= h) continue;
                var t = grid[nx, nz];
                if (t != TileType.Wall && t != TileType.Empty)
                {
                    inwardDir = new Vector3(dx[d], 0f, dz[d]);
                    return true;
                }
            }
            return false;
        }

        // 벽 조명 1개 배치. boost=true면 출구 조명으로 승격(강도·범위 증폭).
        void PlaceWallLight(int x, int z, Vector3 inwardDir, bool boost)
        {
            var wallLocal  = new Vector3(x * cellSize - offset.x, lightY, z * cellSize - offset.z);
            var lightLocal = wallLocal + inwardDir * (cellSize * 0.45f);
            var go = Object.Instantiate(cfg.wallLightPrefab,
                parent.TransformPoint(lightLocal), Quaternion.LookRotation(inwardDir), parent);

            if (boost)
            {
                // 주 출구를 '더 중요해 보이는 조명'으로 — 밝기 위계로 경로를 읽히게 한다.
                var lt = go.GetComponentInChildren<Light>();
                if (lt != null)
                {
                    lt.intensity *= DoorLightIntensityMul;
                    lt.range     *= DoorLightRangeMul;
                }
            }

            lit.Add(new Vector2Int(x, z));
            placed++;
        }

        if (cfg.wallLightPrefab != null)
        {
            // ── 패스 1: 출구 조명 (웨이파인딩 우선, 예산을 여기서 먼저 쓴다) ──
            if (doorCells != null)
            {
                foreach (var door in doorCells)
                {
                    if (cfg.maxWallLights > 0 && placed >= cfg.maxWallLights) break;

                    int found = 0;
                    // 문 셀을 링(반경 1→2)으로 훑어 개구부 양옆 벽을 최대 2개 밝힌다.
                    for (int r = 1; r <= 2 && found < DoorLightsPerDoor; r++)
                    {
                        for (int ox = -r; ox <= r && found < DoorLightsPerDoor; ox++)
                        {
                            for (int oz = -r; oz <= r && found < DoorLightsPerDoor; oz++)
                            {
                                if (Mathf.Abs(ox) != r && Mathf.Abs(oz) != r) continue; // 링 경계만
                                int x = door.x + ox, z = door.y + oz;
                                var cell = new Vector2Int(x, z);
                                if (lit.Contains(cell)) continue;
                                if (!TryInward(x, z, out var inward)) continue;
                                if (cfg.maxWallLights > 0 && placed >= cfg.maxWallLights) break;

                                PlaceWallLight(x, z, inward, boost: true);
                                found++;
                            }
                        }
                    }
                }
            }

            // ── 패스 2: 균일 채움 (남은 예산). 출구 조명 주변은 건너뛴다 ──
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (!TryInward(x, z, out var inwardDir)) continue;
                    if (counter++ % cfg.wallLightSpacing != 0) continue;

                    // 0이면 무제한(현행). 큰 방의 과도한 실시간 조명을 캡한다.
                    if (cfg.maxWallLights > 0 && placed >= cfg.maxWallLights) continue;

                    // 출구 조명과 뭉치지 않게 인접 셀 회피
                    bool nearDoorLight = false;
                    for (int ox = -1; ox <= 1 && !nearDoorLight; ox++)
                        for (int oz = -1; oz <= 1; oz++)
                            if (lit.Contains(new Vector2Int(x + ox, z + oz))) { nearDoorLight = true; break; }
                    if (nearDoorLight) continue;

                    PlaceWallLight(x, z, inwardDir, boost: false);
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
    /// 문 개구부 바깥으로 짧은 복도 스텁(바닥+측벽+천장+끝막이)을 뻗는다.
    /// 격리형 방의 문 너머가 허공(절벽)으로 보이는 것을 막고 "뒤로 이어지는 통로" 느낌을 준다.
    /// 방 블록과 동일 팔레트를 쓰고 parent(=roomGO)에 부착되어 디졸브/디스폰에 함께 동참한다.
    /// </summary>
    /// <param name="openingCenterLocal">개구부 바닥 중앙의 로컬 좌표(Build의 offset 규약과 동일).</param>
    /// <param name="edge">문 엣지 — 바깥 방향 결정(North=+Z/South=-Z/East=+X/West=-X).</param>
    /// <param name="widthCells">개구부 폭(셀 수). 측벽은 폭+1 위치에 세운다.</param>
    /// <param name="lengthCells">바깥으로 뻗는 길이(셀 수). 0 이하면 아무것도 안 함.</param>
    public static List<PlacedBlock> BuildDoorCorridor(
        BlockPalette palette,
        Transform    parent,
        Vector3      openingCenterLocal,
        DoorEdge     edge,
        int          widthCells,
        int          lengthCells,
        float        cellSize,
        float        baseY,
        int          wallLayers,
        System.Random rng = null)
    {
        var placed = new List<PlacedBlock>();
        if (palette == null || lengthCells <= 0) return placed;

        var floorDef = palette.Pick(TileType.Floor, rng);
        var wallDef  = palette.Pick(TileType.Wall, rng);
        var ceilDef  = palette.Pick(TileType.Ceiling, rng);
        bool ceilFlip = ceilDef == null;          // 전용 천장 없으면 바닥 타일 뒤집기(BuildCeiling과 동일)
        var ceilUse  = ceilDef ?? floorDef;
        if (floorDef?.prefab == null) return placed;

        // 팔레트 수직 프로필 반영 — Single 모드는 벽 1개만.
        int  wallReps  = palette.WallMode == WallBuildMode.Single ? 1 : wallLayers;
        // 복도는 방이 열린 하늘이어도 항상 천장으로 감싼다 → 밖으로 이어지는 "터널"이 되어
        // 끝(끝막이)이 어둠 속으로 사라지고 방과 연결된 느낌을 준다.
        bool doCeiling = true;

        // 바깥/측면 단위 방향 (로컬 XZ)
        Vector3 outward, lateral;
        switch (edge)
        {
            case DoorEdge.North: outward = Vector3.forward; lateral = Vector3.right;   break; // +Z
            case DoorEdge.South: outward = Vector3.back;    lateral = Vector3.right;   break; // -Z
            case DoorEdge.East:  outward = Vector3.right;   lateral = Vector3.forward; break; // +X
            case DoorEdge.West:  outward = Vector3.left;    lateral = Vector3.forward; break; // -X
            default:             return placed;
        }

        int half       = Mathf.Max(0, widthCells / 2); // RoomDoorPlanner.Open과 동일 규약(개구부 = 2*half+1)
        float ceilingY = baseY + wallLayers * cellSize;

        // step=1..length: 바닥(개구부 폭) + 측벽(폭+1) + 천장
        for (int step = 1; step <= lengthCells; step++)
        {
            Vector3 axis = openingCenterLocal + outward * (step * cellSize);

            for (int lat = -half; lat <= half; lat++)
            {
                Vector3 fLocal = axis + lateral * (lat * cellSize); fLocal.y = baseY;
                Place(floorDef, parent, fLocal, Quaternion.identity, 3, $"Corridor_F_{step}_{lat}", TileType.Floor, placed);

                Vector3 cLocal = fLocal; cLocal.y = ceilingY;
                var cRot = ceilFlip ? Quaternion.Euler(180f, 0f, 0f) : Quaternion.identity;
                if (doCeiling && ceilUse?.prefab != null)
                    Place(ceilUse, parent, cLocal, cRot, 3, $"Corridor_C_{step}_{lat}", TileType.Ceiling, placed);
            }

            if (wallDef?.prefab != null)
                for (int sign = -1; sign <= 1; sign += 2)
                    StackWall(wallDef, parent, axis + lateral * (sign * (half + 1) * cellSize), baseY, cellSize, wallReps, $"Corridor_W_{step}_{sign}", placed);
        }

        // 끝막이 대신 — 복도 끝을 '다음 방'으로 열어, 통로가 실제 다른 방으로 이어진 것처럼 보이게 한다.
        // 핵심 두 가지: (1) 방 규모로 넓고 깊게, (2) 천장을 덮지 않아(개방) 부감 카메라가 방 안을 들여다볼 수 있게.
        // → 지붕 덮인 복도 터널이 '개방된 방'으로 이어지는 대비가 생겨, 덩어리가 아니라 실제 방으로 읽힌다.
        if (floorDef?.prefab != null && wallDef?.prefab != null)
        {
            // 다음 방과 비슷한 규모로 — 작으면 통로 끝이 '섬'처럼 보인다. 실제 방(폭 38~49)에 맞먹게 크게.
            int chamberDepth = 20;             // 방 깊이(칸) — 실제 방 깊이급
            int chamberHalf  = half + 17;      // 방 반폭 → 폭 약 2*(half+17)+1 ≈ 39칸(방 규모)
            int nearStep     = lengthCells + 1;
            int farStep      = lengthCells + chamberDepth;

            // 바닥만 깔고 천장은 덮지 않는다(개방) — 본 방과 동일한 개방형이라 부감에서 '방'으로 보인다.
            for (int step = nearStep; step <= farStep; step++)
            {
                Vector3 axis = openingCenterLocal + outward * (step * cellSize);
                for (int lat = -chamberHalf; lat <= chamberHalf; lat++)
                {
                    Vector3 fLocal = axis + lateral * (lat * cellSize); fLocal.y = baseY;
                    Place(floorDef, parent, fLocal, Quaternion.identity, 3, $"Chamber_F_{step}_{lat}", TileType.Floor, placed);
                }
                for (int sign = -1; sign <= 1; sign += 2)
                    StackWall(wallDef, parent, axis + lateral * (sign * (chamberHalf + 1) * cellSize), baseY, cellSize, wallReps, $"Chamber_W_{step}_{sign}", placed);
            }
            // 근벽(어깨) — 복도 개구부(±half) 밖의 넓어진 부분을 막아 '방 입구(문틀)'를 만든다.
            Vector3 nearAxis = openingCenterLocal + outward * (lengthCells * cellSize);
            for (int lat = half + 1; lat <= chamberHalf; lat++)
            {
                StackWall(wallDef, parent, nearAxis + lateral * (lat * cellSize),  baseY, cellSize, wallReps, $"Chamber_ShR_{lat}", placed);
                StackWall(wallDef, parent, nearAxis + lateral * (-lat * cellSize), baseY, cellSize, wallReps, $"Chamber_ShL_{lat}", placed);
            }
            // 먼벽(방 끝) — 완전히 막아 bounded 방으로 마감(void 차단).
            Vector3 farAxis = openingCenterLocal + outward * ((farStep + 1) * cellSize);
            for (int lat = -(chamberHalf + 1); lat <= chamberHalf + 1; lat++)
                StackWall(wallDef, parent, farAxis + lateral * (lat * cellSize), baseY, cellSize, wallReps, $"Chamber_Cap_{lat}", placed);

            // 내부 기둥 — 빈 슬래브가 아니라 '내용이 있는 방'으로 읽히게 스케일 기준을 준다.
            // 깊어진 방에 맞춰 양옆 2열(안/바깥)을 깊이 방향 여러 지점에 세운다.
            for (int row = 3; row <= chamberDepth - 2; row += 4)
            {
                Vector3 pAxis = openingCenterLocal + outward * ((nearStep + row) * cellSize);
                foreach (int pl in new[] { chamberHalf - 2, half + 4 })
                {
                    StackWall(wallDef, parent, pAxis + lateral * ( pl * cellSize), baseY, cellSize, wallReps, $"Chamber_Pillar_{row}_{pl}_R", placed);
                    StackWall(wallDef, parent, pAxis + lateral * (-pl * cellSize), baseY, cellSize, wallReps, $"Chamber_Pillar_{row}_{pl}_L", placed);
                }
            }
        }

        return placed;
    }

    /// <summary>local 위치에 블록 1개 인스턴스화 후 placed에 기록.</summary>
    private static void Place(
        BlockDef def, Transform parent, Vector3 local, Quaternion rot, int layer, string name,
        TileType tt, List<PlacedBlock> placed)
    {
        var world = parent.TransformPoint(local);
        var go = Object.Instantiate(def.prefab, world, rot, parent);
        ApplyBlockAdjust(go, def); // 복도 블록도 BlockDef 스케일·오프셋 적용(기둥 등 건축 프롭이 방 벽과 일치)
        go.name = name;
        SetLayerRecursive(go, layer);
        placed.Add(new PlacedBlock { instance = go, targetPosition = go.transform.position, targetRotationY = rot.eulerAngles.y, tileType = tt, cell = Vector2Int.zero });
    }

    /// <summary>한 위치에 벽을 wallLayers만큼 수직으로 쌓는다.</summary>
    private static void StackWall(
        BlockDef wallDef, Transform parent, Vector3 baseLocal, float baseY, float cellSize, int wallLayers,
        string name, List<PlacedBlock> placed)
    {
        for (int layer = 0; layer < Mathf.Max(1, wallLayers); layer++)
        {
            Vector3 local = baseLocal; local.y = baseY + layer * cellSize;
            Place(wallDef, parent, local, Quaternion.identity, 8, $"{name}_L{layer}", TileType.Wall, placed);
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

    private static float CalcRotation(FacingRule rule, Vector3 pos, Vector3 center, System.Random rng = null)
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
                return (rng != null ? rng.Next(0, 4) : Random.Range(0, 4)) * 90f;

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

    /// <summary>BlockDef의 배치 보정(스케일·오프셋)을 인스턴스에 적용.
    /// 건축 프롭처럼 피벗·크기가 셀과 안 맞는 메시를 셀에 앉힌다. 기본값(scale 1/offset 0)은 무영향.</summary>
    private static void ApplyBlockAdjust(GameObject go, BlockDef def)
    {
        if (def.localScale != Vector3.one)
            go.transform.localScale = Vector3.Scale(go.transform.localScale, def.localScale);
        if (def.localOffset != Vector3.zero)
            go.transform.position += def.localOffset;
    }

    /// <summary>디버그용 블록 명명. 릴리즈 빌드에서는 호출문(문자열 보간 포함)이 제거돼 GC 할당이 사라진다.
    /// 런타임 코드는 블록 이름에 의존하지 않는다(이름 기반 Find 없음 — 확인 완료).</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private static void Name(GameObject go, string n) => go.name = n;
}
