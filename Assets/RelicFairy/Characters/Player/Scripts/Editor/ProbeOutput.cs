using System;
using System.IO;

/// <summary>
/// 실측 도구의 결과 파일 입출력. <b>시작하자마자 파일을 표시해 두는 것</b>이 핵심이다.
///
/// 실측 도구는 대개 끝에서야 결과 파일을 쓴다. 그래서 도중에 끊기면(플레이 종료·도메인 리로드·예외)
/// <b>옛 결과 파일이 그대로 남아 새 결과처럼 보인다</b> — 09-21에 실제로 3시간 전 파일을 새 결과로 읽을 뻔했고,
/// UI 세션도 같은 함정으로 빈 기록을 새 기록으로 볼 뻔했다.
/// 시작 시각을 먼저 적어 두면, 끊긴 실행은 「아직 안 끝남」 한 줄만 남아 한눈에 구별된다.
/// </summary>
public static class ProbeOutput
{
    /// <summary>실측 시작을 파일에 먼저 남긴다. 끝나면 <see cref="Write"/>가 덮어쓴다.</summary>
    public static void Begin(string path, string title)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, $"[{title}] {DateTime.Now:HH:mm:ss} 시작 — 아직 안 끝남(중간에 끊겼다면 이 줄만 남는다)\n");
        }
        catch (IOException) { /* 결과 파일은 보조 수단이다 — 못 써도 실측 자체는 진행한다 */ }
    }

    /// <summary>최종 결과를 쓴다. 끝난 시각을 함께 남겨 언제 잰 값인지 파일만 봐도 알 수 있다.</summary>
    public static void Write(string path, string title, string body)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, $"[{title}] {DateTime.Now:HH:mm:ss} 완료\n{body}");
        }
        catch (IOException) { }
    }
}
