using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// StagePointUI 노드를 층(layer)별로 랜덤 배치한다.
/// StageMapScroller.BuildContentContainer() 후, StageLineConnector.BuildLines() 전에 실행.
///
/// 층 구성은 pointId 순서로 자동 결정:
///   - StageCategory.Start → 최상단 (1개)
///   - StageCategory.Boss  → 최하단 (1개)
///   - StageCategory.Normal → 중간 층들에 균등 배분
///
/// 같은 층의 노드는 X축으로 균등 분배 + 랜덤 오프셋.
/// Y축은 층 간격 기반 + 작은 랜덤 지터.
/// </summary>
public class StageNodeLayout : MonoBehaviour
{
    [Header("레이아웃 설정")]
    [Tooltip("층 간 Y 간격")]
    [SerializeField] private float layerSpacing = 800f;

    [Tooltip("첫 번째 층의 Y 위치 (콘텐츠 중심 기준, 양수 = 위)")]
    [SerializeField] private float topY = 2400f;

    [Tooltip("같은 층 노드 간 X 간격")]
    [SerializeField] private float nodeSpacingX = 500f;

    [Tooltip("X 랜덤 오프셋 최대값")]
    [SerializeField] private float randomOffsetX = 40f;

    [Tooltip("Y 랜덤 오프셋 최대값")]
    [SerializeField] private float randomOffsetY = 20f;

    [Header("층 구성 (자동 감지 안 될 때 수동 지정)")]
    [Tooltip("각 층의 노드 수. 비어있으면 자동 배분.")]
    [SerializeField] private int[] manualLayerSizes;

    /// <summary>동적 생성 시 층별 노드 수를 외부에서 설정.</summary>
    public void SetLayerSizes(int[] sizes)
    {
        manualLayerSizes = sizes;
    }

    /// <summary>노드를 층별로 랜덤 배치한다. Start 전에 호출.</summary>
    public void ApplyLayout()
    {
        var allPoints = GetComponentsInChildren<StagePointUI>(true);
        if (allPoints.Length == 0) return;

        // Start, Normal, Boss 분류
        StagePointUI startNode = null;
        StagePointUI bossNode = null;
        var normalNodes = new List<StagePointUI>();

        foreach (var p in allPoints)
        {
            // pointId 100 이상 = Boss (컨벤션)
            if (p.PointId >= 100)
                bossNode = p;
            else if (p.PointId == 0)
                startNode = p;
            else
                normalNodes.Add(p);
        }

        // pointId 순 정렬
        normalNodes.Sort((a, b) => a.PointId.CompareTo(b.PointId));

        // 층 구성 결정
        var layers = BuildLayers(startNode, normalNodes, bossNode);

        // 배치
        float currentY = topY;

        for (int i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            float y = currentY;

            // Y 지터 (첫 층/마지막 층은 지터 없음)
            if (i > 0 && i < layers.Count - 1)
                y += Random.Range(-randomOffsetY, randomOffsetY);

            LayoutLayer(layer, y);
            currentY -= layerSpacing;
        }

    }

    private List<List<StagePointUI>> BuildLayers(
        StagePointUI start, List<StagePointUI> normals, StagePointUI boss)
    {
        var layers = new List<List<StagePointUI>>();

        // 층1: Start
        if (start != null)
            layers.Add(new List<StagePointUI> { start });

        // 중간 층: manualLayerSizes 또는 자동 배분
        if (manualLayerSizes != null && manualLayerSizes.Length > 0)
        {
            int idx = 0;
            foreach (int size in manualLayerSizes)
            {
                var layer = new List<StagePointUI>();
                for (int j = 0; j < size && idx < normals.Count; j++, idx++)
                    layer.Add(normals[idx]);
                if (layer.Count > 0)
                    layers.Add(layer);
            }
        }
        else
        {
            // 자동: 2-3-2-2 패턴 (9개 노드 기준)
            int[] pattern = GetAutoPattern(normals.Count);
            int idx = 0;
            foreach (int size in pattern)
            {
                var layer = new List<StagePointUI>();
                for (int j = 0; j < size && idx < normals.Count; j++, idx++)
                    layer.Add(normals[idx]);
                if (layer.Count > 0)
                    layers.Add(layer);
            }
        }

        // 마지막 층: Boss
        if (boss != null)
            layers.Add(new List<StagePointUI> { boss });

        return layers;
    }

    private int[] GetAutoPattern(int count)
    {
        // 노드 수에 따른 기본 패턴
        return count switch
        {
            <= 2  => new[] { count },
            <= 4  => new[] { 2, count - 2 },
            <= 6  => new[] { 2, 2, count - 4 },
            <= 9  => new[] { 2, 3, 2, count - 7 },
            <= 12 => new[] { 2, 3, 3, 2, count - 10 },
            _     => new[] { 2, 3, 3, 3, 2, count - 13 },
        };
    }

    private void LayoutLayer(List<StagePointUI> nodes, float y)
    {
        int count = nodes.Count;
        if (count == 0) return;

        // X 위치: 중앙 기준 균등 배분
        float totalWidth = (count - 1) * nodeSpacingX;
        float startX = -totalWidth * 0.5f;

        for (int i = 0; i < count; i++)
        {
            float x = startX + i * nodeSpacingX;

            // 랜덤 오프셋 (1개짜리 층은 X 오프셋 없음)
            if (count > 1)
                x += Random.Range(-randomOffsetX, randomOffsetX);

            var rt = nodes[i].GetComponent<RectTransform>();
            if (rt != null)
                rt.anchoredPosition = new Vector2(x, y);
        }
    }

}
