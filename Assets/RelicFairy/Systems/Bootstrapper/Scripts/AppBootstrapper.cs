using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using BackEnd;

public sealed class AppBootstrapper : MonoBehaviour
{
    public static AppBootstrapper Instance { get; private set; }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoCreate()
    {
        if (FindObjectOfType<AppBootstrapper>(true) != null)
            return;

        var go = new GameObject("@AppBootstrapper");
        go.AddComponent<AppBootstrapper>();
    }
#endif

    [Header("Core Init")]
    [SerializeField] private bool initBackend = true;
    [SerializeField] private bool initAddressables = true;

    public static bool IsBackendInitialized { get; private set; }
    public static bool IsAutoLoggedIn { get; private set; }

    [Header("Animation Preload (Test Friendly)")]
    [SerializeField] private bool preloadAnimations = true;

    [Header("UIRoot Auto Create")]
    [SerializeField] private bool autoCreateUIRoot = true;
    [SerializeField] private string uiRootPrefabKey = "@UIRoot";
    private bool _uiRootEnsured;

    [Header("Steam Login")]
    [SerializeField] private bool useSteamLogin = false;

    [Header("Auto Login (Device ID)")]
    [SerializeField] private bool useAutoLogin = true;

    [Header("Flow Start (Optional)")]
    [SerializeField] private bool startFlow = false;   // 테스트 씬이면 보통 false
    [SerializeField] private Define.Scene startScene = Define.Scene.Logo;
    public bool IsReady { get; private set; }

    // ---- 로드아웃 (로비 선택 → InGame 전달) ----
    public PlayerLoadout Loadout { get; private set; } = new PlayerLoadout();

    // ---- Run 수명 관리 ----
    public GameRunSession CurrentRun { get; private set; }
    public bool IsNewRunPending { get; private set; }

    public void BeginRun(GameRunSession session)
    {
        if (CurrentRun != null)
            CurrentRun.OnRunEnded -= HandleRunEnded;

        CurrentRun = session;
        CurrentRun.OnRunEnded += HandleRunEnded;
    }

    public void EndRun()
    {
        if (CurrentRun != null)
            CurrentRun.OnRunEnded -= HandleRunEnded;

        var rpm = RunProgressManager.Instance;
        rpm?.ClearAsync(rpm.ActiveSlotIndex).Forget();
        CurrentRun = null;
        Loadout.Clear();
    }

    private static void HandleRunEnded(EndRunResult result)
    {
        HandleRunEndedAsync(result).Forget();
    }

    private static async UniTaskVoid HandleRunEndedAsync(EndRunResult result)
    {
        if (BackendGameData.Instance != null)
            await BackendGameData.Instance.ApplyRunResultAsync(result);
    }

    /// <summary>챕터 ID에 대응하는 GameScene 씬을 반환한다.</summary>
    public static Define.Scene GetSceneForChapter(ChapterId chapter) => chapter switch
    {
        ChapterId.Chapter1 => Define.Scene.GameScene_Ch1,
        ChapterId.Chapter2 => Define.Scene.GameScene_Ch2,
        ChapterId.Chapter3 => Define.Scene.GameScene_Ch3,
        ChapterId.Chapter4 => Define.Scene.GameScene_Ch4,
        _                  => Define.Scene.GameScene_Ch1,
    };

    public void RequestLoad(Define.Scene scene)
    {
        if (_flow != null)
        {
            _flow.RequestLoad(scene);
            return;
        }

        // startFlow = false 환경(테스트 씬): UI 정리 후 비동기 로드
        Managers.UI.ClearOnSceneTransition();
        LoadSceneNoFlowAsync(scene).Forget();
    }

    private async UniTaskVoid LoadSceneNoFlowAsync(Define.Scene scene)
    {
        Managers.Sound?.StopBgm();

        var loading = UI_SceneLoading.Instance;
        if (loading != null) await loading.ShowAsync();

        var op = SceneManager.LoadSceneAsync(scene.ToString());
        op.allowSceneActivation = false;

        float speed = loading != null ? loading.ProgressSpeed : 0.5f;
        float display = 0f;
        while (op.progress < 0.9f)
        {
            display = Mathf.MoveTowards(display, op.progress / 0.9f, Time.unscaledDeltaTime * speed);
            loading?.SetProgress(display);
            await UniTask.Yield(PlayerLoopTiming.Update);
        }
        // 표시 진행도를 1까지 천천히 채운 뒤 씬 활성화
        while (display < 1f)
        {
            display = Mathf.MoveTowards(display, 1f, Time.unscaledDeltaTime * speed);
            loading?.SetProgress(display);
            await UniTask.Yield(PlayerLoopTiming.Update);
        }
        op.allowSceneActivation = true;
        await UniTask.WaitUntil(() => op.isDone);
    }

    public void NotifySceneReady()
    {
        UI_SceneLoading.Instance?.HideAsync().Forget();
    }

    public void RequestStartRun()
    {
        RequestStartRunAsync(destroyCancellationToken).Forget();
    }

    private async UniTaskVoid RequestStartRunAsync(CancellationToken token)
    {
        try
        {
            IsNewRunPending = true;
            var rpm = RunProgressManager.Instance;
            if (rpm != null)
                await rpm.ClearAsync(rpm.ActiveSlotIndex);

            var vp = UIRootBootstrapper.Instance != null
                ? UIRootBootstrapper.Instance.GetComponentInChildren<GameStartVideoPlayer>(true)
                : null;
            if (vp != null)
                await vp.PlayAsync(token);

            RequestLoad(Define.Scene.GameScene_Ch1); // 새 런은 항상 Chapter 1 씬부터
        }
        catch (OperationCanceledException) { }
    }

    public bool ConsumeNewRunPending()
    {
        bool was = IsNewRunPending;
        IsNewRunPending = false;
        return was;
    }

    /// <summary>
    /// 저장 슬롯의 이어하기. 세션을 복원한 뒤 StageMap 씬으로 이동한다.
    /// StageMapBootstrapper는 CurrentRun.IsRunning=true를 감지해 재진입 경로로 처리한다.
    /// </summary>
    public void RequestRestoreRun(Action onFailed = null)
    {
        RequestRestoreRunAsync(onFailed).Forget();
    }

    private async UniTaskVoid RequestRestoreRunAsync(Action onFailed = null)
    {
        var rpm = RunProgressManager.Instance;
        if (rpm == null) return;

        int slot = rpm.ActiveSlotIndex;
        var save = rpm.Saves[slot];
        if (save == null || !save.hasActiveRun)
        {
            Debug.LogWarning($"[AppBootstrapper] RequestRestoreRun: slot {slot}에 유효한 저장 없음");
            onFailed?.Invoke();
            return;
        }

        // 스타트룸 미퇴장 상태에서 종료 → 선택 초기화 후 새로 시작
        if (save.isInStartRoom)
        {
            Debug.Log("[AppBootstrapper] RestoreRun: isInStartRoom=true — 세이브 초기화 후 새로 시작");
            Loadout.Clear();
            rpm.ClearAsync(slot).Forget();
            RequestLoad(Define.Scene.GameScene_Ch1);
            return;
        }

        // CharacterData SO 로드 — 이어하기 시 캐릭터 스탯 복원에 필요
        CharacterData charData = null;
        if (!string.IsNullOrEmpty(save.characterKey))
        {
            try
            {
                charData = await Managers.AddressableManager.TryLoadAssetAsync<CharacterData>(save.characterKey + "Data");
                if (charData != null)
                    Managers.CharacterData?.SetCharacterData(charData, save.characterKey);
                else
                    Debug.LogWarning($"[AppBootstrapper] RestoreRun: CharacterData '{save.characterKey}Data' 로드 실패");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AppBootstrapper] RestoreRun: CharacterData 로드 예외: {e.Message}");
            }
        }
        Loadout.SetCharacter(charData, save.characterKey);

        // WeaponSO 로드 — 실패해도 복원 흐름은 계속 진행
        WeaponSO ws0 = null, ws1 = null;

        if (!string.IsNullOrEmpty(save.weapon0PrefabKey))
        {
            try
            {
                ws0 = await Managers.AddressableManager.TryLoadAssetAsync<WeaponSO>(save.weapon0PrefabKey);
                if (ws0 != null) Loadout.SetWeaponSlot0(ws0);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AppBootstrapper] RestoreRun: 무기0 로드 실패: {e.Message}");
            }
        }

        if (!string.IsNullOrEmpty(save.weapon1PrefabKey))
        {
            try
            {
                ws1 = await Managers.AddressableManager.TryLoadAssetAsync<WeaponSO>(save.weapon1PrefabKey);
                if (ws1 != null) Loadout.SetWeaponSlot1(ws1);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AppBootstrapper] RestoreRun: 무기1 로드 실패: {e.Message}");
            }
        }

        var session = new GameRunSession();
        await session.RestoreFromSaveAsync(save, LoadTextAsset);

        if (!session.IsRunning)
        {
            Debug.LogError("[AppBootstrapper] RequestRestoreRun: 세션 복원 실패.");
            onFailed?.Invoke();
            return;
        }

        // 체크포인트 저장 시 weapon key 보존 — FromSO로 생성한 WeaponData 사용
        var w0 = ws0 != null ? WeaponData.FromSO(ws0) : null;
        var w1 = ws1 != null ? WeaponData.FromSO(ws1) : null;
        if (w0 != null || w1 != null)
            session.SaveWeaponSlots(new WeaponData[] { w0, w1 }, 0);

        BeginRun(session);
        RequestLoad(GetSceneForChapter(session.CurrentChapter));
    }

    /// <summary>
    /// StageMap에서 다음 방 진입 직전에 호출.
    /// 현재 챕터 팔레트의 MonsterSpawn 블록에서 스폰 테이블을 읽어 몬스터 풀을 백그라운드 프리웜한다.
    /// AppBootstrapper는 DDOL이므로 씬 전환 중에도 프리웜이 계속 실행된다.
    /// </summary>
    public void StartRoomPrewarm()
    {
        var paletteKey = CurrentRun?.ActiveTheme;
        if (string.IsNullOrEmpty(paletteKey)) return;
        PrewarmFromPaletteAsync(paletteKey, this.GetCancellationTokenOnDestroy()).Forget();
    }

    private static async UniTaskVoid PrewarmFromPaletteAsync(string paletteKey, System.Threading.CancellationToken ct)
    {
        try
        {
            var palette = await Managers.AddressableManager.TryLoadAssetAsync<BlockPalette>(paletteKey);
            if (palette == null || ct.IsCancellationRequested) return;

            var tileTypes = new[] { TileType.MonsterSpawn, TileType.MonsterSpawnCandidate };
            foreach (var tileType in tileTypes)
            {
                var defs = palette.GetAll(tileType);
                foreach (var def in defs)
                {
                    if (def?.prefab == null) continue;
                    var spawners = def.prefab.GetComponentsInChildren<MonsterSpawner>(true);
                    foreach (var spawner in spawners)
                    {
                        if (ct.IsCancellationRequested) return;
                        await spawner.PrewarmPoolsAsync(3, ct);
                    }
                }
            }
            Debug.Log($"[AppBootstrapper] 백그라운드 프리웜 완료 — palette={paletteKey}");
        }
        catch (System.OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogWarning($"[AppBootstrapper] 방 프리웜 실패: {e.Message}");
        }
    }

    private static UniTask<TextAsset> LoadTextAsset(string key) =>
        Managers.AddressableManager.LoadAssetAsync<TextAsset>(key);

    private GameFlow _flow;
    private SceneTransitionManager _scene;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // RunProgressManager (이어하기 저장) — 뒤끝 로그인 전부터 인스턴스 준비
        if (RunProgressManager.Instance == null)
        {
            var rpmGo = new GameObject("@RunProgressManager");
            rpmGo.AddComponent<RunProgressManager>();
        }

        // BackendGameData (유저 데이터 저장) — 로그인 전부터 인스턴스 준비
        if (BackendGameData.Instance == null)
        {
            var bgdGo = new GameObject("@BackendGameData");
            bgdGo.AddComponent<BackendGameData>();
        }

        if (initBackend && !IsBackendInitialized)
        {
            var bro = Backend.Initialize();
            if (bro.IsSuccess())
            {
                IsBackendInitialized = true;
                Debug.Log("[AppBootstrapper] Backend initialized.");
            }
            else
            {
                Debug.LogError($"[AppBootstrapper] Backend initialize failed: {bro.GetMessage()}");
            }
        }

        SystemSetup();
    }

    private static void SystemSetup()
    {
        Application.runInBackground = true;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        int width = Screen.width;
        int height = (int)(Screen.width * 9f / 16f);
        Screen.SetResolution(width, height, true);
    }

    private async void Start()
    {
        // 1) Managers 준비 보장 (서비스 로케이터)
        var mgr = Managers.Instance;
        if (mgr == null)
        {
            Debug.LogError("[AppBootstrapper] Managers.Instance is null. (IsQuitting flag?)");
            return;
        }

        // 2) 뒤끝 초기화
        if (initBackend && !IsBackendInitialized)
        {
            var bro = Backend.Initialize();
            if (bro.IsSuccess())
            {
                IsBackendInitialized = true;
                Debug.Log("[AppBootstrapper] Backend initialized.");
            }
            else
            {
                Debug.LogError($"[AppBootstrapper] Backend initialize failed: {bro.GetMessage()}");
            }
        }

        // 3) Addressables 초기화
        if (initAddressables)
        {
            var addr = Managers.AddressableManager;
            if (addr == null)
            {
                Debug.LogError("[AppBootstrapper] Managers.AddressableManager is null.");
                return;
            }

            await addr.InitAsync();
        }

        Managers.Sound?.Init();
        await InitSoundTableAsync();

        // 4-b) QuestManager 초기화 — QuestDatabase / AchievementDatabase Addressables 로드
        await InitQuestManagerAsync();

        // 5) (선택) UIRoot 확보 + UIManager에 캔버스 루트 주입
        if (autoCreateUIRoot)
        {
            await EnsureUIRootAsync();

            var uiRoot = UIRootBootstrapper.Instance;
            if (uiRoot != null)
                Managers.UI.SetRoots(uiRoot.SceneRoot, uiRoot.PopupRoot, uiRoot.OverlayRoot, uiRoot.WorldRoot);
            else
                Debug.LogWarning("[AppBootstrapper] UIRootBootstrapper not found. UIManager will use legacy root.");
        }

        // 6) (선택) Steam 로그인 → Lobby로 직행
        if (useSteamLogin)
        {
            var steamGo = new GameObject("@SteamManager");
            steamGo.AddComponent<SteamManager>();

            bool steamOk = await SteamLoginService.LoginAsync();
            if (!steamOk)
            {
                Debug.LogError("[AppBootstrapper] Steam 로그인 실패. 게임을 시작할 수 없습니다.");
                return;
            }

            await UniTask.WhenAll(
                Managers.ItemData.InitializeAsync(),
                Managers.RuneData.InitializeAsync()
            );
            startScene = Define.Scene.Lobby;
        }

        // 6-b) 디바이스 ID 자동 로그인 (Login 씬 제거 — 성공/실패 모두 Lobby로)
        if (useAutoLogin && !useSteamLogin)
        {
            bool autoOk = await DeviceAutoLoginAsync();
            if (autoOk)
            {
                IsAutoLoggedIn = true;
                // 아이템/블록 데이터는 CDN 인증 후 로드해야 하므로 로그인 성공 이후 초기화
                await UniTask.WhenAll(
                    Managers.ItemData.InitializeAsync(),
                    Managers.RuneData.InitializeAsync(),
                    RunProgressManager.Instance.LoadAsync(),
                    BackendGameData.Instance.LoadAsync()
                );
                Debug.Log("[AppBootstrapper] 자동 로그인 성공");
            }
            else
            {
                Debug.LogWarning("[AppBootstrapper] 자동 로그인 실패 → Lobby로 진입");
                // 로그인 실패 시 Addressables 폴백으로 초기화
                await UniTask.WhenAll(
                    Managers.ItemData.InitializeAsync(),
                    Managers.RuneData.InitializeAsync()
                );
            }
            if (startScene == Define.Scene.Logo)
                startScene = Define.Scene.Lobby;
        }

        // 7) (선택) Flow 시작 (SceneTransitionManager 바인딩 필수)
        if (startFlow)
        {
            _flow = new GameFlow();
            _scene = new SceneTransitionManager(this);

            _flow.BindSceneTransition(_scene);
            _flow.OnStateChanged += OnFlowStateChanged;
            _flow.RequestLoad(startScene);
        }
        else
        {
            // startFlow = false 일 때 (테스트 씬 직접 실행):
            // 씬 전환마다 UI를 자동 활성화
            SceneManager.sceneLoaded += OnSceneLoadedNoFlow;
            AutoShowUIForCurrentScene();
        }

        IsReady = true;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoadedNoFlow;
    }

    private void OnSceneLoadedNoFlow(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
    {
        AutoShowUIForCurrentScene();
    }

    private void OnFlowStateChanged(GameFlowState state)
    {
        Managers.UI.ClearOnSceneTransition();
        ApplyUIForState(state);
    }

    /// <summary>
    /// startFlow = false 환경(테스트 씬 직접 실행)에서
    /// 현재 씬 이름을 기반으로 UI를 자동 활성화합니다.
    /// 씬→상태 매핑은 GameFlow.TryGetStateForScene을 재사용합니다.
    /// </summary>
    private void AutoShowUIForCurrentScene()
    {
        var sceneName = SceneManager.GetActiveScene().name;
        if (!Enum.TryParse<Define.Scene>(sceneName, out var scene))
            return;

        if (GameFlow.TryGetStateForScene(scene, out var state))
            ApplyUIForState(state);
    }

    private void ApplyUIForState(GameFlowState state)
    {
        switch (state)
        {
            case GameFlowState.Logo:
                Managers.UI.ShowMenuUI<UI_Logo>();
                NotifySceneReady();
                break;

            case GameFlowState.Tutorial:
                // TutorialBootstrapper가 초기화 완료 후 NotifySceneReady() 호출
                break;

            case GameFlowState.BaseCamp:
                // BaseCampBootstrapper가 초기화 완료 후 NotifySceneReady() 호출
                break;

            case GameFlowState.Lobby:
                Managers.UI.ShowMenuUI<UI_Lobby>();
                NotifySceneReady();
                break;

            case GameFlowState.StageMap:
                Managers.UI.ShowMenuUI<UI_StageMap>();
                // StageMapBootstrapper가 비동기 초기화 완료 후 NotifySceneReady() 호출
                break;

            case GameFlowState.InGame:
                // GameRunBootstrapper가 비동기 초기화 완료 후 NotifySceneReady() 호출
                break;
        }
    }

    /// <summary>
    /// 디바이스 고유 ID로 자동 로그인.
    /// 계정 없으면 자동 회원가입 후 재로그인.
    /// </summary>
    private static async UniTask<bool> DeviceAutoLoginAsync()
    {
        string deviceId = SystemInfo.deviceUniqueIdentifier;
        // 뒤끝 ID 제한에 맞게 접두사 + 해시
        string id = "dev_" + deviceId;
        if (id.Length > 20) id = id.Substring(0, 20);
        string pw = deviceId;
        if (pw.Length > 20) pw = pw.Substring(0, 20);

        Debug.Log($"[AutoLogin] 디바이스 로그인 시도: {id}");

        // 1차: 로그인 시도
        var loginResult = await TryCustomLoginAsync(id, pw);
        if (loginResult) return true;

        // 2차: 계정 없으면 회원가입
        Debug.Log("[AutoLogin] 계정 없음 → 회원가입 시도");
        var signupResult = Backend.BMember.CustomSignUp(id, pw);
        if (!signupResult.IsSuccess())
        {
            Debug.LogError($"[AutoLogin] 회원가입 실패: {signupResult.GetMessage()}");
            return false;
        }

        // 3차: 재로그인
        return await TryCustomLoginAsync(id, pw);
    }

    private static UniTask<bool> TryCustomLoginAsync(string id, string pw)
    {
        var tcs = new UniTaskCompletionSource<bool>();

        Backend.BMember.CustomLogin(id, pw, callback =>
        {
            if (callback.IsSuccess())
            {
                Debug.Log($"[AutoLogin] 로그인 성공: gamerId={Backend.BMember.GetUserInfo()?.GetReturnValuetoJSON()?["row"]?["gamerId"]}");
                tcs.TrySetResult(true);
            }
            else
            {
                Debug.LogWarning($"[AutoLogin] 로그인 실패: {callback.GetStatusCode()} {callback.GetMessage()}");
                tcs.TrySetResult(false);
            }
        });

        return tcs.Task;
    }

    private async UniTask InitSoundTableAsync()
    {
        var addr = Managers.AddressableManager;
        if (addr == null) return;

        try
        {
            var table = await addr.TryLoadAssetAsync<SoundEventTableSO>("SoundEventTable");
            if (table != null)
                Managers.Sound?.SetEventTable(table);
            else
                Debug.Log("[AppBootstrapper] SoundEventTable 없음 — 이벤트 사운드 비활성");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AppBootstrapper] SoundEventTable 로드 실패: {e.Message}");
        }
    }

    private async UniTask InitQuestManagerAsync()
    {
        var addr = Managers.AddressableManager;
        if (addr == null) return;

        QuestDatabase questDb = null;
        QuestDatabase achievementDb = null;

        try { questDb       = await addr.TryLoadAssetAsync<QuestDatabase>("QuestDatabase"); }
        catch (Exception e) { Debug.LogWarning($"[AppBootstrapper] QuestDatabase 로드 실패: {e.Message}"); }

        try { achievementDb = await addr.TryLoadAssetAsync<QuestDatabase>("AchievementDatabase"); }
        catch (Exception e) { Debug.LogWarning($"[AppBootstrapper] AchievementDatabase 로드 실패: {e.Message}"); }

        if (questDb != null || achievementDb != null)
            Managers.Quest.Initialize(questDb, achievementDb);
        else
            Debug.Log("[AppBootstrapper] QuestDatabase 없음 — Quest 시스템 대기 상태 유지");
    }

    private async UniTask EnsureUIRootAsync()
    {
        if (_uiRootEnsured)
            return;

        var existing = FindObjectOfType<UIRootBootstrapper>(true);
        if (existing != null)
        {
            _uiRootEnsured = true;
            return;
        }

        Debug.LogWarning("[AppBootstrapper] UIRoot not found. Creating from Addressables...");

        var addr = Managers.AddressableManager; 
        if (addr == null)
        {
            Debug.LogError("[AppBootstrapper] EnsureUIRootAsync failed: AddressableManager is null.");
            return;
        }

        GameObject uiRootPrefab;
        try
        {
            uiRootPrefab = await addr.LoadAssetAsync<GameObject>(uiRootPrefabKey);
        }
        catch (Exception e)
        {
            Debug.LogError($"[AppBootstrapper] Failed to load UIRoot prefab. key={uiRootPrefabKey}\n{e}");
            return;
        }

        if (uiRootPrefab == null)
        {
            Debug.LogError($"[AppBootstrapper] UIRoot prefab is null. key={uiRootPrefabKey}");
            return;
        }

        var go = Instantiate(uiRootPrefab);
        go.name = "@UIRoot";
        DontDestroyOnLoad(go);

        _uiRootEnsured = true;
        Debug.Log("[AppBootstrapper] UIRoot created successfully");
    }

}
