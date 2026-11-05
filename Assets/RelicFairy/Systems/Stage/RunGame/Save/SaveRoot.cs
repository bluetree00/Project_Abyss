using System;
using System.IO;
using Steamworks;
using UnityEngine;

/// <summary>
/// 진행 세이브(런 · 메타 · 퀘스트 · <see cref="ProgressPrefs"/>)가 놓이는 폴더 — Steam 클라우드(Auto-Cloud) 기준(11-06 사용자 「클라우드 · 도전과제를 스팀에 맞게」).
/// Steam이 켜져 있으면 <c>persistentDataPath/{Steam 계정 번호}/</c> — Steam 문서 「저장 경로는 Steam 사용자마다 달라야 한다」 · Auto-Cloud 하위 폴더 <c>{Steam3AccountID}</c>와 같은 이름.
/// Steam 없이 켰으면(개발 · Steam 꺼짐) <c>persistentDataPath</c> 그대로. 차트 캐시 · 뒤끝 SDK 파일은 늘 바깥(동기화하지 않는 것).
/// <para>계정 폴더를 처음 쓸 때 바깥에 있던 진행 파일을 한 번 복사해 온다(이 업데이트 전 세이브 · 원본은 남긴다) — 끝나면 표식 파일을 남겨 다시 복사하지 않는다
/// (지운 슬롯이 바깥 옛 파일로 되살아나지 않게). 다른 PC에서 클라우드가 계정 폴더를 먼저 내려 주면 표식도 같이 와서 그 PC의 옛 파일은 건드리지 않는다(클라우드가 정본).</para>
/// 세이브를 처음 만지는 곳(RunProgressManager)보다 먼저 Steam을 켜야 한다 — AppBootstrapper.Awake 맨 앞.
/// </summary>
public static class SaveRoot
{
    // ── Constants ─────────────────────────────────────────────
    /// <summary>계정 폴더로 옮겨 Steam 클라우드에 올리는 진행 파일 — 이 이름으로 시작하는 파일(.json · .bak · .sig · .corrupt · 옛 이름 포함).</summary>
    private static readonly string[] ProgressPrefixes = { "run_save", "meta_save", "quest_save", ProgressPrefs.FileStem };
    /// <summary>바깥 진행 파일을 계정 폴더로 옮겼다는 표식(클라우드에도 같이 오른다).</summary>
    public const string MigratedMarker = ".root_migrated";

    // ── Static ────────────────────────────────────────────────
    private static string s_dir;

    // ── Properties ────────────────────────────────────────────
    /// <summary>진행 세이브 폴더(처음 부를 때 정해지고 그 세션 동안 바뀌지 않는다).</summary>
    public static string Dir => s_dir ??= ResolveForThisSession();
    /// <summary>지금 폴더가 Steam 계정 폴더인가.</summary>
    public static bool PerAccount { get; private set; }
    /// <summary>계정 폴더 이름(Steam이 꺼져 있으면 null).</summary>
    public static string Account { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { s_dir = null; PerAccount = false; Account = null; }

    // ── Public Methods ────────────────────────────────────────
    /// <summary>
    /// <paramref name="root"/> 아래 <paramref name="account"/> 폴더를 쓴다 — 표식이 없으면 바깥 진행 파일을 복사해 온다(있는 파일은 덮지 않음).
    /// 실패하면(디스크 가득 등) root를 돌려준다(세이브가 끊기지 않게). <paramref name="copied"/> = 이번에 복사한 파일 수.
    /// </summary>
    public static string Resolve(string root, string account, out int copied)
    {
        copied = 0;
        if (string.IsNullOrEmpty(account)) return root;
        string dir = Path.Combine(root, account);
        try
        {
            Directory.CreateDirectory(dir);
            if (!File.Exists(Path.Combine(dir, MigratedMarker)))
            {
                foreach (var path in Directory.GetFiles(root))
                {
                    string name = Path.GetFileName(path);
                    if (!IsProgressFile(name)) continue;
                    string to = Path.Combine(dir, name);
                    if (File.Exists(to)) continue;
                    File.Copy(path, to);
                    copied++;
                }
                File.WriteAllText(Path.Combine(dir, MigratedMarker), DateTime.UtcNow.ToString("o"));
            }
            return dir;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveRoot] 계정 폴더 준비 실패 — 바깥 폴더를 쓴다: {e.Message}");
            return root;
        }
    }

    /// <summary>계정 폴더로 옮기는 진행 파일인가(차트 캐시 · 뒤끝 SDK 파일 · 로그는 아니다).</summary>
    public static bool IsProgressFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        foreach (var p in ProgressPrefixes)
            if (fileName.StartsWith(p, StringComparison.Ordinal)) return true;
        return false;
    }

    // ── Private Methods ───────────────────────────────────────
    private static string ResolveForThisSession()
    {
        string root = Application.persistentDataPath;
        Account = SteamAccountFolder();
        string dir = Resolve(root, Account, out int copied);
        PerAccount = !string.Equals(dir, root, StringComparison.Ordinal);
        if (!PerAccount) Account = null;
        Debug.Log(PerAccount
            ? $"[SaveRoot] Steam 계정 폴더 {Account}{(copied > 0 ? $" · 바깥 진행 파일 {copied}개를 옮겨 옴" : "")}"
            : "[SaveRoot] Steam이 꺼져 있다 — persistentDataPath 그대로(클라우드 동기화 안 됨)");
        return dir;
    }

    private static string SteamAccountFolder()
    {
        if (!SteamManager.Initialized) return null;
        try { return SteamUser.GetSteamID().GetAccountID().m_AccountID.ToString(); }
        catch (Exception e) { Debug.LogWarning($"[SaveRoot] Steam 계정 번호를 못 읽었다: {e.Message}"); return null; }
    }
}
