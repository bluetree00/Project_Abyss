using UnityEngine;

/// <summary>
/// 퀘스트/업적 진행도 저장소. 슬롯별 파일(<c>quest_save_{slot}.json</c>).
///
/// 예전엔 <c>PlayerPrefs["questSystem"]</c> 문자열 하나에 전 슬롯이 뒤섞여 있었다 —
/// 슬롯을 지워도 퀘스트 완료 기록이 남았고, 한 슬롯의 진행이 다른 슬롯을 오염시켰으며,
/// 백업(.bak)도 무결성 서명(.sig)도 없어 세이브 계층 밖에 홀로 떠 있었다.
///
/// JSON의 <b>내용</b>(스키마)은 QuestManager가 소유한다. 여기는 어디에 어떻게 담느냐만 책임진다.
/// </summary>
public sealed class QuestSaveStore
{
    private const string Tag = "QuestSave";

    /// <summary>슬롯 도입 이전의 전역 키. 마이그레이션 후에도 <b>지우지 않는다</b>
    /// (구버전 빌드로 되돌아가도 진행이 남아야 한다).</summary>
    private const string LegacyPrefsKey = "questSystem";

    private static string FilePath(int slot) => SaveFileIO.PathFor($"quest_save_{slot}.json");

    public bool HasSave(int slot) => SaveFileIO.Exists(FilePath(slot));

    /// <summary>저장된 퀘스트 JSON 원문(없으면 null).</summary>
    public string Load(int slot) => SaveFileIO.ReadWithBackup(FilePath(slot), Tag);

    public void Save(int slot, string json) => SaveFileIO.WriteAtomic(FilePath(slot), json, Tag);

    public void Delete(int slot) => SaveFileIO.Delete(FilePath(slot));

    /// <summary>
    /// 레거시 PlayerPrefs 진행도를 지정 슬롯 파일로 1회 이전한다.
    /// 대상 슬롯에 파일이 이미 있으면 덮어쓰지 않는다(슬롯 데이터 우선).
    /// </summary>
    public void MigrateIfNeeded(int slot)
    {
        if (HasSave(slot)) return;
        if (!PlayerPrefs.HasKey(LegacyPrefsKey)) return;

        string json = PlayerPrefs.GetString(LegacyPrefsKey);
        if (string.IsNullOrWhiteSpace(json)) return;

        Save(slot, json);
        Debug.Log($"[{Tag}] 레거시 PlayerPrefs 퀘스트 → 슬롯{slot} 파일 이전 완료(원본 보존)");
    }
}
