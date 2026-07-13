using System.Threading;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Cysharp.Threading.Tasks;
using TMPro;
using RelicFairy.Monster;

public sealed class GameRunBootstrapper : MonoBehaviour
{
    public static GameRunBootstrapper Instance { get; private set; }

    [SerializeField] private string playerPrefabKey = "PlayerCharacter";
    [Tooltip("시작방에서 바로 스폰할 CombatGirl 베이스 몸 Addressables 키 (유물 없는 상태). 유물은 시작방 유물 오브젝트에서 획득.")]
    [SerializeField] private string startBodyKey = "PlayerCharacter";
    [SerializeField] private string debugDefaultWeaponKey = "T3_Katana";
    [Tooltip("Loadout에 유물이 없을 때(에디터 직접 전투 테스트) 적용할 기본 유물 클래스. 비우면 유물 미적용. 시작방 경로에는 영향 없음.")]
    [SerializeField] private RelicClassSO debugDefaultRelic;
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

    [Tooltip("true면 zone_layout_key의 zone_index=0을 스타트 방으로 사용. 캐릭터/무기 선택 후 나머지 존(1-25)을 게이트에서 스폰.")]
    [SerializeField] private bool startWithZoneLayout = true;

    [Header("ProcGen (하데스형 절차 진행)")]
    // 절차적 생성이 유일/기본 진행 방식. 레거시 contiguous 존 경로는 더 이상 사용하지 않음(정리 예정).
    [Tooltip("절차 진행 컨트롤러. 비우면 런타임에 AddComponent로 생성(RunFlowController 기본 풀 키 사용).")]
    [SerializeField] private RunFlowController runFlowController;

    [Tooltip("스타트 방 진입 시 재생할 대화 시퀀스 SO. 서버 CSV에 'StartRoom' 시퀀스가 없을 때 폴백으로 사용.")]
    [SerializeField] private DialogueSequenceSO startRoomDialogueSO;

    /// <summary>서버에서 로드하는 대화 시퀀스 ID. 비워두면 서버 데이터를 사용하지 않음.</summary>
    private const string StartRoomSequenceId = "StartRoom";

    // OpenWallsForConnections / CorridorBridgeSpawner / StartRoomGate 세 곳에서 동일하게 사용하는 게이트 너비(타일 수)
    private const int GateWidth = 5;

    /// <summary>스타트 방 씬으로 진입한 상태. Loadout 준비 여부와 무관. 디버그 스킵 등에 사용.</summary>
    public bool IsStartRoomScene => AppBootstrapper.Instance != null
        && (!string.IsNullOrEmpty(startRoomMapKey) || startWithZoneLayout);

    /// <summary>스타트 방 모드 여부. 로비를 거쳐 진입한 경우 true.
    /// AppBootstrapper.Instance가 null이면 에디터 직접 실행으로 간주해 false를 반환한다.</summary>
    public bool IsInStartRoom => IsStartRoomScene
        && !(AppBootstrapper.Instance.Loadout?.IsReady ?? false);

    /// <summary>zone_index=0 을 스타트 방으로 사용하는 모드. StartRoomGate가 이 값으로 분기 판별.</summary>
    public bool IsZoneLayoutMode => startWithZoneLayout;

    [Tooltip("스타트 방 캐릭터 픽업 프리팹 배열. CP 타일 발견 순서대로 매핑됨. 각 프리팹에 StartRoomPickup(Character) + CharacterData 설정 필요.")]
    [SerializeField] private GameObject[] characterPickupPrefabs;

    [Tooltip("스타트 방 무기 픽업 프리팹 배열. WP 타일 발견 순서대로 매핑됨. 각 프리팹에 StartRoomPickup(Weapon) + WeaponSO 설정 필요.")]
    [SerializeField] private GameObject[] weaponPickupPrefabs;

    [Tooltip("스타트 방 탈출 게이트 프리팹. next_zone_indices 기준 존 출구 엣지에 배치됨. StartRoomGate 컴포넌트 필요.")]
    [SerializeField] private GameObject startGatePrefab;

    [Header("Block Map Gen")]
    [SerializeField, Tooltip("단일 팔레트 (fallback). blockPalettes에 테마 매칭이 없으면 이 값 사용.")]
    private BlockPalette blockPalette;

    [SerializeField, Tooltip("테마별 블록 팔레트 배열. MapRoomEntry.theme와 BlockPalette.themeMatch가 일치하는 첫 항목이 사용됨.")]
    private BlockPalette[] blockPalettes;

    [SerializeField] private float blockCellSize = 1f;

    [SerializeField, Tooltip("블록 배치 Y 오프셋. 바닥 블록 scale.y=0.2(반높이 0.1)이면 -0.1. 프리팹 피봇이 바닥이면 0.")]
    private float blockBaseY = -0.1f;

    [SerializeField, Tooltip("벽 높이 배율. 1=기본(1블록), 12=12배 높이(약 12m). 천장도 이 값에 맞춰 자동 배치.")]
    private int wallLayers = 12;

    [SerializeField, Min(0), Tooltip("절차 방의 각 문(입구/출구) 바깥으로 뻗는 복도 스텁 길이(셀 수). " +
             "문 너머가 허공(절벽)으로 보이지 않게 '뒤로 이어지는 통로' 느낌을 준다. 0이면 비활성.")]
    private int procDoorCorridorLength = 6;


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

    [Tooltip("상점 NPC 프리팹 Addressable 키. 플레이어가 F로 상호작용하면 상점 UI(UI_ShopPanel)를 연다. " +
             "기존 월드 매대는 ShopRoomController가 비활성화한다.")]
    [SerializeField] private string shopNpcAddressableKey = "Shop/ShopNpc";

    [Tooltip("재련소 NPC 프리팹 Addressable 키. 미등록 시 상점 NPC(shopNpcAddressableKey)로 폴백.")]
    [SerializeField] private string crucibleNpcAddressableKey = "Crucible/CrucibleNpc";

    [Tooltip("매대 타일이 없는 상점 방의 무기 슬롯 수 폴백. 매대가 있으면 매대 카테고리를 그대로 사용.")]
    [SerializeField, Min(0)] private int shopWeaponSlotFallback = 1;

    [Tooltip("상점 리롤 기능 on/off 피처 플래그. 기본 off. (리롤은 의도적 비결정 RNG 사용)")]
    [SerializeField] private bool shopRerollEnabled = false;

    [Tooltip("리롤 1회 비용(골드).")]
    [SerializeField, Min(0)] private int shopRerollCost = 50;

    [Header("Room Clear Effects")]
    [SerializeField, Tooltip("방 클리어 시 맵 중앙에 재생할 이펙트 프리팹.")]
    private GameObject clearEndEffectPrefab;

    [SerializeField, Tooltip("EndEffect 후 보상 상호작용 오브젝트로 사용할 이펙트 프리팹.")]
    private GameObject clearEndEffect2Prefab;

    [Header("World Map")]
    [Tooltip("챕터 존 레이아웃 SO 레지스트리. zone_layout_key 서버 데이터가 없을 때 SO 폴백에 사용.")]
    [SerializeField] private ChapterRegistry chapterRegistry;
    [Tooltip("모든 존 구조물이 배치될 씬 루트 Transform. null이면 mapRoot 폴백.")]
    [SerializeField] private Transform worldMapRoot;
    [Header("Corridor")]
    [Tooltip("테마별 코리더 스타일 SO 배열. CorridorStyleSO.themeMatch가 zone.corridor_style과 일치하는 첫 항목을 사용.\n" +
             "미할당 시 CorridorBridgeSpawner 호출이 생략되어 방 사이 갭이 빈 상태로 남는다.")]
    [SerializeField] private CorridorStyleSO[] corridorStyles;

    [Header("Decoration")]
    [Tooltip("방 테마별 장식 카탈로그. 방 진입 시 MapRoomEntry.theme와 themeMatch가 일치하는 첫 항목 사용. " +
             "일치 없으면 themeMatch=\"*\" 범용 카탈로그로 폴백.")]
    [SerializeField] private DecorationCatalogSO[] decorationCatalogs;

    [Header("Covenant")]
    [Tooltip("서약 수치 데이터 테이블 SO. 비어있으면 각 서약 구현체의 하드코딩 fallback 값 사용.")]
    [SerializeField] private CovenantDataTableSO covenantDataTable;

    [Header("Awakening")]
    [Tooltip("런 중 심연의 정수를 추적하는 컴포넌트. 없으면 자동 생성.")]
    [SerializeField] private EssenceTracker essenceTracker;

    private GameObject _currentMapGO;
    // grid_csv의 P 토큰에서 계산한 플레이어 스폰 월드 좌표.
    // SpawnBlockMapAsync에서 채워지고 SpawnPlayerAsync에서 소비.
    private Vector3? _pendingPlayerSpawnPos;
    private bool _isSpawning;

    private GameRunSession _run;
    public GameRunSession Run => _run;

    // 미니맵 스포너 이벤트 구독 추적 (새 방 진입 시 해제)
    private readonly System.Collections.Generic.List<MonsterSpawner> _minimapSpawnerSubs = new();

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

        _run.SetCovenantDataTable(covenantDataTable);

        if (disableSceneBakedNavMeshOnStart)
            DisableSceneBakedNavMesh();

        _run.OnMapSpawnRequested += OnMapSpawnRequestedHandler;
        _run.OnBossRoomCleared += OnBossRoomClearedHandler;
    }

    /// <summary>보스방 클리어 신호 → 비최종 챕터면 챕터 전환 게이트를 스폰(등장 연출).
    /// 최종 챕터는 게이트 없이 ClearRewardTrigger가 런 클리어를 담당한다. 보상은 별개(ClearRewardTrigger).</summary>
    private void OnBossRoomClearedHandler(Vector3 center)
    {
        if (_run != null && _run.HasNextChapter())
            ChapterGate.Spawn(center);
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
        await InitCovenantDataAsync();
        await InitRelicAwakeningAsync();
        await InitChapterDataAsync();
        await InitRunStructureDataAsync();

        // 트래커 바인딩 (데이터 초기화 후)
        BindEssenceTracker();

        // startWithZoneLayout이면 Zone 0은 StartRoomAsync에서, 나머지는 게이트 통과 후 스폰
        // 그 외(continuing run / 에디터 직접 실행 fallback)는 전체 존을 지금 스폰
        if (!IsStartRoomScene)
            await SpawnWorldMapAsync(this.GetCancellationTokenOnDestroy());

        // UIRoot 로드 대기 (MerlinRuneBridge가 @HUD에 있음)
        if (UIRootBootstrapper.Instance == null)
            await UniTask.WaitUntil(() => UIRootBootstrapper.Instance != null || !this);

        // 블록 시너지 그리드 구성 (UIRoot @HUD에 있는 Bridge 사용)
        var bridge = MerlinRuneBridge.Instance;
        if (bridge == null)
            bridge = Object.FindFirstObjectByType<MerlinRuneBridge>(FindObjectsInactive.Include);
        if (bridge != null)
            bridge.InitializeGridsFromServer();

        // IsRunning이 true면 StageMap을 거쳐 전투 씬으로 진입한 것 → 전투 시작
        // zone-layout 이어하기: 마지막 클리어된 존에서 재개하며 출구 게이트 활성화 상태로 복원
        // IsInStartRoom이면 로비를 거쳐 스타트 방으로 진입 → 스타트 방 모드 (에디터 직접 실행 시 false)
        // DebugStageRunPanel이 있으면 해당 패널이 StartRunAsync를 통해 전투를 시작하므로 중복 실행 방지
        bool hasDebugPanel = Object.FindFirstObjectByType<DebugStageRunPanel>() != null;

        // 베이스캠프(영속 허브)에서 로드아웃 확정 후 진입한 새 런: 바로 전투가 아니라 Zone0를 대기 방으로 띄운다.
        // IsNewRunPending(로비 새 런 신호, BaseCamp 경유 시 미소비 상태로 유지)을 여기서 소비한다.
        // 디버그 패널이 있어도 허브발 실제 새 런이 우선한다(디버그 패널은 IsStartRoomScene이면 자동 시작을 보류).
        bool newRunFromHub = (AppBootstrapper.Instance?.Loadout?.IsReady ?? false)
            && (AppBootstrapper.Instance?.ConsumeNewRunPending() ?? false);

        // 챕터 전환 진입: 기존 런(IsRunning) 유지하되 저장 이어하기가 아니라 새 챕터를 처음부터 시작.
        bool chapterAdvance = AppBootstrapper.Instance?.ConsumeChapterAdvance() ?? false;

        if (_run != null && _run.IsRunning && chapterAdvance && !hasDebugPanel)
            await StartNextChapterInSceneAsync(this.GetCancellationTokenOnDestroy());
        else if (_run != null && _run.IsRunning && !hasDebugPanel)
            await ContinueProcGenRunAsync(this.GetCancellationTokenOnDestroy());
        else if (newRunFromHub)
            await StartWaitingRoomAsync();
        else if (IsInStartRoom)
            await StartRoomAsync();
        else if (!hasDebugPanel)
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
            _run.OnBossRoomCleared -= OnBossRoomClearedHandler;

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

        var blockData = Managers.RuneData;
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

    private async UniTask InitCovenantDataAsync()
    {
        var covenantData = Managers.CovenantData;
        if (covenantData == null || covenantData.IsInitialized) return;

        try { await covenantData.InitializeAsync(); }
        catch (System.Exception e) { Debug.LogWarning($"[GameRunBootstrapper] CovenantData 예외: {e.Message}"); }
    }

    private async UniTask InitRelicAwakeningAsync()
    {
        var awakening = Managers.RelicAwakening;
        if (awakening == null || awakening.IsInitialized) return;

        try { await awakening.InitializeAsync(); }
        catch (System.Exception e) { Debug.LogWarning($"[GameRunBootstrapper] RelicAwakening 예외: {e.Message}"); }
    }

    private async UniTask InitChapterDataAsync()
    {
        var chapterData = Managers.ChapterData;
        if (chapterData == null || chapterData.IsInitialized) return;

        try { await chapterData.InitializeAsync(); }
        catch (System.Exception e) { Debug.LogWarning($"[GameRunBootstrapper] ChapterData 예외: {e.Message}"); }
    }

    private async UniTask InitRunStructureDataAsync()
    {
        var runStructureData = Managers.RunStructureData;
        if (runStructureData == null || runStructureData.IsInitialized) return;

        try { await runStructureData.InitializeAsync(); }
        catch (System.Exception e) { Debug.LogWarning($"[GameRunBootstrapper] RunStructureData 예외: {e.Message}"); }
    }

    private void BindEssenceTracker()
    {
        if (essenceTracker == null)
            essenceTracker = GetComponentInChildren<EssenceTracker>(true)
                          ?? gameObject.AddComponent<EssenceTracker>();

        essenceTracker.Bind(_run);
    }

    // ── 월드 맵 스폰 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 현재 챕터의 zone_layout_key로 전체 존 레이아웃을 로드하고 모든 존 구조물을 월드에 배치한다.
    /// zone_layout_key가 없거나 데이터 로드 실패 시 조용히 스킵한다.
    /// </summary>
    private async UniTask SpawnWorldMapAsync(System.Threading.CancellationToken ct)
    {
        // 존 레이아웃 키 결정: 서버 데이터 → SO 폴백
        var chapter     = _run?.CurrentChapter ?? ChapterId.Chapter1;
        var serverEntry = Managers.ChapterData?.Get(chapter);
        var chapterSO   = chapterRegistry?.GetData(chapter);
        var zoneLayoutKey = serverEntry?.zone_layout_key ?? chapterSO?.zoneLayoutKey;
        var zoneSlotKey   = serverEntry?.zone_slot_key   ?? chapterSO?.zoneSlotKey;
        var zonePoolKey   = serverEntry?.zone_pool_key   ?? chapterSO?.zonePoolKey;

        if (string.IsNullOrEmpty(zoneLayoutKey))
        {
            Debug.Log("[GameRunBootstrapper] zone_layout_key 없음 — 월드 맵 스폰 스킵");
            return;
        }

        // 존 레이아웃 데이터 로드 (절차적: slot+pool / 고정: 단일 CSV)
        var layoutMgr = Managers.ZoneLayout;
        try
        {
            if (!string.IsNullOrEmpty(zoneSlotKey) && !string.IsNullOrEmpty(zonePoolKey))
                await Managers.ZoneLayout.LoadWithPoolAsync(zoneSlotKey, zonePoolKey, zoneLayoutKey);
            else
                await Managers.ZoneLayout.LoadAsync(zoneLayoutKey);
        }
        catch (System.Exception e) { Debug.LogWarning($"[GameRunBootstrapper] ZoneLayout 로드 예외: {e.Message}"); }

        var zones = layoutMgr.GetZones(zoneLayoutKey);
        if (zones == null || zones.Count == 0)
        {
            Debug.LogWarning($"[GameRunBootstrapper] '{zoneLayoutKey}' 존 데이터 없음 — 스폰 스킵");
            return;
        }

        // 블록 팔레트: 첫 존의 palette 키로 Addressables 로드 → 실패 시 theme 매칭 폴백
        BlockPalette zonePalette = null;
        var firstWithPalette = zones.Find(z => !string.IsNullOrEmpty(z.palette));
        if (firstWithPalette != null)
        {
            zonePalette = await Managers.AddressableManager.TryLoadAssetAsync<BlockPalette>(firstWithPalette.palette);
        }
        if (zonePalette == null)
        {
            var chapterTheme = _run?.ActiveTheme;
            if (string.IsNullOrEmpty(chapterTheme))
                chapterTheme = zones.Find(z => !string.IsNullOrEmpty(z.theme))?.theme ?? string.Empty;
            zonePalette = PickBlockPalette(chapterTheme);
        }

        // 모든 존 구조물 배치
        var root = worldMapRoot != null ? worldMapRoot : mapRoot;
        int spawned = 0;

        for (int i = 0; i < zones.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var zone = zones[i];
            if (string.IsNullOrWhiteSpace(zone.grid_csv)) continue;

            var grid = MapDataLoader.Parse(zone.grid_csv);
            if (grid == null) continue;

            OpenWallsForConnections(grid, zone, zones);

            var worldCenter = CalcZoneWorldCenter(zone);
            var zoneGO = new GameObject($"Zone_{zone.zone_index:D2}_{zone.label}");
            zoneGO.transform.SetParent(root, false);
            zoneGO.transform.position = worldCenter;

            MapBuilder.Build(grid, zonePalette, zoneGO.transform, blockCellSize, blockBaseY, null, wallLayers);
            MapBuilder.BuildCeiling(grid, zonePalette, zoneGO.transform, blockCellSize, blockBaseY, wallLayers * blockCellSize);
            if (zonePalette != null) MapBuilder.BuildRoomLights(grid, zoneGO.transform, blockCellSize, blockBaseY, wallLayers, zonePalette.Lighting);
            spawned++;

            // 4존마다 프레임 분산 (26존 연속 생성 시 히칭 방지)
            if (i % 4 == 3)
                await UniTask.Yield(ct);
        }

        SpawnAllCorridors(zones, root);
        Debug.Log($"[GameRunBootstrapper] 월드 맵 스폰 완료: {spawned}/{zones.Count}개 존 ({zoneLayoutKey})");
    }

    /// <summary>
    /// zone_index=0(Start)를 MapRoomEntry로 변환해 SpawnBlockMapAsync로 빌드.
    /// CP/WP 타일에서 픽업 프리팹이 자동 스폰되고, _pendingPlayerSpawnPos가 설정된다.
    /// </summary>
    private async UniTask SpawnStartZoneFromLayoutAsync(CancellationToken ct, bool suppressInteractables = false)
    {
        // 세션 챕터가 미설정(0)일 수 있으므로 ResolveCurrentChapter로 씬 이름 폴백까지 처리 (StartProcGenRunAsync와 동일).
        var chapter     = ResolveCurrentChapter();
        var serverEntry = Managers.ChapterData?.Get(chapter);
        var chapterSO   = chapterRegistry?.GetData(chapter);
        var zoneLayoutKey = serverEntry?.zone_layout_key ?? chapterSO?.zoneLayoutKey;
        var zoneSlotKey   = serverEntry?.zone_slot_key   ?? chapterSO?.zoneSlotKey;
        var zonePoolKey   = serverEntry?.zone_pool_key   ?? chapterSO?.zonePoolKey;
        // 서버/레지스트리에 키가 없으면 규칙 기반 폴백 (StartProcGenRunAsync의 CHAPTER_N_ROOM_POOL과 동일 패턴).
        if (string.IsNullOrEmpty(zoneLayoutKey))
            zoneLayoutKey = $"chapter_{(int)chapter}_zone_layout";
        if (string.IsNullOrEmpty(zoneLayoutKey))
        {
            Debug.LogError("[GameRunBootstrapper] zone_layout_key 없음 — Zone 0 스폰 불가");
            return;
        }

        var layoutMgr = Managers.ZoneLayout;
        try
        {
            if (!string.IsNullOrEmpty(zoneSlotKey) && !string.IsNullOrEmpty(zonePoolKey))
                await Managers.ZoneLayout.LoadWithPoolAsync(zoneSlotKey, zonePoolKey, zoneLayoutKey);
            else
                await Managers.ZoneLayout.LoadAsync(zoneLayoutKey);
        }
        catch (System.Exception e) { Debug.LogWarning($"[GameRunBootstrapper] ZoneLayout 로드 예외: {e.Message}"); }

        var zones = layoutMgr.GetZones(zoneLayoutKey);
        if (zones == null || zones.Count == 0)
        {
            Debug.LogError($"[GameRunBootstrapper] '{zoneLayoutKey}' 존 데이터 0개 — Zone 0 스폰 불가. 서버/Addressables 확인 필요");
            return;
        }
        Debug.Log($"[GameRunBootstrapper] 로드된 존 목록: {string.Join(", ", zones.ConvertAll(z => $"{z.zone_index}({z.label})"))}");

        var startZone = zones.Find(z => z.zone_index == 0);
        if (startZone == null)
        {
            Debug.LogError($"[GameRunBootstrapper] zone_index=0 없음 (총 {zones.Count}개 존 로드됨) — Zone 0 스폰 불가");
            return;
        }
        if (string.IsNullOrWhiteSpace(startZone.grid_csv))
        {
            Debug.LogError($"[GameRunBootstrapper] Zone 0 ({startZone.label})의 grid_csv가 비어있음 — 서버 데이터 확인 필요");
            return;
        }

        var roomEntry = new MapRoomEntry
        {
            room_id           = $"zone_0_{startZone.label}",
            category          = startZone.category,
            theme             = startZone.theme,
            palette           = startZone.palette,
            grid_csv          = startZone.grid_csv,
            max_active_spawners = startZone.max_active_spawners,
            scatter_range     = startZone.scatter_range,
        };
        var worldCenter = CalcZoneWorldCenter(startZone);
        await SpawnBlockMapAsync(roomEntry, worldCenter, ct, instantEntrance: true, suppressInteractables: suppressInteractables); // 시작방 허브: 디졸브 없이 완성된 방으로

        // P 타일이 없으면 CSV의 spawn_local로 폴백
        if (!_pendingPlayerSpawnPos.HasValue)
            _pendingPlayerSpawnPos = worldCenter + new Vector3(startZone.spawn_local_x, 0f, startZone.spawn_local_z);

        // 존 진행 서비스 초기화 — 클리어 후 다음 존 선택·지연 스폰을 조율
        _run?.InitZoneProgression(zoneLayoutKey, worldCenter, blockCellSize);

        // Zone 0 출구에 StartRoomGate 배치 — 로드아웃 준비 완료 시 픽업이 활성화
        if (_currentMapGO != null)
            CreateStartRoomGates(startZone, zones, _currentMapGO);

        Debug.Log($"[GameRunBootstrapper] Zone 0 ({startZone.label}) 스폰 완료. 플레이어 예정 위치: {_pendingPlayerSpawnPos}");
    }

    /// <summary>
    /// 지정된 zone_index의 존을 월드에 스폰한다. ZoneProgressionService가 선택된 존을 지연 스폰할 때 호출.
    /// 블록 배치 → 스포너 설정 → NavMesh 빌드 → RoomWaveController 부착 → ZoneEntryTrigger 부착.
    /// 플레이어가 존에 진입하면 ZoneEntryTrigger가 스포너·웨이브를 활성화한다.
    /// </summary>
    /// <param name="worldCenterOverride">CSV 위치 대신 사용할 월드 중심 좌표. null이면 CalcZoneWorldCenter 사용.</param>
    public async UniTask SpawnZoneByIndexAsync(int zoneIndex, Vector3? worldCenterOverride = null, CancellationToken ct = default)
    {
        ct = ct == default ? this.GetCancellationTokenOnDestroy() : ct;

        var chapter = _run?.CurrentChapter ?? ChapterId.Chapter1;
        var serverEntry = Managers.ChapterData?.Get(chapter);
        var zoneLayoutKey = serverEntry?.zone_layout_key;
        if (string.IsNullOrEmpty(zoneLayoutKey))
            zoneLayoutKey = chapterRegistry?.GetData(chapter)?.zoneLayoutKey;
        if (string.IsNullOrEmpty(zoneLayoutKey)) return;

        var zones = Managers.ZoneLayout.GetZones(zoneLayoutKey);
        var zone = zones?.Find(z => z.zone_index == zoneIndex);
        if (zone == null || string.IsNullOrWhiteSpace(zone.grid_csv))
        {
            Debug.LogWarning($"[GameRunBootstrapper] SpawnZoneByIndexAsync: zone_index {zoneIndex} 데이터 없음");
            return;
        }

        // 팔레트 결정
        BlockPalette zonePalette = null;
        if (!string.IsNullOrEmpty(zone.palette))
            zonePalette = await Managers.AddressableManager.TryLoadAssetAsync<BlockPalette>(zone.palette);
        if (zonePalette == null)
            zonePalette = PickBlockPalette(!string.IsNullOrEmpty(zone.theme) ? zone.theme : string.Empty);

        // 그리드 파싱 (스폰 정보 포함)
        var spawnInfos = new System.Collections.Generic.Dictionary<Vector2Int, MapDataLoader.CellSpawnInfo>();
        var grid = MapDataLoader.Parse(zone.grid_csv, spawnInfos);
        if (grid == null)
        {
            Debug.LogWarning($"[GameRunBootstrapper] SpawnZoneByIndexAsync: Zone {zoneIndex} grid_csv 파싱 실패");
            return;
        }

        ct.ThrowIfCancellationRequested();

        // 연결된 모든 방향(북/남/동/서)의 벽을 실제 인접 존 좌표 기반으로 개방
        // 비활성 존도 CSV 좌표로 위치를 미리 알 수 있으므로 스폰 전에 처리
        OpenWallsForConnections(grid, zone, zones);

        ApplyMonsterSpawnerPlan(grid, zone.max_active_spawners);

        var root = worldMapRoot != null ? worldMapRoot : mapRoot;
        var worldCenter = worldCenterOverride ?? CalcZoneWorldCenter(zone);

        var zoneGO = new GameObject($"Zone_{zone.zone_index:D2}_{zone.label}");
        zoneGO.transform.SetParent(root, false);
        zoneGO.transform.position = worldCenter;

        // 블록 빌드 (스포너 GO 없음 — MonsterSpawnHandler PreBuild가 담당)
        var blocks = MapBuilder.Build(grid, zonePalette, zoneGO.transform, blockCellSize, blockBaseY, blockShopStallPrefab, wallLayers);
        MapBuilder.BuildCeiling(grid, zonePalette, zoneGO.transform, blockCellSize, blockBaseY, wallLayers * blockCellSize);
        if (zonePalette != null) MapBuilder.BuildRoomLights(grid, zoneGO.transform, blockCellSize, blockBaseY, wallLayers, zonePalette.Lighting);

        // 토큰 실행에 사용할 공유 컨텍스트
        var deferredSpawners = new System.Collections.Generic.List<UnityEngine.MonoBehaviour>();
        var tokenCtx = new TokenContext
        {
            Parent             = zoneGO.transform,
            CellSize           = blockCellSize,
            BaseY              = blockBaseY,
            Theme              = !string.IsNullOrEmpty(zone.theme) ? zone.theme : string.Empty,
            DecorationCatalogs = decorationCatalogs,
            ActivePalette      = zonePalette,
            Grid               = grid,
            SpawnInfos         = spawnInfos,
            DeferredSpawners   = deferredSpawners,
            Ct                 = ct,
        };

        // PreBuild: 몬스터 스포너 배치 + 비활성화 (ZoneEntryTrigger 진입 시 re-enable)
        TokenParser.Execute(zone.grid_csv, grid.GetLength(0), grid.GetLength(1), tokenCtx, TokenPhase.PreBuild);

        // 플레이어 진입 전까지 렌더러 비활성화 (진입 시 디졸브로 등장)
        HideAllBlockRenderers(blocks);

        // NavMesh 빌드 (몬스터 AI 이동 경로 계산)
        await BuildMapNavMeshAsync(zoneGO);

        // PostBuild: 장식(d*) / 보스 스폰(B) — NavMesh 빌드 이후에 배치
        TokenParser.Execute(zone.grid_csv, grid.GetLength(0), grid.GetLength(1), tokenCtx, TokenPhase.PostBuild);

        // 미니맵 초기화 — PostBuild 후 스포너가 모두 배치된 시점
        InitializeMinimapForRoom(zoneGO, grid.GetLength(0), grid.GetLength(1));

        // 방 클리어 컨트롤러 부착 (Activate는 ZoneEntryTrigger가 호출)
        AttachRoomClearController(zoneGO);

        // 이미 스폰된 인접 존과의 코리더 타일 생성 (CorridorStyleSO 할당 시 동작)
        _run?.ZoneProgression?.RegisterSpawnedZone(zoneIndex, worldCenter);
        SpawnCorridorsForZone(zone, worldCenter, zones, root);

        // 게이트 통과 즉시 방 등장 연출 — 플레이어가 걸어오는 동안 방이 생성되는 것처럼 보임
        await new DissolveEntrance().PlayAsync(blocks, default, ct);

        // 디졸브/NavMesh await 도중 전이가 취소되거나(게이트 파괴 등) 존이 파괴되면 중단
        // — DissolveEntrance가 취소를 삼키므로 여기서 직접 검사한다
        if (ct.IsCancellationRequested || zoneGO == null) return;

        // 진입 트리거 + 출구 게이트 생성 (플레이어 진입 시 스포너/웨이브 활성화)
        float zoneSizeX = zone.grid_width  * blockCellSize;
        float zoneSizeZ = zone.grid_height * blockCellSize;
        zoneGO.TryGetComponent<RoomWaveController>(out var waveCtrl);
        var entryTrigger = zoneGO.AddComponent<ZoneEntryTrigger>();
        entryTrigger.Initialize(waveCtrl, deferredSpawners, zoneSizeX, zoneSizeZ);

        CreateZoneExitGates(zoneIndex, zone, zones, zoneGO);

        Debug.Log($"[GameRunBootstrapper] Zone {zoneIndex} ({zone.label}) 스폰 완료 @ {worldCenter}");
    }

    /// <summary>절차적 진행 사용 여부. 항상 true로 고정 — 절차 방식이 유일한 진행 경로.</summary>
    public bool UseProcGen => true;

    /// <summary>
    /// 허브(스타트 방) 이탈 시 절차 진행 시작. RunFlowController를 확보(없으면 AddComponent)하고
    /// 허브와 겹치지 않는 먼 앵커에 첫 방을 빌드한다. 레거시 ZoneProgression 경로를 대체.
    /// </summary>
    public async UniTask StartProcGenRunAsync()
    {
        var flow = runFlowController != null ? runFlowController : gameObject.AddComponent<RunFlowController>();

        // 현재 챕터의 룸 풀 키 결정: 서버 → SO → 규칙(CHAPTER_N_ROOM_POOL) 폴백
        // StartRoom 이탈 흐름은 StartNewRunAsync를 거치지 않아 세션 챕터가 미설정(0)일 수 있으므로 씬에서 유추.
        var chapter     = ResolveCurrentChapter();
        // 세션 CurrentChapter를 확정 — 챕터 종료 판정(HasNextChapter/AdvanceToNextChapter)이
        // raw CurrentChapter(미설정=0)를 쓰는 오프바이원으로 최종 보스에서 Chapter1을 재시작하던 버그 차단.
        _run?.EnsureChapter(chapter);
        var serverEntry = Managers.ChapterData?.Get(chapter);
        var chapterSO   = chapterRegistry?.GetData(chapter);
        var poolKey     = serverEntry?.zone_pool_key;
        if (string.IsNullOrEmpty(poolKey)) poolKey = chapterSO?.zonePoolKey;
        if (string.IsNullOrEmpty(poolKey)) poolKey = $"CHAPTER_{(int)chapter}_ROOM_POOL";

        var structureKey = ResolveStructureKey(chapter, serverEntry, chapterSO);

        // 허브(Zone 0, ~원점)와 겹치지 않게 먼 앵커에서 격리 빌드
        await flow.StartRunAsync(new Vector3(0f, 0f, 2000f), poolKey, structureKey);
    }

    /// <summary>챕터별 레벨 스파인(RunStructureConfig) Addressable 키 해석: 서버 → SO → 규칙(CHAPTER_N_RUN_STRUCTURE) 폴백.
    /// 룸 풀 키와 동일한 3단 우선순위. 해당 챕터 전용 에셋이 없으면 RunFlowController가 RUN_STRUCTURE_DEFAULT로 폴백한다(회귀 0).</summary>
    private static string ResolveStructureKey(ChapterId chapter, ChapterServerEntry serverEntry, ChapterDataSO chapterSO)
    {
        var key = serverEntry?.run_structure_key;
        if (string.IsNullOrEmpty(key)) key = chapterSO?.runStructureKey;
        if (string.IsNullOrEmpty(key)) key = $"CHAPTER_{(int)chapter}_RUN_STRUCTURE";
        return key;
    }

    /// <summary>
    /// 대기방(BlockMap_zone_0_, =_currentMapGO) GO를 호출자에게 넘기고 참조를 비운다.
    /// 절차런은 _currentMapGO를 쓰지 않으므로(ProcRoom_*로 별도 추적) 대기방은 원점에 방치된다.
    /// RunFlowController가 첫 절차 방 진입 후 이를 파괴해 잔존 zone_0이 보스룸 등과 겹치지 않게 한다.
    /// </summary>
    public GameObject ConsumeWaitingRoomMap()
    {
        var go = _currentMapGO;
        _currentMapGO = null;
        return go;
    }

    // ── 런 종료 (사망/클리어) ──────────────────────────────────────
    private bool _runEnding;

    /// <summary>플레이어 사망 시 PlayerController가 호출. 사망 연출 후 메타 저장·세이브 폐기·베이스캠프 복귀.</summary>
    public void HandlePlayerDeath() => HandleRunEndAsync(false).Forget();

    /// <summary>최종 챕터 보스 클리어 시 ClearRewardTrigger가 호출. 클리어 연출 후 메타 저장·세이브 폐기·베이스캠프 복귀.</summary>
    public void HandleRunClear() => HandleRunEndAsync(true).Forget();

    private bool _advancingChapter;

    /// <summary>
    /// 보스 클리어(비최종 챕터) 시 ClearRewardTrigger가 호출 — 챕터 번호·테마 갱신 후
    /// 다음 챕터 전용 씬(GameScene_ChN)을 로드해 해당 챕터 고유 환경에서 새 챕터를 처음부터 시작한다.
    /// 마지막 챕터면 런 클리어로 분기. 런 상태는 DDOL 세션 + 새 씬 BindPlayer 복원으로 이월된다.
    /// </summary>
    public void AdvanceChapter()
    {
        if (_advancingChapter) return;
        _advancingChapter = true;
        try
        {
            var run = Run;
            if (run == null) return;

            run.EnterChapterClear();
            if (!run.AdvanceToNextChapter())
            {
                HandleRunClear(); // 마지막 챕터였음 — 런 클리어
                return;
            }

            var chapter = run.CurrentChapter;

            // 다음 챕터 전용 씬을 로드 — 로드된 GameScene의 GameRunBootstrapper가 IsChapterAdvancePending을
            // 감지해 저장 이어하기가 아니라 새 챕터를 처음부터 시작한다(StartNextChapterInSceneAsync).
            // 화면 전환 가림은 RequestLoad의 로딩 오버레이가 담당한다.
            AppBootstrapper.Instance?.MarkChapterAdvance();
            AppBootstrapper.Instance?.RequestLoad(AppBootstrapper.GetSceneForChapter(chapter));
            Debug.Log($"[GameRunBootstrapper] 챕터 전환 → Chapter {(int)chapter} 씬 로드 요청");
        }
        finally
        {
            _advancingChapter = false;
        }
    }

    /// <summary>
    /// 런 종료 공용 시퀀스. isCleared=false(사망)/true(클리어) 분기.
    /// 사망 모먼트(슬로우모션·쉐이크) → 화면 처리(비네트·암전) → 메시지 → 메타 저장 → 세이브 폐기 → BaseCamp 복귀.
    /// 사망=HandlePlayerDeath, 클리어=HandleRunClear가 진입점.
    /// </summary>
    private async UniTaskVoid HandleRunEndAsync(bool isCleared)
    {
        if (_runEnding) return;
        _runEnding = true;

        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            // 1) 사망 모먼트: 카메라 쉐이크 + 슬로우모션 (realtime 대기, finally로 timeScale 복원 보장)
            if (!isCleared)
            {
                HitFeelService.CameraShake(0.15f, 0.4f);
                TimeScaleArbiter.Acquire(this, 0.25f, TimeScaleArbiter.Priority.SlowMotion);
                try { await UniTask.Delay(System.TimeSpan.FromSeconds(0.6f), DelayType.Realtime, cancellationToken: ct); }
                finally { TimeScaleArbiter.Release(this); }
            }

            // 2) 화면 처리: 사망만 빨간 비네트(피격감) — 클리어(승리)는 비네트 없이 암전만 적용해 사망 연출과 구분.
            if (!isCleared)
            {
                var fx = Managers.UI?.GetOverlayUI<RelicFairy.UI.Overlay.FXLayer>();
                fx?.Vignette(new Color(0.5f, 0f, 0f), 1f, 1.5f);
            }
            await ScreenFade.Out(1.0f, ct);

            // 3) 종료 메시지(영혼 회수) — 스킵 입력 지원
            await ShowRunEndMessageAsync(isCleared, ct);

            // 4) 메타 저장(OnRunEnded) 먼저 → 정리(ClearLocalRun+Loadout.Clear) → 허브 복귀
            _run?.EndRun(isCleared, isCleared ? "clear" : "death");
            RunReturnTracker.RecordRunEnd(isCleared);   // BaseCamp 복귀 대사(사망/클리어 카운트) 기록
            AppBootstrapper.Instance?.EndRun();
            AppBootstrapper.Instance?.RequestLoad(Define.Scene.BaseCamp);
        }
        catch (System.OperationCanceledException) { TimeScaleArbiter.Release(this); }
    }

    /// <summary>종료 메시지 경량 오버레이(코드 생성). 입력 시 즉시 스킵, 아니면 홀드 후 자동 진행.</summary>
    private async UniTask ShowRunEndMessageAsync(bool isCleared, CancellationToken ct)
    {
        string msg = isCleared ? "그대의 여정이 끝났다" : "그대의 영혼이 회수되었다...";

        var go = new GameObject("@RunEndMessage");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;

        var tmp = new GameObject("Text").AddComponent<TextMeshProUGUI>();
        tmp.transform.SetParent(go.transform, false);
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text      = msg;
        tmp.fontSize  = 48f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = new Color(0.85f, 0.8f, 0.7f);
        var rt = tmp.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        const float hold = 2.0f;
        float t = 0f;
        try
        {
            while (t < hold)
            {
                if (Input.anyKeyDown) break;   // 스킵 입력
                t += Time.unscaledDeltaTime;
                await UniTask.Yield(ct);
            }
        }
        finally
        {
            if (go != null) Destroy(go);       // 취소(객체 파괴) 시에도 오버레이 정리 보장
        }
    }

    /// <summary>세션 챕터가 미설정(기본 0, 유효하지 않음)이면 활성 씬 이름(GameScene_ChN)에서 챕터를 유추한다.</summary>
    private ChapterId ResolveCurrentChapter()
    {
        var ch = _run?.CurrentChapter ?? ChapterId.Chapter1;
        if (System.Enum.IsDefined(typeof(ChapterId), ch)) return ch;

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        for (int n = 4; n >= 1; n--)
            if (scene.EndsWith($"Ch{n}")) return (ChapterId)n;
        return ChapterId.Chapter1;
    }

    /// <summary>
    /// 현재 챕터의 보스 스폰 테이블을 레지스트리에서 해석한다. BossSpawner가 호출.
    /// 절차 진행 흐름은 StartNewRunAsync를 거치지 않아 세션 CurrentChapter가 미설정(0)일 수 있으므로
    /// ResolveCurrentChapter(씬 이름 폴백 포함)로 챕터를 확정한다.
    /// chapterRegistry 미할당(예: LichTest)이면 null → BossSpawner가 직렬화 폴백을 사용한다.
    /// </summary>
    public MonsterSpawnTableSO ResolveBossSpawnTable()
        => chapterRegistry != null ? chapterRegistry.GetData(ResolveCurrentChapter())?.bossSpawnTable : null;

    // ─────────────────────────────────────────────────────────────────────
    // 절차적(하데스형) 격리 룸 빌드 — RunFlowController가 호출.
    // SpawnZoneByIndexAsync의 빌드 코어를 재사용하되 contiguous 부분(world_center/
    // OpenWalls/코리더/존게이트)은 제외하고, 문은 RoomDoorPlanner로 개방한다.
    // 레거시 contiguous 경로(SpawnZoneByIndexAsync 등)는 변경하지 않는다.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 풀 엔트리 하나를 고정 앵커에 격리형 방으로 빌드한다.
    /// 입구/직진/턴 문을 개방하고, 진입 위치와 출구 슬롯(클리어 후 게이트 배치용)을 반환한다.
    /// 플레이어 스폰/이동·게이트 배치·웨이브 활성화는 호출자(RunFlowController)가 담당.
    /// </summary>
    public async UniTask<ProcRoomResult> BuildProcRoomAsync(
        ZonePoolEntry entry, Vector3 anchor, bool mirror, int quarterTurns, CancellationToken ct = default,
        System.Random roomRng = null)
    {
        ct = ct == default ? this.GetCancellationTokenOnDestroy() : ct;
        if (entry == null || string.IsNullOrWhiteSpace(entry.grid_csv))
        {
            Debug.LogWarning("[GameRunBootstrapper] BuildProcRoomAsync: entry/grid_csv 없음");
            return null;
        }

        // 1. grid_csv 변환 (좌우 미러 → 헤딩 회전) — MapBuilder/TokenParser가 동일 문자열을 쓰도록 문자열 레벨에서 적용.
        //    quarterTurns = 진행 방향(heading). 방을 회전시켜 입구가 뒤쪽에 오고 직진이 헤딩을 향하게 한다.
        string csv = mirror ? GridTransform.MirrorX(entry.grid_csv) : entry.grid_csv;
        if (quarterTurns != 0) csv = GridTransform.Rotate(csv, quarterTurns);

        // 2. 팔레트
        BlockPalette palette = null;
        if (!string.IsNullOrEmpty(entry.palette))
            palette = await Managers.AddressableManager.TryLoadAssetAsync<BlockPalette>(entry.palette);
        if (palette == null)
            palette = PickBlockPalette(!string.IsNullOrEmpty(entry.theme) ? entry.theme : string.Empty);

        // 3. 파싱 (스포너 + 문)
        var spawnInfos = new System.Collections.Generic.Dictionary<Vector2Int, MapDataLoader.CellSpawnInfo>();
        var doorInfos  = new System.Collections.Generic.Dictionary<Vector2Int, DoorInfo>();
        var grid = MapDataLoader.Parse(csv, spawnInfos, null, doorInfos);
        if (grid == null)
        {
            Debug.LogWarning($"[GameRunBootstrapper] BuildProcRoomAsync: grid 파싱 실패 ({entry.pool_key})");
            return null;
        }
        ct.ThrowIfCancellationRequested();

        int w = grid.GetLength(0);
        int h = grid.GetLength(1);

        // 4. 문 분류(헤딩 기준: 입구=뒤, 직진=헤딩, 턴=좌우) + 개방
        var cls = RoomDoorPlanner.ClassifyWithHeading(doorInfos, quarterTurns);
        if (cls.entrance.HasValue) RoomDoorPlanner.Open(grid, cls.entrance.Value, doorInfos[cls.entrance.Value]);
        if (cls.forward.HasValue)  RoomDoorPlanner.Open(grid, cls.forward.Value,  doorInfos[cls.forward.Value]);
        foreach (var t in cls.turns) RoomDoorPlanner.Open(grid, t, doorInfos[t]);

        // 5. 스포너 플랜 (후보 m 중 max_active_spawners개만 활성) — roomRng로 결정적(이어하기 재현)
        ApplyMonsterSpawnerPlan(grid, entry.max_active_spawners, roomRng);

        // 6. 방 GO @ 앵커
        var root = worldMapRoot != null ? worldMapRoot : mapRoot;
        var roomGO = new GameObject($"ProcRoom_{entry.pool_key}");
        roomGO.transform.SetParent(root, false);
        roomGO.transform.position = anchor;

        // 커스텀 손맵 아레나(보스 등): arena_template_key가 있으면 격자 지형 대신 프리팹이 방 전체를 제공한다.
        // 지형/보스/트리거/배리어를 모두 프리팹이 담은 길 1 구조 — docs/boss-custom-arena-design.md 참조.
        bool useCustomArena = !string.IsNullOrEmpty(entry.arena_template_key);
        // 커스텀 아레나 키가 Addressable에 없으면(미제작 챕터 등) 격자 보스룸으로 폴백 — 빈 방/보스 미스폰 방지.
        if (useCustomArena && !await Managers.AddressableManager.KeyExistsAsync(entry.arena_template_key))
        {
            Debug.LogWarning($"[GameRunBootstrapper] arena '{entry.arena_template_key}' 미등록 — 기본 격자 보스룸으로 폴백");
            useCustomArena = false;
        }
        Vector3? customArenaEntryPos = null; // 커스텀 아레나: 프리팹 PlayerSpawn 마커 위치(있으면 grid 입구 대신 사용 → 격자 정렬 불필요)
        System.Collections.Generic.List<ProcExitSlot> customArenaExits = null; // 프리팹 Exit 마커에서 산출한 출구(있으면 grid DR 대신 사용 → 게이트가 항상 프리팹 바닥 위)

        // 7. 블록 빌드 — 각 패스(블록/천장/조명) 사이에 yield를 넣어 한 프레임에 몰리는 Instantiate 스파이크를 분산.
        //    화면은 전환 커버로 가려져 있고(EnterRoomAsync), 블록은 아래 HideAllBlockRenderers까지 숨김 상태이며
        //    리프프로그 앵커로 카메라 밖(+300)에 빌드되므로 순서/연출에 영향 없음. 순서는 await로 보존된다.
        var blocks = new System.Collections.Generic.List<MapBuilder.PlacedBlock>();
        if (useCustomArena)
        {
            // NavMesh(아래 BuildMapNavMeshAsync)가 프리팹 바닥을 포함하도록 roomGO 자식으로 먼저 인스턴스화한다.
            // localPosition 0 = 방 중앙(anchor). heading 회전은 격자 입구 문(cls.entrance)과 맞물리도록 동일 quarterTurns 적용.
            // (mirror는 적용하지 않음 — 프리팹 입구를 변 중앙에 두면 좌우 미러가 입구 위치에 영향 없음)
            GameObject arena = null;
            try
            {
                arena = await Managers.AddressableManager.InstantiateAsync(entry.arena_template_key, roomGO.transform);
            }
            catch (System.OperationCanceledException) { throw; }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[GameRunBootstrapper] arena '{entry.arena_template_key}' 인스턴스 실패: {ex.Message}");
            }
            if (arena != null)
            {
                arena.transform.localPosition = Vector3.zero;
                arena.transform.localRotation = Quaternion.Euler(0f, 90f * quarterTurns, 0f);

                // 진입 위치: 프리팹의 PlayerSpawn 마커(디자이너 지정)를 우선 사용. 없으면 grid 입구로 폴백.
                // 회전 적용 후의 월드 좌표라 heading 회전이 자동 반영된다 — 프리팹 바닥을 격자 입구에 맞출 필요가 없다.
                var playerSpawn = arena.transform.Find("PlayerSpawn");
                if (playerSpawn != null) customArenaEntryPos = playerSpawn.position;
                else Debug.LogWarning($"[GameRunBootstrapper] arena '{entry.arena_template_key}'에 PlayerSpawn 자식 없음 — grid 입구로 폴백");

                // 출구: 프리팹 Exit 마커(Exit/Exit1/Exit2…)를 우선 사용 — PlayerSpawn 규약과 동일.
                customArenaExits = CollectArenaExitSlots(arena.transform, wallLayers * blockCellSize);
            }
            else
            {
                Debug.LogWarning($"[GameRunBootstrapper] arena '{entry.arena_template_key}' 미배치 — 빈 방으로 진행 ({entry.pool_key})");
            }
        }
        else
        {
            MapBuilder.CreateSafeFloor(w, h, blockCellSize, 0f, roomGO.transform);
            blocks = MapBuilder.Build(grid, palette, roomGO.transform, blockCellSize, blockBaseY, blockShopStallPrefab, wallLayers);
            await UniTask.Yield(ct);
            MapBuilder.BuildCeiling(grid, palette, roomGO.transform, blockCellSize, blockBaseY, wallLayers * blockCellSize);
            await UniTask.Yield(ct);
            if (palette != null) MapBuilder.BuildRoomLights(grid, roomGO.transform, blockCellSize, blockBaseY, wallLayers, palette.Lighting);
            await UniTask.Yield(ct);

            // 7-b. 문 복도 스텁 — 각 문(입구/출구) 바깥으로 통로를 뻗어 너머가 허공(절벽)으로 보이지 않게.
            //      blocks에 합쳐 디졸브/디스폰에 함께 동참시킨다.
            if (procDoorCorridorLength > 0 && palette != null)
            {
                float offX = (w - 1) * 0.5f * blockCellSize;
                float offZ = (h - 1) * 0.5f * blockCellSize;
                void AddCorridor(Vector2Int cell)
                {
                    var info        = doorInfos[cell];
                    var centerLocal = new Vector3(cell.x * blockCellSize - offX, blockBaseY, cell.y * blockCellSize - offZ);
                    blocks.AddRange(MapBuilder.BuildDoorCorridor(
                        palette, roomGO.transform, centerLocal, info.edge, info.width,
                        procDoorCorridorLength, blockCellSize, blockBaseY, wallLayers));
                }
                if (cls.entrance.HasValue) AddCorridor(cls.entrance.Value);
                if (cls.forward.HasValue)  AddCorridor(cls.forward.Value);
                foreach (var t in cls.turns) AddCorridor(t);
            }
        }

        // 8. 토큰 (PreBuild 스포너 → NavMesh → PostBuild 장식/보스)
        //    커스텀 아레나는 프리팹이 보스/스포너/장식을 모두 소유하므로 grid 토큰을 실행하지 않는다.
        //    (실행 시 grid의 B 토큰이 팔레트 보스 스포너를 중복 생성 → 보스 2마리 버그)
        var deferredSpawners = new System.Collections.Generic.List<UnityEngine.MonoBehaviour>();
        var tokenCtx = new TokenContext
        {
            Parent             = roomGO.transform,
            CellSize           = blockCellSize,
            BaseY              = blockBaseY,
            Theme              = ResolveRoomTheme(entry.theme),
            ActivePalette      = palette,
            DecorationCatalogs = decorationCatalogs,
            Grid               = grid,
            SpawnInfos         = spawnInfos,
            DeferredSpawners   = deferredSpawners,
            Ct                 = ct,
        };
        if (!useCustomArena) TokenParser.Execute(csv, w, h, tokenCtx, TokenPhase.PreBuild);

        HideAllBlockRenderers(blocks);
        await BuildMapNavMeshAsync(roomGO);
        if (!useCustomArena) TokenParser.Execute(csv, w, h, tokenCtx, TokenPhase.PostBuild);

        InitializeMinimapForRoom(roomGO, w, h);
        AttachRoomClearController(roomGO);

        // 상점 방이면 ShopRoomController 부착 — 진열 롤에 roomRng를 넘겨 결정적(이어하기 재현) 추첨.
        // (MapBuilder가 stall 타일에서 ShopStallInteraction을 이미 생성한 시점)
        if (IsShopCategory(entry.category))
            await SetupShopRoomAsync(roomGO, entry.pool_key, roomRng);
        else if (IsEventCategory(entry.category))
            SetupEventRoom(roomGO, entry.pool_key, roomRng);   // 챌린지 종류는 pool_key 명명 규약으로 유추
        else if (IsCrucibleCategory(entry.category))
            await SetupCrucibleRoomAsync(roomGO, roomRng);

        // 스포너 활성화 (Start 준비). 웨이브 Activate는 플레이어 배치 후 호출자가 수행.
        for (int i = 0; i < deferredSpawners.Count; i++)
            if (deferredSpawners[i] != null) deferredSpawners[i].enabled = true;

        // 등장 연출(디졸브)은 호출자(RunFlowController)가 화면 복귀 후 재생 — 생성 과정을 보여주기 위함.
        // (블록은 HideAllBlockRenderers로 숨겨진 상태로 반환됨)

        // 9. 결과 — 진입 위치 + 출구 슬롯 + 블록(디졸브용)
        var result = new ProcRoomResult
        {
            roomGO   = roomGO,
            blocks   = blocks,
            entryPos = customArenaEntryPos ?? (cls.entrance.HasValue
                ? CellToWorldFloor(cls.entrance.Value, anchor, w, h) + DoorInwardOffset(doorInfos[cls.entrance.Value].edge)
                : ResolvePlayerSpawnFromGrid(grid, anchor, w, h)),
            exits    = new System.Collections.Generic.List<ProcExitSlot>(),
        };
        float openingH = wallLayers * blockCellSize; // 개구부 높이 = 벽 높이

        // 커스텀 아레나에 Exit 마커가 있으면 그리드 DR 대신 사용 — 출구 게이트가 항상 프리팹 바닥 위에 배치된다.
        // (그리드는 프리팹 footprint보다 커서 DR 셀이 바닥 밖에 떨어지는 문제 회피. 입구 잠금은 생략 — 프리팹이 경계를 소유.)
        if (useCustomArena && customArenaExits != null && customArenaExits.Count > 0)
        {
            result.hasEntrance = false;
            result.exits.AddRange(customArenaExits);
        }
        else
        {
            if (cls.entrance.HasValue)
            {
                var de = doorInfos[cls.entrance.Value];
                result.hasEntrance = true;
                result.entrance = new ProcExitSlot {
                    worldPos = CellToWorldFloor(cls.entrance.Value, anchor, w, h), isForward = false, edge = de.edge,
                    openingWidth = de.width * blockCellSize, openingHeight = openingH };
            }
            if (cls.forward.HasValue)
            {
                var d = doorInfos[cls.forward.Value];
                result.exits.Add(new ProcExitSlot {
                    worldPos = CellToWorldFloor(cls.forward.Value, anchor, w, h), isForward = true, edge = d.edge,
                    openingWidth = d.width * blockCellSize, openingHeight = openingH });
            }
            foreach (var t in cls.turns)
            {
                var d = doorInfos[t];
                result.exits.Add(new ProcExitSlot {
                    worldPos = CellToWorldFloor(t, anchor, w, h), isForward = false, edge = d.edge,
                    openingWidth = d.width * blockCellSize, openingHeight = openingH });
            }
        }

        Debug.Log($"[GameRunBootstrapper] ProcRoom '{entry.pool_key}' 빌드 완료 @ {anchor} (출구 {result.exits.Count})");
        return result;
    }

    /// <summary>셀(x, z) → 바닥 표면(Y=anchor.y) 월드 좌표. MapBuilder의 중앙 오프셋과 동일.</summary>
    private Vector3 CellToWorldFloor(Vector2Int cell, Vector3 anchor, int w, int h)
    {
        float offX = (w - 1) * 0.5f * blockCellSize;
        float offZ = (h - 1) * 0.5f * blockCellSize;
        return new Vector3(anchor.x + cell.x * blockCellSize - offX, anchor.y, anchor.z + cell.y * blockCellSize - offZ);
    }

    /// <summary>문 엣지에서 방 안쪽으로 살짝 들어간 오프셋 (플레이어가 벽에 끼지 않도록).</summary>
    private Vector3 DoorInwardOffset(DoorEdge edge)
    {
        float d = blockCellSize * 1.5f;
        switch (edge)
        {
            case DoorEdge.North: return new Vector3(0f, 0f, -d);
            case DoorEdge.South: return new Vector3(0f, 0f,  d);
            case DoorEdge.East:  return new Vector3(-d, 0f, 0f);
            case DoorEdge.West:  return new Vector3( d, 0f, 0f);
            default:             return Vector3.zero;
        }
    }

    /// <summary>
    /// 커스텀 아레나 프리팹의 Exit 마커(이름이 "Exit"로 시작하는 자식)를 출구 슬롯으로 변환한다.
    /// 마커의 월드 위치 = 게이트 바닥 중앙, 마커의 +Z(forward) = 출구 방향(밖) → 엣지 산출.
    /// 마커가 없으면 빈 리스트(호출자가 grid 폴백).
    /// </summary>
    private System.Collections.Generic.List<ProcExitSlot> CollectArenaExitSlots(Transform arena, float openingHeight)
    {
        var list = new System.Collections.Generic.List<ProcExitSlot>();
        if (arena == null) return list;

        var all = arena.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            var t = all[i];
            if (t == arena || !t.name.StartsWith("Exit", System.StringComparison.OrdinalIgnoreCase)) continue;

            var edge  = EdgeFromForward(t.forward);
            float ow  = t.localScale.x > 0.01f ? t.localScale.x : GateWidth * blockCellSize;
            list.Add(new ProcExitSlot {
                worldPos     = t.position,
                isForward    = edge == DoorEdge.North,
                edge         = edge,
                openingWidth = ow,
                openingHeight = openingHeight });
        }
        return list;
    }

    /// <summary>월드 forward 벡터를 가장 가까운 카디널 DoorEdge로 매핑.</summary>
    private static DoorEdge EdgeFromForward(Vector3 fwd)
    {
        if (Mathf.Abs(fwd.z) >= Mathf.Abs(fwd.x))
            return fwd.z >= 0f ? DoorEdge.North : DoorEdge.South;
        return fwd.x >= 0f ? DoorEdge.East : DoorEdge.West;
    }

    /// <summary>grid의 P 토큰 위치 → 월드. 없으면 anchor.</summary>
    private Vector3 ResolvePlayerSpawnFromGrid(TileType[,] grid, Vector3 anchor, int w, int h)
    {
        var p = MapDataLoader.FindFirst(grid, TileType.PlayerSpawn);
        return p.x >= 0 ? CellToWorldFloor(p, anchor, w, h) : anchor;
    }

    /// <summary>
    /// toZone 방향 엣지(벽 링)에서 실제 문 개구부(Floor 셀)의 중앙 로컬 좌표를 찾는다.
    /// MapBuilder와 동일한 중앙 오프셋 규약 사용. 개구부 없으면 false.
    /// </summary>
    private bool TryFindGateOpeningLocal(TileType[,] grid, ZoneLayoutEntry from, ZoneLayoutEntry to, out Vector3 localPos)
    {
        localPos = default;
        int w = grid.GetLength(0), h = grid.GetLength(1);
        float dX = to.world_center_x - from.world_center_x;
        float dZ = to.world_center_z - from.world_center_z;

        int sum = 0, count = 0;
        if (Mathf.Abs(dZ) > 0f)
        {
            int z = dZ >= 0 ? h - 1 : 0; // 북(+Z)=z h-1 / 남(-Z)=z 0
            for (int x = 0; x < w; x++)
                if (grid[x, z] == TileType.Floor) { sum += x; count++; }
            if (count == 0) return false;
            float cx = sum / (float)count; // 개구부 중앙 x
            localPos = new Vector3((cx - (w - 1) * 0.5f) * blockCellSize, 0f, (z - (h - 1) * 0.5f) * blockCellSize);
            return true;
        }
        else
        {
            int x = dX >= 0 ? w - 1 : 0; // 동(+X)=x w-1 / 서(-X)=x 0
            for (int z = 0; z < h; z++)
                if (grid[x, z] == TileType.Floor) { sum += z; count++; }
            if (count == 0) return false;
            float cz = sum / (float)count; // 개구부 중앙 z
            localPos = new Vector3((x - (w - 1) * 0.5f) * blockCellSize, 0f, (cz - (h - 1) * 0.5f) * blockCellSize);
            return true;
        }
    }

    /// <summary>fromZone에서 toZone 방향을 계산해 fromZone 엣지의 localPosition을 반환.</summary>
    private Vector3 CalcGateExitPositionTo(ZoneLayoutEntry fromZone, ZoneLayoutEntry toZone)
    {
        float halfW = fromZone.grid_width  * blockCellSize * 0.5f;
        float halfH = fromZone.grid_height * blockCellSize * 0.5f;

        float dX = toZone.world_center_x - fromZone.world_center_x;
        float dZ = toZone.world_center_z - fromZone.world_center_z;
        const float csvGridUnit = 55f;

        if (Mathf.Abs(dZ) > 0f)
        {
            // Z축 방향 게이트: 앞뒤 면에 배치하고 레인 차이(dX)만큼 X 오프셋 추가
            float signZ   = dZ >= 0 ? 1f : -1f;
            float xOffset = (dX / csvGridUnit) * halfW;
            return new Vector3(xOffset, 0f, signZ * halfH);
        }
        else
        {
            // 순수 좌우 게이트
            float signX = dX >= 0 ? 1f : -1f;
            return new Vector3(signX * halfW, 0f, 0f);
        }
    }

    /// <summary>
    /// fromZone 엣지에 배치된 게이트가 방 내부(플레이어 접근 방향)를 향하도록 localRotation을 반환.
    /// 게이트 프리팹의 +Z가 정면이라고 가정.
    /// </summary>
    private Quaternion CalcGateRotationTo(ZoneLayoutEntry fromZone, ZoneLayoutEntry toZone)
    {
        float dX = toZone.world_center_x - fromZone.world_center_x;
        float dZ = toZone.world_center_z - fromZone.world_center_z;

        // 방 내부를 향하는 벡터 = 출구 방향의 반대
        Vector3 inward = Mathf.Abs(dZ) > 0f
            ? new Vector3(0f, 0f, dZ >= 0 ? -1f : 1f)
            : new Vector3(dX >= 0 ? -1f : 1f, 0f, 0f);

        return Quaternion.LookRotation(inward);
    }

    /// <summary>
    /// Zone 0 출구 엣지에 StartRoomGate를 배치하고 씬 내 모든 StartRoomPickup에 게이트 레퍼런스를 주입.
    /// 로드아웃 준비 완료(캐릭터+무기 선택) 시 픽업이 EnableGate()를 호출해 게이트를 활성화한다.
    /// </summary>
    /// <summary>
    /// Zone 0 출구 방향에 StartRoomGate 1개를 배치한다.
    /// next_zone_indices가 여러 개여도 물리 게이트는 1개만 생성 —
    /// 실제 진출 존 선택은 트리거 후 ShowZoneSelectionAsync가 UI로 처리한다.
    /// </summary>
    private void CreateStartRoomGates(
        ZoneLayoutEntry startZone,
        System.Collections.Generic.List<ZoneLayoutEntry> allZones,
        GameObject zoneGO)
    {
        if (startGatePrefab == null || string.IsNullOrEmpty(startZone.next_zone_indices))
        {
            Debug.LogWarning("[GameRunBootstrapper] CreateStartRoomGates: startGatePrefab 미할당 또는 next_zone_indices 없음");
            return;
        }

        // 첫 번째 유효한 next zone 방향으로만 게이트 1개 배치
        int primaryToIdx = -1;
        foreach (var part in startZone.next_zone_indices.Split('|'))
        {
            if (int.TryParse(part.Trim(), out int idx)) { primaryToIdx = idx; break; }
        }
        if (primaryToIdx < 0) return;

        var toZone = allZones?.Find(z => z.zone_index == primaryToIdx);
        if (toZone == null) return;

        // 실제 문 개구부(엣지의 Floor 셀) 중앙에 배치. 개구부 없으면 엣지 중점으로 폴백.
        var grid = MapDataLoader.Parse(startZone.grid_csv);
        var gateLocalPos = (grid != null && TryFindGateOpeningLocal(grid, startZone, toZone, out var openLocal))
            ? openLocal
            : CalcGateExitPositionTo(startZone, toZone);
        var gateLocalRot = CalcGateRotationTo(startZone, toZone);
        var gateGO = Instantiate(startGatePrefab, Vector3.zero, Quaternion.identity, zoneGO.transform);
        gateGO.transform.localPosition = gateLocalPos;
        gateGO.transform.localRotation = gateLocalRot;
        gateGO.name = "StartRoomGate";

        if (gateGO.GetComponent<StartRoomGate>() == null)
            gateGO.AddComponent<StartRoomGate>();

        Debug.Log($"[GameRunBootstrapper] StartRoomGate 배치 완료 → Zone {primaryToIdx} 방향");
    }

    private void CreateZoneExitGates(int zoneIndex, ZoneLayoutEntry zone,
        System.Collections.Generic.List<ZoneLayoutEntry> allZones, GameObject zoneGO)
    {
        if (_run?.ZoneProgression == null || zoneGO == null || string.IsNullOrEmpty(zone.next_zone_indices)) return;
        foreach (var part in zone.next_zone_indices.Split('|'))
        {
            if (!int.TryParse(part.Trim(), out int toZoneIdx)) continue;
            var toZone = allZones?.Find(z => z.zone_index == toZoneIdx);
            if (toZone == null) continue;

            var gateLocalPos = CalcGateExitPositionTo(zone, toZone);
            var gateLocalRot = CalcGateRotationTo(zone, toZone);
            if (startGatePrefab == null)
            {
                Debug.LogWarning("[GameRunBootstrapper] CreateZoneExitGates: startGatePrefab 미할당 — 게이트 스킵");
                continue;
            }
            var gateGO = Object.Instantiate(startGatePrefab, Vector3.zero, Quaternion.identity, zoneGO.transform);
            gateGO.transform.localPosition = gateLocalPos;
            gateGO.transform.localRotation = gateLocalRot;

            var gate = gateGO.GetComponent<StartRoomGate>() ?? gateGO.AddComponent<StartRoomGate>();
            gate.InitGate(zoneIndex, toZoneIdx, toZone.label, toZone.category, _run.ZoneProgression, GateWidth * blockCellSize);
            gateGO.SetActive(false);
            _run.ZoneProgression.RegisterExitGate(zoneIndex, toZoneIdx, gate);
        }
    }

    // ── 새 단일-세계 구조용 공개 API ────────────────────────────────────────────

    /// <summary>
    /// 지정 roomId의 맵을 worldCenter 위치에 생성한다.
    /// RoomOpenSequencer에서 호출. 기존 맵(다른 방)은 해제하지 않는다.
    /// </summary>
    public async UniTask SpawnBlockMapAtAsync(string roomId, Vector3 worldCenter, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(roomId)) return;

        var roomEntry = Managers.MapData?.GetById(roomId);
        if (roomEntry == null || string.IsNullOrEmpty(roomEntry.grid_csv))
        {
            Debug.LogWarning($"[GameRunBootstrapper] SpawnBlockMapAtAsync: roomId '{roomId}' 없음 또는 grid_csv 비어있음");
            return;
        }

        await SpawnBlockMapAsync(roomEntry, worldCenter, ct);
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
            await BuildMapNavMeshAsync(_currentMapGO);
        }
        finally
        {
            _isSpawning = false;
        }
    }

    // worldCenter 기본값 = Vector3.zero → 기존 동작 유지
    private async UniTask SpawnBlockMapAsync(MapRoomEntry roomEntry, Vector3 worldCenter = default, CancellationToken ct = default, bool instantEntrance = false, bool suppressInteractables = false)
    {
        ct = ct == default ? this.GetCancellationTokenOnDestroy() : ct;
        var spawnInfos = new System.Collections.Generic.Dictionary<Vector2Int, MapDataLoader.CellSpawnInfo>();
        var grid = MapDataLoader.Parse(roomEntry.grid_csv, spawnInfos);
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
            // worldCenter 오프셋을 더해 단일-세계 배치 지원
            _pendingPlayerSpawnPos = worldCenter + new Vector3(
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

        // 맵 루트 — worldCenter가 있으면 해당 위치에 배치 (단일-세계 구조)
        var mapGO = new GameObject($"BlockMap_{roomEntry.room_id}");
        mapGO.transform.SetParent(mapRoot, false);
        mapGO.transform.position = worldCenter;
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
        var blocks = MapBuilder.Build(grid, activePalette, mapGO.transform, blockCellSize, blockBaseY, blockShopStallPrefab, wallLayers);
        MapBuilder.BuildCeiling(grid, activePalette, mapGO.transform, blockCellSize, blockBaseY, wallLayers * blockCellSize);
        if (activePalette != null) MapBuilder.BuildRoomLights(grid, mapGO.transform, blockCellSize, blockBaseY, wallLayers, activePalette.Lighting);
        Debug.Log($"[GameRunBootstrapper] BlockMap: {roomEntry.room_id} ({w}x{h}), {blocks.Count}블록");

        // 토큰 실행에 사용할 공유 컨텍스트 — PreBuild/PostBuild 양쪽에서 재사용
        var deferredSpawners = new System.Collections.Generic.List<UnityEngine.MonoBehaviour>();
        var tokenCtx = new TokenContext
        {
            Parent                 = mapGO.transform,
            CellSize               = blockCellSize,
            BaseY                  = blockBaseY,
            Theme                  = ResolveRoomTheme(roomEntry.theme),
            RoomEntry              = roomEntry,
            DecorationCatalogs     = decorationCatalogs,
            ActivePalette          = activePalette,
            CharacterPickupPrefabs = suppressInteractables ? null : characterPickupPrefabs,
            WeaponPickupPrefabs    = suppressInteractables ? null : weaponPickupPrefabs,
            Grid                   = grid,
            SpawnInfos             = spawnInfos,
            DeferredSpawners       = deferredSpawners,
            Ct                     = ct,
        };

        // PreBuild: 몬스터 스포너 배치 + 비활성화 (MonsterSpawnHandler/MonsterSpawnCandidateHandler)
        // NavMesh 빌드 전에 실행해 풀 프리웜이 입장 연출 전까지 완료될 수 있도록 한다.
        TokenParser.Execute(roomEntry.grid_csv, w, h, tokenCtx, TokenPhase.PreBuild);

        // FieldPrefab 로드 — NavMesh 빌드 전에 배치해 수동 배치 오브젝트가 NavMesh에 반영되도록 한다.
        var fieldInstance = await LoadFieldPrefabAsync(mapGO, ct);

        // NavMesh 빌드 — MapBuilder.Build + FieldPrefab 완료 후 수행.
        // 장식 프리팹(나무 등)은 Read/Write OFF 메시를 포함할 수 있으므로 NavMesh 빌드 이후에 배치.
        await BuildMapNavMeshAsync(mapGO);

        // 벽 투명도 사전 적용 — Material 생성 비용이 있으므로 8개마다 프레임을 반환한다
        int wallCount = 0;
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].tileType == TileType.Wall && blocks[i].instance != null)
            {
                ApplyWallTransparency(blocks[i].instance, 0.72f);
                if (++wallCount % 8 == 0)
                    await UniTask.Yield(PlayerLoopTiming.Update);
            }
        }

        // 렌더러 선숨김 — 카메라 페이드인 중 블록/필드 오브젝트가 팝업으로 보이지 않도록.
        // instantEntrance(시작방/허브): 디졸브 없이 완성된 방으로 보여주므로 숨기지 않는다.
        if (!instantEntrance)
        {
            HideAllBlockRenderers(blocks);
            if (fieldInstance != null)
            {
                var rs = fieldInstance.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rs.Length; i++) rs[i].enabled = false;
            }
        }

        // IntroFade(sortingOrder=9999)가 아직 불투명하게 UI_SceneLoading을 덮고 있는 이 시점에
        // 로딩 커버를 해제한다. IntroFade 뒤에서 UI_SceneLoading이 조용히 사라지므로 플레이어 눈에 안 보임.
        AppBootstrapper.Instance?.NotifySceneReady();

        // 카메라 페이드인 + Dissolve 머티리얼 프리로드 + 몬스터 풀 프리웜을 병렬로 수행.
        // PreBuild 후 스포너가 씬에 존재하므로 GetComponentsInChildren으로 찾을 수 있다.
        var mapCenter = mapGO != null ? mapGO.transform.position : Vector3.zero;
        await UniTask.WhenAll(
            DissolveEffect.WarmupAsync(ct),
            GameCameraController.Instance?.PrepareMapViewAsync(mapCenter, 0.4f, ct) ?? UniTask.CompletedTask,
            PrewarmSpawnersAsync(mapGO, ct));

        // 입장 연출 (풀이 이미 프리웜된 상태이므로 연출 중 Instantiate 없음)
        // instantEntrance(시작방/허브): 디졸브 생략 — 이미 블록이 보이는 완성 상태.
        if (!instantEntrance)
        {
            var entranceCtx = new MapEntranceContext(roomEntry);
            await MapEntranceRegistry.Resolve(roomEntry.entrance).PlayAsync(blocks, entranceCtx, ct);
        }

        // PostBuild: 장식(d*) / 보스 스폰(B) / 픽업(WP, CP) — NavMesh 빌드 이후에 배치
        TokenParser.Execute(roomEntry.grid_csv, w, h, tokenCtx, TokenPhase.PostBuild);

        // 미니맵 구독 — PostBuild 이후 스포너가 모두 배치된 시점에 수행
        InitializeMinimapForRoom(mapGO, w, h);

        // 방 클리어 컨트롤러 부착 — 미니맵 구독 이후, PostBuild(BossSpawner) 이후여야 한다.
        AttachRoomClearController(mapGO);

        // 스포너 활성화 → Start() 실행 → 스폰 준비
        for (int i = 0; i < deferredSpawners.Count; i++)
            if (deferredSpawners[i] != null) deferredSpawners[i].enabled = true;

        // 기존 방 모드: 플레이어가 이미 방 안에 있으므로 즉시 웨이브 활성화
        if (mapGO.TryGetComponent<RoomWaveController>(out var waveCtrl))
            waveCtrl.Activate();

        // 챕터 필드 구조물 디졸브 등장 — LoadFieldPrefabAsync로 NavMesh 전 배치된 인스턴스를 이 시점에 표시
        await RevealFieldPrefabAsync(fieldInstance, ct);

        // 상점 방이면 ShopRoomController 부착 및 카탈로그 주입
        // (레거시/단일세계 경로 — 방 시드 미보유 → roomRng=null 전역 Random 폴백)
        if (IsShopCategory(roomEntry.category))
            await SetupShopRoomAsync(mapGO, roomEntry.room_id);
        else if (IsEventCategory(roomEntry.category))
            SetupEventRoom(mapGO, roomEntry.room_id);   // 챌린지 종류는 room_id 명명 규약으로 유추
        else if (IsCrucibleCategory(roomEntry.category))
            await SetupCrucibleRoomAsync(mapGO, null);
    }

    /// <summary>FieldPrefab을 로드해 mapParent 하위에 배치. NavMesh 빌드 전에 호출해 수동 배치 오브젝트를 NavMesh에 반영한다.</summary>
    private async UniTask<GameObject> LoadFieldPrefabAsync(GameObject mapParent, CancellationToken ct)
    {
        var key = _run?.ActiveFieldPrefabKey;
        if (string.IsNullOrEmpty(key)) return null;

        var prefab = await Managers.AddressableManager.TryLoadAssetAsync<GameObject>(key);
        if (prefab == null)
        {
            Debug.LogWarning($"[GameRunBootstrapper] FieldPrefab '{key}' 로드 실패 — 스킵");
            return null;
        }

        ct.ThrowIfCancellationRequested();

        var instance = Instantiate(prefab, mapParent.transform);
        instance.name = $"FieldStructure_{key}";
        return instance;
    }

    /// <summary>LoadFieldPrefabAsync로 배치된 인스턴스를 디졸브 등장 연출로 표시.</summary>
    private static async UniTask RevealFieldPrefabAsync(GameObject instance, CancellationToken ct)
    {
        if (instance == null) return;
        await DissolveEffect.PlayAppearAsync(instance, 0.6f, ct);
    }

    private static bool IsShopCategory(string category)
    {
        if (string.IsNullOrEmpty(category)) return false;
        return category.Trim().Equals("Shop", System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEventCategory(string category)
    {
        if (string.IsNullOrEmpty(category)) return false;
        return category.Trim().Equals("Event", System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCrucibleCategory(string category)
    {
        if (string.IsNullOrEmpty(category)) return false;
        return category.Trim().Equals("Crucible", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>재련소 방 셋업 — 비전투 스테이션(상점과 동일 흐름). P5에서 CrucibleRoomController(NPC+강화/승급 UI) 부착.</summary>
    private async UniTask SetupCrucibleRoomAsync(GameObject roomGO, System.Random roomRng = null)
    {
        if (roomGO == null || _run == null) return;

        var controller = roomGO.AddComponent<CrucibleRoomController>();

        // 강화 데이터 로드(없으면 서비스 기본 곡선 폴백)
        var table = await WeaponEnhanceService.EnsureLoadedAsync();

        // 재련공 NPC 프리팹 로드 (전용 키 → 상점 NPC 폴백)
        GameObject npcPrefab = null;
        if (!string.IsNullOrEmpty(crucibleNpcAddressableKey))
            npcPrefab = await Managers.AddressableManager.TryLoadAssetAsync<GameObject>(crucibleNpcAddressableKey);
        if (npcPrefab == null && !string.IsNullOrEmpty(shopNpcAddressableKey))
            npcPrefab = await Managers.AddressableManager.TryLoadAssetAsync<GameObject>(shopNpcAddressableKey);
        if (npcPrefab == null)
            Debug.LogWarning("[GameRunBootstrapper] 재련소 NPC 프리팹 로드 실패 — 재련소 UI를 열 수 없습니다.");

        controller.Initialize(_run, table, roomRng, npcPrefab);
    }

    /// <summary>
    /// 이벤트방 셋업 — 전투 챌린지: 스포너가 있어 RoomWaveController가 붙은 방에 성과 오버레이를 얹는다.
    /// arena_template_key로 챌린지 종류 지정("hitless"/"hitless:2"/"timelimit:30"), 미지정=속공 45초 기본.
    /// 스포너 없는(비전투) 이벤트방은 상호작용 챌린지 담당(후속) — 여기선 무동작.
    /// </summary>
    private void SetupEventRoom(GameObject roomGO, string typeHint, System.Random roomRng = null)
    {
        if (roomGO == null || _run == null) return;

        if (roomGO.TryGetComponent<RoomWaveController>(out _))
        {
            // 전투 챌린지 — 스포너 있는 이벤트방에 성과 오버레이.
            var (type, param) = ParseChallengeType(typeHint);
            var overlay = roomGO.AddComponent<CombatChallengeOverlay>();
            overlay.Initialize(_run, type, param);
            Debug.Log($"[GameRunBootstrapper] 이벤트 전투 챌린지 부착: {type}({param}) — {typeHint}");
            return;
        }

        // 비전투 — pool_key에 "gamble" 포함 시 도박 상자 상호작용. 그 외(서약 sanctum 등)는 무동작(즉시 클리어).
        if (!string.IsNullOrEmpty(typeHint) && typeHint.ToLowerInvariant().Contains("gamble"))
        {
            int seed = roomRng?.Next() ?? Mathf.Abs((typeHint ?? "gamble").GetHashCode());
            var gamble = roomGO.AddComponent<GambleBoxChallenge>();
            gamble.Initialize(_run, luckRollTable, clearEndEffectPrefab, clearEndEffect2Prefab, seed);
            Debug.Log($"[GameRunBootstrapper] 이벤트 도박 상자 부착 (seed={seed}) — {typeHint}");
        }
    }

    /// <summary>pool_key 등 키 문자열에서 챌린지 종류 유추: "hitless/flawless"=무결, "speed/timelimit/rush"=속공, 기본=속공45.
    /// arena_template_key는 커스텀 아레나 프리팹 전용이므로 여기 쓰지 않고 pool_key 명명 규약을 사용한다.</summary>
    private static (CombatChallengeOverlay.OverlayType type, float param) ParseChallengeType(string key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            string k = key.Trim().ToLowerInvariant();
            if (k.Contains("hitless") || k.Contains("flawless"))
                return (CombatChallengeOverlay.OverlayType.Hitless, ParseChallengeParam(k, 2f));
            if (k.Contains("speed") || k.Contains("timelimit") || k.Contains("rush"))
                return (CombatChallengeOverlay.OverlayType.TimeLimit, ParseChallengeParam(k, 45f));
        }
        return (CombatChallengeOverlay.OverlayType.TimeLimit, 45f);
    }

    private static float ParseChallengeParam(string k, float fallback)
    {
        int i = k.IndexOf(':');
        if (i >= 0 && float.TryParse(k.Substring(i + 1), out var v)) return v;
        return fallback;
    }

    private static bool IsStartCategory(string category)
    {
        if (string.IsNullOrEmpty(category)) return false;
        return category.Trim().Equals("Start", System.StringComparison.OrdinalIgnoreCase);
    }

/// <summary>스포너 배치 계획 적용 — 확정(M)은 항상 유지, 후보(m)는 max 한도 내에서 랜덤 선택.
    /// 선택되지 않은 후보는 Floor로 치환된다.
    /// 규칙:
    ///   · max ≤ 0           : 모든 M/m 전체 활성 (제한 없음)
    ///   · 확정 0 + 후보 0   : 아무것도 안 함 (max > 0이어도 스포너 미생성)
    ///   · 확정 ≥ max        : 확정 전부 유지, 후보 전부 Floor 치환
    ///   · 확정 &lt; max       : 확정 유지 + 후보 중 (max - 확정수)개 랜덤 선택
    /// </summary>
    // rng != null이면 결정적(이어하기 재현). null이면 전역 Random(레거시 경로).
    private static void ApplyMonsterSpawnerPlan(TileType[,] grid, int max, System.Random rng = null)
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
            int j = rng != null ? rng.Next(0, i + 1) : UnityEngine.Random.Range(0, i + 1);
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

    /// <summary>
    /// CSV의 world_center 좌표를 실제 월드 좌표로 변환.
    /// world_center_z는 Python 스크립트에서 실제 방 크기 기반으로 계산된 값.
    /// world_center_x는 lane×55 블록 스텝 유지.
    /// </summary>
    private Vector3 CalcZoneWorldCenter(ZoneLayoutEntry zone)
    {
        return new Vector3(
            zone.world_center_x * blockCellSize,
            0f,
            zone.world_center_z * blockCellSize
        );
    }

    /// <summary>
    /// corridor_style 키로 CorridorStyleSO를 검색한다.
    /// 일치 항목이 없거나 corridorStyles가 비어있으면 null 반환 → Spawn 호출이 생략된다.
    /// </summary>
    private CorridorStyleSO ResolveCorridorStyle(string key)
    {
        if (corridorStyles == null || string.IsNullOrEmpty(key)) return null;
        foreach (var s in corridorStyles)
            if (s != null && string.Equals(s.name, key, System.StringComparison.OrdinalIgnoreCase))
                return s;
        return null;
    }

    /// <summary>
    /// 새로 스폰된 존(toZone)과 이미 스폰된 인접 존 사이에 코리더 타일을 생성한다.
    /// corridorStyles가 할당되어 있고 style.floorTilePrefab이 있어야 실제 타일이 생성된다.
    /// </summary>
    private void SpawnCorridorsForZone(
        ZoneLayoutEntry newZone, Vector3 newCenter,
        System.Collections.Generic.List<ZoneLayoutEntry> allZones,
        Transform parent)
    {
        if (allZones == null || _run?.ZoneProgression == null) return;

        var style = ResolveCorridorStyle(newZone.corridor_style);

        foreach (var other in allZones)
        {
            if (other.zone_index == newZone.zone_index) continue;
            if (!_run.ZoneProgression.IsSpawned(other.zone_index)) continue;

            bool newToOther = ContainsZoneIndex(newZone.next_zone_indices, other.zone_index);
            bool otherToNew = ContainsZoneIndex(other.next_zone_indices, newZone.zone_index);
            if (!newToOther && !otherToNew) continue;

            var otherStyle  = style ?? ResolveCorridorStyle(other.corridor_style);
            var otherCenter = CalcZoneWorldCenter(other);

            if (newToOther)
                CorridorBridgeSpawner.Spawn(newZone, other, newCenter, otherCenter, otherStyle, parent, blockCellSize, GateWidth);
            else
                CorridorBridgeSpawner.Spawn(other, newZone, otherCenter, newCenter, otherStyle, parent, blockCellSize, GateWidth);
        }
    }

    private static bool ContainsZoneIndex(string nextIndices, int index)
    {
        if (string.IsNullOrEmpty(nextIndices)) return false;
        foreach (var part in nextIndices.Split('|'))
            if (int.TryParse(part.Trim(), out int v) && v == index) return true;
        return false;
    }

    /// <summary>
    /// 그리드를 MapBuilder.Build()에 넘기기 전, 연결된 모든 인접 존 방향의 벽을 런타임에 개방한다.
    /// CSV의 고정 중앙 개구부 대신 실제 세계 좌표 기반으로 열/행을 계산하므로
    /// 아직 스폰되지 않은 비활성 존의 위치도 미리 매칭해 벽을 뚫는다.
    /// </summary>
    private static void OpenWallsForConnections(
        TileType[,] grid,
        ZoneLayoutEntry zone,
        System.Collections.Generic.List<ZoneLayoutEntry> allZones,
        int gateWidth = 5)
    {
        int w    = grid.GetLength(0);
        int h    = grid.GetLength(1);
        int half = gateWidth / 2;

        var connected = new System.Collections.Generic.HashSet<int>();
        if (!string.IsNullOrEmpty(zone.next_zone_indices))
            foreach (var p in zone.next_zone_indices.Split('|'))
                if (int.TryParse(p.Trim(), out int i)) connected.Add(i);
        foreach (var other in allZones)
            if (ContainsZoneIndex(other.next_zone_indices, zone.zone_index))
                connected.Add(other.zone_index);

        // CalcGateExitPositionTo와 동일 기준: dZ != 0이면 항상 Z축 우선
        const float csvGridUnit = 55f;

        foreach (int idx in connected)
        {
            var other = allZones.Find(z => z.zone_index == idx);
            if (other == null) continue;

            float dX = other.world_center_x - zone.world_center_x;
            float dZ = other.world_center_z - zone.world_center_z;

            if (Mathf.Abs(dZ) > 0.5f)
            {
                // 북/남 벽 개방. 대각선 연결(dX != 0)도 Z벽을 뚫되 X 레인 오프셋 적용
                int wallZ = dZ > 0f ? h - 1 : 0;
                float xRatio = Mathf.Abs(dX) > 0.5f ? dX / csvGridUnit : 0f;
                int cx = Mathf.RoundToInt(w * 0.5f + xRatio * w * 0.5f);
                cx = Mathf.Clamp(cx, half + 1, w - half - 2);
                for (int x = cx - half; x <= cx + half; x++)
                    if (x > 0 && x < w - 1)
                        grid[x, wallZ] = TileType.Floor;
            }
            else if (Mathf.Abs(dX) > 0.5f)
            {
                // 동/서 벽 개방 (순수 X축 연결)
                int wallX = dX > 0f ? w - 1 : 0;
                int cz    = h / 2;
                for (int z2 = Mathf.Max(1, cz - half); z2 <= Mathf.Min(h - 2, cz + half); z2++)
                    grid[wallX, z2] = TileType.Floor;
            }
        }
    }

    /// <summary>존 목록의 모든 연결 쌍에 대해 CorridorBridgeSpawner를 호출한다.
    /// SpawnWorldMapAsync·SpawnRemainingWorldZonesAsync 같은 일괄 스폰 경로에서 사용.</summary>
    private void SpawnAllCorridors(
        System.Collections.Generic.List<ZoneLayoutEntry> zones,
        Transform parent)
    {
        if (corridorStyles == null || corridorStyles.Length == 0) return;
        var done = new System.Collections.Generic.HashSet<(int, int)>();
        foreach (var zone in zones)
        {
            if (string.IsNullOrEmpty(zone.next_zone_indices)) continue;
            foreach (var part in zone.next_zone_indices.Split('|'))
            {
                if (!int.TryParse(part.Trim(), out int toIdx)) continue;
                var key = (Mathf.Min(zone.zone_index, toIdx), Mathf.Max(zone.zone_index, toIdx));
                if (!done.Add(key)) continue;
                var toZone = zones.Find(z => z.zone_index == toIdx);
                if (toZone == null) continue;
                var style      = ResolveCorridorStyle(zone.corridor_style) ?? ResolveCorridorStyle(toZone.corridor_style);
                var fromCenter = CalcZoneWorldCenter(zone);
                var toCenter   = CalcZoneWorldCenter(toZone);
                CorridorBridgeSpawner.Spawn(zone, toZone, fromCenter, toCenter, style, parent, blockCellSize, GateWidth);
            }
        }
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
            // URP Lit 계열만 투명화 지원. 커스텀 셰이더(예: AZURE Nature/Surface)는 _BaseColor가 없어 건너뜀(불투명 유지).
            if (r.sharedMaterial == null || !r.sharedMaterial.HasProperty("_BaseColor")) continue;
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

/// <summary>맵 루트에서 MonsterSpawner를 수집해 스폰 테이블의 모든 풀을 미리 채운다.
    /// 입장 연출 재생 중 병렬 실행해 첫 스폰 프레임 드랍을 방지한다.</summary>
    private async UniTask PrewarmSpawnersAsync(GameObject mapGO, CancellationToken ct)
    {
        if (mapGO == null) return;

        var allSpawners = mapGO.GetComponentsInChildren<MonsterSpawner>(true);
        if (allSpawners.Length == 0) return;

        var tasks = new System.Collections.Generic.List<UniTask>(allSpawners.Length);
        foreach (var spawner in allSpawners)
            tasks.Add(spawner.PrewarmPoolsAsync(3, ct));

        await UniTask.WhenAll(tasks);
    }

    /// <summary>방 클리어 카운터를 맵 루트에 부착. GetComponentsInChildren으로 MonsterSpawner/BossSpawner를 수집.
    /// 스포너가 하나도 없으면 컨트롤러를 생성하지 않는다 (상점/이벤트 방 등).</summary>
    private void AttachRoomClearController(GameObject mapGO)
    {
        if (mapGO == null || _run == null) return;

        var spawners    = new System.Collections.Generic.List<MonsterSpawner>(mapGO.GetComponentsInChildren<MonsterSpawner>(true));
        var bossSpawner = mapGO.GetComponentInChildren<BossSpawner>(true);

        if (spawners.Count == 0 && bossSpawner == null) return;

        var controller = mapGO.AddComponent<RoomWaveController>();
        controller.Initialize(_run, spawners, bossSpawner, luckRollTable, clearEndEffectPrefab, clearEndEffect2Prefab);
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

    /// <summary>
    /// 상점 룸에 ShopRoomController를 부착하고 진열을 초기화한다.
    /// roomRng: 방 시드 기반 결정적 RNG. 진열 롤 결정성(이어하기 재현)을 위해 절차 빌드 경로에서 전달.
    ///          null이면 진열 롤은 전역 Random으로 폴백(비결정적).
    /// </summary>
    private async UniTask SetupShopRoomAsync(GameObject mapGO, string roomId, System.Random roomRng = null)
    {
        var controller = mapGO.AddComponent<ShopRoomController>();

        // 신규 경로: SHOP_PRICE_DATA + LuckRollTable 기반 추첨 (catalog는 fallback용)
        var catalog = await LoadShopCatalogAsync(roomId);

        if (luckRollTable == null)
            Debug.LogWarning("[GameRunBootstrapper] LuckRollTable 미할당 — 상점 매대는 fallback 카탈로그를 사용합니다.");

        if (catalog == null && luckRollTable == null)
            Debug.LogWarning($"[GameRunBootstrapper] ShopCatalog/LuckRollTable 모두 없음: {roomId}. 진열대가 비어 있게 됩니다.");

        // NPC 프리팹 로드 (월드 매대 대신 NPC + UI로 진열)
        GameObject npcPrefab = null;
        if (!string.IsNullOrEmpty(shopNpcAddressableKey))
            npcPrefab = await Managers.AddressableManager.TryLoadAssetAsync<GameObject>(shopNpcAddressableKey);
        if (npcPrefab == null)
            Debug.LogWarning($"[GameRunBootstrapper] 상점 NPC 프리팹 로드 실패: {shopNpcAddressableKey}. 상점 UI를 열 수 없습니다.");

        controller.Initialize(_run, catalog, luckRollTable, shopSlotCount, roomRng,
                              npcPrefab, shopWeaponSlotFallback, shopRerollEnabled, shopRerollCost);
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

    // TODO: NavMeshSurface.BuildNavMesh()는 메인 스레드를 블로킹한다.
    //       완전한 비동기 처리는 NavMeshBuilder.UpdateNavMeshDataAsync() 전환이 필요하다.
    //       현재는 에이전트 타입 간 UniTask.Yield()로 프레임 분산만 적용한다.
    private async UniTask BuildMapNavMeshAsync(GameObject mapRootObject)
    {
        if (!buildRuntimeNavMesh || mapRootObject == null)
            return;

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

            // 에이전트 타입이 여러 개일 때 빌드 사이에 프레임을 반환한다
            if (i < agentCount - 1)
                await UniTask.Yield(PlayerLoopTiming.Update);
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
                string weaponKey = weaponSO != null ? null : debugDefaultWeaponKey;

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
    /// 베이스캠프(영속 허브)에서 로드아웃을 확정한 새 런의 챕터 진입.
    /// 바로 전투로 들어가면 어색하므로 Zone0를 "대기 방"으로 빌드한다 — 선택 콘텐츠(CP/WP 픽업·각성 제단·대사)는
    /// 모두 생략하고 로드아웃 기반 플레이어만 스폰한다. 출구 게이트(StartRoomGate)는 로드아웃이 이미 준비됐으므로
    /// 즉시 열린 상태가 되며, 통과 시 ExitStartRoomAsync가 서약 선택 후 StartProcGenRunAsync로 던전을 시작한다.
    /// </summary>
    private async UniTask StartWaitingRoomAsync()
    {
        Managers.Sound?.PlayBgmAsync(SoundKey.Bgm.InGame).Forget();

        // 대기 방 동안 전투 HUD 억제 — 출구 게이트 통과 시 ExitStartRoomAsync가 복원한다.
        UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(true);
        if (UIRootBootstrapper.Instance == null)
            await UniTask.WaitUntil(() => UIRootBootstrapper.Instance != null || !this);
        UIRootBootstrapper.Instance?.BindHudToRun(_run);

        // 진입 연출: 방 생성·카메라 배치를 검정으로 가린 뒤 둘러보기에서 페이드인으로 드러냄.
        await ScreenFade.Out(0f);

        // Zone0를 깨끗한 대기 방으로 빌드 — 선택 픽업(CP/WP)은 억제(BaseCamp에서 이미 선택).
        // 출구에 StartRoomGate가 배치되고 _pendingPlayerSpawnPos가 설정된다.
        await SpawnStartZoneFromLayoutAsync(this.GetCancellationTokenOnDestroy(), suppressInteractables: true);

        // 시작방 둘러보기 카메라 연출 (StartRoomAsync와 동일 구성).
        if (_currentMapGO != null && GameCameraController.Instance != null)
            await GameCameraController.Instance.PlayStartRoomTourAsync(_currentMapGO.transform.position, this.GetCancellationTokenOnDestroy());
        else
            await ScreenFade.In(0.4f);

        // 허브 신규 런: 세션 런을 정식으로 시작(Phase=Running + PlayerState 생성 + 이벤트 발행).
        // 절차 진행 흐름은 StartNewRunAsync를 거치지 않아 Phase가 NotRunning으로 남고, 그 결과
        // 챕터 게이트 통과 시 AdvanceToNextChapter가 !IsRunning으로 false를 반환해 런 클리어(베이스캠프 복귀)로
        // 잘못 분기하던 버그를 차단한다. BindPlayer 전에 호출해 PlayerState↔RuntimeStats 동기화·서약 초기화가 정상 동작하게 한다.
        if (_run != null && !_run.IsRunning)
            await _run.StartNewRunAsync(ResolveCurrentChapter(),
                key => Managers.AddressableManager.LoadAssetAsync<TextAsset>(key));

        // 로드아웃(body+유물+무기) 기반 스폰 — SpawnPlayerAsync가 로드아웃 키 해석·유물 적용·무기 장착을 처리하고
        // _pendingPlayerSpawnPos(Zone0 스폰 지점)에 배치한다.
        var player = await SpawnPlayerAsync(startBodyKey);
        if (player == null)
        {
            Debug.LogError("[GameRunBootstrapper] StartWaitingRoomAsync: 플레이어 스폰 실패");
            return;
        }

        _run?.BindPlayer(player);
        _run?.RequestHudMode(HUDIds.Mode.Combat);
        GameCameraController.Instance?.HandToGameplayCamera(player.transform);

        // 챕터 시작 대기방: 조립 서약 제단 배치(선택 픽업은 억제해도 서약 제단은 항상 제공)
        SpawnCovenantAltar(player.transform.position);

        // 대기방 도착 대사(방문 변형 — 첫 도착/재도착 다른 스크립트)
        await ShowWaitingRoomDialogueAsync(ResolveCurrentChapter());

        // 던전 빌드(StartProcGenRunAsync)는 여기서 호출하지 않는다 — 출구 게이트가 통과 시 시작한다.
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

        // 진입 연출: 방 생성·카메라 배치를 검정으로 가린 뒤 둘러보기 시작 시 페이드아웃으로 드러냄
        await ScreenFade.Out(0f);

        await SpawnStartZoneFromLayoutAsync(this.GetCancellationTokenOnDestroy());

        // 시작방 둘러보기 카메라 연출 — 시작 포즈에서 페이드아웃+레터박스로 시네마틱하게 진입 후 패닝
        if (_currentMapGO != null && GameCameraController.Instance != null)
            await GameCameraController.Instance.PlayStartRoomTourAsync(_currentMapGO.transform.position, this.GetCancellationTokenOnDestroy());
        else
            await ScreenFade.In(0.4f); // 둘러보기 미실행 시에도 검정 해제 보장

        // 각성 제단: 플레이어 스폰 지점 옆에 배치 (_pendingPlayerSpawnPos가 소비되기 전)
        SpawnAwakeningAltar();
        SpawnCovenantAltar(_pendingPlayerSpawnPos ?? Vector3.zero);

        await ShowStartRoomDialogueAsync();

        // CombatGirl 플레이어를 시작방에 바로 스폰 (위습 단계 제거). 유물은 시작방 유물 오브젝트에서 획득.
        // 로드아웃 준비(=무기 픽업 허용) — 단일 몸 체제라 body 키만 설정(CharacterData는 몸이 자체 로드).
        AppBootstrapper.Instance?.Loadout?.SetCharacter(null, startBodyKey);
        Vector3 startSpawnPos = _pendingPlayerSpawnPos ?? Vector3.zero;
        _pendingPlayerSpawnPos = null;
        SpawnCharacterInStartRoomAsync(startBodyKey, startSpawnPos, Quaternion.identity).Forget();
    }

    private void SpawnAwakeningAltar()
    {
        var basePos = _pendingPlayerSpawnPos ?? Vector3.zero;
        WorldAwakeningAltar.SpawnAt(basePos + new Vector3(4f, 0f, 2f));
    }

    /// <summary>챕터 시작 대기방의 조립 서약 제단(원인×효과). 세 대기방 경로 공통 — 첫 서약=실버 고정은 제단이 판정.</summary>
    private void SpawnCovenantAltar(Vector3 basePos)
    {
        WorldCovenantAltar.SpawnAt(basePos + new Vector3(-4f, 0f, 2f));
    }

    /// <summary>챕터 시작 대기방 도착 시 대사 재생(방문 변형). Chapter{N}_Enter: 첫 도착=컨셉 소개+준비, 재도착=지겨움/준비 변형.</summary>
    private async UniTask ShowWaitingRoomDialogueAsync(ChapterId chapter)
    {
        var dlg = Managers.DialogueData;
        if (dlg == null) return;
        if (!dlg.IsInitialized) await dlg.InitializeAsync();

        var lines = dlg.GetVisitLines($"Chapter{(int)chapter}_Enter");
        if (lines == null || lines.Length == 0) return;

        // 선택/편집 UI가 열려있으면 닫힐 때까지 대기 후 대사(대사끼리도 큐잉).
        await Managers.UI.WaitUntilNoBlockingPopupAsync();

        var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_DialoguePopup>();
        if (popup == null) return;
        try { await popup.ShowAsync(lines); }
        catch (System.OperationCanceledException) { }
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

        // 선택/편집 UI가 열려있으면 닫힐 때까지 대기 후 대사.
        await Managers.UI.WaitUntilNoBlockingPopupAsync();

        var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_DialoguePopup>();
        if (popup == null) return;

        try
        {
            await popup.ShowAsync(lines);
        }
        catch (System.OperationCanceledException) { }
    }

    /// <summary>스타트 방에서 캐릭터(CombatGirl)를 해당 위치에 스폰한다.</summary>
    public async UniTaskVoid SpawnCharacterInStartRoomAsync(
        string prefabKey, Vector3 pos, Quaternion rot)
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

        // 선택된 유물 적용 (CombatGirl 단일 몸 + 유물). Loadout.Relic 없으면 no-op.
        player.SetRelicAndApply(AppBootstrapper.Instance?.Loadout?.Relic);

        // 투어 종료 → 게임플레이 카메라로 핸드오프. BindPlayer(OnPlayerBound) 전에 호출해
        // 레거시 줌인 인트로(PlayIntroAsync)가 발화되지 않도록 _introStarted를 선점한다.
        GameCameraController.Instance?.HandToGameplayCamera(player.transform);

        // HUD를 플레이어에 바인딩 — 무기 선택 시 HUD 슬롯이 즉시 갱신되도록
        _run?.BindPlayer(player);
        _run?.RequestHudMode(HUDIds.Mode.Combat);

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

    /// <summary>
    /// 하데스식 절차생성 이어하기. 로컬 세이브의 마스터 시드/visitCount로 저장된 방을 재생성하고
    /// 로드아웃·인벤토리·서약·룬보드를 복원한 뒤 방 입구에서 전투를 새로 시작한다.
    /// 방 내부 전투 상태(적 위치/HP)·플레이어 좌표는 직렬화하지 않는다.
    /// </summary>
    private async UniTask ContinueProcGenRunAsync(CancellationToken ct)
    {
        var pm   = RunProgressManager.Instance;
        int slot = pm != null ? pm.ActiveSlotIndex : 0;
        var save = pm != null && pm.HasLocalRun(slot) ? pm.LoadLocalRun(slot) : null;
        if (save == null)
        {
            Debug.LogError("[GameRunBootstrapper] ContinueProcGenRunAsync: 로컬 세이브 없음 — 새 런으로 폴백");
            await StartCombatDirectAsync();
            return;
        }

        // 룸 풀 키: 서버 → SO → 규칙 폴백 (StartProcGenRunAsync와 동일)
        var chapter     = ResolveCurrentChapter();
        var serverEntry = Managers.ChapterData?.Get(chapter);
        var chapterSO   = chapterRegistry?.GetData(chapter);
        var poolKey     = serverEntry?.zone_pool_key;
        if (string.IsNullOrEmpty(poolKey)) poolKey = chapterSO?.zonePoolKey;
        if (string.IsNullOrEmpty(poolKey)) poolKey = $"CHAPTER_{(int)chapter}_ROOM_POOL";

        // HUD + 플레이어 (절차 재개 전에 바인드 — MovePlayer/시너지 재계산이 Player를 참조)
        UIRootBootstrapper.Instance?.BindHudToRun(_run);
        _run?.RequestHudMode(HUDIds.Mode.Combat);

        var player = await SpawnPlayerAsync(playerPrefabKey);
        if (player != null)
        {
            SetupEntrance(player);
            _run?.BindPlayer(player);
        }
        _run?.RequestHudMode(HUDIds.Mode.Combat);

        // 서약 복원 (BindPlayer가 CovenantHandler.Initialize 수행한 직후)
        RestoreCovenantsFromSave(save);

        // 룬 보드 복원 (점유 셀 기반 — 시너지 스탯/메커닉 재계산)
        RestoreRuneBoardFromSave(save);

        // 절차 흐름 재개 — 저장된 방을 동일 시드로 재생성, 입구에서 시작
        var flow = runFlowController != null ? runFlowController : gameObject.AddComponent<RunFlowController>();
        var meta = BuildMetaFromSave(save);
        var structureKey = ResolveStructureKey(chapter, serverEntry, chapterSO);
        await flow.ResumeAsync(meta, new Vector3(0f, 0f, 2000f), poolKey, ct, structureKey);

        Debug.Log($"[GameRunBootstrapper] 절차생성 이어하기 완료 — visit={save.visitCount}, room={save.currentRoomPoolKey}");
    }

    /// <summary>
    /// 챕터 전환으로 다음 챕터 씬을 로드한 직후 진입. 저장 이어하기와 달리 "새 챕터를 처음부터" 시작한다.
    /// 런 상태(아이템/버프/서약/시너지)는 DDOL 세션에 유지되며, 새 플레이어 인스턴스에 BindPlayer가 복원한다.
    /// 흐름: HUD 바인드 → 대기방(Zone0) 빌드 → 플레이어 스폰 → BindPlayer → 출구 게이트 통과 시 던전(StartProcGenRunAsync).
    /// </summary>
    private async UniTask StartNextChapterInSceneAsync(CancellationToken ct)
    {
        // 챕터 전환도 첫 챕터와 동일하게 "대기 방"을 띄운 뒤 출구 게이트로 던전에 진입한다.
        // (이전엔 곧장 던전으로 들어가 챕터별 대기방이 없었음 — 우선 방 구조만, 준비 스테이션은 후속.)
        UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(true);
        UIRootBootstrapper.Instance?.BindHudToRun(_run);
        _run?.RequestHudMode(HUDIds.Mode.Combat);

        await ScreenFade.Out(0f);

        // 새 챕터의 Zone0를 대기 방으로 빌드 (StartRoomGate 포함). 준비 픽업은 우선 억제(스테이션은 후속).
        await SpawnStartZoneFromLayoutAsync(ct, suppressInteractables: true);

        if (_currentMapGO != null && GameCameraController.Instance != null)
            await GameCameraController.Instance.PlayStartRoomTourAsync(_currentMapGO.transform.position, ct);
        else
            await ScreenFade.In(0.4f);

        // 로드아웃 기반 스폰 + 라이브 세션 복원(BindPlayer가 아이템/버프/서약/시너지 이월 처리).
        var player = await SpawnPlayerAsync(startBodyKey);
        if (player == null)
        {
            Debug.LogError("[GameRunBootstrapper] StartNextChapterInSceneAsync: 플레이어 스폰 실패");
            return;
        }
        _run?.BindPlayer(player);
        _run?.RequestHudMode(HUDIds.Mode.Combat);
        GameCameraController.Instance?.HandToGameplayCamera(player.transform);

        // 챕터 시작 대기방: 조립 서약 제단 배치(챕터마다 서약 획득 기회)
        SpawnCovenantAltar(player.transform.position);

        // 대기방 도착 대사(방문 변형 — 첫 도착/재도착 다른 스크립트)
        await ShowWaitingRoomDialogueAsync(ResolveCurrentChapter());

        // 던전(StartProcGenRunAsync)은 대기방 출구 게이트(StartRoomGate) 통과 시 시작된다.
        Debug.Log($"[GameRunBootstrapper] 챕터 {(int)ResolveCurrentChapter()} 대기방 진입");
    }

    private static RunMetaSnapshot BuildMetaFromSave(RunSaveData save)
    {
        var cooldowns = new System.Collections.Generic.List<CooldownKV>();
        if (!string.IsNullOrEmpty(save.cooldownsJson))
        {
            var w = JsonUtility.FromJson<CooldownListWrapper>(save.cooldownsJson);
            if (w?.items != null) cooldowns.AddRange(w.items);
        }

        return new RunMetaSnapshot
        {
            masterSeed         = save.masterSeed,
            visitCount         = save.visitCount,
            seqPhase           = save.seqPhase,
            shopUsed           = save.shopUsed,
            eventUsed          = save.eventUsed,
            heading            = save.heading,
            anchorToggle       = save.anchorToggle,
            currentRoomPoolKey = save.currentRoomPoolKey,
            currentRoomKind    = save.currentRoomKind,
            currentRoomMirror  = save.currentRoomMirror,
            currentRoomCleared = save.currentRoomCleared,   // 클리어 상태로 복원 → 몹 재스폰 X
            crucibleRollIndex  = save.crucibleRollIndex,    // 재련소 RNG 스트림 재개 위치
            cooldowns          = cooldowns,
        };
    }

    private void RestoreCovenantsFromSave(RunSaveData save)
    {
        if (string.IsNullOrEmpty(save.covenantsJson)) return;
        var w = JsonUtility.FromJson<CovenantListWrapper>(save.covenantsJson);
        if (w?.items != null && w.items.Count > 0)
            _run?.CovenantHandler?.RestoreSelections(w.items);
    }

    private void RestoreRuneBoardFromSave(RunSaveData save)
    {
        if (string.IsNullOrEmpty(save.runeCellsJson)) return;
        var w = JsonUtility.FromJson<Vector2IntListWrapper>(save.runeCellsJson);
        if (w?.items == null || w.items.Count == 0) return;

        // 점유 셀이 룬 시너지의 단일 진실원본. 레코드 기반 중복 적용을 제거한 뒤
        // 점유 재주입으로 스탯+메커닉을 한 번만 재계산한다.
        var stats = _run?.Player?.RuntimeStats;
        stats?.RestoreSynergies(null);                  // 시너지 스탯 초기화 (멱등)
        _run?.ClearAppliedSynergies();                  // 세션 시너지 이력 초기화
        MerlinRuneBridge.Instance?.ClearAppliedGrids(); // 브릿지 적용 가드 초기화

        // Shape 재구성(재편집 가능 상태) — 실패해도 점유 기반 복원으로 폴백(시너지 무영향)
        try
        {
            if (!string.IsNullOrEmpty(save.runePlacementsJson))
            {
                var pw = JsonUtility.FromJson<RunePlacementListWrapper>(save.runePlacementsJson);
                if (pw?.items != null && pw.items.Count > 0)
                    MerlinRuneBridge.Instance?.RestoreRunePlacements(pw.items);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[GameRunBootstrapper] 룬 Shape 재구성 실패(점유 폴백): {e.Message}");
        }

        // 점유 셀 기반 시너지 재계산 (권위) — Shape 재구성 여부와 무관하게 빌드 효과 보장
        MerlinRuneBridge.Instance?.RestoreRuneCells(w.items);
    }

    /// <summary>
    /// zone-layout 이어하기 진입. 저장된 존 인덱스의 방을 빌드하고,
    /// 출구 게이트를 즉시 활성화해 선택지가 남은 상태로 복원한다.
    /// </summary>
    private async UniTask ContinueZoneLayoutRunAsync(CancellationToken ct)
    {
        var chapter     = _run?.CurrentChapter ?? ChapterId.Chapter1;
        var serverEntry = Managers.ChapterData?.Get(chapter);
        var chapterSO   = chapterRegistry?.GetData(chapter);
        var zoneLayoutKey = serverEntry?.zone_layout_key ?? chapterSO?.zoneLayoutKey;
        var zoneSlotKey   = serverEntry?.zone_slot_key   ?? chapterSO?.zoneSlotKey;
        var zonePoolKey   = serverEntry?.zone_pool_key   ?? chapterSO?.zonePoolKey;
        if (string.IsNullOrEmpty(zoneLayoutKey))
        {
            Debug.LogError("[GameRunBootstrapper] ContinueZoneLayoutRunAsync: zone_layout_key 없음");
            return;
        }

        var layoutMgr = Managers.ZoneLayout;
        try
        {
            if (!string.IsNullOrEmpty(zoneSlotKey) && !string.IsNullOrEmpty(zonePoolKey))
                await Managers.ZoneLayout.LoadWithPoolAsync(zoneSlotKey, zonePoolKey, zoneLayoutKey);
            else
                await Managers.ZoneLayout.LoadAsync(zoneLayoutKey);
        }
        catch (System.Exception e) { Debug.LogWarning($"[GameRunBootstrapper] ZoneLayout 로드 예외: {e.Message}"); }

        var zones = layoutMgr.GetZones(zoneLayoutKey);
        if (zones == null || zones.Count == 0)
        {
            Debug.LogError($"[GameRunBootstrapper] ContinueZoneLayoutRunAsync: '{zoneLayoutKey}' 존 없음");
            return;
        }

        // 슬롯에서 저장된 존 인덱스 및 클리어 목록 복원 (로컬 세이브가 단독 권위)
        var rp   = RunProgressManager.Instance;
        var save = rp != null ? rp.LoadLocalRun(rp.ActiveSlotIndex) : null;
        int resumeZoneIndex = save?.currentZoneIndex ?? 0;

        // Zone 0 월드 중심 기준으로 ZoneProgression 초기화 (이어하기 시 Zone 0은 스폰하지 않음)
        var startZone   = zones.Find(z => z.zone_index == 0);
        var zone0Center = startZone != null ? CalcZoneWorldCenter(startZone) : Vector3.zero;
        _run?.InitZoneProgression(zoneLayoutKey, zone0Center, blockCellSize);

        // 이전 클리어 이력 복원 — GetNextZoneOptions가 이미 클리어된 존을 필터링할 수 있도록
        if (_run?.ZoneProgression != null && !string.IsNullOrEmpty(save?.clearedZoneIndicesJson))
        {
            var clearedWrapper = JsonUtility.FromJson<IntListWrapper>(save.clearedZoneIndicesJson);
            if (clearedWrapper?.items != null)
                _run.ZoneProgression.RestoreState(resumeZoneIndex, clearedWrapper.items);
        }

        // 저장된 존 스폰 (내부에서 CreateZoneExitGates → RegisterExitGate 호출)
        await SpawnZoneByIndexAsync(resumeZoneIndex, null, ct);

        // 스폰된 존 월드 중심 등록 (이후 인접 존 계산에 사용)
        var resumeZone = zones.Find(z => z.zone_index == resumeZoneIndex);
        if (resumeZone != null && _run?.ZoneProgression != null)
        {
            var resumeCenter = CalcZoneWorldCenter(resumeZone);
            _run.ZoneProgression.RegisterSpawnedZone(resumeZoneIndex, resumeCenter);
            // 플레이어 스폰 위치: 존 중앙의 spawn_local 오프셋
            if (!_pendingPlayerSpawnPos.HasValue)
                _pendingPlayerSpawnPos = resumeCenter + new Vector3(resumeZone.spawn_local_x, 0f, resumeZone.spawn_local_z);
        }

        UIRootBootstrapper.Instance?.BindHudToRun(_run);
        _run?.RequestHudMode(HUDIds.Mode.Combat);

        var player = await SpawnPlayerAsync(playerPrefabKey);
        if (player != null)
        {
            SetupEntrance(player);
            _run?.BindPlayer(player);
        }

        _run?.RequestHudMode(HUDIds.Mode.Combat);

        // 출구 게이트 즉시 활성화 — 방 클리어 상태이므로 선택지 표시
        _run?.ZoneProgression?.EnableExitGateForZone(resumeZoneIndex);

        Debug.Log($"[GameRunBootstrapper] zone-layout 이어하기 완료 — Zone {resumeZoneIndex} 복원, 출구 게이트 활성화");
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

        if (!run.IsRunning) return;

        await StartProcGenRunAsync();

        UIRootBootstrapper.Instance?.BindHudToRun(run);
        run.RequestHudMode(HUDIds.Mode.Combat);

        var player = await SpawnPlayerAsync(playerPrefabKey);
        if (player != null)
        {
            // 무기가 없으면 기본 무기 자동 장착
            if (player.WeaponManager != null && !player.WeaponManager.HasWeapon)
            {
                Debug.Log($"[GameRunBootstrapper] StartRunAsync: 기본 무기 장착 ({debugDefaultWeaponKey})");
                var handle = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<WeaponSO>(debugDefaultWeaponKey);
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

    /// <summary>Loadout 유물 우선, 없으면 에디터 직접 전투 테스트용 debugDefaultRelic 폴백.</summary>
    private RelicClassSO ResolveRelicForDirectSpawn()
        => AppBootstrapper.Instance?.Loadout?.Relic ?? debugDefaultRelic;

    private async UniTask<PlayerController> SpawnPlayerAsync(string prefabKey)
    {
        // 우선순위: Loadout(시작방 선택) → CharacterDataManager(PrepPanel) → Inspector 기본값(에디터 테스트용)
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

        // 선택된 유물 적용 (CombatGirl 단일 몸 + 유물). Loadout 유물 없으면 디버그 기본 유물(에디터 테스트).
        player.SetRelicAndApply(ResolveRelicForDirectSpawn());

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

    // ── Minimap ───────────────────────────────────────────────

    private void InitializeMinimapForRoom(GameObject mapGO, int gridW, int gridH)
    {
        var minimap = UIRootBootstrapper.Instance?.GetMinimapView();
        if (minimap == null) return;

        // 이전 방 스포너 구독 해제
        UnsubscribeMinimapSpawners(minimap);

        var roomCenter = mapGO != null ? mapGO.transform.position : Vector3.zero;
        var roomSize   = new Vector2(gridW * blockCellSize, gridH * blockCellSize);
        minimap.Initialize(roomCenter, roomSize);

        if (mapGO == null) return;

        foreach (var spawner in mapGO.GetComponentsInChildren<MonsterSpawner>(true))
        {
            var capturedMinimap = minimap;
            spawner.OnMonsterSpawned += monster => OnMinimapMonsterSpawned(capturedMinimap, monster);
            _minimapSpawnerSubs.Add(spawner);
        }
    }

    private void UnsubscribeMinimapSpawners(MinimapView minimap)
    {
        // 람다 기반 구독은 직접 해제 불가 — Initialize()의 ClearAllMarkers로 마커 정리,
        // 스포너 자체는 씬 언로드 시 파괴되므로 이벤트도 자동 해제됨.
        _minimapSpawnerSubs.Clear();
    }

    private static void OnMinimapMonsterSpawned(MinimapView minimap, MonsterBase monster)
    {
        if (minimap == null || monster == null) return;

        var type = monster is IBoss ? MinimapMarkerType.Boss : MinimapMarkerType.Monster;

        var marker = minimap.AddMarker(monster.transform, type);
        if (marker == null) return;

        monster.OnDied += died => minimap.RemoveMarker(died != null ? died.transform : null);
    }
}
