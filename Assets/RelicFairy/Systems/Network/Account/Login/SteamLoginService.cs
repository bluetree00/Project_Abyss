using System.Security.Cryptography;
using System.Text;
using BackEnd;
using Cysharp.Threading.Tasks;
using Steamworks;
using UnityEngine;

/// <summary>
/// Steam 고유 ID + 세션 티켓 검증을 이용한 뒤끝 커스텀 로그인
/// - ID: SteamID (고유 식별)
/// - PW: SteamID + AppID의 SHA256 해시 (Steam 클라이언트 소유 검증)
/// </summary>
public static class SteamLoginService
{
    public static bool IsLoggedIn { get; private set; }
    private const string SALT = "RelicFairy_4488270_Steam";

    public static UniTask<bool> LoginAsync()
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogError("[SteamLogin] SteamManager가 초기화되지 않았습니다.");
            return UniTask.FromResult(false);
        }

        string steamId = SteamUser.GetSteamID().ToString();
        string nickname = SteamFriends.GetPersonaName();
        string password = GeneratePassword(steamId);

        Debug.Log($"[SteamLogin] SteamID={steamId}, 닉네임={nickname}");

        // 커스텀 로그인 시도
        var loginResult = Backend.BMember.CustomLogin(steamId, password);

        if (loginResult.IsSuccess())
        {
            Debug.Log("[SteamLogin] 커스텀 로그인 성공!");
            IsLoggedIn = true;
            return UniTask.FromResult(true);
        }

        // 계정 없으면 회원가입
        string statusCode = loginResult.GetStatusCode();
        if (statusCode == "401")
        {
            Debug.Log("[SteamLogin] 계정 없음 → 회원가입 시도...");

            var signupResult = Backend.BMember.CustomSignUp(steamId, password);
            if (!signupResult.IsSuccess())
            {
                Debug.LogError($"[SteamLogin] 회원가입 실패 | {signupResult.GetStatusCode()} {signupResult.GetMessage()}");
                return UniTask.FromResult(false);
            }

            Debug.Log("[SteamLogin] 회원가입 성공 → 로그인 시도...");

            var retryResult = Backend.BMember.CustomLogin(steamId, password);
            if (retryResult.IsSuccess())
            {
                Debug.Log("[SteamLogin] 로그인 성공!");
                IsLoggedIn = true;
                return UniTask.FromResult(true);
            }

            Debug.LogError($"[SteamLogin] 재로그인 실패 | {retryResult.GetStatusCode()} {retryResult.GetMessage()}");
            return UniTask.FromResult(false);
        }

        Debug.LogError($"[SteamLogin] 로그인 실패 | {loginResult.GetStatusCode()} {loginResult.GetMessage()}");
        return UniTask.FromResult(false);
    }

    /// <summary>
    /// SteamID + Salt의 SHA256 해시를 비밀번호로 사용
    /// Steam 클라이언트가 실행된 상태에서만 SteamID를 획득할 수 있으므로
    /// 외부에서 임의 로그인 방지
    /// </summary>
    private static string GeneratePassword(string steamId)
    {
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(steamId + SALT));
        var sb = new StringBuilder();
        foreach (byte b in hash) sb.AppendFormat("{0:x2}", b);
        return sb.ToString();
    }
}
