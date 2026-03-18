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

    private static async UniTask<string> GetAuthTicketAsync()
    {
        var tcs = new UniTaskCompletionSource<string>();

        Callback<GetTicketForWebApiResponse_t> cb = null;
        cb = Callback<GetTicketForWebApiResponse_t>.Create(response =>
        {
            cb.Dispose();
            Debug.Log($"[SteamLogin] 티켓 콜백 수신 | result={response.m_eResult} size={response.m_cubTicket}");

            if (response.m_eResult == EResult.k_EResultOK)
            {
                string hex = BitConverter.ToString(response.m_rgubTicket, 0, response.m_cubTicket).Replace("-", "").ToLower();
                tcs.TrySetResult(hex);
            }
            else
            {
                Debug.LogError($"[SteamLogin] GetAuthTicketForWebApi 실패: {response.m_eResult}");
                tcs.TrySetResult(null);
            }
        });

        Debug.Log("[SteamLogin] GetAuthTicketForWebApi 호출...");
        SteamUser.GetAuthTicketForWebApi("");

        // 10초 타임아웃
        var timeoutTask = UniTask.Delay(10000).ContinueWith(() => (string)null);
        var (winIndex, ticketResult, _) = await UniTask.WhenAny(tcs.Task, timeoutTask);
        if (winIndex == 1)
        {
            cb.Dispose();
            Debug.LogError("[SteamLogin] 티켓 콜백 타임아웃 (10초 초과)");
            return null;
        }
        return ticketResult;
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
