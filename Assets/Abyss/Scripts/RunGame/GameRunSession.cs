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

    public RoomManager RoomManager { get; private set; }
    public StagePointManager StagePointManager { get; private set; }

    public StageMapSpawner Spawner { get; private set; }
    public PlayerController Player { get; private set; }

    public PlayerRunState PlayerState { get; private set; }
    public RunDelta RunDelta { get; private set; } = new RunDelta();

    // --------------------
    // Events
    // --------------------
    public event Action OnRunStarted;
    public event Action<EndRunResult> OnRunEnded;

    public event Action<PlayerRunState> OnPlayerStateReady;
    public event Action<PlayerController> OnPlayerBound;
    public event Action<StageMapSpawner> OnSpawnerBound;

    // HUD mode
    public event Action<HUDIds.Mode> OnHudModeChanged;

    // Run state
    public RunState CurrentRunState { get; private set; } = RunState.None;
    public event Action<RunState> OnRunStateChanged;

    public HUDIds.Mode CurrentHudMode { get; private set; } = HUDIds.Mode.None;
    private bool _hudModeSet = false;

    private Dictionary<int, StageData> _stageDataCache;

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
        RoomManager = null;
        StagePointManager = null;
        Player = null;
        Spawner = null;
        PlayerState = null;
        CurrentRunState = RunState.None;
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
        ChangeRunState(roomState);
    }

    public void EnterStandby()
    {
        if (!IsRunning) return;
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
        ChangeRunState(RunState.ChapterClear);
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
    public void BindSpawner(StageMapSpawner spawner)
    {
        Spawner = spawner;
        if (Spawner == null) Debug.LogWarning("[GameRun] BindSpawner: spawner is null");
        OnSpawnerBound?.Invoke(Spawner);
    }

    public void BindPlayer(PlayerController player)
    {
        Player = player;
        if (Player == null) Debug.LogWarning("[GameRun] BindPlayer: player is null");
        OnPlayerBound?.Invoke(Player);
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
    public void SpawnCurrentPointMap()
    {
        if (!IsRunning || StagePointManager == null || RoomManager == null)
        {
            Debug.LogWarning("[GameRun] SpawnCurrentPointMap ignored: not ready");
            return;
        }

        if (Spawner == null)
        {
            Debug.LogError("[GameRun] Spawner is not bound. (GameRunBootstrapper에서 주입 필요)");
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
        Spawner.ChangeMap(room.prefab);

        // 방 카테고리 → RunState 자동 전환
        var category = RoomCategoryUtil.Parse(room.category);
        ChangeRunState(CategoryToRunState(category));
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

        SpawnCurrentPointMap();
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

    public void SpawnPointMap(int pointId)
    {
        if (!IsRunning || StagePointManager == null || RoomManager == null) return;

        if (Spawner == null)
        {
            Debug.LogError("[GameRun] Spawner is not bound.");
            return;
        }

        var ctx = StagePointManager.GetContext(pointId);
        if (ctx == null) return;

        StagePointManager.Resolve(ctx);

        var room = RoomManager.GetById(ctx.ResolvedRoomId);
        if (room == null || string.IsNullOrEmpty(room.prefab)) return;

        Spawner.ChangeMap(room.prefab);
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
        return new PlayerRunState();
    }
}