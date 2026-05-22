namespace RelicFairy.Monster
{
/// <summary>
/// 피쉬맨. 원거리 작살 투척 (attackShape = MonsterRangedAttackSO).
/// </summary>
public class FishmanMonster : MonsterBase
{
    public const string PrefabAddress = "Fishman/Fishman";
    protected override string ConfigAddress => "Fishman/FishmanConfig";
    protected override string DataAddress   => string.Empty;
}
}
