
using BackEnd;
using UnityEngine;

public class LobbyScenario : MonoBehaviour
{
    [SerializeField]
    private UserInfo user;
    [SerializeField]
    private TopPanelViewer topPanel;

    private void Awake()
    {
        user.onUserInfoEvent.AddListener(topPanel.UpdateNickname);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!DevAutoLoginBootstrap.IsLoggedIn)
            return; // DevAutoLoginBootstrap이 로그인 완료 후 FetchUserInfo()를 호출
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
        if (!DevAutoLoginBootstrap.IsLoggedIn)
            return; // DevAutoLoginBootstrap이 로그인 완료 후 GameDataLoad를 호출
#endif
        BackendGameData.Instance.GameDataLoad();
    }
}
