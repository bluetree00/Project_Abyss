using MapGeneratorManager; // Graph 타입 위치

namespace StageSystem
{
    /// <summary>
    /// 책임: StageGraphData -> ChapterData 변환 후 Graph 생성.
    /// 분리 이유:
    /// 1) 그래프 생성 규칙 변경/확장 (특수 노드, 이벤트 노드 등) 시 StageManager 수정 회피
    /// 2) 검증/로깅/프로파일링 삽입 위치 명확화
    /// 3) GenerateGraph 단위 테스트 용이
    /// </summary>
    public interface IStageGraphBuilder
    {
        Graph BuildGraph(StageGraphData data);
    }
}
