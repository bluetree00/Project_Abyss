using UnityEngine;

public class LogoScenario : MonoBehaviour
{
    [SerializeField]
    private Progress progress; // 로딩바 스크립트

    private void Start()
    {
        progress.Play(OnAfterProgress);
    }

    private void OnAfterProgress()
    {
        AppBootstrapper.Instance.RequestLoad(Define.Scene.Login);
    }
}
