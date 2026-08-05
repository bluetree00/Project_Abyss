using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 로컬 세이브 파일의 공용 입출력 — 원자적 쓰기 · .bak 폴백 · 무결성 사이드카(.sig)를 한 곳에 모은다.
///
/// 런 세이브와 메타 세이브가 같은 절차를 각자 들고 있었고 퀘스트 세이브가 세 번째가 되는 시점이라,
/// <b>절차만</b> 여기로 모았다. 무엇을 담느냐(스키마·정규화·마이그레이션 정책)는 각 스토어가 계속 소유한다.
///
/// 파일 3형제 규약: 본파일 <c>name.json</c> · 임시 <c>name.tmp</c> · 백업 <c>name.bak</c>.
/// 쓰기 도중 종료돼도 본파일/백업 중 하나는 온전하게 남는다.
/// </summary>
public static class SaveFileIO
{
    public static string Dir => Application.persistentDataPath;

    /// <summary>persistentDataPath 기준 절대 경로.</summary>
    public static string PathFor(string fileName) => Path.Combine(Dir, fileName);

    public static string BakPathOf(string mainPath) => Path.ChangeExtension(mainPath, ".bak");
    public static string TmpPathOf(string mainPath) => Path.ChangeExtension(mainPath, ".tmp");

    /// <summary>본파일 또는 백업이 존재하는지(= 이 슬롯에 저장이 있는지).</summary>
    public static bool Exists(string mainPath)
        => File.Exists(mainPath) || File.Exists(BakPathOf(mainPath));

    /// <summary>
    /// 원자적 쓰기: tmp 기록 → 기존본을 .bak으로 백업(사이드카 동반) → tmp를 본파일로 교체 → 새 서명 기록.
    /// 실패해도 예외를 전파하지 않는다(세이브 실패가 게임 흐름을 끊지 않게).
    /// </summary>
    public static bool WriteAtomic(string mainPath, string json, string tag)
    {
        try
        {
            string tmp = TmpPathOf(mainPath);
            File.WriteAllText(tmp, json);

            if (File.Exists(mainPath))
            {
                string bak = BakPathOf(mainPath);
                File.Copy(mainPath, bak, true);
                SaveIntegrity.CopySidecar(mainPath, bak);   // .bak 폴백도 검증 가능하게
                File.Delete(mainPath);
            }
            File.Move(tmp, mainPath);

            // 서명 기록 실패는 격리 — 세이브 자체는 성공으로 둔다.
            SaveIntegrity.WriteSidecar(mainPath, json);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[{tag}] 저장 실패 ({Path.GetFileName(mainPath)}): {e}");
            return false;
        }
    }

    /// <summary>본파일 → 손상/부재 시 .bak 폴백. 둘 다 없으면 null.</summary>
    public static string ReadWithBackup(string mainPath, string tag)
    {
        var json = TryRead(mainPath, tag);
        if (json != null) return json;

        var bak = TryRead(BakPathOf(mainPath), tag);
        if (bak != null)
            Debug.LogWarning($"[{tag}] 본 세이브 손상 — .bak에서 복구 ({Path.GetFileName(mainPath)})");
        return bak;
    }

    /// <summary>본파일 · tmp · bak · 사이드카를 모두 정리한다.</summary>
    public static void Delete(string mainPath)
    {
        TryDelete(mainPath);
        TryDelete(TmpPathOf(mainPath));
        TryDelete(BakPathOf(mainPath));
        SaveIntegrity.DeleteSidecar(mainPath);
        SaveIntegrity.DeleteSidecar(BakPathOf(mainPath));
    }

    /// <summary>원본을 지우지 않고 이름만 바꿔 보관한다(레거시 마이그레이션 후 재이전 방지).</summary>
    public static void Archive(string mainPath, string archiveFileName, string tag)
    {
        try
        {
            if (!File.Exists(mainPath)) return;
            string dst = PathFor(archiveFileName);
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(mainPath, dst);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[{tag}] 레거시 보관 실패 ({Path.GetFileName(mainPath)}): {e.Message}");
        }
    }

    // ── Private ──────────────────────────────────────────────

    private static string TryRead(string path, string tag)
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

            return json;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[{tag}] 읽기 실패 ({Path.GetFileName(path)}): {e.Message}");
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception e) { Debug.LogWarning($"[SaveFileIO] 삭제 실패 ({Path.GetFileName(path)}): {e.Message}"); }
    }
}
