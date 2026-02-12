//============================================================
// GameRunManager.cs (Improved + HUD Mode Event Added)
//============================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed class GameRunManager
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

    // ✅ 추가: HUD 모드 변경 요청(게임 로직 -> UI 로직 분리)
    public event Action<HUDIds.Mode> OnHudModeChanged;

    // ✅ 추가: 현재 HUD 모드(늦게 붙는 UI가 즉시 동기화 가능)
    public HUDIds.Mode CurrentHudMode { get; private set; } = HUDIds.Mode.None;

    private Dictionary<int, StageData> _stageDataCache;

    // =========================================================
    // Run Lifecycle
    // =========================================================
    public async UniTask StartNewRunAsync(ChapterId chapter)
    {
        if (Phase != RunPhase.NotRunning)
        {
            Debug.LogWarning($"[GameRun] StartNewRunAsync ignored: phase={Phase}");
            return;
        }

        Phase = RunPhase.Starting;
        CurrentChapter = chapter;

        try
        {
            await LoadStageDataAsync(STAGE_KEY);

            RoomManager = new RoomManager();
            await RoomManager.InitializeAsync(ROOMS_KEY);

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

            // ✅ 준비 완료 이벤트
            OnPlayerStateReady?.Invoke(PlayerState);

            // ✅ 추가: 기본 HUD 모드 요청 (런 시작 시 기본은 탐험)
            RequestHudMode(HUDIds.Mode.Explore);

            // ✅ 이제부터 Running
            Phase = RunPhase.Running;

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

        var result = new EndRunResult(
            isCleared: isCleared,
            chapter: CurrentChapter,
            gainedGold: RunDelta.GainedGold,
            gainedItems: RunDelta.GainedItems.ToArray(),
            reason: reason
        );

        OnRunEnded?.Invoke(result);

        try
        {
            PlayerState?.Deactivate();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GameRun] PlayerState.Deactivate() error: {e.Message}");
        }

        // ✅ 추가: 종료 시 HUD 모드 초기화(선택)
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
    }

    // =========================================================
    // ✅ HUD Mode API (게임 로직이 UI를 직접 만지지 않게)
    // =========================================================

    /// <summary>
    /// HUD 모드를 바꾸고 싶을 때 호출 (UI 직접 참조 금지)
    /// - Running 전(Starting)에도 호출 허용: UI가 먼저 떠 있어도 반영 가능
    /// </summary>
    public void RequestHudMode(HUDIds.Mode mode)
    {
        Debug.Log($"2️⃣ RequestHudMode: {mode}");

        CurrentHudMode = mode;
        Debug.Log($"2️⃣ CurrentHudMode set: {CurrentHudMode}");

        Debug.Log($"2️⃣ OnHudModeChanged is null? {OnHudModeChanged == null}");

        OnHudModeChanged?.Invoke(mode);
    }


    /// <summary>
    /// 늦게 붙는 UI용: 현재 HUD 모드를 즉시 알려주는 유틸
    /// </summary>
    public bool TryGetHudMode(out HUDIds.Mode mode)
    {
        mode = CurrentHudMode;
        return Phase != RunPhase.NotRunning;
    }

    // (선택) 자주 쓰는 래퍼: 호출부 가독성
    public void NotifyCombatStarted()
    {
        Debug.Log("1️⃣ NotifyCombatStarted called");
        RequestHudMode(HUDIds.Mode.Combat);
    }

    public void NotifyCombatEnded()    => RequestHudMode(HUDIds.Mode.Explore);
    public void NotifyBossStarted()    => RequestHudMode(HUDIds.Mode.Boss);
    public void NotifyCutsceneStarted()=> RequestHudMode(HUDIds.Mode.Cutscene);
    public void NotifyCutsceneEnded()  => RequestHudMode(HUDIds.Mode.Explore);

    // =========================================================
    // Scene Bind (Bootstrapper가 주입)
    // =========================================================
    public void BindSpawner(StageMapSpawner spawner)
    {
        Spawner = spawner;

        if (Spawner == null)
            Debug.LogWarning("[GameRun] BindSpawner: spawner is null");

        OnSpawnerBound?.Invoke(Spawner);
    }

    public void BindPlayer(PlayerController player)
    {
        Player = player;

        if (Player == null)
            Debug.LogWarning("[GameRun] BindPlayer: player is null");

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

        foreach (var ui in points)
            ui.Register(StagePointManager, RoomManager);
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

        Spawner.ChangeMap(room.prefab);
    }

    public void RequestMoveTo(int targetPointId)
    {
        if (!IsRunning || StagePointManager == null)
        {
            Debug.LogWarning("[GameRun] RequestMoveTo ignored: not running");
            return;
        }

        if (!StagePointManager.CanMove(targetPointId))
            return;

        if (!StagePointManager.TryMoveTo(targetPointId))
            return;

        SpawnCurrentPointMap();
    }

    public void SpawnPointMap(int pointId)
    {
        if (!IsRunning || StagePointManager == null || RoomManager == null)
            return;

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
    private async UniTask LoadStageDataAsync(string key)
    {
        TextAsset textAsset = null;

        try
        {
            textAsset = await Managers.AddressableManager.LoadAssetAsync<TextAsset>(key);
        }
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
