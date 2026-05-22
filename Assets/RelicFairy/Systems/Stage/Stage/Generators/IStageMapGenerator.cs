using System.Collections.Generic;

/// <summary>
/// StageMap 노드 그래프 생성 전략. UI 생성과 분리된 순수 데이터 계층.
/// 구현체는 같은 입력에 대해 결정적(시드 기반) 또는 확률적 결과를 반환할 수 있다.
/// </summary>
public interface IStageMapGenerator
{
    StageMapGraph Generate(in StageMapGenerationRequest request);
}

/// <summary>생성 요청 파라미터.</summary>
public readonly struct StageMapGenerationRequest
{
    public readonly int MiddleLayers;
    public readonly int PeakLayer;

    public StageMapGenerationRequest(int middleLayers, int peakLayer)
    {
        MiddleLayers = middleLayers;
        PeakLayer = peakLayer;
    }
}

/// <summary>생성 결과. Bootstrapper가 이걸 받아 UI 노드를 찍어낸다.</summary>
public sealed class StageMapGraph
{
    public List<StageMapNode> Nodes;

    /// <summary>전체 층 패턴 (Start 1 + 중간층 + Boss 1).</summary>
    public int[] FullPattern;

    /// <summary>중간층 패턴만. StageNodeLayout.SetLayerSizes용.</summary>
    public int[] MiddlePattern;
}

/// <summary>그래프 노드 한 개의 순수 데이터 표현.</summary>
public sealed class StageMapNode
{
    public int PointId;
    public StageCategory Stage;
    public NormalRoomCategory Normal;
    public int LayerIndex;
    public int IndexInLayer;
    public List<int> NextPointIds = new();
}
