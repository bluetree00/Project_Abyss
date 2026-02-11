using UnityEngine;

public sealed class HudPresenter : MonoBehaviour
{
    [SerializeField] private HudView view;

    private PlayerRunState _state;
    private UIHudDataProvider _provider;

    public void Construct(GameRunManager run, UIHudDataProvider provider)
    {
        // ✅ 중복 구독/재바인딩 방어
        Dispose();

        _provider = provider;
        _state = run?.PlayerState;

        if (view == null)
        {
            Debug.LogError("[HudPresenter] view is null.");
            return;
        }

        if (_state == null || !_state.IsActive)
        {
            Debug.LogWarning("[HudPresenter] Construct ignored: PlayerState is null/inactive.");
            return;
        }

        // ✅ 초기 1회 반영 (스냅샷)
        if (_provider != null && _provider.TryGet(out var data))
        {
            view.SetHp(data.Hp, data.MaxHp);
            view.SetGold(data.TempGold);
        }
        else
        {
            // provider가 없어도 state 직접 반영 가능
            view.SetHp(_state.Hp, _state.MaxHp);
            view.SetGold(_state.TempGold);
        }

        // ✅ 이벤트 구독
        _state.OnHpChanged += HandleHpChanged;
        _state.OnGoldChanged += HandleGoldChanged;
    }

    public void Dispose()
    {
        if (_state != null)
        {
            _state.OnHpChanged -= HandleHpChanged;
            _state.OnGoldChanged -= HandleGoldChanged;
            _state = null;
        }
        _provider = null;
    }

    private void OnDisable() => Dispose();
    private void OnDestroy() => Dispose();

    private void HandleHpChanged(int hp, int maxHp) => view?.SetHp(hp, maxHp);
    private void HandleGoldChanged(int gold) => view?.SetGold(gold);
}
