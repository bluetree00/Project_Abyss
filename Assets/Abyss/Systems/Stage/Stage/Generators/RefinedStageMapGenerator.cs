using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// StageMap 그래프 생성 - 슬라이딩 윈도우 방식.
/// 각 from[i]는 연속된 to[a, a+1] 두 노드에 고정 연결.
/// Boss 층(to.Count==1)만 예외로 1개 연결.
/// 구성상 교차 0 보장, 모든 to 커버 보장.
/// </summary>
public sealed class RefinedStageMapGenerator : IStageMapGenerator
{
    public StageMapGraph Generate(in StageMapGenerationRequest request)
    {
        int middleLayers = Mathf.Max(1, request.MiddleLayers);
        int peakLayer = Mathf.Clamp(request.PeakLayer, 1, middleLayers);

        int[] middlePattern = BuildMiddlePattern(middleLayers, peakLayer);

        var nodes = new List<StageMapNode>();
        int pointId = 1;

        var startNode = new StageMapNode
        {
            PointId = 0,
            Stage = StageCategory.Start,
            Normal = NormalRoomCategory.Battle,
            LayerIndex = 0,
            IndexInLayer = 0,
        };
        nodes.Add(startNode);
        var prevLayer = new List<StageMapNode> { startNode };

        for (int li = 0; li < middlePattern.Length; li++)
        {
            int count = middlePattern[li];
            var layerNodes = new List<StageMapNode>(count);

            for (int n = 0; n < count; n++)
            {
                var category = StageMapCategoryRoller.Pick(li, middlePattern.Length);
                var node = new StageMapNode
                {
                    PointId = pointId++,
                    Stage = StageCategory.Normal,
                    Normal = category,
                    LayerIndex = li + 1,
                    IndexInLayer = n,
                };
                layerNodes.Add(node);
                nodes.Add(node);
            }

            ConnectLayers(prevLayer, layerNodes);
            prevLayer = layerNodes;
        }

        var bossNode = new StageMapNode
        {
            PointId = 100,
            Stage = StageCategory.Boss,
            Normal = NormalRoomCategory.Battle,
            LayerIndex = middlePattern.Length + 1,
            IndexInLayer = 0,
        };
        nodes.Add(bossNode);
        ConnectLayers(prevLayer, new List<StageMapNode> { bossNode });

        return new StageMapGraph
        {
            Nodes = nodes,
            FullPattern = BuildFullPattern(middlePattern),
            MiddlePattern = middlePattern,
        };
    }

    private static int[] BuildMiddlePattern(int middleLayers, int peakLayer)
    {
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

    private static int[] BuildFullPattern(int[] middle)
    {
        var full = new int[middle.Length + 2];
        full[0] = 1;
        for (int i = 0; i < middle.Length; i++) full[i + 1] = middle[i];
        full[full.Length - 1] = 1;
        return full;
    }

    /// <summary>
    /// 슬라이딩 윈도우 연결. 각 from은 연속된 to 2개([a, a+1])에 연결.
    /// to.Count==1 (Boss)일 땐 각 from이 to[0] 1개만 연결.
    /// 구성상 항상 planar(교차 없음), 모든 to 커버.
    /// </summary>
    private static void ConnectLayers(List<StageMapNode> from, List<StageMapNode> to)
    {
        if (from.Count == 0 || to.Count == 0) return;

        // Boss 층: 각 from → to[0]
        if (to.Count == 1)
        {
            for (int i = 0; i < from.Count; i++)
                AddLink(from[i], to[0]);
            return;
        }

        // 일반 층: 슬라이딩 윈도우 [a, a+1]
        int n = from.Count;
        int m = to.Count;
        int denom = Mathf.Max(1, n - 1);
        int maxStart = m - 2; // a는 [0, m-2] 범위

        for (int i = 0; i < n; i++)
        {
            int a = n > 1 ? (i * (m - 1)) / denom : 0;
            a = Mathf.Clamp(a, 0, maxStart);
            AddLink(from[i], to[a]);
            AddLink(from[i], to[a + 1]);
        }
    }

    private static void AddLink(StageMapNode from, StageMapNode to)
    {
        if (!from.NextPointIds.Contains(to.PointId))
            from.NextPointIds.Add(to.PointId);
    }
}
