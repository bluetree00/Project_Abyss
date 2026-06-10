using System;
using System.IO;
using UnityEngine;

/// <summary>
/// persistentDataPath 기반 로컬 런 세이브 저장소.
///
/// - 직렬화: JsonUtility (프로젝트의 DataManager 캐시 패턴과 동일).
/// - 원자적 쓰기: tmp 파일 기록 → 기존본 .bak 백업 → tmp를 본파일로 교체.
///   쓰기 도중 종료돼도 본파일/백업 중 하나는 온전하게 남는다.
/// - Steam Auto-Cloud: 이 파일 경로를 파트너 설정에 등록하면 코드 변경 없이 동기화된다.
/// </summary>
public sealed class LocalFileRunSaveStore : IRunSaveStore
{
    private const string FileName = "run_save.json";
    private const string TempName = "run_save.tmp";
    private const string BakName  = "run_save.bak";

    private static string Dir      => Application.persistentDataPath;
    private static string FilePath => Path.Combine(Dir, FileName);
    private static string TempPath => Path.Combine(Dir, TempName);
    private static string BakPath  => Path.Combine(Dir, BakName);

    public bool HasSave()
    {
        var data = Load();
        return data != null && data.hasActiveRun;
    }

    public RunSaveData Load()
    {
        var data = TryReadFrom(FilePath);
        if (data != null) return data;

        // 본파일 손상/부재 → 백업 폴백
        var bak = TryReadFrom(BakPath);
        if (bak != null)
            Debug.LogWarning("[RunSaveStore] 본 세이브 손상 — .bak에서 복구");
        return bak;
    }

    public void Save(RunSaveData data)
    {
        if (data == null) return;

        try
        {
            string json = JsonUtility.ToJson(data);
            File.WriteAllText(TempPath, json);

            if (File.Exists(FilePath))
            {
                File.Copy(FilePath, BakPath, true);
                File.Delete(FilePath);
            }
            File.Move(TempPath, FilePath);
        }
        catch (Exception e)
        {
            Debug.LogError($"[RunSaveStore] 저장 실패: {e}");
        }
    }

    public void Delete()
    {
        TryDelete(FilePath);
        TryDelete(TempPath);
        TryDelete(BakPath);
    }

    // ── Private ──────────────────────────────────────────────

    private static RunSaveData TryReadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonUtility.FromJson<RunSaveData>(json);
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
