using System.Threading.Tasks;

namespace StageSystem
{
    /// <summary>
    /// StageManager가 새로운 StageGraphData로 재초기화 될 수 있음을 나타내는 인터페이스.
    /// 분리 이유:
    /// - 외부(챕터 전환 서비스)가 StageManager 구체 타입 의존 없이 Reload 호출.
    /// - 테스트에서 Fake 구현 대체 가능.
    /// </summary>
    public interface IStageReloadable
    {
        Task ReloadAsync(StageGraphData newData);
    }
}
