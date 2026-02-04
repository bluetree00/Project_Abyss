using UnityEngine;

public sealed class HudBootstrapper : MonoBehaviour
{
    [SerializeField] private HudPresenter presenter;

    private UIHudDataProvider _provider;
    private CharacterDataManager _character;

    public void Bind(CharacterDataManager characterDataManager)
    {
        _character = characterDataManager;

        if (presenter == null)
        {
            Debug.LogError("[HudBootstrapper] presenter is null.");
            return;
        }
        if (_character == null)
        {
            Debug.LogError("[HudBootstrapper] characterDataManager is null.");
            return;
        }

        _provider = new UIHudDataProvider(_character);
        presenter.Construct(_character, _provider);
    }
}
