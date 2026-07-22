using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>Lobby 씬 BGM 시작. 씬에 배치하면 된다.</summary>
public sealed class LobbyBgmController : MonoBehaviour
{
    private const string BgmKey = "Lobby";

    private void Start()
    {
        Managers.Sound.PlayBgmAsync(BgmKey).Forget();
    }
}
