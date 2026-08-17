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

    // 포션 — 신규 런 시작 지급량, 사용 시 회복 비율(최대 HP 대비).
    private const int   NewRunStartingPotions = 3;
    private const float PotionHealRatio       = 0.40f;   // 즉발 40% 회복

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

    /// <summary>무한 루프 회차(심연 깊이). 0 = 스토리 1회차. 최종 보스 클리어 후 '계속' 선택 시 +1.
    /// 세션(런) 스코프 인메모리 — 챕터 전환 간 유지되나, 이어하기 저장에는 아직 포함하지 않는다(v1).</summary>
    public int AbyssDepth { get; private set; }

    // ── 누적 플레이 시간 ──
    // 세션은 씬 전환에도 살아남으므로(DDOL), 복원 시점의 누적치에 '이번 세션 경과'를 더해 계산한다.
    // 앱을 껐다 켜도 세이브의 playSeconds가 누적치로 들어와 이어진다.
    private double _playSecondsAccum;
    private float  _playSessionStart;

    /// <summary>이 런의 누적 플레이 시간(초). 세이브·로비 표시에 사용.</summary>
    public int PlaySeconds =>
        (int)(_playSecondsAccum + Mathf.Max(0f, Time.realtimeSinceStartup - _playSessionStart));

    /// <summary>플레이 시간 기준점 설정. 신규 런=0, 이어하기=세이브 누적치.</summary>
    private void ResetPlayClock(int accumSeconds)
    {
        _playSecondsAccum = Mathf.Max(0, accumSeconds);
        _playSessionStart = Time.realtimeSinceStartup;
    }

    private RefineryService _refinery;
    /// <summary>정제소(특수 룬 제작). 런 스코프 — 피버·비용·버프 상태를 유지한다. 최초 접근 시 생성.</summary>
    public RefineryService Refinery => _refinery ??= new RefineryService(FuelBank, ItemInventory);

    /// <summary>루프 1회당 적 스탯(HP·공격력) 가산 배율. 밸런스 대상(시작값 +25%/회차).</summary>
    private const float LoopScalePerDepth = 0.25f;

    /// <summary>현재 챕터의 몬스터 스탯 배율(HP·공격력). ChapterDataSO.difficultyScale × 루프 깊이 배율. 미주입/미설정 시 1.</summary>
    public float CurrentDifficultyScale
    {
        get
        {
            float chapterScale = _chapterRegistry != null
                ? (_chapterRegistry.GetData(CurrentChapter)?.difficultyScale ?? 1f) : 1f;
            return chapterScale * (1f + AbyssDepth * LoopScalePerDepth);
        }
    }

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

    private bool _covenantInitialized;

    /// <summary>존 단위 진행 서비스. startWithZoneLayout 모드에서만 초기화된다.</summary>
    public ZoneProgressionService ZoneProgression { get; private set; }

    public void InitZoneProgression(string layoutKey, Vector3 zone0WorldCenter, float blockCellSize)
    {
        ZoneProgression = new ZoneProgressionService(layoutKey, zone0WorldCenter, blockCellSize);
        Debug.Log($"[GameRunSession] ZoneProgressionService 초기화 완료 ({layoutKey})");
    }

    // 구 시너지 이력(_appliedSynergies / SynergyRecord)은 제거됨.
    // 시너지의 진실원본은 룬 보드 점유 셀(RunSaveData.runeCellsJson)이고, 복원도 그쪽이 담당한다.
    // 현재 발동 중인 단계 수가 필요하면 MerlinRuneBridge.ActiveSynergyCount를 읽는다(상승·하강 반영).
    private static int ActiveSynergyCount => MerlinRuneBridge.Instance != null
        ? MerlinRuneBridge.Instance.ActiveSynergyCount
        : 0;

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

    /// <summary>
    /// 조립 서약 「연마」(티어만 재굴림)를 이번 런에서 이미 썼는지. 런당 1회.
    /// 팝업이 아니라 런이 쥐고 있어야 한다 — 팝업마다 초기화되면 서약을 얻을 때마다 한 번씩 연마할 수 있다.
    /// (세이브에는 넣지 않는다. 이어하기로 한 번 돌려받는 건 손해가 아니고, 스키마를 건드리지 않는 쪽이 싸다.)
    /// </summary>
    public bool CovenantWhetUsed { get; set; }

    /// <summary>
    /// 지금 들어와 있는 방의 종류. 클리어 보상(<see cref="RoomRewardTable"/>)이 이 값으로 갈린다 —
    /// 정예방이 일반방과 같은 보상을 주던 결함(통합설계서 §3-2)의 해소 지점.
    /// 방 빌드 권한은 RunFlowController에 있으므로 그쪽이 방 진입마다 세팅한다.
    /// 레거시(단일 세계) 경로는 세팅하지 않으므로 기본값 Normal로 남는다.
    /// </summary>
    public RoomPlanKind CurrentRoomKind { get; private set; } = RoomPlanKind.Normal;

    /// <summary>방 진입 시 RunFlowController가 호출.</summary>
    public void SetCurrentRoomKind(RoomPlanKind kind)
    {
        CurrentRoomKind = kind;

        // 「고독」 기행 — 특수방을 하나도 안 들르고 완주했는가.
        if (kind is RoomPlanKind.Shop or RoomPlanKind.Event or RoomPlanKind.Crucible or RoomPlanKind.Refinery)
            SpecialRoomVisits++;
    }

    // 씬 전환 시 무기 슬롯 복원용
    public WeaponData[] SavedWeaponSlots { get; private set; }
    public int SavedCurrentSlotIndex { get; private set; } = -1;

    public void SaveWeaponSlots(WeaponData[] slots, int currentIndex)
    {
        SavedWeaponSlots = slots;
        SavedCurrentSlotIndex = currentIndex;
    }

    /// <summary>
    /// 살아 있는 플레이어의 무기 슬롯을 세션에 즉시 캡처한다. 플레이어가 없으면 아무것도 하지 않는다
    /// (이전 캡처를 지우면 안 된다 — 씬 전환 중 저장에서 무기 정보가 통째로 날아간다).
    ///
    /// 과거엔 씬 이탈(GameRunBootstrapper.OnDestroy)에서만 캡처해서, 방 경계 자동저장이나
    /// 재련소 SaveNow("crucible-enhance")가 <b>낡거나 비어 있는</b> 값을 저장했다.
    /// 그래서 강화·진화를 하고 게임을 끄면 이어하기에서 0강으로 돌아갔다. 저장 직전에 반드시 부른다.
    /// </summary>
    public void CaptureWeaponSlotsFromPlayer()
    {
        var wm = Player != null ? Player.WeaponManager : null;
        if (wm == null || wm.slots == null) return;

        var slots = new WeaponData[wm.SlotCount];
        for (int i = 0; i < wm.SlotCount && i < wm.slots.Length; i++)
            slots[i] = wm.slots[i]?.runtimeData;

        SaveWeaponSlots(slots, wm.CurrentSlotIndex);
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
    public void NotifyBossRoomCleared(Vector3 center)
    {
        BossKillCount++;

        // 보스방 클리어 = 챕터 완료. 이 챕터를 한 대도 안 맞고 끝냈으면 기행 1회.
        if (!_damagedThisChapter) FlawlessChapters++;
        _damagedThisChapter = false;
        OnBossRoomCleared?.Invoke(center);
    }

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
    // Room Clear Recording
    // =========================================================

    /// <summary>방 진입 시 호출. 이 방에서 얻은 양을 계산하기 위한 스냅샷.</summary>
    private void SnapshotRoomEntry()
    {
        _snapGold         = PlayerState?.TempGold ?? 0;
        _snapItemCount    = ItemInventory.PlacedCount + ItemInventory.StagingCount;
        _snapSynergyCount = ActiveSynergyCount;
    }

    /// <summary>방 클리어 시 호출. 현재 상태와 스냅샷의 차이로 방 내 획득 정보를 기록한다.</summary>
    private void RecordRoomClear(int pointId, RunState clearedState)
    {
        if (!IsRunning) return;

        int goldAfter  = PlayerState?.TempGold ?? 0;
        int itemCount  = ItemInventory.PlacedCount + ItemInventory.StagingCount;
        int synCount   = ActiveSynergyCount;

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

            // 원거리 파츠는 런 스코프 — 새 런은 빈 상태로 시작한다(직전 런 장착이 새어 나오지 않게).
            RangedPartsState.Current = new RangedPartsState();
            RangedPartsState.ApplyTestCarry();   // 베이스캠프 테스트 제단을 쓴 경우에만 동작(평소 무해)

            // 계약 3개 부여. 시드를 챕터에 섞어 런마다 다른 조합이 나오되, 같은 시드는 같은 계약을 준다.
            RunContracts.Current = new RunContracts();
            RunContracts.Current.RollIfEmpty(new System.Random(Environment.TickCount ^ (int)CurrentChapter));

            FirstRunService.BeginRun();   // 초행 판정은 로드아웃이 확정될 때 들어온다

            // 신규 런 강화재료 시작 지급 — 재련소(첫 소비처)가 이벤트방(첫 생산처)보다 앞 순번일 수 있어
            // 첫 재련소에서 강화재료=0이 되는 갭을 막는 안전장치. 이어하기(RestoreFromSaveAsync)는 이 경로를
            // 거치지 않으므로 이중지급 없음. 신규 런 = 새 세션(FuelBank 잔량 0)이라 정확히 초기량만 지급된다.
            FuelBank.Add(FuelKind.EnhanceMaterial, NewRunStartingEnhanceMaterial);
            PlayerState.AddPotion(NewRunStartingPotions);   // 신규 런 포션 지급(이어하기는 세이브 복원)
            ResetPlayClock(0);                              // 신규 런 = 플레이 시간 0부터

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
        ResetPlayClock(save.playSeconds);   // 이어하기 = 저장된 누적 시간부터 계속
        ResolveActiveTheme();
        CurrentHudMode = HUDIds.Mode.None;
        _hudModeSet = false;

        try
        {
            await UniTask.CompletedTask;

            PlayerState = new PlayerRunState(save.maxHp, save.runGold);
            PlayerState.SetHp(save.currentHp);
            PlayerState.RestorePotions(save.potionCount, save.potionCapacity > 0 ? save.potionCapacity : PlayerRunState.DefaultPotionCapacity);

            RunDelta = new RunDelta();

            // 원거리 파츠 복원 — 장착·레벨은 런 진행분이라 세이브에서 되살린다.
            RangedPartsState.Current = new RangedPartsState();
            if (!string.IsNullOrEmpty(save.rangedPartsJson))
            {
                var pw = JsonUtility.FromJson<RangedPartListWrapper>(save.rangedPartsJson);
                if (pw?.items != null)
                    RangedPartsState.Current.Restore(pw.items, save.rangedInvested, save.rangedGrantedTier);
            }

            // 계약 복원 — 이어하기는 같은 계약을 이어간다. 세이브에 없으면(구 세이브) 새로 뽑는다.
            RunContracts.Current = new RunContracts();
            RunContracts.Current.Restore(save.contractIds);
            if (!RunContracts.Current.HasAny)
            {
                RunContracts.Current.RollIfEmpty(new System.Random(Environment.TickCount ^ (int)CurrentChapter));
            }

            if (!string.IsNullOrEmpty(save.itemsJson))
            {
                var itemWrapper = JsonUtility.FromJson<ItemListWrapper>(save.itemsJson);
                if (itemWrapper?.items != null)
                    ItemInventory.RestorePlacedItems(itemWrapper.items);
            }

            // 시너지는 복원하지 않는다 — 룬 보드 점유 셀(runeCellsJson)에서 재계산되는 게 권위다.

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

        // 연료(강화재료·원석)는 <b>회수하지 않는다.</b> 런을 넘어 남는 것은 각성 정수(abyssEssence)뿐이다.
        // 과거엔 미소비 연료를 전액 정수로 환산했는데, 그러면 "아껴두면 죽어도 전액 회수"가 되어
        // 정제소·재련소 도박을 안 하는 것이 최적이 됐다(하드리셋의 무게가 사라짐).
        // 정수는 런 중 실제로 정수로 획득한 분(RunDelta.GainedEssence)만 이월된다.

        // 종료 상태 발행 (OnRunEnded 전에 구독자가 반응할 수 있도록)
        var endState = isCleared ? RunState.RunClear : RunState.RunEnd;
        CurrentRunState = endState;
        OnRunStateChanged?.Invoke(endState);

        // ── 계약 정산 ──
        // 달성분만 자동 지급한다. 수령 버튼을 두지 않는 이유는 주기 때문이다 — 매 런 생기는 목표를
        // 수령까지 요구하면 피곤하고, 죽은 직후에 버튼을 누르게 만드는 것도 어색하다.
        // 「죽어도 헛되지 않았다」는 <b>실패한 런에서도 이만큼 남는다</b>는 사실이 만든다.
        int contractEssence = RunContracts.Current?.EarnedEssence(this) ?? 0;
        if (contractEssence > 0)
        {
            RunDelta.GainedEssence += contractEssence;
            Debug.Log($"[계약] 정산 — {RunContracts.Current.DoneCount(this)}건 달성 · 정수 +{contractEssence}");
        }

        // ── 초행 정산 ──
        // 계약분까지 포함한 총액에 배율을 건다 — 초행을 깊이 끌고 갈수록 이득이 커지는 게 설계 의도다.
        RunDelta.GainedEssence += FirstRunService.SettleRun(RunDelta.GainedEssence);

        var result = new EndRunResult(
            isCleared:     isCleared,
            chapter:       CurrentChapter,
            gainedGold:    RunDelta.GainedGold,
            gainedEssence: RunDelta.GainedEssence,
            gainedItems:   RunDelta.GainedItems.ToArray(),
            reason:        reason,
            abyssDepth:    AbyssDepth,
            roomClears:    _roomClearRecords.Count,
            kills:         KillCount,
            potionUsed:    PotionUsedThisRun,
            specialVisits: SpecialRoomVisits,
            flawless:      FlawlessChapters,
            eliteKills:    EliteKillCount,
            bossKills:     BossKillCount,
            shopUses:      ShopUseCount,
            refineUses:    RefineUseCount,
            maxEnhance:    MaxEnhanceLevel
        );

        OnRunEnded?.Invoke(result);

        try { PlayerState?.Deactivate(); }
        catch (Exception e) { Debug.LogWarning($"[GameRun] PlayerState.Deactivate() error: {e.Message}"); }

        // 룬판 하드리셋 (DDOL이라 씬 전환으로 안 죽으므로 수동 정리).
        // 적용 이력만 지우면 판 위의 룬이 다음 런까지 남아 빈 인벤토리와 어긋난다 → 판째로 버린다.
        MerlinRuneBridge.Instance?.ClearBoard();

        // 살아있는 몬스터도 같은 이유로 수동 회수 — 풀(@Pools)이 DDOL이라 씬 전환으로 죽지 않는다.
        // 남겨두면 허브·다음 런까지 따라와 이전 런의 적이 돌아다닌다.
        RelicFairy.Monster.MonsterBase.DespawnAll();

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

    /// <summary>설정상 진행 가능한 마지막 챕터. ChapterRegistry._finalChapter(없으면 Chapter3).
    /// Ch4는 빌드에서 제외된 미완 콘텐츠라, 레지스트리 유실 시에도 없는 씬으로 진행하지 않도록 Chapter3로 폴백한다.
    /// <para>「챕터 4 개방」을 해금하면 한 챕터 더 나아간다 — 다만 <b>레지스트리 상한을 넘지는 않는다.</b>
    /// 해금이 없는 씬을 여는 열쇠가 되면 안 되므로, 레지스트리가 Ch3까지만 인정하면 해금해도 Ch3에 머문다.</para></summary>
    private ChapterId FinalChapter
    {
        get
        {
            var registryMax = _chapterRegistry != null ? _chapterRegistry.FinalChapter : ChapterId.Chapter3;
            if (!MemoryAltarService.IsChapter4Unlocked && registryMax > ChapterId.Chapter3)
                return ChapterId.Chapter3;
            return registryMax;
        }
    }

    /// <summary>현재 챕터 다음에 진행할 챕터가 남아 있으면 true. 마지막 챕터면 false(= 보스 클리어 시 런 클리어).</summary>
    public bool HasNextChapter() => CurrentChapter + 1 <= FinalChapter;

    /// <summary>현재 챕터가 '최종 직전'인지 — 이 보스를 깨면 다음이 최종 챕터.
    /// 유물 코어 진화 드래프트를 여기에 배치해, 최종 챕터를 진화형으로 플레이하게 한다(설계서 §1-6).</summary>
    public bool IsNextChapterFinal() => CurrentChapter + 1 == FinalChapter;

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

    /// <summary>무한 루프 진입 — 심연 깊이를 올리고 Ch1으로 회귀한다(로드아웃은 유지, 적 스탯만 스케일↑).
    /// 최종 보스 클리어 후 '계속'을 선택했을 때 호출. 챕터 전환 기계를 재사용하되 목적지만 Ch1으로 되돌린다.
    /// <para>「심연 깊이 개방」이 없으면 <b>false</b>를 돌려준다 — 호출부는 그냥 런 클리어로 마감해야 한다.
    /// 첫 완주가 이 노드의 선행 조건이므로, 해금 전에는 루프 자체가 존재하지 않는다(정본 §2-3).</para></summary>
    /// <returns>루프에 진입했으면 true. false면 상태를 전혀 바꾸지 않았다.</returns>
    public bool BeginAbyssLoop()
    {
        if (!IsRunning) return false;

        if (!MemoryAltarService.IsAbyssDepthUnlocked)
        {
            Debug.Log("[GameRun] 심연 깊이 미해금 — 루프 진입 없이 런을 종료한다.");
            return false;
        }

        AbyssDepth++;
        CurrentChapter = ChapterId.Chapter1;
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
        // 챕터 전환 시 새 플레이어는 InitializeFrom으로 만피(Hp=MaxHp)가 된다.
        // 자동 만피를 없애고 HP를 이어받는다 — 회복은 챕터 시작 특수 오브젝트/포션이 담당.
        // PlayerState는 씬 전환에도 살아남아 직전 HP를 들고 있으므로, 그 '비율'을 새 플레이어에 적용한다.
        //   · 첫 시작:  PlayerState.Hp==MaxHp → 비율 1.0 → 만피 (정상)
        //   · 챕터 전환: 직전 챕터 종료 HP 비율로 진입
        //   · 이어하기:  save.currentHp/save.maxHp 비율 복원(LoadFromSave가 PlayerState에 선반영)
        // SubscribePlayerStateSource가 곧 PlayerState를 새 만피로 덮으므로 비율은 지금 캡처한다.
        float carriedHpRatio = (PlayerState != null && PlayerState.MaxHp > 0)
            ? Mathf.Clamp01((float)PlayerState.Hp / PlayerState.MaxHp)
            : 1f;

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

            // 시너지는 룬 보드 점유 셀에서 재계산된다(GameRunBootstrapper.RestoreRuneBoardFromSave →
            // OnZoneCellsUpdated → RuneEffects.Activate). 구 RestoreSynergies 경로는 제거함.

            // 위 Refresh들로 MaxHp가 최종 확정된 뒤, 이어받은 비율로 HP를 설정한다(자동 만피 제거).
            var rs = Player.RuntimeStats;
            rs.SetHp(Mathf.Clamp(Mathf.RoundToInt(rs.MaxHp * carriedHpRatio), 1, rs.MaxHp));
        }

        OnPlayerBound?.Invoke(Player);

        // 서약 핸들러 초기화 — PlayerState + RuntimeStats가 준비된 직후 1회만 수행
        if (!_covenantInitialized && PlayerState != null && player?.RuntimeStats != null)
        {
            _covenantInitialized = true;
            var covenantCtx = new CovenantContext(player, player.RuntimeStats, PlayerState, this);
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

        RunDelta.GainedEssence += Mathf.RoundToInt(amount * EssenceDepthMultiplier);
    }

    /// <summary>
    /// 「깊이 보상 배율」 해금 시 깊이마다 +15%. 미해금이면 1배 — 깊이를 내려가도 수급이 안 늘어난다.
    /// <para>깊이가 오를수록 적이 ×1.25로 세지는데 보상이 그대로면 깊이가 순손해가 된다.
    /// 이 노드가 그 기울기를 메우는 자리라, 곱은 <b>수급 깔때기 한 곳</b>에만 건다.</para>
    /// </summary>
    private float EssenceDepthMultiplier =>
        MemoryAltarService.IsUnlocked(MemoryAltarCatalog.DepthReward)
            ? 1f + AbyssDepth * 0.15f
            : 1f;

    public void AddItem(ItemId itemId, int count)
    {
        if (!IsRunning) return;
        if (count <= 0) return;

        RunDelta.GainedItems.Add(new ItemStack(itemId, count));
    }

    // ── 포션 ──────────────────────────────────────────────────

    // ── 기억의 제단 기록 집계 ─────────────────────────────────
    // 런 <b>안에서만</b> 센다. 영구 기록(UserGameData.records)에는 EndRun 시 1회만 반영한다 —
    // 매 이벤트마다 저장하면 저장 빈도가 올라가고 이어하기와 얽힌다(정본 §8-1).

    public int KillCount        { get; private set; }

    // ── 기행 판정 ──
    /// <summary>이 런에서 포션을 한 번이라도 썼는가.</summary>
    public bool PotionUsedThisRun   { get; private set; }
    /// <summary>이 런에서 들른 특수방(상점·이벤트·재련·정제) 수.</summary>
    public int  SpecialRoomVisits   { get; private set; }
    /// <summary>피격 없이 클리어한 챕터 누적.</summary>
    public int  FlawlessChapters    { get; private set; }
    private bool _damagedThisChapter;

    /// <summary>플레이어가 실제로 피해를 입었다. 무피격 챕터 판정을 깬다.</summary>
    public void ReportPlayerDamaged() => _damagedThisChapter = true;
    public int EliteKillCount   { get; private set; }
    public int BossKillCount    { get; private set; }
    public int ShopUseCount     { get; private set; }
    public int RefineUseCount   { get; private set; }
    public int MaxEnhanceLevel  { get; private set; }

    /// <summary>「부활 1회」를 이 런에서 이미 썼는가. 이어하기로 되살아나지 않도록 세이브에 실린다.</summary>
    public bool MetaReviveUsed { get; private set; }

    /// <summary>
    /// 「부활 1회」를 소모한다. 해금돼 있고 아직 안 썼을 때만 true.
    /// <para>런 스코프라 사망 시 자연히 사라진다 — 다음 런에 다시 1회가 주어진다.</para>
    /// </summary>
    public bool TryConsumeMetaRevive()
    {
        if (MetaReviveUsed) return false;
        if (!MemoryAltarService.HasRevive) return false;

        MetaReviveUsed = true;
        return true;
    }

    /// <summary>이어하기 복원 — 세이브에 실린 제단 관련 런 상태를 되돌린다.</summary>
    public void RestoreAltarProgress(bool reviveUsed, int kills, int eliteKills, int bossKills,
                                     int shopUses, int refineUses, int maxEnhance,
                                     bool potionUsed, int specialVisits, int flawless)
    {
        MetaReviveUsed      = reviveUsed;
        PotionUsedThisRun   = potionUsed;
        SpecialRoomVisits   = specialVisits;
        FlawlessChapters    = flawless;
        KillCount        = kills;
        EliteKillCount   = eliteKills;
        BossKillCount    = bossKills;
        ShopUseCount     = shopUses;
        RefineUseCount   = refineUses;
        MaxEnhanceLevel  = maxEnhance;
    }

    public void ReportKill()      => KillCount++;
    public void ReportPotionUsed() => PotionUsedThisRun = true;
    public void ReportEliteKill() => EliteKillCount++;
    public void ReportBossKill()  => BossKillCount++;
    public void ReportShopUse()   => ShopUseCount++;
    public void ReportRefineUse() => RefineUseCount++;

    /// <summary>무기 강화 성공 시 도달 수치를 보고한다(최고치만 남는다).</summary>
    public void ReportEnhanceLevel(int level)
    {
        if (level > MaxEnhanceLevel) MaxEnhanceLevel = level;
    }

    // ── 상점 정비소 연결점 ────────────────────────────────────

    /// <summary>룬 제거 누적 횟수(누진 가격용, StS 카드제거 방식). 런 지속.</summary>
    public int RuneExtractCount { get; set; }

    /// <summary>
    /// 배치된 룬 1개를 제거하고 원석으로 되돌린다(상점 룬 제거 서비스의 실제 처리).
    /// ⚠️ 보드 셀/시너지 재계산은 UI(UI_GridPanel.HandleItemRemoved) 경로가 담당하므로,
    ///    이 메서드는 <b>인벤토리 + 원석 환원</b>만 처리하고 보드 정리는 호출부(룬판 제거 모드)가 함께 해야 한다.
    ///    현재는 연결점만 — 룬판 제거 모드 UI 배선 시 여기로 들어온다.
    /// </summary>
    public bool TryExtractPlacedRune(RuntimeItemData item, int oreRefund)
    {
        if (item == null || ItemInventory == null) return false;
        if (!ItemInventory.IsPlaced(item.instanceId)) return false;

        ItemInventory.RemovePlaced(item);
        if (oreRefund > 0) FuelBank?.Add(FuelKind.RuneOre, oreRefund);
        return true;
    }

    /// <summary>포션 사용 — 1개 소모 후 즉발 % 회복. 성공 시 true.</summary>
    public bool TryUsePotion()
    {
        if (Player?.RuntimeStats == null || PlayerState == null) return false;

        // 못 쓰는 경우엔 <b>이유를 말해준다</b>. 예전엔 조용히 false만 반환해서
        // 만피·재고0·회복봉인 어느 쪽이든 "포션이 고장났다"로만 보였다.
        if (Player.RuntimeStats.Hp >= Player.RuntimeStats.MaxHp)
        {
            NotifyPotionBlocked("체력이 가득 찼다");
            return false;
        }
        if (PlayerState.HealLocked)
        {
            NotifyPotionBlocked("회복이 봉인되어 있다");
            return false;
        }
        if (PlayerState.PotionCount <= 0)
        {
            NotifyPotionBlocked("포션이 없다");
            return false;
        }
        if (!PlayerState.TryConsumePotion()) return false;

        PotionUsedThisRun = true;   // 「금욕」 기행 판정 — 실제로 소비된 순간에만 센다

        int heal = Mathf.Max(1, Mathf.RoundToInt(Player.RuntimeStats.MaxHp * PotionHealRatio));
        Player.Heal(heal);
        return true;
    }

    /// <summary>포션을 못 쓴 이유를 HUD 알림으로 알린다(무반응 = 고장으로 읽히지 않게).</summary>
    private static void NotifyPotionBlocked(string reason)
    {
        var hud = UnityEngine.Object.FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        hud?.ShowBuffNotice($"<color=#9AA0A6>{reason}</color>");
    }

    /// <summary>대기방 회복 오브젝트 — 만피 + 포션 가득 보충.</summary>
    public void RestAtSanctuary()
    {
        if (Player?.RuntimeStats != null)
            Player.RuntimeStats.SetHp(Player.RuntimeStats.MaxHp);
        PlayerState?.RefillPotions();
    }

    private PlayerRunState CreateInitialPlayerStateFromSession()
    {
        var charData = Managers.CharacterData?.M_CharacterData;
        int maxHp = (charData != null && charData.maxHealth > 0) ? charData.maxHealth : 100;

        if (charData == null)
            Debug.LogWarning("[GameRun] CharacterData not set — PlayerRunState uses default maxHp=100.");

        // 출시 정책: 신규 런은 골드 0에서 시작 — 방 보상/전투 드롭으로만 확보한다.
        const int NewRunStartingGold = 0;
        return new PlayerRunState(maxHp, NewRunStartingGold);
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
