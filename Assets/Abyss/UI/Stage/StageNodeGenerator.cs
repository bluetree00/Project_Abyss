using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 챕터 설정(middleLayers, peakLayer)을 기반으로 다이아몬드 패턴 노드를 동적 생성한다.
/// Start(1) → 확장(2,3,4...) → 피크 → 축소(...3,2) → Boss(1)
/// 기존 프리팹 노드를 템플릿으로 복제.
/// </summary>
public class StageNodeGenerator : MonoBehaviour
{
    [Header("노드 템플릿 (프리팹에서 1개 남겨두기)")]
    [SerializeField] private GameObject nodeTemplate;

    [Header("기본값 (ChapterRegistry 없을 때)")]
    [SerializeField] private int defaultMiddleLayers = 5;
    [SerializeField] private int defaultPeakLayer = 3;

    [Header("참조")]
    [SerializeField] private ChapterRegistry chapterRegistry;

    // ── Private ──
    private readonly List<StagePointUI> _generatedNodes = new();

    /// <summary>현재 챕터에 맞는 노드를 동적 생성 후 반환.</summary>
    public StagePointUI[] Generate(ChapterId chapter, Transform parent)
    {
        ClearGenerated();

        int middleLayers, peakLayer;
        ResolveConfig(chapter, out middleLayers, out peakLayer);

        int[] pattern = BuildDiamondPattern(middleLayers, peakLayer);

        int pointId = 1;

        // Start
        var startNode = CreateNode(parent, 0, StageCategory.Start, NormalRoomCategory.Battle);
        _generatedNodes.Add(startNode);

        var prevLayerIds = new List<int> { 0 };

        // 중간 층
        for (int li = 0; li < pattern.Length; li++)
        {
            int count = pattern[li];
            var currentIds = new List<int>();

            for (int n = 0; n < count; n++)
            {
                var category = PickCategory(li, pattern.Length);
                var node = CreateNode(parent, pointId, StageCategory.Normal, category);
                _generatedNodes.Add(node);
                currentIds.Add(pointId);
                pointId++;
            }

            ConnectLayers(prevLayerIds, currentIds);
            prevLayerIds = currentIds;
        }

        // Boss
        int bossId = 100;
        var bossNode = CreateNode(parent, bossId, StageCategory.Boss, NormalRoomCategory.Battle);
        _generatedNodes.Add(bossNode);
        ConnectLayers(prevLayerIds, new List<int> { bossId });

        return _generatedNodes.ToArray();
    }

    public void ClearGenerated()
    {
        foreach (var node in _generatedNodes)
            if (node != null) Destroy(node.gameObject);
        _generatedNodes.Clear();
    }

    // ── Pattern ──

    /// <summary>
    /// 다이아몬드 패턴 생성.
    /// middleLayers=5, peakLayer=3 → {2, 3, 4, 3, 2}
    /// middleLayers=7, peakLayer=4 → {2, 3, 4, 5, 4, 3, 2}
    /// </summary>
    public static int[] BuildDiamondPattern(int middleLayers, int peakLayer)
    {
        middleLayers = Mathf.Max(1, middleLayers);
        peakLayer = Mathf.Clamp(peakLayer, 1, middleLayers);

        var pattern = new int[middleLayers];

        for (int i = 0; i < middleLayers; i++)
        {
            if (i < peakLayer)
            {
                // 확장 구간: 2 → peakLayer+1
                float t = (float)i / Mathf.Max(1, peakLayer - 1);
                pattern[i] = Mathf.RoundToInt(Mathf.Lerp(2f, peakLayer + 1f, t));
            }
            else
            {
                // 축소 구간: peakLayer+1 → 2
                float t = (float)(i - peakLayer) / Mathf.Max(1, middleLayers - peakLayer - 1);
                pattern[i] = Mathf.RoundToInt(Mathf.Lerp(peakLayer + 1f, 2f, t));
            }
            pattern[i] = Mathf.Max(1, pattern[i]);
        }

        return pattern;
    }

    // ── Config ──

    private void ResolveConfig(ChapterId chapter, out int middleLayers, out int peakLayer)
    {
        middleLayers = defaultMiddleLayers;
        peakLayer = defaultPeakLayer;

        if (chapterRegistry != null)
        {
            var data = chapterRegistry.Get(chapter);
            if (data != null)
            {
                middleLayers = data.middleLayers;
                peakLayer = data.peakLayer;
            }
        }
    }

    // ── Node Creation ──

    private StagePointUI CreateNode(Transform parent, int pointId, StageCategory stage, NormalRoomCategory normal)
    {
        GameObject go;
        if (nodeTemplate != null)
        {
            go = Instantiate(nodeTemplate, parent, false);
            go.name = $"Node_{pointId}";
        }
        else
        {
            go = new GameObject($"Node_{pointId}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(80f, 80f);
        }

        go.SetActive(true);

        var point = go.GetComponent<StagePointUI>();
        if (point == null)
            point = go.AddComponent<StagePointUI>();

        SetField(point, "pointId", pointId);
        SetField(point, "stageCategory", stage);
        SetField(point, "normalRoomCategory", normal);
        SetField(point, "nextPointIds", new List<int>());

        return point;
    }

    // ── Connection ──

    private void ConnectLayers(List<int> fromIds, List<int> toIds)
    {
        var fromMap = new Dictionary<int, StagePointUI>();
        foreach (var node in _generatedNodes)
            if (fromIds.Contains(node.PointId))
                fromMap[node.PointId] = node;

        // 비율 기반 매핑 + 인접 분기
        for (int fi = 0; fi < fromIds.Count; fi++)
        {
            float ratio = fromIds.Count > 1 ? (float)fi / (fromIds.Count - 1) : 0.5f;
            int primary = Mathf.Clamp(Mathf.RoundToInt(ratio * (toIds.Count - 1)), 0, toIds.Count - 1);

            var nextList = GetNextList(fromMap[fromIds[fi]]);
            AddUnique(nextList, toIds[primary]);

            if (primary > 0 && fromIds.Count > 1)
                AddUnique(nextList, toIds[primary - 1]);
            if (primary < toIds.Count - 1 && fromIds.Count > 1)
                AddUnique(nextList, toIds[primary + 1]);
        }

        // 고아 to 노드 보장
        foreach (var toId in toIds)
        {
            bool connected = false;
            foreach (var fn in fromMap.Values)
                if (GetNextList(fn).Contains(toId)) { connected = true; break; }

            if (!connected && fromMap.Count > 0)
            {
                int toIdx = toIds.IndexOf(toId);
                float ratio = toIds.Count > 1 ? (float)toIdx / (toIds.Count - 1) : 0.5f;
                int bestFrom = Mathf.Clamp(Mathf.RoundToInt(ratio * (fromIds.Count - 1)), 0, fromIds.Count - 1);
                AddUnique(GetNextList(fromMap[fromIds[bestFrom]]), toId);
            }
        }
    }

    // ── Category ──

    private NormalRoomCategory PickCategory(int layerIdx, int totalLayers)
    {
        float progress = totalLayers > 1 ? (float)layerIdx / (totalLayers - 1) : 0.5f;
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

    // ── Helpers ──

    private static List<int> GetNextList(StagePointUI node)
    {
        var field = typeof(StagePointUI).GetField("nextPointIds",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field?.GetValue(node) as List<int>;
    }

    private static void AddUnique(List<int> list, int value)
    {
        if (!list.Contains(value)) list.Add(value);
    }

    private static void SetField(object obj, string name, object value)
    {
        var field = obj.GetType().GetField(name,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(obj, value);
    }
}
