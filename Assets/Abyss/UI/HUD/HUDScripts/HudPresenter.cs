using UnityEngine;

public sealed class HudPresenter : MonoBehaviour
{
    [SerializeField] private HudView view;

    private CharacterDataManager _character;
    private UIHudDataProvider _provider;

    public void Construct(CharacterDataManager character, UIHudDataProvider provider)
    {
        _character = character;
        _provider = provider;

        // 초기 표시
        Refresh();

        // 데이터 변경되면 HUD 갱신
        _character.OnChanged += Refresh;
    }

    private void OnDestroy()
    {
        if (_character != null)
            _character.OnChanged -= Refresh;
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
