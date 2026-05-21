namespace RelicFairy.Monster
{
/// <summary>
/// 크랩 몬스터. 높은 방어력, 느린 이동의 탱커형 몬스터.
/// </summary>
public class CrabMonster : MonsterBase
{
    public const string PrefabAddress = "CrabMonster/CrabMonster";
    protected override string ConfigAddress => "CrabMonster/CrabMonsterConfig";
    protected override string DataAddress   => string.Empty;
}
}
