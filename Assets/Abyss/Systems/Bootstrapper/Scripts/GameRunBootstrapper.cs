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

    [Header("Start Room")]
    [Tooltip("로비에서 바로 GameScene 진입 시 로드할 스타트 방 맵 키. 비워두면 기존 에디터 직접 실행 fallback으로 동작.")]
    [SerializeField] private string startRoomMapKey = "";

    [Tooltip("스타트 방 진입 시 재생할 대화 시퀀스 SO. 서버 CSV에 'StartRoom' 시퀀스가 없을 때 폴백으로 사용.")]
    [SerializeField] private DialogueSequenceSO startRoomDialogueSO;

    /// <summary>서버에서 로드하는 대화 시퀀스 ID. 비워두면 서버 데이터를 사용하지 않음.</summary>
    private const string StartRoomSequenceId = "StartRoom";

    /// <summary>스타트 방 씬으로 진입한 상태. Loadout 준비 여부와 무관. 디버그 스킵 등에 사용.</summary>
    public bool IsStartRoomScene => AppBootstrapper.Instance != null
        && !string.IsNullOrEmpty(startRoomMapKey);

    /// <summary>스타트 방 모드 여부. 로비를 거쳐 진입했고 Wisp가 캐릭터를 선택하기 전까지 true.
    /// AppBootstrapper.Instance가 null이면 에디터 직접 실행으로 간주해 false를 반환한다.</summary>
    public bool IsInStartRoom => IsStartRoomScene
        && !(AppBootstrapper.Instance.Loadout?.IsReady ?? false);

    [Tooltip("스타트 방 캐릭터 픽업 프리팹 배열. CP 타일 발견 순서대로 매핑됨. 각 프리팹에 StartRoomPickup(Character) + CharacterData 설정 필요.")]
    [SerializeField] private GameObject[] characterPickupPrefabs;

    [Tooltip("스타트 방 무기 픽업 프리팹 배열. WP 타일 발견 순서대로 매핑됨. 각 프리팹에 StartRoomPickup(Weapon) + WeaponSO 설정 필요.")]
    [SerializeField] private GameObject[] weaponPickupPrefabs;

    [Tooltip("스타트 방 탈출 게이트 프리팹. SG 타일 위치에 배치됨. StartRoomGate 컴포넌트 필요.")]
    [SerializeField] private GameObject startGatePrefab;

    [Tooltip("스타트 방에서 캐릭터 선택 전 조작할 Wisp 프리팹. 비워두면 playerPrefabKey 폴백.")]
    [SerializeField] private GameObject wispPrefab;

    [Header("Block Map Gen")]
    [SerializeField, Tooltip("단일 팔레트 (fallback). blockPalettes에 테마 매칭이 없으면 이 값 사용.")]
    private BlockPalette blockPalette;

    [SerializeField, Tooltip("테마별 블록 팔레트 배열. MapRoomEntry.theme와 BlockPalette.themeMatch가 일치하는 첫 항목이 사용됨.")]
    private BlockPalette[] blockPalettes;

    [SerializeField] private float blockCellSize = 1f;

    [SerializeField, Tooltip("블록 배치 Y 오프셋. 바닥 블록 scale.y=0.2(반높이 0.1)이면 -0.1. 프리팹 피봇이 바닥이면 0.")]
    private float blockBaseY = -0.1f;

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

    [Header("Room Clear Effects")]
    [SerializeField, Tooltip("방 클리어 시 맵 중앙에 재생할 이펙트 프리팹.")]
    private GameObject clearEndEffectPrefab;

    [SerializeField, Tooltip("EndEffect 후 보상 상호작용 오브젝트로 사용할 이펙트 프리팹.")]
    private GameObject clearEndEffect2Prefab;

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

        // IsRunning이 true면 StageMap을 거쳐 전투 씬으로 진입한 것 → 전투 시작
        // IsInStartRoom이면 로비를 거쳐 스타트 방으로 진입 → Wisp 모드 (에디터 직접 실행 시 false)
        if (_run != null && _run.IsRunning)
            await StartCombatAsync();
        else if (IsInStartRoom)
            await StartRoomAsync();
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
            Debug.Log("[GameRunBootstrapper] MapData 0개 — Addressables/STAGEDATA_MAP.json 폴백");
            var textAsset = await Managers.AddressableManager.TryLoadAssetAsync<TextAsset>("STAGEDATA_MAP");
            if (textAsset != null)
                mapData.InitializeFromJson(textAsset.text);
            else
                Debug.LogWarning("[GameRunBootstrapper] STAGEDATA_MAP.json not found in Addressables");
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

        // 입장 디졸브 연출 중 몬스터 스폰 방지 — 첫 await 전 같은 프레임에 비활성화해 Start() 호출을 지연
        var deferredSpawners = DisableSpawnersBeforeEntrance(blocks);

        // NavMesh 빌드 — MapBuilder.Build 직후(Wall 배치 완료) 수행.
        // AttachRoomClearController → RoomWaveController.StartWaveAsync는 같은 프레임에 동기적으로
        // TryGetSpawnPosition(NavMesh.SamplePosition)을 호출하므로, NavMesh가 먼저 준비되어야 한다.
        // 장식 프리팹(나무 등)은 Read/Write OFF 메시를 포함할 수 있으므로 NavMesh 빌드 이후에 배치.
        BuildMapNavMesh(mapGO);

        var ct = this.GetCancellationTokenOnDestroy();

        // 벽 투명도 사전 적용
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].tileType == TileType.Wall && blocks[i].instance != null)
                ApplyWallTransparency(blocks[i].instance, 0.72f);
        }

        // 렌더러 선숨김 — 카메라 페이드인 중 블록이 팝업으로 보이지 않도록.
        // DissolveEntrance도 동일하게 숨기지만, 그 전에 화면이 열리면 순간 팝업이 발생한다.
        HideAllBlockRenderers(blocks);

        // IntroFade(sortingOrder=9999)가 아직 불투명하게 UI_SceneLoading을 덮고 있는 이 시점에
        // 로딩 커버를 해제한다. IntroFade 뒤에서 UI_SceneLoading이 조용히 사라지므로 플레이어 눈에 안 보임.
        AppBootstrapper.Instance?.NotifySceneReady();

        // 카메라 페이드인 + Dissolve 머티리얼 프리로드를 병렬로 수행
        // → 화면이 열린 상태에서 맵 등장 디졸브를 플레이어가 볼 수 있도록
        var mapCenter = mapGO != null ? mapGO.transform.position : Vector3.zero;
        await UniTask.WhenAll(
            DissolveEffect.WarmupAsync(ct),
            GameCameraController.Instance?.PrepareMapViewAsync(mapCenter, 0.4f, ct) ?? UniTask.CompletedTask);

        var entranceCtx = new MapEntranceContext(roomEntry);
        await MapEntranceRegistry.Resolve(roomEntry.entrance).PlayAsync(blocks, entranceCtx, ct);

        // 입장 연출 완료 후 방 클리어 컨트롤러 부착 + 스포너 활성화 → Start() 실행 → 몬스터 스폰 시작
        AttachRoomClearController(mapGO, blocks);
        for (int i = 0; i < deferredSpawners.Count; i++)
            if (deferredSpawners[i] != null) deferredSpawners[i].enabled = true;

        // 장식(Decoration) 후처리 — NavMesh 빌드 후에 배치.
        // 이중 방어로 NavMeshModifier.ignoreFromBuild = true 를 오브젝트마다 부착한다.
        SpawnDecorations(mapGO, grid, decorationInfos, roomEntry, ct, ResolveRoomTheme(roomEntry.theme));

        // 챕터 필드 구조물 스폰 (디졸브 등장)
        await SpawnFieldPrefabAsync(mapGO, ct);

        // 상점 방이면 ShopRoomController 부착 및 카탈로그 주입
        if (IsShopCategory(roomEntry.category))
            await SetupShopRoomAsync(mapGO, roomEntry);

        // 스타트 방 전용 오브젝트 (캐릭터/무기 픽업, 탈출 게이트)
        if (IsStartCategory(roomEntry.category))
            SpawnStartRoomObjects(mapGO, grid, w, h);
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

    private static bool IsStartCategory(string category)
    {
        if (string.IsNullOrEmpty(category)) return false;
        return category.Trim().Equals("Start", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>스타트 방 전용 픽업/게이트 오브젝트를 그리드 좌표 기반으로 스폰.
    /// CP → characterPickupPrefabs[i] (발견 순서), WP → weaponPickupPrefabs[i], SG → startGatePrefab.</summary>
    private void SpawnStartRoomObjects(GameObject mapParent, TileType[,] grid, int w, int h)
    {
        var offset = new Vector3((w / 2f - 0.5f) * blockCellSize, 0f, (h / 2f - 0.5f) * blockCellSize);

        // 캐릭터 픽업
        var cpCells = MapDataLoader.FindAll(grid, TileType.CharacterPickup);
        for (int i = 0; i < cpCells.Count; i++)
        {
            if (characterPickupPrefabs == null || i >= characterPickupPrefabs.Length || characterPickupPrefabs[i] == null)
            {
                Debug.LogWarning($"[GameRunBootstrapper] CP 타일 {i}에 대한 characterPickupPrefabs[{i}] 미할당 — 스킵");
                continue;
            }
            var pos = new Vector3(cpCells[i].x * blockCellSize - offset.x, 0f, cpCells[i].y * blockCellSize - offset.z);
            var go = Instantiate(characterPickupPrefabs[i], pos, Quaternion.identity, mapParent.transform);
            go.name = $"CharPickup_{i}";
        }

        // 무기 픽업
        var wpCells = MapDataLoader.FindAll(grid, TileType.WeaponPickup);
        for (int i = 0; i < wpCells.Count; i++)
        {
            if (weaponPickupPrefabs == null || i >= weaponPickupPrefabs.Length || weaponPickupPrefabs[i] == null)
            {
                Debug.LogWarning($"[GameRunBootstrapper] WP 타일 {i}에 대한 weaponPickupPrefabs[{i}] 미할당 — 스킵");
                continue;
            }
            var pos = new Vector3(wpCells[i].x * blockCellSize - offset.x, 0f, wpCells[i].y * blockCellSize - offset.z);
            var go = Instantiate(weaponPickupPrefabs[i], pos, Quaternion.identity, mapParent.transform);
            go.name = $"WeaponPickup_{i}";
        }

        // 탈출 게이트
        var sgCells = MapDataLoader.FindAll(grid, TileType.StartGate);
        if (sgCells.Count > 0)
        {
            if (startGatePrefab != null)
            {
                var c = sgCells[0];
                var pos = new Vector3(c.x * blockCellSize - offset.x, 0f, c.y * blockCellSize - offset.z);
                var go = Instantiate(startGatePrefab, pos, Quaternion.identity, mapParent.transform);
                go.name = "StartGate";
            }
            else
            {
                Debug.LogWarning("[GameRunBootstrapper] SG 타일 발견했지만 startGatePrefab 미할당 — 스킵");
            }
        }

        Debug.Log($"[GameRunBootstrapper] 스타트 방 오브젝트 — CP:{cpCells.Count} WP:{wpCells.Count} SG:{sgCells.Count}");
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
        System.Threading.CancellationToken ct,
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
            DissolveEffect.PlayAppearAsync(go, 0.6f, ct).Forget();

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

    /// <summary>방 클리어 카운터를 맵 루트에 부착. PlacedBlock에서 MonsterSpawner/BossSpawner를 수집해 Initialize.
    /// 스포너가 하나도 없으면 컨트롤러를 생성하지 않는다 (상점/이벤트 방 등).</summary>
    private void AttachRoomClearController(
        GameObject mapGO,
        System.Collections.Generic.IReadOnlyList<MapBuilder.PlacedBlock> blocks)
    {
        if (mapGO == null || blocks == null || _run == null) return;

        var spawners = new System.Collections.Generic.List<MonsterSpawner>();
        BossSpawner bossSpawner = null;

        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            if (b.instance == null) continue;

            if (b.tileType == TileType.MonsterSpawn || b.tileType == TileType.MonsterSpawnCandidate)
            {
                var sp = b.instance.GetComponent<MonsterSpawner>();
                if (sp != null) spawners.Add(sp);
            }
            else if (b.tileType == TileType.BossSpawn)
            {
                // 보스 스포너는 방당 1개. 여럿이면 첫 번째만 사용.
                if (bossSpawner == null)
                    bossSpawner = b.instance.GetComponent<BossSpawner>();
            }
        }

        if (spawners.Count == 0 && bossSpawner == null) return;

        var controller = mapGO.AddComponent<RoomWaveController>();
        controller.Initialize(_run, spawners, bossSpawner, luckRollTable, clearEndEffectPrefab, clearEndEffect2Prefab);
    }

    /// <summary>입장 연출 전 MonsterSpawner·BossSpawner를 비활성화해 Start() 호출을 연출 종료 이후로 지연시킨다.</summary>
    private static System.Collections.Generic.List<MonoBehaviour> DisableSpawnersBeforeEntrance(
        System.Collections.Generic.IReadOnlyList<MapBuilder.PlacedBlock> blocks)
    {
        var list = new System.Collections.Generic.List<MonoBehaviour>();
        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            if (b.instance == null) continue;

            if (b.tileType == TileType.MonsterSpawn || b.tileType == TileType.MonsterSpawnCandidate)
            {
                if (b.instance.TryGetComponent<MonsterSpawner>(out var ms))
                { ms.enabled = false; list.Add(ms); }
            }
            else if (b.tileType == TileType.BossSpawn)
            {
                if (b.instance.TryGetComponent<BossSpawner>(out var bs))
                { bs.enabled = false; list.Add(bs); }
            }
        }
        return list;
    }

    private static void HideAllBlockRenderers(
        System.Collections.Generic.IReadOnlyList<MapBuilder.PlacedBlock> blocks)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].instance == null) continue;
            var rs = blocks[i].instance.GetComponentsInChildren<Renderer>(true);
            for (int j = 0; j < rs.Length; j++)
                rs[j].enabled = false;
        }
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

            if (info.waves != null && info.waves.Length >= 2)
                spawner.ConfigureWaves(info.waves);  // 웨이브 배열 모드
            else
                spawner.Configure(info.maxGrade, info.totalCount); // 레거시 단일 등급/수량
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
        Managers.Sound?.PlayBgmAsync(SoundKey.Bgm.InGame).Forget();

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

    /// <summary>
    /// 로비 → GameScene 직행 시 스타트 방을 로드하고 플레이어를 스폰한다.
    /// 세션은 IsRunning=false 상태를 유지해 StageMap이 새 런으로 정상 시작되도록 한다.
    /// 캐릭터/무기 선택은 StartRoomPickup/StartRoomGate로 처리.
    /// </summary>
    private async UniTask StartRoomAsync()
    {
        // 대화·위스프 구간 동안 HUD 숨김 — 캐릭터 획득 시점에 복원
        UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(true);

        await SpawnMapAsync(startRoomMapKey);

        await ShowStartRoomDialogueAsync();

        if (wispPrefab != null)
            SpawnWisp();
        else
            Debug.LogError("[GameRunBootstrapper] wispPrefab 미할당 — 스타트 방에서 캐릭터를 생성할 수 없습니다.");
    }

    private async UniTask ShowStartRoomDialogueAsync()
    {
        // 서버 CSV 우선, 없으면 인스펙터 SO 폴백
        var dlgMgr = Managers.DialogueData;
        if (dlgMgr != null && !dlgMgr.IsInitialized)
            await dlgMgr.InitializeAsync();

        DialogueLine[] lines = dlgMgr?.GetLines(StartRoomSequenceId)
                               ?? startRoomDialogueSO?.Lines;
        if (lines == null || lines.Length == 0) return;

        var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_DialoguePopup>();
        if (popup == null) return;

        try
        {
            await popup.ShowAsync(lines);
        }
        catch (System.OperationCanceledException) { }
    }

    private void SpawnWisp()
    {
        Vector3 pos = _pendingPlayerSpawnPos ?? Vector3.zero;
        _pendingPlayerSpawnPos = null;

        var go = Instantiate(wispPrefab, pos, Quaternion.identity);
        go.name = "@Wisp";

        // WispCameraFollow.Start()보다 먼저 동기 호출로 신뢰성 확보
        GameCameraController.Instance?.ActivateForStartRoom(go.transform);

        Debug.Log($"[GameRunBootstrapper] Wisp 스폰: {pos}");
    }

    /// <summary>스타트 방에서 캐릭터 선택 시 호출. 해당 위치에 PlayerController를 스폰하고 Wisp를 제거한다.</summary>
    public async UniTaskVoid SpawnCharacterInStartRoomAsync(
        string prefabKey, Vector3 pos, Quaternion rot, WispController wisp)
    {
        var prefab = await Managers.AddressableManager.LoadAssetAsync<GameObject>(prefabKey);
        if (prefab == null)
        {
            Debug.LogError($"[GameRunBootstrapper] 스타트 방 캐릭터 프리팹 로드 실패: {prefabKey}");
            return;
        }

        var go = Instantiate(prefab, pos, rot);
        var player = go.GetComponent<PlayerController>();
        if (player == null)
        {
            Debug.LogError($"[GameRunBootstrapper] PlayerController 없음: {prefabKey}");
            Destroy(go);
            return;
        }

        // InitAsync 완료 대기 (SetupCamera 포함 — Cinemachine 타겟이 player로 전환됨)
        await UniTask.WaitUntil(
            () => player.WeaponManager != null,
            cancellationToken: destroyCancellationToken);

        // Wisp 위치에서 player 쪽으로 카메라 줌인 연출
        GameCameraController.Instance?.PlayStartRoomIntroAsync(player.transform).Forget();

        // Wisp 제거 (카메라 인트로가 시작된 후 — 인트로는 현재 카메라 위치에서 시작하므로 순서 중요)
        if (wisp != null)
            Destroy(wisp.gameObject);

        Debug.Log($"[GameRunBootstrapper] 스타트 방 캐릭터 스폰 완료: {prefabKey} at {pos}");
    }

    public async UniTask StartCombatAsync()
    {
        var run = _run;
        if (run == null)
        {
            Debug.LogError("[GameRunBootstrapper] StartCombatAsync failed: run is null.");
            return;
        }

        Managers.Sound?.PlayBgmAsync(SoundKey.Bgm.InGame).Forget();

        // 맵 스폰을 awaited로 처리 — 맵 생성 완료 후 몬스터 스포너가 초기화되므로
        // 플레이어 스폰 전에 반드시 맵이 준비되어야 한다.
        // RequestSpawnCurrentPointMap의 RunState 전환 side effect를 유지하면서
        // SpawnMapAsync는 직접 await한다.
        UniTask mapTask = UniTask.CompletedTask;
        _run.OnMapSpawnRequested -= OnMapSpawnRequestedHandler;
        void captureMap(string key) { mapTask = SpawnMapAsync(key); }
        _run.OnMapSpawnRequested += captureMap;
        run.RequestSpawnCurrentPointMap();
        _run.OnMapSpawnRequested -= captureMap;
        _run.OnMapSpawnRequested += OnMapSpawnRequestedHandler;
        await mapTask;

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
        // 우선순위: Loadout(Wisp 선택) → CharacterDataManager(PrepPanel) → Inspector 기본값(에디터 테스트용)
        var loadoutKey = AppBootstrapper.Instance?.Loadout?.CharacterPrefabKey;
        if (!string.IsNullOrEmpty(loadoutKey))
            prefabKey = loadoutKey;
        else
        {
            var overrideKey = Managers.CharacterData?.PlayerPrefabKey;
            if (!string.IsNullOrEmpty(overrideKey))
                prefabKey = overrideKey;
        }

        if (string.IsNullOrEmpty(prefabKey))
        {
            Debug.LogWarning("[GameRunBootstrapper] prefabKey가 비어 있어 'Knight'로 폴백합니다 (에디터 테스트용)");
            prefabKey = "Knight";
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
            // 저장된 슬롯이 있으면 복원 (StageMap 복귀 or 이어하기)
            if (run?.SavedWeaponSlots != null)
            {
                for (int i = 0; i < run.SavedWeaponSlots.Length; i++)
                {
                    if (run.SavedWeaponSlots[i] != null)
                    {
                        PlayerWeaponManager.ApplyServerOverride(run.SavedWeaponSlots[i]);
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
                    PlayerWeaponManager.ApplyServerOverride(weaponData);
                    await PreloadWeaponClipsAsync(weaponData);
                    await wm.AcquireWeaponAsync(weaponData, autoEquip: true);
                    Debug.Log($"[GameRunBootstrapper] 메인 무기 장착: {loadout.WeaponSlot0.displayName}");
                }
                // 서브 장비 장착
                if (loadout?.WeaponSlot1 != null)
                {
                    var subData = new WeaponData(loadout.WeaponSlot1);
                    PlayerWeaponManager.ApplyServerOverride(subData);
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

    /// <summary>스타트 방에서 무기 선택 즉시 해당 플레이어에게 장착.</summary>
    public static async UniTask EquipWeaponToPlayerAsync(WeaponSO weaponSO, PlayerController player)
    {
        var wm = player?.WeaponManager;
        if (wm == null || weaponSO == null) return;
        var weaponData = new WeaponData(weaponSO);
        await PreloadWeaponClipsAsync(weaponData);
        await wm.AcquireWeaponAsync(weaponData, autoEquip: true);
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
