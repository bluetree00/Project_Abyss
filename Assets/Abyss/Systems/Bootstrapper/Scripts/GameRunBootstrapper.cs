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

    [Header("Block Map Gen")]
    [SerializeField] private BlockPalette blockPalette;
    [SerializeField] private float blockCellSize = 1f;

    private StagePointUI[] _points;
    private GameObject _currentMapGO;
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
        // 데이터 매니저 초기화
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
    }

    private async UniTask InitItemDataAsync()
    {
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

            if (roomEntry != null && !string.IsNullOrEmpty(roomEntry.grid_csv) && blockPalette != null)
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
        var grid = MapDataLoader.Parse(roomEntry.grid_csv);
        if (grid == null)
        {
            Debug.LogError($"[GameRunBootstrapper] grid_csv 파싱 실패: {roomEntry.room_id}");
            return;
        }

        int w = grid.GetLength(0);
        int h = grid.GetLength(1);

        // 맵 루트
        var mapGO = new GameObject($"BlockMap_{roomEntry.room_id}");
        mapGO.transform.SetParent(mapRoot, false);
        _currentMapGO = mapGO;

        // 투명 바닥 (플레이어 추락 방지)
        var safeFloor = MapBuilder.CreateSafeFloor(w, h, blockCellSize, 0f, mapGO.transform);

        // 블록 생성
        var blocks = MapBuilder.Build(grid, blockPalette, mapGO.transform, blockCellSize, 0f);
        Debug.Log($"[GameRunBootstrapper] BlockMap: {roomEntry.room_id} ({w}x{h}), {blocks.Count}블록");

        // Scatter → Return 연출
        await MapPresenter.PlayEntrance(
            blocks,
            roomEntry.scatter_range,
            roomEntry.return_duration);

        // 투명 바닥 유지 (빈 공간 추락 방지)

        // NavMesh 빌드
        BuildMapNavMesh(mapGO);
    }

    private void BuildMapNavMesh(GameObject mapRootObject)
    {
        if (!buildRuntimeNavMesh || mapRootObject == null)
            return;

        var surface = mapRootObject.GetComponent<NavMeshSurface>();
        if (surface == null)
            surface = mapRootObject.AddComponent<NavMeshSurface>();

        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
        surface.layerMask = ~0;
        surface.BuildNavMesh();
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
            _run?.BindPlayer(player);

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
            run.BindPlayer(player);

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
            run.BindPlayer(player);

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

        Vector3 pos = playerSpawnPoint != null ? playerSpawnPoint.position : Vector3.zero;
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
