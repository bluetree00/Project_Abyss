public readonly struct UILobbyData
{
    public readonly int CharacterIconId;
    public readonly int EquippedWeaponIconId;
    public readonly bool CanStartRun;

    public UILobbyData(int characterIconId, int equippedWeaponIconId, bool canStartRun)
    {
        CharacterIconId = characterIconId;
        EquippedWeaponIconId = equippedWeaponIconId;
        CanStartRun = canStartRun;
    }
}
