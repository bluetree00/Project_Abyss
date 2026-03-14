using UnityEngine;

public sealed class HudBootstrapper : MonoBehaviour
{
    [SerializeField] private HudPresenter presenter;

    private UIHudDataProvider _provider;
    private GameRunSession _run;

    // ✅ Construct 중복 방지
    private bool _constructed;

    private void Awake()
    {
        if (presenter == null)
            presenter = GetComponentInChildren<HudPresenter>(true);

        if (presenter == null)
            Debug.LogError("[HudBootstrapper] presenter is null.");

        _provider ??= new UIHudDataProvider();
    }

    public void BindRun(GameRunSession run)
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

        Unbind(); // ✅ 항상 깨끗하게 정리하고 바인딩

        _run = run;

        // ✅ 1) HUD 모드 이벤트 구독
        _run.OnHudModeChanged += HandleHudModeChanged;

        // ✅ 2) 현재 모드 즉시 반영(늦게 뜬 HUD도 동기화)
        if (_run.TryGetHudMode(out var mode))
            presenter.SetMode(mode);

        // ✅ 3) PlayerState가 준비되었으면 즉시 Construct
        if (_run.TryGetPlayerState(out var st) && st != null)
        {
            BindStateNow(st);
        }
        else
        {
            // ✅ 4) 아직이면 준비 이벤트 대기
            _run.OnPlayerStateReady += BindStateNow;
        }

        // ✅ 5) 플레이어 스폰 이벤트 구독 (스탯·장비 HUD 연결)
        _run.OnPlayerBound += HandlePlayerBound;

        // 이미 스폰된 경우 즉시 반영
        if (_run.Player != null)
            presenter.BindPlayer(_run.Player);
    }

    private void BindStateNow(PlayerRunState st)
    {
        if (_constructed) return;
        if (_run == null || st == null) return;

        _constructed = true;

        // ✅ 한번만
        _run.OnPlayerStateReady -= BindStateNow;

        _provider.Bind(st);
        presenter.Construct(_run, _provider);
    }

    private void HandleHudModeChanged(HUDIds.Mode mode)
    {
        presenter.SetMode(mode);
    }

    private void HandlePlayerBound(PlayerController player)
    {
        presenter.BindPlayer(player);
    }

    public void Unbind()
    {
        if (_run != null)
        {
            _run.OnPlayerStateReady -= BindStateNow;
            _run.OnHudModeChanged   -= HandleHudModeChanged;
            _run.OnPlayerBound      -= HandlePlayerBound;
        }

        _constructed = false;

        presenter?.Dispose();
        _provider?.Unbind();
        _run = null;
    }

    private void OnDestroy() => Unbind();
}