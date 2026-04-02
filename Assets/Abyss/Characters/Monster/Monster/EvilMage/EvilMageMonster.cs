namespace Abyss.Monster
{
/// <summary>
/// 악의 마법사. 원거리 마법탄 공격 (attackShape = MonsterRangedAttackSO).
/// </summary>
public class EvilMageMonster : MonsterBase
{
    public const string PrefabAddress = "EvilMage/EvilMage";
    protected override string ConfigAddress => "EvilMage/EvilMageConfig";
    protected override string DataAddress   => string.Empty;
}
}
