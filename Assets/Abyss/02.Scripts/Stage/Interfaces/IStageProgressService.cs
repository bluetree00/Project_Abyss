using Cysharp.Threading.Tasks;

namespace StageSystem
{
    /// <summary>
    /// 책임: 진행 상태(현재 노드, 방문/클리어 목록) 갱신과 영속화.
    /// StageManager가 UpdateProgress/Save JSON 세부 구현에서 분리.
    /// 분리 이유:
    /// 1) 저장 전략 교체 (즉시 저장 -> 배치/서버 동기화)
    /// 2) 재시도/백오프/암호화 등 정책 적용 지점 확보
    /// 3) 테스트에서 메모리 Progress 대체
    /// </summary>
    public interface IStageProgressService
    {
        UniTask InitializeAsync(StageGraphData data);
        void UpdateProgress(StageGraphData data, int nodeId, bool isCompleted = false);
        UniTask SaveAsync(StageGraphData data);
    }
}
