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
    private const int MinShopPerChapter = 2;

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

        EnsureMinShops(nodes, MinShopPerChapter);

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

    /// <summary>
    /// 한 챕터에 Shop 노드를 최소 <paramref name="minCount"/>개 보장.
    /// Roller가 배치한 Battle/Event를 우선, 부족하면 Elite까지 승격.
    /// 가능하면 서로 다른 층에 분산.
    /// </summary>
    private static void EnsureMinShops(List<StageMapNode> nodes, int minCount)
    {
        if (minCount <= 0 || nodes == null) return;

        int shopCount = 0;
        var primary = new List<StageMapNode>();   // Battle/Event — 우선 교체
        var secondary = new List<StageMapNode>(); // Elite — 부족할 때만
        var shopLayers = new HashSet<int>();

        foreach (var n in nodes)
        {
            if (n.Stage != StageCategory.Normal) continue;
            switch (n.Normal)
            {
                case NormalRoomCategory.Shop:
                    shopCount++;
                    shopLayers.Add(n.LayerIndex);
                    break;
                case NormalRoomCategory.Battle:
                case NormalRoomCategory.Event:
                    primary.Add(n);
                    break;
                case NormalRoomCategory.Elite:
                    secondary.Add(n);
                    break;
            }
        }

        int needed = minCount - shopCount;
        if (needed <= 0) return;

        Shuffle(primary);
        Shuffle(secondary);

        // 1차: Shop 없는 층 우선, primary(Battle/Event)만
        int placed = PromoteToShop(primary, shopLayers, needed, requireDifferentLayer: true);
        needed -= placed;

        // 2차: primary에서 층 제약 풀고 채움
        if (needed > 0)
        {
            placed = PromoteToShop(primary, shopLayers, needed, requireDifferentLayer: false);
            needed -= placed;
        }

        // 3차: Elite까지 동원
        if (needed > 0)
        {
            placed = PromoteToShop(secondary, shopLayers, needed, requireDifferentLayer: false);
            needed -= placed;
        }

        if (needed > 0)
            Debug.LogWarning($"[RefinedStageMapGenerator] Shop 최소 {minCount}개 확보 실패: 후보 부족 (남은 {needed}개)");
    }

    private static int PromoteToShop(
        List<StageMapNode> candidates,
        HashSet<int> shopLayers,
        int needed,
        bool requireDifferentLayer)
    {
        int placed = 0;
        for (int i = 0; i < candidates.Count && placed < needed; i++)
        {
            var node = candidates[i];
            if (node.Normal == NormalRoomCategory.Shop) continue;
            if (requireDifferentLayer && shopLayers.Contains(node.LayerIndex)) continue;

            node.Normal = NormalRoomCategory.Shop;
            shopLayers.Add(node.LayerIndex);
            placed++;
        }
        return placed;
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
