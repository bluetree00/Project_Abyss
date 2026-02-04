using UnityEngine;

public sealed class UIHudDataProvider
{
    private readonly CharacterDataManager _character;

    public UIHudDataProvider(CharacterDataManager character)
    {
        _character = character;
    }

    /// <summary>
    /// 캐릭터 데이터가 없을 수 있으므로 TryGet 권장
    /// </summary>
    public bool TryGet(out UIHudData data)
    {
        var c = _character.M_CharacterData;
        if (c == null)
        {
            data = default;
            return false;
        }

        data = new UIHudData(c.attackPower);
        return true;
    }

    /// <summary>
    /// 무조건 반환이 필요할 때(기본값 포함)
    /// </summary>
    public UIHudData GetOrDefault()
    {
        return TryGet(out var data) ? data : default;
    }
}
