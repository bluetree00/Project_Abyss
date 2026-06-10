using System;
using System.IO;
using UnityEngine;

/// <summary>
/// persistentDataPath 기반 영구 메타(UserGameData) 로컬 저장소. (PR5)
///
/// 권위 정책(보수): 로컬이 있으면 로컬 우선, 없으면 서버값을 로컬로 이관(최초 1회).
/// 저장은 로컬+뒤끝 병행(이중 기록) — 뒤끝은 백업/텔레메트리. 손상 시 .bak 폴백, 그래도 실패면 서버값 사용.
/// </summary>
public sealed class LocalFileMetaStore
{
    private const string FileName = "meta_save.json";
    private const string TempName = "meta_save.tmp";
    private const string BakName  = "meta_save.bak";

    private static string Dir      => Application.persistentDataPath;
    private static string FilePath => Path.Combine(Dir, FileName);
    private static string TempPath => Path.Combine(Dir, TempName);
    private static string BakPath  => Path.Combine(Dir, BakName);

    public bool HasSave() => File.Exists(FilePath) || File.Exists(BakPath);

    /// <summary>로컬 메타를 로드한다(없거나 손상 시 null → 호출측이 서버값 유지).</summary>
    public UserGameData Load()
    {
        var data = TryReadFrom(FilePath);
        if (data != null) return data;

        var bak = TryReadFrom(BakPath);
        if (bak != null)
            Debug.LogWarning("[MetaStore] 본 메타 손상 — .bak에서 복구");
        return bak;
    }

    public void Save(UserGameData data)
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
            Debug.LogError($"[MetaStore] 저장 실패: {e}");
        }
    }

    private static UserGameData TryReadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonUtility.FromJson<UserGameData>(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MetaStore] 읽기 실패 ({Path.GetFileName(path)}): {e.Message}");
            return null;
        }
    }
}
