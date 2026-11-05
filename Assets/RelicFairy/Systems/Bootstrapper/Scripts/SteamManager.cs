using UnityEngine;
using Steamworks;

/// <summary>
/// SteamAPI 라이프사이클 관리 (Init / RunCallbacks / Shutdown)
/// AppBootstrapper가 생성하며 DontDestroyOnLoad로 앱 전체 유지
/// </summary>
public sealed class SteamManager : MonoBehaviour
{
    /// <summary>Steam 앱 번호(steam_appid.txt와 같은 값).</summary>
    public const uint AppId = 4488270;

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

#if !UNITY_EDITOR
        // 11-06 Steam 밖에서 exe를 직접 켜면 Steam으로 다시 켠다 — Steam 없이 켜지면 계정을 몰라 Steam 계정 세이브 폴더(SaveRoot)를 못 찾아 진행이 사라진 것처럼 보였다.
        // exe 옆에 steam_appid.txt가 있으면(개발 빌드 — BuildScript가 복사) Steam이 false를 돌려줘 그대로 켜진다. Steam 업로드에서는 그 파일을 뺀다.
        try
        {
            if (SteamAPI.RestartAppIfNecessary(new AppId_t(AppId)))
            {
                Application.Quit();
                return;
            }
        }
        catch (System.DllNotFoundException e)
        {
            Debug.LogError($"[SteamManager] steam_api64.dll을 못 찾았다 — Steam 없이 계속: {e.Message}");
        }
#endif

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
        if (!Initialized) return;
        SteamAPI.RunCallbacks();
        SteamAchievements.Tick();   // 11-06 — 이번 프레임에 새로 연 도전과제를 한 번에 보낸다
    }

    private void OnDestroy()
    {
        if (!Initialized) return;

        SteamAPI.Shutdown();
        Initialized = false;
    }
}
