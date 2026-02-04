using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Run(런) 단위 상태 관리자
/// - Run 시작/종료 수명 주기 관리
/// - RoomManager / StagePointManager 초기화 및 이동 처리
/// - 씬 의존 오브젝트(Spawner, Player)를 "주입(Bind)" 받아 사용
/// - 런 전용 플레이어 상태(PlayerRunState) 생성/관리 (HUD는 이걸 바라보는 것을 권장)
/// - 런 종료 시 "영구 반영"은 EndRunResult로 커밋(Managers 쪽에서 Apply)
/// </summary>
public sealed class GameRunManager
{
    // --------------------
    // Addressables Keys
    // --------------------
    private const string ROOMS_KEY = "STAGEDATA_ROOMS";
    private const string STAGE_KEY = "STAGEDATA_STAGE";

    // --------------------
    // Public State
    // --------------------
    public ChapterId CurrentChapter { get; private set; }
    public bool IsRunning { get; private set; }

    public RoomManager RoomManager { get; private set; }
    public StagePointManager StagePointManager { get; private set; }
    

    /// <summary>씬에서 생성되는 StageMapSpawner를 Bootstrapper가 주입</summary>
    public StageMapSpawner Spawner { get; private set; }

    /// <summary>씬의 플레이어 오브젝트(컨트롤러). 필요하다면 주입</summary>
    public PlayerController Player { get; private set; }

    /// <summary>
    /// 인게임에서만 사용하는 런 상태(HP/버프/런 재화 등)
    /// HUD는 가능하면 이 상태를 구독해서 표시
    /// </summary>
    public PlayerRunState PlayerState { get; private set; }

    public event Action<PlayerController> OnPlayerBound;

    /// <summary>런 도중 획득/변경된 영구 반영 후보(재화/아이템 등)</summary>
    public RunDelta RunDelta { get; private set; } = new RunDelta();

    // --------------------
    // Events (선택)
    // --------------------
    public event Action OnRunStarted;
    public event Action<EndRunResult> OnRunEnded;

    // --------------------
    // Internal caches
    // --------------------
    private Dictionary<int, StageData> _stageDataCache;

    // =========================================================
    // Run Lifecycle
    // =========================================================

    /// <summary>
    /// 새 런 시작
    /// - Room/StagePoint 초기화
    /// - (권장) 로비에서 확정된 Loadout/유저 데이터를 기반으로 PlayerState 생성
    /// </summary>
    public async UniTask StartNewRunAsync(ChapterId chapter)
    {
        if (IsRunning)
        {
            Debug.LogWarning("[GameRun] StartNewRunAsync ignored: already running");
            return;
        }

        IsRunning = true;
        CurrentChapter = chapter;

        // 1) Stage 데이터(선택)
        await LoadStageDataAsync(STAGE_KEY);

        // 2) Rooms 데이터(필수)
        RoomManager = new RoomManager();
        await RoomManager.InitializeAsync(ROOMS_KEY);

        if (!RoomManager.IsInitialized)
        {
            Debug.LogError($"[GameRun] RoomManager init failed. Address='{ROOMS_KEY}'");
            IsRunning = false;
            return;
        }

        // 3) StagePoint (런 단위 상태)
        StagePointManager = new StagePointManager();
        StagePointManager.Initialize(chapter, RoomManager);

        // 4) 런 전용 플레이어 상태 생성 (로비/유저 데이터 기반)
        //    - 너 프로젝트에서는 아래 CreateInitialPlayerStateFromSession()를
        //      "유저/로비 로드아웃"에 맞게 구현해주면 됨.
        PlayerState = CreateInitialPlayerStateFromSession();
        RunDelta = new RunDelta();

        OnRunStarted?.Invoke();
        Debug.Log($"[GameRun] Started. chapter={CurrentChapter}");
    }

    /// <summary>
    /// 런 종료
    /// - RunDelta를 기반으로 EndRunResult 생성
    /// - 영구 반영은 Managers(세션 데이터)쪽에서 Apply 하는 것을 권장
    /// </summary>
    public EndRunResult EndRun(bool isCleared, string reason = null)
    {
        if (!IsRunning)
        {
            Debug.LogWarning("[GameRun] EndRun ignored: not running");
            return default;
        }

        IsRunning = false;

        // 런 결과 생성(영구 반영용)
        var result = new EndRunResult(
            isCleared: isCleared,
            chapter: CurrentChapter,
            gainedGold: RunDelta.GainedGold,
            gainedItems: RunDelta.GainedItems.ToArray(),
            reason: reason
        );

        // (권장) 여기서 바로 Managers에 반영하지 말고,
        // 외부(예: GameRunBootstrapper/ResultFlow)가 Apply 하게 분리해도 됨.
        // Managers.UserData.Apply(result);

        OnRunEnded?.Invoke(result);

        // 런 상태 정리(선택: 참조 해제)
        RoomManager = null;
        StagePointManager = null;
        Player = null;
        Spawner = null;
        PlayerState = null;

        Debug.Log($"[GameRun] Ended. cleared={isCleared}, reason={reason}");
        return result;
    }

    // =========================================================
    // Scene Bind (Bootstrapper가 주입)
    // =========================================================

    public void BindSpawner(StageMapSpawner spawner)
    {
        Spawner = spawner;
        if (Spawner == null) Debug.LogWarning("[GameRun] BindSpawner: spawner is null");
    }

    public void BindPlayer(PlayerController player)
    {
        Player = player;

        if (Player == null)
            Debug.LogWarning("[GameRun] BindPlayer: player is null");

        // ✅ 여기서 HUD/시스템에 알림
        OnPlayerBound?.Invoke(Player);
    }

    // =========================================================
    // StagePoint UI Bind
    // =========================================================

    /// <summary>
    /// 씬의 StagePointUI를 런 시스템에 등록(이벤트 구독 포함)
    /// - 반드시 ResolveAll 전에 호출되어야 UI가 OnPointResolved를 받는다.
    /// </summary>
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

    /// <summary>
    /// 모든 노드 룸 확정 + Start 포인트를 현재로 세팅.
    /// UI가 Register된 뒤 호출해야 UI 표시가 바로 갱신됨.
    /// </summary>
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

        // 방문 시 Resolve 보장이 있지만, 안전하게 한 번 더
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

    /// <summary>
    /// UI 클릭 등으로 "해당 포인트로 이동"을 요청한다.
    /// - 이동 성공 시 맵 교체까지 수행
    /// </summary>
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

    /// <summary>
    /// 외부에서 특정 포인트를 스폰하고 싶을 때(선택)
    /// </summary>
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
    // RunDelta APIs (런 중 획득/변경 기록)
    // =========================================================

    public void AddGold(int amount)
    {
        if (!IsRunning) return;
        if (amount <= 0) return;

        RunDelta.GainedGold += amount;
        PlayerState?.AddTempGold(amount); // 런 HUD에 표시하고 싶다면
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

    // =========================================================
    // PlayerState Creation (프로젝트 맞춤 구현 포인트)
    // =========================================================

    /// <summary>
    /// 로비/유저 데이터 기반으로 런 상태 생성.
    /// - 너 프로젝트에서는 Managers.User / Managers.Inventory / Loadout 등을 보고
    ///   base stats + equipped weapon 등을 반영해서 런 상태를 만들어주면 됨.
    /// </summary>
    private PlayerRunState CreateInitialPlayerStateFromSession()
    {
        // 예시(가짜):
        // var loadout = Managers.User.Loadout;
        // var baseStats = Managers.User.GetCharacterBaseStats(loadout.CharacterId);
        // return new PlayerRunState(baseStats);

        // 최소한 null이 아닌 런 상태를 반환
        return new PlayerRunState();
    }
}


