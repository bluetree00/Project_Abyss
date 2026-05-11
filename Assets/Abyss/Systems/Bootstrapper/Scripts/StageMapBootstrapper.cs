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

    [Tooltip("동적 노드 생성용 베이스 프리팹. 설정되면 씬에 수동 배치된 노드는 모두 제거되고 " +
             "이 프리팹으로만 동적 노드를 찍어낸다. 미설정 시 씬의 첫 노드를 런타임 템플릿으로 복제하는 구버전 경로(fallback).")]
    [SerializeField] private GameObject nodeTemplatePrefab;

    [Header("맵 배경")]
    [SerializeField] private Sprite defaultMapBackground;

    [Header("맵 배경 장식 (폴백)")]
    [Tooltip("ChapterData에 decorationSprites 없을 때 사용할 기본 장식 스프라이트 목록.")]
    [SerializeField] private Sprite[] defaultDecorationSprites;

    [Header("지형 군집")]
    [Tooltip("맵에 배치할 지형 군집 개수. 각 군집은 같은 스프라이트를 사용해 숲·바위구역처럼 표현.")]
    [SerializeField] private int terrainZoneCount = 14;
    [Tooltip("군집당 스프라이트 개수.")]
    [SerializeField] private int decorationsPerZone = 11;
    [Tooltip("군집 반경(픽셀). 군집 중심에서 이 반경 내에 스프라이트를 배치.")]
    [SerializeField] private float terrainZoneRadius = 420f;
    [Tooltip("노드 주위 회피 반경. 이 반경 안쪽엔 군집 중심·스프라이트를 배치 안 함.")]
    [SerializeField] private float decorationNodeClearance = 160f;
    [Tooltip("군집 내 스프라이트 간 최소 간격. 작을수록 자연스럽게 겹침.")]
    [SerializeField] private float intraZoneMinSpacing = 22f;
    [Tooltip("장식 최대 픽셀 크기. 스프라이트 원본이 이보다 크면 축소.")]
    [SerializeField] private float decorationMaxSize = 65f;
    [Tooltip("장식 스케일 범위 (min~max). 군집 중심에 가까울수록 크게 렌더.")]
    [SerializeField] private Vector2 decorationScaleRange = new Vector2(0.75f, 1.4f);
    [Tooltip("장식 회전 범위 (±도).")]
    [SerializeField] private float decorationMaxRotation = 25f;

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
            PlayChapterBgm(app.CurrentRun.CurrentChapter);

            // [DIAG] 재진입 시점의 상태 로그 — 그래프 캐시 유지 여부 확인
            var diagRun = app.CurrentRun;
            int diagCtxCount = diagRun.StagePointManager?.Contexts.Count ?? 0;
            int diagCurPt = diagRun.StagePointManager?.CurrentPointId ?? -1;
            int diagGraphNodes = diagRun.CachedStageGraph?.Nodes?.Count ?? 0;
            Debug.Log($"[StageMapBootstrapper] 재진입 — chapter={diagRun.CurrentChapter} " +
                      $"CachedGraph={(diagRun.CachedStageGraph != null ? $"OK({diagGraphNodes}노드)" : "NULL")} " +
                      $"Contexts={diagCtxCount} CurrentPointId={diagCurPt}");

            // 재진입: 캐시된 그래프가 있으면 Generator 재실행 없이 UI만 재생성해
            // 방문 기록·노드 연결·Resolve 결과가 유지되도록 한다.
            GenerateAndLayoutNodes(app.CurrentRun.CurrentChapter, app.CurrentRun.CachedStageGraph);

            var points = FindObjectsOfType<StagePointUI>(true);
            app.CurrentRun.RegisterPoints(points);

            var spm = app.CurrentRun.StagePointManager;
            if (spm != null && spm.CurrentPointId < 0)
                app.CurrentRun.ResolveAllPointsAndSetStart();

            foreach (var p in points)
                p.RefreshIconFromResolved();

            RefreshStageMapUI();
            app.NotifySceneReady();

            var introScroller = FindObjectOfType<StageMapScroller>(true);
            if (introScroller != null)
                await introScroller.PlayCurrentNodeZoomIntroAsync(this.GetCancellationTokenOnDestroy());
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

        // 챕터→테마 해석을 위해 레지스트리를 StartNewRunAsync 이전에 주입
        session.BindChapterRegistry(chapterRegistry);

        await session.StartNewRunAsync(startChapter, LoadTextAsset);

        if (!session.IsRunning)
        {
            Debug.LogError("[StageMapBootstrapper] GameRunSession 초기화 실패.");
            app.EndRun();
            return;
        }

        GenerateAndLayoutNodes(startChapter);
        PlayChapterBgm(startChapter);

        var points = FindObjectsOfType<StagePointUI>(true);
        session.RegisterPoints(points);
        session.ResolveAllPointsAndSetStart();

        RefreshStageMapUI();

        Debug.Log("[StageMapBootstrapper] 새 런 시작 완료.");
    }

    // ── 노드 생성 ──

    /// <summary>
    /// 기존 노드 제거 → (existingGraph이 있으면 그걸 재사용, 없으면 Generator로 신규 그래프 생성)
    /// → UI 노드 인스턴스화 + 레이아웃 + 라인. 신규 생성 시 세션에 그래프를 캐시한다.
    /// </summary>
    private void GenerateAndLayoutNodes(ChapterId chapter, StageMapGraph existingGraph = null)
    {
        var scroller = FindObjectOfType<StageMapScroller>(true);
        if (scroller == null || scroller.ContentTransform == null)
        {
            Debug.LogError("[StageMapBootstrapper] StageMapScroller or ContentTransform not found.");
            return;
        }

        var contentParent = scroller.ContentTransform;

        // 템플릿 결정:
        //   ① nodeTemplatePrefab 설정 → prefab 자산 직접 사용 (권장 경로). 씬 노드 전부 제거.
        //   ② 미설정 → 씬 첫 노드 복제본을 런타임 템플릿으로 사용 (구버전 fallback).
        //   ③ 둘 다 없음 → 노드는 투명 raw GameObject로 생성되어 "화면에서 안 보이는" 사고 발생.
        //                 명확한 에러로 빠르게 진단할 수 있도록 early return.
        var existingNodes = contentParent.GetComponentsInChildren<StagePointUI>(true);

        if (nodeTemplatePrefab == null && existingNodes.Length == 0)
        {
            Debug.LogError(
                "[StageMapBootstrapper] 노드 템플릿 소스 없음. 노드가 생성돼도 시각적으로 투명해 보이지 않습니다.\n" +
                "  해결책 ①: 메뉴 [Abyss/Setup/Extract StageNode Template Prefab] 실행 → prefab 자동 추출·할당\n" +
                "  해결책 ②: Inspector의 StageMapBootstrapper.nodeTemplatePrefab 필드에 NodeTemplate 프리팹을 드래그\n" +
                "  해결책 ③: 씬 StageMapRoot 아래에 StagePointUI가 달린 노드를 최소 1개 배치",
                this);
            return;
        }

        GameObject template = null;
        GameObject sceneTemplateCopy = null;

        if (nodeTemplatePrefab != null)
        {
            template = nodeTemplatePrefab;
        }
        else if (existingNodes.Length > 0)
        {
            sceneTemplateCopy = Instantiate(existingNodes[0].gameObject, contentParent, false);
            sceneTemplateCopy.SetActive(false);
            sceneTemplateCopy.name = "NodeTemplate";
            template = sceneTemplateCopy;
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

        // 그래프: 캐시 우선. 없으면 새로 생성하고 세션에 캐시.
        StageMapGraph graph;
        if (existingGraph != null)
        {
            graph = existingGraph;
            Debug.Log($"[StageMapBootstrapper] 캐시된 그래프 사용 — {graph.Nodes.Count}노드");
        }
        else
        {
            IStageMapGenerator generator = new RefinedStageMapGenerator();
            graph = generator.Generate(new StageMapGenerationRequest(middleLayers, peakLayer));
            AppBootstrapper.Instance?.CurrentRun?.CacheStageGraph(graph);
            Debug.Log($"[StageMapBootstrapper] 새 그래프 생성·캐시 — {graph.Nodes.Count}노드");
        }

        // 그래프 데이터를 기반으로 UI 노드 인스턴스화
        InstantiateNodesFromGraph(contentParent, graph, template);

        // 씬 복제 템플릿만 파괴. prefab 자산은 절대 Destroy 하지 말 것.
        // DestroyImmediate — 같은 프레임에 이어지는 ApplyLayout이 GetComponentsInChildren(true)로
        // inactive까지 수집하므로, Destroy(지연 파괴)를 쓰면 sceneTemplateCopy가 layer 0에 섞여
        // Start 노드 X가 중앙에서 벗어나는 문제가 생긴다.
        if (sceneTemplateCopy != null)
            DestroyImmediate(sceneTemplateCopy);

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

        // 레이아웃 + 라인 + 포커스 (노드 위치가 여기서 확정됨)
        scroller.RebuildMap();

        // 장식 스캐터: 노드 위치 기반 회피. RebuildMap 이후에 호출해야
        // anchoredPosition이 레이아웃 결과로 채워진 상태에서 회피 가능.
        // 장식 레이어 자체는 MapPanel 직후 sibling에 삽입해 노드보다 아래에 렌더됨.
        PopulateMapDecorations(scroller, chapter, width, height);
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

        // 주의: template 파괴는 호출자(GenerateAndLayoutNodes)가 sceneTemplateCopy만 선별해 처리한다.
        //       여기서 무조건 Destroy(template)하면 prefab 자산을 파괴할 수 있어 절대 금지.
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
                rt.sizeDelta = new Vector2(120f, 120f);
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
            scroller.FocusOnStartNode();
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
        else if (chapterData != null && !string.IsNullOrEmpty(chapterData.mapBackgroundKey))
        {
            LoadChapterBackgroundAsync(image, chapterData.mapBackgroundKey, chapterData.mapBackgroundTint).Forget();
        }
        else
        {
            // 폴백: Addressable에서 기본 배경 로드
            LoadDefaultBackgroundAsync(image).Forget();
        }
    }

    private void PlayChapterBgm(ChapterId chapter)
    {
        string bgmKey = null;

        var serverEntry = Managers.ChapterData?.Get(chapter);
        if (serverEntry != null && !string.IsNullOrEmpty(serverEntry.bgm_key))
            bgmKey = serverEntry.bgm_key;

        if (string.IsNullOrEmpty(bgmKey) && chapterRegistry != null)
        {
            var chapterData = chapterRegistry.Get(chapter);
            if (chapterData != null && !string.IsNullOrEmpty(chapterData.bgmKey))
                bgmKey = chapterData.bgmKey;
        }

        if (string.IsNullOrEmpty(bgmKey))
            return;

        Managers.Sound?.PlayBgmAsync(bgmKey).Forget();
    }

    /// <summary>
    /// 배경 위·노드 아래 레이어에 장식 스프라이트를 스캐터 배치.
    /// ChapterDataSO.decorationSprites 우선, 없으면 Bootstrapper의 defaultDecorationSprites 사용.
    /// 각 노드 주위 decorationNodeClearance 반경을 회피, 군집 내부는 intraZoneMinSpacing 유지.
    /// 호출 시점: scroller.RebuildMap() 이후 (노드 anchoredPosition이 레이아웃으로 채워진 상태).
    /// </summary>
    private void PopulateMapDecorations(StageMapScroller scroller, ChapterId chapter, float contentWidth, float contentHeight)
    {
        if (scroller == null || scroller.ContentTransform == null) return;
        if (terrainZoneCount <= 0 || decorationsPerZone <= 0) return;

        var content = scroller.ContentTransform;

        // 스프라이트 소스 결정: 챕터 데이터 우선
        Sprite[] sprites = null;
        var chapterData = chapterRegistry != null ? chapterRegistry.Get(chapter) : null;
        if (chapterData != null && chapterData.decorationSprites != null && chapterData.decorationSprites.Length > 0)
            sprites = chapterData.decorationSprites;
        else if (defaultDecorationSprites != null && defaultDecorationSprites.Length > 0)
            sprites = defaultDecorationSprites;

        if (sprites == null || sprites.Length == 0) return;

        // 전용 장식 컨테이너 (MapPanel 바로 다음 sibling = 노드들보다 아래 렌더)
        var layerT = content.Find("MapDecorLayer");
        RectTransform layerRT;
        if (layerT == null)
        {
            var layerGO = new GameObject("MapDecorLayer", typeof(RectTransform));
            layerRT = layerGO.GetComponent<RectTransform>();
            layerRT.SetParent(content, false);
            layerRT.anchorMin = new Vector2(0.5f, 0.5f);
            layerRT.anchorMax = new Vector2(0.5f, 0.5f);
            layerRT.pivot = new Vector2(0.5f, 0.5f);
            layerRT.anchoredPosition = Vector2.zero;
            layerRT.sizeDelta = new Vector2(contentWidth, contentHeight);

            var mapPanel = content.Find("MapPanel");
            int targetIdx = (mapPanel != null) ? mapPanel.GetSiblingIndex() + 1 : 0;
            layerRT.SetSiblingIndex(targetIdx);
        }
        else
        {
            layerRT = (RectTransform)layerT;
            layerRT.sizeDelta = new Vector2(contentWidth, contentHeight);
            for (int i = layerT.childCount - 1; i >= 0; i--)
                DestroyImmediate(layerT.GetChild(i).gameObject);
        }

        // 노드 위치 수집
        var nodeUIs = content.GetComponentsInChildren<StagePointUI>(true);
        var nodePositions = new List<Vector2>(nodeUIs.Length);
        for (int i = 0; i < nodeUIs.Length; i++)
        {
            var rt = nodeUIs[i].GetComponent<RectTransform>();
            if (rt != null) nodePositions.Add(rt.anchoredPosition);
        }

        float padX = 80f;
        float padY = 80f;
        float halfW = Mathf.Max(0f, contentWidth * 0.5f - padX);
        float halfH = Mathf.Max(0f, contentHeight * 0.5f - padY);
        if (halfW <= 0f || halfH <= 0f) return;

        float nodeClearSq     = decorationNodeClearance * decorationNodeClearance;
        float intraSpacingSq  = intraZoneMinSpacing * intraZoneMinSpacing;
        // 군집 중심끼리는 반경의 45% 이상 떨어지도록 (군집 수가 많으므로 완화)
        float zoneSepSq       = (terrainZoneRadius * 0.45f) * (terrainZoneRadius * 0.45f);

        // ── 1단계: 군집 중심 선정 ──
        var zoneCenters = new List<Vector2>(terrainZoneCount);
        for (int z = 0; z < terrainZoneCount; z++)
        {
            for (int attempt = 0; attempt < 80; attempt++)
            {
                var center = new Vector2(Random.Range(-halfW, halfW), Random.Range(-halfH, halfH));

                bool reject = false;
                for (int n = 0; n < nodePositions.Count; n++)
                {
                    if ((center - nodePositions[n]).sqrMagnitude < nodeClearSq)
                    { reject = true; break; }
                }
                if (reject) continue;

                for (int c = 0; c < zoneCenters.Count; c++)
                {
                    if ((center - zoneCenters[c]).sqrMagnitude < zoneSepSq)
                    { reject = true; break; }
                }
                if (reject) continue;

                zoneCenters.Add(center);
                break;
            }
        }

        // ── 2단계: 군집별 스프라이트 배치 ──
        // 같은 군집은 같은 스프라이트 → 숲/바위 구역처럼 보임
        int placed = 0;
        var allPlaced = new List<Vector2>(zoneCenters.Count * decorationsPerZone);

        for (int z = 0; z < zoneCenters.Count; z++)
        {
            var center     = zoneCenters[z];
            var zoneSprite = sprites[z % sprites.Length];

            for (int i = 0; i < decorationsPerZone; i++)
            {
                // 극좌표 기반 배치: sqrt(Random.value)로 균등 분포
                float angle  = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float radius = Mathf.Sqrt(Random.value) * terrainZoneRadius;
                var pos      = center + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
                pos.x = Mathf.Clamp(pos.x, -halfW, halfW);
                pos.y = Mathf.Clamp(pos.y, -halfH, halfH);

                bool reject = false;
                for (int n = 0; n < nodePositions.Count; n++)
                {
                    if ((pos - nodePositions[n]).sqrMagnitude < nodeClearSq)
                    { reject = true; break; }
                }
                if (reject) continue;

                for (int p = 0; p < allPlaced.Count; p++)
                {
                    if ((pos - allPlaced[p]).sqrMagnitude < intraSpacingSq)
                    { reject = true; break; }
                }
                if (reject) continue;

                // 중심에 가까울수록 크게 (원근감)
                float distRatio = Mathf.Clamp01(radius / terrainZoneRadius);
                float s = Mathf.Lerp(decorationScaleRange.y, decorationScaleRange.x, distRatio * 0.6f);
                s = Mathf.Clamp(s + Random.Range(-0.08f, 0.08f), decorationScaleRange.x, decorationScaleRange.y);

                SpawnDecoration(layerRT, zoneSprite, pos, placed, s);
                allPlaced.Add(pos);
                placed++;
            }
        }
    }

    private void SpawnDecoration(RectTransform parent, Sprite sprite, Vector2 pos, int index, float scale)
    {
        var go = new GameObject($"Decor_{index}", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;

        rt.localScale = new Vector3(scale, scale, 1f);
        rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-decorationMaxRotation, decorationMaxRotation));

        var img = go.GetComponent<UnityEngine.UI.Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        img.preserveAspect = true;
        img.SetNativeSize();

        // 스프라이트 원본이 노드(80px)보다 크지 않도록 상한 적용
        if (rt.sizeDelta.x > decorationMaxSize || rt.sizeDelta.y > decorationMaxSize)
        {
            float ratio = Mathf.Min(decorationMaxSize / rt.sizeDelta.x, decorationMaxSize / rt.sizeDelta.y);
            rt.sizeDelta *= ratio;
        }
    }

    private static async UniTaskVoid LoadChapterBackgroundAsync(UnityEngine.UI.Image image, string key, Color tint)
    {
        try
        {
            // TryLoadAssetAsync: 키 미등록 시 예외/로그 없이 null 반환
            var sprite = await Managers.AddressableManager.TryLoadAssetAsync<Sprite>(key);
            if (sprite != null && image != null)
            {
                image.sprite = sprite;
                image.color = tint;
                return;
            }
            var tex = await Managers.AddressableManager.TryLoadAssetAsync<Texture2D>(key);
            if (tex != null && image != null)
            {
                image.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                image.color = tint;
            }
        }
        catch (System.OperationCanceledException) { }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[StageMapBootstrapper] 챕터 배경 로드 실패 ({key}): {e.Message}");
        }
    }

    private static async UniTaskVoid LoadDefaultBackgroundAsync(UnityEngine.UI.Image image)
    {
        try
        {
            // TryLoadAssetAsync: 키 미등록 시 예외/로그 없이 null 반환
            var sprite = await Managers.AddressableManager.TryLoadAssetAsync<Sprite>("map_3");
            if (sprite != null && image != null)
            {
                image.sprite = sprite;
                image.color = Color.white;
                return;
            }

            // Sprite 실패 시 Texture2D로 폴백
            var tex = await Managers.AddressableManager.TryLoadAssetAsync<Texture2D>("map_3");
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
