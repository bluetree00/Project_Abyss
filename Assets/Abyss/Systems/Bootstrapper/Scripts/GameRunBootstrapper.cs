using System.Threading;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Cysharp.Threading.Tasks;

public sealed class GameRunBootstrapper : MonoBehaviour
{
    public static GameRunBootstrapper Instance { get; private set; }

    [SerializeField] private string playerPrefabKey = "Knight";
    [SerializeField] private string directCombatMapPrefabKey = "TestNomarStage_01";
    [SerializeField] private Transform playerSpawnPoint;
    [SerializeField] private Transform mapRoot;
    [SerializeField] private bool buildRuntimeNavMesh = true;
    [SerializeField] private bool disableSceneBakedNavMeshOnStart = true;

    [Header("Player Entrance")]
    [SerializeField, Tooltip("캐릭터 데이터에 개별 연출이 없을 때 사용할 기본 등장 연출 SO")]
    private PlayerEntranceBehaviourSO defaultPlayerEntrance;

    [Header("Block Map Gen")]
    [SerializeField, Tooltip("단일 팔레트 (fallback). blockPalettes에 테마 매칭이 없으면 이 값 사용.")]
    private BlockPalette blockPalette;

    [SerializeField, Tooltip("테마별 블록 팔레트 배열. MapRoomEntry.theme와 BlockPalette.themeMatch가 일치하는 첫 항목이 사용됨.")]
    private BlockPalette[] blockPalettes;

    [SerializeField] private float blockCellSize = 1f;

    [SerializeField, Tooltip("블록 배치 Y 오프셋. 피봇이 센터인 큐브(cellSize=1)에서 타일이 떠보이면 -0.5. 프리팹 피봇이 바닥이면 0.")]
    private float blockBaseY = -0.5f;

    [Header("Shop Room")]
    [SerializeField, Min(0)] private int shopSlotCount = 3;

    [Tooltip("상점 매대 프리팹 (Block_ShopStall). MapBuilder가 ShopStall 타일에서 인스턴스화하고 " +
             "TileType(ShopStallWeapon/ShopStallItem)에 따라 ShopStallInteraction.category를 자동 설정.")]
    [SerializeField] private GameObject blockShopStallPrefab;

    [Tooltip("상점 등급별 기본가 SO. ShopDataManager 초기화에 사용. " +
             "비어있으면 ResolvePrice는 price_override만 적용 + 기본가 0 폴백.")]
    [SerializeField] private ShopPriceTableSO shopPriceTable;

    [Tooltip("행운치 기반 등급 추첨 테이블. 상점 매대 등급 추첨에 사용.")]
    [SerializeField] private LuckRollTableSO luckRollTable;

    [Header("Decoration")]
    [Tooltip("방 테마별 장식 카탈로그. 방 진입 시 MapRoomEntry.theme와 themeMatch가 일치하는 첫 항목 사용. " +
             "일치 없으면 themeMatch=\"*\" 범용 카탈로그로 폴백.")]
    [SerializeField] private DecorationCatalogSO[] decorationCatalogs;

    private StagePointUI[] _points;
    private GameObject _currentMapGO;
    // grid_csv의 P 토큰에서 계산한 플레이어 스폰 월드 좌표.
    // SpawnBlockMapAsync에서 채워지고 SpawnPlayerAsync에서 소비.
    private Vector3? _pendingPlayerSpawnPos;
    private bool _isSpawning;

    private GameRunSession _run;
    public GameRunSession Run => _run;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        var app = AppBootstrapper.Instance;
        if (app != null && app.CurrentRun != null)
        {
            _run = app.CurrentRun;
        }
        else
        {
            _run = new GameRunSession();
            if (app != null)
                app.BeginRun(_run);
        }

        var uiRoot = UIRootBootstrapper.Instance;
        if (uiRoot != null)
            uiRoot.BindHudToRun(_run);

        if (disableSceneBakedNavMeshOnStart)
            DisableSceneBakedNavMesh();

        _run.OnMapSpawnRequested += OnMapSpawnRequestedHandler;
    }

    private async void Start()
    {
        // 카메라 인트로 준비 (즉시 멀리 배치 + OnPlayerBound 이벤트 대기)
        EnsureCameraController();

        // AppBootstrapper 준비 대기 (자동 로그인 포함)
        // null인 경우(씬 직접 실행)는 즉시 통과
        await UniTask.WaitUntil(() => AppBootstrapper.Instance == null || AppBootstrapper.Instance.IsReady);

        // 데이터 매니저 초기화 (로그인 완료 후 CDN 사용 가능)
        await InitMapDataAsync();
        await InitPlayerDataAsync();
        await InitItemDataAsync();

        // UIRoot 로드 대기 (BlockSynergyBridge가 @HUD에 있음)
        if (UIRootBootstrapper.Instance == null)
            await UniTask.WaitUntil(() => UIRootBootstrapper.Instance != null || !this);

        // 블록 시너지 그리드 구성 (UIRoot @HUD에 있는 Bridge 사용)
        var bridge = BlockSynergyBridge.Instance;
        if (bridge == null)
            bridge = Object.FindFirstObjectByType<BlockSynergyBridge>(FindObjectsInactive.Include);
        if (bridge != null)
            bridge.InitializeGridsFromServer();

        if (_run != null && _run.IsRunning)
            await StartCombatAsync();
        else if (Object.FindFirstObjectByType<DebugStageRunPanel>() == null)
            await StartCombatDirectAsync(); // 에디터 직접 실행 fallback (DebugStageRunPanel 없을 때만)

        AppBootstrapper.Instance?.NotifySceneReady();

        // 디버그 스탯 UI 생성
        if (Object.FindFirstObjectByType<DebugStatsBootstrap>() == null)
        {
            var debugGO = new GameObject("@DebugStatsBootstrap");
            debugGO.AddComponent<DebugStatsBootstrap>();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 디버그 그리드 치트 패널 생성
        if (Object.FindFirstObjectByType<DebugGridCheatPanel>(FindObjectsInactive.Include) == null)
        {
            var cheatGO = new GameObject("@DebugGridCheatPanel");
            cheatGO.AddComponent<DebugGridCheatPanel>();
        }
#endif
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;

        if (_run != null)
        {
            _run.OnMapSpawnRequested -= OnMapSpawnRequestedHandler;

            // 씬 이탈 전 현재 무기 슬롯 저장
            if (_run.IsRunning)
            {
                var player = _run.Player;
                if (player != null && player.WeaponManager != null)
                {
                    var wm = player.WeaponManager;
                    var slotData = new WeaponData[wm.SlotCount];
                    for (int i = 0; i < wm.SlotCount; i++)
                        slotData[i] = wm.slots[i]?.runtimeData;
                    _run.SaveWeaponSlots(slotData, wm.CurrentSlotIndex);
                    Debug.Log($"[GameRunBootstrapper] 무기 슬롯 저장: currentSlot={wm.CurrentSlotIndex}");
                }
            }
        }

        if (_currentMapGO != null && Managers.AddressableManager != null)
        {
            Managers.AddressableManager.ReleaseInstance(_currentMapGO);
            _currentMapGO = null;
        }

        _run = null;
    }

    private async UniTask InitMapDataAsync()
    {
        var mapData = Managers.MapData;
        if (mapData == null || mapData.IsInitialized) return;

        try
        {
            await mapData.InitializeAsync();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[GameRunBootstrapper] MapData CDN 예외: {e.Message}");
        }

        // CDN 로드 실패 또는 0개면 오프라인 JSON 폴백
        if (mapData.GetAll().Count == 0)
        {
            Debug.Log("[GameRunBootstrapper] MapData 0개 — Resources/STAGEDATA_MAP.json 폴백");
            var textAsset = Resources.Load<TextAsset>("STAGEDATA_MAP");
            if (textAsset != null)
                mapData.InitializeFromJson(textAsset.text);
            else
                Debug.LogWarning("[GameRunBootstrapper] STAGEDATA_MAP.json not found in Resources");
        }
    }

    private async UniTask InitPlayerDataAsync()
    {
        var playerData = Managers.PlayerData;
        if (playerData == null || playerData.IsInitialized) return;

        try { await playerData.InitializeAsync(); }
        catch (System.Exception e) { Debug.LogWarning($"[GameRunBootstrapper] PlayerData 예외: {e.Message}"); }

        Debug.Log($"[GameRunBootstrapper] PlayerData: {playerData.GetAllPlayers().Count}명");

        var equipData = Managers.ServerEquipment;
        if (equipData != null && !equipData.IsInitialized)
        {
            try { await equipData.InitializeAsync(); }
            catch (System.Exception e) { Debug.LogWarning($"[GameRunBootstrapper] EquipmentData 예외: {e.Message}"); }
        }

        var monsterStatData = Managers.ServerMonsterStat;
        if (monsterStatData != null && !monsterStatData.IsInitialized)
        {
            try { await monsterStatData.InitializeAsync(); }
            catch (System.Exception e) { Debug.LogWarning($"[GameRunBootstrapper] MonsterStatData 예외: {e.Message}"); }
        }
    }

    private async UniTask InitItemDataAsync()
    {
        // ItemSO 레지스트리 초기화 (Addressable)
        try
        {
            var dbPrefab = await Managers.AddressableManager.LoadAssetAsync<ItemSODatabase>("ItemSODatabase");
            if (dbPrefab != null) dbPrefab.RegisterAll();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[GameRunBootstrapper] ItemSODatabase 로드 실패 (SO 없이 진행): {e.Message}");
        }

        var itemData = Managers.ItemData;
        if (itemData != null && !itemData.IsInitialized)
        {
            try { await itemData.InitializeAsync(); }
            catch (System.Exception e) { Debug.LogWarning($"[GameRunBootstrapper] ItemData 예외: {e.Message}"); }
        }

        var blockData = Managers.BlockData;
        if (blockData != null && !blockData.IsInitialized)
        {
            try { await blockData.InitializeAsync(); }
            catch (System.Exception e) { Debug.LogWarning($"[GameRunBootstrapper] BlockData 예외: {e.Message}"); }
        }

        var buffData = Managers.BuffData;
        if (buffData != null && !buffData.IsInitialized)
        {
            try { await buffData.InitializeAsync(); }
            catch (System.Exception e) { Debug.LogWarning($"[GameRunBootstrapper] BuffData 예외: {e.Message}"); }
        }

        var elementEffectData = Managers.ElementEffectData;
        if (elementEffectData != null && !elementEffectData.IsInitialized)
        {
            try { await elementEffectData.InitializeAsync(); }
            catch (System.Exception e) { Debug.LogWarning($"[GameRunBootstrapper] ElementEffectData 예외: {e.Message}"); }
        }

        // 상점 데이터(SHOP_PRICE_DATA) 초기화 — Item/Equipment 레지스트리 로드 이후여야 등급 인덱싱이 정상 작동
        var shopData = Managers.ShopData;
        if (shopData != null && !shopData.IsInitialized)
        {
            if (shopPriceTable == null)
                Debug.LogWarning("[GameRunBootstrapper] shopPriceTable 미할당 — 상점 기본가는 0으로 폴백 (price_override만 적용)");

            try
            {
                var ct = this.GetCancellationTokenOnDestroy();
                await shopData.InitializeAsync(shopPriceTable, ct);
            }
            catch (System.OperationCanceledException) { /* 정상 취소 */ }
            catch (System.Exception e) { Debug.LogWarning($"[GameRunBootstrapper] ShopData 예외: {e.Message}"); }
        }
    }

    private void OnMapSpawnRequestedHandler(string prefabKey) => SpawnMapAsync(prefabKey).Forget();

    private async UniTask SpawnMapAsync(string prefabKey)
    {
        if (string.IsNullOrEmpty(prefabKey)) return;

        if (_isSpawning)
        {
            Debug.LogWarning($"[GameRunBootstrapper] SpawnMapAsync ignored: already spawning. key={prefabKey}");
            return;
        }

        _isSpawning = true;
        try
        {
            if (_currentMapGO != null)
            {
                Managers.AddressableManager.ReleaseInstance(_currentMapGO);
                _currentMapGO = null;
            }

            // ── 블록 맵 생성 시도 ──
            var mapData = Managers.MapData;
            MapRoomEntry roomEntry = mapData?.GetById(prefabKey);

            if (roomEntry != null && !string.IsNullOrEmpty(roomEntry.grid_csv) && HasAnyPalette())
            {
                await SpawnBlockMapAsync(roomEntry);
                return;
            }

            // ── 기존 프리팹 맵 로드 ──
            _currentMapGO = await Managers.AddressableManager.InstantiateAsync(prefabKey, mapRoot);
            BuildMapNavMesh(_currentMapGO);
        }
        finally
        {
            _isSpawning = false;
        }
    }

    private async UniTask SpawnBlockMapAsync(MapRoomEntry roomEntry)
    {
        var spawnInfos = new System.Collections.Generic.Dictionary<Vector2Int, MapDataLoader.CellSpawnInfo>();
        var decorationInfos = new System.Collections.Generic.Dictionary<Vector2Int, string>();
        var grid = MapDataLoader.Parse(roomEntry.grid_csv, spawnInfos, decorationInfos);
        if (grid == null)
        {
            Debug.LogError($"[GameRunBootstrapper] grid_csv 파싱 실패: {roomEntry.room_id}");
            return;
        }

        // 스포너 배치 계획: 확정(M)은 유지, 후보(m) 중 (max - 확정수)개만 랜덤 선택, 나머지는 Floor 치환
        ApplyMonsterSpawnerPlan(grid, roomEntry.max_active_spawners);

        int w = grid.GetLength(0);
        int h = grid.GetLength(1);

        // grid_csv의 P 토큰 위치를 월드 좌표로 변환 → SpawnPlayerAsync에서 소비
        var spawnCell = MapDataLoader.FindFirst(grid, TileType.PlayerSpawn);
        if (spawnCell.x >= 0)
        {
            _pendingPlayerSpawnPos = new Vector3(
                (spawnCell.x - w / 2f + 0.5f) * blockCellSize,
                0f,
                (spawnCell.y - h / 2f + 0.5f) * blockCellSize
            );
            Debug.Log($"[GameRunBootstrapper] PlayerSpawn 셀 ({spawnCell.x},{spawnCell.y}) → world {_pendingPlayerSpawnPos}");
        }
        else
        {
            _pendingPlayerSpawnPos = null;
            Debug.LogWarning($"[GameRunBootstrapper] grid에 PlayerSpawn(P) 토큰 없음 — playerSpawnPoint Transform 폴백 사용: {roomEntry.room_id}");
        }

        // 맵 루트
        var mapGO = new GameObject($"BlockMap_{roomEntry.room_id}");
        mapGO.transform.SetParent(mapRoot, false);
        _currentMapGO = mapGO;

        // 투명 바닥 (플레이어 추락 방지) — 플레이어 이동 기준 Y(=0)에 얇은 판으로 항상 존재
        var safeFloor = MapBuilder.CreateSafeFloor(w, h, blockCellSize, 0f, mapGO.transform);

        // 블록 생성 — blockBaseY로 피봇 보정 (센터 피봇 큐브는 -0.5로 top을 Y=0에 맞춤)
        // 팔레트 우선순위: 챕터 ActiveTheme → roomEntry.palette → roomEntry.theme → Inspector 배열 폴백
        BlockPalette activePalette = null;
        var activeTheme = _run?.ActiveTheme;
        string paletteKey = !string.IsNullOrEmpty(activeTheme) ? activeTheme
            : !string.IsNullOrEmpty(roomEntry.palette) ? roomEntry.palette
            : roomEntry.theme;
        if (!string.IsNullOrEmpty(paletteKey))
            activePalette = await Managers.AddressableManager.TryLoadAssetAsync<BlockPalette>(paletteKey);
        if (activePalette == null)
            activePalette = PickBlockPalette(!string.IsNullOrEmpty(activeTheme) ? activeTheme : roomEntry.theme);
        var blocks = MapBuilder.Build(grid, activePalette, mapGO.transform, blockCellSize, blockBaseY, blockShopStallPrefab);
        Debug.Log($"[GameRunBootstrapper] BlockMap: {roomEntry.room_id} ({w}x{h}), {blocks.Count}블록");

        // 각 스포너 인스턴스에 셀별 설정(maxGrade, totalCount) 주입 (Start() 호출 직전)
        ConfigureMonsterSpawners(blocks, spawnInfos);

        // 방 클리어 카운터 부착 — 모든 스포너의 maxTotalSpawns 합이 킬 목표
        AttachRoomClearController(mapGO, blocks);

        // 등장 연출 — Wall은 기본 연출과 시차를 두어 맵이 깔린 뒤 디졸브로 나타난다.
        //   1) mainBlocks(Floor/Obstacle/스포너 등): entrance 필드로 지정된 기본 연출
        //   2) wallBlocks: 기본 연출 완료 후 대각선 디졸브
        var mainBlocks = new System.Collections.Generic.List<MapBuilder.PlacedBlock>(blocks.Count);
        var wallBlocks = new System.Collections.Generic.List<MapBuilder.PlacedBlock>(64);
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].tileType == TileType.Wall) wallBlocks.Add(blocks[i]);
            else                                     mainBlocks.Add(blocks[i]);
        }

        var entranceCtx = new MapEntranceContext(roomEntry);
        var ct = this.GetCancellationTokenOnDestroy();

        // 디졸브 머티리얼 사전 캐시 + 벽 렌더러 숨김 (메인 연출 전까지 보이지 않게)
        await DissolveEffect.WarmupAsync(ct);
        for (int i = 0; i < wallBlocks.Count; i++)
        {
            var b = wallBlocks[i];
            if (b.instance == null) continue;
            foreach (var r in b.instance.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;
        }

        // 1) 기본 연출 — Wall 제외 전체 블록
        var entrance = MapEntranceRegistry.Resolve(roomEntry.entrance);
        await entrance.PlayAsync(mainBlocks, entranceCtx, ct);

        // 2) Wall 디졸브 등장 — 기본 연출이 끝난 뒤 제자리에서 전체 동시 디졸브 출현
        if (wallBlocks.Count > 0)
        {
            var wallTasks = new System.Collections.Generic.List<UniTask>(wallBlocks.Count);
            for (int i = 0; i < wallBlocks.Count; i++)
            {
                var b = wallBlocks[i];
                if (b.instance == null) continue;
                b.instance.transform.position = b.targetPosition;
                b.instance.transform.rotation = Quaternion.Euler(0f, b.targetRotationY, 0f);
                MapEntranceUtil.SetCollidersEnabled(b.instance, true);
                ApplyWallTransparency(b.instance, 0.72f);
                foreach (var r in b.instance.GetComponentsInChildren<Renderer>(true))
                    r.enabled = true;
                wallTasks.Add(DissolveEffect.PlayAppearAsync(b.instance, 0.4f, ct));
            }
            await UniTask.WhenAll(wallTasks);
        }

        // 투명 바닥 유지 (빈 공간 추락 방지)

        // NavMesh 빌드 — Wall이 자리잡은 후에 수행해야 정확한 경계가 생성됨.
        // 장식 프리팹(나무 등)은 Read/Write OFF 메시를 포함할 수 있으므로 NavMesh 빌드 이후에 배치.
        BuildMapNavMesh(mapGO);

        // 장식(Decoration) 후처리 — NavMesh 빌드 후에 배치.
        // 이중 방어로 NavMeshModifier.ignoreFromBuild = true 를 오브젝트마다 부착한다.
        SpawnDecorations(mapGO, grid, decorationInfos, roomEntry, ResolveRoomTheme(roomEntry.theme));

        // 챕터 필드 구조물 스폰 (디졸브 등장)
        await SpawnFieldPrefabAsync(mapGO, ct);

        // 상점 방이면 ShopRoomController 부착 및 카탈로그 주입
        if (IsShopCategory(roomEntry.category))
            await SetupShopRoomAsync(mapGO, roomEntry);
    }

    private async UniTask SpawnFieldPrefabAsync(GameObject mapParent, CancellationToken ct)
    {
        var key = _run?.ActiveFieldPrefabKey;
        if (string.IsNullOrEmpty(key)) return;

        var prefab = await Managers.AddressableManager.TryLoadAssetAsync<GameObject>(key);
        if (prefab == null)
        {
            Debug.LogWarning($"[GameRunBootstrapper] FieldPrefab '{key}' 로드 실패 — 스킵");
            return;
        }

        ct.ThrowIfCancellationRequested();

        var instance = Instantiate(prefab, mapParent.transform);
        instance.name = $"FieldStructure_{key}";

        await DissolveEffect.PlayAppearAsync(instance, 0.6f, ct);
    }

    private static bool IsShopCategory(string category)
    {
        if (string.IsNullOrEmpty(category)) return false;
        return category.Trim().Equals("Shop", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>스포너 배치 계획 적용 — 확정(M)은 항상 유지, 후보(m)는 max 한도 내에서 랜덤 선택.
    /// 선택되지 않은 후보는 Floor로 치환된다.
    /// 규칙:
    ///   · max ≤ 0           : 모든 M/m 전체 활성 (제한 없음)
    ///   · 확정 0 + 후보 0   : 아무것도 안 함 (max > 0이어도 스포너 미생성)
    ///   · 확정 ≥ max        : 확정 전부 유지, 후보 전부 Floor 치환
    ///   · 확정 &lt; max       : 확정 유지 + 후보 중 (max - 확정수)개 랜덤 선택
    /// </summary>
    private static void ApplyMonsterSpawnerPlan(TileType[,] grid, int max)
    {
        if (grid == null) return;

        var fixedSpots     = MapDataLoader.FindAll(grid, TileType.MonsterSpawn);
        var candidateSpots = MapDataLoader.FindAll(grid, TileType.MonsterSpawnCandidate);

        if (fixedSpots.Count == 0 && candidateSpots.Count == 0)
        {
            if (max > 0)
                Debug.Log($"[GameRunBootstrapper] 스포너 타일 0개 — max={max} 무시, 스포너 사용 안 함");
            return;
        }

        // max ≤ 0: 무제한. 후보는 전부 확정 타입으로 승격시켜 MapBuilder가 동일 처리하게 함
        if (max <= 0)
        {
            foreach (var c in candidateSpots) grid[c.x, c.y] = TileType.MonsterSpawn;
            Debug.Log($"[GameRunBootstrapper] 스포너 max 제한 없음 — 확정 {fixedSpots.Count} + 후보 {candidateSpots.Count} 전부 활성");
            return;
        }

        // 확정이 이미 max 이상이면 후보 전부 Floor
        if (fixedSpots.Count >= max)
        {
            foreach (var c in candidateSpots) grid[c.x, c.y] = TileType.Floor;
            Debug.LogWarning($"[GameRunBootstrapper] 확정 스포너 {fixedSpots.Count}개가 max={max}를 초과/충족 — 후보 {candidateSpots.Count}개 모두 비활성");
            return;
        }

        // 후보 중 필요한 개수만 Fisher-Yates로 선택, 나머지는 Floor 치환 후 선택된 것은 확정으로 승격
        int need = Mathf.Min(max - fixedSpots.Count, candidateSpots.Count);

        for (int i = candidateSpots.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (candidateSpots[i], candidateSpots[j]) = (candidateSpots[j], candidateSpots[i]);
        }

        for (int i = 0; i < need; i++)
        {
            var c = candidateSpots[i];
            grid[c.x, c.y] = TileType.MonsterSpawn; // 확정 승격 — MapBuilder가 오버레이 처리
        }
        for (int i = need; i < candidateSpots.Count; i++)
        {
            var c = candidateSpots[i];
            grid[c.x, c.y] = TileType.Floor;
        }

        Debug.Log($"[GameRunBootstrapper] 스포너 계획 적용 — 확정 {fixedSpots.Count} + 후보 {need}/{candidateSpots.Count} 활성 (max={max})");
    }

    /// <summary>장식(Decoration) 셀에 카탈로그 프리팹을 Instantiate. 테마 일치 카탈로그 우선, 없으면 "*" 폴백.
    /// MapBuilder는 d* 셀을 Floor로 배치하므로 이미 바닥은 깔려있고, 그 위에 오버레이로 얹힌다.</summary>
    private void SpawnDecorations(
        GameObject mapGO,
        TileType[,] grid,
        System.Collections.Generic.IReadOnlyDictionary<Vector2Int, string> decorationInfos,
        MapRoomEntry roomEntry,
        string themeOverride = null)
    {
        if (mapGO == null || grid == null || decorationInfos == null || decorationInfos.Count == 0) return;
        if (decorationCatalogs == null || decorationCatalogs.Length == 0) return;

        string theme = !string.IsNullOrEmpty(themeOverride) ? themeOverride : roomEntry.theme;
        var catalog = PickDecorationCatalog(theme);
        if (catalog == null)
        {
            Debug.LogWarning($"[GameRunBootstrapper] Decoration 카탈로그 없음 (theme='{theme}') — {decorationInfos.Count}개 장식 셀 미배치");
            return;
        }

        int w = grid.GetLength(0);
        int h = grid.GetLength(1);
        var offset = new Vector3((w - 1) * 0.5f * blockCellSize, 0f, (h - 1) * 0.5f * blockCellSize);

        int placed = 0, missing = 0;
        foreach (var kv in decorationInfos)
        {
            var cell = kv.Key;
            var entry = catalog.Get(kv.Value);
            if (entry == null || entry.prefab == null) { missing++; continue; }

            var pos = new Vector3(
                cell.x * blockCellSize - offset.x,
                blockBaseY + 0.5f + entry.yOffset, // 바닥 블록 상단에 얹기
                cell.y * blockCellSize - offset.z);

            Quaternion rot = entry.randomYRotation
                ? Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f)
                : Quaternion.identity;

            var go = Object.Instantiate(entry.prefab, pos, rot, mapGO.transform);
            go.name = $"Deco_{cell.x}_{cell.y}_{kv.Value}";
            if (entry.scale != 1f)
                go.transform.localScale *= entry.scale;

            // NavMesh 빌드에서 제외 — 나무 등 외부 FBX의 Read/Write OFF 메시로 인한 런타임 실패 방지.
            // 루트 + 모든 MeshRenderer 자식에 NavMeshModifier 부착.
            AttachNavMeshIgnore(go);

            placed++;
        }

        Debug.Log($"[GameRunBootstrapper] Decoration 배치 — {placed}개 성공 / {missing}개 카탈로그 미스 (theme={theme}, catalog={catalog.name})");
    }

    private static void AttachNavMeshIgnore(GameObject root)
    {
        if (root == null) return;
        EnsureNavMeshIgnore(root);

        // 자식 중 Renderer가 있는 GameObject에도 부착 — NavMeshSurface가 자식 렌더러를 스캔하므로.
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            EnsureNavMeshIgnore(r.gameObject);
        }
    }

    private static void EnsureNavMeshIgnore(GameObject go)
    {
        var mod = go.GetComponent<Unity.AI.Navigation.NavMeshModifier>();
        if (mod == null)
            mod = go.AddComponent<Unity.AI.Navigation.NavMeshModifier>();
        mod.ignoreFromBuild = true;
    }

    /// <summary>적어도 한 개의 BlockPalette가 연결되어 있는지.</summary>
    private bool HasAnyPalette()
    {
        if (blockPalette != null) return true;
        if (blockPalettes == null) return false;
        for (int i = 0; i < blockPalettes.Length; i++)
            if (blockPalettes[i] != null) return true;
        return false;
    }

    /// <summary>벽 블록 렌더러에 반투명 머티리얼 인스턴스를 적용한다. URP Lit Transparent 모드로 전환.</summary>
    private static void ApplyWallTransparency(GameObject wall, float alpha)
    {
        foreach (var r in wall.GetComponentsInChildren<Renderer>())
        {
            var mat = new Material(r.sharedMaterial);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", 5f);
            mat.SetFloat("_DstBlend", 10f);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
            var col = mat.GetColor("_BaseColor");
            mat.SetColor("_BaseColor", new Color(col.r, col.g, col.b, alpha));
            r.material = mat;
        }
    }

    /// <summary>챕터 테마(ActiveTheme) 우선, 없으면 방별 roomTheme 사용. 둘 다 비면 빈 문자열 → PickBlockPalette가 Default로 폴백.</summary>
    private string ResolveRoomTheme(string roomTheme)
    {
        var chapterTheme = _run != null ? _run.ActiveTheme : null;
        return !string.IsNullOrEmpty(chapterTheme) ? chapterTheme : roomTheme;
    }

    /// <summary>방 테마에 맞는 BlockPalette 선택. 정확한 매칭 우선, 범용 "*" 폴백, 최후엔 단일 blockPalette.</summary>
    private BlockPalette PickBlockPalette(string theme)
    {
        if (blockPalettes != null)
        {
            BlockPalette wildcard = null;
            for (int i = 0; i < blockPalettes.Length; i++)
            {
                var p = blockPalettes[i];
                if (p == null) continue;
                if (p.MatchesTheme(theme) && !string.IsNullOrEmpty(p.ThemeMatch) && p.ThemeMatch != "*")
                    return p; // 정확 매칭 우선
                if (p.ThemeMatch == "*" || string.IsNullOrEmpty(p.ThemeMatch))
                    wildcard = p;
            }
            if (wildcard != null) return wildcard;
        }
        return blockPalette; // 하위호환 fallback
    }

    private DecorationCatalogSO PickDecorationCatalog(string theme)
    {
        if (decorationCatalogs == null) return null;

        DecorationCatalogSO fallback = null;
        foreach (var cat in decorationCatalogs)
        {
            if (cat == null) continue;
            if (cat.MatchesTheme(theme) && !string.IsNullOrEmpty(cat.ThemeMatch) && cat.ThemeMatch != "*")
                return cat; // 정확한 테마 매칭 우선
            if (cat.ThemeMatch == "*" || string.IsNullOrEmpty(cat.ThemeMatch))
                fallback = cat;
        }
        return fallback;
    }

    /// <summary>방 클리어 카운터를 맵 루트에 부착. PlacedBlock에서 MonsterSpawner를 수집해 Initialize.
    /// 스포너가 0개면 컨트롤러를 생성하지 않는다 (상점/이벤트 방 등).</summary>
    private void AttachRoomClearController(
        GameObject mapGO,
        System.Collections.Generic.IReadOnlyList<MapBuilder.PlacedBlock> blocks)
    {
        if (mapGO == null || blocks == null || _run == null) return;

        var spawners = new System.Collections.Generic.List<MonsterSpawner>();
        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            if (b.tileType != TileType.MonsterSpawn && b.tileType != TileType.MonsterSpawnCandidate)
                continue;
            if (b.instance == null) continue;

            var sp = b.instance.GetComponent<MonsterSpawner>();
            if (sp != null) spawners.Add(sp);
        }

        if (spawners.Count == 0) return;

        var controller = mapGO.AddComponent<RoomClearController>();
        controller.Initialize(_run, spawners, luckRollTable);
    }

    /// <summary>MapBuilder.Build 결과 중 스포너 오브젝트에 CellSpawnInfo를 주입.
    /// Start() 호출 전(같은 프레임)에 실행되어야 MonsterSpawner가 올바른 설정으로 SpawnLoop을 시작한다.</summary>
    private static void ConfigureMonsterSpawners(
        System.Collections.Generic.IReadOnlyList<MapBuilder.PlacedBlock> blocks,
        System.Collections.Generic.IReadOnlyDictionary<Vector2Int, MapDataLoader.CellSpawnInfo> infos)
    {
        if (blocks == null || infos == null) return;

        int applied = 0;
        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            if (b.tileType != TileType.MonsterSpawn && b.tileType != TileType.MonsterSpawnCandidate)
                continue;
            if (b.instance == null) continue;

            var spawner = b.instance.GetComponent<MonsterSpawner>();
            if (spawner == null) continue;

            // 후보에서 승격된 셀(MonsterSpawn)이라도 원래 기록된 후보 infos는 좌표 기준으로 찾음
            if (!infos.TryGetValue(b.cell, out var info)) continue;

            spawner.Configure(info.maxGrade, info.totalCount);
            applied++;
        }

        if (applied > 0)
            Debug.Log($"[GameRunBootstrapper] MonsterSpawner 설정 주입 — {applied}개");
    }

    private async UniTask SetupShopRoomAsync(GameObject mapGO, MapRoomEntry roomEntry)
    {
        var controller = mapGO.AddComponent<ShopRoomController>();

        // 신규 경로: SHOP_PRICE_DATA + LuckRollTable 기반 추첨 (catalog는 fallback용)
        var catalog = await LoadShopCatalogAsync(roomEntry.room_id);

        if (luckRollTable == null)
            Debug.LogWarning("[GameRunBootstrapper] LuckRollTable 미할당 — 상점 매대는 fallback 카탈로그를 사용합니다.");

        if (catalog == null && luckRollTable == null)
            Debug.LogWarning($"[GameRunBootstrapper] ShopCatalog/LuckRollTable 모두 없음: {roomEntry.room_id}. 진열대가 비어 있게 됩니다.");

        controller.Initialize(_run, catalog, luckRollTable, shopSlotCount);
    }

    private async UniTask<ShopCatalogSO> LoadShopCatalogAsync(string roomId)
    {
        var mgr = Managers.AddressableManager;

        if (!string.IsNullOrEmpty(roomId))
        {
            var primary = await mgr.TryLoadAssetAsync<ShopCatalogSO>($"ShopCatalog_{roomId}");
            if (primary != null) return primary;
        }

        return await mgr.TryLoadAssetAsync<ShopCatalogSO>("ShopCatalog_Default");
    }

    private void BuildMapNavMesh(GameObject mapRootObject)
    {
        if (!buildRuntimeNavMesh || mapRootObject == null)
            return;

        // PhysicsColliders 기반 — FBX Read/Write OFF 메시(AZURE Cliff/Rock 등)를 우회.
        // All로 씬 전체 물리 콜라이더를 포함하되, Player·Monster 레이어를 제외해
        // 캐릭터 캡슐 콜라이더가 NavMesh에 구멍을 내지 않도록 한다.
        // 프로젝트에 등록된 모든 AgentType에 대해 NavMesh를 빌드해 타입 불일치로
        // isOnNavMesh=false가 되는 현상을 방지한다.
        int excludeMask = ~((1 << LayerMask.NameToLayer("Player")) | (1 << LayerMask.NameToLayer("Monster")));
        int agentCount  = NavMesh.GetSettingsCount();
        for (int i = 0; i < agentCount; i++)
        {
            var agentSettings = NavMesh.GetSettingsByIndex(i);
            var surface = mapRootObject.AddComponent<NavMeshSurface>();
            surface.agentTypeID    = agentSettings.agentTypeID;
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry    = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask      = excludeMask;
            surface.BuildNavMesh();
        }
    }

    private static void DisableSceneBakedNavMesh()
    {
        // Clear pre-baked NavMesh in scene so monsters are not constrained to old small areas.
        NavMesh.RemoveAllNavMeshData();

        var surfaces = Resources.FindObjectsOfTypeAll<NavMeshSurface>();
        for (int i = 0; i < surfaces.Length; i++)
        {
            var surface = surfaces[i];
            if (surface == null) continue;
            if (!surface.gameObject.scene.IsValid()) continue;
            surface.enabled = false;
        }
    }

    /// <summary>
    /// StageMap → GameScene 전환 후 호출.
    /// 이미 실행 중인 런의 선택된 포인트 맵 스폰 + 플레이어 스폰만 수행합니다.
    /// </summary>
    // 정상 런 없이 GameScene을 직접 실행할 때 (에디터 테스트용)
    private async UniTask StartCombatDirectAsync()
    {
        // 에디터 직접 실행 시 Phase를 Running으로 설정 (Tab 등 입력 활성화)
        _run?.ForceRunningForTest();

        // UIRoot가 아직 로드 안 됐으면 대기
        if (UIRootBootstrapper.Instance == null)
            await UniTask.WaitUntil(() => UIRootBootstrapper.Instance != null || !this);

        var uiRoot = UIRootBootstrapper.Instance;
        if (uiRoot != null)
            uiRoot.BindHudToRun(_run);

        _run?.RequestHudMode(HUDIds.Mode.Combat);

        if (!string.IsNullOrEmpty(directCombatMapPrefabKey))
            await SpawnMapAsync(directCombatMapPrefabKey);

        var player = await SpawnPlayerAsync(playerPrefabKey);
        if (player != null)
        {
            // 에디터 직접 실행 시 기본 무기 자동 장착
            if (player.WeaponManager != null && !player.WeaponManager.HasWeapon)
            {
                // 로비에서 선택한 무기가 있으면 복원, 없으면 기본 무기
                var loadout = AppBootstrapper.Instance?.Loadout;
                var weaponSO = loadout?.WeaponSlot0;
                string weaponKey = weaponSO != null ? null : "T1_Bow";

                if (weaponSO != null)
                {
                    var wd = WeaponData.FromSO(weaponSO);
                    await PreloadWeaponClipsAsync(wd);
                    await player.WeaponManager.AcquireWeaponAsync(wd);
                    Debug.Log($"[GameRunBootstrapper] 테스트: 로드아웃 무기 장착 ({weaponSO.displayName})");
                }
                else
                {
                    Debug.Log($"[GameRunBootstrapper] 테스트: 기본 무기 장착 ({weaponKey})");
                    var handle = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<WeaponSO>(weaponKey);
                    await handle.Task;
                    if (handle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded && handle.Result != null)
                    {
                        var wd = WeaponData.FromSO(handle.Result);
                        await PreloadWeaponClipsAsync(wd);
                        await player.WeaponManager.AcquireWeaponAsync(wd);
                    }
                }
            }

            // 무기 장착 완료 후 숨김 → 카메라 인트로 → 등장 연출
            SetupEntrance(player);
            _run?.BindPlayer(player);
        }

        // Guard: force combat HUD once more after player/map bootstrap settles.
        _run?.RequestHudMode(HUDIds.Mode.Combat);
    }

    public async UniTask StartCombatAsync()
    {
        var run = _run;
        if (run == null)
        {
            Debug.LogError("[GameRunBootstrapper] StartCombatAsync failed: run is null.");
            return;
        }

        run.RequestSpawnCurrentPointMap();

        var uiRoot = UIRootBootstrapper.Instance;
        if (uiRoot != null)
            uiRoot.BindHudToRun(run);

        run.RequestHudMode(HUDIds.Mode.Combat);

        var player = await SpawnPlayerAsync(playerPrefabKey);
        if (player != null)
        {
            SetupEntrance(player);
            run.BindPlayer(player);
        }

        // Guard: some room/bootstrap flows can override HUD mode after early request.
        run.RequestHudMode(HUDIds.Mode.Combat);
    }

    public async UniTask StartRunAsync(ChapterId chapter)
    {
        var run = _run;
        if (run == null)
        {
            Debug.LogError("[GameRunBootstrapper] StartRunAsync failed: run is null.");
            return;
        }

        await run.StartNewRunAsync(chapter, LoadTextAsset);

        static UniTask<TextAsset> LoadTextAsset(string key) =>
            Managers.AddressableManager.LoadAssetAsync<TextAsset>(key);

        if (!run.IsRunning || run.RoomManager == null || !run.RoomManager.IsInitialized)
        {
            Debug.LogWarning("[GameRunBootstrapper] StartRunAsync aborted: RoomManager not ready.");
            return;
        }

        _points = FindObjectsOfType<StagePointUI>(true);
        run.RegisterPoints(_points);
        run.ResolveAllPointsAndSetStart();
        run.RequestSpawnCurrentPointMap();

        UIRootBootstrapper.Instance?.BindHudToRun(run);
        run.RequestHudMode(HUDIds.Mode.Combat);

        var player = await SpawnPlayerAsync(playerPrefabKey);
        if (player != null)
        {
            // 무기가 없으면 기본 무기 자동 장착
            if (player.WeaponManager != null && !player.WeaponManager.HasWeapon)
            {
                Debug.Log("[GameRunBootstrapper] StartRunAsync: 기본 무기 장착");
                var handle = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<WeaponSO>("T1_Bow");
                await handle.Task;
                if (handle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded && handle.Result != null)
                {
                    var wd = WeaponData.FromSO(handle.Result);
                    await PreloadWeaponClipsAsync(wd);
                    await player.WeaponManager.AcquireWeaponAsync(wd);
                }
            }

            // 무기 장착 완료 후 숨김 → 카메라 인트로 → 등장 연출
            SetupEntrance(player);
            run.BindPlayer(player);
        }

        // Guard: ensure HUD remains in combat mode after late binds complete.
        run.RequestHudMode(HUDIds.Mode.Combat);
    }

    private async UniTask<PlayerController> SpawnPlayerAsync(string prefabKey)
    {
        // PrepPanel에서 선택한 캐릭터 키가 있으면 우선 사용
        var overrideKey = Managers.CharacterData?.PlayerPrefabKey;
        if (!string.IsNullOrEmpty(overrideKey))
            prefabKey = overrideKey;

        if (string.IsNullOrEmpty(prefabKey))
        {
            Debug.LogError("[GameRunBootstrapper] SpawnPlayerAsync failed: prefabKey is empty");
            return null;
        }

        var prefab = await Managers.AddressableManager.LoadAssetAsync<GameObject>(prefabKey);
        if (prefab == null)
        {
            Debug.LogError($"[GameRunBootstrapper] Player prefab load failed: key={prefabKey}");
            return null;
        }

        // grid_csv의 P 토큰 위치 우선 — 없으면 인스펙터 playerSpawnPoint Transform 폴백
        Vector3 pos;
        if (_pendingPlayerSpawnPos.HasValue)
        {
            pos = _pendingPlayerSpawnPos.Value;
            _pendingPlayerSpawnPos = null;
        }
        else
        {
            pos = playerSpawnPoint != null ? playerSpawnPoint.position : Vector3.zero;
        }
        Quaternion rot = playerSpawnPoint != null ? playerSpawnPoint.rotation : Quaternion.identity;

        var go = Instantiate(prefab, pos, rot);
        var player = go.GetComponent<PlayerController>();

        if (player == null)
        {
            Debug.LogError("[GameRunBootstrapper] Spawned player has no PlayerController");
            Destroy(go);
            return null;
        }

        // CharacterBase.Awake()가 async void이므로 InitAsync가 완료될 때까지 대기
        // (WeaponManager는 InitAsync 중에 설정되므로 null이 아닐 때 초기화 완료)
        await Cysharp.Threading.Tasks.UniTask.WaitUntil(
            () => player.WeaponManager != null,
            cancellationToken: destroyCancellationToken);

        var run = AppBootstrapper.Instance?.CurrentRun;
        var wm = player.WeaponManager;
        if (wm != null)
        {
            // 저장된 슬롯이 있으면 복원 (StageMap 복귀)
            if (run?.SavedWeaponSlots != null)
            {
                for (int i = 0; i < run.SavedWeaponSlots.Length; i++)
                {
                    if (run.SavedWeaponSlots[i] != null)
                    {
                        await PreloadWeaponClipsAsync(run.SavedWeaponSlots[i]);
                        await wm.AcquireWeaponAsync(run.SavedWeaponSlots[i], autoEquip: true);
                    }
                }
                if (run.SavedCurrentSlotIndex >= 0)
                    await wm.SwitchToSlotAsync(run.SavedCurrentSlotIndex);
                Debug.Log($"[GameRunBootstrapper] 무기 슬롯 복원: currentSlot={run.SavedCurrentSlotIndex}");
            }
            else
            {
                // 런 최초 진입: 로비에서 선택한 무기 장착
                var loadout = AppBootstrapper.Instance?.Loadout;
                if (loadout?.WeaponSlot0 != null)
                {
                    var weaponData = new WeaponData(loadout.WeaponSlot0);
                    await PreloadWeaponClipsAsync(weaponData);
                    await wm.AcquireWeaponAsync(weaponData, autoEquip: true);
                    Debug.Log($"[GameRunBootstrapper] 메인 무기 장착: {loadout.WeaponSlot0.displayName}");
                }
                // 서브 장비 장착
                if (loadout?.WeaponSlot1 != null)
                {
                    var subData = new WeaponData(loadout.WeaponSlot1);
                    await PreloadWeaponClipsAsync(subData);
                    await wm.AcquireWeaponAsync(subData, autoEquip: true);
                    Debug.Log($"[GameRunBootstrapper] 서브 장비 장착: {loadout.WeaponSlot1.displayName}");
                }
            }
        }

        return player;
    }

    /// <summary>플레이어에 등장 연출 컨트롤러를 부착하고 초기화한다.</summary>
    private void SetupEntrance(PlayerController player)
    {
        if (player == null) return;

        // 캐릭터 고유 연출 우선, 없으면 기본 연출로 폴백
        var behaviour = player.CharacterData?.playerEntrance ?? defaultPlayerEntrance;

        var entrance = player.GetComponent<PlayerEntranceController>();
        if (entrance == null)
            entrance = player.gameObject.AddComponent<PlayerEntranceController>();
        entrance.Initialize(player, behaviour);
    }

    private static void EnsureCameraController()
    {
        var cam = Camera.main;
        if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
        if (cam == null) return;

        if (cam.GetComponent<GameCameraController>() == null)
            cam.gameObject.AddComponent<GameCameraController>();
    }

    /// <summary>무기 데이터의 애니메이션 클립을 AcquireWeapon 전에 로드</summary>
    private static async UniTask PreloadWeaponClipsAsync(WeaponData data)
    {
        var animSet = data?.animationSet as WeaponAnimationSetSO;
        if (animSet == null) return;

        var keys = new System.Collections.Generic.List<string>();
        foreach (var mapping in animSet.GetAllMappings())
        {
            if (!string.IsNullOrEmpty(mapping.addressableKey))
                keys.Add(mapping.addressableKey);
        }

        if (keys.Count > 0)
            await Managers.AnimationResources.PreloadClipsAsync(keys);
    }
}
