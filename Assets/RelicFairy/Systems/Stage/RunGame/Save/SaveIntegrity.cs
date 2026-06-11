using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// 로컬 세이브 변조 탐지(tamper-evidence). 본 .json 평문은 그대로 두고,
/// HMAC-SHA256 서명을 별도 사이드카 파일(<paramref/>.sig)에 기록한다.
///
/// 사이드카 방식 채택 근거:
/// - 본 .json 포맷을 바꾸지 않아 기존 세이브 · Steam Auto-Cloud · 외부 툴이 그대로 호환된다.
/// - .sig가 없으면(레거시/외부 동기화) 정상 로드 → 데이터 손실 0.
///
/// ⚠ 보안 한계: 키가 빌드에 내장되므로 결정적 해커는 서명을 재계산해 우회할 수 있다.
///   목적은 "메모장으로 .json을 직접 편집하는 캐주얼 변조" 탐지뿐이다.
///   서버 권위 검증이 필요하면 백엔드에서 별도로 수행해야 한다.
///
/// 현재 정책: 불일치해도 거부하지 않고 경고만 남긴다(데이터 손실 0 우선).
/// ── 향후 "거부 모드" 전환 지점: Verify 결과가 Mismatch일 때 호출측에서
///    로드를 차단하려면 LocalFile*Store.TryReadFrom의 Mismatch 분기에서 null 반환.
///    (전환 전 반드시 정품 세이브 전수가 .sig를 갖도록 마이그레이션 필요.)
/// </summary>
public static class SaveIntegrity
{
    public enum SignatureStatus
    {
        NoSignature, // 사이드카 없음(레거시/외부 동기화) — 정상 로드
        Valid,       // 서명 일치
        Mismatch,    // 서명 불일치 — 변조 의심(그래도 로드)
    }

    private const string SidecarExt = ".sig";

    // 빌드 내장 키. 결정적 해커는 우회 가능(위 주석 참조) — 캐주얼 변조 탐지 전용.
    private const string SecretKey = "RelicFairy.SaveIntegrity.v1.4f2a9c1e-do-not-relocate";

    private static byte[] KeyBytes => Encoding.UTF8.GetBytes(SecretKey);

    /// <summary>payload(본 .json 문자열)에 대한 HMAC-SHA256 16진 문자열.</summary>
    public static string Sign(string payload)
    {
        using var hmac = new HMACSHA256(KeyBytes);
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload ?? string.Empty));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (byte b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>
    /// 본파일 경로의 사이드카(.sig)를 읽어 payload 서명과 비교한다.
    /// .sig 없음 → NoSignature(정상). 읽기 실패도 NoSignature로 취급(거부 금지).
    /// </summary>
    public static SignatureStatus Verify(string mainPath, string payload)
    {
        try
        {
            string sigPath = SidecarPath(mainPath);
            if (!File.Exists(sigPath)) return SignatureStatus.NoSignature;

            string stored = File.ReadAllText(sigPath)?.Trim();
            if (string.IsNullOrEmpty(stored)) return SignatureStatus.NoSignature;

            string expected = Sign(payload);
            return string.Equals(stored, expected, StringComparison.OrdinalIgnoreCase)
                ? SignatureStatus.Valid
                : SignatureStatus.Mismatch;
        }
        catch (Exception e)
        {
            // 검증 자체가 실패해도 로드는 막지 않는다(데이터 손실 0).
            Debug.LogWarning($"[SaveIntegrity] 서명 검증 예외(무시): {e.Message}");
            return SignatureStatus.NoSignature;
        }
    }

    /// <summary>
    /// 본파일 payload의 서명을 사이드카(.sig)에 기록한다(tmp→move 원자적).
    /// 서명 쓰기 실패가 본 세이브 저장을 막지 않도록 절대 예외를 전파하지 않는다.
    /// </summary>
    public static void WriteSidecar(string mainPath, string payload)
    {
        try
        {
            string sigPath = SidecarPath(mainPath);
            string tmpPath = sigPath + ".tmp";
            File.WriteAllText(tmpPath, Sign(payload));
            if (File.Exists(sigPath)) File.Delete(sigPath);
            File.Move(tmpPath, sigPath);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveIntegrity] 서명 기록 실패(세이브는 정상): {e.Message}");
        }
    }

    /// <summary>본파일의 .bak 등 사본을 만들 때 사이드카도 함께 따라가게 복사한다.</summary>
    public static void CopySidecar(string srcMainPath, string dstMainPath)
    {
        try
        {
            string src = SidecarPath(srcMainPath);
            if (!File.Exists(src)) return;
            File.Copy(src, SidecarPath(dstMainPath), true);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveIntegrity] 서명 복사 실패: {e.Message}");
        }
    }

    /// <summary>본파일 삭제 시 사이드카도 정리한다.</summary>
    public static void DeleteSidecar(string mainPath)
    {
        try
        {
            string sigPath = SidecarPath(mainPath);
            if (File.Exists(sigPath)) File.Delete(sigPath);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveIntegrity] 서명 삭제 실패: {e.Message}");
        }
    }

    private static string SidecarPath(string mainPath) => mainPath + SidecarExt;
}
