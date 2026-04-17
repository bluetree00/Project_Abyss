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

        // 패턴 계산: 중간층만 (Start/Boss 제외)
        int[] middlePattern = BuildMiddlePattern(middleLayers, peakLayer);

        // 노드 생성 + 연결
        GenerateNodes(contentParent, middlePattern, template);

        // 레이아웃 패턴 설정
        var layout = scroller.GetComponent<StageNodeLayout>();
        if (layout != null)
            layout.SetLayerSizes(middlePattern);

        // 콘텐츠 크기 조정 (피크층 노드 수에 따라 동적 계산)
        int totalLayers = middleLayers + 2;
        float layerSpacing = 800f;
        float nodeSpacingX = 400f;
        float marginY = 600f;
        float marginX = 600f;
        float height = totalLayers * layerSpacing + marginY;
        float width = Mathf.Max(1920f, peakLayer * nodeSpacingX + marginX);
        scroller.SetContentSize(width, height);

        // 배경 적용
        ApplyMapBackground(scroller, chapter);

        // 레이아웃 + 라인 + 포커스
        scroller.RebuildMap();
    }

    /// <summary>
    /// 중간층 패턴 생성 (Start/Boss 제외).
    /// middleLayers=5, peakLayer=3 → {2, 3, 4, 3, 2}
    /// 전체: Start(1) + {2, 3, 4, 3, 2} + Boss(1) = {1, 2, 3, 4, 3, 2, 1}
    /// </summary>
    private static int[] BuildMiddlePattern(int middleLayers, int peakLayer)
    {
        middleLayers = Mathf.Max(1, middleLayers);
        peakLayer = Mathf.Clamp(peakLayer, 1, middleLayers);

        var pattern = new int[middleLayers];
        int peakIdx = peakLayer - 1;

        for (int i = 0; i < middleLayers; i++)
        {
            if (i <= peakIdx)
                pattern[i] = i + 2;
            else
                pattern[i] = 2 * peakLayer - i;

            pattern[i] = Mathf.Max(1, pattern[i]);
        }

        return pattern;
    }

    /// <summary>Start + 중간 + Boss 노드 생성 및 연결.</summary>
    private void GenerateNodes(Transform parent, int[] middlePattern, GameObject template)
    {
        int pointId = 1;

        // Start (pointId = 0)
        var startNode = CreateNode(parent, template, 0, StageCategory.Start, NormalRoomCategory.Battle);

        var prevLayerNodes = new List<StagePointUI> { startNode };

        // 중간 층
        for (int li = 0; li < middlePattern.Length; li++)
        {
            int count = middlePattern[li];
            var layerNodes = new List<StagePointUI>();

            for (int n = 0; n < count; n++)
            {
                var category = PickCategory(li, middlePattern.Length);
                var node = CreateNode(parent, template, pointId, StageCategory.Normal, category);
                layerNodes.Add(node);
                pointId++;
            }

            ConnectLayers(prevLayerNodes, layerNodes);
            prevLayerNodes = layerNodes;
        }

        // Boss (pointId = 100)
        var bossNode = CreateNode(parent, template, 100, StageCategory.Boss, NormalRoomCategory.Battle);
        ConnectLayers(prevLayerNodes, new List<StagePointUI> { bossNode });

        // 템플릿 정리
        if (template != null)
            Destroy(template);
    }

    private StagePointUI CreateNode(Transform parent, GameObject template, int pointId, StageCategory stage, NormalRoomCategory normal)
    {
        GameObject go;
        if (template != null)
        {
            go = Instantiate(template, parent, false);
            go.name = $"Node_{pointId}";
        }
        else
        {
            go = new GameObject($"Node_{pointId}",
                typeof(RectTransform), typeof(UnityEngine.UI.Image),
                typeof(UnityEngine.UI.Button), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(80f, 80f);
        }

        go.SetActive(true);

        var point = go.GetComponent<StagePointUI>();
        if (point == null)
            point = go.AddComponent<StagePointUI>();

        point.Init(pointId, stage, normal, iconMap);
        return point;
    }

    /// <summary>이전 층과 현재 층 노드를 비율 기반으로 연결.</summary>
    private static void ConnectLayers(List<StagePointUI> fromNodes, List<StagePointUI> toNodes)
    {
        // 비율 기반 매핑 + 인접 분기
        for (int fi = 0; fi < fromNodes.Count; fi++)
        {
            float ratio = fromNodes.Count > 1 ? (float)fi / (fromNodes.Count - 1) : 0.5f;
            int primary = Mathf.Clamp(Mathf.RoundToInt(ratio * (toNodes.Count - 1)), 0, toNodes.Count - 1);

            fromNodes[fi].AddNextPointId(toNodes[primary].PointId);

            if (primary > 0 && fromNodes.Count > 1)
                fromNodes[fi].AddNextPointId(toNodes[primary - 1].PointId);
            if (primary < toNodes.Count - 1 && fromNodes.Count > 1)
                fromNodes[fi].AddNextPointId(toNodes[primary + 1].PointId);
        }

        // 고아 방지: 연결 안 된 to 노드 보장
        for (int ti = 0; ti < toNodes.Count; ti++)
        {
            bool connected = false;
            foreach (var fn in fromNodes)
            {
                if (fn.NextPointIds != null)
                {
                    foreach (var nid in fn.NextPointIds)
                    {
                        if (nid == toNodes[ti].PointId) { connected = true; break; }
                    }
                }
                if (connected) break;
            }

            if (!connected && fromNodes.Count > 0)
            {
                float ratio = toNodes.Count > 1 ? (float)ti / (toNodes.Count - 1) : 0.5f;
                int bestFrom = Mathf.Clamp(Mathf.RoundToInt(ratio * (fromNodes.Count - 1)), 0, fromNodes.Count - 1);
                fromNodes[bestFrom].AddNextPointId(toNodes[ti].PointId);
            }
        }
    }

    /// <summary>진행도에 따른 방 카테고리 랜덤 선택.</summary>
    private static NormalRoomCategory PickCategory(int layerIdx, int totalMiddleLayers)
    {
        float progress = totalMiddleLayers > 1 ? (float)layerIdx / (totalMiddleLayers - 1) : 0.5f;
        float roll = Random.value;

        if (progress < 0.3f)
            return roll < 0.7f ? NormalRoomCategory.Battle : NormalRoomCategory.Event;
        if (progress < 0.6f)
            return roll < 0.4f ? NormalRoomCategory.Battle :
                   roll < 0.7f ? NormalRoomCategory.Event :
                   roll < 0.85f ? NormalRoomCategory.Shop : NormalRoomCategory.Elite;
        return roll < 0.5f ? NormalRoomCategory.Battle :
               roll < 0.75f ? NormalRoomCategory.Elite : NormalRoomCategory.Event;
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
