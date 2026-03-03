using UnityEngine;
using Cysharp.Threading.Tasks;

public sealed class GameRunBootstrapper : MonoBehaviour
{
    public static GameRunBootstrapper Instance { get; private set; }

    [SerializeField] private StageMapSpawner spawner;
    [SerializeField] private string playerPrefabKey = "Knight";
    [SerializeField] private Transform playerSpawnPoint;

    [Header("In-Run UI (Optional)")]
    [SerializeField] private GameObject mapUIRoot;      // RunState.Map 일 때 활성화
    [SerializeField] private GameObject standbyUIRoot;  // Standby / GridSynergy 일 때 활성화

    private StagePointUI[] _points;

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

        _run = new GameRunSession();
        _run.OnRunStateChanged += HandleRunStateChanged;

        // HUD가 이미 존재할 수 있으니 선-바인딩 (안전)
        UIRootBootstrapper.Instance?.BindHudToRun(_run);

        Bind();
    }

    private void Start()
    {
        Bind();
    }

    private void HandleRunStateChanged(GameRunSession.RunState state)
    {
        if (mapUIRoot != null)
            mapUIRoot.SetActive(state == GameRunSession.RunState.Map);

        if (standbyUIRoot != null)
            standbyUIRoot.SetActive(
                state == GameRunSession.RunState.Standby ||
                state == GameRunSession.RunState.GridSynergy);
    }

    private void OnDestroy()
    {
        if (_run != null)
            _run.OnRunStateChanged -= HandleRunStateChanged;

        if (ReferenceEquals(Instance, this))
            Instance = null;

        _run = null;
    }

    public void Bind()
    {
        if (_run == null)
        {
            Debug.LogWarning("[GameRunBootstrapper] Bind ignored: run is null.");
            return;
        }

        if (spawner == null)
            spawner = FindObjectOfType<StageMapSpawner>(true);

        if (spawner == null)
        {
            Debug.LogWarning("[GameRunBootstrapper] StageMapSpawner not found yet.");
            return;
        }

        _run.BindSpawner(spawner);

        if (_run.IsRunning && _run.StagePointManager != null)
        {
            _points = FindObjectsOfType<StagePointUI>(true);
            _run.RegisterPoints(_points);
        }
    }

    public async UniTask StartRunAsync(ChapterId chapter)
    {
        var run = _run;
        if (run == null)
        {
            Debug.LogError("[GameRunBootstrapper] StartRunAsync failed: run is null.");
            return;
        }

        // Spawner 확보/바인딩 보장
        if (spawner == null)
            spawner = FindObjectOfType<StageMapSpawner>(true);

        if (spawner == null)
        {
            Debug.LogError("[GameRunBootstrapper] StartRunAsync failed: StageMapSpawner not found.");
            return;
        }

        run.BindSpawner(spawner);

        // 1) 런 로직 초기화
        await run.StartNewRunAsync(chapter, LoadTextAsset);

        static UniTask<TextAsset> LoadTextAsset(string key) =>
            Managers.AddressableManager.LoadAssetAsync<TextAsset>(key);

        if (!run.IsRunning || run.RoomManager == null || !run.RoomManager.IsInitialized)
        {
            Debug.LogWarning("[GameRunBootstrapper] StartRunAsync aborted: RoomManager not ready.");
            return;
        }

        // 2) 씬 UI 등록
        _points = FindObjectsOfType<StagePointUI>(true);
        run.RegisterPoints(_points);

        // 3) Resolve + Start 세팅
        run.ResolveAllPointsAndSetStart();

        // 4) 시작 맵 스폰
        run.SpawnCurrentPointMap();

        UIRootBootstrapper.Instance?.BindHudToRun(run);

        // 5) 플레이어 스폰 + 런에 바인딩 + 세션 주입
        var player = await SpawnPlayerAsync(playerPrefabKey);
        if (player != null)
        {
            run.BindPlayer(player);
            player.BindSession(run);
        }
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
