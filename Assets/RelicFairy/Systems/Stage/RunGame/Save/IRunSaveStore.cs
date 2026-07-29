/// <summary>
/// 진행 중 런 세이브의 저장소 추상화. PR1은 로컬 파일(LocalFileRunSaveStore)만 구현.
/// 추후 Steam Cloud/콘솔 등 매체 교체를 위해 인터페이스로 분리한다.
///
/// 멀티슬롯: 유저당 고정 3슬롯(0~2). 모든 접근은 slot 인덱스로 주소 지정한다.
/// </summary>
public interface IRunSaveStore
{
    bool        HasSave(int slot);
    RunSaveData Load(int slot);
    void        Save(int slot, RunSaveData data);
    void        Delete(int slot);

    /// <summary>레거시 단일 세이브 포맷이 있으면 슬롯 포맷으로 1회 이전한다(무손실).</summary>
    void        MigrateIfNeeded();
}
