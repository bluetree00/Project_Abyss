//============================================================
// GameRunManager.cs (Improved + HUD Mode Safe + Late Bind Friendly)
//============================================================
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed class GameRunSession
{
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

    /// <summary>신규 런 시작 시 지급하는 강화재료(EnhanceMaterial) 초기량. 재련소(#1 소비처)가
    /// 이벤트방(유일 생산처)보다 앞 순번일 때 첫 재련소에서 재료=0이 되는 갭의 안전장치.
    /// EnhanceTable +0→+1 비용(1) 기준 약 5회 시도분. 이어하기 복원 경로에는 지급하지 않는다.</summary>
    private const int NewRunStartingEnhanceMaterial = 5;

    public RunPhase Phase { get; private set; } = RunPhase.NotRunning;
    public bool IsRunning => Phase == RunPhase.Running;

    public ChapterId CurrentChapter { get; private set; }

    /// <summary>현재 챕터의 블록 테마. ChapterDataSO.theme에서 해석된 값. 빈 문자열이면 방별 theme 또는 Default 팔레트 폴백.</summary>
    public string ActiveTheme { get; private set; } = string.Empty;

    /// <summary>현재 챕터의 필드 구조물 프리팹 Addressables 키.</summary>
    public string ActiveFieldPrefabKey { get; private set; } = string.Empty;

    /// <summary>현재 챕터 ChapterDataSO의 보스 스폰 테이블. 레지스트리 미주입/미설정 시 null(BossSpawner가 직렬화 폴백 사용).</summary>
    public MonsterSpawnTableSO CurrentBossSpawnTable =>
        _chapterRegistry != null ? _chapterRegistry.GetData(CurrentChapter)?.bossSpawnTable : null;

    /// <summary>현재 챕터의 몬스터 스탯 배율(HP·공격력). ChapterDataSO.difficultyScale. 미주입/미설정 시 1.</summary>
    public float CurrentDifficultyScale =>
        _chapterRegistry != null ? (_chapterRegistry.GetData(CurrentChapter)?.difficultyScale ?? 1f) : 1f;

    /// <summary>현재 챕터의 몬스터 수량 배율. ChapterDataSO.monsterCountScale. 미주입/미설정 시 1.</summary>
    public float CurrentMonsterCountScale =>
        _chapterRegistry != null ? (_chapterRegistry.GetData(CurrentChapter)?.monsterCountScale ?? 1f) : 1f;

    private ChapterRegistry _chapterRegistry;

    /// <summary>챕터 레지스트리 주입. 챕터 변경 시 ActiveTheme 자동 해석에 사용.</summary>
    public void BindChapterRegistry(ChapterRegistry registry)
    {
        _chapterRegistry = registry;
        ResolveActiveTheme();
    }

    private void ResolveActiveTheme()
    {
        var data = _chapterRegistry != null ? _chapterRegistry.GetData(CurrentChapter) : null;
        ActiveTheme = data != null && !string.IsNullOrEmpty(data.theme) ? data.theme : string.Empty;
        ActiveFieldPrefabKey = data != null ? data.fieldPrefabKey ?? string.Empty : string.Empty;
    }

    public PlayerController Player { get; private set; }
    private PlayerController _playerStateSource;

    public PlayerRunState PlayerState { get; private set; }
    public RunItemInventory ItemInventory { get; private set; } = new RunItemInventory();
    /// <summary>런 지속 연료(강화재료·원석). 이벤트방 생산·#1/#2 소비. 미소비분은 런 종료 시 abyssEssence로 환산.</summary>
    public RunFuelBank FuelBank { get; private set; } = new RunFuelBank();
    public RunDelta RunDelta { get; private set; } = new RunDelta();
    public RoomBuffHandler BuffHandler { get; private set; } = new RoomBuffHandler();
    public ItemEffectManager EffectManager { get; private set; } = new ItemEffectManager();
    public CovenantHandler CovenantHandler { get; private set; } = new CovenantHandler();

    private CovenantDataTableSO _covenantDataTable;
    private bool _covenantInitialized;

    /// <summary>런 시작 전(Awake 등)에 DataTable SO를 주입해 BindPlayer 시 자동으로 CovenantHandler를 초기화한다.</summary>
    public void SetCovenantDataTable(CovenantDataTableSO dataTable) => _covenantDataTable = dataTable;

    /// <summary>존 단위 진행 서비스. startWithZoneLayout 모드에서만 초기화된다.</summary>
    public ZoneProgressionService ZoneProgression { get; private set; }

    public void InitZoneProgression(string layoutKey, Vector3 zone0WorldCenter, float blockCellSize)
    {
        ZoneProgression = new ZoneProgressionService(layoutKey, zone0WorldCenter, blockCellSize);
        Debug.Log($"[GameRunSession] ZoneProgressionService 초기화 완료 ({layoutKey})");
    }

    // ── 시너지 이력 (씬 전환에도 생존) ──
    private readonly List<SynergyRecord> _appliedSynergies = new();
    public IReadOnlyList<SynergyRecord> AppliedSynergies => _appliedSynergies;

    // ── 방 클리어 이력 ──
    private readonly List<RoomClearRecord> _roomClearRecords = new();
    public IReadOnlyList<RoomClearRecord> RoomClearRecords => _roomClearRecords;

    // 방 진입 시 스냅샷 (클리어 시 방 내 획득분 계산에 사용)
    private int _snapGold;
    private int _snapItemCount;
    private int _snapSynergyCount;

    /// <summary>
    /// 현재 방에서 재련소가 소비한 결정적 롤 수.
    /// 방 안에서 저장(S3)을 허용하면 재접속 시 RNG 스트림이 처음으로 리셋돼
    /// "같은 롤을 다시 굴리는" save-scum이 뚫린다 → 이 값만큼 스트림을 진행시켜 막는다.
    /// 새 방 진입 시 0으로 리셋(방마다 시드가 다르므로).
    /// </summary>
    public int CrucibleRollIndex { get; set; }

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
    /// <summary>방 클리어 시 발행. isBossRoom=true면 챕터 보스 처치.</summary>
    public event Action<bool> OnRoomCleared;

    /// <summary>보스방 클리어 시점(보상/전환 전)에 발행. Vector3=보스/방 중심. 챕터 게이트 스폰 트리거.
    /// RoomWaveController가 NotifyBossRoomCleared로 발행 — 보스/몬스터 코드는 건드리지 않는다.</summary>
    public event Action<Vector3> OnBossRoomCleared;

    /// <summary>RoomWaveController에서 보스방 클리어 시 호출. OnBossRoomCleared 구독자(챕터 게이트)에게 알린다.</summary>
    public void NotifyBossRoomCleared(Vector3 center) => OnBossRoomCleared?.Invoke(center);

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

    /// <summary>시너지 이력 전체 초기화. 이어하기 시 룬 보드 점유 기반 재계산 전에 호출(중복 적용 방지).</summary>
    public void ClearAppliedSynergies() => _appliedSynergies.Clear();

    // =========================================================
    // Room Clear Recording
    // =========================================================

    /// <summary>방 진입 시 호출. 이 방에서 얻은 양을 계산하기 위한 스냅샷.</summary>
    private void SnapshotRoomEntry()
    {
        _snapGold         = PlayerState?.TempGold ?? 0;
        _snapItemCount    = ItemInventory.PlacedCount + ItemInventory.StagingCount;
        _snapSynergyCount = _appliedSynergies.Count;
    }

    /// <summary>방 클리어 시 호출. 현재 상태와 스냅샷의 차이로 방 내 획득 정보를 기록한다.</summary>
    private void RecordRoomClear(int pointId, RunState clearedState)
    {
        if (!IsRunning) return;

        int goldAfter  = PlayerState?.TempGold ?? 0;
        int itemCount  = ItemInventory.PlacedCount + ItemInventory.StagingCount;
        int synCount   = _appliedSynergies.Count;

        _roomClearRecords.Add(new RoomClearRecord
        {
            pointId              = pointId,
            chapter              = (int)CurrentChapter,
            runState             = clearedState.ToString(),
            hpAfter              = PlayerState?.Hp    ?? 0,
            maxHp                = PlayerState?.MaxHp ?? 0,
            goldAfter            = goldAfter,
            goldGainedInRoom     = goldAfter - _snapGold,
            itemsGainedCount     = itemCount - _snapItemCount,
            totalItemCount       = itemCount,
            synergiesGainedCount = synCount  - _snapSynergyCount,
            totalSynergyCount    = synCount,
            clearedAt            = DateTime.UtcNow.ToString("o"),
        });
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
            await UniTask.CompletedTask;

            PlayerState = CreateInitialPlayerStateFromSession();
            RunDelta = new RunDelta();

            // 신규 런 강화재료 시작 지급 — 재련소(첫 소비처)가 이벤트방(첫 생산처)보다 앞 순번일 수 있어
            // 첫 재련소에서 강화재료=0이 되는 갭을 막는 안전장치. 이어하기(RestoreFromSaveAsync)는 이 경로를
            // 거치지 않으므로 이중지급 없음. 신규 런 = 새 세션(FuelBank 잔량 0)이라 정확히 초기량만 지급된다.
            FuelBank.Add(FuelKind.EnhanceMaterial, NewRunStartingEnhanceMaterial);

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

    // =========================================================
    // Restore (이어하기)
    // =========================================================

    /// <summary>
    /// 저장 슬롯 데이터로 세션을 완전 복원한다.
    /// StartNewRunAsync와 달리 랜덤 맵 생성 없이 graphJson에서 그래프를 재구성하고,
    /// HP/골드/아이템/시너지를 저장 값으로 채운다.
    /// </summary>
    public async UniTask RestoreFromSaveAsync(RunSaveData save, Func<string, UniTask<TextAsset>> loader)
    {
        if (Phase != RunPhase.NotRunning)
        {
            Debug.LogWarning($"[GameRun] RestoreFromSaveAsync ignored: phase={Phase}");
            return;
        }

        Phase = RunPhase.Starting;
        CurrentChapter = (ChapterId)save.chapter;
        ResolveActiveTheme();
        CurrentHudMode = HUDIds.Mode.None;
        _hudModeSet = false;

        try
        {
            await UniTask.CompletedTask;

            PlayerState = new PlayerRunState(save.maxHp, save.runGold);
            PlayerState.SetHp(save.currentHp);

            RunDelta = new RunDelta();

            if (!string.IsNullOrEmpty(save.itemsJson))
            {
                var itemWrapper = JsonUtility.FromJson<ItemListWrapper>(save.itemsJson);
                if (itemWrapper?.items != null)
                    ItemInventory.RestorePlacedItems(itemWrapper.items);
            }

            if (!string.IsNullOrEmpty(save.synergiesJson))
            {
                var synWrapper = JsonUtility.FromJson<SynergyListWrapper>(save.synergiesJson);
                if (synWrapper?.items != null)
                {
                    foreach (var record in synWrapper.items)
                        if (record != null) _appliedSynergies.Add(record);
                }
            }

            if (!string.IsNullOrEmpty(save.roomLogsJson))
            {
                var logWrapper = JsonUtility.FromJson<RoomClearLogWrapper>(save.roomLogsJson);
                if (logWrapper?.records != null)
                    _roomClearRecords.AddRange(logWrapper.records);
            }

            // 보관함(미배치) 아이템 복원
            if (!string.IsNullOrEmpty(save.stagingItemsJson))
            {
                var stagingWrapper = JsonUtility.FromJson<ItemListWrapper>(save.stagingItemsJson);
                if (stagingWrapper?.items != null)
                    ItemInventory.RestoreStagingItems(stagingWrapper.items);
            }

            // 런 중 적립 정수 복원 (런 종료 시 메타 반영분)
            RunDelta.GainedEssence = save.runEssence;
            FuelBank.RestoreRaw(save.fuelEnhanceMaterial, save.fuelRuneOre);   // 연료 은행 복원

            Phase = RunPhase.Running;
            ChangeRunState(RunState.Map);

            OnPlayerStateReady?.Invoke(PlayerState);
            OnRunStarted?.Invoke();

            Debug.Log($"[GameRun] Restored. chapter={CurrentChapter}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[GameRun] RestoreFromSaveAsync failed: {e}");
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

        // 미소비 연료(강화재료·원석) → abyssEssence(메타) 환산 (로스 0). Phase=Ending이라 AddEssence 가드 우회, RunDelta 직접 가산.
        int fuelConverted = FuelBank.TotalRemaining();
        if (fuelConverted > 0) RunDelta.GainedEssence += fuelConverted;

        // 종료 상태 발행 (OnRunEnded 전에 구독자가 반응할 수 있도록)
        var endState = isCleared ? RunState.RunClear : RunState.RunEnd;
        CurrentRunState = endState;
        OnRunStateChanged?.Invoke(endState);

        var result = new EndRunResult(
            isCleared:     isCleared,
            chapter:       CurrentChapter,
            gainedGold:    RunDelta.GainedGold,
            gainedEssence: RunDelta.GainedEssence,
            gainedItems:   RunDelta.GainedItems.ToArray(),
            reason:        reason
        );

        OnRunEnded?.Invoke(result);

        try { PlayerState?.Deactivate(); }
        catch (Exception e) { Debug.LogWarning($"[GameRun] PlayerState.Deactivate() error: {e.Message}"); }

        // MerlinRuneBridge 적용 이력 초기화 (DDOL이므로 수동 정리)
        MerlinRuneBridge.Instance?.ClearAppliedGrids();

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
        ItemInventory.OnPlacedChanged -= RebuildItemEffects;
        EffectManager.Cleanup();
        BuffHandler.OnBuffsChanged -= RefreshPlayerRoomBuffs;
        BuffHandler.ClearAll();
        CovenantHandler.Cleanup();
        _covenantInitialized = false;
        Player = null;
        PlayerState = null;
        CurrentRunState = RunState.None;
        SavedWeaponSlots = null;
        SavedCurrentSlotIndex = -1;
        _appliedSynergies.Clear();
        _roomClearRecords.Clear();
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
        RunState.GridSynergy => HUDIds.Mode.Combat,
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

        SnapshotRoomEntry();

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

        RecordRoomClear(-1, CurrentRunState);

        // 아이템 효과: 방 클리어 hook
        EffectManager?.OnRoomClear();
        OnRoomCleared?.Invoke(false);

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
        OnRoomCleared?.Invoke(true);

        ChangeRunState(RunState.ChapterClear);
    }

    /// <summary>설정상 진행 가능한 마지막 챕터. ChapterRegistry._finalChapter(없으면 Chapter4).</summary>
    private ChapterId FinalChapter => _chapterRegistry != null ? _chapterRegistry.FinalChapter : ChapterId.Chapter4;

    /// <summary>현재 챕터 다음에 진행할 챕터가 남아 있으면 true. 마지막 챕터면 false(= 보스 클리어 시 런 클리어).</summary>
    public bool HasNextChapter() => CurrentChapter + 1 <= FinalChapter;

    /// <summary>다음 챕터로 진행. 마지막 챕터면 false 반환.</summary>
    public bool AdvanceToNextChapter()
    {
        if (!IsRunning) return false;

        var next = CurrentChapter + 1;
        if (next > FinalChapter) return false;

        CurrentChapter = next;
        ResolveActiveTheme();

        ChangeRunState(RunState.Map);
        return true;
    }

    /// <summary>
    /// CurrentChapter 확정. 절차 진행 흐름(베이스캠프→시작방 게이트→StartProcGenRunAsync)은
    /// StartNewRunAsync를 거치지 않아 CurrentChapter가 미설정((ChapterId)0)으로 남는다.
    /// 이 경우 HasNextChapter/AdvanceToNextChapter가 0+1=Chapter1로 오판해 최종 보스에서
    /// 런 클리어 대신 Chapter1을 재시작하는 오프바이원이 발생한다 → 런 시작 시 1회 확정한다.
    /// 이미 정의된 챕터(1~4)면 유지한다.
    /// </summary>
    public void EnsureChapter(ChapterId chapter)
    {
        if (System.Enum.IsDefined(typeof(ChapterId), CurrentChapter)) return;
        CurrentChapter = chapter;
        ResolveActiveTheme();
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
            ItemInventory.OnPlacedChanged -= RefreshPlayerItemStats;
            ItemInventory.OnPlacedChanged += RefreshPlayerItemStats;

            // 방 버프 ↔ 스탯 연동
            BuffHandler.OnBuffsChanged -= RefreshPlayerRoomBuffs;
            BuffHandler.OnBuffsChanged += RefreshPlayerRoomBuffs;

            // ItemEffectManager 초기화
            ItemInventory.OnPlacedChanged -= RebuildItemEffects;
            ItemInventory.OnPlacedChanged += RebuildItemEffects;
            EffectManager.Initialize(Player, this, ItemInventory);

            // 영구 각성 보너스 적용 (런 시작 시 1회)
            Player.RuntimeStats.RefreshAwakening();

            // 현재 인벤토리 아이템 보너스 즉시 적용 (씬 전환 후 복원)
            RefreshPlayerItemStats();

            // 방 버프 즉시 적용
            RefreshPlayerRoomBuffs();

            // 시너지 이력 복원 (씬 전환 후 복원)
            if (_appliedSynergies.Count > 0)
                Player.RuntimeStats.RestoreSynergies(_appliedSynergies);
        }

        OnPlayerBound?.Invoke(Player);

        // 서약 핸들러 초기화 — PlayerState + RuntimeStats가 준비된 직후 1회만 수행
        if (!_covenantInitialized && PlayerState != null && player?.RuntimeStats != null)
        {
            _covenantInitialized = true;
            var covenantCtx = new CovenantContext(player, player.RuntimeStats, PlayerState, this, _covenantDataTable);
            CovenantHandler.Initialize(covenantCtx);
        }

        // 서약 ↔ 플레이어 바인드 — 매 씬 전환(새 플레이어 인스턴스)마다 무기교체 구독·토스트 위치 갱신.
        // BindPlayer 내부가 UnsubscribeWeapon으로 재구독 멱등 처리하므로 중복 호출 안전.
        if (player != null)
            CovenantHandler.BindPlayer(player);
    }

    private void RefreshPlayerItemStats()
    {
        Debug.Log($"[GridChk] 스탯 refresh (frame {Time.frameCount}) placed={ItemInventory.PlacedCount} 효과수={EffectManager.ActiveEffects.Count}");
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
        Debug.Log($"[GridChk] 효과 rebuild (frame {Time.frameCount}) placed={ItemInventory.PlacedCount} → 효과수={EffectManager.ActiveEffects.Count}");
    }

    public bool TryGetPlayerState(out PlayerRunState state)
    {
        state = PlayerState;
        return (Phase == RunPhase.Running || Phase == RunPhase.Starting) && state != null;
    }

    // =========================================================
    // RunDelta APIs
    // =========================================================
    public void AddGold(int amount)
    {
        if (!IsRunning) return;
        if (amount <= 0) return;

        // 골드 획득 배율(아이템 GoldGainRate, 0.2 = +20%) 적용 — 모든 골드원이 이 진입점을 거치므로 한 곳에서 일괄 반영.
        // 환불(ShopRoomController.AddTempGold)은 이 진입점을 거치지 않으므로 배율 미적용(중복/이중곱 0).
        float goldGainRate = EffectManager?.GetAccumulatedStats().GoldGainRate ?? 0f;
        if (goldGainRate > 0f)
            amount = Mathf.Max(0, Mathf.RoundToInt(amount * (1f + goldGainRate)));

        RunDelta.GainedGold += amount;
        PlayerState?.AddTempGold(amount);
        QuestEvents.ReportGold(amount);
        Managers.Sound?.PlayEvent(SoundEvent.GoldPickup);
    }

    /// <summary>런 중 심연의 정수를 적립한다. EssenceTracker에서 호출.</summary>
    public void AddEssence(int amount)
    {
        if (!IsRunning) return;
        if (amount <= 0) return;

        RunDelta.GainedEssence += amount;
    }

    public void AddItem(ItemId itemId, int count)
    {
        if (!IsRunning) return;
        if (count <= 0) return;

        RunDelta.GainedItems.Add(new ItemStack(itemId, count));
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
