using UnityEngine;

public sealed class HudBootstrapper : MonoBehaviour
{
    [SerializeField] private HudPresenter presenter;

    private UIHudDataProvider _provider;
    private GameRunManager _run;

    private void Awake()
    {
        if (presenter == null)
            presenter = GetComponentInChildren<HudPresenter>(true);

        if (presenter == null)
            Debug.LogError("[HudBootstrapper] presenter is null.");

        _provider = new UIHudDataProvider();
    }

    public void BindRun(GameRunManager run)
    {
        if (presenter == null)
        {
            Debug.LogError("[HudBootstrapper] BindRun failed: presenter is null.");
            return;
        }
        if (run == null)
        {
            Debug.LogError("[HudBootstrapper] BindRun failed: run is null.");
            return;
        }

        _run = run;
        presenter.Construct(_run, _provider);
    }
}
