using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// StagePointUI 노드를 층(layer)별로 배치한다.
/// StageMapScroller.RebuildMap() → ApplyLayout() 순으로 호출됨.
///
/// 배치 규칙:
///   1. 각 노드의 LayerIndex 메타데이터로 층 분류 (Generator가 주입).
///      메타데이터 없으면 pointId 기반 fallback (구버전 씬 호환).
///   2. 모든 층에 대해 "가장 넓은 층"의 폭을 기준으로 정규화된 X 비율 배치.
///      → 층마다 X 스케일이 달라 연결선이 사선되거나 노드가 뭉쳐 보이는 문제 제거.
///   3. 랜덤 오프셋은 노드 간격의 일부(safeOffsetRatio)로 제한해 슬라이딩 윈도우
///      planar 보장을 깨지 않음.
/// </summary>
public class StageNodeLayout : MonoBehaviour
{
    [Header("레이아웃 설정")]
    [Tooltip("층 간 Y 간격")]
    [SerializeField] private float layerSpacing = 800f;

    [Tooltip("첫 번째 층의 Y 위치 (콘텐츠 중심 기준, 양수 = 위)")]
    [SerializeField] private float topY = 2400f;

    [Tooltip("가장 넓은 층의 노드 간 X 간격. 이 값으로 전체 층 폭이 결정된다.")]
    [SerializeField] private float nodeSpacingX = 500f;

    [Header("랜덤 지터")]
    [Tooltip("X 오프셋 최대값. 실제 적용값은 nodeSpacingX * safeOffsetRatio 와 min.")]
    [SerializeField] private float randomOffsetX = 40f;

    [Tooltip("노드 간격 대비 X 지터 안전 비율(0~0.3 권장). 초과 시 교차 가능.")]
    [Range(0f, 0.3f)]
    [SerializeField] private float safeOffsetRatio = 0.12f;

    [Tooltip("Y 랜덤 오프셋 최대값")]
    [SerializeField] private float randomOffsetY = 20f;

    [Header("층 구성 (메타데이터 없을 때 fallback)")]
    [Tooltip("각 층의 노드 수. 비어있으면 자동 배분.")]
    [SerializeField] private int[] manualLayerSizes;

    /// <summary>동적 생성 시 층별 노드 수를 외부에서 설정 (fallback용).</summary>
    public void SetLayerSizes(int[] sizes)
    {
        manualLayerSizes = sizes;
    }

    /// <summary>노드를 층별로 배치한다. 동적 생성 후 호출.</summary>
    public void ApplyLayout()
    {
        var allPoints = GetComponentsInChildren<StagePointUI>(true);
        if (allPoints.Length == 0) return;

        var layers = BuildLayers(allPoints);
        if (layers.Count == 0) return;

        int maxCount = 1;
        for (int i = 0; i < layers.Count; i++)
            if (layers[i].Count > maxCount) maxCount = layers[i].Count;

        // 가장 넓은 층의 총 폭. 모든 층이 이 폭 안에서 정규화 배치됨.
        float referenceWidth = (maxCount - 1) * nodeSpacingX;
        float currentY = topY;

        for (int i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            float y = currentY;

            // Y 지터 (첫 층/마지막 층은 지터 없음)
            if (i > 0 && i < layers.Count - 1)
                y += Random.Range(-randomOffsetY, randomOffsetY);

            LayoutLayer(layer, y, referenceWidth);
            currentY -= layerSpacing;
        }
    }

    /// <summary>allPoints를 층 리스트로 분류. LayerIndex 메타가 있으면 우선 사용.</summary>
    private List<List<StagePointUI>> BuildLayers(StagePointUI[] allPoints)
    {
        bool hasMeta = false;
        for (int i = 0; i < allPoints.Length; i++)
        {
            if (allPoints[i].LayerIndex >= 0) { hasMeta = true; break; }
        }

        if (hasMeta)
            return BuildLayersFromMeta(allPoints);

        return BuildLayersFromPointId(allPoints);
    }

    /// <summary>LayerIndex 메타데이터로 층 분류 (Generator-aligned). IndexInLayer 순 정렬.
    /// 메타 누락 노드는 배치에서 제외 (Start 층에 Boss/Middle가 섞여 잘못된 위치에 가는 것 방지).</summary>
    private List<List<StagePointUI>> BuildLayersFromMeta(StagePointUI[] allPoints)
    {
        var byLayer = new SortedDictionary<int, List<StagePointUI>>();

        foreach (var p in allPoints)
        {
            int li = p.LayerIndex;
            if (li < 0)
            {
                Debug.LogWarning($"[StageNodeLayout] LayerIndex 누락 노드: pointId={p.PointId} — 배치에서 제외됨.");
                continue;
            }
            if (!byLayer.TryGetValue(li, out var list))
            {
                list = new List<StagePointUI>();
                byLayer[li] = list;
            }
            list.Add(p);
        }

        foreach (var list in byLayer.Values)
            list.Sort((a, b) => a.IndexInLayer.CompareTo(b.IndexInLayer));

        return new List<List<StagePointUI>>(byLayer.Values);
    }

    /// <summary>구버전 fallback: pointId 기반 분류 (메타 없는 정적 씬용).</summary>
    private List<List<StagePointUI>> BuildLayersFromPointId(StagePointUI[] allPoints)
    {
        StagePointUI startNode = null;
        StagePointUI bossNode = null;
        var normalNodes = new List<StagePointUI>();

        foreach (var p in allPoints)
        {
            if (p.PointId >= 100)
                bossNode = p;
            else if (p.PointId == 0)
                startNode = p;
            else
                normalNodes.Add(p);
        }

        normalNodes.Sort((a, b) => a.PointId.CompareTo(b.PointId));

        var layers = new List<List<StagePointUI>>();
        if (startNode != null)
            layers.Add(new List<StagePointUI> { startNode });

        if (manualLayerSizes != null && manualLayerSizes.Length > 0)
        {
            int idx = 0;
            foreach (int size in manualLayerSizes)
            {
                var layer = new List<StagePointUI>();
                for (int j = 0; j < size && idx < normalNodes.Count; j++, idx++)
                    layer.Add(normalNodes[idx]);
                if (layer.Count > 0)
                    layers.Add(layer);
            }
        }
        else
        {
            int[] pattern = GetAutoPattern(normalNodes.Count);
            int idx = 0;
            foreach (int size in pattern)
            {
                var layer = new List<StagePointUI>();
                for (int j = 0; j < size && idx < normalNodes.Count; j++, idx++)
                    layer.Add(normalNodes[idx]);
                if (layer.Count > 0)
                    layers.Add(layer);
            }
        }

        if (bossNode != null)
            layers.Add(new List<StagePointUI> { bossNode });

        return layers;
    }

    private int[] GetAutoPattern(int count)
    {
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

    /// <summary>referenceWidth 내부에 count개 노드를 균등 분배.
    /// 모든 층이 같은 referenceWidth를 쓰므로 층 간 X 스케일이 일치한다.</summary>
    private void LayoutLayer(List<StagePointUI> nodes, float y, float referenceWidth)
    {
        int count = nodes.Count;
        if (count == 0) return;

        // count==1이면 중앙, 아니면 referenceWidth를 (count-1)등분
        float step = count > 1 ? referenceWidth / (count - 1) : 0f;
        float startX = count > 1 ? -referenceWidth * 0.5f : 0f;

        // 교차 방지 안전 오프셋: step의 일부로 제한
        float safeOffset = count > 1
            ? Mathf.Min(randomOffsetX, step * safeOffsetRatio)
            : 0f;

        for (int i = 0; i < count; i++)
        {
            float x = startX + i * step;

            if (safeOffset > 0f)
                x += Random.Range(-safeOffset, safeOffset);

            var rt = nodes[i].GetComponent<RectTransform>();
            if (rt != null)
                rt.anchoredPosition = new Vector2(x, y);
        }
    }
}
