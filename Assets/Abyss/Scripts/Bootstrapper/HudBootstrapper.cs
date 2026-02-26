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

        _provider ??= new UIHudDataProvider();
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
        if (ReferenceEquals(_run, run))
            return;

        Unbind();

        _run = run;

        // ✅ 1) HUD 모드 이벤트 구독 (Combat/Explore 전환을 여기서 받는다)
        _run.OnHudModeChanged += HandleHudModeChanged;

        // ✅ 2) 이미 현재 모드가 정해져 있으면 즉시 반영(늦게 뜬 HUD도 동기화)
        if (_run.TryGetHudMode(out var mode))
            presenter.SetMode(mode);

        // ✅ 3) PlayerState가 준비되었으면 즉시 Construct
        if (_run.TryGetPlayerState(out var st) && st != null)
        {
            BindStateNow(st);
            return;
        }

        // ✅ 4) 아직이면 준비 이벤트를 기다린다 (HUD가 먼저 로드되어도 안전)
        _run.OnPlayerStateReady += BindStateNow;
    }

    private void BindStateNow(PlayerRunState st)
    {
        if (_run == null || st == null) return;

        // ✅ 한번만
        _run.OnPlayerStateReady -= BindStateNow;

        _provider.Bind(st);
        presenter.Construct(_run, _provider);
    }

    private void HandleHudModeChanged(HUDIds.Mode mode)
    {
        // 여기서 CombatPanel 토글이 일어남
        presenter.SetMode(mode);
    }

    public void Unbind()
    {
        if (_run != null)
        {
            _run.OnPlayerStateReady -= BindStateNow;
            _run.OnHudModeChanged -= HandleHudModeChanged;
        }

        presenter?.Dispose();
        _provider?.Unbind();
        _run = null;
    }

    private void OnDestroy() => Unbind();
}
