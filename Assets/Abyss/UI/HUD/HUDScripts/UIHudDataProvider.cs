using UnityEngine;

public sealed class UIHudDataProvider
{
    private PlayerController _player;

    public void Bind(PlayerController player)
    {
        _player = player;
    }

    public void Unbind()
    {
        _player = null;
    }

    public bool TryGet(out UIHudData data)
    {
        data = default;

        if (_player == null || _player.RuntimeStats == null)
            return false;

        data = new UIHudData
        {
            AttackPower = _player.RuntimeStats.AttackPower,
            Hp = _player.RuntimeStats.Hp,
            MaxHp = _player.RuntimeStats.MaxHp
        };
        return true;
    }
}
