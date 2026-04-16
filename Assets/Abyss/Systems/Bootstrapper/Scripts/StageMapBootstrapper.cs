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
    [SerializeField] private ChapterRegistry chapterRegistry;

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

        // 이미 진행 중인 런이 있으면 재사용 (전투 후 복귀 또는 챕터 전환)
        if (app.CurrentRun != null && app.CurrentRun.IsRunning)
        {
            app.CurrentRun.EnterMap();

            var points = FindObjectsOfType<StagePointUI>(true);
            app.CurrentRun.RegisterPoints(points);

            // 새 챕터 진입이면 노드 재배치
            var spm = app.CurrentRun.StagePointManager;
            if (spm != null && spm.CurrentPointId < 0)
            {
                app.CurrentRun.ResolveAllPointsAndSetStart();
            }

            foreach (var p in points)
                p.RefreshIconFromResolved();

            RefreshStageMapUI();
            ApplyChapterBackground(app.CurrentRun.CurrentChapter);
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

        var connector = FindObjectOfType<StageLineConnector>(true);
        if (connector != null)
            connector.RefreshLineStates();

        // 줌/스크롤 상태 리셋 (GameScene 복귀 시 확대 상태 방지)
        ResetMapZoom();

        // 도달 가능 노드 glow 갱신
        RefreshNodeGlow();
    }

    private void ResetMapZoom()
    {
        var scroller = FindObjectOfType<StageMapScroller>(true);
        if (scroller == null || scroller.ContentTransform == null) return;

        var content = scroller.ContentTransform;
        content.localScale = Vector3.one;

        // 현재 노드로 포커스
        var run = AppBootstrapper.Instance?.CurrentRun;
        int currentId = run?.StagePointManager?.CurrentPointId ?? -1;

        if (currentId >= 0)
        {
            var points = content.GetComponentsInChildren<StagePointUI>(true);
            foreach (var p in points)
            {
                if (p.PointId == currentId)
                {
                    scroller.FocusOn(p.GetComponent<RectTransform>());
                    return;
                }
            }
        }
    }

    private void RefreshNodeGlow()
    {
        var run = AppBootstrapper.Instance?.CurrentRun;
        if (run?.StagePointManager == null) return;

        var mgr = run.StagePointManager;
        var points = FindObjectsOfType<StagePointUI>(true);

        foreach (var p in points)
            p.SetGlow(mgr.CanMove(p.PointId));
    }

    private void ApplyChapterBackground(ChapterId chapter)
    {
        var scroller = FindObjectOfType<StageMapScroller>(true);
        if (scroller == null || scroller.ContentTransform == null) return;

        var mapPanel = scroller.ContentTransform.Find("MapPanel");
        if (mapPanel == null) return;

        var image = mapPanel.GetComponent<UnityEngine.UI.Image>();
        if (image == null) return;

        // ChapterRegistry에서 데이터 조회
        var chapterData = chapterRegistry != null ? chapterRegistry.Get(chapter) : null;

        if (chapterData != null)
        {
            if (chapterData.mapBackground != null)
                image.sprite = chapterData.mapBackground;
            image.color = chapterData.mapBackgroundTint;
        }
        else
        {
            // 레지스트리 없을 때 폴백
            image.color = chapter switch
            {
                ChapterId.Chapter1 => new Color(0.75f, 0.70f, 0.55f),
                ChapterId.Chapter2 => new Color(0.55f, 0.60f, 0.70f),
                ChapterId.Chapter3 => new Color(0.50f, 0.45f, 0.55f),
                ChapterId.Chapter4 => new Color(0.70f, 0.55f, 0.45f),
                ChapterId.Chapter5 => new Color(0.40f, 0.40f, 0.50f),
                _ => Color.white,
            };
        }
    }

    private static UniTask<TextAsset> LoadTextAsset(string key) =>
        Managers.AddressableManager.LoadAssetAsync<TextAsset>(key);
}
