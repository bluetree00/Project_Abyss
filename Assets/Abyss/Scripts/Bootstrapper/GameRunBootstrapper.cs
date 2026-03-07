using UnityEngine;
using Cysharp.Threading.Tasks;

public sealed class GameRunBootstrapper : MonoBehaviour
{
    public static GameRunBootstrapper Instance { get; private set; }

    [SerializeField] private string playerPrefabKey = "Knight";
    [SerializeField] private Transform playerSpawnPoint;
    [SerializeField] private Transform mapRoot;

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

        _run.OnMapSpawnRequested += OnMapSpawnRequestedHandler;
    }

    private async void Start()
    {
        if (_run != null && _run.IsRunning)
            await StartCombatAsync();

        AppBootstrapper.Instance?.NotifySceneReady();
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;

        if (_run != null)
            _run.OnMapSpawnRequested -= OnMapSpawnRequestedHandler;

        if (_currentMapGO != null && Managers.AddressableManager != null)
        {
            Managers.AddressableManager.ReleaseInstance(_currentMapGO);
            _currentMapGO = null;
        }

        _run = null;
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

            _currentMapGO = await Managers.AddressableManager.InstantiateAsync(prefabKey, mapRoot);
        }
        finally
        {
            _isSpawning = false;
        }
    }

    /// <summary>
    /// StageMap → GameScene 전환 후 호출.
    /// 이미 실행 중인 런의 선택된 포인트 맵 스폰 + 플레이어 스폰만 수행합니다.
    /// </summary>
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

        var player = await SpawnPlayerAsync(playerPrefabKey);
        if (player != null)
            run.BindPlayer(player);
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

        var player = await SpawnPlayerAsync(playerPrefabKey);
        if (player != null)
            run.BindPlayer(player);
    }

    private async UniTask<PlayerController> SpawnPlayerAsync(string prefabKey)
    {
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

        return player;
    }
}
