using UnityEngine;
using BackEnd;
using Cysharp.Threading.Tasks;

/// <summary>
/// DEV 전용: 게임 시작 시 뒤끝 자동 로그인 → (선택) 게임데이터 로드까지 트리거
/// - 에디터/개발빌드에서만 동작
/// - SDK에 IsLogin/GetUserInDate가 없어도 "콜백 성공"으로 상태를 관리
/// </summary>
public sealed class DevAutoLoginBootstrap : MonoBehaviour
{
    // --------------------
    // Config (Inspector)
    // --------------------
    [Header("DEV ONLY")]
    [SerializeField] private bool enableAutoLogin = true;

    [Header("Credentials (DEV)")]
    [SerializeField] private string devId = "bg";
    [SerializeField] private string devPw = "1234";

    [Header("Next Step (Optional)")]
    [Tooltip("로그인 성공 후 GameDataLoad까지 자동으로 호출")]
    [SerializeField] private bool loadGameDataAfterLogin = true;

    [Tooltip("게임데이터 로드 완료 시 로비로 이동(프로젝트에 맞게 코드 연결 필요)")]
    [SerializeField] private bool loadLobbyOnReady = false;

    // --------------------
    // State
    // --------------------
    private bool _initialized;
    private bool _loginInProgress;

    public static bool IsLoggedIn { get; private set; }

    private const string TAG = "[DevAutoLogin]";

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // 안전장치: 에디터/개발빌드에서만 자동 로그인 실행
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        enableAutoLogin = false;
#endif
        if (!enableAutoLogin)
        {
            Log("Disabled.");
            return;
        }

        Boot();
    }

    // --------------------
    // Boot Flow
    // --------------------
    private void Boot()
    {
        if (!TryInitializeBackend()) return;
        TryLogin();
    }

    private bool TryInitializeBackend()
    {
        if (_initialized) return true;

        // AppBootstrapper가 이미 초기화한 경우 재사용
        if (AppBootstrapper.IsBackendInitialized)
        {
            _initialized = true;
            return true;
        }

        var init = Backend.Initialize();
        if (!init.IsSuccess())
        {
            LogError($"Initialize FAIL | code={init.GetStatusCode()} msg={init.GetMessage()}");
            return false;
        }

        _initialized = true;
        Log("Initialize OK");
        return true;
    }

    private void TryLogin()
    {
        if (_loginInProgress) return;
        _loginInProgress = true;

        Log($"Login... id={devId}");

        Backend.BMember.CustomLogin(devId, devPw, callback =>
        {
            _loginInProgress = false;

            if (!callback.IsSuccess())
            {
                IsLoggedIn = false;
                LogError($"Login FAIL | code={callback.GetStatusCode()} msg={callback.GetMessage()}");
                return;
            }

            IsLoggedIn = true;

            // UserInfo.Data는 로그인 후 채워지는 구조(너 프로젝트가 이미 사용 중)
            Log($"Login OK | gamerId={UserInfo.Data.gamerId} nickname={(string.IsNullOrEmpty(UserInfo.Data.nickname) ? "(null)" : UserInfo.Data.nickname)}");

            if (loadGameDataAfterLogin)
            {
                LoadGameData();
            }
            else
            {
                OnReady();
            }
        });
    }

    // --------------------
    // Game Data
    // --------------------
    private void LoadGameData()
    {
        LoadGameDataAsync().Forget();
    }

    private async UniTaskVoid LoadGameDataAsync()
    {
        Log("GameData Load...");
        await BackendGameData.Instance.LoadAsync();
        Log("GameData Load OK");
        OnReady();
    }

    private void OnReady()
    {
        Log("READY");

        // 로비씬에 이미 있는 경우 유저 정보 fetch 트리거
        var lobby = FindObjectOfType<LobbyScenario>();
        lobby?.FetchUserInfo();

        if (!loadLobbyOnReady) return;

        // TODO: 프로젝트 씬 로더에 연결
        // SceneUtilitys.LoadScene(SceneNames.Lobby);
        Log("TODO: Load Lobby");
    }

    // --------------------
    // Logging helpers
    // --------------------
    private static void Log(string msg) => Debug.Log($"{TAG} {msg}");
    private static void LogError(string msg) => Debug.LogError($"{TAG} {msg}");

    // --------------------
    // Debug helpers
    // --------------------
    [ContextMenu("DEV: Force Login")]
    private void ForceLogin()
    {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        Log("ForceLogin disabled in release build.");
        return;
#endif
        _loginInProgress = false;
        TryLogin();
    }
}
