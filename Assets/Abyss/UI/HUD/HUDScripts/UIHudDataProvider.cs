
public sealed class UIHudDataProvider
{
    private PlayerRunState _state;

    public void Bind(PlayerRunState state)
    {
        _state = state;
    }

    public void Unbind()
    {
        _state = null;
    }

    public bool TryGet(out UIHudData data)
    {
        data = default;

        var s = _state;
        if (s == null || !s.IsActive)
            return false;

        data = new UIHudData
        {
            Hp = s.Hp,
            MaxHp = s.MaxHp,
            TempGold = s.TempGold
        };
        return true;
    }
}
