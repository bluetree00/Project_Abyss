/// <summary>
/// 진행 중 런 세이브의 저장소 추상화. PR1은 로컬 파일(LocalFileRunSaveStore)만 구현.
/// 추후 Steam Cloud/콘솔 등 매체 교체를 위해 인터페이스로 분리한다.
/// </summary>
public interface IRunSaveStore
{
    bool        HasSave();
    RunSaveData Load();
    void        Save(RunSaveData data);
    void        Delete();
}
