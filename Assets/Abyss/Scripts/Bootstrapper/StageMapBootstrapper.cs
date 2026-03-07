using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// StageMap 씬 전용 부트스트래퍼.
/// - 새 런 시작 시 GameRunSession을 초기화하고 AppBootstrapper에 등록
/// - 기존 런이 있으면 재사용 (GameScene → StageMap 복귀 시)
/// - 맵 UI 초기화만 담당 (전투/플레이어 스폰은 GameRunBootstrapper 담당)
/// </summary>
public sealed class StageMapBootstrapper : MonoBehaviour
{
    public static StageMapBootstrapper Instance { get; private set; }

    [SerializeField] private ChapterId startChapter = ChapterId.Chapter1;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    private async void Start()
    {
        var app = AppBootstrapper.Instance;
        if (app == null)
        {
            Debug.LogError("[StageMapBootstrapper] AppBootstrapper not found.");
            return;
        }

        if (!app.IsReady)
        {
            Debug.LogWarning("[StageMapBootstrapper] AppBootstrapper not ready yet. Waiting...");
            await UniTask.WaitUntil(() => app.IsReady);
        }

        // 이미 진행 중인 런이 있으면 재사용 (전투 후 복귀)
        if (app.CurrentRun != null && app.CurrentRun.IsRunning)
        {
            app.CurrentRun.EnterMap();
            RefreshStageMapUI();
            app.NotifySceneReady();
            return;
        }

        // 새 런 시작
        await StartNewRunAsync(app);
        app.NotifySceneReady();
    }

    private async UniTask StartNewRunAsync(AppBootstrapper app)
    {
        var session = new GameRunSession();
        app.BeginRun(session);

        await session.StartNewRunAsync(startChapter, LoadTextAsset);

        if (!session.IsRunning)
        {
            Debug.LogError("[StageMapBootstrapper] GameRunSession 초기화 실패.");
            app.EndRun();
            return;
        }

        var points = FindObjectsOfType<StagePointUI>(true);
        session.RegisterPoints(points);
        session.ResolveAllPointsAndSetStart();

        RefreshStageMapUI();

        Debug.Log("[StageMapBootstrapper] 새 런 시작 완료.");
    }

    private void RefreshStageMapUI()
    {
        var ui = FindObjectOfType<UI_StageMap>(true);
        if (ui != null)
            ui.RefreshStageMap();
    }

    private static UniTask<TextAsset> LoadTextAsset(string key) =>
        Managers.AddressableManager.LoadAssetAsync<TextAsset>(key);
}
