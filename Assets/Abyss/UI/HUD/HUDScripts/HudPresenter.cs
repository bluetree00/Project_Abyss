using UnityEngine;

public sealed class HudPresenter : MonoBehaviour
{
    [SerializeField] private HudView view;

    private GameRunManager _run;
    private UIHudDataProvider _provider;

    private PlayerController _player;

    public void Construct(GameRunManager run, UIHudDataProvider provider)
    {
        _run = run;
        _provider = provider;

        if (_run == null || _provider == null)
        {
            Debug.LogError("[HudPresenter] Construct failed: run/provider is null.");
            return;
        }

        _run.OnRunStarted -= OnRunStarted;
        _run.OnRunEnded -= OnRunEnded;
        _run.OnRunStarted += OnRunStarted;
        _run.OnRunEnded += OnRunEnded;

        // 이미 런 중이면 즉시 반영
        if (_run.IsRunning) OnRunStarted();
        else Refresh();
    }

    private void OnDestroy()
    {
        UnbindPlayer();

        if (_run != null)
        {
            _run.OnRunStarted -= OnRunStarted;
            _run.OnRunEnded -= OnRunEnded;
        }
    }

    private void OnRunStarted()
    {
        BindPlayer(_run.Player);   // 이 시점에 Player가 null일 수 있음(타이밍 이슈)
        Refresh();
    }

    private void OnRunEnded(EndRunResult _)
    {
        UnbindPlayer();
        Refresh();
    }

    private void BindPlayer(PlayerController player)
    {
        if (_player == player) return;

        UnbindPlayer();
        _player = player;

        if (_player == null)
        {
            _provider.Unbind();
            return;
        }

        // ✅ 너가 PlayerController에 만들어 둔 래핑 이벤트
        _player.OnHudStatChanged += Refresh;

        _provider.Bind(_player);
    }

    private void UnbindPlayer()
    {
        if (_player != null)
        {
            _player.OnHudStatChanged -= Refresh;
        }

        _player = null;

        _provider?.Unbind();
    }

    private void Refresh()
    {
        if (view == null || _provider == null)
            return;

        if (_provider.TryGet(out var data))
            view.Render(data);
        else
            view.Clear();
    }
}
