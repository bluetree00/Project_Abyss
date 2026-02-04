using UnityEngine;
using Cysharp.Threading.Tasks;

public sealed class GameRunBootstrapper : MonoBehaviour
{
    [SerializeField] private StageMapSpawner spawner;

    // ✅ 플레이어 프리팹 키(캐릭터 선택이면 여기 대신 세션/로비에서 받아오면 됨)
    [SerializeField] private string playerPrefabKey = "Knight";

    // ✅ 스폰 위치(없으면 Vector3.zero)
    [SerializeField] private Transform playerSpawnPoint;

    private StagePointUI[] _points;

    private void Awake()
    {
        Bind();
    }

    public void Bind()
    {
        if (spawner == null)
            spawner = FindObjectOfType<StageMapSpawner>(true);

        Managers.GameRun.BindSpawner(spawner);

        if (Managers.GameRun.IsRunning && Managers.GameRun.StagePointManager != null)
        {
            _points = FindObjectsOfType<StagePointUI>(true);
            Managers.GameRun.RegisterPoints(_points);
        }
    }

    public async UniTask StartRunAsync(ChapterId chapter)
    {
        // 1) 런 로직 초기화
        await Managers.GameRun.StartNewRunAsync(chapter);

        if (!Managers.GameRun.IsRunning || Managers.GameRun.RoomManager == null || !Managers.GameRun.RoomManager.IsInitialized)
            return;

        // 2) 씬 UI 등록 (구독 먼저!)
        _points = FindObjectsOfType<StagePointUI>(true);
        Managers.GameRun.RegisterPoints(_points);

        // 3) 전체 Resolve + Start 세팅
        Managers.GameRun.ResolveAllPointsAndSetStart();

        // 4) 시작 맵 스폰
        Managers.GameRun.BindSpawner(spawner);
        Managers.GameRun.SpawnCurrentPointMap();

        // 5) ✅ 플레이어 스폰 + 런에 바인딩
        var player = await SpawnPlayerAsync(playerPrefabKey);
        Managers.GameRun.BindPlayer(player);
    }

    private async UniTask<PlayerController> SpawnPlayerAsync(string prefabKey)
    {
        if (string.IsNullOrEmpty(prefabKey))
        {
            Debug.LogError("[GameRunBootstrapper] SpawnPlayerAsync failed: prefabKey is empty");
            return null;
        }

        // 1) Addressables에서 프리팹 로드
        var prefab = await Managers.AddressableManager.LoadAssetAsync<GameObject>(prefabKey);
        if (prefab == null)
        {
            Debug.LogError($"[GameRunBootstrapper] Player prefab load failed: key={prefabKey}");
            return null;
        }

        // 2) Instantiate
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
