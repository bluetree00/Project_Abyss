using UnityEngine;
using Cysharp.Threading.Tasks;

public sealed class GameRunBootstrapper : MonoBehaviour
{
    [SerializeField] private StageMapSpawner spawner;
    [SerializeField] private string playerPrefabKey = "Knight";
    [SerializeField] private Transform playerSpawnPoint;

    private StagePointUI[] _points;

    // ✅ Run을 이 부트스트래퍼가 소유
    private GameRunManager _run;

    private void Awake()
    {
        // ✅ Run 생성 + 주입 (중요!)
        _run = new GameRunManager();
        Managers.SetGameRun(_run);

        // 씬 오브젝트 바인딩은 Awake에서도 가능하지만,
        // 생성 순서가 애매하면 Start에서 한 번 더 Bind 해도 됨.
        Bind();
    }

    private void Start()
    {
        // 안전하게 한 번 더(Spawner가 늦게 생기는 씬 대비)
        Bind();
    }

    private void OnDestroy()
    {
        // 내가 주입한 Run이면 해제
        if (ReferenceEquals(Managers.GameRun, _run))
            Managers.SetGameRun(null);

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
        // ✅ 로컬 캐시 + null 방어 (Managers.GameRun을 직접 계속 쓰지 말기)
        var run = _run ?? Managers.GameRun;
        if (run == null)
        {
            Debug.LogError("[GameRunBootstrapper] StartRunAsync failed: run is null. (SetGameRun not called?)");
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
        await run.StartNewRunAsync(chapter);

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

        // 5) 플레이어 스폰 + 런에 바인딩
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