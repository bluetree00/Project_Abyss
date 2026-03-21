using System;
using BackEnd;
using Cysharp.Threading.Tasks;
using Steamworks;
using UnityEngine;

/// <summary>
/// Steam 세션 티켓 획득 후 뒤끝 서버 Federation 로그인 처리
/// </summary>
public static class SteamLoginService
{
    public static bool IsLoggedIn { get; private set; }

    public static async UniTask<bool> LoginAsync()
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogError("[SteamLogin] SteamManager가 초기화되지 않았습니다.");
            return false;
        }

        string ticket = await GetAuthTicketAsync();
        if (string.IsNullOrEmpty(ticket))
        {
            Debug.LogError("[SteamLogin] 세션 티켓 획득 실패.");
            return false;
        }

        Debug.Log($"[SteamLogin] 티켓 획득 완료 | len={ticket.Length} prefix={ticket.Substring(0, Math.Min(32, ticket.Length))}");
        Debug.Log("[SteamLogin] 뒤끝 Federation 로그인 시도...");

        bool result = await FederationLoginAsync(ticket);
        IsLoggedIn = result;
        return result;
    }

    private static UniTask<string> GetAuthTicketAsync()
    {
        byte[] buffer = new byte[1024];
        var identity = new SteamNetworkingIdentity();
        identity.SetSteamID(SteamUser.GetSteamID());
        HAuthTicket handle = SteamUser.GetAuthSessionTicket(buffer, buffer.Length, out uint ticketSize, ref identity);

        if (handle == HAuthTicket.Invalid || ticketSize == 0)
        {
            Debug.LogError("[SteamLogin] GetAuthSessionTicket 실패 — 티켓 핸들 무효");
            return UniTask.FromResult<string>(null);
        }

        string hex = BitConverter.ToString(buffer, 0, (int)ticketSize).Replace("-", "").ToLower();
        Debug.Log($"[SteamLogin] 세션 티켓 획득 완료 | size={ticketSize} prefix={hex.Substring(0, Math.Min(32, hex.Length))}");
        return UniTask.FromResult(hex);
    }

    private static UniTask<bool> FederationLoginAsync(string ticket)
    {
        var tcs = new UniTaskCompletionSource<bool>();

        Backend.BMember.AuthorizeFederation(ticket, FederationType.Steam, callback =>
        {
            if (callback.IsSuccess())
            {
                Debug.Log("[SteamLogin] 뒤끝 Federation 로그인 성공.");
                tcs.TrySetResult(true);
            }
            else
            {
                Debug.LogError($"[SteamLogin] 뒤끝 Federation 로그인 실패 | status={callback.GetStatusCode()} errorCode={callback.GetErrorCode()} message={callback.GetMessage()}");
                tcs.TrySetResult(false);
            }
        });

        return tcs.Task;
    }
}
