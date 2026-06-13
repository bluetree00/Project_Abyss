/// <summary>
/// IItemEffect에 전달되는 컨텍스트.
/// 현재 플레이어 상태, 무기 타입, HP 비율 등 조건 판정에 필요한 정보.
/// ItemEffectManager가 갱신하고, 각 효과가 읽기 전용으로 참조.
/// </summary>
public sealed class ItemEffectContext
{
    public PlayerController Player { get; private set; }
    public PlayerRuntimeStats Stats { get; private set; }
    public WeaponType WeaponType { get; private set; }
    public Define.CharacterClass CharacterClass { get; private set; }
    public float HpRatio { get; private set; }
    public bool HasShield { get; private set; }
    public GameRunSession Session { get; private set; }

    public void Update(PlayerController player, GameRunSession session)
    {
        Player = player;
        Stats = player?.RuntimeStats;
        Session = session;

        var weaponData = player?.WeaponManager?.CurrentWeaponData;
        WeaponType = weaponData?.weaponType ?? WeaponType.None;
        CharacterClass = player?.CharacterData?.conClass ?? Define.CharacterClass.Default;
        HasShield = Stats != null && Stats.HasShield;

        if (Stats != null && Stats.MaxHp > 0)
            HpRatio = (float)Stats.Hp / Stats.MaxHp;
        else
            HpRatio = 1f;
    }

    public void RefreshHpRatio()
    {
        if (Stats != null && Stats.MaxHp > 0)
            HpRatio = (float)Stats.Hp / Stats.MaxHp;
    }
}
