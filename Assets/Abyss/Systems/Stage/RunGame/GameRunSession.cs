//============================================================
// GameRunManager.cs (Improved + HUD Mode Safe + Late Bind Friendly)
//============================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed class GameRunSession
{
    private const string ROOMS_KEY = "STAGEDATA_ROOMS";
    private const string STAGE_KEY = "STAGEDATA_STAGE";

    public enum RunPhase
    {
        NotRunning = 0,
        Starting   = 1,
        Running    = 2,
        Ending     = 3
    }

    public enum RunState
    {
        None,
        Map,          // 맵 선택 화면
        CombatRoom,   // 전투방
        ItemRoom,     // 아이템방
        RewardRoom,   // 재화방
        SpecialRoom,  // 특별방
        BossRoom,     // 보스방
        Standby,      // 방 클리어 후 대기
        GridSynergy,  // 그리드 시너지 선택
        ChapterClear, // 챕터 클리어 화면
        RunClear,     // 런 클리어 (최종 보스 격파)
        RunEnd,       // 런 종료 (사망)
    }

    public RunPhase Phase { get; private set; } = RunPhase.NotRunning;
    public bool IsRunning => Phase == RunPhase.Running;

    public ChapterId CurrentChapter { get; private set; }

    /// <summary>현재 챕터의 블록 테마. ChapterDataSO.theme에서 해석된 값. 빈 문자열이면 방별 theme 또는 Default 팔레트 폴백.</summary>
    public string ActiveTheme { get; private set; } = string.Empty;

    /// <summary>현재 챕터의 필드 구조물 프리팹 Addressables 키.</summary>
    public string ActiveFieldPrefabKey { get; private set; } = string.Empty;

    private ChapterRegistry _chapterRegistry;

    /// <summary>챕터 레지스트리 주입. 챕터 변경 시 ActiveTheme 자동 해석에 사용.</summary>
    public void BindChapterRegistry(ChapterRegistry registry)
    {
        _chapterRegistry = registry;
        ResolveActiveTheme();
    }

    private void ResolveActiveTheme()
    {
        var data = _chapterRegistry != null ? _chapterRegistry.Get(CurrentChapter) : null;
        ActiveTheme = data != null && !string.IsNullOrEmpty(data.theme) ? data.theme : string.Empty;
        ActiveFieldPrefabKey = data != null ? data.fieldPrefabKey ?? string.Empty : string.Empty;
    }

    public RoomManager RoomManager { get; private set; }
    public StagePointManager StagePointManager { get; private set; }

    // 챕터 단위로 생성된 맵 그래프 캐시 — StageMap 씬 재진입 시 Generator 재실행 없이 UI를 복원해
    // 노드 연결·방문 기록이 유지되도록 한다. AdvanceToNextChapter 시 무효화.
    public StageMapGraph CachedStageGraph { get; private set; }

    public void CacheStageGraph(StageMapGraph graph) => CachedStageGraph = graph;
    public void InvalidateStageGraph() => CachedStageGraph = null;

    public PlayerController Player { get; private set; }
    private PlayerController _playerStateSource;

    public PlayerRunState PlayerState { get; private set; }
    public RunItemInventory ItemInventory { get; private set; } = new RunItemInventory();
    public RunDelta RunDelta { get; private set; } = new RunDelta();
    public RoomBuffHandler BuffHandler { get; private set; } = new RoomBuffHandler();
    public ItemEffectManager EffectManager { get; private set; } = new ItemEffectManager();

    // ── 시너지 이력 (씬 전환에도 생존) ──
    private readonly List<SynergyRecord> _appliedSynergies = new();
    public IReadOnlyList<SynergyRecord> AppliedSynergies => _appliedSynergies;

    // 씬 전환 시 무기 슬롯 복원용
    public WeaponData[] SavedWeaponSlots { get; private set; }
    public int SavedCurrentSlotIndex { get; private set; } = -1;

    public void SaveWeaponSlots(WeaponData[] slots, int currentIndex)
    {
        SavedWeaponSlots = slots;
        SavedCurrentSlotIndex = currentIndex;
    }

    // --------------------
    // Events
    // --------------------
    public event Action OnRunStarted;
    public event Action<EndRunResult> OnRunEnded;

    public event Action<PlayerRunState> OnPlayerStateReady;
    public event Action<PlayerController> OnPlayerBound;
    public event Action<string> OnMapSpawnRequested;

    // HUD mode
    public event Action<HUDIds.Mode> OnHudModeChanged;

    // Run state
    public RunState CurrentRunState { get; private set; } = RunState.None;
    public event Action<RunState> OnRunStateChanged;

    public HUDIds.Mode CurrentHudMode { get; private set; } = HUDIds.Mode.None;
    private bool _hudModeSet = false;

    private Dictionary<int, StageData> _stageDataCache;

    // =========================================================
    // Synergy Record
    // =========================================================

    /// <summary>시너지 효과 기록 추가.</summary>
    public void RecordSynergy(SynergyRecord record)
    {
        if (record == null) return;
        _appliedSynergies.Add(record);
    }

    /// <summary>특정 그리드의 시너지 철회 (아이템 제거로 그리드 미완성 시).</summary>
    public void RemoveSynergiesByGrid(string gridId)
    {
        if (string.IsNullOrEmpty(gridId)) return;
        _appliedSynergies.RemoveAll(r => r.gridId == gridId);
    }

    // =========================================================
    // Run Lifecycle
    // =========================================================
    public async UniTask StartNewRunAsync(ChapterId chapter, Func<string, UniTask<TextAsset>> loader)
    {
        if (Phase != RunPhase.NotRunning)
        {
            Debug.LogWarning($"[GameRun] StartNewRunAsync ignored: phase={Phase}");
            return;
        }

        Phase = RunPhase.Starting;
        CurrentChapter = chapter;
        ResolveActiveTheme();

        // HUD state reset for a new run
        CurrentHudMode = HUDIds.Mode.None;
        _hudModeSet = false;

        try
        {
            await LoadStageDataAsync(STAGE_KEY, loader);

            RoomManager = new RoomManager();
            await RoomManager.InitializeAsync(ROOMS_KEY, loader);

            if (!RoomManager.IsInitialized)
            {
                Debug.LogError($"[GameRun] RoomManager init failed. Address='{ROOMS_KEY}'");
                ResetToNotRunning();
                return;
            }

            StagePointManager = new StagePointManager();
            StagePointManager.Initialize(chapter, RoomManager);

            PlayerState = CreateInitialPlayerStateFromSession();
            RunDelta = new RunDelta();

            // PlayerState ready (HUD may already exist)
            OnPlayerStateReady?.Invoke(PlayerState);

            Phase = RunPhase.Running;

            // 런 시작 = 맵 상태 (HUD Explore 포함)
            ChangeRunState(RunState.Map);

            OnRunStarted?.Invoke();
            Debug.Log($"[GameRun] Started. chapter={CurrentChapter}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[GameRun] StartNewRunAsync failed: {e}");
            ResetToNotRunning();
        }
    }

    public EndRunResult EndRun(bool isCleared, string reason = null)
    {
        if (Phase != RunPhase.Running)
        {
            Debug.LogWarning($"[GameRun] EndRun ignored: phase={Phase}");
            return default;
        }

        Phase = RunPhase.Ending;

        // 종료 상태 발행 (OnRunEnded 전에 구독자가 반응할 수 있도록)
        var endState = isCleared ? RunState.RunClear : RunState.RunEnd;
        CurrentRunState = endState;
        OnRunStateChanged?.Invoke(endState);

        var result = new EndRunResult(
            isCleared: isCleared,
            chapter: CurrentChapter,
            gainedGold: RunDelta.GainedGold,
            gainedItems: RunDelta.GainedItems.ToArray(),
            reason: reason
        );

        OnRunEnded?.Invoke(result);

        try { PlayerState?.Deactivate(); }
        catch (Exception e) { Debug.LogWarning($"[GameRun] PlayerState.Deactivate() error: {e.Message}"); }

        // BlockSynergyBridge 적용 이력 초기화 (DDOL이므로 수동 정리)
        BlockSynergyBridge.Instance?.ClearAppliedGrids();

        // Optional: end => none (keeps HUD consistent if it remains alive)
        RequestHudMode(HUDIds.Mode.None);

        ClearRunReferences();
        Phase = RunPhase.NotRunning;

        Debug.Log($"[GameRun] Ended. cleared={isCleared}, reason={reason}");
        return result;
    }

    private void ResetToNotRunning()
    {
        ClearRunReferences();
        Phase = RunPhase.NotRunning;
    }

    private void ClearRunReferences()
    {
        UnsubscribePlayerStateSource();
        ItemInventory.OnInventoryChanged -= RebuildItemEffects;
        EffectManager.Cleanup();
        BuffHandler.OnBuffsChanged -= RefreshPlayerRoomBuffs;
        BuffHandler.ClearAll();
        RoomManager = null;
        StagePointManager = null;
        CachedStageGraph = null;
        Player = null;
        PlayerState = null;
        CurrentRunState = RunState.None;
        SavedWeaponSlots = null;
        SavedCurrentSlotIndex = -1;
        _appliedSynergies.Clear();
        ActiveTheme = string.Empty;
        ActiveFieldPrefabKey = string.Empty;
    }

    /// <summary>
    /// 에디터 직접 실행 시 Phase를 Running으로 강제 설정.
    /// StartNewRunAsync를 거치지 않고 테스트할 때 사용.
    /// </summary>
    public void ForceRunningForTest()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Phase == RunPhase.NotRunning)
        {
            Phase = RunPhase.Running;
            Debug.Log("[GameRun] ForceRunningForTest: Phase → Running");
        }
#endif
    }

    // =========================================================
    // Run State Machine
    // =========================================================
    private static RunState CategoryToRunState(RoomCategory cat) => cat switch
    {
        RoomCategory.Battle  => RunState.CombatRoom,
        RoomCategory.Elite   => RunState.CombatRoom,
        RoomCategory.Boss    => RunState.BossRoom,
        RoomCategory.Event   => RunState.SpecialRoom,
        RoomCategory.Shop    => RunState.ItemRoom,
        RoomCategory.Start   => RunState.Map,
        _                    => RunState.Standby,
    };

    private static HUDIds.Mode RunStateToHudMode(RunState state) => state switch
    {
        RunState.Map         => HUDIds.Mode.None,    // StageMap 씬이 전담, GameScene HUD 없음
        RunState.CombatRoom  => HUDIds.Mode.Combat,
        RunState.ItemRoom    => HUDIds.Mode.Combat,
        RunState.RewardRoom  => HUDIds.Mode.Combat,
        RunState.SpecialRoom => HUDIds.Mode.Combat,
        RunState.BossRoom    => HUDIds.Mode.Boss,
        RunState.Standby     => HUDIds.Mode.Combat,
        RunState.GridSynergy => HUDIds.Mode.Puzzle,
        _                    => HUDIds.Mode.None,
    };

    private void ChangeRunState(RunState newState)
    {
        if (CurrentRunState == newState) return;
        var prev = CurrentRunState;
        CurrentRunState = newState;
        Debug.Log($"[RunState] {prev} → {newState}  |  HudMode: {RunStateToHudMode(newState)}");
        RequestHudMode(RunStateToHudMode(newState));
        OnRunStateChanged?.Invoke(newState);
    }

    public void EnterMap()
    {
        if (!IsRunning) return;
        ChangeRunState(RunState.Map);
    }

    public void EnterRoom(RunState roomState)
    {
        if (!IsRunning) return;

        // 아이템 효과: 방/보스방 진입 hook
        if (roomState == RunState.BossRoom)
            EffectManager?.OnBossEnter();
        else if (roomState == RunState.CombatRoom || roomState == RunState.ItemRoom ||
                 roomState == RunState.RewardRoom  || roomState == RunState.SpecialRoom)
            EffectManager?.OnRoomEnter();

        ChangeRunState(roomState);
    }

    public void EnterStandby()
    {
        if (!IsRunning) return;

        // 아이템 효과: 방 클리어 hook
        EffectManager?.OnRoomClear();

        ChangeRunState(RunState.Standby);
    }

    public void EnterGridSynergy()
    {
        if (!IsRunning) return;
        ChangeRunState(RunState.GridSynergy);
    }

    public void ExitGridSynergy()
    {
        if (!IsRunning) return;
        ChangeRunState(RunState.Standby);
    }

    public void EnterChapterClear()
    {
        if (!IsRunning) return;

        // 아이템 효과: 보스 클리어 hook
        EffectManager?.OnBossClear();

        ChangeRunState(RunState.ChapterClear);
    }

    /// <summary>다음 챕터로 진행. 마지막 챕터면 false 반환.</summary>
    public bool AdvanceToNextChapter()
    {
        if (!IsRunning) return false;

        var next = CurrentChapter + 1;
        if (next > ChapterId.Chapter5) return false;

        CurrentChapter = next;
        ResolveActiveTheme();

        // StagePointManager 재초기화 (새 챕터 노드 배치)
        StagePointManager.Initialize(next, RoomManager);

        // 이전 챕터의 그래프는 폐기 — 다음 StageMap 진입 시 새로 생성
        InvalidateStageGraph();

        ChangeRunState(RunState.Map);
        return true;
    }

    // =========================================================
    // HUD Mode API
    // =========================================================
    public void RequestHudMode(HUDIds.Mode mode)
    {
        // (Optional) ignore repeated requests
        if (_hudModeSet && CurrentHudMode == mode)
            return;

        CurrentHudMode = mode;
        _hudModeSet = true;

        // listener could be 0 => null is normal
        OnHudModeChanged?.Invoke(mode);
    }

    public bool TryGetHudMode(out HUDIds.Mode mode)
    {
        mode = CurrentHudMode;
        return _hudModeSet;
    }

    // Convenience wrappers
    public void NotifyCombatStarted()   => EnterRoom(RunState.CombatRoom);
    public void NotifyCombatEnded()     => EnterStandby();
    public void NotifyBossStarted()     => EnterRoom(RunState.BossRoom);
    public void NotifyCutsceneStarted() => RequestHudMode(HUDIds.Mode.Cutscene);
    public void NotifyCutsceneEnded()   => EnterMap();

    // =========================================================
    // Scene Bind (Bootstrapper injects)
    // =========================================================
    public void BindPlayer(PlayerController player)
    {
        UnsubscribePlayerStateSource();
        Player = player;
        if (Player == null) Debug.LogWarning("[GameRun] BindPlayer: player is null");

        SubscribePlayerStateSource(Player);

        // 인벤토리 ↔ 스탯 연동 (추가/제거 시 자동 재계산)
        if (Player?.RuntimeStats != null)
        {
            ItemInventory.OnInventoryChanged -= RefreshPlayerItemStats;
            ItemInventory.OnInventoryChanged += RefreshPlayerItemStats;

            // 방 버프 ↔ 스탯 연동
            BuffHandler.OnBuffsChanged -= RefreshPlayerRoomBuffs;
            BuffHandler.OnBuffsChanged += RefreshPlayerRoomBuffs;

            // ItemEffectManager 초기화
            ItemInventory.OnInventoryChanged -= RebuildItemEffects;
            ItemInventory.OnInventoryChanged += RebuildItemEffects;
            EffectManager.Initialize(Player, this, ItemInventory);

            // 현재 인벤토리 아이템 보너스 즉시 적용 (씬 전환 후 복원)
            RefreshPlayerItemStats();

            // 방 버프 즉시 적용
            RefreshPlayerRoomBuffs();

            // 시너지 이력 복원 (씬 전환 후 복원)
            if (_appliedSynergies.Count > 0)
                Player.RuntimeStats.RestoreSynergies(_appliedSynergies);
        }

        OnPlayerBound?.Invoke(Player);
    }

    private void RefreshPlayerItemStats()
    {
        Player?.RuntimeStats?.RefreshItemBonuses(ItemInventory);
    }

    private void RefreshPlayerRoomBuffs()
    {
        Player?.RuntimeStats?.RefreshRoomBuffs(BuffHandler);
    }

    private void RebuildItemEffects()
    {
        EffectManager.RefreshContext(Player, this);
        EffectManager.Rebuild();
    }

    public bool TryGetPlayerState(out PlayerRunState state)
    {
        state = PlayerState;
        return (Phase == RunPhase.Running || Phase == RunPhase.Starting) && state != null;
    }

    // =========================================================
    // StagePoint UI Bind
    // =========================================================
    public void RegisterPoints(IEnumerable<StagePointUI> points)
    {
        if (!IsRunning || StagePointManager == null || RoomManager == null)
        {
            Debug.LogWarning("[GameRun] RegisterPoints ignored: not ready");
            return;
        }

        if (points == null) return;
        foreach (var ui in points) ui.Register(StagePointManager, RoomManager);
    }

    public void ResolveAllPointsAndSetStart()
    {
        if (!IsRunning || StagePointManager == null)
        {
            Debug.LogWarning("[GameRun] ResolveAllPointsAndSetStart ignored: not ready");
            return;
        }

        StagePointManager.ResolveAll();
        StagePointManager.SetStartAsCurrent();
    }

    // =========================================================
    // Map Spawn / Movement
    // =========================================================
    public void RequestSpawnCurrentPointMap()
    {
        if (!IsRunning || StagePointManager == null || RoomManager == null)
        {
            Debug.LogWarning("[GameRun] RequestSpawnCurrentPointMap ignored: not ready");
            return;
        }

        if (StagePointManager.CurrentPointId < 0)
        {
            Debug.LogWarning("[GameRun] CurrentPointId < 0. Start를 먼저 세팅하세요.");
            return;
        }

        var ctx = StagePointManager.GetContext(StagePointManager.CurrentPointId);
        if (ctx == null)
        {
            Debug.LogError($"[GameRun] Context not found. pointId={StagePointManager.CurrentPointId}");
            return;
        }

        StagePointManager.Resolve(ctx);

        var roomId = ctx.ResolvedRoomId;
        if (string.IsNullOrEmpty(roomId))
        {
            Debug.LogError($"[GameRun] ResolvedRoomId is empty. pointId={ctx.PointId}");
            return;
        }

        var room = RoomManager.GetById(roomId);
        if (room == null)
        {
            Debug.LogError($"[GameRun] Room not found. roomId={roomId}");
            return;
        }

        if (string.IsNullOrEmpty(room.prefab))
        {
            Debug.LogError($"[GameRun] Room prefab key is empty. roomId={roomId}");
            return;
        }

        Debug.Log($"[SpawnMap] pointId={ctx.PointId}  room={room.name}  category={room.category}  prefab={room.prefab}");

        // 방 카테고리 → RunState 자동 전환
        var category = RoomCategoryUtil.Parse(room.category);
        ChangeRunState(CategoryToRunState(category));

        OnMapSpawnRequested?.Invoke(room.prefab);
    }

    public void RequestMoveTo(int targetPointId)
    {
        if (!IsRunning || StagePointManager == null)
        {
            Debug.LogWarning("[GameRun] RequestMoveTo ignored: not running");
            return;
        }

        if (!StagePointManager.CanMove(targetPointId)) return;
        if (!StagePointManager.TryMoveTo(targetPointId)) return;

        RequestSpawnCurrentPointMap();
    }

    /// <summary>
    /// StageMap 씬에서 호출. 맵 스폰 없이 포인트 이동만 기록합니다.
    /// 실제 맵 스폰은 GameScene 진입 후 GameRunBootstrapper가 담당합니다.
    /// </summary>
    public bool SelectPoint(int targetPointId)
    {
        if (!IsRunning || StagePointManager == null)
        {
            Debug.LogWarning("[GameRun] SelectPoint ignored: not running");
            return false;
        }

        if (!StagePointManager.CanMove(targetPointId)) return false;
        return StagePointManager.TryMoveTo(targetPointId);
    }

    // =========================================================
    // RunDelta APIs
    // =========================================================
    public void AddGold(int amount)
    {
        if (!IsRunning) return;
        if (amount <= 0) return;

        RunDelta.GainedGold += amount;
        PlayerState?.AddTempGold(amount);
    }

    public void AddItem(ItemId itemId, int count)
    {
        if (!IsRunning) return;
        if (count <= 0) return;

        RunDelta.GainedItems.Add(new ItemStack(itemId, count));
    }

    // =========================================================
    // Optional: Stage Data
    // =========================================================
    private async UniTask LoadStageDataAsync(string key, Func<string, UniTask<TextAsset>> loader)
    {
        TextAsset textAsset = null;

        try { textAsset = await loader(key); }
        catch (Exception e)
        {
            Debug.LogWarning($"[GameRun] Stage data load failed (optional). key={key}, err={e.Message}");
            return;
        }

        if (textAsset == null)
        {
            Debug.LogWarning($"[GameRun] Stage data TextAsset is null (optional). key={key}");
            return;
        }

        var root = JsonUtility.FromJson<StageDataRoot>(textAsset.text);
        if (root?.stages == null)
        {
            Debug.LogWarning("[GameRun] StageDataRoot.stages is null");
            return;
        }

        _stageDataCache = root.stages.ToDictionary(s => s.stageId, s => s);
        Debug.Log($"[GameRun] StageData Loaded: {_stageDataCache.Count}");
    }

    private PlayerRunState CreateInitialPlayerStateFromSession()
    {
        var charData = Managers.CharacterData?.M_CharacterData;
        int maxHp = (charData != null && charData.maxHealth > 0) ? charData.maxHealth : 100;

        if (charData == null)
            Debug.LogWarning("[GameRun] CharacterData not set — PlayerRunState uses default maxHp=100.");

        // 디버그/테스트용 시작 골드 — 출시 전 정책. 추후 0 또는 메타-프로그레션 값으로 교체.
        const int DebugStartGold = 99999;
        return new PlayerRunState(maxHp, DebugStartGold);
    }

    private void SubscribePlayerStateSource(PlayerController player)
    {
        if (player?.RuntimeStats == null || PlayerState == null)
            return;

        _playerStateSource = player;
        _playerStateSource.RuntimeStats.OnChanged += SyncPlayerStateFromRuntimeStats;
        SyncPlayerStateFromRuntimeStats();
    }

    private void UnsubscribePlayerStateSource()
    {
        if (_playerStateSource?.RuntimeStats != null)
            _playerStateSource.RuntimeStats.OnChanged -= SyncPlayerStateFromRuntimeStats;

        _playerStateSource = null;
    }

    private void SyncPlayerStateFromRuntimeStats()
    {
        if (_playerStateSource?.RuntimeStats == null || PlayerState == null || !PlayerState.IsActive)
            return;

        var stats = _playerStateSource.RuntimeStats;
        PlayerState.SetMaxHp(stats.MaxHp);
        PlayerState.SetHp(stats.Hp);
    }
}
