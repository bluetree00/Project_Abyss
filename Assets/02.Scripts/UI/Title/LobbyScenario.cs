
using UnityEngine;

public class LobbyScenario : MonoBehaviour
{
    [SerializeField]
    private UserInfo user; // UserInfo 인스턴스

    private void Awake()
    {
        user.GetUserInfoFromBackend(); // 유저 정보 가져오기
    }
}
