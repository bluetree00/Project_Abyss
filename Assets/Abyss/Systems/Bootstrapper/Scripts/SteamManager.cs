using UnityEngine;
using Steamworks;

/// <summary>
/// SteamAPI 라이프사이클 관리 (Init / RunCallbacks / Shutdown)
/// AppBootstrapper가 생성하며 DontDestroyOnLoad로 앱 전체 유지
/// </summary>
public sealed class SteamManager : MonoBehaviour
{
    public static SteamManager Instance { get; private set; }
    public static bool Initialized { get; private set; }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (!Packsize.Test())
        {
            Debug.LogError("[SteamManager] Packsize 불일치 — 잘못된 Steamworks.NET 빌드입니다.");
            return;
        }

        if (!SteamAPI.Init())
        {
            Debug.LogError("[SteamManager] SteamAPI.Init() 실패 — Steam 클라이언트가 실행 중인지 확인하세요.");
            return;
        }

        Initialized = true;
        Debug.Log($"[SteamManager] 초기화 성공 | {SteamFriends.GetPersonaName()} ({SteamUser.GetSteamID()})");
    }

    private void Update()
    {
        if (Initialized)
            SteamAPI.RunCallbacks();
    }

    private void OnDestroy()
    {
        if (!Initialized) return;

        SteamAPI.Shutdown();
        Initialized = false;
    }
}
