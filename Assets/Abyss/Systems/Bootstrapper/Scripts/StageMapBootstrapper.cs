using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// StageMap 씬 전용 부트스트래퍼.
/// - 새 런 시작 시 GameRunSession을 초기화하고 AppBootstrapper에 등록
/// - 기존 런이 있으면 재사용 (GameScene → StageMap 복귀 시)
/// - 맵 UI 초기화만 담당 (전투/플레이어 스폰은 GameRunBootstrapper 담당)
/// - 챕터 데이터 기반으로 동적 노드 생성
/// </summary>
public sealed class StageMapBootstrapper : MonoBehaviour
{
    public static StageMapBootstrapper Instance { get; private set; }

    [Header("챕터")]
    [SerializeField] private ChapterId startChapter = ChapterId.Chapter1;
    [SerializeField] private ChapterRegistry chapterRegistry;

    [Header("노드 생성")]
    [SerializeField] private StageNodeIconMap iconMap;

    [Header("맵 배경")]
    [SerializeField] private Sprite defaultMapBackground;

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

            GenerateAndLayoutNodes(app.CurrentRun.CurrentChapter);

            var points = FindObjectsOfType<StagePointUI>(true);
            app.CurrentRun.RegisterPoints(points);

            var spm = app.CurrentRun.StagePointManager;
            if (spm != null && spm.CurrentPointId < 0)
                app.CurrentRun.ResolveAllPointsAndSetStart();

            foreach (var p in points)
                p.RefreshIconFromResolved();

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
        // 챕터 데이터 서버 로드
        var chapterData = Managers.ChapterData;
        if (chapterData != null && !chapterData.IsInitialized)
        {
            try { await chapterData.InitializeAsync(); }
            catch (System.Exception e) { Debug.LogWarning($"[StageMapBootstrapper] ChapterData 예외: {e.Message}"); }
        }

        var session = new GameRunSession();
        app.BeginRun(session);

        await session.StartNewRunAsync(startChapter, LoadTextAsset);

        if (!session.IsRunning)
        {
            Debug.LogError("[StageMapBootstrapper] GameRunSession 초기화 실패.");
            app.EndRun();
            return;
        }

        GenerateAndLayoutNodes(startChapter);

        var points = FindObjectsOfType<StagePointUI>(true);
        session.RegisterPoints(points);
        session.ResolveAllPointsAndSetStart();

        RefreshStageMapUI();

        Debug.Log("[StageMapBootstrapper] 새 런 시작 완료.");
    }

    // ── 노드 생성 ──

    /// <summary>기존 노드 제거 → 동적 노드 생성 + 배치 → 라인 생성.</summary>
    private void GenerateAndLayoutNodes(ChapterId chapter)
    {
        var scroller = FindObjectOfType<StageMapScroller>(true);
        if (scroller == null || scroller.ContentTransform == null)
        {
            Debug.LogError("[StageMapBootstrapper] StageMapScroller or ContentTransform not found.");
            return;
        }

        var contentParent = scroller.ContentTransform;

        // 기존 노드에서 템플릿 캡처 후 즉시 제거
        var existingNodes = contentParent.GetComponentsInChildren<StagePointUI>(true);
        GameObject template = null;
        if (existingNodes.Length > 0)
        {
            template = Instantiate(existingNodes[0].gameObject, contentParent, false);
            template.SetActive(false);
            template.name = "NodeTemplate";
        }
        foreach (var node in existingNodes)
            DestroyImmediate(node.gameObject);

        // 챕터 데이터에서 층 설정 조회 (서버 → SO 폴백)
        int middleLayers = 5;
        int peakLayer = 3;

        var serverEntry = Managers.ChapterData?.Get(chapter);
        if (serverEntry != null && serverEntry.total_layers > 0)
        {
            middleLayers = serverEntry.total_layers - 2;
            peakLayer = serverEntry.peak_layer;
        }
        else if (chapterRegistry != null)
        {
            var data = chapterRegistry.Get(chapter);
            if (data != null)
            {
                middleLayers = data.middleLayers;
                peakLayer = data.peakLayer;
            }
        }

        // 그래프 생성 (Refined: X좌표 단조 매칭)
        IStageMapGenerator generator = new RefinedStageMapGenerator();
        var graph = generator.Generate(new StageMapGenerationRequest(middleLayers, peakLayer));

        // 그래프 데이터를 기반으로 UI 노드 인스턴스화
        InstantiateNodesFromGraph(contentParent, graph, template);

        // 레이아웃 패턴 설정
        var layout = scroller.GetComponent<StageNodeLayout>();
        if (layout != null)
            layout.SetLayerSizes(graph.MiddlePattern);

        // 콘텐츠 크기 조정: 가장 넓은 층(피크) 기준. peakLayer는 "층 번호"이고
        // 해당 층 노드 수는 MiddlePattern의 최댓값(=peakLayer+1)임. 한 칸 부족 버그 회피용.
        int totalLayers = middleLayers + 2;
        float layerSpacing = 800f;
        float nodeSpacingX = 500f;
        float marginY = 800f;
        float marginX = 1200f;

        int maxNodesInLayer = 1;
        if (graph.MiddlePattern != null)
        {
            for (int i = 0; i < graph.MiddlePattern.Length; i++)
                if (graph.MiddlePattern[i] > maxNodesInLayer)
                    maxNodesInLayer = graph.MiddlePattern[i];
        }

        float height = totalLayers * layerSpacing + marginY;
        float width = Mathf.Max(2400f, (maxNodesInLayer - 1) * nodeSpacingX + marginX);
        scroller.SetContentSize(width, height);

        // 배경 적용
        ApplyMapBackground(scroller, chapter);

        // 레이아웃 + 라인 + 포커스
        scroller.RebuildMap();
    }

    /// <summary>Generator가 만든 그래프 데이터로 StagePointUI를 인스턴스화하고 연결 적용.</summary>
    private void InstantiateNodesFromGraph(Transform parent, StageMapGraph graph, GameObject template)
    {
        var uiByPointId = new Dictionary<int, StagePointUI>(graph.Nodes.Count);

        foreach (var node in graph.Nodes)
        {
            var ui = CreateNode(parent, template, node);
            uiByPointId[node.PointId] = ui;
        }

        foreach (var node in graph.Nodes)
        {
            if (!uiByPointId.TryGetValue(node.PointId, out var ui)) continue;
            foreach (var nextId in node.NextPointIds)
                ui.AddNextPointId(nextId);
        }

        if (template != null)
            Destroy(template);
    }

    private StagePointUI CreateNode(Transform parent, GameObject template, StageMapNode node)
    {
        GameObject go;
        if (template != null)
        {
            go = Instantiate(template, parent, false);
            go.name = $"Node_{node.PointId}";
        }
        else
        {
            go = new GameObject($"Node_{node.PointId}",
                typeof(RectTransform), typeof(UnityEngine.UI.Image),
                typeof(UnityEngine.UI.Button), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
        }

        go.SetActive(true);

        // 템플릿은 씬/프리팹의 기존 노드를 복제한 것이라 scale/position/anchor가
        // 오염돼 있을 수 있음. Layout이 위치를 결정할 수 있도록 기준값으로 리셋.
        var rt = go.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            if (template == null)
                rt.sizeDelta = new Vector2(80f, 80f);
        }

        var point = go.GetComponent<StagePointUI>();
        if (point == null)
            point = go.AddComponent<StagePointUI>();

        point.Init(node.PointId, node.Stage, node.Normal, iconMap);
        point.SetLayerMeta(node.LayerIndex, node.IndexInLayer);
        return point;
    }

    // ── UI 갱신 ──

    private void RefreshStageMapUI()
    {
        var ui = FindObjectOfType<UI_StageMap>(true);
        if (ui != null)
            ui.RefreshStageMap();

        var connector = FindObjectOfType<StageLineConnector>(true);
        if (connector != null)
            connector.RefreshLineStates();

        var scroller = FindObjectOfType<StageMapScroller>(true);
        if (scroller != null)
        {
            if (scroller.ContentTransform != null)
                scroller.ContentTransform.localScale = Vector3.one;
            scroller.FocusOnCurrentNode();
        }

        RefreshNodeGlow();
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

    private void ApplyMapBackground(StageMapScroller scroller, ChapterId chapter)
    {
        if (scroller == null || scroller.ContentTransform == null) return;

        var mapPanel = scroller.ContentTransform.Find("MapPanel");
        if (mapPanel == null) return;

        var image = mapPanel.GetComponent<UnityEngine.UI.Image>();
        if (image == null) return;

        // 챕터별 배경 우선, 없으면 기본 배경
        var chapterData = chapterRegistry != null ? chapterRegistry.Get(chapter) : null;

        if (chapterData != null && chapterData.mapBackground != null)
        {
            image.sprite = chapterData.mapBackground;
            image.color = chapterData.mapBackgroundTint;
        }
        else if (defaultMapBackground != null)
        {
            image.sprite = defaultMapBackground;
            image.color = Color.white;
        }
        else
        {
            // 폴백: Addressable에서 기본 배경 로드
            LoadDefaultBackgroundAsync(image).Forget();
        }
    }

    private static async UniTaskVoid LoadDefaultBackgroundAsync(UnityEngine.UI.Image image)
    {
        try
        {
            var sprite = await Managers.AddressableManager.LoadAssetAsync<Sprite>("map_3");
            if (sprite != null && image != null)
            {
                image.sprite = sprite;
                image.color = Color.white;
                return;
            }

            // Sprite 실패 시 Texture2D로 폴백
            var tex = await Managers.AddressableManager.LoadAssetAsync<Texture2D>("map_3");
            if (tex != null && image != null)
            {
                image.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), 100f);
                image.color = Color.white;
            }
        }
        catch (System.OperationCanceledException) { }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[StageMapBootstrapper] 기본 배경 로드 실패: {e.Message}");
        }
    }

    private static UniTask<TextAsset> LoadTextAsset(string key) =>
        Managers.AddressableManager.LoadAssetAsync<TextAsset>(key);
}
