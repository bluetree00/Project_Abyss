
using BackEnd;
using UnityEngine;

public class LobbyScenario : MonoBehaviour
{
    [SerializeField]
    private UserInfo user;

    private void Awake()
    {
        var hud = FindAnyObjectByType<HudPresenter>(FindObjectsInactive.Include);
        if (hud != null) hud.BindLobby(user);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!DevAutoLoginBootstrap.IsLoggedIn && !SteamLoginService.IsLoggedIn)
            return; // 로그인 완료 후 FetchUserInfo() 호출
#endif
        user.GetUserInfoFromBackend();
    }

    public void FetchUserInfo()
    {
        user.GetUserInfoFromBackend();
    }

    private void Start()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!DevAutoLoginBootstrap.IsLoggedIn && !SteamLoginService.IsLoggedIn)
            return; // 로그인 완료 후 GameDataLoad 호출
#endif
        BackendGameData.Instance.GameDataLoad();
    }
}
