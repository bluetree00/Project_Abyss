using System;
using System.IO;
using UnityEngine;

/// <summary>
/// persistentDataPath 기반 로컬 런 세이브 저장소. 고정 3슬롯(0~2), 슬롯별 독립 파일.
///
/// - 파일: run_save_{slot}.json (+ 슬롯별 .tmp / .bak). 빈 슬롯 = 파일 없음.
/// - 직렬화: JsonUtility (프로젝트의 DataManager 캐시 패턴과 동일).
/// - 원자적 쓰기: tmp 파일 기록 → 기존본 .bak 백업 → tmp를 본파일로 교체.
///   쓰기 도중 종료돼도 본파일/백업 중 하나는 온전하게 남는다.
/// - Steam Auto-Cloud: 이 파일 경로를 파트너 설정에 등록하면 코드 변경 없이 동기화된다.
/// - 마이그레이션: 단일 run_save.json(레거시)이 있으면 slotIndex 기준 슬롯 파일로 1회 이전한다.
/// </summary>
public sealed class LocalFileRunSaveStore : IRunSaveStore
{
    private const int SlotCount = 3;

    // 레거시 단일 세이브(슬롯 도입 이전). 마이그레이션 후 보관용 이름으로 변경한다(삭제 금지).
    private const string LegacyName       = "run_save.json";
    private const string LegacyBakName    = "run_save.bak";
    private const string LegacyArchive    = "run_save.legacy.bak";
    private const string LegacyBakArchive = "run_save.legacy2.bak";

    private static string Dir => Application.persistentDataPath;

    private static string FilePath(int slot) => Path.Combine(Dir, $"run_save_{slot}.json");
    private static string TempPath(int slot) => Path.Combine(Dir, $"run_save_{slot}.tmp");
    private static string BakPath(int slot)  => Path.Combine(Dir, $"run_save_{slot}.bak");

    public bool HasSave(int slot)
    {
        var data = Load(slot);
        return data != null && data.hasActiveRun;
    }

    public RunSaveData Load(int slot)
    {
        if (!IsValidSlot(slot)) return null;

        var data = TryReadFrom(FilePath(slot));
        if (data != null) return data;

        // 본파일 손상/부재 → 백업 폴백
        var bak = TryReadFrom(BakPath(slot));
        if (bak != null)
            Debug.LogWarning($"[RunSaveStore] 슬롯{slot} 본 세이브 손상 — .bak에서 복구");
        return bak;
    }

    public void Save(int slot, RunSaveData data)
    {
        if (data == null || !IsValidSlot(slot)) return;

        try
        {
            string json = JsonUtility.ToJson(data);
            File.WriteAllText(TempPath(slot), json);

            if (File.Exists(FilePath(slot)))
            {
                File.Copy(FilePath(slot), BakPath(slot), true);
                // 본 세이브의 사이드카 서명도 백업과 함께 따라가게 복사(.bak 폴백 검증 유지).
                SaveIntegrity.CopySidecar(FilePath(slot), BakPath(slot));
                File.Delete(FilePath(slot));
            }
            File.Move(TempPath(slot), FilePath(slot));

            // 새 본파일의 서명을 사이드카에 기록(실패해도 세이브는 성공 — 격리됨).
            SaveIntegrity.WriteSidecar(FilePath(slot), json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[RunSaveStore] 슬롯{slot} 저장 실패: {e}");
        }
    }

    public void Delete(int slot)
    {
        if (!IsValidSlot(slot)) return;
        TryDelete(FilePath(slot));
        TryDelete(TempPath(slot));
        TryDelete(BakPath(slot));
        SaveIntegrity.DeleteSidecar(FilePath(slot));
        SaveIntegrity.DeleteSidecar(BakPath(slot));
    }

    /// <summary>
    /// 레거시 단일 run_save.json → 슬롯 파일 1회 이전. 기존 세이브 무손실이 최우선.
    /// - 레거시가 없으면 무동작.
    /// - 양쪽(본/백업) 모두 손상이면 보류(파일 보존, 재시도 가능).
    /// - 대상 슬롯 파일이 이미 있으면 덮어쓰지 않음(신규 슬롯 데이터 우선).
    /// - 레거시 원본은 삭제하지 않고 이름만 변경해 보관(재마이그레이션 방지 + 폴백).
    /// </summary>
    public void MigrateIfNeeded()
    {
        try
        {
            string legacyPath    = Path.Combine(Dir, LegacyName);
            string legacyBakPath = Path.Combine(Dir, LegacyBakName);
            if (!File.Exists(legacyPath) && !File.Exists(legacyBakPath)) return;

            var data = TryReadFrom(legacyPath) ?? TryReadFrom(legacyBakPath);
            if (data == null)
            {
                Debug.LogWarning("[RunSaveStore] 레거시 단일 세이브 읽기 실패 — 마이그레이션 보류(원본 보존)");
                return;
            }

            int slot = Mathf.Clamp(data.slotIndex, 0, SlotCount - 1);

            if (!File.Exists(FilePath(slot)))
            {
                Save(slot, data);
                Debug.Log($"[RunSaveStore] 레거시 세이브 → 슬롯{slot} 마이그레이션 완료");
            }
            else
            {
                Debug.Log($"[RunSaveStore] 슬롯{slot} 파일 존재 — 레거시 보관만(덮어쓰기 안 함)");
            }

            // 무손실 가드: 슬롯 파일이 실제 존재할 때만(Save 성공 또는 선존) 레거시를 치운다.
            // Save가 실패(예외 삼킴)해 슬롯 파일이 없으면 레거시를 보존 → 다음 실행 재마이그레이션.
            if (!File.Exists(FilePath(slot)))
            {
                Debug.LogWarning($"[RunSaveStore] 슬롯{slot} 파일 생성 실패 — 레거시 보존(다음 실행 재시도)");
                return;
            }

            // 원본 보존(삭제 금지): 이름 변경으로 다음 실행 재마이그레이션 방지.
            ArchiveLegacy(legacyPath,    LegacyArchive);
            ArchiveLegacy(legacyBakPath, LegacyBakArchive);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RunSaveStore] 마이그레이션 예외(원본 보존): {e.Message}");
        }
    }

    // ── Private ──────────────────────────────────────────────

    private static bool IsValidSlot(int slot) => slot >= 0 && slot < SlotCount;

    private static void ArchiveLegacy(string srcPath, string archiveName)
    {
        try
        {
            if (!File.Exists(srcPath)) return;
            string dstPath = Path.Combine(Dir, archiveName);
            if (File.Exists(dstPath)) File.Delete(dstPath);
            File.Move(srcPath, dstPath);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RunSaveStore] 레거시 보관 실패 ({Path.GetFileName(srcPath)}): {e.Message}");
        }
    }

    private static RunSaveData TryReadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return null;

            // 사이드카 서명 검증: 불일치해도 경고만, 로드는 진행(데이터 손실 0).
            // .sig 없음(레거시/외부 동기화)은 정상 — 경고 없이 통과.
            if (SaveIntegrity.Verify(path, json) == SaveIntegrity.SignatureStatus.Mismatch)
                Debug.LogWarning($"[SaveIntegrity] 서명 불일치 — 변조 의심 ({Path.GetFileName(path)})");

            var data = JsonUtility.FromJson<RunSaveData>(json);
            if (data == null) return null;

            // 반환 직전 중앙 정규화 — 모든 호출자(Load/HasSave/Migrate)가 클램프된 값 수령.
            SaveSanitizer.Sanitize(data);
            return data;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RunSaveStore] 읽기 실패 ({Path.GetFileName(path)}): {e.Message}");
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception e) { Debug.LogWarning($"[RunSaveStore] 삭제 실패 ({Path.GetFileName(path)}): {e.Message}"); }
    }
}
