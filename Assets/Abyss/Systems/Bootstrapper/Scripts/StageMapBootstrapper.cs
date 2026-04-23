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
    [Tooltip("맵에 뿌릴 장식 개수.")]
    [SerializeField] private int decorationCount = 22;
    [Tooltip("노드 주위 회피 반경. 이 반경 안쪽엔 장식 배치 안 함 (노드 가림·클릭 방해 방지).")]
    [SerializeField] private float decorationNodeClearance = 200f;
    [Tooltip("장식 간 최소 간격. 군집 방지.")]
    [SerializeField] private float decorationMinSpacing = 110f;
    [Tooltip("장식 스케일 범위 (min~max).")]
    [SerializeField] private Vector2 decorationScaleRange = new Vector2(0.7f, 1.3f);
    [Tooltip("장식 회전 범위 (±도).")]
    [SerializeField] private float decorationMaxRotation = 8f;

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
        else
        {
            // 폴백: Addressable에서 기본 배경 로드
            LoadDefaultBackgroundAsync(image).Forget();
        }
    }

    /// <summary>
    /// 배경 위·노드 아래 레이어에 장식 스프라이트를 스캐터 배치.
    /// ChapterDataSO.decorationSprites 우선, 없으면 Bootstrapper의 defaultDecorationSprites 사용.
    /// 각 노드 주위 decorationNodeClearance 반경을 회피, 장식끼리도 decorationMinSpacing 유지.
    /// 호출 시점: scroller.RebuildMap() 이후 (노드 anchoredPosition이 레이아웃으로 채워진 상태).
    /// </summary>
    private void PopulateMapDecorations(StageMapScroller scroller, ChapterId chapter, float contentWidth, float contentHeight)
    {
        if (scroller == null || scroller.ContentTransform == null) return;
        if (decorationCount <= 0) return;

        var content = scroller.ContentTransform;

        // 장식 소스 결정: 챕터 데이터 우선
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

        // 노드 위치 수집 (MapContent 내부 StagePointUI들, anchoredPosition 기준)
        var nodeUIs = content.GetComponentsInChildren<StagePointUI>(true);
        var nodePositions = new List<Vector2>(nodeUIs.Length);
        for (int i = 0; i < nodeUIs.Length; i++)
        {
            var rt = nodeUIs[i].GetComponent<RectTransform>();
            if (rt != null) nodePositions.Add(rt.anchoredPosition);
        }

        // 배치 영역: 콘텐츠 가장자리에서 안쪽으로 약간 패딩
        float padX = 60f;
        float padY = 60f;
        float halfW = Mathf.Max(0f, contentWidth * 0.5f - padX);
        float halfH = Mathf.Max(0f, contentHeight * 0.5f - padY);
        if (halfW <= 0f || halfH <= 0f) return;

        float nodeClearSq = decorationNodeClearance * decorationNodeClearance;
        float minSpacingSq = decorationMinSpacing * decorationMinSpacing;

        // 이미 배치된 장식 좌표 (장식끼리 군집 방지용)
        var placedPositions = new List<Vector2>(decorationCount);

        // 시도 예산: 개당 최대 50회 재표집 (빽빽한 설정에서 거부율 대응)
        int maxAttemptsPerItem = 50;
        int placed = 0;

        for (int i = 0; i < decorationCount; i++)
        {
            Vector2 pos = Vector2.zero;
            bool accepted = false;

            for (int attempt = 0; attempt < maxAttemptsPerItem; attempt++)
            {
                pos = new Vector2(Random.Range(-halfW, halfW), Random.Range(-halfH, halfH));

                bool tooClose = false;
                for (int n = 0; n < nodePositions.Count; n++)
                {
                    if ((pos - nodePositions[n]).sqrMagnitude < nodeClearSq)
                    {
                        tooClose = true; break;
                    }
                }
                if (tooClose) continue;

                for (int p = 0; p < placedPositions.Count; p++)
                {
                    if ((pos - placedPositions[p]).sqrMagnitude < minSpacingSq)
                    {
                        tooClose = true; break;
                    }
                }
                if (tooClose) continue;

                accepted = true;
                break;
            }

            if (!accepted) continue;

            var sprite = sprites[Random.Range(0, sprites.Length)];
            if (sprite == null) continue;

            SpawnDecoration(layerRT, sprite, pos, placed);
            placedPositions.Add(pos);
            placed++;
        }
    }

    private void SpawnDecoration(RectTransform parent, Sprite sprite, Vector2 pos, int index)
    {
        var go = new GameObject($"Decor_{index}", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;

        float s = Random.Range(decorationScaleRange.x, decorationScaleRange.y);
        rt.localScale = new Vector3(s, s, 1f);
        rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-decorationMaxRotation, decorationMaxRotation));

        var img = go.GetComponent<UnityEngine.UI.Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        img.preserveAspect = true;
        img.SetNativeSize();
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
