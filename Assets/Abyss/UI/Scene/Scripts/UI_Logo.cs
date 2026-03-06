using UnityEngine;

public class UI_Logo : UI_Scene
{
    [SerializeField] private Progress progress;

    public override void Init()
    {
        base.Init();
        progress.Play(OnProgressComplete);
    }

    private void OnProgressComplete()
    {
        AppBootstrapper.Instance.RequestLoad(Define.Scene.Login);
    }
}
