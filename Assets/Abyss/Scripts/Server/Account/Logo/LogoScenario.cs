using UnityEngine;

public class LogoScenario : MonoBehaviour
{
    [SerializeField]
    private Progress progress; // 로딩바 스크립트

    private void Awake()
    {
        SystemSetup();
    }

    private void SystemSetup()
    {
        Application.runInBackground = true;

        int width = Screen.width;
        int height = (int)(Screen.width * 9f / 16f);
        Screen.SetResolution(width, height, true);

        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        progress.Play(OnAfterProgress);
    }

    private void OnAfterProgress()
    {
        AppBootstrapper.Instance.RequestLoad(Define.Scene.Login);
    }
}
