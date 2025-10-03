using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

namespace StageSystem
{
    /// <summary>
    /// 책임: 챕터/스테이지 구조(Map + Progress) 로딩. 
    /// StageManager가 파일 경로/역직렬화 세부 사항을 몰라도 되도록 분리.
    /// 분리 이유:
    /// 1) 데이터 소스 교체 (로컬 JSON -> 서버) 시 StageManager 수정 최소화
    /// 2) 단위 테스트 용이 (Mock 구현으로 파일 IO 제거)
    /// 3) 버전 정책/캐싱 계층 삽입 여지 확보
    /// </summary>
    public interface IStageDataLoader
    {
        /// <summary>
        /// 맵 구조(StageMapStruct.json) 로드 (존재하지 않으면 기본 데이터 반환)
        /// </summary>
        UniTask<StageGraphData> LoadMapStructAsync();

        /// <summary>
        /// 진행(Progress) 로드. 없으면 null 반환.
        /// </summary>
        UniTask<ProgressDataJSON> LoadProgressAsync();
    }
}
