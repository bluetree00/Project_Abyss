using System;
using UnityEngine;

/// <summary>
/// 영구 메타(UserGameData) 로컬 저장소. <b>슬롯별 파일</b>(<c>meta_save_{slot}.json</c>).
///
/// 슬롯 = 독립 세이브. 각성·심연의 정수·보스 봉인·누적 통계가 슬롯마다 따로 자란다.
/// 뒤끝은 계정당 1행이라 슬롯을 담지 못하므로, 로컬이 슬롯별 권위이고 서버는 활성 슬롯의
/// 스냅샷(백업/텔레메트리)으로만 남는다.
///
/// 손상 시 .bak 폴백, 읽기 반환 직전 SaveSanitizer 정규화 — 거부·삭제는 절대 하지 않는다.
/// </summary>
public sealed class LocalFileMetaStore
{
    private const string Tag = "MetaStore";

    /// <summary>슬롯 도입 이전의 단일 메타. 마이그레이션 후 이름만 바꿔 보관한다(삭제 금지).</summary>
    private const string LegacyName    = "meta_save.json";
    private const string LegacyArchive = "meta_save.legacy.bak";

    private static string FilePath(int slot) => SaveFileIO.PathFor($"meta_save_{slot}.json");

    public bool HasSave(int slot) => SaveFileIO.Exists(FilePath(slot));

    /// <summary>슬롯 메타를 로드한다(없거나 손상 시 null → 호출측이 시작값을 정한다).</summary>
    public UserGameData Load(int slot)
    {
        string json = SaveFileIO.ReadWithBackup(FilePath(slot), Tag);
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var data = JsonUtility.FromJson<UserGameData>(json);
            if (data == null) return null;

            // 반환 직전 중앙 정규화 — 모든 호출자가 클램프된 값을 수령한다.
            SaveSanitizer.Sanitize(data);
            return data;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[{Tag}] 슬롯{slot} 역직렬화 실패: {e.Message}");
            return null;
        }
    }

    public void Save(int slot, UserGameData data)
    {
        if (data == null) return;
        SaveFileIO.WriteAtomic(FilePath(slot), JsonUtility.ToJson(data), Tag);
    }

    public void Delete(int slot) => SaveFileIO.Delete(FilePath(slot));

    /// <summary>
    /// 레거시 단일 <c>meta_save.json</c>을 지정 슬롯으로 1회 이전한다. 기존 진행 무손실이 최우선.
    /// - 대상 슬롯에 파일이 이미 있으면 덮어쓰지 않는다.
    /// - 이전에 성공했을 때만 레거시를 보관용 이름으로 바꾼다(실패 시 다음 실행 재시도).
    /// </summary>
    public void MigrateIfNeeded(int slot)
    {
        if (HasSave(slot)) return;

        string legacyPath = SaveFileIO.PathFor(LegacyName);
        if (!SaveFileIO.Exists(legacyPath)) return;

        string json = SaveFileIO.ReadWithBackup(legacyPath, Tag);
        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogWarning($"[{Tag}] 레거시 메타 읽기 실패 — 마이그레이션 보류(원본 보존)");
            return;
        }

        SaveFileIO.WriteAtomic(FilePath(slot), json, Tag);

        if (!HasSave(slot))
        {
            Debug.LogWarning($"[{Tag}] 슬롯{slot} 파일 생성 실패 — 레거시 보존(다음 실행 재시도)");
            return;
        }

        SaveFileIO.Archive(legacyPath, LegacyArchive, Tag);
        Debug.Log($"[{Tag}] 레거시 메타 → 슬롯{slot} 마이그레이션 완료(원본은 {LegacyArchive}로 보관)");
    }
}
