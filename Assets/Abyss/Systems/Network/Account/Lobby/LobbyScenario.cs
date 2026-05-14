using Cysharp.Threading.Tasks;
using UnityEngine;

public class LobbyScenario : MonoBehaviour
{
    [SerializeField] private UserInfo user;

    // ── Lifecycle ──────────────────────────────────────────────────────────
    private void Awake()
    {
        var hud = FindAnyObjectByType<HudPresenter>(FindObjectsInactive.Include);
        if (hud != null) hud.BindLobby(user);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!DevAutoLoginBootstrap.IsLoggedIn && !SteamLoginService.IsLoggedIn)
            return;
#endif
        user.GetUserInfoFromBackend();
    }

    private async void Start()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!DevAutoLoginBootstrap.IsLoggedIn && !SteamLoginService.IsLoggedIn)
            return;
#endif
        await UniTask.WhenAll(
            BackendGameData.Instance.LoadAsync(),
            RunProgressManager.Instance != null
                ? RunProgressManager.Instance.LoadAsync()
                : UniTask.CompletedTask
        );
    }

    // ── Public Methods ─────────────────────────────────────────────────────

    public void FetchUserInfo()
    {
        user.GetUserInfoFromBackend();
    }
}
