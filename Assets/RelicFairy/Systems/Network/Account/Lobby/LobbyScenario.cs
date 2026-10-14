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

        // 에디터 · 빌드가 같은 길을 탄다 — 예전 판정(#if 안의 개발용 로그인 플래그)은 에디터에서 늘 거짓이라
        // 아래 두 호출이 빌드에서만 돌았다(10-09 출시 점검: 에디터에서 한 번도 확인되지 않은 경로).
        if (!AppBootstrapper.IsAutoLoggedIn && !SteamLoginService.IsLoggedIn)
            return;
        user.GetUserInfoFromBackend();
    }

    private async void Start()
    {
        // 메타는 부팅 자동 로그인에서 이미 읽었다 — 로비에 돌아올 때마다 서버에서 다시 읽지 않는다.
        // Steam 로그인 경로는 부팅에서 메타를 읽지 않으므로 여기서 한 번 읽는다.
        if (AppBootstrapper.IsAutoLoggedIn || !SteamLoginService.IsLoggedIn)
            return;
        await BackendGameData.Instance.LoadAsync();
    }

    // ── Public Methods ─────────────────────────────────────────────────────

    public void FetchUserInfo()
    {
        user.GetUserInfoFromBackend();
    }
}
